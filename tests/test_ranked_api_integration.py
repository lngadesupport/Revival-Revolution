import json
import os
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from unittest.mock import patch

import peer_session
from http.server import ThreadingHTTPServer

from ranked_server.server import make_handler
from ranked_server.storage import RankedStore


class RankedApiIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = RankedStore(self.temp.name + "/ranked.sqlite3")
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(self.store))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def request(self, method, path, payload=None, token=None, authority=None):
        data = None if payload is None else json.dumps(payload).encode()
        headers = {}
        if data is not None:
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = "Bearer " + token
        if authority:
            headers["X-Ranked-Authority"] = authority
        request = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=3) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as exc:
            return exc.code, json.loads(exc.read())

    def register(self, name):
        status, player = self.request("POST", "/v1/register", {"display_name": name})
        self.assertEqual(status, 201)
        return player

    def test_ranked_and_casual_queues_are_isolated_on_radmin_base(self):
        casual_one = self.register("Casual One")
        casual_two = self.register("Casual Two")
        ranked_one = self.register("Ranked One")
        ranked_two = self.register("Ranked Two")

        self.assertEqual(self.request("POST", "/v1/queue/join", {"region": "sa-east"}, casual_one["token"])[0], 202)
        status, casual_match = self.request("POST", "/v1/queue/join", {"region": "sa-east"}, casual_two["token"])
        self.assertEqual(status, 201)
        self.assertEqual(casual_match["match"]["match_type"], "casual")

        self.assertEqual(self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, ranked_one["token"])[0], 202)
        status, ranked_match = self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, ranked_two["token"])
        self.assertEqual(status, 201)
        self.assertEqual(ranked_match["match"]["match_type"], "ranked")
        self.assertNotEqual(casual_match["match"]["match_id"], ranked_match["match"]["match_id"])

    def test_private_radmin_room_is_not_eligible_for_elo(self):
        host = self.register("Room Host")
        guest = self.register("Room Guest")
        status, room = self.request("POST", "/v1/rooms/create", {"region": "sa-east"}, host["token"])
        self.assertEqual(status, 201)
        status, match = self.request("POST", "/v1/rooms/join", {"code": room["room"]["room_code"]}, guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(match["match"]["match_type"], "private")
        with self.assertRaisesRegex(ValueError, "match_is_not_ranked"):
            from ranked_server.competitive import CompetitiveStore
            CompetitiveStore(self.store.database).record_authoritative_result(
                match["match"]["match_id"], host["player_id"], authority_event_id="private-room-event"
            )

    def test_ranked_match_uses_radmin_mr1_code_exchange(self):
        one = self.register("Radmin One")
        two = self.register("Radmin Two")
        self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, one["token"])
        self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, two["token"])
        _, one_match = self.request("GET", "/v1/ranked/match", token=one["token"])
        _, two_match = self.request("GET", "/v1/ranked/match", token=two["token"])
        self.assertEqual(one_match["match"]["match_id"], two_match["match"]["match_id"])
        self.request("POST", "/v1/ranked/match/accept", {}, one["token"])
        self.request("POST", "/v1/ranked/match/accept", {}, two["token"])
        host, guest = (one, two) if one_match["match"]["player_slot"] == 0 else (two, one)
        code = peer_session.encode_code("26.10.20.30", 8766, bytes(range(16)))
        status, _ = self.request(
            "POST", "/v1/ranked/match/connection-code", {"code": code}, host["token"]
        )
        self.assertEqual(status, 200)
        status, guest_state = self.request("GET", "/v1/ranked/match", token=guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(guest_state["match"]["connection_code"], code)
        status, _ = self.request(
            "POST", "/v1/ranked/match/connection-code", {"code": code}, guest["token"]
        )
        self.assertEqual(status, 400)

    def test_ranked_profile_and_leaderboard_routes(self):
        player = self.register("Leaderboard Player")
        status, profile = self.request("GET", "/v1/ranked/me", token=player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["rating"], 1000)
        status, board = self.request("GET", "/v1/ranked/leaderboard?limit=10", token=player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(board["entries"][0]["display_name"], "Leaderboard Player")

    def test_result_authority_is_checked_before_request_body(self):
        one = self.register("Authority One")
        two = self.register("Authority Two")
        self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, one["token"])
        _, result = self.request("POST", "/v1/ranked/queue/join", {"region": "sa-east"}, two["token"])
        match_id = result["match"]["match_id"]
        self.assertEqual(self.request("POST", "/v1/ranked/match/accept", {}, one["token"])[0], 200)
        self.assertEqual(self.request("POST", "/v1/ranked/match/accept", {}, two["token"])[0], 200)
        self.assertEqual(self.request("GET", "/v1/ranked/match", token=one["token"])[1]["match"]["status"], "ready")
        self.assertEqual(self.request("POST", "/v1/ranked/match/finish", {}, one["token"])[0], 200)
        self.assertEqual(self.request("POST", "/v1/ranked/match/finish", {}, two["token"])[0], 200)
        self.assertEqual(self.request("GET", "/v1/ranked/match", token=one["token"])[1]["match"], None)
        with patch.dict(os.environ, {"RANKED_RESULT_SECRET": ""}):
            status, body = self.request("POST", f"/internal/v1/matches/{match_id}/result")
            self.assertEqual(status, 503)
            self.assertEqual(body["error"], "result_authority_not_configured")
        with patch.dict(os.environ, {"RANKED_RESULT_SECRET": "authority-secret"}):
            status, body = self.request("POST", f"/internal/v1/matches/{match_id}/result")
            self.assertEqual(status, 403)
            self.assertEqual(body["error"], "result_authority_forbidden")
            status, body = self.request(
                "POST", f"/internal/v1/matches/{match_id}/result",
                {"winner_player_id": one["player_id"], "authority_event_id": "ranked-event-1"},
                authority="authority-secret",
            )
            self.assertEqual(status, 200)
            self.assertFalse(body["result"]["already_recorded"])
            status, repeated = self.request(
                "POST", f"/internal/v1/matches/{match_id}/result",
                {"winner_player_id": one["player_id"], "authority_event_id": "ranked-event-1"},
                authority="authority-secret",
            )
            self.assertEqual(status, 200)
            self.assertTrue(repeated["result"]["already_recorded"])


if __name__ == "__main__":
    unittest.main()
