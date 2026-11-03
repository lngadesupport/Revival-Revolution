import json
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

from ranked_server.server import make_handler
from ranked_server.storage import RankedStore


class RankedHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = RankedStore(Path(self.temp.name) / "ranked.sqlite3")
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(self.store))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base_url = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def request(self, method, path, payload=None, token=None):
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = {}
        if body is not None:
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = urllib.request.Request(
            self.base_url + path, data=body, headers=headers, method=method
        )
        try:
            with urllib.request.urlopen(request, timeout=3) as response:
                return response.status, json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read().decode("utf-8"))

    def test_health_endpoint_reports_expected_service_version(self):
        status, payload = self.request("GET", "/health")
        self.assertEqual(status, 200)
        self.assertEqual(payload, {
            "ok": True, "service": "revival-ranked", "version": "0.8.1"
        })

    def test_registration_and_authenticated_profile_round_trip(self):
        status, account = self.request(
            "POST", "/v1/register", {"display_name": "HTTP-TEST-PLAYER"}
        )
        self.assertEqual(status, 201)
        self.assertTrue(account["token"])
        status, profile = self.request("GET", "/v1/me", token=account["token"])
        self.assertEqual(status, 200)
        self.assertEqual(profile["player_id"], account["player_id"])
        self.assertEqual(profile["display_name"], "HTTP-TEST-PLAYER")

    def test_match_accept_and_finish_lifecycle_over_http(self):
        _, one = self.request("POST", "/v1/register", {"display_name": "HTTP-PLAYER-ONE"})
        _, two = self.request("POST", "/v1/register", {"display_name": "HTTP-PLAYER-TWO"})

        status, _ = self.request(
            "POST", "/v1/queue/join", {"region": "sa-east"}, token=one["token"]
        )
        self.assertEqual(status, 202)
        status, assigned = self.request(
            "POST", "/v1/queue/join", {"region": "sa-east"}, token=two["token"]
        )
        self.assertEqual(status, 201)
        self.assertEqual(assigned["match"]["status"], "assigned")

        status, first_accept = self.request(
            "POST", "/v1/match/accept", {}, token=one["token"]
        )
        self.assertEqual(status, 200)
        self.assertEqual(first_accept["match"]["status"], "assigned")
        status, second_accept = self.request(
            "POST", "/v1/match/accept", {}, token=two["token"]
        )
        self.assertEqual(status, 200)
        self.assertEqual(second_accept["match"]["status"], "ready")

        status, first_finish = self.request(
            "POST", "/v1/match/finish", {}, token=one["token"]
        )
        self.assertEqual(status, 200)
        self.assertEqual(first_finish["status"], "awaiting_opponent_confirmation")
        self.assertFalse(first_finish["rating_changed"])
        status, second_finish = self.request(
            "POST", "/v1/match/finish", {}, token=two["token"]
        )
        self.assertEqual(status, 200)
        self.assertEqual(second_finish["status"], "completed")
        self.assertFalse(second_finish["rating_changed"])


    def test_private_endpoints_reject_missing_bearer_token(self):
        status, payload = self.request("GET", "/v1/me")
        self.assertEqual(status, 401)
        self.assertEqual(payload["error"], "authentication_required")


if __name__ == "__main__":
    unittest.main()
