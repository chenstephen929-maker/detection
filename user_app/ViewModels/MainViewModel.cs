using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AirGroundControlApp.Models;
using AirGroundControlApp.Services;
using AirGroundControlApp.Utilities;

namespace AirGroundControlApp.ViewModels;

public sealed class MainViewModel : ObservableObject, ILogSink, IDisposable
{
    private readonly SshSessionManager _sshSessionManager = new();
    private readonly LocalProcessTaskRunner _localRunner;
    private readonly RemoteShellTaskRunner _remoteRunner;
    private string _pythonPath = @"D:\anaconda2024\envs\pytorch\python.exe";
    private string _screenshotScriptPath = @"E:\ultralytics-main\ultralytics-main\windows_GetScreenshot.py";
    private string _windowsIp = "100.68.90.47";
    private string _carIp = "100.69.99.51";
    private string _selectedLogFilter = "全部";

    public MainViewModel()
    {
        Drone = new DeviceConnectionViewModel(DeviceKind.Drone, "无人机", _sshSessionManager, this);
        Car = new DeviceConnectionViewModel(DeviceKind.Car, "无人车", _sshSessionManager, this);

        _localRunner = new LocalProcessTaskRunner(GetRuntimeConfig, this);
        _remoteRunner = new RemoteShellTaskRunner(_sshSessionManager, GetDeviceConfig, GetRuntimeConfig, this);

        foreach (var definition in BuildTaskDefinitions())
        {
            IManagedTaskRunner runner = definition.RunnerKind == CommandRunnerKind.LocalProcess
                ? _localRunner
                : _remoteRunner;
            Tasks.Add(new ManagedTaskViewModel(definition, runner));
        }

        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.Filter = FilterLog;

        StartAllCommand = new AsyncRelayCommand(StartAllAsync);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync);
        ClearLogsCommand = new RelayCommand(Logs.Clear);
    }

    public DeviceConnectionViewModel Drone { get; }
    public DeviceConnectionViewModel Car { get; }
    public ObservableCollection<ManagedTaskViewModel> Tasks { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();
    public ICollectionView LogsView { get; }

    public IReadOnlyList<string> LogFilters { get; } = new[]
    {
        "全部",
        "本地",
        "无人机",
        "无人车",
        "视频",
        "算法",
        "SSH"
    };

    public string PythonPath
    {
        get => _pythonPath;
        set => SetProperty(ref _pythonPath, value);
    }

    public string ScreenshotScriptPath
    {
        get => _screenshotScriptPath;
        set => SetProperty(ref _screenshotScriptPath, value);
    }

    public string WindowsIp
    {
        get => _windowsIp;
        set => SetProperty(ref _windowsIp, value);
    }

    public string CarIp
    {
        get => _carIp;
        set => SetProperty(ref _carIp, value);
    }

    public string SelectedLogFilter
    {
        get => _selectedLogFilter;
        set
        {
            if (SetProperty(ref _selectedLogFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public AsyncRelayCommand StartAllCommand { get; }
    public AsyncRelayCommand StopAllCommand { get; }
    public RelayCommand ClearLogsCommand { get; }

    public void AddLog(string source, string category, LogLevel level, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry(DateTime.Now, source, category, level, message));
            if (Logs.Count > 1200)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    public DeviceConfig GetDeviceConfig(DeviceKind kind)
    {
        return kind switch
        {
            DeviceKind.Drone => Drone.ToConfig(),
            DeviceKind.Car => Car.ToConfig(),
            _ => new DeviceConfig { Kind = DeviceKind.Local }
        };
    }

    public RuntimeConfig GetRuntimeConfig()
    {
        return new RuntimeConfig
        {
            PythonPath = PythonPath.Trim(),
            ScreenshotScriptPath = ScreenshotScriptPath.Trim(),
            WindowsIp = WindowsIp.Trim(),
            CarIp = CarIp.Trim()
        };
    }

    private async Task StartAllAsync()
    {
        AddLog("一键启动", "编排", LogLevel.Info, "开始按顺序启动任务");

        var screenshotTask = FindTask("windows-screenshot");
        if (screenshotTask is not null && screenshotTask.State is TaskRunState.Idle or TaskRunState.Stopped or TaskRunState.Failed)
        {
            await screenshotTask.StartCommand.ExecuteAsync();
        }

        if (!Drone.IsConnected)
        {
            await Drone.ConnectCommand.ExecuteAsync();
        }

        if (!Car.IsConnected)
        {
            await Car.ConnectCommand.ExecuteAsync();
        }

        foreach (var taskId in new[] { "drone-mavproxy", "car-mavproxy", "drone-video", "car-detect" })
        {
            var task = FindTask(taskId);
            if (task is null)
            {
                continue;
            }

            if (task.State is TaskRunState.Idle or TaskRunState.Stopped or TaskRunState.Failed)
            {
                await task.StartCommand.ExecuteAsync();
            }
        }

        AddLog("一键启动", "编排", LogLevel.Success, "一键启动流程已完成");
    }

    private async Task StopAllAsync()
    {
        AddLog("停止全部", "编排", LogLevel.Info, "开始停止全部任务");

        foreach (var taskId in new[] { "car-detect", "drone-video", "car-mavproxy", "drone-mavproxy", "windows-screenshot" })
        {
            var task = FindTask(taskId);
            if (task is null)
            {
                continue;
            }

            if (task.State is TaskRunState.Running or TaskRunState.RemoteAlreadyRunning or TaskRunState.Failed)
            {
                await task.StopCommand.ExecuteAsync();
            }
        }

        AddLog("停止全部", "编排", LogLevel.Success, "停止全部流程已完成");
    }

    private ManagedTaskViewModel? FindTask(string id)
    {
        return Tasks.FirstOrDefault(task => task.Id == id);
    }

    private bool FilterLog(object item)
    {
        if (item is not LogEntry log)
        {
            return false;
        }

        return SelectedLogFilter switch
        {
            "本地" => log.Source.Contains("截图", StringComparison.Ordinal) || log.Category == "本地",
            "无人机" => log.Source.Contains("无人机", StringComparison.Ordinal),
            "无人车" => log.Source.Contains("无人车", StringComparison.Ordinal),
            "视频" => log.Category.Contains("视频", StringComparison.Ordinal),
            "算法" => log.Category.Contains("算法", StringComparison.Ordinal),
            "SSH" => log.Category == "SSH",
            _ => true
        };
    }

    private static IReadOnlyList<ManagedTaskDefinition> BuildTaskDefinitions()
    {
        return new[]
        {
            new ManagedTaskDefinition
            {
                Id = "windows-screenshot",
                DisplayName = "Windows 截图服务",
                Category = "本地",
                TargetDevice = DeviceKind.Local,
                RunnerKind = CommandRunnerKind.LocalProcess,
                LocalPort = 5001
            },
            new ManagedTaskDefinition
            {
                Id = "drone-mavproxy",
                DisplayName = "无人机飞控转发",
                Category = "飞控",
                TargetDevice = DeviceKind.Drone,
                RunnerKind = CommandRunnerKind.RemoteShell,
                ExistingCheckCommandTemplate = "pgrep -af \"mavproxy.py.*serial0\" || true",
                PreStartSteps = new[] { new RemoteCommandStep("pkill -f \"mavproxy.py --master=/dev/serial0\" || true", description: "清理无人机 MAVProxy") },
                StartCommandTemplate = "mavproxy.py --master=/dev/serial0 --baudrate=921600 --out=udp:{CarIp}:14551",
                StopCommandTemplate = "pkill -f \"mavproxy.py --master=/dev/serial0\" || true"
            },
            new ManagedTaskDefinition
            {
                Id = "car-mavproxy",
                DisplayName = "无人车飞控汇聚",
                Category = "飞控",
                TargetDevice = DeviceKind.Car,
                RunnerKind = CommandRunnerKind.RemoteShell,
                ExistingCheckCommandTemplate = "pgrep -af \"mavproxy.py.*ttyTHS0\" || true",
                PreStartSteps = new[] { new RemoteCommandStep("pkill -f \"mavproxy.py --master=/dev/ttyTHS0\" || true", description: "清理无人车 MAVProxy") },
                StartCommandTemplate = "mavproxy.py --master=/dev/ttyTHS0 --baudrate=921600 --master=udp:0.0.0.0:14551 --out=udp:{WindowsIp}:14550 --out=udp:127.0.0.1:14550",
                StopCommandTemplate = "pkill -f \"mavproxy.py --master=/dev/ttyTHS0\" || true"
            },
            new ManagedTaskDefinition
            {
                Id = "drone-video",
                DisplayName = "无人机视频推流",
                Category = "视频",
                TargetDevice = DeviceKind.Drone,
                RunnerKind = CommandRunnerKind.RemoteShell,
                ExistingCheckCommandTemplate = "pgrep -af \"ffmpeg.*udp://{CarIp}:5000\" || true",
                PreStartSteps = new[]
                {
                    new RemoteCommandStep("fuser -k /dev/video0 2>/dev/null || true", requiresSudo: true, description: "清理无人机摄像头占用"),
                    new RemoteCommandStep("pkill -f \"ffmpeg.*udp://{CarIp}:5000\" || true", description: "清理无人机 FFmpeg")
                },
                StartCommandTemplate = "ffmpeg -f v4l2 -video_size 1280x720 -framerate 10 -i /dev/video0 -map 0:v -vf scale=960:540 -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -bf 0 -b:v 800k -maxrate 800k -bufsize 400k -g 10 -f mpegts \"udp://{CarIp}:5000?pkt_size=1316&buffer_size=65535\"",
                StopCommandTemplate = "fuser -k /dev/video0 2>/dev/null || true; pkill -f \"ffmpeg.*udp://{CarIp}:5000\" || true",
                StopRequiresSudo = true
            },
            new ManagedTaskDefinition
            {
                Id = "car-detect",
                DisplayName = "无人车算法容器",
                Category = "算法",
                TargetDevice = DeviceKind.Car,
                RunnerKind = CommandRunnerKind.RemoteShell,
                ExistingCheckCommandTemplate = "docker ps --filter \"name=airground_detect_car\" --format \"{{.Names}}\" || true",
                ExistingCheckRequiresSudo = true,
                PreStartSteps = new[]
                {
                    new RemoteCommandStep("docker stop airground_detect_car 2>/dev/null || true; fuser -k /dev/video0 2>/dev/null || true", requiresSudo: true, description: "清理算法容器和摄像头"),
                    new RemoteCommandStep("export DISPLAY=:0; xhost +", description: "授权无人车 X11 屏幕访问")
                },
                StartCommandTemplate = "export DISPLAY=:0; docker run --rm --name airground_detect_car --runtime nvidia --network host --ipc=host -e DISPLAY=:0 -v /tmp/.X11-unix:/tmp/.X11-unix -v ~/.Xauthority:/root/.Xauthority:ro --device /dev/video0:/dev/video0 -v ~/my_model:/workspace drowning-env-v3 python3 /workspace/detect_car.py",
                StartRequiresSudo = true,
                StopCommandTemplate = "docker stop airground_detect_car 2>/dev/null || true; fuser -k /dev/video0 2>/dev/null || true",
                StopRequiresSudo = true
            }
        };
    }

    public void Dispose()
    {
        _localRunner.Dispose();
        _remoteRunner.Dispose();
        _sshSessionManager.Dispose();
    }
}
