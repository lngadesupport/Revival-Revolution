import tempfile
import unittest
from pathlib import Path

from ranked_server.competitive import CompetitiveStore, calculate_elo, division_for_rating
from ranked_server.storage import RankedStore


class CompetitiveRatingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.now = 5000.0
        self.db = Path(self.temp.name) / "ranked.sqlite3"
        self.accounts = RankedStore(self.db, clock=lambda: self.now)
        self.one = self.accounts.register("RANKED-ONE")
        self.two = self.accounts.register("RANKED-TWO")
        self.competitive = CompetitiveStore(self.db, clock=lambda: self.now)
        self.accounts.join_queue(self.one["player_id"], "sa-east", mode="ranked")
        match = self.accounts.join_queue(self.two["player_id"], "sa-east", mode="ranked")
        self.match_id = match["match_id"]

    def tearDown(self):
        self.temp.cleanup()

    def test_casual_queue_does_not_create_ranked_match(self):
        self.assertIsNone(self.accounts.join_queue(self.one["player_id"], "sa-east", mode="casual"))
        self.assertIsNone(self.accounts.join_queue(self.two["player_id"], "sa-east", mode="ranked"))
        self.assertEqual(self.accounts.queue_status(self.one["player_id"])["mode"], "casual")
        self.assertEqual(self.accounts.queue_status(self.two["player_id"])["mode"], "ranked")

    def test_elo_is_zero_sum_and_rewards_upset(self):
        one, two = calculate_elo(1000, 1400, 1.0)
        self.assertGreater(one, 0)
        self.assertLess(two, 0)
        self.assertEqual(one + two, 0)

    def test_draw_is_zero_sum(self):
        one, two = calculate_elo(1000, 1000, 0.5)
        self.assertEqual((one, two), (0, 0))

    def test_divisions(self):
        self.assertEqual(division_for_rating(1000), "Bronze")
        self.assertEqual(division_for_rating(1200), "Prata")
        self.assertEqual(division_for_rating(2000), "Mestre")

    def test_authoritative_result_updates_both_players_and_history(self):
        result = self.competitive.record_authoritative_result(
            self.match_id, self.one["player_id"], authority_event_id="game-server-event-1"
        )
        self.assertFalse(result["already_recorded"])
        self.assertEqual(result["player_one_delta"] + result["player_two_delta"], 0)
        winner = self.competitive.player_summary(self.one["player_id"])
        loser = self.competitive.player_summary(self.two["player_id"])
        self.assertEqual(winner["wins"], 1)
        self.assertEqual(loser["losses"], 1)
        self.assertEqual(self.competitive.match_history(self.one["player_id"])[0]["outcome"], "win")

    def test_result_submission_is_idempotent(self):
        first = self.competitive.record_authoritative_result(
            self.match_id, self.one["player_id"], authority_event_id="event-1"
        )
        second = self.competitive.record_authoritative_result(
            self.match_id, self.one["player_id"], authority_event_id="event-1"
        )
        self.assertFalse(first["already_recorded"])
        self.assertTrue(second["already_recorded"])
        self.assertEqual(
            self.competitive.player_summary(self.one["player_id"])["games_played"], 1
        )

    def test_player_cannot_be_declared_winner_if_not_in_match(self):
        with self.assertRaises(ValueError):
            self.competitive.record_authoritative_result(
                self.match_id, "not-a-player", authority_event_id="event-2"
            )

    def test_unrated_room_is_rejected(self):
        host = self.accounts.register("ROOM-HOST")
        guest = self.accounts.register("ROOM-GUEST")
        room = self.accounts.create_private_room(host["player_id"], "sa-east")
        match = self.accounts.join_private_room(guest["player_id"], room["room_code"])
        with self.assertRaisesRegex(ValueError, "match_is_not_ranked"):
            self.competitive.record_authoritative_result(
                match["match_id"], host["player_id"], authority_event_id="room-event"
            )

    def test_draw_record(self):
        result = self.competitive.record_authoritative_result(
            self.match_id, None, is_draw=True, authority_event_id="event-draw"
        )
        self.assertTrue(result["draw"])
        self.assertEqual(self.competitive.player_summary(self.one["player_id"])["draws"], 1)


if __name__ == "__main__":
    unittest.main()
