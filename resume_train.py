from ultralytics import YOLO

if __name__ == '__main__':
# 1. 直接加载你 E 盘里刚刚抢救下来的 last.pt
    model = YOLO(r"E:\ultralytics-main\ultralytics-main\runs\detect\sea_stage14\weights\last.pt")

# 2. 开启断点续训！
# ⚠️ 强烈建议加上 workers=2 或 4，限制内存占用，保护 C 盘！
    results = model.train(resume=True, workers=4)