from __future__ import annotations

import threading
import webview
from urllib.parse import urlparse
import tkinter as tk

from .navigation import is_url_allowed, extract_domain
from ..database import db

BLOCKED_PAGE = """
<html>
<head><meta charset="utf-8"><style>
body { background:#0f0f23; color:#a78bfa; font-family:sans-serif;
       display:flex; align-items:center; justify-content:center; height:100vh; margin:0; }
.container { text-align:center; max-width:500px; padding:40px; }
.logo { font-size:48px; font-weight:bold; color:#7c3aed; margin-bottom:20px; }
h1 { color:#7c3aed; font-size:24px; }
p { color:#999; font-size:16px; line-height:1.6; }
.blocked-icon { font-size:64px; margin-bottom:20px; }
</style></head>
<body>
<div class="container">
<div class="blocked-icon">&#128274;</div>
<div class="logo">APAC</div>
<h1>Site Bloqueado</h1>
<p>Este site não está disponível. Entre em contato com o administrador.</p>
</div>
</body></html>
"""


class KioskBrowser:
    def __init__(self, user_id: int, profile_id: int | None, default_home: str = "https://www.google.com"):
        self.user_id = user_id
        self.profile_id = profile_id
        self.default_home = default_home
        self._window: webview.Window | None = None
        self._current_url = default_home
        self._history: list[str] = []
        self._history_index = -1
        self._nav_toolbar_visible = False

    def start(self):
        self._history = [self.default_home]
        self._history_index = 0

        html = self._build_navigation_html(self.default_home)

        self._window = webview.create_window(
            title="APAC Kiosk",
            html=html,
            fullscreen=True,
            frameless=False,
            easy_drag=False,
            text_select=False,
        )

        self._window.events.loaded += self._on_loaded
        webview.start(gui="edgechromium", debug=False)

    def destroy(self):
        if self._window:
            webview.destroy_window()
            self._window = None

    def _on_loaded(self):
        js = """
        document.addEventListener('contextmenu', function(e) { e.preventDefault(); return false; });
        document.addEventListener('keydown', function(e) {
            if (e.key === 'F12' || (e.ctrlKey && e.shiftKey && e.key === 'I')) {
                e.preventDefault(); return false;
            }
        });
        """
        self._window.evaluate_js(js)

    def _build_navigation_html(self, url: str) -> str:
        return f"""
        <html>
        <head>
        <meta charset="utf-8">
        <style>
            * {{ margin:0; padding:0; box-sizing:border-box; }}
            body {{ background:#0f0f23; font-family:'Segoe UI',sans-serif; height:100vh; display:flex; flex-direction:column; }}
            .navbar {{
                background:#1a1a2e; border-bottom:1px solid #2a2a4a; display:flex;
                align-items:center; padding:8px 16px; gap:8px; height:48px;
            }}
            .navbar button {{
                background:#2a2a4a; color:#a78bfa; border:1px solid #3a3a5a;
                padding:6px 14px; border-radius:6px; cursor:pointer; font-size:14px;
            }}
            .navbar button:hover {{ background:#3a3a5a; }}
            .navbar button:disabled {{ opacity:0.4; cursor:default; }}
            .url-bar {{
                flex:1; background:#0f0f23; color:#a78bfa; border:1px solid #2a2a4a;
                padding:6px 12px; border-radius:6px; font-size:14px; font-family:'Consolas',monospace;
                min-width:0; user-select:none;
            }}
            .timer {{
                color:#7c3aed; font-size:14px; font-weight:bold; white-space:nowrap; margin-left:auto;
            }}
            iframe {{
                flex:1; border:none; width:100%;
            }}
        </style>
        </head>
        <body>
        <div class="navbar">
            <button onclick="goBack()" id="btnBack">&#x25C0; Voltar</button>
            <button onclick="goForward()" id="btnFwd">Avan&#xE7;ar &#x25B6;</button>
            <input class="url-bar" id="urlBar" value="{url}" readonly>
            <button onclick="goHome()">&#x2302; In&#xED;cio</button>
            <span class="timer" id="timerDisplay">00:00</span>
        </div>
        <iframe id="contentFrame" src="{url}" sandbox="allow-same-origin allow-scripts allow-forms allow-popups"></iframe>
        <script>
        function goBack() {{ window.pywebview.api.go_back(); }}
        function goForward() {{ window.pywebview.api.go_forward(); }}
        function goHome() {{ window.pywebview.api.go_home(); }}
        setInterval(() => {{ window.pywebview.api.tick_timer(); }}, 1000);
        </script>
        </body></html>
        """

    def go_back(self):
        if self._history_index > 0:
            self._history_index -= 1
            url = self._history[self._history_index]
            self._navigate_to_iframe(url)

    def go_forward(self):
        if self._history_index < len(self._history) - 1:
            self._history_index += 1
            url = self._history[self._history_index]
            self._navigate_to_iframe(url)

    def go_home(self):
        self.navigate_to(self.default_home)

    def navigate_to(self, url: str):
        if not url.startswith("http"):
            url = "https://" + url

        if not is_url_allowed(url, self.profile_id):
            self._show_blocked(url)
            db.insert_log(self.user_id, "navigation_blocked",
                          description=f"URL bloqueada: {url}")
            return

        db.insert_log(self.user_id, "navigation",
                      description=f"Navegação: {url}")

        self._history_index += 1
        if self._history_index < len(self._history):
            self._history = self._history[:self._history_index]
        self._history.append(url)
        self._current_url = url

        if self._window:
            js = f"document.getElementById('contentFrame').src = '{url}';"
            js += f"document.getElementById('urlBar').value = '{url}';"
            self._window.evaluate_js(js)

    def _navigate_to_iframe(self, url: str):
        if not is_url_allowed(url, self.profile_id):
            self._show_blocked(url)
            db.insert_log(self.user_id, "navigation_blocked",
                          description=f"URL bloqueada: {url}")
            return
        self._current_url = url
        if self._window:
            js = f"document.getElementById('contentFrame').src = '{url}';"
            js += f"document.getElementById('urlBar').value = '{url}';"
            self._window.evaluate_js(js)

    def _show_blocked(self, url: str):
        encoded = BLOCKED_PAGE.replace("'", "\\'").replace("\n", "\\n")
        if self._window:
            js = f"""
            document.getElementById('contentFrame').srcdoc = '{encoded}';
            document.getElementById('urlBar').value = 'BLOCKED: {url}';
            """
            self._window.evaluate_js(js)

    def tick_timer(self):
        if self._window:
            remaining = self._calculate_remaining_seconds()
            if remaining is not None:
                mins = remaining // 60
                secs = remaining % 60
                self._window.evaluate_js(
                    f"document.getElementById('timerDisplay').textContent = '{mins:02d}:{secs:02d}';"
                )
                if remaining <= 0:
                    self._window.evaluate_js("alert('Sua sessão expirou. O sistema será fechado.');")
                    threading.Timer(3.0, self.destroy).start()

    def _calculate_remaining_seconds(self) -> int | None:
        import time
        from datetime import datetime
        row = db.get_db().execute(
            "SELECT login_time, session_minutes_used FROM user_sessions WHERE user_id=? AND is_active=1 ORDER BY id DESC LIMIT 1",
            (self.user_id,)
        ).fetchone()
        if not row:
            return None

        login_time = datetime.fromisoformat(row["login_time"])
        elapsed = int(time.time() - login_time.timestamp())
        used = row["session_minutes_used"] or 0

        max_minutes = 0
        profile_row = db.get_db().execute(
            "SELECT max_session_minutes FROM access_profiles WHERE id=?",
            (self.profile_id,)
        ).fetchone()
        if profile_row and profile_row["max_session_minutes"]:
            max_minutes = profile_row["max_session_minutes"]

        if max_minutes == 0:
            return None
        remaining = (max_minutes * 60) - elapsed - used
        return max(0, remaining)
