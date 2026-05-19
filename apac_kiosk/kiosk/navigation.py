from __future__ import annotations

import re
import fnmatch
from urllib.parse import urlparse

from ..database import db


def matches_pattern(url: str, pattern: str) -> bool:
    if not pattern:
        return False
    pattern = pattern.strip().lower()
    url = url.strip().lower()

    if pattern.startswith("*."):
        domain = pattern[2:]
        try:
            parsed = urlparse(url if "://" in url else f"http://{url}")
            host = parsed.hostname or parsed.path
            return host == domain or host.endswith("." + domain)
        except Exception:
            return fnmatch.fnmatch(url, pattern)

    if "*" in pattern:
        return fnmatch.fnmatch(url, pattern)

    if pattern in url:
        return True

    try:
        parsed_pattern = urlparse(pattern if "://" in pattern else f"http://{pattern}")
        pattern_host = parsed_pattern.hostname or pattern
        parsed_url = urlparse(url if "://" in url else f"http://{url}")
        url_host = parsed_url.hostname or url
        return url_host == pattern_host or url.endswith(pattern)
    except Exception:
        return url == pattern


def is_url_allowed(url: str, profile_id: int | None = None) -> bool:
    global_sites = db.get_db().execute(
        "SELECT url FROM allowed_sites WHERE profile_id IS NULL"
    ).fetchall()

    for row in global_sites:
        if matches_pattern(url, row["url"]):
            return True

    if profile_id is not None:
        profile_sites = db.get_db().execute(
            "SELECT url FROM allowed_sites WHERE profile_id = ?", (profile_id,)
        ).fetchall()
        for row in profile_sites:
            if matches_pattern(url, row["url"]):
                return True

        parent_global = db.get_db().execute(
            "SELECT url FROM allowed_sites WHERE profile_id IS NULL"
        ).fetchall()
        for row in parent_global:
            if matches_pattern(url, row["url"]):
                return True

    return False


def extract_domain(url: str) -> str:
    try:
        if "://" not in url:
            url = "http://" + url
        parsed = urlparse(url)
        return parsed.hostname or url
    except Exception:
        return url


def test_url(url: str) -> tuple[bool, str]:
    import urllib.request
    try:
        req = urllib.request.Request(
            url if "://" in url else f"http://{url}",
            headers={"User-Agent": "Mozilla/5.0"}
        )
        urllib.request.urlopen(req, timeout=5)
        return True, f"URL acessível: {url}"
    except Exception as e:
        return False, f"URL inacessível: {str(e)}"


def get_allowed_sites(profile_id: int | None = None) -> list[dict]:
    if profile_id is not None:
        return db.get_db().execute(
            "SELECT * FROM allowed_sites WHERE profile_id = ? ORDER BY url",
            (profile_id,)
        ).fetchall()
    else:
        return db.get_db().execute(
            "SELECT * FROM allowed_sites WHERE profile_id IS NULL ORDER BY url"
        ).fetchall()


def add_allowed_site(profile_id: int | None, url: str, notes: str = ""):
    existing = db.get_db().execute(
        "SELECT id FROM allowed_sites WHERE url = ? AND profile_id IS ?", (url, profile_id)
    ).fetchone()
    if existing:
        return False
    db.get_db().execute(
        "INSERT INTO allowed_sites (url, profile_id, notes) VALUES (?, ?, ?)",
        (url, profile_id, notes)
    )
    db.get_db().commit()
    return True


def remove_allowed_site(site_id: int):
    db.get_db().execute("DELETE FROM allowed_sites WHERE id = ?", (site_id,))
    db.get_db().commit()


def import_sites_from_file(filepath: str, profile_id: int | None) -> int:
    count = 0
    with open(filepath, "r", encoding="utf-8") as f:
        for line in f:
            url = line.strip()
            if url and not url.startswith("#"):
                if add_allowed_site(profile_id, url):
                    count += 1
    return count


def export_sites_to_file(filepath: str, profile_id: int | None):
    sites = get_allowed_sites(profile_id)
    with open(filepath, "w", encoding="utf-8") as f:
        for site in sites:
            f.write(site["url"] + "\n")
