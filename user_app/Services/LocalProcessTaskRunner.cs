using System.Diagnostics;
using System.IO;
using AirGroundControlApp.Models;
using AirGroundControlApp.Utilities;
using AirGroundControlApp.ViewModels;

namespace AirGroundControlApp.Services;

public sealed class LocalProcessTaskRunner : IManagedTaskRunner, IDisposable
{
    private readonly Func<RuntimeConfig> _configProvider;
    private readonly ILogSink _logSink;
    private Process? _process;
    private bool _stopping;

    public LocalProcessTaskRunner(Func<RuntimeConfig> configProvider, ILogSink logSink)
    {
        _configProvider = configProvider;
        _logSink = logSink;
    }

    public async Task StartAsync(ManagedTaskViewModel task, CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            task.SetState(TaskRunState.RemoteAlreadyRunning, "本地截图服务已经在运行");
            return;
        }

        var config = _configProvider();
        if (!File.Exists(config.PythonPath))
        {
            task.SetState(TaskRunState.Failed, $"Python 解释器不存在: {config.PythonPath}");
            return;
        }

        if (!File.Exists(config.ScreenshotScriptPath))
        {
            task.SetState(TaskRunState.Failed, $"截图脚本不存在: {config.ScreenshotScriptPath}");
            return;
        }

        if (task.Definition.LocalPort > 0 && !PortProbe.IsTcpPortAvailable(task.Definition.LocalPort))
        {
            task.SetState(TaskRunState.RemoteAlreadyRunning, $"端口 {task.Definition.LocalPort} 已被占用，截图服务可能已经运行");
            return;
        }

        _stopping = false;
        task.SetState(TaskRunState.Starting, "");
        _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Info, "正在启动本地截图服务");

        var startInfo = new ProcessStartInfo
        {
            FileName = config.PythonPath,
            Arguments = $"\"{config.ScreenshotScriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(config.ScreenshotScriptPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => LogLine(task, LogLevel.Info, args.Data);
        process.ErrorDataReceived += (_, args) => LogLine(task, LogLevel.Error, args.Data);
        process.Exited += (_, _) =>
        {
            if (_stopping)
            {
                return;
            }

            task.SetState(TaskRunState.Failed, $"本地进程已退出，ExitCode={process.ExitCode}");
            _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Error, $"截图服务异常退出，ExitCode={process.ExitCode}");
        };

        if (!process.Start())
        {
            task.SetState(TaskRunState.Failed, "本地进程启动失败");
            return;
        }

        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        task.SetState(TaskRunState.Running, "");
        _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Success, "本地截图服务已启动");

        await Task.CompletedTask;
    }

    public Task StopAsync(ManagedTaskViewModel task)
    {
        _stopping = true;
        task.SetState(TaskRunState.Stopping, "");

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }

            _process?.Dispose();
            _process = null;
            task.SetState(TaskRunState.Stopped, "");
            _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Success, "本地截图服务已停止");
        }
        catch (Exception ex)
        {
            task.SetState(TaskRunState.Failed, ex.Message);
            _logSink.AddLog(task.DisplayName, task.Category, LogLevel.Error, $"停止本地截图服务失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void LogLine(ManagedTaskViewModel task, LogLevel level, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _logSink.AddLog(task.DisplayName, task.Category, level, line);
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Application shutdown path.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }
}
