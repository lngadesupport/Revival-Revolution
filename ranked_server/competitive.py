"""Competitive rating ledger.

Only a trusted match authority may submit results. Player bearer tokens are
never accepted by the internal result endpoint.
"""
from __future__ import annotations

import math
import sqlite3
import time
from pathlib import Path


SEASON_ID = "season-1"
K_FACTOR = 32


def division_for_rating(rating: int) -> str:
    rating = max(0, int(rating))
    if rating < 1200:
        return "Bronze"
    if rating < 1400:
        return "Prata"
    if rating < 1600:
        return "Ouro"
    if rating < 1800:
        return "Platina"
    if rating < 2000:
        return "Diamante"
    return "Mestre"


def calculate_elo(rating_one: int, rating_two: int, score_one: float,
                   k_factor: int = K_FACTOR) -> tuple[int, int]:
    """Return zero-sum integer deltas for player one and player two."""
    if type(rating_one) is not int or type(rating_two) is not int:
        raise ValueError("ratings must be integers")
    if rating_one < 0 or rating_two < 0:
        raise ValueError("ratings cannot be negative")
    if score_one not in (0.0, 0.5, 1.0):
        raise ValueError("score_one must be 0, 0.5, or 1")
    if type(k_factor) is not int or not 1 <= k_factor <= 128:
        raise ValueError("invalid K-factor")
    expected_one = 1.0 / (1.0 + math.pow(10.0, (rating_two - rating_one) / 400.0))
    delta_one = round(k_factor * (score_one - expected_one))
    # Keep the system zero-sum, including at the lower rating boundary.
    delta_one = max(-rating_one, delta_one)
    return delta_one, -delta_one


