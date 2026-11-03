"""Check that a deployed Revival Ranked API responds with the expected health payload.

Usage:
    python scripts/check_render_api.py https://your-service.onrender.com
"""
from __future__ import annotations

import json
import sys
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


EXPECTED_VERSION = "0.8.1"


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: python scripts/check_render_api.py https://SERVICE.onrender.com")
        return 2

    base_url = sys.argv[1].strip().rstrip("/")
    if not base_url.startswith("https://"):
        print("ERROR: use the public HTTPS URL assigned by Render.")
        return 2

    request = Request(
        base_url + "/health",
        headers={"Accept": "application/json", "User-Agent": "RevivalRanked-HealthCheck/0.8.1"},
    )
    try:
        with urlopen(request, timeout=15) as response:
            if response.status != 200:
                print(f"ERROR: /health returned HTTP {response.status}")
                return 1
            payload = json.loads(response.read().decode("utf-8"))
    except (HTTPError, URLError, TimeoutError, ValueError) as exc:
        print(f"ERROR: API health check failed: {exc}")
        return 1

    if payload.get("ok") is not True or payload.get("service") != "revival-ranked":
        print(f"ERROR: unexpected health payload: {payload!r}")
        return 1
    if payload.get("version") != EXPECTED_VERSION:
        print(
            f"ERROR: expected API version {EXPECTED_VERSION}, "
            f"received {payload.get('version')!r}"
        )
        return 1

    print(f"OK: Revival Ranked API {EXPECTED_VERSION} is responding over HTTPS.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
