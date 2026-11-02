import unittest

from ranked_server.matchmaking import Matchmaker


class MatchmakerTests(unittest.TestCase):
    def setUp(self):
        self.now = 1000.0
        self.ids = iter(["match-1", "match-2"])
        self.mm = Matchmaker(clock=lambda: self.now, id_factory=lambda: next(self.ids))

    def test_pairs_closest_eligible_ratings(self):
        self.mm.join("a", "PC-A", 1000)
        self.mm.join("b", "PC-B", 1080)
        self.mm.join("c", "PC-C", 1010)
        match = self.mm.match_one()
        self.assertIsNotNone(match)
        self.assertEqual({match.player_one.player_id, match.player_two.player_id}, {"a", "c"})
        self.assertEqual(match.match_id, "match-1")
        self.assertTrue(self.mm.queued("b"))

    def test_rating_window_expands_with_wait(self):
        self.mm.join("a", "PC-A", 1000)
        self.mm.join("b", "PC-B", 1250)
        self.assertIsNone(self.mm.match_one())
        self.now += 30
        self.assertIsNotNone(self.mm.match_one())

    def test_region_is_respected(self):
        self.mm.join("a", "PC-A", 1000, "sa")
        self.mm.join("b", "PC-B", 1000, "eu")
        self.assertIsNone(self.mm.match_one())

    def test_duplicate_queue_entry_is_rejected(self):
        self.mm.join("a", "PC-A", 1000)
        with self.assertRaises(ValueError):
            self.mm.join("a", "PC-A-2", 1000)

    def test_leave_removes_only_requested_player(self):
        self.mm.join("a", "PC-A", 1000)
        self.assertTrue(self.mm.leave("a"))
        self.assertFalse(self.mm.leave("a"))
        self.assertEqual(self.mm.snapshot(), ())

    def test_match_all_never_reuses_a_player(self):
        for player in "abcd":
            self.mm.join(player, f"PC-{player}", 1000)
        matches = self.mm.match_all()
        self.assertEqual(len(matches), 2)
        ids = [p.player_id for m in matches for p in (m.player_one, m.player_two)]
        self.assertEqual(len(ids), len(set(ids)))


if __name__ == "__main__":
    unittest.main()
