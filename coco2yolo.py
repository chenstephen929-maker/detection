from ultralytics.data.converter import convert_coco

convert_coco(
    labels_dir=r"E:\dataset\Compressed Version\Compressed Version\annotations",
    save_dir=r"E:\dataset\Compressed Version\Compressed Version\yololabels",
    cls91to80=False
)