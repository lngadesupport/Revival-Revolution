"""Persistent social/profile features for Revival Revolution.

Clans are intentionally not implemented: there is no verified in-game clan
system or integration point yet. This module only stores features that the
current account backend can support independently.
"""
from __future__ import annotations

import base64
import re
import sqlite3
import time
from pathlib import Path


class SocialStore:
    def __init__(self, database: str | Path, *, clock=time.time):
        self.database = str(database)
        self.clock = clock
        if self.database != ":memory:":
            Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    def _connect(self):
        db = sqlite3.connect(self.database, timeout=10, isolation_level=None)
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        db.execute("PRAGMA busy_timeout=10000")
        return db

    def _initialize(self):
        with self._connect() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS player_profiles (
                    player_id TEXT PRIMARY KEY REFERENCES players(player_id) ON DELETE CASCADE,
                    avatar_data TEXT,
                    updated_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS friendships (
                    requester_id TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
                    addressee_id TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
                    status TEXT NOT NULL CHECK(status IN ('pending','accepted')),
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    PRIMARY KEY(requester_id, addressee_id),
                    CHECK(requester_id <> addressee_id)
                );
                CREATE INDEX IF NOT EXISTS friendships_addressee_status
                    ON friendships(addressee_id, status);
                CREATE INDEX IF NOT EXISTS friendships_requester_status
                    ON friendships(requester_id, status);
                CREATE TABLE IF NOT EXISTS blocked_players (
                    blocker_id TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
                    blocked_id TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
                    created_at REAL NOT NULL,
                    PRIMARY KEY(blocker_id, blocked_id),
                    CHECK(blocker_id <> blocked_id)
                );
            """)

    @staticmethod
    def _nickname(value):
        if not isinstance(value, str):
            raise ValueError("display_name must be a string")
        value = " ".join(value.strip().split())
        if not value or len(value) > 32 or any(ord(ch) < 32 for ch in value):
            raise ValueError("display_name must contain 1 to 32 printable characters")
        return value

    @staticmethod
    def _avatar(value):
        if value is None or value == "":
            return None
        if not isinstance(value, str) or len(value) > 350000:
            raise ValueError("avatar_data must be a base64 image up to 256 KiB")
        match = re.fullmatch(r"data:image/(png|jpeg|webp);base64,([A-Za-z0-9+/=]+)", value)
        if not match:
            raise ValueError("avatar_data must be a PNG, JPEG, or WebP data URL")
        try:
            raw = base64.b64decode(match.group(2), validate=True)
        except (ValueError, base64.binascii.Error):
            raise ValueError("invalid avatar_data")
        if not raw or len(raw) > 256 * 1024:
            raise ValueError("avatar image must be between 1 byte and 256 KiB")
        image_type = match.group(1)
        valid = (
            (image_type == "png" and raw.startswith(b"\x89PNG\r\n\x1a\n"))
            or (image_type == "jpeg" and raw.startswith(b"\xff\xd8\xff"))
            or (image_type == "webp" and len(raw) >= 12 and raw[:4] == b"RIFF" and raw[8:12] == b"WEBP")
        )
        if not valid:
            raise ValueError("avatar data does not match its declared image type")
        return value

    def profile(self, player_id):
        with self._connect() as db:
            row = db.execute("""
                SELECT p.player_id,p.display_name,p.rating,
                       COALESCE(s.avatar_data,'') AS avatar_data,
                       CASE WHEN (? - p.last_seen) <= 90 THEN 1 ELSE 0 END AS online
                FROM players p LEFT JOIN player_profiles s USING(player_id)
                WHERE p.player_id=?
            """, (self.clock(), player_id)).fetchone()
            if row is None:
                raise ValueError("unknown player")
            return dict(row)

    def avatar(self, player_id):
        with self._connect() as db:
            row = db.execute(
                "SELECT avatar_data FROM player_profiles WHERE player_id=?",
                (player_id,),
            ).fetchone()
            if row is None:
                # Distinguish an existing account without a photo from an unknown ID.
                exists = db.execute("SELECT 1 FROM players WHERE player_id=?", (player_id,)).fetchone()
                if not exists:
                    raise ValueError("unknown player")
                return {"player_id": player_id, "avatar_data": ""}
            return {"player_id": player_id, "avatar_data": row["avatar_data"] or ""}

    def update_profile(self, player_id, display_name=None, avatar_data=None, update_avatar=False):
        name = self._nickname(display_name) if display_name is not None else None
        avatar = self._avatar(avatar_data) if update_avatar else None
        with self._connect() as db:
            db.execute("BEGIN IMMEDIATE")
            try:
                exists = db.execute("SELECT 1 FROM players WHERE player_id=?", (player_id,)).fetchone()
                if not exists:
                    raise ValueError("unknown player")
                if name is not None:
                    db.execute("UPDATE players SET display_name=? WHERE player_id=?", (name, player_id))
                if update_avatar:
                    db.execute("""
                        INSERT INTO player_profiles(player_id,avatar_data,updated_at) VALUES(?,?,?)
                        ON CONFLICT(player_id) DO UPDATE SET
                          avatar_data=excluded.avatar_data, updated_at=excluded.updated_at
                    """, (player_id, avatar, self.clock()))
                db.commit()
            except Exception:
                db.rollback()
                raise
        return self.profile(player_id)

    def search_players(self, query, limit=20):
        if not isinstance(query, str) or len(query.strip()) < 2:
            raise ValueError("query must contain at least 2 characters")
        query = query.strip()[:32]
        with self._connect() as db:
            rows = db.execute("""
                SELECT p.player_id,p.display_name,p.rating,
                       CASE WHEN s.avatar_data IS NOT NULL AND s.avatar_data <> '' THEN 1 ELSE 0 END AS has_avatar,
                       CASE WHEN (? - p.last_seen) <= 90 THEN 1 ELSE 0 END AS online
                FROM players p LEFT JOIN player_profiles s USING(player_id)
                WHERE p.display_name LIKE ? COLLATE NOCASE
                ORDER BY p.display_name COLLATE NOCASE, p.player_id LIMIT ?
            """, (self.clock(), f"%{query}%", max(1, min(int(limit), 20)))).fetchall()
            return [dict(row) for row in rows]

    def leaderboard(self, limit=100):
        with self._connect() as db:
            rows = db.execute("""
                SELECT p.player_id,p.display_name,p.rating,
                       CASE WHEN s.avatar_data IS NOT NULL AND s.avatar_data <> '' THEN 1 ELSE 0 END AS has_avatar,
                       CASE WHEN (? - p.last_seen) <= 90 THEN 1 ELSE 0 END AS online
                FROM players p LEFT JOIN player_profiles s USING(player_id)
                ORDER BY p.rating DESC, p.created_at ASC, p.player_id ASC LIMIT ?
            """, (self.clock(), max(1, min(int(limit), 100)))).fetchall()
            return [{"position": index + 1, **dict(row)} for index, row in enumerate(rows)]

    def request_friend(self, requester_id, addressee_id):
        if not isinstance(addressee_id, str) or not addressee_id.strip():
            raise ValueError("addressee_id is required")
        addressee_id = addressee_id.strip()
        if requester_id == addressee_id:
            raise ValueError("cannot add yourself")
        now = self.clock()
        with self._connect() as db:
            db.execute("BEGIN IMMEDIATE")
            try:
                if not db.execute("SELECT 1 FROM players WHERE player_id=?", (addressee_id,)).fetchone():
                    raise ValueError("player_not_found")
                blocked = db.execute("""
                    SELECT 1 FROM blocked_players
                    WHERE (blocker_id=? AND blocked_id=?) OR (blocker_id=? AND blocked_id=?)
                """, (requester_id, addressee_id, addressee_id, requester_id)).fetchone()
                if blocked:
                    raise ValueError("friend_request_blocked")
                existing = db.execute("""
                    SELECT requester_id,addressee_id,status FROM friendships
                    WHERE (requester_id=? AND addressee_id=?)
                       OR (requester_id=? AND addressee_id=?)
                """, (requester_id, addressee_id, addressee_id, requester_id)).fetchone()
                if existing:
                    if existing["status"] == "accepted":
                        raise ValueError("already_friends")
                    if existing["requester_id"] == addressee_id:
                        db.execute("""
                            UPDATE friendships SET status='accepted',updated_at=?
                            WHERE requester_id=? AND addressee_id=?
                        """, (now, addressee_id, requester_id))
                        db.commit()
                        return {"status": "accepted", "player_id": addressee_id}
                    raise ValueError("request_already_pending")
                db.execute("""
                    INSERT INTO friendships(requester_id,addressee_id,status,created_at,updated_at)
                    VALUES(?,?,'pending',?,?)
                """, (requester_id, addressee_id, now, now))
                db.commit()
                return {"status": "pending", "player_id": addressee_id}
            except Exception:
                db.rollback()
                raise

    def accept_friend(self, player_id, requester_id):
        with self._connect() as db:
            result = db.execute("""
                UPDATE friendships SET status='accepted',updated_at=?
                WHERE requester_id=? AND addressee_id=? AND status='pending'
            """, (self.clock(), requester_id, player_id))
            if result.rowcount == 0:
                raise ValueError("pending_request_not_found")
        return {"status": "accepted", "player_id": requester_id}

    def remove_friend(self, player_id, other_id):
        with self._connect() as db:
            result = db.execute("""
                DELETE FROM friendships
                WHERE (requester_id=? AND addressee_id=?)
                   OR (requester_id=? AND addressee_id=?)
            """, (player_id, other_id, other_id, player_id))
            return result.rowcount > 0

    def friends(self, player_id):
        with self._connect() as db:
            rows = db.execute("""
                SELECT f.requester_id,f.addressee_id,f.status,f.created_at,
                       p.player_id,p.display_name,p.rating,p.last_seen,
                       CASE WHEN s.avatar_data IS NOT NULL AND s.avatar_data <> '' THEN 1 ELSE 0 END AS has_avatar
                FROM friendships f
                JOIN players p ON p.player_id =
                    CASE WHEN f.requester_id=? THEN f.addressee_id ELSE f.requester_id END
                LEFT JOIN player_profiles s ON s.player_id=p.player_id
                WHERE (f.requester_id=? OR f.addressee_id=?)
                ORDER BY f.status, p.display_name COLLATE NOCASE
            """, (player_id, player_id, player_id)).fetchall()
            result = {"friends": [], "incoming": [], "outgoing": []}
            for row in rows:
                item = {"player_id": row["player_id"], "display_name": row["display_name"],
                        "rating": row["rating"], "has_avatar": bool(row["has_avatar"]),
                        "online": self.clock() - float(row["last_seen"]) <= 90}
                if row["status"] == "accepted":
                    result["friends"].append(item)
                elif row["addressee_id"] == player_id:
                    result["incoming"].append(item)
                else:
                    result["outgoing"].append(item)
            return result


    def decline_friend(self, player_id, requester_id):
        with self._connect() as db:
            result = db.execute("""
                DELETE FROM friendships
                WHERE requester_id=? AND addressee_id=? AND status='pending'
            """, (requester_id, player_id))
            if result.rowcount == 0:
                raise ValueError("pending_request_not_found")
        return {"status": "declined", "player_id": requester_id}

    def block_player(self, blocker_id, blocked_id):
        if not isinstance(blocked_id, str) or not blocked_id.strip():
            raise ValueError("player_id is required")
        blocked_id = blocked_id.strip()
        if blocker_id == blocked_id:
            raise ValueError("cannot block yourself")
        now = self.clock()
        with self._connect() as db:
            db.execute("BEGIN IMMEDIATE")
            try:
                if not db.execute("SELECT 1 FROM players WHERE player_id=?", (blocked_id,)).fetchone():
                    raise ValueError("player_not_found")
                db.execute("""
                    INSERT OR IGNORE INTO blocked_players(blocker_id,blocked_id,created_at)
                    VALUES(?,?,?)
                """, (blocker_id, blocked_id, now))
                db.execute("""
                    DELETE FROM friendships
                    WHERE (requester_id=? AND addressee_id=?)
                       OR (requester_id=? AND addressee_id=?)
                """, (blocker_id, blocked_id, blocked_id, blocker_id))
                db.commit()
            except Exception:
                db.rollback()
                raise
        return {"blocked": True, "player_id": blocked_id}

    def unblock_player(self, blocker_id, blocked_id):
        with self._connect() as db:
            result = db.execute("""
                DELETE FROM blocked_players WHERE blocker_id=? AND blocked_id=?
            """, (blocker_id, blocked_id))
            return {"unblocked": result.rowcount > 0, "player_id": blocked_id}

    def blocked_players(self, blocker_id):
        with self._connect() as db:
            rows = db.execute("""
                SELECT p.player_id,p.display_name,b.created_at
                FROM blocked_players b JOIN players p ON p.player_id=b.blocked_id
                WHERE b.blocker_id=?
                ORDER BY p.display_name COLLATE NOCASE
            """, (blocker_id,)).fetchall()
            return [dict(row) for row in rows]
