# 空地协同识别项目源码

该目录包含本机项目的 Ultralytics 源码、检测/截图脚本与 Windows WPF 控制端。仓库已包含指定的 mixed_fixed_stage 自训练 best.pt；训练数据、其他模型、已有 Docker 镜像和设备软件另行准备。

## 目录与入口

- `ultralytics/`：当前项目所用的完整框架源码与配置。
- `detect_car.py`：无人车侧检测、通信与截图业务入口。
- `detect_4g.py`：另一套单路监测入口，包含 Windows 专用接口。
- `windows_GetScreenshot.py`：Windows 截图接收服务。
- `user_app/`：空地协同 Windows 控制端，WPF/.NET 8。
- `stage2.py`、`resume_train.py`、`yolov8-drowing.py`：训练/续训脚本。
- `coco2yolo.py` 与根目录 YAML：数据转换与数据集配置。
- `tests/`：保留的上游验证代码，不代表真实设备联调已经通过。

## Python 依赖

在目标设备合适的 Python 环境中安装当前源码，不要用在线最新版替换本地框架：

```bash
pip install -e .
pip install -r requirements-business.txt
```

PyTorch/CUDA 需要与设备匹配。额外业务依赖未锁版本；精确复现时应补充已经验证的 Python、PyTorch、CUDA 和依赖版本。

## Windows 控制端

需要 Windows 与 .NET 8 SDK。在 `user_app/` 中执行：

```powershell
dotnet restore AirGroundControlApp.csproj
dotnet build AirGroundControlApp.csproj
```

界面使用方式见 `user_app/README.md`。Python 解释器、脚本路径、IP 与远端命令需要按实际机器配置。

## 已提供的自训练权重

- 文件：`runs/detect/mixed_fixed_stage/weights/best.pt`。
- 大小：22,592,099 字节（约 22.6 MB）。这份权重已随仓库提供，克隆后可直接获取。
- SHA-256：`FFBC4B01AEAB84BBD07E4BF7DE6FFC435109AC6740F022980005E9F209805128`。
- 从仓库根目录运行 Python 时，模型加载路径如下：

```python
from ultralytics import YOLO
model = YOLO("runs/detect/mixed_fixed_stage/weights/best.pt")
```

已有脚本中的其他本机路径和 TensorRT engine 路径仍需按实际运行环境配置。这次补传没有替换各脚本原来使用的模型。

## 模型与设备环境

- `detect_car.py` 当前读取 `/workspace/best.engine`；控制端命令挂载远端 `~/my_model` 到 `/workspace`，使用已有 `drowning-env-v3` 镜像。engine、镜像、MAVProxy、FFmpeg、QGC 与设备网络需另行准备。
- 训练脚本使用 `runs/detect/sea_stage/weights/best.pt`。本机核查时该文件存在，但不包含在本源码包中，应提供经过确认的模型下载地址和保存路径。
- `detect_4g.py` 引用的 `runs/detect/train3/weights/best.pt` 与 `resume_train.py` 引用的 `runs/detect/sea_stage14/weights/last.pt`，本机核查时未找到。运行前必须选择正确权重并修改配置。
- 源码保留了现有设备路径与默认值；没有运行训练、编译 WPF 或执行设备控制来验证端到端行为。

## 数据与复现

根目录 YAML 与部分脚本仍包含 `E:/dataset`、E 盘项目的绝对路径。请按目标环境调整。训练复现需要对应图片、标签、类别与训练/验证/测试划分；数据应单独提供下载方式。仅有 XML/TXT 标签不足以复现训练。

`dataset/`、其他训练输出和模型、虚拟环境、缓存及编译输出仍排除；`runs/detect/mixed_fixed_stage/weights/best.pt` 是已纳入版本管理的例外。原 `DDPG/` 目录中未找到算法源码，如需复现该部分，应另外补充实际算法代码。

指定的混合数据集训练权重已提供；其他数据与容器镜像仍需补充可用获取方式，并完成独立环境与真实设备验证。

