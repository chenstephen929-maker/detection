using System.Text;
using AirGroundControlApp.Models;
using AirGroundControlApp.Utilities;
using Renci.SshNet;

namespace AirGroundControlApp.Services;

public sealed class SshSessionManager : IDisposable
{
    private const string OneShotExitMarker = "__AGC_ONESHOT_EXIT:";
    private readonly Dictionary<DeviceKind, SshClient> _clients = new();
    private readonly object _lock = new();

    public bool IsConnected(DeviceKind kind)
    {
        lock (_lock)
        {
            return _clients.TryGetValue(kind, out var client) && client.IsConnected;
        }
    }

    public Task ConnectAsync(DeviceConfig config, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config.Kind is DeviceKind.Local)
            {
                return;
            }

            lock (_lock)
            {
                if (_clients.TryGetValue(config.Kind, out var oldClient))
                {
                    oldClient.Dispose();
                    _clients.Remove(config.Kind);
                }
            }

            var connectionInfo = new PasswordConnectionInfo(
                config.Host,
                config.Port,
                config.Username,
                config.Password)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            var client = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(20)
            };

            client.Connect();

            lock (_lock)
            {
                _clients[config.Kind] = client;
            }
        }, cancellationToken);
    }

    public void Disconnect(DeviceKind kind)
    {
        lock (_lock)
        {
            if (_clients.Remove(kind, out var client))
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }

                client.Dispose();
            }
        }
    }

    public ShellStream CreateShellStream(DeviceKind kind)
    {
        var client = GetConnectedClient(kind);
        return client.CreateShellStream("xterm", 120, 40, 1024, 768, 4096);
    }

    public Task<SshCommandResult> ExecuteCommandAsync(
        DeviceKind kind,
        string commandText,
        bool requiresSudo,
        string sudoPassword,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = GetConnectedClient(kind);
            if (requiresSudo)
            {
                return ExecuteSudoCommand(client, commandText, sudoPassword, timeout, cancellationToken);
            }

            using var command = client.CreateCommand(commandText);
            var asyncResult = command.BeginExecute();

            var completed = asyncResult.AsyncWaitHandle.WaitOne(timeout);
            if (!completed)
            {
                return new SshCommandResult
                {
                    ExitStatus = -1,
                    Output = command.Result ?? "",
                    Error = "命令执行超时",
                    TimedOut = true
                };
            }

            command.EndExecute(asyncResult);
            return new SshCommandResult
            {
                ExitStatus = command.ExitStatus ?? -1,
                Output = command.Result ?? "",
                Error = command.Error ?? ""
            };
        }, cancellationToken);
    }

    private static SshCommandResult ExecuteSudoCommand(
        SshClient client,
        string commandText,
        string sudoPassword,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var stream = client.CreateShellStream("xterm", 120, 40, 1024, 768, 4096);
        var output = new StringBuilder();
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[4096];
        var command = $"sudo -S -p '' sh -lc {ShellEscaper.SingleQuote(commandText)}; echo {OneShotExitMarker}$?";

        stream.WriteLine(command);
        Thread.Sleep(150);
        stream.WriteLine(sudoPassword);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stream.DataAvailable)
            {
                Thread.Sleep(50);
                continue;
            }

            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                continue;
            }

            output.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var text = output.ToString();
            var markerIndex = text.LastIndexOf(OneShotExitMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var exitText = text[(markerIndex + OneShotExitMarker.Length)..]
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "-1";
                var exitStatus = int.TryParse(exitText.Trim(), out var parsedExitStatus) ? parsedExitStatus : -1;
                var cleanOutput = CleanShellOutput(text, command, sudoPassword);

                return new SshCommandResult
                {
                    ExitStatus = exitStatus,
                    Output = cleanOutput,
                    Error = ""
                };
            }
        }

        return new SshCommandResult
        {
            ExitStatus = -1,
            Output = CleanShellOutput(output.ToString(), command, sudoPassword),
            Error = "命令执行超时",
            TimedOut = true
        };
    }

    private static string CleanShellOutput(string text, string command, string sudoPassword)
    {
        var sanitizedText = string.IsNullOrEmpty(sudoPassword)
            ? text
            : text.Replace(sudoPassword, "[sudo password]", StringComparison.Ordinal);

        var lines = sanitizedText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line =>
                !string.IsNullOrWhiteSpace(line) &&
                !line.Contains(command, StringComparison.Ordinal) &&
                !line.Contains(OneShotExitMarker, StringComparison.Ordinal) &&
                !line.Contains("[sudo password]", StringComparison.Ordinal))
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    private SshClient GetConnectedClient(DeviceKind kind)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(kind, out var client) || !client.IsConnected)
            {
                throw new InvalidOperationException($"{kind} SSH 未连接");
            }

            return client;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var client in _clients.Values)
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }

                client.Dispose();
            }

            _clients.Clear();
        }
    }
}
