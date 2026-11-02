import cv2
import os
from ultralytics import YOLO
import time
import threading
import datetime
from pymavlink import mavutil
import requests
import queue
import numpy as np

#核心配置区

VIDEO_URL = 'udp://@0.0.0.0:5000?fifo_size=50000&overrun_nonfatal=1&buffer_size=65535'   #  接收天上无人机的视频流（UDP）
USB_CAMERA_ID = 0                   #  岸上小车本地 USB 摄像头

#  只保留一个 MAVLink 本地入口
LOCAL_MAVLINK_URL = 'udp:127.0.0.1:14550'

WINDOWS_IP = "100.68.90.47"
UPLOAD_URL = f"http://{WINDOWS_IP}:5001/upload"

MODEL_PATH = '/workspace/best.engine'
SCREENSHOT_PATH = '/workspace/screenshot'

CONF_THRE = 0.4
TARGET_CLASSES = [1]
ALARM_COOLDOWN = 5.0

STREAM_WIDTH = 1280
STREAM_HEIGHT = 720
STREAM_FPS = 15

MAX_READ_FAIL = 30
QUEUE_MAXSIZE = 20

# ================= 全局状态 =================

drone_lat, drone_lon = 0.0, 0.0
car_lat, car_lon = 0.0, 0.0
gps_lock = threading.Lock()

last_alarm_time_car = 0
last_alarm_time_drone = 0

#  MAVLink 发送队列：由主线程塞入告警，由 MAVLink 线程统一发出
mav_tx_queue = queue.Queue(maxsize=50)

# 截图上传队列：由主线程塞任务，由后台线程上传
upload_queue = queue.Queue(maxsize=QUEUE_MAXSIZE)

os.makedirs(SCREENSHOT_PATH, exist_ok=True)


#  视频源线程
# 1. 每路视频各自独立采集
# 2. 断流重连只影响自己，不阻塞另一条链路
# 3. 始终只保留最新帧，主线程不会处理过期帧

class LatestFrameSource:
    def __init__(self, name, open_func, reopen_interval=1.0, max_fail=30):
        self.name = name
        self.open_func = open_func
        self.reopen_interval = reopen_interval
        self.max_fail = max_fail

        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.thread = None

        self.cap = None
        self.latest_frame = None
        self.frame_id = 0
        self.status = "INIT"

    def start(self):
        self.thread = threading.Thread(target=self._worker, daemon=True)
        self.thread.start()

    def _worker(self):
        fail_count = 0

        while not self.stop_event.is_set():
            # [新增] 如果当前没有可用连接，则在本线程内部重连
            if self.cap is None or not self.cap.isOpened():
                self.status = "RECONNECTING"
                try:
                    self.cap = self.open_func()
                except Exception as e:
                    print(f"❌ [{self.name}] 打开视频源失败: {e}")
                    self.cap = None

                if self.cap is None or not self.cap.isOpened():
                    time.sleep(self.reopen_interval)
                    continue

                print(f"✅ [{self.name}] 视频源已连接")
                fail_count = 0
                self.status = "ONLINE"

            try:
                ret, frame = self.cap.read()
            except Exception:
                ret, frame = False, None

            if ret and frame is not None:
                with self.lock:
                    self.latest_frame = frame
                    self.frame_id += 1
                    self.status = "ONLINE"
                fail_count = 0
            else:
                fail_count += 1
                self.status = "NO_SIGNAL"

                if fail_count >= self.max_fail:
                    print(f"⚠️ [{self.name}] 连续读帧失败，准备重连...")
                    try:
                        self.cap.release()
                    except Exception:
                        pass
                    self.cap = None
                    fail_count = 0

                #  这里的 sleep 只阻塞本视频线程，不阻塞主线程/另一条视频
                time.sleep(0.02)

    def get_latest(self):
        with self.lock:
            if self.latest_frame is None:
                return None, None, self.status
            return self.frame_id, self.latest_frame.copy(), self.status

    def stop(self):
        self.stop_event.set()
        try:
            if self.cap is not None:
                self.cap.release()
        except Exception:
            pass


# ================= 📡 MAVLink 总线线程 =================
# 1. 单线程独占一个 udp: 连接
# 2. 统一负责 GPS 接收
# 3. 统一负责告警消息发回 MAVProxy
# 这样就避免“一个连接多线程同时收发”的问题，也避免错误的 udpout 回环

