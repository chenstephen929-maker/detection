from ultralytics import YOLO

if __name__ == '__main__':
    model = YOLO(r'E:\ultralytics-main\ultralytics-main\runs\detect\sea_stage\weights\best.pt')

    result = model.train(
        data=r'E:\ultralytics-main\ultralytics-main\mixed_fixed.yaml',
        epochs=80,
        patience=40,
        batch=8,
        imgsz=960,
        optimizer='AdamW',
        workers=4,
        amp=True,
        cache=False,
        mosaic=1.0,
        close_mosaic=10,
        mixup=0.0,
        fliplr=0.5,
        flipud=0.2,
        cos_lr=True,
        device=0,
        name='mixed_fixed_stage'
    )