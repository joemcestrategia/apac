from __future__ import annotations

import ctypes
import ctypes.wintypes
import threading
import time
import psutil
import win32api
import win32con
import win32gui
import win32process
from winreg import OpenKey, SetValueEx, DeleteValue, QueryValueEx, KEY_ALL_ACCESS
from datetime import datetime

from ..database import db

FORBIDDEN_PROCESSES = [
    "taskmgr.exe", "regedit.exe", "cmd.exe", "powershell.exe",
    "powershell_ise.exe", "mmc.exe", "msconfig.exe", "gpedit.msc",
    "eventvwr.exe", "procexp.exe", "procexp64.exe", "procmon.exe",
    "wireshark.exe", "fiddler.exe", "x64dbg.exe", "ollydbg.exe",
    "autoruns.exe", "autoruns64.exe"
]

HOTKEYS = [
    (win32con.VK_LWIN, 0), (win32con.VK_LWIN, win32con.MOD_WIN),
    (win32con.VK_RWIN, 0), (win32con.VK_RWIN, win32con.MOD_WIN),
    (ord('D'), win32con.MOD_WIN),
    (ord('R'), win32con.MOD_WIN),
    (ord('L'), win32con.MOD_WIN),
    (ord('E'), win32con.MOD_WIN),
    (win32con.VK_F4, win32con.MOD_ALT),
    (win32con.VK_TAB, win32con.MOD_ALT),
    (win32con.VK_ESCAPE, win32con.MOD_ALT),
    (win32con.VK_ESCAPE, win32con.MOD_CONTROL),
    (win32con.VK_ESCAPE, win32con.MOD_CONTROL | win32con.MOD_SHIFT),
    (win32con.VK_F11, 0),
]

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32


class SecurityManager:
    def __init__(self, main_window_hwnd: int):
        self.main_hwnd = main_window_hwnd
        self._hotkey_ids = {}
        self._watchdog_thread: threading.Thread | None = None
        self._window_protect_thread: threading.Thread | None = None
        self._registry_thread: threading.Thread | None = None
        self._watchdog_running = False
        self._window_protect_running = False
        self._registry_running = False
        self._next_hotkey_id = 1

    def start_all(self):
        self.register_hotkeys()
        self.start_process_watchdog()
        self.start_window_protector()
        self.start_registry_protector()

    def stop_all(self):
        self.unregister_hotkeys()
        self._watchdog_running = False
        self._window_protect_running = False
        self._registry_running = False

    def register_hotkeys(self):
        for vk, mod in HOTKEYS:
            try:
                hid = self._next_hotkey_id
                result = user32.RegisterHotKey(self.main_hwnd, hid, mod, vk)
                if result:
                    self._hotkey_ids[hid] = (vk, mod)
                    self._next_hotkey_id += 1
            except Exception:
                pass

    def unregister_hotkeys(self):
        for hid in self._hotkey_ids:
            try:
                user32.UnregisterHotKey(self.main_hwnd, hid)
            except Exception:
                pass
        self._hotkey_ids.clear()

    def start_process_watchdog(self):
        self._watchdog_running = True
        self._watchdog_thread = threading.Thread(target=self._watchdog_loop, daemon=True)
        self._watchdog_thread.start()

    def _watchdog_loop(self):
        while self._watchdog_running:
            try:
                for proc in psutil.process_iter(["name", "pid"]):
                    try:
                        name = proc.info["name"]
                        if name and name.lower() in FORBIDDEN_PROCESSES:
                            proc.kill()
                            db.insert_log(None, "security_process_killed",
                                          description=f"Processo bloqueado: {name} (PID {proc.info['pid']})")
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        pass
            except Exception:
                pass
            time.sleep(2)

    def start_window_protector(self):
        self._window_protect_running = True
        self._window_protect_thread = threading.Thread(target=self._window_protect_loop, daemon=True)
        self._window_protect_thread.start()

    def _window_protect_loop(self):
        hwnd_ptr = ctypes.c_int(self.main_hwnd)
        while self._window_protect_running:
            try:
                user32.SetWindowPos(hwnd_ptr, -1, 0, 0, 0, 0, 0x0002 | 0x0001)
                fg = user32.GetForegroundWindow()
                if fg and fg != self.main_hwnd:
                    user32.BringWindowToTop(hwnd_ptr)
                    user32.SetForegroundWindow(hwnd_ptr)
            except Exception:
                pass
            time.sleep(1)

    def start_registry_protector(self):
        self._registry_running = True
        self._registry_thread = threading.Thread(target=self._registry_protect_loop, daemon=True)
        self._registry_thread.start()

    def _registry_protect_loop(self):
        while self._registry_running:
            try:
                self._ensure_autostart_key()
            except Exception:
                pass
            time.sleep(30)

    def _ensure_autostart_key(self):
        try:
            key = OpenKey(
                win32con.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\CurrentVersion\Run",
                0, KEY_ALL_ACCESS
            )
            val, _ = QueryValueEx(key, "APAC_Kiosk")
            key.Close()
        except Exception:
            self.set_autostart(True)

    def set_autostart(self, enable: bool):
        import sys
        exe_path = sys.executable
        script = os.path.abspath(sys.argv[0]) if sys.argv[0] else ""
        key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
        key = OpenKey(win32con.HKEY_CURRENT_USER, key_path, 0, KEY_ALL_ACCESS)
        if enable:
            cmd = f'"{exe_path}" "{script}"'
            SetValueEx(key, "APAC_Kiosk", 0, win32con.REG_SZ, cmd)
        else:
            try:
                DeleteValue(key, "APAC_Kiosk")
            except Exception:
                pass
        key.Close()


import os
