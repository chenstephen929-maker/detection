namespace AirGroundControlApp.Models;

public sealed class LogEntry
{
    public LogEntry(DateTime timestamp, string source, string category, LogLevel level, string message)
    {
        Timestamp = timestamp;
        Source = source;
        Category = category;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }
    public string Source { get; }
    public string Category { get; }
    public LogLevel Level { get; }
    public string Message { get; }

    public string TimestampText => Timestamp.ToString("HH:mm:ss");
    public string LevelText => levelText(Level);

    private static string levelText(LogLevel level) => level switch
    {
        LogLevel.Warning => "警告",
        LogLevel.Error => "错误",
        LogLevel.Success => "成功",
        _ => "信息"
    };
}
