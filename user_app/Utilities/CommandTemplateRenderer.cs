using AirGroundControlApp.Models;

namespace AirGroundControlApp.Utilities;

public static class CommandTemplateRenderer
{
    public static string Render(string template, RuntimeConfig config)
    {
        return template
            .Replace("{WindowsIp}", config.WindowsIp, StringComparison.Ordinal)
            .Replace("{CarIp}", config.CarIp, StringComparison.Ordinal);
    }
}
