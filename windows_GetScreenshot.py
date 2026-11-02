from flask import Flask, request
import os
import winsound
import subprocess
app = Flask(__name__)

# 存照片的路径
SAVE_DIR = r"E:\screenshot_drone"
os.makedirs(SAVE_DIR, exist_ok=True)


@app.route('/upload', methods=['POST'])
def upload_file():
    if 'file' not in request.files:
        return "No file found", 400

    file = request.files['file']
    if file.filename == '':
        return "Empty filename", 400

    # 拼装 Windows 的绝对路径并保存
    filepath = os.path.join(SAVE_DIR, file.filename)
    file.save(filepath)

    # 收到照片时，Windows 电脑主板会“滴”地响一声提示
    winsound.Beep(1500, 300)
    print(f"收到 Jetson 实时告警截图，已保存至: {filepath}")

    try:
        os.startfile(filepath)
    except AttributeError:
        subprocess.call(['cmd', '/c', 'start', '', filepath])

    return "Success", 200


if __name__ == '__main__':
    print(f"Windows 截图接收服务器已启动！正在监听端口 5001...")
    print(f"接收文件夹: {SAVE_DIR}")
    # 0.0.0.0 表示监听来自 ZeroTier 的所有连接
    app.run(host='0.0.0.0', port=5001)