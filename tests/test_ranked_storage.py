import tempfile
import unittest
from pathlib import Path

from ranked_server.storage import RankedStore
from peer_session import encode_code


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

    def test_longest_wait_expands_rating_window_for_new_arrival(self):
        with self.store.transaction() as db:
            db.execute("UPDATE players SET rating=1000 WHERE player_id=?", (self.one["player_id"],))
            db.execute("UPDATE players SET rating=1250 WHERE player_id=?", (self.two["player_id"],))
        self.store.join_queue(self.one["player_id"], "sa-east")
        self.now += 30
        match = self.store.join_queue(self.two["player_id"], "sa-east")
        self.assertIsNotNone(match)


    def test_queue_polling_triggers_match_when_rating_window_expands(self):
        with self.store.transaction() as db:
            db.execute("UPDATE players SET rating=1000 WHERE player_id=?", (self.one["player_id"],))
            db.execute("UPDATE players SET rating=1250 WHERE player_id=?", (self.two["player_id"],))
        self.assertIsNone(self.store.join_queue(self.one["player_id"], "sa-east"))
        self.assertIsNone(self.store.join_queue(self.two["player_id"], "sa-east"))
        self.assertTrue(self.store.queue_status(self.one["player_id"])["queued"])

        self.now += 30
        status = self.store.queue_status(self.one["player_id"])
        self.assertFalse(status["queued"])
        first_match = self.store.current_match(self.one["player_id"])
        second_match = self.store.current_match(self.two["player_id"])
        self.assertIsNotNone(first_match)
        self.assertIsNotNone(second_match)
        self.assertEqual(first_match["match_id"], second_match["match_id"])
        self.assertEqual(first_match["opponent"]["display_name"], "PC-PLAYER-TWO")

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

    def _create_assigned_match(self):
        self.store.join_queue(self.one["player_id"])
        self.store.join_queue(self.two["player_id"])
        return self.store.current_match(self.one["player_id"])

    def test_match_requires_both_acceptances(self):
        match = self._create_assigned_match()
        first = self.store.accept_match(self.one["player_id"])
        self.assertEqual(first["status"], "assigned")
        self.assertTrue(first["accepted"])
        self.assertFalse(first["opponent_accepted"])
        second = self.store.accept_match(self.two["player_id"])
        self.assertEqual(second["status"], "ready")
        self.assertTrue(second["accepted"])
        self.assertTrue(second["opponent_accepted"])
        self.assertEqual(self.store.current_match(self.one["player_id"])["status"], "ready")

    def test_decline_releases_both_players_from_active_match(self):
        self._create_assigned_match()
        result = self.store.decline_match(self.one["player_id"])
        self.assertEqual(result["status"], "declined")
        self.assertIsNone(self.store.current_match(self.two["player_id"]))
        self.assertIsNone(self.store.join_queue(self.one["player_id"]))

    def test_unaccepted_match_expires_after_ninety_seconds(self):
        self._create_assigned_match()
        self.now += 91
        self.assertIsNone(self.store.current_match(self.one["player_id"]))
        self.assertIsNone(self.store.current_match(self.two["player_id"]))
        self.assertIsNone(self.store.join_queue(self.one["player_id"]))

    def test_ready_match_finishes_only_after_both_confirm(self):
        self._create_assigned_match()
        self.store.accept_match(self.one["player_id"])
        self.store.accept_match(self.two["player_id"])
        first = self.store.confirm_match_finished(self.one["player_id"])
        self.assertEqual(first["status"], "awaiting_opponent_confirmation")
        self.assertFalse(first["opponent_confirmed"])
        self.assertFalse(first["rating_changed"])
        second = self.store.confirm_match_finished(self.two["player_id"])
        self.assertEqual(second["status"], "completed")
        self.assertTrue(second["opponent_confirmed"])
        self.assertFalse(second["rating_changed"])
        self.assertIsNone(self.store.current_match(self.one["player_id"]))

    def test_rating_is_not_changed_by_client_completion(self):
        before = self.store.player_profile(self.one["player_id"])["rating"]
        self.store.join_queue(self.one["player_id"])
        self.store.join_queue(self.two["player_id"])
        after = self.store.player_profile(self.one["player_id"])["rating"]
        self.assertEqual(before, after)


    def test_ready_player_cannot_create_private_room(self):
        self._create_assigned_match()
        self.store.accept_match(self.one["player_id"])
        self.store.accept_match(self.two["player_id"])
        with self.assertRaisesRegex(ValueError, "already has an assigned match"):
            self.store.create_private_room(self.one["player_id"], "sa-east")

    def test_ready_player_cannot_join_private_room(self):
        host = self.store.register("PC-ROOM-HOST")
        room = self.store.create_private_room(host["player_id"], "sa-east")
        self._create_assigned_match()
        self.store.accept_match(self.one["player_id"])
        self.store.accept_match(self.two["player_id"])
        with self.assertRaisesRegex(ValueError, "already has an assigned match"):
            self.store.join_private_room(self.one["player_id"], room["room_code"])


    def test_private_room_host_is_auto_ready_and_host_controls_start(self):
        host = self.store.register("AUTO-ROOM-HOST")
        guest = self.store.register("AUTO-ROOM-GUEST")
        room = self.store.create_private_room(host["player_id"], "sa-east")
        guest_match = self.store.join_private_room(guest["player_id"], room["room_code"])

        self.assertEqual(guest_match["status"], "ready")
        self.assertTrue(guest_match["accepted"])
        self.assertTrue(guest_match["opponent_accepted"])
        self.assertEqual(guest_match["room_role"], "guest")
        self.assertEqual(guest_match["room_status"], "matched")

        host_room = self.store.current_private_room(host["player_id"])
        self.assertEqual(host_room["match"]["room_role"], "host")
        self.assertEqual(host_room["match"]["room_status"], "matched")

        started = self.store.start_private_room(host["player_id"])
        self.assertEqual(started["status"], "starting")
        self.assertEqual(self.store.start_private_room(host["player_id"])["status"], "starting")
        self.assertEqual(self.store.current_match(guest["player_id"])["room_status"], "starting")
        with self.assertRaisesRegex(ValueError, "no joined private room"):
            self.store.start_private_room(guest["player_id"])

    def test_host_can_publish_connection_code_for_guest(self):
        host = self.store.register("CODE-HOST")
        guest = self.store.register("CODE-GUEST")
        room = self.store.create_private_room(host["player_id"], "sa-east")
        self.store.join_private_room(guest["player_id"], room["room_code"])
        self.store.start_private_room(host["player_id"])
        code = encode_code("26.10.20.30", 8766, b"\x11" * 16)
        result = self.store.publish_private_room_code(host["player_id"], code)
        self.assertTrue(result["connection_code_published"])
        self.assertEqual(self.store.current_match(guest["player_id"])["connection_code"], code)
        with self.assertRaisesRegex(ValueError, "no started private room"):
            self.store.publish_private_room_code(guest["player_id"], code)

if __name__ == "__main__":
    unittest.main()