class CompetitiveStore:
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
                CREATE TABLE IF NOT EXISTS ranked_seasons (
                    season_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    status TEXT NOT NULL CHECK(status IN ('active','closed')),
                    created_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ranked_player_stats (
                    season_id TEXT NOT NULL REFERENCES ranked_seasons(season_id),
                    player_id TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
                    rating INTEGER NOT NULL CHECK(rating >= 0),
                    wins INTEGER NOT NULL DEFAULT 0,
                    losses INTEGER NOT NULL DEFAULT 0,
                    draws INTEGER NOT NULL DEFAULT 0,
                    games_played INTEGER NOT NULL DEFAULT 0,
                    peak_rating INTEGER NOT NULL,
                    updated_at REAL NOT NULL,
                    PRIMARY KEY(season_id, player_id)
                );
                CREATE TABLE IF NOT EXISTS ranked_results (
                    match_id TEXT PRIMARY KEY REFERENCES matches(match_id),
                    season_id TEXT NOT NULL REFERENCES ranked_seasons(season_id),
                    winner_player_id TEXT REFERENCES players(player_id),
                    is_draw INTEGER NOT NULL CHECK(is_draw IN (0,1)),
                    player_one_before INTEGER NOT NULL,
                    player_two_before INTEGER NOT NULL,
                    player_one_delta INTEGER NOT NULL,
                    player_two_delta INTEGER NOT NULL,
                    authority_event_id TEXT NOT NULL,
                    recorded_at REAL NOT NULL,
                    CHECK((is_draw=1 AND winner_player_id IS NULL) OR
                          (is_draw=0 AND winner_player_id IS NOT NULL))
                );
                CREATE INDEX IF NOT EXISTS ranked_stats_order
                    ON ranked_player_stats(season_id, rating DESC, games_played DESC);
                CREATE INDEX IF NOT EXISTS ranked_results_season_time
                    ON ranked_results(season_id, recorded_at DESC);
            """)
            db.execute(
                "INSERT OR IGNORE INTO ranked_seasons(season_id,name,status,created_at) VALUES(?,?,?,?)",
                (SEASON_ID, "Temporada 1", "active", self.clock()),
            )

    def _ensure_stats(self, db, player_id: str, now: float):
        player = db.execute(
            "SELECT rating FROM players WHERE player_id=?", (player_id,)
        ).fetchone()
        if player is None:
            raise ValueError("unknown player")
        db.execute("""
            INSERT OR IGNORE INTO ranked_player_stats
                (season_id,player_id,rating,peak_rating,updated_at)
            VALUES(?,?,?,?,?)
        """, (SEASON_ID, player_id, int(player["rating"]), int(player["rating"]), now))

    def record_authoritative_result(self, match_id: str, winner_player_id: str | None,
                                    *, is_draw: bool = False,
                                    authority_event_id: str) -> dict:
        if not isinstance(match_id, str) or not match_id.strip():
            raise ValueError("match_id is required")
        if type(is_draw) is not bool:
            raise ValueError("is_draw must be boolean")
        if not isinstance(authority_event_id, str) or not authority_event_id.strip() or len(authority_event_id) > 128:
            raise ValueError("authority_event_id is required")
        if is_draw:
            if winner_player_id is not None:
                raise ValueError("draw cannot include a winner")
            score_one = 0.5
        else:
            if not isinstance(winner_player_id, str) or not winner_player_id.strip():
                raise ValueError("winner_player_id is required")
            score_one = None

        now = self.clock()
        with self._connect() as db:
            db.execute("BEGIN IMMEDIATE")
            try:
                previous = db.execute(
                    "SELECT * FROM ranked_results WHERE match_id=?", (match_id,)
                ).fetchone()
                if previous:
                    same_submission = (
                        previous["authority_event_id"] == authority_event_id.strip()
                        and previous["is_draw"] == int(is_draw)
                        and previous["winner_player_id"] == winner_player_id
                    )
                    if not same_submission:
                        raise ValueError("match_result_conflict")
                    db.commit()
                    return self._result_payload(previous, already_recorded=True)

                reused_event = db.execute(
                    "SELECT match_id FROM ranked_results WHERE authority_event_id=? LIMIT 1",
                    (authority_event_id.strip(),),
                ).fetchone()
                if reused_event is not None and reused_event["match_id"] != match_id:
                    raise ValueError("authority_event_already_used")

                match = db.execute(
                    "SELECT * FROM matches WHERE match_id=?", (match_id,)
                ).fetchone()
                if match is None:
                    raise ValueError("match_not_found")
                if match["status"] not in ("assigned", "ready"):
                    raise ValueError("match_is_not_active")
                if match["match_type"] != "ranked":
                    raise ValueError("match_is_not_ranked")
                one, two = match["player_one"], match["player_two"]
                if not is_draw and winner_player_id not in (one, two):
                    raise ValueError("winner_does_not_belong_to_match")
                if score_one is None:
                    score_one = 1.0 if winner_player_id == one else 0.0

                self._ensure_stats(db, one, now)
                self._ensure_stats(db, two, now)
                stats = db.execute("""
                    SELECT player_id,rating FROM ranked_player_stats
                    WHERE season_id=? AND player_id IN (?,?)
                """, (SEASON_ID, one, two)).fetchall()
                ratings = {row["player_id"]: int(row["rating"]) for row in stats}
                before_one, before_two = ratings[one], ratings[two]
                delta_one, delta_two = calculate_elo(before_one, before_two, score_one)
                after_one, after_two = before_one + delta_one, before_two + delta_two

                db.execute("""
                    UPDATE ranked_player_stats
                    SET rating=?,peak_rating=MAX(peak_rating,?),wins=wins+?,
                        losses=losses+?,draws=draws+?,games_played=games_played+1,updated_at=?
                    WHERE season_id=? AND player_id=?
                """, (after_one, after_one, int(score_one == 1.0),
                      int(score_one == 0.0), int(score_one == 0.5), now, SEASON_ID, one))
                db.execute("""
                    UPDATE ranked_player_stats
                    SET rating=?,peak_rating=MAX(peak_rating,?),wins=wins+?,
                        losses=losses+?,draws=draws+?,games_played=games_played+1,updated_at=?
                    WHERE season_id=? AND player_id=?
                """, (after_two, after_two, int(score_one == 0.0),
                      int(score_one == 1.0), int(score_one == 0.5), now, SEASON_ID, two))
                # Keep the existing matchmaking/profile rating in sync with the
                # active competitive season.
                db.execute("UPDATE players SET rating=? WHERE player_id=?", (after_one, one))
                db.execute("UPDATE players SET rating=? WHERE player_id=?", (after_two, two))
                db.execute("""
                    INSERT INTO ranked_results
                    (match_id,season_id,winner_player_id,is_draw,player_one_before,
                     player_two_before,player_one_delta,player_two_delta,authority_event_id,recorded_at)
                    VALUES(?,?,?,?,?,?,?,?,?,?)
                """, (match_id, SEASON_ID, winner_player_id, int(is_draw), before_one,
                      before_two, delta_one, delta_two, authority_event_id.strip(), now))
                db.execute("UPDATE matches SET status='completed' WHERE match_id=?", (match_id,))
                result = db.execute(
                    "SELECT * FROM ranked_results WHERE match_id=?", (match_id,)
                ).fetchone()
                db.commit()
                return self._result_payload(result, already_recorded=False)
            except Exception:
                db.rollback()
                raise

    @staticmethod
    def _result_payload(row, *, already_recorded: bool):
        return {
            "match_id": row["match_id"], "season_id": row["season_id"],
            "winner_player_id": row["winner_player_id"], "draw": bool(row["is_draw"]),
            "player_one_before": row["player_one_before"],
            "player_two_before": row["player_two_before"],
            "player_one_delta": row["player_one_delta"],
            "player_two_delta": row["player_two_delta"],
            "recorded_at": row["recorded_at"], "already_recorded": already_recorded,
        }

    def player_summary(self, player_id: str) -> dict:
        with self._connect() as db:
            player = db.execute(
                "SELECT player_id,display_name,rating FROM players WHERE player_id=?",
                (player_id,),
            ).fetchone()
            if player is None:
                raise ValueError("unknown player")
            stats = db.execute("""
                SELECT rating,wins,losses,draws,games_played,peak_rating
                FROM ranked_player_stats WHERE season_id=? AND player_id=?
            """, (SEASON_ID, player_id)).fetchone()
            values = dict(stats) if stats else {
                "rating": int(player["rating"]), "wins": 0, "losses": 0,
                "draws": 0, "games_played": 0, "peak_rating": int(player["rating"]),
            }
            return {
                **dict(player), **values, "season_id": SEASON_ID,
                "division": division_for_rating(values["rating"]),
            }

    def leaderboard(self, limit: int = 100) -> list[dict]:
        limit = max(1, min(int(limit), 100))
        with self._connect() as db:
            rows = db.execute("""
                SELECT p.player_id,p.display_name,s.rating,s.wins,s.losses,s.draws,
                       s.games_played,s.peak_rating
                FROM ranked_player_stats s JOIN players p USING(player_id)
                WHERE s.season_id=?
                ORDER BY s.rating DESC,s.wins DESC,s.games_played ASC,p.player_id ASC
                LIMIT ?
            """, (SEASON_ID, limit)).fetchall()
            return [{
                "position": index + 1, **dict(row),
                "division": division_for_rating(row["rating"]),
            } for index, row in enumerate(rows)]

    def match_history(self, player_id: str, limit: int = 20) -> list[dict]:
        limit = max(1, min(int(limit), 50))
        with self._connect() as db:
            rows = db.execute("""
                SELECT r.*,m.player_one,m.player_two,
                       p1.display_name AS player_one_name,p2.display_name AS player_two_name
                FROM ranked_results r JOIN matches m USING(match_id)
                JOIN players p1 ON p1.player_id=m.player_one
                JOIN players p2 ON p2.player_id=m.player_two
                WHERE m.player_one=? OR m.player_two=?
                ORDER BY r.recorded_at DESC LIMIT ?
            """, (player_id, player_id, limit)).fetchall()
            result = []
            for row in rows:
                own_is_one = row["player_one"] == player_id
                own_delta = row["player_one_delta"] if own_is_one else row["player_two_delta"]
                opponent = row["player_two_name"] if own_is_one else row["player_one_name"]
                if row["is_draw"]:
                    outcome = "draw"
                elif row["winner_player_id"] == player_id:
                    outcome = "win"
                else:
                    outcome = "loss"
                result.append({
                    "match_id": row["match_id"], "opponent": opponent,
                    "outcome": outcome, "rating_delta": own_delta,
                    "recorded_at": row["recorded_at"],
                })
            return result