def mavlink_bus_worker():
    global drone_lat, drone_lon, car_lat, car_lon

    print("📡 [MAV] 正在连接 MAVProxy 本地 UDP 总线 (用于接收 GPS)...")
    # 接收通道保持不变，只管收 GPS
    connection = mavutil.mavlink_connection(LOCAL_MAVLINK_URL, source_system=250)

    #  建立一条直接通往 Windows QGC 的报警热线！
    print(f"🚨 [MAV] 正在建立 QGC 直连告警通道 -> {WINDOWS_IP}:14550")
    qgc_direct = mavutil.mavlink_connection(f'udpout:{WINDOWS_IP}:14550', source_system=2, source_component=191)
    #

    print("✅ [MAV] 链路已就绪，开始解析并转发告警")

    while True:
        try:
            msg = connection.recv_match(
                type=['GLOBAL_POSITION_INT', 'GPS_RAW_INT'],
                blocking=True,
                timeout=0.2
            )
            if msg:
                lat_raw = getattr(msg, 'lat', 0)
                lon_raw = getattr(msg, 'lon', 0)

                if lat_raw != 0 and lon_raw != 0:
                    sysid = msg.get_srcSystem()
                    lat = lat_raw / 1e7
                    lon = lon_raw / 1e7

                    with gps_lock:
                        if sysid == 1:
                            drone_lat = lat
                            drone_lon = lon
                        elif sysid == 2:
                            car_lat = lat
                            car_lon = lon
        except Exception as e:
            pass  # 屏蔽接收超时的刷屏

        while True:
            try:
                text = mav_tx_queue.get_nowait()
            except queue.Empty:
                break

            try:
                safe_text = text.encode('ascii', errors='ignore')[:50]

                # 使用直连热线发给 QGC，强制触发 CRITICAL 警报！
                qgc_direct.mav.statustext_send(
                    mavutil.mavlink.MAV_SEVERITY_CRITICAL,
                    safe_text
                )
                print(f"🚨 [QGC] 告警已直接拍给 QGC: {text}")

            except Exception as e:
                print(f"❌ [QGC] 告警注入失败: {e}")
            finally:
                mav_tx_queue.task_done()

def enqueue_qgc_msg(text):
    # [新增] 主线程不直接发 MAVLink，只把消息塞给 MAV 线程
    try:
        mav_tx_queue.put_nowait(text)
    except queue.Full:
        print("⚠️ [QGC] 告警队列已满，本次告警被丢弃")


# ================= 上传线程 =================

def upload_worker():
    print("🌐 [上传线程] 已启动")
    while True:
        task = upload_queue.get()
        if task is None:
            break

        screenshot_fullpath, screenshot_filename, source = task
        try:
            with open(screenshot_fullpath, 'rb') as f:
                files = {'file': (screenshot_filename, f, 'image/jpeg')}
                response = requests.post(UPLOAD_URL, files=files, timeout=3.0)

            if response.status_code == 200:
                print(f"✅ [{source}] 现场照片已同步至指挥中心")
            else:
                print(f"❌ [{source}] 照片回传失败，状态码: {response.status_code}")
        except Exception as e:
            print(f"❌ [{source}] 照片回传失败: {e}")
        finally:
            upload_queue.task_done()


def save_screenshot(frame, lat, lon, source="Unknown"):
    timestamp = datetime.datetime.now().strftime('%H%M%S')
    screenshot_filename = f'Target_{source}_{timestamp}_lat{lat:.6f}_lon{lon:.6f}.jpg'
    screenshot_fullpath = os.path.join(SCREENSHOT_PATH, screenshot_filename)
    cv2.imwrite(screenshot_fullpath, frame)
    print(f'📸 [{source}] 抓拍成功! 坐标: {lat:.5f}, {lon:.5f}')

    # [修改] 原来是每次告警都新建线程上传
    # 现在统一走队列，后台固定线程处理
    try:
        upload_queue.put_nowait((screenshot_fullpath, screenshot_filename, source))
    except queue.Full:
        print(f"⚠️ [{source}] 上传队列已满，本次截图仅本地保存")


# ================= 🖼️ 画框与叠字 =================

def draw_overlay(frame, source_name, lat, lon, detected):
    # [新增] 给截图叠加来源/GPS/时间，方便回传到 Windows 后直接看
    now_str = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    gps_text = f"GPS: {lat:.6f}, {lon:.6f}" if lat != 0 and lon != 0 else "GPS: LOST"
    target_text = "TARGET: YES" if detected else "TARGET: NO"

    lines = [
        f"SRC: {source_name}",
        gps_text,
        target_text,
        now_str
    ]

    y = 30
    for line in lines:
        cv2.putText(frame, line, (20, y), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 255, 255), 2)
        y += 30

    return frame


