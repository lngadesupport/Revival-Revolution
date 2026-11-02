"""SQLite persistence for Ranked identities, queue entries, and match assignments.

The database stores only token hashes. Plaintext player tokens are returned once
at registration and must be stored by the launcher in its private config area.
"""
from __future__ import annotations

import hashlib
import hmac
import secrets
import sqlite3
import time
import uuid
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator


class RankedStore:
    def __init__(self, database: str | Path = "ranked.sqlite3", *, clock=time.time):
        self.database = str(database)
        self.clock = clock
        if self.database != ":memory:":
            Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database, timeout=10, isolation_level=None)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA busy_timeout = 10000")
        return connection

    @contextmanager
    def transaction(self) -> Iterator[sqlite3.Connection]:
        connection = self._connect()
        try:
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def _initialize(self) -> None:
        with self._connect() as db:
            db.execute("PRAGMA journal_mode = WAL")
            db.executescript("""
                CREATE TABLE IF NOT EXISTS players (
                    player_id TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    token_hash TEXT NOT NULL UNIQUE,
                    rating INTEGER NOT NULL DEFAULT 1000 CHECK(rating >= 0),
                    created_at REAL NOT NULL,
                    last_seen REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS queue (
                    player_id TEXT PRIMARY KEY REFERENCES players(player_id) ON DELETE CASCADE,
                    region TEXT NOT NULL,
                    joined_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS matches (
                    match_id TEXT PRIMARY KEY,
                    player_one TEXT NOT NULL REFERENCES players(player_id),
                    player_two TEXT NOT NULL REFERENCES players(player_id),
                    rating_one INTEGER NOT NULL,
                    rating_two INTEGER NOT NULL,
                    region TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'assigned',
                    created_at REAL NOT NULL,
                    CHECK(player_one <> player_two)
                );
                CREATE INDEX IF NOT EXISTS queue_region_time ON queue(region, joined_at);
                CREATE INDEX IF NOT EXISTS matches_players ON matches(player_one, player_two, created_at);
            """)

    @staticmethod
    def _token_hash(token: str) -> str:
        return hashlib.sha256(token.encode("utf-8")).hexdigest()

    @staticmethod
    def _display_name(name: str) -> str:
        if not isinstance(name, str):
            raise ValueError("display_name must be a string")
        name = " ".join(name.strip().split())
        if not name or len(name) > 32 or any(ord(ch) < 32 for ch in name):
            raise ValueError("display_name must contain 1 to 32 printable characters")
        return name

    def register(self, display_name: str) -> dict:
        display_name = self._display_name(display_name)
        player_id = str(uuid.uuid4())
        token = secrets.token_urlsafe(32)
        now = self.clock()
        with self.transaction() as db:
            db.execute(
                "INSERT INTO players(player_id,display_name,token_hash,created_at,last_seen) VALUES(?,?,?,?,?)",
                (player_id, display_name, self._token_hash(token), now, now),
            )
        return {"player_id": player_id, "display_name": display_name, "rating": 1000, "token": token}

    def authenticate(self, token: str) -> dict | None:
        if not isinstance(token, str) or not 20 <= len(token) <= 128:
            return None
        digest = self._token_hash(token)
        with self.transaction() as db:
            row = db.execute("SELECT player_id,display_name,rating FROM players WHERE token_hash=?", (digest,)).fetchone()
            if row is None:
                return None
            now = self.clock()
            db.execute("UPDATE players SET last_seen=? WHERE player_id=?", (now, row["player_id"]))
            return {**dict(row), "last_seen": now}

    def rotate_token(self, player_id: str) -> dict:
        """Replace a player's bearer token; the previous token stops working immediately."""
        token = secrets.token_urlsafe(32)
        now = self.clock()
        with self.transaction() as db:
            result = db.execute(
                "UPDATE players SET token_hash=?,last_seen=? WHERE player_id=?",
                (self._token_hash(token), now, player_id),
            )
            if result.rowcount == 0:
                raise ValueError("unknown player")
        return {"player_id": player_id, "token": token}

    def join_queue(self, player_id: str, region: str = "global", *, base_window: int = 100, step: int = 50, widen_seconds: int = 10) -> dict | None:
        region = str(region).strip().lower()
        if not region or len(region) > 24 or not all(c.isalnum() or c in "-_" for c in region):
            raise ValueError("invalid region")
        now = self.clock()
        with self.transaction() as db:
            player = db.execute("SELECT rating FROM players WHERE player_id=?", (player_id,)).fetchone()
            if player is None:
                raise ValueError("unknown player")
            active = db.execute(
                "SELECT 1 FROM matches WHERE status='assigned' AND (player_one=? OR player_two=?)",
                (player_id, player_id),
            ).fetchone()
            if active:
                raise ValueError("player already has an assigned match")
            existing = db.execute("SELECT region,joined_at FROM queue WHERE player_id=?", (player_id,)).fetchone()
            if existing:
                if existing["region"] != region:
                    raise ValueError("player is already queued in another region")
                joined_at = existing["joined_at"]
            else:
                joined_at = now
                db.execute("INSERT INTO queue(player_id,region,joined_at) VALUES(?,?,?)", (player_id, region, now))
            own_rating = int(player["rating"])
            wait = max(0, now - joined_at)
            candidates = db.execute("""
                SELECT q.player_id,q.joined_at,p.rating,p.display_name
                FROM queue q JOIN players p USING(player_id)
                WHERE q.region=? AND q.player_id<>?
                ORDER BY ABS(p.rating-?), q.joined_at, q.player_id
            """, (region, player_id, own_rating)).fetchall()
            # Use the longer wait of the two players so an older queued player
            # can receive the advertised widening even when a newcomer arrives.
            eligible = []
            for candidate in candidates:
                candidate_wait = max(0, now - float(candidate["joined_at"]))
                effective_wait = max(wait, candidate_wait)
                window = base_window + int(effective_wait // widen_seconds) * step
                if abs(int(candidate["rating"]) - own_rating) <= window:
                    eligible.append(candidate)
            if not eligible:
                return None
            opponent = eligible[0]
            first_id, second_id = sorted((player_id, opponent["player_id"]))
            first = db.execute("SELECT rating FROM players WHERE player_id=?", (first_id,)).fetchone()
            second = db.execute("SELECT rating FROM players WHERE player_id=?", (second_id,)).fetchone()
            match_id = str(uuid.uuid4())
            db.execute(
                "INSERT INTO matches(match_id,player_one,player_two,rating_one,rating_two,region,status,created_at) VALUES(?,?,?,?,?,?,?,?)",
                (match_id, first_id, second_id, int(first["rating"]), int(second["rating"]), region, "assigned", now),
            )
            db.execute("DELETE FROM queue WHERE player_id IN (?,?)", (first_id, second_id))
            return self._match_payload(db, match_id, player_id)

    @staticmethod
    def _match_payload(db: sqlite3.Connection, match_id: str, player_id: str) -> dict | None:
        row = db.execute("SELECT * FROM matches WHERE match_id=?", (match_id,)).fetchone()
        if row is None or player_id not in (row["player_one"], row["player_two"]):
            return None
        own = row["player_one"] == player_id
        opponent_id = row["player_two"] if own else row["player_one"]
        opponent = db.execute("SELECT display_name,rating FROM players WHERE player_id=?", (opponent_id,)).fetchone()
        return {
            "match_id": row["match_id"], "status": row["status"], "region": row["region"],
            "player_slot": 0 if own else 1, "opponent": dict(opponent),
            "created_at": row["created_at"],
        }

    def current_match(self, player_id: str) -> dict | None:
        with self._connect() as db:
            row = db.execute("""
                SELECT match_id FROM matches
                WHERE status='assigned' AND (player_one=? OR player_two=?)
                ORDER BY created_at DESC LIMIT 1
            """, (player_id, player_id)).fetchone()
            return self._match_payload(db, row["match_id"], player_id) if row else None

    def leave_queue(self, player_id: str) -> bool:
        with self.transaction() as db:
            result = db.execute("DELETE FROM queue WHERE player_id=?", (player_id,))
            return result.rowcount > 0

    def queue_status(self, player_id: str) -> dict:
        with self._connect() as db:
            row = db.execute("SELECT region,joined_at FROM queue WHERE player_id=?", (player_id,)).fetchone()
            if not row:
                return {"queued": False}
            position = db.execute(
                "SELECT COUNT(*) FROM queue WHERE region=? AND (joined_at<? OR (joined_at=? AND player_id<=?))",
                (row["region"], row["joined_at"], row["joined_at"], player_id),
            ).fetchone()[0]
            return {"queued": True, "region": row["region"], "joined_at": row["joined_at"], "position": position}

    def player_profile(self, player_id: str) -> dict:
        with self._connect() as db:
            row = db.execute("SELECT player_id,display_name,rating,created_at FROM players WHERE player_id=?", (player_id,)).fetchone()
            if row is None:
                raise ValueError("unknown player")
            return dict(row)
