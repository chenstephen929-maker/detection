using AirGroundControlApp.Models;
using AirGroundControlApp.Services;
using AirGroundControlApp.Utilities;

namespace AirGroundControlApp.ViewModels;

public sealed class DeviceConnectionViewModel : ObservableObject
{
    private readonly SshSessionManager _sshSessionManager;
    private readonly ILogSink _logSink;
    private string _host = "";
    private int _port = 22;
    private string _username = "";
    private string _password = "";
    private string _sudoPassword = "";
    private bool _isConnected;
    private string _statusText = "未连接";

    public DeviceConnectionViewModel(DeviceKind kind, string title, SshSessionManager sshSessionManager, ILogSink logSink)
    {
        Kind = kind;
        Title = title;
        _sshSessionManager = sshSessionManager;
        _logSink = logSink;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
    }

    public DeviceKind Kind { get; }
    public string Title { get; }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string SudoPassword
    {
        get => _sudoPassword;
        set => SetProperty(ref _sudoPassword, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AsyncRelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }

    public DeviceConfig ToConfig()
    {
        return new DeviceConfig
        {
            Kind = Kind,
            Host = Host.Trim(),
            Port = Port,
            Username = Username.Trim(),
            Password = Password,
            SudoPassword = SudoPassword
        };
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            StatusText = "请填写 Host 和用户名";
            _logSink.AddLog(Title, "SSH", LogLevel.Warning, "请填写 Host 和用户名");
            return;
        }

        StatusText = "连接中";
        _logSink.AddLog(Title, "SSH", LogLevel.Info, $"正在连接 {Host}:{Port}");

        try
        {
            await _sshSessionManager.ConnectAsync(ToConfig());
            IsConnected = true;
            StatusText = "已连接";
            _logSink.AddLog(Title, "SSH", LogLevel.Success, "SSH 已连接");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = "连接失败";
            _logSink.AddLog(Title, "SSH", LogLevel.Error, $"SSH 连接失败: {ex.Message}");
        }
    }

    private void Disconnect()
    {
        _sshSessionManager.Disconnect(Kind);
        IsConnected = false;
        StatusText = "未连接";
        _logSink.AddLog(Title, "SSH", LogLevel.Info, "SSH 已断开");
    }
}
