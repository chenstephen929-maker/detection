using AirGroundControlApp.Models;

namespace AirGroundControlApp.Services;

public interface ILogSink
{
    void AddLog(string source, string category, LogLevel level, string message);
}
