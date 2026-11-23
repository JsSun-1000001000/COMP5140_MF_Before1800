"""Start the local dialogue service, then the Windows game; close both together."""
import argparse
import getpass
import os
from pathlib import Path
import socket
import subprocess
import sys
import time
import warnings

import httpx
import uvicorn


class KeyRejected(Exception):
    """Model discovery could not authenticate this credential."""


def select_deepseek_model(key):
    try:
        response = httpx.get("https://api.deepseek.com/models", headers={"Authorization": "Bearer " + key}, timeout=12)
        response.raise_for_status()
        available = {item["id"] for item in response.json()["data"]}
    except httpx.HTTPStatusError as exc:
        status = exc.response.status_code
        if status in (401, 403):
            raise KeyRejected(status) from None
        raise SystemExit(f"DeepSeek model discovery failed (HTTP {status}).") from None
    except (httpx.RequestError, ValueError, KeyError, TypeError):
        raise SystemExit("Could not read DeepSeek's model list. Check the network.") from None
    for candidate in ("deepseek-flash", "deepseek-v4-flash", "deepseek-chat"):
        if candidate in available:
            return candidate
    raise SystemExit("No supported DeepSeek chat model was listed for this account.")


def dialogue_credentials():
    key = os.environ.get("LLM_API_KEY", "").strip()
    if key:
        try:
            return key, select_deepseek_model(key)
        except KeyRejected as exc:
            print(f"DeepSeek rejected LLM_API_KEY (HTTP {exc}). Enter a replacement for this session.", file=sys.stderr)

    try:
        # Refuse getpass's echoing fallback if the launcher lacks a terminal.
        with warnings.catch_warnings():
            warnings.simplefilter("error", getpass.GetPassWarning)
            key = getpass.getpass("DeepSeek API key (hidden): ").strip()
    except (EOFError, getpass.GetPassWarning):
        raise SystemExit("Open Play-With-Lin.cmd in a terminal to enter the key privately.") from None
    if not key:
        raise SystemExit("A key is required for live dialogue. Launch Before1800.exe for offline play.")
    try:
        return key, select_deepseek_model(key)
    except KeyRejected as exc:
        raise SystemExit(f"DeepSeek rejected the entered key (HTTP {exc}).") from None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path)
    parser.add_argument("--serve-only", action="store_true")
    parser.add_argument("--port", type=int, default=0)
    args = parser.parse_args()
    if not args.serve_only and (not args.game or not args.game.is_file()):
        parser.error("Supply the Windows executable with --game.")
    # A newly entered secret is only copied into the gateway child's environment.
    key, model = dialogue_credentials()
    gateway_env = os.environ.copy()
    gateway_env.update(LLM_BASE_URL="https://api.deepseek.com/v1", LLM_PROVIDER="deepseek",
                       LLM_API_KEY=key, LLM_MODEL=model, LLM_TIMEOUT_SECONDS="22")
    if args.serve_only:
        os.environ.update(gateway_env)
        uvicorn.run("app.main:app", host="127.0.0.1", port=args.port or 18761, access_log=False)
        return

    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", args.port))
        port = reservation.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    gateway = subprocess.Popen([sys.executable, "-m", "uvicorn", "app.main:app", "--host", "127.0.0.1",
                                "--port", str(port), "--no-access-log"],
                               env=gateway_env, cwd=Path(__file__).resolve().parent)
    try:
        with httpx.Client(trust_env=False, timeout=1) as local:
            for _ in range(55):
                if gateway.poll() is not None:
                    raise SystemExit("Local dialogue service stopped during startup.")
                try:
                    if local.get(base + "/health").json().get("ready"):
                        break
                except (httpx.RequestError, ValueError):
                    pass
                time.sleep(.2)
            else:
                raise SystemExit("Local dialogue service did not become ready.")
        game_env = {k: v for k, v in os.environ.items() if k.upper() not in {"LLM_API_KEY", "DEEPSEEK_API_KEY"}}
        subprocess.run([str(args.game.resolve()), "--lin-gateway=" + base], cwd=args.game.resolve().parent,
                       env=game_env, check=True)
    finally:
        gateway.terminate()
        gateway.wait()


if __name__ == "__main__":
    main()
