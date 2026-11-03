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
                CREATE TABLE IF NOT EXISTS private_rooms (
                    room_code TEXT PRIMARY KEY,
                    host_player TEXT NOT NULL REFERENCES players(player_id),
                    guest_player TEXT REFERENCES players(player_id),
                    region TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'waiting',
                    match_id TEXT UNIQUE REFERENCES matches(match_id),
                    created_at REAL NOT NULL,
                    expires_at REAL NOT NULL,
                    CHECK(guest_player IS NULL OR host_player <> guest_player)
                );
                CREATE INDEX IF NOT EXISTS queue_region_time ON queue(region, joined_at);
                CREATE INDEX IF NOT EXISTS matches_players ON matches(player_one, player_two, created_at);
                CREATE TABLE IF NOT EXISTS room_join_attempts (
                    player_id TEXT PRIMARY KEY REFERENCES players(player_id) ON DELETE CASCADE,
                    window_started REAL NOT NULL,
                    attempts INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS private_rooms_host_status ON private_rooms(host_player, status);
            """)
            columns = {row["name"] for row in db.execute("PRAGMA table_info(matches)")}
            migrations = {"accepted_one":"INTEGER NOT NULL DEFAULT 0","accepted_two":"INTEGER NOT NULL DEFAULT 0","completed_one":"INTEGER NOT NULL DEFAULT 0","completed_two":"INTEGER NOT NULL DEFAULT 0","expires_at":"REAL"}
            for name, declaration in migrations.items():
                if name not in columns:
                    db.execute(f"ALTER TABLE matches ADD COLUMN {name} {declaration}")
            db.execute("UPDATE matches SET expires_at=created_at+90 WHERE expires_at IS NULL")
            room_columns = {row["name"] for row in db.execute("PRAGMA table_info(private_rooms)")}
            if "connection_code" not in room_columns:
                db.execute("ALTER TABLE private_rooms ADD COLUMN connection_code TEXT")

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
                "SELECT 1 FROM matches WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?)",
                (player_id, player_id),
            ).fetchone()
            if active:
                raise ValueError("player already has an assigned match")
            open_room = db.execute(
                "SELECT 1 FROM private_rooms WHERE host_player=? AND status='waiting' AND expires_at>?",
                (player_id, now),
            ).fetchone()
            if open_room:
                raise ValueError("player already owns an open private room")
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
                "INSERT INTO matches(match_id,player_one,player_two,rating_one,rating_two,region,status,created_at,expires_at) VALUES(?,?,?,?,?,?,?,?,?)",
                (match_id, first_id, second_id, int(first["rating"]), int(second["rating"]), region, "assigned", now, now + 90),
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
        room = db.execute(
            "SELECT status,host_player,guest_player FROM private_rooms WHERE match_id=?",
            (match_id,),
        ).fetchone()
        return {
            "match_id": row["match_id"], "status": row["status"], "region": row["region"],
            "player_slot": 0 if own else 1, "opponent": dict(opponent),
            "created_at": row["created_at"],
            "expires_at": row["expires_at"],
            "accepted": bool(row["accepted_one"] if own else row["accepted_two"]),
            "opponent_accepted": bool(row["accepted_two"] if own else row["accepted_one"]),
            "room_role": ("host" if room["host_player"] == player_id else "guest") if room else None,
            "room_status": room["status"] if room else None,
            "connection_code": room["connection_code"] if room else None,
        }

    @staticmethod
    def _expire_matches(db: sqlite3.Connection, now: float) -> None:
        db.execute("UPDATE matches SET status='expired' WHERE status IN ('assigned','ready') AND expires_at<=?", (now,))
        db.execute("UPDATE private_rooms SET status='expired' WHERE status IN ('matched','starting') AND match_id IN (SELECT match_id FROM matches WHERE status='expired')")

    def accept_match(self, player_id: str) -> dict:
        now = self.clock()
        with self.transaction() as db:
            self._expire_matches(db, now)
            row = db.execute("SELECT * FROM matches WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?) ORDER BY created_at DESC LIMIT 1", (player_id, player_id)).fetchone()
            if row is None: raise ValueError("no active match to accept")
            own = row["player_one"] == player_id
            column = "accepted_one" if own else "accepted_two"
            db.execute(f"UPDATE matches SET {column}=1 WHERE match_id=?", (row["match_id"],))
            updated = db.execute("SELECT * FROM matches WHERE match_id=?", (row["match_id"],)).fetchone()
            if updated["accepted_one"] and updated["accepted_two"] and row["status"] == "assigned":
                db.execute("UPDATE matches SET status='ready',expires_at=? WHERE match_id=? AND status='assigned'", (now + 7200, row["match_id"]))
            return self._match_payload(db, row["match_id"], player_id)

    def decline_match(self, player_id: str) -> dict:
        now = self.clock()
        with self.transaction() as db:
            self._expire_matches(db, now)
            row = db.execute("SELECT match_id FROM matches WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?) ORDER BY created_at DESC LIMIT 1", (player_id, player_id)).fetchone()
            if row is None: raise ValueError("no active match to decline")
            db.execute("UPDATE matches SET status='declined' WHERE match_id=?", (row["match_id"],))
            db.execute("UPDATE private_rooms SET status='declined' WHERE match_id=?", (row["match_id"],))
            return {"match_id": row["match_id"], "status": "declined"}

    def confirm_match_finished(self, player_id: str) -> dict:
        now = self.clock()
        with self.transaction() as db:
            self._expire_matches(db, now)
            row = db.execute("SELECT * FROM matches WHERE status='ready' AND (player_one=? OR player_two=?) ORDER BY created_at DESC LIMIT 1", (player_id, player_id)).fetchone()
            if row is None: raise ValueError("no ready match to finish")
            own = row["player_one"] == player_id
            column = "completed_one" if own else "completed_two"
            opponent_column = "completed_two" if own else "completed_one"
            db.execute(f"UPDATE matches SET {column}=1 WHERE match_id=?", (row["match_id"],))
            updated = db.execute("SELECT * FROM matches WHERE match_id=?", (row["match_id"],)).fetchone()
            finished = bool(updated["completed_one"] and updated["completed_two"])
            if finished:
                db.execute("UPDATE matches SET status='completed' WHERE match_id=?", (row["match_id"],))
                db.execute("UPDATE private_rooms SET status='completed' WHERE match_id=?", (row["match_id"],))
            return {"match_id": row["match_id"], "status": "completed" if finished else "awaiting_opponent_confirmation", "opponent_confirmed": bool(updated[opponent_column]), "rating_changed": False}

    def _match_queued_in_region(
        self,
        db: sqlite3.Connection,
        region: str,
        now: float,
        *,
        base_window: int = 100,
        step: int = 50,
        widen_seconds: int = 10,
    ) -> None:
        """Pair eligible queued players during polling, not only on new arrivals."""
        rows = db.execute(
            "SELECT q.player_id,q.joined_at,p.rating FROM queue q "
            "JOIN players p USING(player_id) WHERE q.region=? "
            "ORDER BY q.joined_at,q.player_id",
            (region,),
        ).fetchall()
        waiting = [
            {"player_id": row["player_id"], "joined_at": float(row["joined_at"]), "rating": int(row["rating"])}
            for row in rows
        ]

        while len(waiting) >= 2:
            best = None
            for index, first in enumerate(waiting):
                for second in waiting[index + 1:]:
                    waited = max(0.0, now - min(first["joined_at"], second["joined_at"]))
                    window = base_window + int(waited // widen_seconds) * step
                    difference = abs(first["rating"] - second["rating"])
                    if difference > window:
                        continue
                    ids = sorted((first["player_id"], second["player_id"]))
                    score = (
                        difference,
                        max(first["joined_at"], second["joined_at"]),
                        min(first["joined_at"], second["joined_at"]),
                        ids[0],
                        ids[1],
                    )
                    if best is None or score < best[0]:
                        best = (score, first, second)

            if best is None:
                break

            first, second = best[1], best[2]
            first_id, second_id = sorted((first["player_id"], second["player_id"]))
            first_rating = first["rating"] if first["player_id"] == first_id else second["rating"]
            second_rating = second["rating"] if second["player_id"] == second_id else first["rating"]
            match_id = str(uuid.uuid4())
            db.execute(
                "INSERT INTO matches(match_id,player_one,player_two,rating_one,rating_two,region,status,created_at,expires_at) "
                "VALUES(?,?,?,?,?,?,?,?,?)",
                (match_id, first_id, second_id, first_rating, second_rating, region, "assigned", now, now + 90),
            )
            db.execute("DELETE FROM queue WHERE player_id IN (?,?)", (first_id, second_id))
            waiting = [
                entry for entry in waiting
                if entry["player_id"] not in (first_id, second_id)
            ]

    def current_match(self, player_id: str) -> dict | None:
        with self.transaction() as db:
            now = self.clock()
            self._expire_matches(db, now)
            queued = db.execute("SELECT region FROM queue WHERE player_id=?", (player_id,)).fetchone()
            if queued:
                self._match_queued_in_region(db, queued["region"], now)
            row = db.execute("""
                SELECT match_id FROM matches
                WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?)
                ORDER BY created_at DESC LIMIT 1
            """, (player_id, player_id)).fetchone()
            return self._match_payload(db, row["match_id"], player_id) if row else None

    def leave_queue(self, player_id: str) -> bool:
        with self.transaction() as db:
            result = db.execute("DELETE FROM queue WHERE player_id=?", (player_id,))
            return result.rowcount > 0

    def queue_status(self, player_id: str) -> dict:
        with self.transaction() as db:
            self._expire_matches(db, self.clock())
            row = db.execute("SELECT region,joined_at FROM queue WHERE player_id=?", (player_id,)).fetchone()
            if not row:
                return {"queued": False}
            self._match_queued_in_region(db, row["region"], self.clock())
            row = db.execute("SELECT region,joined_at FROM queue WHERE player_id=?", (player_id,)).fetchone()
            if not row:
                return {"queued": False}
            position = db.execute(
                "SELECT COUNT(*) FROM queue WHERE region=? AND (joined_at<? OR (joined_at=? AND player_id<=?))",
                (row["region"], row["joined_at"], row["joined_at"], player_id),
            ).fetchone()[0]
            return {"queued": True, "region": row["region"], "joined_at": row["joined_at"], "position": position}


    @staticmethod
    def _normalize_room_code(code: str) -> str:
        if not isinstance(code, str):
            raise ValueError("room code must be a string")
        normalized = "".join(ch for ch in code.upper().strip() if ch not in " -")
        if len(normalized) != 6 or not all(ch in "23456789ABCDEFGHJKLMNPQRSTUVWXYZ" for ch in normalized):
            raise ValueError("invalid room code")
        return normalized

    def create_private_room(self, player_id: str, region: str = "sa-east", *, ttl_seconds: int = 900) -> dict:
        region = str(region).strip().lower()
        if not region or len(region) > 24 or not all(c.isalnum() or c in "-_" for c in region):
            raise ValueError("invalid region")
        now = self.clock()
        alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
        with self.transaction() as db:
            player = db.execute("SELECT 1 FROM players WHERE player_id=?", (player_id,)).fetchone()
            if player is None:
                raise ValueError("unknown player")
            db.execute(
                "UPDATE private_rooms SET status='expired' WHERE status='waiting' AND expires_at<=?",
                (now,),
            )
            if db.execute("SELECT 1 FROM queue WHERE player_id=?", (player_id,)).fetchone():
                raise ValueError("leave the random queue before creating a private room")
            if db.execute(
                "SELECT 1 FROM matches WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?)",
                (player_id, player_id),
            ).fetchone():
                raise ValueError("player already has an assigned match")
            if db.execute(
                "SELECT 1 FROM private_rooms WHERE host_player=? AND status='waiting'",
                (player_id,),
            ).fetchone():
                raise ValueError("player already owns an open private room")
            for _ in range(20):
                code = "".join(secrets.choice(alphabet) for _ in range(6))
                if not db.execute("SELECT 1 FROM private_rooms WHERE room_code=?", (code,)).fetchone():
                    break
            else:
                raise RuntimeError("could not allocate a unique room code")
            expires_at = now + max(60, min(int(ttl_seconds), 3600))
            db.execute(
                "INSERT INTO private_rooms(room_code,host_player,region,status,created_at,expires_at) VALUES(?,?,?,'waiting',?,?)",
                (code, player_id, region, now, expires_at),
            )
            return {"room_code": code, "status": "waiting", "region": region,
                    "created_at": now, "expires_at": expires_at}

    def join_private_room(self, player_id: str, code: str) -> dict:
        code = self._normalize_room_code(code)
        now = self.clock()
        # Commit the attempt counter independently: an invalid code must not
        # roll back the rate limit when the room lookup raises below.
        with self.transaction() as db:
            attempts = db.execute(
                "SELECT window_started,attempts FROM room_join_attempts WHERE player_id=?",
                (player_id,),
            ).fetchone()
            if attempts and now - float(attempts["window_started"]) < 60:
                if int(attempts["attempts"]) >= 20:
                    raise ValueError("too many room code attempts; wait one minute")
                db.execute(
                    "UPDATE room_join_attempts SET attempts=attempts+1 WHERE player_id=?",
                    (player_id,),
                )
            else:
                db.execute(
                    "INSERT INTO room_join_attempts(player_id,window_started,attempts) VALUES(?,?,1) "
                    "ON CONFLICT(player_id) DO UPDATE SET window_started=excluded.window_started,attempts=1",
                    (player_id, now),
                )
        with self.transaction() as db:
            db.execute(
                "UPDATE private_rooms SET status='expired' WHERE status='waiting' AND expires_at<=?",
                (now,),
            )
            room = db.execute(
                "SELECT * FROM private_rooms WHERE room_code=? AND status='waiting'",
                (code,),
            ).fetchone()
            if room is None:
                raise ValueError("room not found, expired, or already used")
            if room["host_player"] == player_id:
                raise ValueError("you cannot join your own room")
            if db.execute("SELECT 1 FROM queue WHERE player_id=?", (player_id,)).fetchone():
                raise ValueError("leave the random queue before joining a private room")
            if db.execute(
                "SELECT 1 FROM matches WHERE status IN ('assigned','ready') AND (player_one=? OR player_two=?)",
                (player_id, player_id),
            ).fetchone():
                raise ValueError("player already has an assigned match")
            if db.execute(
                "SELECT 1 FROM private_rooms WHERE host_player=? AND status='waiting' AND expires_at>?",
                (player_id, now),
            ).fetchone():
                raise ValueError("cancel your own private room before joining another")
            host_id = room["host_player"]
            host = db.execute("SELECT rating FROM players WHERE player_id=?", (host_id,)).fetchone()
            guest = db.execute("SELECT rating FROM players WHERE player_id=?", (player_id,)).fetchone()
            if host is None or guest is None:
                raise ValueError("room player no longer exists")
            first_id, second_id = sorted((host_id, player_id))
            first = db.execute("SELECT rating FROM players WHERE player_id=?", (first_id,)).fetchone()
            second = db.execute("SELECT rating FROM players WHERE player_id=?", (second_id,)).fetchone()
            match_id = str(uuid.uuid4())
            db.execute(
                "INSERT INTO matches(match_id,player_one,player_two,rating_one,rating_two,region,status,created_at,expires_at,accepted_one,accepted_two) VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                (match_id, first_id, second_id, int(first["rating"]), int(second["rating"]),
                 room["region"], "ready", now, now + 7200, 1, 1),
            )
            db.execute(
                "UPDATE private_rooms SET guest_player=?,status='matched',match_id=?,expires_at=? WHERE room_code=? AND status='waiting'",
                (player_id, match_id, now + 7200, code),
            )
            return self._match_payload(db, match_id, player_id)

    def current_private_room(self, player_id: str) -> dict:
        now = self.clock()
        with self.transaction() as db:
            db.execute(
                "UPDATE private_rooms SET status='expired' WHERE host_player=? AND status='waiting' AND expires_at<=?",
                (player_id, now),
            )
            room = db.execute(
                "SELECT * FROM private_rooms WHERE host_player=? AND status IN ('waiting','matched','starting') ORDER BY created_at DESC LIMIT 1",
                (player_id,),
            ).fetchone()
            if room is None:
                return {"room": None, "match": None}
            payload = {
                "room_code": room["room_code"], "status": room["status"],
                "region": room["region"], "created_at": room["created_at"],
                "expires_at": room["expires_at"],
            }
            match = self._match_payload(db, room["match_id"], player_id) if room["match_id"] else None
            return {"room": payload, "match": match}

    def start_private_room(self, player_id: str) -> dict:
        """Host-only, idempotent start signal for a fully joined private room."""
        now = self.clock()
        with self.transaction() as db:
            db.execute(
                "UPDATE private_rooms SET status='expired' WHERE status IN ('matched','starting') "
                "AND expires_at<=?",
                (now,),
            )
            room = db.execute(
                "SELECT * FROM private_rooms WHERE host_player=? "
                "AND status IN ('matched','starting') ORDER BY created_at DESC LIMIT 1",
                (player_id,),
            ).fetchone()
            if room is None:
                raise ValueError("no joined private room is available to start")
            if not room["guest_player"] or not room["match_id"]:
                raise ValueError("a guest must join before the host can start")
            match = db.execute(
                "SELECT status FROM matches WHERE match_id=?",
                (room["match_id"],),
            ).fetchone()
            if match is None or match["status"] != "ready":
                raise ValueError("private match is no longer ready")
            if room["status"] == "matched":
                db.execute(
                    "UPDATE private_rooms SET status='starting' WHERE room_code=? AND status='matched'",
                    (room["room_code"],),
                )
            return {"match_id": room["match_id"], "status": "starting"}

    def publish_private_room_code(self, player_id: str, code: str) -> dict:
        """Store the host's authenticated MR1 endpoint code for this private match."""
        if not isinstance(code, str):
            raise ValueError("connection code must be text")
        code = code.strip().upper()
        if len(code) != 46 or not code.startswith("MR1-") or any(
            ch not in "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567" for ch in code[4:]
        ):
            raise ValueError("invalid Radmin MR1 connection code")
        with self.transaction() as db:
            room = db.execute(
                "SELECT * FROM private_rooms WHERE host_player=? AND status='starting' "
                "ORDER BY created_at DESC LIMIT 1",
                (player_id,),
            ).fetchone()
            if room is None or not room["guest_player"] or not room["match_id"]:
                raise ValueError("no started private room is available")
            if room["connection_code"] and room["connection_code"] != code:
                raise ValueError("a connection code has already been published for this room")
            db.execute(
                "UPDATE private_rooms SET connection_code=? WHERE room_code=? AND status='starting'",
                (code, room["room_code"]),
            )
            return {"match_id": room["match_id"], "status": "starting", "connection_code_published": True}

    def cancel_private_room(self, player_id: str) -> bool:
        with self.transaction() as db:
            result = db.execute(
                "UPDATE private_rooms SET status='cancelled' WHERE host_player=? AND status='waiting'",
                (player_id,),
            )
            return result.rowcount > 0

    def player_profile(self, player_id: str) -> dict:
        with self._connect() as db:
            row = db.execute("SELECT player_id,display_name,rating,created_at FROM players WHERE player_id=?", (player_id,)).fetchone()
            if row is None:
                raise ValueError("unknown player")
            return dict(row)
