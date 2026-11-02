namespace AirGroundControlApp.Models;

public sealed class ManagedTaskDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public DeviceKind TargetDevice { get; init; }
    public CommandRunnerKind RunnerKind { get; init; }
    public string StartCommandTemplate { get; init; } = "";
    public bool StartRequiresSudo { get; init; }
    public string StopCommandTemplate { get; init; } = "";
    public bool StopRequiresSudo { get; init; }
    public string ExistingCheckCommandTemplate { get; init; } = "";
    public bool ExistingCheckRequiresSudo { get; init; }
    public int LocalPort { get; init; }
    public IReadOnlyList<RemoteCommandStep> PreStartSteps { get; init; } = Array.Empty<RemoteCommandStep>();
}
