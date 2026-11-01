from ultralytics import YOLO

if __name__ == '__main__':
    model = YOLO(r'E:\ultralytics-main\ultralytics-main\runs\detect\sea_stage\weights\best.pt')

    result = model.train(
        data=r'E:\ultralytics-main\ultralytics-main\mixed.yaml',
        epochs=80,
        imgsz=960,
        batch=8,
        workers=4,
        lr0=0.001,
        lrf=0.01,
        warmup_epochs=1.0,
        name='mixed_stage'
    )