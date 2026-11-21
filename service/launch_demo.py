"""Start the local DeepSeek service and the Windows greybox demo."""
import json
from pathlib import Path
import subprocess
import sys
import time
from urllib.request import urlopen
from urllib.error import URLError

SERVICE = Path(__file__).resolve().parent
PLAYER = SERVICE.parent / "Builds/PartC/Before1800-PartC.exe"


def health():
    try:
        with urlopen("http://127.0.0.1:8180/health", timeout=1) as response:
            result = json.load(response)
            return result.get("ready") and result.get("provider") == "deepseek"
    except (URLError, OSError, ValueError):
        return False


def main():
    if not PLAYER.is_file():
        sys.exit("Build the Windows player via Unity: Before 1800 > Build Part C Windows Demo.")
    gateway = None
    try:
        if not health():
            gateway = subprocess.Popen(
                [sys.executable, "-m", "uvicorn", "app.main:app", "--host", "127.0.0.1",
                 "--port", "8180", "--workers", "1", "--no-access-log"],
                cwd=SERVICE, creationflags=subprocess.CREATE_NO_WINDOW,
            )
            for _ in range(40):
                if gateway.poll() is not None:
                    sys.exit("Gateway failed to start. Check that port 8180 is free.")
                if health():
                    break
                time.sleep(.25)
            else:
                sys.exit("Gateway is not ready. Check service/.env and the DeepSeek configuration.")
        print("DeepSeek gateway ready. Press F2 in the game to enable live dialogue. F3 shows its log.", flush=True)
        return subprocess.call([str(PLAYER), "-screen-fullscreen", "0", "-screen-width", "1280",
                                "-screen-height", "720"], cwd=PLAYER.parent)
    finally:
        if gateway is not None and gateway.poll() is None:
            gateway.terminate()
            gateway.wait()


if __name__ == "__main__":
    sys.exit(main())
