"""Small standard-library HTTP API for the Ranked matchmaking MVP.

Run behind HTTPS in production. This service intentionally does not accept match
results or modify ratings: the current client-reported peer completion is not
server-authoritative enough for competitive rating changes.
"""
from __future__ import annotations

import argparse
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from .storage import RankedStore
from .social import SocialStore


def make_handler(store: RankedStore):
    social = SocialStore(store.database, clock=store.clock)

    class Handler(BaseHTTPRequestHandler):
        server_version = "RevivalRanked/0.1"

        def log_message(self, fmt, *args):
            # Keep access logs useful without logging Authorization headers.
            super().log_message(fmt, *args)

        def _json(self, status: int, payload: dict):
            body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def _body(self):
            try:
                size = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                raise ValueError("invalid content length")
            if not 0 < size <= 400000:
                raise ValueError("request body must be 1..4096 bytes")
            try:
                value = json.loads(self.rfile.read(size))
            except (json.JSONDecodeError, UnicodeDecodeError):
                raise ValueError("invalid JSON")
            if not isinstance(value, dict):
                raise ValueError("JSON object required")
            return value

        def _player(self):
            header = self.headers.get("Authorization", "")
            if not header.startswith("Bearer "):
                return None
            return store.authenticate(header[7:].strip())

        def do_GET(self):
            path = urlparse(self.path).path
            if path == "/health":
                return self._json(200, {"ok": True, "service": "revival-ranked", "version": "0.1"})
            player = self._player()
            if player is None:
                return self._json(401, {"error": "authentication_required"})
            if path == "/v1/me":
                return self._json(200, store.player_profile(player["player_id"]))
            if path == "/v1/queue":
                return self._json(200, store.queue_status(player["player_id"]))
            if path == "/v1/match":
                return self._json(200, {"match": store.current_match(player["player_id"])})
            return self._json(404, {"error": "not_found"})

        def do_POST(self):
            path = urlparse(self.path).path
            try:
                body = self._body()
                if path == "/v1/register":
                    name = body.get("display_name")
                    result = store.register(name)
                    return self._json(201, result)
                player = self._player()
                if player is None:
                    return self._json(401, {"error": "authentication_required"})
                player_id = player["player_id"]
                if path == "/v1/profile":
                    result = social.update_profile(
                        player_id,
                        display_name=body.get("display_name"),
                        avatar_data=body.get("avatar_data"),
                        update_avatar="avatar_data" in body,
                    )
                    return self._json(200, result)
                if path == "/v1/friends/request":
                    result = social.request_friend(player_id, body.get("player_id"))
                    return self._json(201 if result["status"] == "pending" else 200, result)
                if path == "/v1/friends/accept":
                    return self._json(200, social.accept_friend(player_id, body.get("player_id")))
                if path == "/v1/friends/remove":
                    return self._json(200, {"removed": social.remove_friend(player_id, body.get("player_id"))})
                if path == "/v1/queue/join":
                    result = store.join_queue(player_id, body.get("region", "global"))
                    if result:
                        return self._json(201, {"match": result})
                    return self._json(202, {"queued": True, "queue": store.queue_status(player_id)})
                if path == "/v1/queue/leave":
                    return self._json(200, {"left": store.leave_queue(player_id)})
                return self._json(404, {"error": "not_found"})
            except ValueError as exc:
                return self._json(400, {"error": "invalid_request", "message": str(exc)})

    return Handler


def main():
    parser = argparse.ArgumentParser(description="Revival Revolution Ranked API")
    parser.add_argument("--host", default=os.environ.get("RANKED_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("RANKED_PORT", "8765")))
    parser.add_argument("--database", default=os.environ.get("RANKED_DATABASE", "ranked.sqlite3"))
    args = parser.parse_args()
    store = RankedStore(args.database)
    server = ThreadingHTTPServer((args.host, args.port), make_handler(store))
    print(f"Revival Ranked API listening on {args.host}:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
