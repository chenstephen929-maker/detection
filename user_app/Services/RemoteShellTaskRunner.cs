using System.Text;
using AirGroundControlApp.Models;
using AirGroundControlApp.Utilities;
using AirGroundControlApp.ViewModels;
using Renci.SshNet;

namespace AirGroundControlApp.Services;

public sealed class RemoteShellTaskRunner : IManagedTaskRunner, IDisposable
{
    private const string ExitMarker = "__AGC_TASK_EXIT:";
    private readonly SshSessionManager _sshSessionManager;
    private readonly Func<DeviceKind, DeviceConfig> _deviceConfigProvider;
    private readonly Func<RuntimeConfig> _runtimeConfigProvider;
    private readonly ILogSink _logSink;
    private readonly Dictionary<string, ShellStream> _shells = new();
    private readonly Dictionary<string, CancellationTokenSource> _readTokens = new();
    private readonly object _lock = new();

    public RemoteShellTaskRunner(
        SshSessionManager sshSessionManager,
        Func<DeviceKind, DeviceConfig> deviceConfigProvider,
        Func<RuntimeConfig> runtimeConfigProvider,
        ILogSink logSink)
    {
        _sshSessionManager = sshSessionManager;
        _deviceConfigProvider = deviceConfigProvider;
        _runtimeConfigProvider = runtimeConfigProvider;
        _logSink = logSink;
    }

    public async Task StartAsync(ManagedTaskViewModel task, CancellationToken cancellationToken)
    {
        var definition = task.Definition;
        var config = _runtimeConfigProvider();
        var deviceConfig = _deviceConfigProvider(definition.TargetDevice);

        if (!_sshSessionManager.IsConnected(definition.TargetDevice))
        {
            task.SetState(TaskRunState.Failed, $"{definition.TargetDevice} SSH 未连接");
            return;
        }

        if (!string.IsNullOrWhiteSpace(definition.ExistingCheckCommandTemplate))
        {
            var checkCommand = CommandTemplateRenderer.Render(definition.ExistingCheckCommandTemplate, config);
            var checkResult = await _sshSessionManager.ExecuteCommandAsync(
                definition.TargetDevice,
                checkCommand,
                definition.ExistingCheckRequiresSudo,
                deviceConfig.SudoPassword,
                TimeSpan.FromSeconds(8),
                cancellationToken);

            var existingOutput = (checkResult.Output + checkResult.Error).Trim();
            if (!checkResult.Success)
            {
                task.SetState(TaskRunState.Failed, "远端存在性检查失败");
                if (!string.IsNullOrWhiteSpace(existingOutput))
                {
                    _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Error, existingOutput);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(existingOutput))
            {
                task.SetState(TaskRunState.RemoteAlreadyRunning, "远端已存在匹配进程");
                _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Warning, existingOutput);
                return;
            }
        }

