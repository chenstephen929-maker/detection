namespace AirGroundControlApp.Models;

public sealed class RemoteCommandStep
{
    public RemoteCommandStep(string commandTemplate, bool requiresSudo = false, string description = "")
    {
        CommandTemplate = commandTemplate;
        RequiresSudo = requiresSudo;
        Description = description;
    }

    public string CommandTemplate { get; }
    public bool RequiresSudo { get; }
    public string Description { get; }
}
