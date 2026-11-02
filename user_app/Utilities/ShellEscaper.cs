namespace AirGroundControlApp.Utilities;

public static class ShellEscaper
{
    public static string SingleQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
