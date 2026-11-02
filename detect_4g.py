from fileinput import filename

import cv2
import os


from ultralytics import YOLO
import time
import threading
import datetime
import winsound
from pymavlink import mavutil



#config
RTSP_URL='udp://@0.0.0.0:5000'#模块IP(缺个端口)
QGC_CONNECTION_URL='udpin:127.0.0.1:14445'#QGC链接地址
#model
MODEL_PATH=r'E:\ultralytics-main\ultralytics-main\runs\detect\train3\weights\best.pt'
CONF_THRE=0.3
TARGET_CLASSES=[0,1,2]
#WARNING MODEL
SCREENSHOT_PATH=r'E:\screenshot_drone'
ALARM_COOLDOWN=5.0
SOUND_FRE=1000
SOUND_DUR=500

current_lat=0
current_lon=0
last_alarm_time=0

os.makedirs(SCREENSHOT_PATH,exist_ok=True)

#Mavlink数据读取
def mavlink_receive(connection):
    global current_lat,current_lon
    print("start monitoring...waiting for connection...")
    timeout=0
    while True:
        try:
            #等待接收
            msg= connection.recv_match(type='GPS_RAW_INT',blocking=True,timeout=1.0)
            if msg is not None:
                current_lat=msg.lat/1e7
                current_lon=msg.lon/1e7
                timeout=0
            else:
                timeout+=1
                if timeout%5==0:
                    print('timeout')
        except Exception as e:
            print(f"reading error {e}")
            break

#告警
def play_sound():
    for _ in range(3):
        winsound.Beep(SOUND_FRE,SOUND_DUR)
        time.sleep(0.1)

def send_qgc_msg(mav_connection,text):
    if mav_connection is not None:
        mav_connection.mav.statustext_send(mavutil.mavlink.MAV_SEVERITY_CRITICAL,text.encode('utf-8'))

def save_screenshot(frame,lat,lon):
    timestamp=datetime.datetime.now().strftime('%H%M%S')
    screenshot_filename=f'target_{timestamp}_lat{lat:.6f}_lon{lon:.6f}.jpg'
    screenshot_fullpath=os.path.join(SCREENSHOT_PATH,screenshot_filename)
    cv2.imwrite(screenshot_fullpath,frame)
    print(f'screenshot saved  position:{lat},{lon}--------{screenshot_fullpath}')

def main():
    global last_alarm_time
    #initialize mavlink
    try:
        mav_connection = mavutil.mavlink_connection(QGC_CONNECTION_URL,source_system=1,source_component=191)
        threading.Thread(target=mavlink_receive,args=(mav_connection,),daemon=True).start()
        print('mavlink_receive')
    except Exception as e:
        print(f"mavlink error(data passing) {e}")
        mav_connection = None

    model = YOLO(MODEL_PATH)

    #cap = cv2.VideoCapture(RTSP_URL, cv2.CAP_FFMPEG)
    cap = cv2.VideoCapture(RTSP_URL)#test
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

    print('initialization over, start monitoring')
    while cap.isOpened():
        ret, frame = cap.read()
        if not ret: break
        results=model(frame,conf=CONF_THRE,stream=True,verbose=False)
        detected=False
        label=""

        for result in results:
            for box in result.boxes:
                if int(box.cls[0]) in TARGET_CLASSES:
                    detected=True
                    label=model.names[int(box.cls[0])]
                    x1,y1,x2,y2=map(int,box.xyxy[0])
                    cv2.rectangle(frame,(x1,y1),(x2,y2),(0,0,255),2)

        #warning
        currnt_time=time.time()
        if detected and (currnt_time-last_alarm_time>ALARM_COOLDOWN):#大于间隔
            lat,lon=current_lat,current_lon#read position
            threading.Thread(target=play_sound, daemon=True).start()
            save_screenshot(frame,lat,lon)
            send_qgc_msg(mav_connection,f"Alert:{label} at {lat:.4f},{lon:.4f}")

            last_alarm_time=currnt_time

        cv2.imshow('Drone drowning detection',frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):break
    cap.release()
    cv2.destroyAllWindows()

if __name__ == '__main__':
    main()
