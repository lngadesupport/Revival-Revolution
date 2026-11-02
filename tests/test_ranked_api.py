import json
import threading
import tempfile
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

from ranked_server.server import make_handler
from ranked_server.storage import RankedStore


class RankedApiTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        store = RankedStore(self.temp.name + "/ranked.sqlite3")
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(store))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def request(self, method, path, payload=None, token=None):
        data = None if payload is None else json.dumps(payload).encode()
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        req = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=3) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read())

    def register(self, name):
        status, body = self.request("POST", "/v1/register", {"display_name": name})
        self.assertEqual(status, 201)
        return body

    def test_health_is_public(self):
        status, body = self.request("GET", "/health")
        self.assertEqual(status, 200)
        self.assertTrue(body["ok"])

    def test_profile_requires_token(self):
        player = self.register("PC-ONE")
        status, _ = self.request("GET", "/v1/me")
        self.assertEqual(status, 401)
        status, profile = self.request("GET", "/v1/me", token=player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["player_id"], player["player_id"])

    def test_queue_assigns_match(self):
        one = self.register("PC-ONE")
        two = self.register("PC-TWO")
        status, _ = self.request("POST", "/v1/queue/join", {"region": "sa-east"}, one["token"])
        self.assertEqual(status, 202)
        status, body = self.request("POST", "/v1/queue/join", {"region": "sa-east"}, two["token"])
        self.assertEqual(status, 201)
        self.assertEqual(body["match"]["opponent"]["display_name"], "PC-ONE")

    def test_invalid_registration_is_rejected(self):
        status, _ = self.request("POST", "/v1/register", {"display_name": ""})
        self.assertEqual(status, 400)


    def test_profile_can_change_nickname_without_changing_identity(self):
        player = self.register("OLD-NAME")
        status, profile = self.request("POST", "/v1/profile", {"display_name": "New Name"}, player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["player_id"], player["player_id"])
        self.assertEqual(profile["display_name"], "New Name")
        status, current = self.request("GET", "/v1/me", token=player["token"])
        self.assertEqual(current["player_id"], player["player_id"])
        self.assertEqual(current["display_name"], "New Name")

    def test_profile_avatar_can_be_saved_and_cleared(self):
        player = self.register("AVATAR-PLAYER")
        avatar = "data:image/png;base64,aGVsbG8="
        status, profile = self.request("POST", "/v1/profile", {"avatar_data": avatar}, player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["avatar_data"], avatar)
        status, profile = self.request("POST", "/v1/profile", {"avatar_data": None}, player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["avatar_data"], "")

    def test_friend_request_accept_and_remove(self):
        one = self.register("FRIEND-ONE")
        two = self.register("FRIEND-TWO")
        status, result = self.request("POST", "/v1/friends/request", {"player_id": two["player_id"]}, one["token"])
        self.assertEqual(status, 201)
        self.assertEqual(result["status"], "pending")
        status, friends = self.request("GET", "/v1/friends", token=two["token"])
        self.assertEqual(status, 200)
        self.assertEqual(friends["incoming"][0]["player_id"], one["player_id"])
        status, result = self.request("POST", "/v1/friends/accept", {"player_id": one["player_id"]}, two["token"])
        self.assertEqual(status, 200)
        self.assertEqual(result["status"], "accepted")
        status, friends = self.request("GET", "/v1/friends", token=one["token"])
        self.assertEqual(friends["friends"][0]["player_id"], two["player_id"])
        status, result = self.request("POST", "/v1/friends/remove", {"player_id": two["player_id"]}, one["token"])
        self.assertTrue(result["removed"])

    def test_player_search_and_leaderboard(self):
        one = self.register("SEARCHABLE-ONE")
        two = self.register("SEARCHABLE-TWO")
        status, found = self.request("GET", "/v1/players/search?q=SEARCHABLE")
        self.assertEqual(status, 200)
        self.assertEqual(len(found["players"]), 2)
        status, board = self.request("GET", "/v1/leaderboard", token=one["token"])
        self.assertEqual(status, 200)
        self.assertEqual(len(board["entries"]), 2)
        self.assertEqual(board["entries"][0]["position"], 1)

    def test_friend_request_rejects_self(self):
        player = self.register("SELF")
        status, _ = self.request("POST", "/v1/friends/request", {"player_id": player["player_id"]}, player["token"])
        self.assertEqual(status, 400)


if __name__ == "__main__":
    unittest.main()
