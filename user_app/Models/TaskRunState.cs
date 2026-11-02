namespace AirGroundControlApp.Models;

public enum TaskRunState
{
    Idle,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed,
    RemoteAlreadyRunning
}
