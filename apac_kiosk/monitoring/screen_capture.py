from __future__ import annotations

import os
import threading
import time
from datetime import datetime
from PIL import ImageGrab, Image
import io

from ..database import db


class ScreenCapture:
    def __init__(self):
        self._running = False
        self._thread: threading.Thread | None = None
        self._user_id: int | None = None
        self._username: str = ""

    def start(self, user_id: int, username: str):
        self._user_id = user_id
        self._username = username
        self._running = True
        self._thread = threading.Thread(target=self._capture_loop, daemon=True)
        self._thread.start()

    def stop(self):
        self._running = False

    def _capture_loop(self):
        while self._running:
            try:
                interval = int(db.get_config("screenshot_interval", "60"))
                self._capture_once()
            except Exception:
                pass
            time.sleep(interval)

    def _capture_once(self):
        try:
            img = ImageGrab.grab(all_screens=True)
            quality = db.get_config("screenshot_quality", "medium")
            quality_map = {"high": 95, "medium": 70, "low": 40}
            jpg_quality = quality_map.get(quality, 70)

            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            filename = f"screenshot_{timestamp}_{self._username}.jpg"
            folder = db.get_config("screenshot_path", os.path.join(os.path.dirname(db.DB_PATH), "screenshots"))
            os.makedirs(folder, exist_ok=True)
            filepath = os.path.join(folder, filename)

            if quality == "low":
                img = img.resize((img.width // 2, img.height // 2), Image.LANCZOS)

            img.save(filepath, "JPEG", quality=jpg_quality)
            db.insert_log(
                self._user_id, "screenshot", file_path=filepath,
                description=f"Captura de tela"
            )
        except Exception:
            pass
