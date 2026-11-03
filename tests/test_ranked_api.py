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
        self.assertEqual(body["version"], "0.8.1")

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
        avatar = "data:image/png;base64,iVBORw0KGgo="
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
        status, found = self.request("GET", "/v1/players/search?q=SEARCHABLE", token=one["token"])
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


    def test_player_profile_lookup_by_permanent_id(self):
        owner = self.register("PROFILE-OWNER")
        viewer = self.register("PROFILE-VIEWER")
        status, profile = self.request("GET", "/v1/players/" + owner["player_id"], token=viewer["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["player_id"], owner["player_id"])
        self.assertEqual(profile["display_name"], "PROFILE-OWNER")


    def test_friend_request_can_be_declined(self):
        sender = self.register("DECLINE-SENDER")
        receiver = self.register("DECLINE-RECEIVER")
        status, _ = self.request("POST", "/v1/friends/request", {"player_id": receiver["player_id"]}, sender["token"])
        self.assertEqual(status, 201)
        status, result = self.request("POST", "/v1/friends/decline", {"player_id": sender["player_id"]}, receiver["token"])
        self.assertEqual(status, 200)
        self.assertEqual(result["status"], "declined")
        status, friends = self.request("GET", "/v1/friends", token=receiver["token"])
        self.assertEqual(friends["incoming"], [])

    def test_block_removes_friendship_and_prevents_new_requests(self):
        one = self.register("BLOCK-ONE")
        two = self.register("BLOCK-TWO")
        self.request("POST", "/v1/friends/request", {"player_id": two["player_id"]}, one["token"])
        self.request("POST", "/v1/friends/accept", {"player_id": one["player_id"]}, two["token"])
        status, result = self.request("POST", "/v1/players/block", {"player_id": two["player_id"]}, one["token"])
        self.assertEqual(status, 200)
        self.assertTrue(result["blocked"])
        status, friends = self.request("GET", "/v1/friends", token=one["token"])
        self.assertEqual(friends["friends"], [])
        status, _ = self.request("POST", "/v1/friends/request", {"player_id": one["player_id"]}, two["token"])
        self.assertEqual(status, 400)
        status, blocked = self.request("GET", "/v1/blocks", token=one["token"])
        self.assertEqual(blocked["players"][0]["player_id"], two["player_id"])
        status, result = self.request("POST", "/v1/players/unblock", {"player_id": two["player_id"]}, one["token"])
        self.assertTrue(result["unblocked"])

    def test_presence_heartbeat_requires_authentication(self):
        status, _ = self.request("POST", "/v1/presence/heartbeat", {})
        self.assertEqual(status, 401)
        player = self.register("HEARTBEAT")
        status, result = self.request("POST", "/v1/presence/heartbeat", {}, player["token"])
        self.assertEqual(status, 200)
        self.assertTrue(result["online"])
        self.assertIsNotNone(result["last_seen"])

    def test_player_search_rejects_too_short_query(self):
        player = self.register("SEARCH")
        status, _ = self.request("GET", "/v1/players/search?q=a", token=player["token"])
        self.assertEqual(status, 400)

    def test_avatar_rejects_mismatched_image_signature(self):
        player = self.register("BAD-AVATAR")
        status, body = self.request("POST", "/v1/profile", {"avatar_data": "data:image/png;base64,aGVsbG8="}, player["token"])
        self.assertEqual(status, 400)
        self.assertEqual(body["error"], "invalid_request")

    def test_token_rotation_invalidates_old_token(self):
        player = self.register("TOKEN-ROTATION")
        status, rotated = self.request("POST", "/v1/account/rotate-token", {}, player["token"])
        self.assertEqual(status, 200)
        self.assertEqual(rotated["player_id"], player["player_id"])
        self.assertNotEqual(rotated["token"], player["token"])
        status, _ = self.request("GET", "/v1/me", token=player["token"])
        self.assertEqual(status, 401)
        status, profile = self.request("GET", "/v1/me", token=rotated["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["player_id"], player["player_id"])

    def test_social_lists_include_avatar_without_exact_last_seen(self):
        one = self.register("AVATAR-ONE")
        two = self.register("AVATAR-TWO")
        avatar = "data:image/png;base64,iVBORw0KGgo="
        self.request("POST", "/v1/profile", {"avatar_data": avatar}, two["token"])
        status, profile = self.request("GET", "/v1/players/" + two["player_id"], token=one["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["avatar_data"], avatar)
        status, image = self.request("GET", "/v1/players/" + two["player_id"] + "/avatar", token=one["token"])
        self.assertEqual(status, 200)
        self.assertEqual(image["avatar_data"], avatar)
        self.assertIn("online", profile)
        self.assertNotIn("last_seen", profile)
        self.request("POST", "/v1/friends/request", {"player_id": two["player_id"]}, one["token"])
        self.request("POST", "/v1/friends/accept", {"player_id": one["player_id"]}, two["token"])
        status, friends = self.request("GET", "/v1/friends", token=one["token"])
        self.assertTrue(friends["friends"][0]["has_avatar"])
        status, search = self.request("GET", "/v1/players/search?q=AVATAR-TWO", token=one["token"])
        self.assertTrue(search["players"][0]["has_avatar"])
        status, board = self.request("GET", "/v1/leaderboard", token=one["token"])
        self.assertTrue(any(entry["player_id"] == two["player_id"] and entry["has_avatar"] for entry in board["entries"]))
        self.assertNotIn("last_seen", board["entries"][0])


    def test_private_room_host_and_guest_receive_same_match(self):
        host = self.register("ROOM-HOST")
        guest = self.register("ROOM-GUEST")
        status, created = self.request("POST", "/v1/rooms/create", {"region": "sa-east"}, host["token"])
        self.assertEqual(status, 201)
        code = created["room"]["room_code"]
        self.assertEqual(len(code), 6)
        status, joined = self.request("POST", "/v1/rooms/join", {"code": code.lower()}, guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(joined["match"]["opponent"]["display_name"], "ROOM-HOST")
        status, host_room = self.request("GET", "/v1/rooms/current", token=host["token"])
        self.assertEqual(status, 200)
        self.assertEqual(host_room["room"]["status"], "matched")
        self.assertEqual(host_room["match"]["match_id"], joined["match"]["match_id"])
        status, guest_match = self.request("GET", "/v1/match", token=guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(guest_match["match"]["match_id"], joined["match"]["match_id"])

    def test_private_room_rejects_host_join_and_reuse(self):
        host = self.register("ROOM-OWNER")
        guest = self.register("ROOM-OTHER")
        _, created = self.request("POST", "/v1/rooms/create", {}, host["token"])
        code = created["room"]["room_code"]
        status, _ = self.request("POST", "/v1/rooms/join", {"code": code}, host["token"])
        self.assertEqual(status, 400)
        status, _ = self.request("POST", "/v1/rooms/join", {"code": code}, guest["token"])
        self.assertEqual(status, 200)
        status, _ = self.request("POST", "/v1/rooms/join", {"code": code}, self.register("ROOM-THIRD")["token"])
        self.assertEqual(status, 400)

    def test_private_room_can_be_cancelled_and_code_becomes_invalid(self):
        host = self.register("ROOM-CANCEL")
        guest = self.register("ROOM-TRY")
        _, created = self.request("POST", "/v1/rooms/create", {}, host["token"])
        code = created["room"]["room_code"]
        status, result = self.request("POST", "/v1/rooms/cancel", {}, host["token"])
        self.assertEqual(status, 200)
        self.assertTrue(result["cancelled"])
        status, _ = self.request("POST", "/v1/rooms/join", {"code": code}, guest["token"])
        self.assertEqual(status, 400)

    def test_private_room_and_random_queue_are_mutually_exclusive(self):
        player = self.register("ROOM-QUEUE")
        other = self.register("QUEUE-OTHER")
        status, _ = self.request("POST", "/v1/queue/join", {"region": "sa-east"}, player["token"])
        self.assertEqual(status, 202)
        status, _ = self.request("POST", "/v1/rooms/create", {}, player["token"])
        self.assertEqual(status, 400)
        self.request("POST", "/v1/queue/leave", {}, player["token"])
        _, _ = self.request("POST", "/v1/rooms/create", {}, player["token"])
        status, _ = self.request("POST", "/v1/queue/join", {"region": "sa-east"}, player["token"])
        self.assertEqual(status, 400)

    def test_private_room_rejects_malformed_code(self):
        guest = self.register("ROOM-BAD-CODE")
        status, _ = self.request("POST", "/v1/rooms/join", {"code": "123"}, guest["token"])
        self.assertEqual(status, 400)


    def test_private_room_code_attempts_are_rate_limited_per_account(self):
        player = self.register("ROOM-RATE-LIMIT")
        for _ in range(20):
            status, _ = self.request("POST", "/v1/rooms/join", {"code": "ZZZZZZ"}, player["token"])
            self.assertEqual(status, 400)
        status, body = self.request("POST", "/v1/rooms/join", {"code": "ZZZZZZ"}, player["token"])
        self.assertEqual(status, 400)
        self.assertIn("too many", body["message"])

    def test_private_room_start_is_host_only_and_visible_to_both_players(self):
        host = self.register("START-HOST")
        guest = self.register("START-GUEST")
        status, created = self.request("POST", "/v1/rooms/create", {"region": "sa-east"}, host["token"])
        self.assertEqual(status, 201)
        code = created["room"]["room_code"]

        status, joined = self.request("POST", "/v1/rooms/join", {"code": code}, guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(joined["match"]["status"], "ready")
        self.assertEqual(joined["match"]["room_role"], "guest")
        self.assertEqual(joined["match"]["room_status"], "matched")

        status, host_room = self.request("GET", "/v1/rooms/current", token=host["token"])
        self.assertEqual(status, 200)
        self.assertEqual(host_room["match"]["room_role"], "host")
        self.assertEqual(host_room["match"]["status"], "ready")

        status, started = self.request("POST", "/v1/rooms/start", {}, host["token"])
        self.assertEqual(status, 200)
        self.assertEqual(started["room"]["status"], "starting")

        status, guest_match = self.request("GET", "/v1/match", token=guest["token"])
        self.assertEqual(status, 200)
        self.assertEqual(guest_match["match"]["room_status"], "starting")

        status, denied = self.request("POST", "/v1/rooms/start", {}, guest["token"])
        self.assertEqual(status, 400)
        self.assertEqual(denied["error"], "invalid_request")

if __name__ == "__main__":
    unittest.main()
