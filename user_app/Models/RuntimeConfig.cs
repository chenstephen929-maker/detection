namespace AirGroundControlApp.Models;

public sealed class RuntimeConfig
{
    public string PythonPath { get; init; } = "";
    public string ScreenshotScriptPath { get; init; } = "";
    public string WindowsIp { get; init; } = "";
    public string CarIp { get; init; } = "";
}
