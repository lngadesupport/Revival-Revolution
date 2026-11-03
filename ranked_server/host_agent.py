"""Background agent installed as a per-user Windows logon task.

It waits for the configured Radmin address, starts the local Ranked API, and
restarts it if the process exits. No user-operated script or console is needed.
"""
from __future__ import annotations

import argparse
import logging
import os
import subprocess
import sys
import time
from pathlib import Path
from urllib.error import URLError
from urllib.request import urlopen


def has_local_address(address: str) -> bool:
    command = [
        "powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
        f"(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | "
        f"Where-Object {{ $_.IPAddress -eq '{address}' }} | Select-Object -First 1 -ExpandProperty IPAddress)",
    ]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=8, check=False,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return address in result.stdout.split()
    except (OSError, subprocess.SubprocessError):
        return False


def healthy(address: str, port: int) -> bool:
    try:
        with urlopen(f"http://{address}:{port}/health", timeout=2) as response:
            return response.status == 200
    except (OSError, URLError, TimeoutError):
        return False


def main() -> None:
    parser = argparse.ArgumentParser(description="Revival Revolution Radmin host agent")
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--database", required=True)
    parser.add_argument("--root", required=True)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    database = Path(args.database).resolve()
    database.parent.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(
        filename=str(database.with_suffix(".agent.log")),
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        encoding="utf-8",
    )

    python = Path(sys.executable).with_name("python.exe")
    child: subprocess.Popen | None = None
    unhealthy_since: float | None = None
    logging.info("Host agent started; waiting for Radmin IPv4 %s", args.host)

    while True:
        available = has_local_address(args.host)
        if not available:
            if child is not None and child.poll() is None:
                logging.warning("Radmin address disappeared; stopping local API.")
                child.terminate()
                try:
                    child.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    child.kill()
            child = None
            time.sleep(5)
            continue

        if healthy(args.host, args.port):
            unhealthy_since = None
            if child is not None and child.poll() is not None:
                child = None
            time.sleep(5)
            continue

        if child is not None and child.poll() is None:
            unhealthy_since = unhealthy_since or time.monotonic()
            if time.monotonic() - unhealthy_since > 30:
                logging.warning("Ranked API process is alive but unhealthy; restarting it.")
                child.terminate()
                try:
                    child.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    child.kill()
                child = None
                unhealthy_since = None

        if child is None or child.poll() is not None:
            command = [
                str(python), "-m", "ranked_server",
                "--host", args.host, "--port", str(args.port),
                "--database", str(database),
            ]
            log = open(database.with_suffix(".log"), "a", encoding="utf-8")
            try:
                child = subprocess.Popen(
                    command, cwd=str(root), stdin=subprocess.DEVNULL,
                    stdout=log, stderr=subprocess.STDOUT,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
                logging.info("Ranked API process started (pid=%s)", child.pid)
                log.close()
            except OSError:
                log.close()
                logging.exception("Unable to start Ranked API process")
                child = None
        time.sleep(5)


if __name__ == "__main__":
    main()
