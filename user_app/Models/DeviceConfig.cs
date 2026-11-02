namespace AirGroundControlApp.Models;

public sealed class DeviceConfig
{
    public DeviceKind Kind { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string SudoPassword { get; init; } = "";
}
