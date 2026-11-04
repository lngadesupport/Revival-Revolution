"""Small standard-library HTTP API for the Ranked matchmaking MVP.

Run behind HTTPS in production. This service intentionally does not accept match
results or modify ratings: the current client-reported peer completion is not
server-authoritative enough for competitive rating changes.
"""
from __future__ import annotations

import argparse
import json
import logging
import os
import hmac
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from .storage import RankedStore
from .social import SocialStore
from .competitive import CompetitiveStore


def make_handler(store: RankedStore):
    social = SocialStore(store.database, clock=store.clock)
    competitive = CompetitiveStore(store.database, clock=store.clock)

    class Handler(BaseHTTPRequestHandler):
        server_version = "RevivalRanked/0.8.1"

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
                raise ValueError("request body must be 1..400000 bytes")
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
            parsed = urlparse(self.path)
            path = parsed.path
            if path == "/health":
                return self._json(200, {"ok": True, "service": "revival-ranked", "version": "0.8.1"})
            player = self._player()
            if player is None:
                return self._json(401, {"error": "authentication_required"})
            player_id = player["player_id"]
            if path == "/v1/me":
                return self._json(200, social.profile(player_id))
            if path == "/v1/ranked/me":
                summary = competitive.player_summary(player_id)
                summary["recent_matches"] = competitive.match_history(player_id)
                return self._json(200, summary)
            if path == "/v1/ranked/leaderboard":
                query = parse_qs(parsed.query)
                try:
                    limit = int(query.get("limit", ["100"])[0])
                    return self._json(200, {"season_id": "season-1", "entries": competitive.leaderboard(limit)})
                except (TypeError, ValueError) as exc:
                    return self._json(400, {"error": "invalid_request", "message": str(exc)})
            if path == "/v1/friends":
                return self._json(200, social.friends(player_id))
            if path == "/v1/blocks":
                return self._json(200, {"players": social.blocked_players(player_id)})
            if path.startswith("/v1/players/") and path.endswith("/avatar"):
                target_id = path[len("/v1/players/"):-len("/avatar")].strip("/")
                if not target_id or "/" in target_id:
                    return self._json(404, {"error": "not_found"})
                try:
                    return self._json(200, social.avatar(target_id))
                except ValueError:
                    return self._json(404, {"error": "player_not_found"})
            if path.startswith("/v1/players/") and path != "/v1/players/search":
                target_id = path[len("/v1/players/"):]
                if not target_id or "/" in target_id:
                    return self._json(404, {"error": "not_found"})
                try:
                    return self._json(200, social.profile(target_id))
                except ValueError:
                    return self._json(404, {"error": "player_not_found"})
            if path == "/v1/players/search":
                query = parse_qs(parsed.query)
                try:
                    return self._json(200, {"players": social.search_players(query.get("q", [""])[0])})
                except ValueError as exc:
                    return self._json(400, {"error": "invalid_request", "message": str(exc)})
            if path == "/v1/leaderboard":
                query = parse_qs(parsed.query)
                try:
                    limit = int(query.get("limit", ["100"])[0])
                except ValueError:
                    return self._json(400, {"error": "invalid_request", "message": "limit must be an integer"})
                return self._json(200, {"entries": social.leaderboard(limit)})
            if path in ("/v1/queue", "/v1/ranked/queue"):
                mode = "ranked" if path == "/v1/ranked/queue" else "casual"
                return self._json(200, store.queue_status(player_id, mode=mode))
            if path in ("/v1/match", "/v1/ranked/match"):
                mode = "ranked" if path == "/v1/ranked/match" else "casual"
                return self._json(200, {"match": store.current_match(player_id, mode=mode)})
            if path == "/v1/rooms/current":
                return self._json(200, store.current_private_room(player_id))
            return self._json(404, {"error": "not_found"})
        def do_POST(self):
            path = urlparse(self.path).path
            try:
                is_internal_result = path.startswith("/internal/v1/matches/") and path.endswith("/result")
                if is_internal_result:
                    # Authority must authenticate before an untrusted body is parsed.
                    authority_secret = os.environ.get("RANKED_RESULT_SECRET", "")
                    if not authority_secret:
                        return self._json(503, {"error": "result_authority_not_configured"})
                    supplied = self.headers.get("X-Ranked-Authority", "")
                    if not hmac.compare_digest(supplied, authority_secret):
                        return self._json(403, {"error": "result_authority_forbidden"})
                body = self._body()
                if is_internal_result:
                    match_id = path[len("/internal/v1/matches/"):-len("/result")].strip("/")
                    if not match_id or "/" in match_id:
                        return self._json(404, {"error": "not_found"})
                    result = competitive.record_authoritative_result(
                        match_id, body.get("winner_player_id"),
                        is_draw=body.get("draw", False),
                        authority_event_id=body.get("authority_event_id", ""),
                    )
                    return self._json(200, {"result": result})
                if path == "/v1/register":
                    name = body.get("display_name")
                    result = store.register(name)
                    return self._json(201, result)
                player = self._player()
                if player is None:
                    return self._json(401, {"error": "authentication_required"})
                player_id = player["player_id"]
                if path == "/v1/account/rotate-token":
                    return self._json(200, store.rotate_token(player_id))
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
                if path == "/v1/friends/decline":
                    return self._json(200, social.decline_friend(player_id, body.get("player_id")))
                if path == "/v1/players/block":
                    return self._json(200, social.block_player(player_id, body.get("player_id")))
                if path == "/v1/players/unblock":
                    return self._json(200, social.unblock_player(player_id, body.get("player_id")))
                if path == "/v1/presence/heartbeat":
                    return self._json(200, {"online": True, "last_seen": player.get("last_seen")})
                if path == "/v1/friends/remove":
                    return self._json(200, {"removed": social.remove_friend(player_id, body.get("player_id"))})
                if path in ("/v1/queue/join", "/v1/ranked/queue/join"):
                    mode = "ranked" if path == "/v1/ranked/queue/join" else "casual"
                    result = store.join_queue(player_id, body.get("region", "global"), mode=mode)
                    if result:
                        return self._json(201, {"match": result})
                    return self._json(202, {"queued": True, "queue": store.queue_status(player_id, mode=mode)})
                if path in ("/v1/queue/leave", "/v1/ranked/queue/leave"):
                    mode = "ranked" if path == "/v1/ranked/queue/leave" else "casual"
                    return self._json(200, {"left": store.leave_queue(player_id, mode=mode)})
                if path == "/v1/match/accept":
                    return self._json(200, {"match": store.accept_match(player_id, mode="casual")})
                if path == "/v1/ranked/match/accept":
                    return self._json(200, {"match": store.accept_match(player_id, mode="ranked")})
                if path == "/v1/match/decline":
                    return self._json(200, store.decline_match(player_id, mode="casual"))
                if path == "/v1/ranked/match/decline":
                    return self._json(200, store.decline_match(player_id, mode="ranked"))
                if path == "/v1/match/finish":
                    return self._json(200, store.confirm_match_finished(player_id, mode="casual"))
                if path == "/v1/ranked/match/finish":
                    return self._json(200, store.confirm_match_finished(player_id, mode="ranked"))
                if path == "/v1/match/accept":
                    return self._json(200, {"match": store.accept_match(player_id)})
                if path == "/v1/match/decline":
                    return self._json(200, store.decline_match(player_id))
                if path == "/v1/match/finish":
                    return self._json(200, store.confirm_match_finished(player_id))
                if path == "/v1/rooms/create":
                    room = store.create_private_room(player_id, body.get("region", "sa-east"))
                    return self._json(201, {"room": room})
                if path == "/v1/rooms/join":
                    match = store.join_private_room(player_id, body.get("code"))
                    return self._json(200, {"match": match})
                if path == "/v1/rooms/start":
                    return self._json(200, {"room": store.start_private_room(player_id)})
                if path == "/v1/rooms/connection-code":
                    return self._json(200, store.publish_private_room_code(player_id, body.get("code")))
                if path == "/v1/rooms/cancel":
                    return self._json(200, {"cancelled": store.cancel_private_room(player_id)})
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
    database_path = Path(args.database).expanduser().resolve()
    database_path.parent.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(
        filename=str(Path(str(database_path) + ".log")),
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        encoding="utf-8",
    )
    store = RankedStore(str(database_path))
    server = ThreadingHTTPServer((args.host, args.port), make_handler(store))
    logging.info("Revival Ranked API listening on %s:%s", args.host, args.port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