        task.SetState(TaskRunState.Starting, "");
        _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Info, "正在执行启动前清理");

        foreach (var step in definition.PreStartSteps)
        {
            var renderedStep = CommandTemplateRenderer.Render(step.CommandTemplate, config);
            var result = await _sshSessionManager.ExecuteCommandAsync(
                definition.TargetDevice,
                renderedStep,
                step.RequiresSudo,
                deviceConfig.SudoPassword,
                TimeSpan.FromSeconds(12),
                cancellationToken);

            LogCommandResult(task, step.Description, result);
            if (!result.Success)
            {
                task.SetState(TaskRunState.Failed, result.TimedOut ? $"{step.Description} 超时" : $"{step.Description} 失败");
                return;
            }
        }

        var startCommand = CommandTemplateRenderer.Render(definition.StartCommandTemplate, config);
        var stream = _sshSessionManager.CreateShellStream(definition.TargetDevice);
        var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_lock)
        {
            DisposeTaskShell(task.Id);
            _shells[task.Id] = stream;
            _readTokens[task.Id] = readCts;
        }

        _ = Task.Run(() => ReadShellLoop(task, stream, readCts.Token), CancellationToken.None);

        var commandLine = BuildInteractiveCommand(startCommand, definition.StartRequiresSudo);
        stream.WriteLine("export TERM=xterm");
        stream.WriteLine(commandLine);

        if (definition.StartRequiresSudo)
        {
            await Task.Delay(250, cancellationToken);
            stream.WriteLine(deviceConfig.SudoPassword);
        }

        task.SetState(TaskRunState.Running, "");
        _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Success, "远程任务已启动");
    }

    public async Task StopAsync(ManagedTaskViewModel task)
    {
        var definition = task.Definition;
        var config = _runtimeConfigProvider();
        var deviceConfig = _deviceConfigProvider(definition.TargetDevice);

        task.SetState(TaskRunState.Stopping, "");

        lock (_lock)
        {
            DisposeTaskShell(task.Id);
        }

        if (!string.IsNullOrWhiteSpace(definition.StopCommandTemplate) &&
            _sshSessionManager.IsConnected(definition.TargetDevice))
        {
            var stopCommand = CommandTemplateRenderer.Render(definition.StopCommandTemplate, config);
            var result = await _sshSessionManager.ExecuteCommandAsync(
                definition.TargetDevice,
                stopCommand,
                definition.StopRequiresSudo,
                deviceConfig.SudoPassword,
                TimeSpan.FromSeconds(15));

            LogCommandResult(task, "停止命令", result);
            if (!result.Success)
            {
                task.SetState(TaskRunState.Failed, "停止命令执行失败");
                return;
            }
        }

        task.SetState(TaskRunState.Stopped, "");
        _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Success, "远程任务已停止");
    }

    private static string BuildInteractiveCommand(string command, bool requiresSudo)
    {
        var wrapped = $"sh -lc {ShellEscaper.SingleQuote(command)}; echo {ExitMarker}$?";
        return requiresSudo
            ? $"sudo -S -p '' {wrapped}"
            : wrapped;
    }

    private void ReadShellLoop(ManagedTaskViewModel task, ShellStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var partial = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && stream.CanRead)
        {
            try
            {
                if (!stream.DataAvailable)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    continue;
                }

                partial.Append(Encoding.UTF8.GetString(buffer, 0, read));
                FlushLines(task, partial);
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    task.SetState(TaskRunState.Failed, ex.Message);
                    _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Error, $"远程日志读取失败: {ex.Message}");
                }

                break;
            }
        }
    }

    private void FlushLines(ManagedTaskViewModel task, StringBuilder partial)
    {
        var text = partial.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = text.Split('\n');
        partial.Clear();

        for (var i = 0; i < lines.Length; i++)
        {
            var isLast = i == lines.Length - 1;
            if (isLast && !text.EndsWith('\n'))
            {
                partial.Append(lines[i]);
                continue;
            }

            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var markerIndex = line.IndexOf(ExitMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var exitText = line[(markerIndex + ExitMarker.Length)..].Trim();
                if (int.TryParse(exitText, out var exitCode) && task.State == TaskRunState.Running)
                {
                    task.SetState(TaskRunState.Failed, $"远程命令已退出，ExitCode={exitCode}");
                    _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Error, $"远程命令已退出，ExitCode={exitCode}");
                }

                continue;
            }

            _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Info, line);
        }
    }

    private void LogCommandResult(ManagedTaskViewModel task, string description, SshCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Info, $"{description}: {result.Output.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            _logSink.AddLog(task.DisplayName, task.Category, result.Success ? LogLevel.Warning : LogLevel.Error, $"{description}: {result.Error.Trim()}");
        }
    }

    private void DisposeTaskShell(string taskId)
    {
        if (_readTokens.Remove(taskId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_shells.Remove(taskId, out var shell))
        {
            shell.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var taskId in _shells.Keys.ToArray())
            {
                DisposeTaskShell(taskId);
            }
        }
    }
}
