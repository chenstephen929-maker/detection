namespace AirGroundControlApp.Services;

public sealed class SshCommandResult
{
    public int ExitStatus { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
    public bool TimedOut { get; init; }

    public bool Success => !TimedOut && ExitStatus == 0;
}
