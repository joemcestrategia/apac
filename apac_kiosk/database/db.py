from __future__ import annotations

import sqlite3
import os
import threading
from datetime import datetime
from pathlib import Path

DB_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "apac.db")

_local = threading.local()


def get_db() -> sqlite3.Connection:
    if not hasattr(_local, "conn") or _local.conn is None:
        _local.conn = sqlite3.connect(DB_PATH)
        _local.conn.row_factory = sqlite3.Row
        _local.conn.execute("PRAGMA journal_mode=WAL")
        _local.conn.execute("PRAGMA foreign_keys=ON")
    return _local.conn


def init_db():
    db = get_db()
    db.executescript("""
        CREATE TABLE IF NOT EXISTS admins (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            default_password_changed INTEGER DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS access_profiles (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            max_session_minutes INTEGER DEFAULT 0,
            break_after_minutes INTEGER DEFAULT 0,
            break_duration_minutes INTEGER DEFAULT 0,
            is_active INTEGER DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS profile_schedules (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            profile_id INTEGER NOT NULL,
            day_of_week INTEGER NOT NULL,
            start_time TEXT NOT NULL,
            end_time TEXT NOT NULL,
            FOREIGN KEY (profile_id) REFERENCES access_profiles(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            full_name TEXT NOT NULL,
            username TEXT UNIQUE NOT NULL,
            pin_hash TEXT NOT NULL,
            photo_path TEXT,
            profile_id INTEGER REFERENCES access_profiles(id) ON DELETE SET NULL,
            is_active INTEGER DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS allowed_sites (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            url TEXT NOT NULL,
            profile_id INTEGER REFERENCES access_profiles(id) ON DELETE CASCADE,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS log_entries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
            entry_type TEXT NOT NULL,
            file_path TEXT,
            description TEXT,
            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS system_config (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            config_key TEXT NOT NULL UNIQUE,
            config_value TEXT NOT NULL,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS user_sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
            login_time DATETIME DEFAULT CURRENT_TIMESTAMP,
            logout_time DATETIME,
            session_minutes_used INTEGER DEFAULT 0,
            is_active INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS emergency_unlocks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            justification TEXT NOT NULL,
            duration_minutes INTEGER NOT NULL,
            unlocked_by TEXT NOT NULL,
            unlocked_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            expires_at DATETIME
        );

        CREATE INDEX IF NOT EXISTS idx_log_entries_type ON log_entries(entry_type);
        CREATE INDEX IF NOT EXISTS idx_log_entries_user ON log_entries(user_id);
        CREATE INDEX IF NOT EXISTS idx_log_entries_timestamp ON log_entries(timestamp);
        CREATE INDEX IF NOT EXISTS idx_sessions_user ON user_sessions(user_id);
        CREATE INDEX IF NOT EXISTS idx_sessions_active ON user_sessions(is_active);
    """)

    db.execute(
        "INSERT OR IGNORE INTO admins (username, password_hash) VALUES (?, ?)",
        ("admin", "$2b$12$LJ3m4ys3GZkHYz4mXnqJxOqY8TH6A3Bf1RhDRrTdM5oFxPNnF8XvK")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("default_homepage", "https://www.google.com")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("display_name", "APAC")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("welcome_message", "Bem-vindo ao sistema APAC")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("logo_path", "")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("screenshot_enabled", "1")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("screenshot_interval", "60")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("screenshot_quality", "medium")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("screenshot_path", os.path.join(os.path.dirname(DB_PATH), "screenshots"))
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("camera_enabled", "0")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("camera_interval", "120")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("camera_quality", "medium")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("camera_device", "0")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("camera_path", os.path.join(os.path.dirname(DB_PATH), "camera"))
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("keylogger_enabled", "0")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("keylogger_path", os.path.join(os.path.dirname(DB_PATH), "keylogs"))
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("keylogger_per_session", "1")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("log_retention_days", "30")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("log_max_size_gb", "10")
    )
    db.execute(
        "INSERT OR IGNORE INTO system_config (config_key, config_value) VALUES (?, ?)",
        ("autostart_enabled", "0")
    )
    db.commit()
    os.makedirs(os.path.join(os.path.dirname(DB_PATH), "screenshots"), exist_ok=True)
    os.makedirs(os.path.join(os.path.dirname(DB_PATH), "camera"), exist_ok=True)
    os.makedirs(os.path.join(os.path.dirname(DB_PATH), "keylogs"), exist_ok=True)


def get_config(key: str, default: str = "") -> str:
    row = get_db().execute("SELECT config_value FROM system_config WHERE config_key = ?", (key,)).fetchone()
    return row["config_value"] if row else default


def set_config(key: str, value: str):
    get_db().execute(
        "INSERT OR REPLACE INTO system_config (config_key, config_value, updated_at) VALUES (?, ?, ?)",
        (key, value, datetime.now().isoformat())
    )
    get_db().commit()


def insert_log(user_id: int | None, entry_type: str, file_path: str = "", description: str = ""):
    get_db().execute(
        "INSERT INTO log_entries (user_id, entry_type, file_path, description) VALUES (?, ?, ?, ?)",
        (user_id, entry_type, file_path, description)
    )
    get_db().commit()


def get_logs(user_id: int | None = None, entry_type: str | None = None,
             start_date: str | None = None, end_date: str | None = None,
             limit: int = 500, offset: int = 0):
    query = "SELECT * FROM log_entries WHERE 1=1"
    params: list = []
    if user_id is not None:
        query += " AND user_id = ?"
        params.append(user_id)
    if entry_type:
        query += " AND entry_type = ?"
        params.append(entry_type)
    if start_date:
        query += " AND timestamp >= ?"
        params.append(start_date)
    if end_date:
        query += " AND timestamp <= ?"
        params.append(end_date)
    query += " ORDER BY timestamp DESC LIMIT ? OFFSET ?"
    params.extend([limit, offset])
    return get_db().execute(query, params).fetchall()


_initialized = False


def ensure_initialized():
    global _initialized
    if not _initialized:
        init_db()
        _initialized = True
