"""Deterministic 1v1 rating-based matchmaking core.

This module is deliberately transport- and database-independent. The API layer
must authenticate players before calling it and persist queue/match transitions
transactionally before this is used by a live service.
"""
from __future__ import annotations

from dataclasses import dataclass
import time
import uuid
from typing import Callable


@dataclass(frozen=True, slots=True)
class QueueEntry:
    player_id: str
    display_name: str
    rating: int
    joined_at: float
    region: str = "global"


@dataclass(frozen=True, slots=True)
class MatchAssignment:
    match_id: str
    player_one: QueueEntry
    player_two: QueueEntry
    created_at: float


class Matchmaker:
    """In-process queue matcher; one active queue entry per player."""

    def __init__(
        self,
        *,
        base_rating_window: int = 100,
        rating_window_step: int = 50,
        widen_every_seconds: int = 10,
        clock: Callable[[], float] = time.time,
        id_factory: Callable[[], str] | None = None,
    ) -> None:
        if base_rating_window < 0 or rating_window_step < 0 or widen_every_seconds <= 0:
            raise ValueError("Invalid matchmaking window configuration")
        self.base_rating_window = base_rating_window
        self.rating_window_step = rating_window_step
        self.widen_every_seconds = widen_every_seconds
        self._clock = clock
        self._id_factory = id_factory or (lambda: uuid.uuid4().hex)
        self._queue: dict[str, QueueEntry] = {}

    def join(self, player_id: str, display_name: str, rating: int, region: str = "global") -> QueueEntry:
        if not player_id or not player_id.strip():
            raise ValueError("player_id is required")
        if not display_name or not display_name.strip():
            raise ValueError("display_name is required")
        if type(rating) is not int or rating < 0:
            raise ValueError("rating must be a non-negative integer")
        if not region or not region.strip():
            raise ValueError("region is required")
        if player_id in self._queue:
            raise ValueError("player is already queued")
        entry = QueueEntry(player_id, display_name.strip()[:32], rating, self._clock(), region.strip().lower())
        self._queue[player_id] = entry
        return entry

    def leave(self, player_id: str) -> bool:
        return self._queue.pop(player_id, None) is not None

    def queued(self, player_id: str) -> bool:
        return player_id in self._queue

    def snapshot(self) -> tuple[QueueEntry, ...]:
        return tuple(sorted(self._queue.values(), key=lambda item: (item.joined_at, item.player_id)))

    def match_one(self, now: float | None = None) -> MatchAssignment | None:
        """Create the best eligible 1v1 assignment, if one exists.

        The rating window widens with the older player's queue wait. Region
        must match for now; cross-region policy belongs in a later routing layer.
        """
        current = self._clock() if now is None else now
        entries = self.snapshot()
        best: tuple[tuple[float, int, int, str, str], QueueEntry, QueueEntry] | None = None
        for index, first in enumerate(entries):
            waited = max(0.0, current - first.joined_at)
            window = self.base_rating_window + int(waited // self.widen_every_seconds) * self.rating_window_step
            for second in entries[index + 1:]:
                if first.region != second.region:
                    continue
                difference = abs(first.rating - second.rating)
                if difference > window:
                    continue
                score = (difference, max(first.joined_at, second.joined_at),
                         min(first.joined_at, second.joined_at), first.player_id, second.player_id)
                if best is None or score < best[0]:
                    best = (score, first, second)
        if best is None:
            return None
        first, second = best[1], best[2]
        del self._queue[first.player_id]
        del self._queue[second.player_id]
        return MatchAssignment(self._id_factory(), first, second, current)

    def match_all(self, now: float | None = None) -> list[MatchAssignment]:
        assignments: list[MatchAssignment] = []
        while True:
            assignment = self.match_one(now)
            if assignment is None:
                return assignments
            assignments.append(assignment)