def process_one_frame(model, frame, source_name, lat, lon, last_alarm_time):
    detected = False

    try:
        # 不再用 stream=True 生成器
        # 当前是单帧-立即处理场景，直接取 results[0] 更清晰
        results = model(frame, conf=CONF_THRE, verbose=False,imgsz=960)

        if results and len(results) > 0:
            result = results[0]
            for box in result.boxes:
                cls_id = int(box.cls[0])
                if cls_id in TARGET_CLASSES:
                    detected = True
                    x1, y1, x2, y2 = map(int, box.xyxy[0])
                    conf = float(box.conf[0])

                    cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 0, 255), 2)
                    label = f"cls:{cls_id} conf:{conf:.2f}"
                    cv2.putText(
                        frame, label, (x1, max(25, y1 - 10)),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2
                    )
    except Exception as e:
        print(f"⚠️ [{source_name}] YOLO 推理异常: {e}")

    frame = draw_overlay(frame, source_name, lat, lon, detected)

    current_time = time.time()
    if detected and (current_time - last_alarm_time > ALARM_COOLDOWN):
        if lat != 0 and lon != 0:
            if source_name == "DRONE":
                msg = f"DRONE_ALERT LAT:{lat:.5f} LON:{lon:.5f}"
            else:
                msg = f"CAR_ALERT LAT:{lat:.5f} LON:{lon:.5f}"
        else:
            if source_name == "DRONE":
                msg = "DRONE_ALERT GPS_LOST"
            else:
                msg = "CAR_ALERT GPS_LOST"

        save_screenshot(frame, lat, lon, source=source_name)
        enqueue_qgc_msg(msg)
        last_alarm_time = current_time

    return last_alarm_time


# ================= 视频源打开函数 =================

def open_drone_stream():
    cap = cv2.VideoCapture(VIDEO_URL, cv2.CAP_FFMPEG)
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    return cap


def open_usb_camera():
    cap = cv2.VideoCapture(USB_CAMERA_ID,cv2.CAP_V4L2)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, STREAM_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, STREAM_HEIGHT)
    cap.set(cv2.CAP_PROP_FPS, STREAM_FPS)
    return cap


# ================= 🚀 主循环 =================

def main():
    global last_alarm_time_car, last_alarm_time_drone

    # [新增] 启动 MAVLink 总线线程
    threading.Thread(target=mavlink_bus_worker, daemon=True).start()

    # [新增] 启动上传线程
    threading.Thread(target=upload_worker, daemon=True).start()

    # [新增] 两路视频采集线程独立
    drone_source = LatestFrameSource(
        name="DRONE",
        open_func=open_drone_stream,
        reopen_interval=1.0,
        max_fail=MAX_READ_FAIL
    )
    car_source = LatestFrameSource(
        name="CAR",
        open_func=open_usb_camera,
        reopen_interval=1.0,
        max_fail=MAX_READ_FAIL
    )

    drone_source.start()
    car_source.start()

    # [保留] 单模型，先保证整套算法稳定跑起来
    model = YOLO(MODEL_PATH, task='detect')

    last_drone_frame_id = -1
    last_car_frame_id = -1

    print('🟢 海空双路 AI 监控系统已就绪（全链路 UDP 版）')
    print(f'📡 无人机视频输入: {VIDEO_URL}')
    print(f'📡 MAVLink 本地总线: {LOCAL_MAVLINK_URL}')
    print(f'🌐 截图回传目标: {UPLOAD_URL}')
    # 1. 声明窗口，允许缩放
    cv2.namedWindow("Drone View", cv2.WINDOW_NORMAL)
    # 2. 强制窗口进入全屏模式！去他喵的边框和任务栏！
    cv2.setWindowProperty("Drone View", cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)

    try:
        while True:
            processed_any = False

            # ================= 🚁 处理无人机最新帧 =================
            drone_frame_id, frame_drone, drone_status = drone_source.get_latest()
            if frame_drone is not None and drone_frame_id != last_drone_frame_id:
                last_drone_frame_id = drone_frame_id
                processed_any = True

                with gps_lock:
                    lat, lon = drone_lat, drone_lon

                last_alarm_time_drone = process_one_frame(
                    model=model,
                    frame=frame_drone,
                    source_name="DRONE",
                    lat=lat,
                    lon=lon,
                    last_alarm_time=last_alarm_time_drone
                )

                cv2.imshow("Drone View", frame_drone)


            # ================= 🚜 处理小车最新帧 =================
            car_frame_id, frame_car, car_status = car_source.get_latest()
            if frame_car is not None and car_frame_id != last_car_frame_id:
                last_car_frame_id = car_frame_id
                processed_any = True

                with gps_lock:
                    lat, lon = car_lat, car_lon

                last_alarm_time_car = process_one_frame(
                    model=model,
                    frame=frame_car,
                    source_name="CAR",
                    lat=lat,
                    lon=lon,
                    last_alarm_time=last_alarm_time_car
                )
            if cv2.waitKey(1) & 0xFF == ord('q'):
                print("🛑 收到退出指令，正在关闭显示...")
                break
            # [修改] 不再出现“重连 sleep 1 秒把整个主循环卡死”的情况
            if not processed_any:
                time.sleep(0.005)

    finally:
        print("🛑 正在释放资源...")
        drone_source.stop()
        car_source.stop()

        try:
            upload_queue.put_nowait(None)
        except Exception:
            pass

        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()