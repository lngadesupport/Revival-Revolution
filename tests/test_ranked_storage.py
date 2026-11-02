import tempfile
import unittest
from pathlib import Path

from ranked_server.storage import RankedStore


class RankedStoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.now = 1000.0
        self.store = RankedStore(Path(self.temp.name) / "ranked.sqlite3", clock=lambda: self.now)
        self.one = self.store.register("PC-PLAYER-ONE")
        self.two = self.store.register("PC-PLAYER-TWO")

    def tearDown(self):
        self.temp.cleanup()

    def test_registration_and_authentication(self):
        self.assertNotEqual(self.one["player_id"], self.two["player_id"])
        self.assertEqual(self.store.authenticate(self.one["token"])["player_id"], self.one["player_id"])
        self.assertIsNone(self.store.authenticate("invalid-token"))

    def test_queue_pairs_players(self):
        self.assertIsNone(self.store.join_queue(self.one["player_id"], "sa-east"))
        match = self.store.join_queue(self.two["player_id"], "sa-east")
        self.assertIsNotNone(match)
        self.assertEqual(match["opponent"]["display_name"], "PC-PLAYER-ONE")
        self.assertIsNotNone(self.store.current_match(self.one["player_id"]))

    def test_regions_do_not_mix(self):
        self.store.join_queue(self.one["player_id"], "sa-east")
        self.assertIsNone(self.store.join_queue(self.two["player_id"], "eu-west"))

    def test_repeated_queue_join_is_safe(self):
        self.store.join_queue(self.one["player_id"], "global")
        self.assertIsNone(self.store.join_queue(self.one["player_id"], "global"))

    def test_assigned_player_cannot_requeue(self):
        self.store.join_queue(self.one["player_id"])
        self.store.join_queue(self.two["player_id"])
        with self.assertRaises(ValueError):
            self.store.join_queue(self.one["player_id"])

    def test_leave_queue(self):
        self.store.join_queue(self.one["player_id"])
        self.assertTrue(self.store.leave_queue(self.one["player_id"]))
        self.assertFalse(self.store.leave_queue(self.one["player_id"]))

    def test_display_name_validation(self):
        with self.assertRaises(ValueError):
            self.store.register("")

    def test_rating_is_not_changed_by_client_completion(self):
        before = self.store.player_profile(self.one["player_id"])["rating"]
        self.store.join_queue(self.one["player_id"])
        self.store.join_queue(self.two["player_id"])
        after = self.store.player_profile(self.one["player_id"])["rating"]
        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
