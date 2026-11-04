import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import offline_launcher


KEYS = (
    "REVIVAL_UNITY_SCREEN_FULLSCREEN",
    "REVIVAL_UNITY_SCREEN_WIDTH",
    "REVIVAL_UNITY_SCREEN_HEIGHT",
)


def run_with(**values):
    previous = {key: os.environ.get(key) for key in KEYS}
    try:
        for key in KEYS:
            value = values.get(key)
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value
        return offline_launcher.unity_launch_args()
    finally:
        for key, value in previous.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value


assert run_with() == ["-screen-fullscreen", "1"]
assert run_with(
    REVIVAL_UNITY_SCREEN_FULLSCREEN="0",
    REVIVAL_UNITY_SCREEN_WIDTH="1920",
    REVIVAL_UNITY_SCREEN_HEIGHT="1080",
) == ["-screen-fullscreen", "0", "-screen-width", "1920", "-screen-height", "1080"]
assert run_with(
    REVIVAL_UNITY_SCREEN_FULLSCREEN="invalid",
    REVIVAL_UNITY_SCREEN_WIDTH="3440",
    REVIVAL_UNITY_SCREEN_HEIGHT="1440",
) == ["-screen-fullscreen", "1"]
assert run_with(
    REVIVAL_UNITY_SCREEN_FULLSCREEN="1",
    REVIVAL_UNITY_SCREEN_WIDTH="1920",
    REVIVAL_UNITY_SCREEN_HEIGHT="900",
) == ["-screen-fullscreen", "1"]

print("Offline launcher Unity display option tests passed.")
