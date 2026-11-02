# 空地协同 Windows 控制端

这是 QGC 配套的 Windows 任务控制台。它不接管飞控状态显示，只负责启动、停止和记录以下任务：

- Windows 截图接收服务：`windows_GetScreenshot.py`
- 无人机 MAVProxy 飞控转发
- 无人车 MAVProxy 飞控汇聚
- 无人机 FFmpeg 视频推流
- 无人车 Docker 算法容器：`detect_car.py`

## 默认路径

- Python 解释器：`D:\anaconda2024\envs\pytorch\python.exe`
- 截图脚本：`E:\ultralytics-main\ultralytics-main\windows_GetScreenshot.py`
- Windows/QGC IP：`100.68.90.47`
- 无人车接收 IP：`100.69.99.51`

## 构建

```powershell
dotnet restore AirGroundControlApp.csproj
dotnet build AirGroundControlApp.csproj
```

如果当前终端访问用户目录受限，可临时指定本地缓存目录：

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:LOCALAPPDATA="$PWD\.localappdata"
$env:APPDATA="$PWD\.appdata"
$env:USERPROFILE="$PWD\.userprofile"
$env:NUGET_PACKAGES="$PWD\.nuget\packages"
dotnet restore AirGroundControlApp.csproj
dotnet build AirGroundControlApp.csproj
```

## 使用

1. 手动打开 QGC，并监听 Windows UDP `14550`。
2. 填写无人机和无人车 SSH 信息。
3. 输入 SSH 密码和 sudo 密码，密码不会保存。
4. 可单项启动，也可点击“一键启动全部”。
5. 启动“无人车算法容器”前，程序会自动在无人车上执行 `export DISPLAY=:0; xhost +`，让 SSH 启动的 Docker 算法可以访问无人车屏幕。
6. 远程设备联调时重点观察任务状态和日志区。

## X11 显示授权排查

如果无人车算法容器启动后仍然无法打开 OpenCV 显示窗口，可先在无人车本机终端手动验证：

```bash
export DISPLAY=:0
xhost +
```

确认该命令成功后，再回到 Windows 控制端启动“无人车算法容器”。当前默认显示号固定为 `:0`。
