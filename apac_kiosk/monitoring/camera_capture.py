from __future__ import annotations

import os
import threading
import time
from datetime import datetime

import cv2

from ..database import db


class CameraCapture:
    def __init__(self):
        self._running = False
        self._thread: threading.Thread | None = None
        self._user_id: int | None = None
        self._username: str = ""
        self._cap: cv2.VideoCapture | None = None

    def start(self, user_id: int, username: str):
        self._user_id = user_id
        self._username = username
        device_id = int(db.get_config("camera_device", "0"))
        self._cap = cv2.VideoCapture(device_id)
        if not self._cap.isOpened():
            db.insert_log(None, "camera_error", description="Câmera não encontrada ou indisponível")
            self._cap = None
            return
        self._running = True
        self._thread = threading.Thread(target=self._capture_loop, daemon=True)
        self._thread.start()

    def stop(self):
        self._running = False
        if self._cap:
            self._cap.release()
            self._cap = None

    def _capture_loop(self):
        while self._running:
            try:
                interval = int(db.get_config("camera_interval", "120"))
                self._capture_once()
            except Exception:
                pass
            time.sleep(interval)

    def _capture_once(self):
        if self._cap is None:
            return
        try:
            ret, frame = self._cap.read()
            if not ret:
                db.insert_log(None, "camera_error", description="Falha ao capturar frame da câmera")
                return

            quality = db.get_config("camera_quality", "medium")
            quality_map = {"high": 95, "medium": 70, "low": 40}
            jpg_quality = quality_map.get(quality, 70)

            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            filename = f"camera_{timestamp}_{self._username}.jpg"
            folder = db.get_config("camera_path", os.path.join(os.path.dirname(db.DB_PATH), "camera"))
            os.makedirs(folder, exist_ok=True)
            filepath = os.path.join(folder, filename)

            cv2.imwrite(filepath, frame, [cv2.IMWRITE_JPEG_QUALITY, jpg_quality])
            db.insert_log(
                self._user_id, "camera", file_path=filepath,
                description="Captura de câmera"
            )
        except Exception:
            pass

    @staticmethod
    def list_cameras() -> list[tuple[int, str]]:
        cameras = []
        for i in range(10):
            cap = cv2.VideoCapture(i)
            if cap.isOpened():
                cameras.append((i, f"Câmera {i}"))
                cap.release()
        return cameras
