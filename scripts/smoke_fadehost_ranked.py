#!/usr/bin/env python3
"""Explicit production smoke test for the Revival Ranked API.

Creates two clearly named smoke accounts and one completed smoke match.
The API currently has no account/match deletion endpoint, so these records
remain in the production SQLite database for auditability.
"""
from __future__ import annotations

import json
import os
import secrets
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone


def request(base: str, method: str, path: str, *, payload=None, token=None):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            raw = response.read()
            body = json.loads(raw.decode("utf-8")) if raw else {}
            return response.status, body
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        try:
            body = json.loads(raw.decode("utf-8")) if raw else {}
        except (UnicodeDecodeError, json.JSONDecodeError):
            body = {"error": "non_json_error"}
        return exc.code, body


def require(condition: bool, message: str):
    if not condition:
        raise RuntimeError(message)


def main():
    base = (os.environ.get("RANKED_API_URL") or "").strip().rstrip("/")
    if not base.startswith("https://"):
        raise SystemExit("RANKED_API_URL must be an HTTPS URL")
    if not base.endswith(".fadehost.app") and os.environ.get("ALLOW_NON_FADEHOST") != "1":
        raise SystemExit("Refusing non-FadeHost host; set ALLOW_NON_FADEHOST=1 only for an intentional alternate target")

    status, health = request(base, "GET", "/health")
    require(status == 200, f"health returned HTTP {status}; response={str(health)[:1200]}")
    require(health == {"ok": True, "service": "revival-ranked", "version": "0.8.1"},
            f"unexpected health payload: {health}")
    print("PASS health/version 0.8.1")

    stamp = datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S")
    suffix = secrets.token_hex(3).upper()
    accounts = []
    completed = False
    try:
        for slot in ("A", "B"):
            name = f"RR-SMOKE-{stamp}-{suffix}-{slot}"
            status, account = request(base, "POST", "/v1/register", payload={"display_name": name})
            require(status == 201, f"register {slot} returned HTTP {status}: {account.get('error')}")
            require(account.get("token") and account.get("player_id"), f"register {slot} omitted credentials")
            accounts.append(account)
            status, profile = request(base, "GET", "/v1/me", token=account["token"])
            require(status == 200, f"profile {slot} returned HTTP {status}")
            require(profile.get("player_id") == account["player_id"], f"profile {slot} identity mismatch")
            require(profile.get("display_name") == name, f"profile {slot} nickname mismatch")
        print("PASS two temporary accounts and authenticated profiles")

        status, queued = request(base, "POST", "/v1/queue/join",
                                 payload={"region": "sa-east"}, token=accounts[0]["token"])
        require(status == 202 and queued.get("queued") is True,
                f"first queue join returned HTTP {status}: {queued}")
        status, assigned = request(base, "POST", "/v1/queue/join",
                                   payload={"region": "sa-east"}, token=accounts[1]["token"])
        require(status == 201 and assigned.get("match", {}).get("status") == "assigned",
                f"second queue join returned HTTP {status}: {assigned}")
        match_id = assigned["match"]["match_id"]

        for slot, account in zip(("A", "B"), accounts):
            status, current = request(base, "GET", "/v1/match", token=account["token"])
            match = current.get("match")
            require(status == 200 and match and match.get("match_id") == match_id,
                    f"match lookup {slot} did not return the same match")
        print("PASS matchmaking assigned the same match to both accounts")

        status, first_accept = request(base, "POST", "/v1/match/accept", payload={}, token=accounts[0]["token"])
        require(status == 200 and first_accept.get("match", {}).get("status") == "assigned",
                f"first acceptance unexpected: HTTP {status}")
        status, second_accept = request(base, "POST", "/v1/match/accept", payload={}, token=accounts[1]["token"])
        require(status == 200 and second_accept.get("match", {}).get("status") == "ready",
                f"second acceptance did not make match ready: HTTP {status}")

        status, first_finish = request(base, "POST", "/v1/match/finish", payload={}, token=accounts[0]["token"])
        require(status == 200 and first_finish.get("status") == "awaiting_opponent_confirmation",
                f"first finish unexpected: HTTP {status}")
        status, second_finish = request(base, "POST", "/v1/match/finish", payload={}, token=accounts[1]["token"])
        require(status == 200 and second_finish.get("status") == "completed",
                f"second finish did not complete match: HTTP {status}")
        require(first_finish.get("rating_changed") is False and second_finish.get("rating_changed") is False,
                "smoke test unexpectedly changed rating")
        print("PASS bilateral accept/finish lifecycle; rating unchanged")

        status, room_result = request(base, "POST", "/v1/rooms/create",
                                      payload={"region": "sa-east"}, token=accounts[0]["token"])
        require(status == 201 and room_result.get("room", {}).get("room_code"),
                f"private room creation failed: HTTP {status}")
        status, current_room = request(base, "GET", "/v1/rooms/current", token=accounts[0]["token"])
        require(status == 200 and current_room.get("room", {}).get("room_code") == room_result["room"]["room_code"],
                "private room lookup mismatch")
        status, cancelled = request(base, "POST", "/v1/rooms/cancel", payload={}, token=accounts[0]["token"])
        require(status == 200 and cancelled.get("cancelled") is True,
                f"private room cancellation failed: HTTP {status}")
        print("PASS private room create/read/cancel")

        status, queue_again = request(base, "POST", "/v1/queue/join",
                                       payload={"region": "sa-east"}, token=accounts[0]["token"])
        require(status == 202 and queue_again.get("queued") is True,
                f"requeue failed: HTTP {status}")
        status, left = request(base, "POST", "/v1/queue/leave", payload={}, token=accounts[0]["token"])
        require(status == 200 and left.get("left") is True, f"queue leave failed: HTTP {status}")
        print("PASS queue join/leave")

        completed = True
        print(f"PRODUCTION_SMOKE_OK match_id={match_id}")
        print("NOTE: two RR-SMOKE accounts and one completed smoke match remain in the database.")
    finally:
        if not completed:
            for account in accounts:
                token = account.get("token")
                if not token:
                    continue
                # Best-effort cleanup of transient queue/assignment state. There
                # is intentionally no destructive account deletion endpoint.
                try:
                    request(base, "POST", "/v1/queue/leave", payload={}, token=token)
                    request(base, "POST", "/v1/match/decline", payload={}, token=token)
                    request(base, "POST", "/v1/rooms/cancel", payload={}, token=token)
                except Exception:
                    pass


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"PRODUCTION_SMOKE_FAILED: {type(exc).__name__}: {exc}", file=sys.stderr)
        raise SystemExit(1)
