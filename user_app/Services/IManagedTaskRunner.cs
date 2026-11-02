using AirGroundControlApp.ViewModels;

namespace AirGroundControlApp.Services;

public interface IManagedTaskRunner
{
    Task StartAsync(ManagedTaskViewModel task, CancellationToken cancellationToken);
    Task StopAsync(ManagedTaskViewModel task);
}
