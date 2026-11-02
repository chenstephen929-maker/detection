using System.Windows.Threading;
using AirGroundControlApp.Models;
using AirGroundControlApp.Services;
using AirGroundControlApp.Utilities;

namespace AirGroundControlApp.ViewModels;

public sealed class ManagedTaskViewModel : ObservableObject
{
    private readonly IManagedTaskRunner _runner;
    private readonly DispatcherTimer _timer;
    private TaskRunState _state = TaskRunState.Idle;
    private string _lastError = "";
    private DateTime? _startedAt;
    private string _runtimeText = "--";

    public ManagedTaskViewModel(ManagedTaskDefinition definition, IManagedTaskRunner runner)
    {
        Definition = definition;
        _runner = runner;

        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, CanRestart);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateRuntimeText();
    }

    public ManagedTaskDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Category => Definition.Category;
    public string TargetText => Definition.TargetDevice switch
    {
        DeviceKind.Drone => "无人机",
        DeviceKind.Car => "无人车",
        _ => "本地"
    };

    public TaskRunState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsBusy));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateText => State switch
    {
        TaskRunState.Starting => "启动中",
        TaskRunState.Running => "运行中",
        TaskRunState.Stopping => "停止中",
        TaskRunState.Stopped => "已停止",
        TaskRunState.Failed => "异常",
        TaskRunState.RemoteAlreadyRunning => "已存在",
        _ => "未启动"
    };

    public bool IsBusy => State is TaskRunState.Starting or TaskRunState.Stopping;

    public string LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public string RuntimeText
    {
        get => _runtimeText;
        private set => SetProperty(ref _runtimeText, value);
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }

    public void SetState(TaskRunState state, string lastError)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            State = state;
            LastError = lastError;

            if (state == TaskRunState.Running)
            {
                _startedAt ??= DateTime.Now;
                _timer.Start();
            }
            else if (state is TaskRunState.Idle or TaskRunState.Stopped or TaskRunState.Failed)
            {
                _timer.Stop();
                _startedAt = null;
                RuntimeText = "--";
            }
        });
    }

    private async Task StartAsync()
    {
        await _runner.StartAsync(this, CancellationToken.None);
    }

    private async Task StopAsync()
    {
        await _runner.StopAsync(this);
    }

    private async Task RestartAsync()
    {
        if (CanStop())
        {
            await StopAsync();
            await Task.Delay(300);
        }

        await StartAsync();
    }

    private bool CanStart()
    {
        return State is TaskRunState.Idle or TaskRunState.Stopped or TaskRunState.Failed;
    }

    private bool CanStop()
    {
        return State is TaskRunState.Running or TaskRunState.RemoteAlreadyRunning or TaskRunState.Failed;
    }

    private bool CanRestart()
    {
        return State is TaskRunState.Running or TaskRunState.RemoteAlreadyRunning or TaskRunState.Failed or TaskRunState.Stopped;
    }

    private void UpdateRuntimeText()
    {
        if (_startedAt is null)
        {
            RuntimeText = "--";
            return;
        }

        var runtime = DateTime.Now - _startedAt.Value;
        RuntimeText = runtime.ToString(@"hh\:mm\:ss");
    }
}
