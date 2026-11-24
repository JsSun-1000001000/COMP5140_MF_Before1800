"""Start the local gateway and the Mac playtest without exposing provider credentials."""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

SERVICE = Path(__file__).resolve().parent
PROJECT = SERVICE.parent
HEALTH = "http://127.0.0.1:8180/health"


def health():
    try:
        # Ignore system HTTP proxies for this strictly local endpoint.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(HEALTH, timeout=2) as response:
            data = json.load(response)
        if not isinstance(data, dict) or "api_key_configured" not in data:
            raise RuntimeError("8180 端口已被其他程序占用，请关闭占用程序后再试。")
        return data
    except urllib.error.HTTPError as exc:
        if exc.code == 503:
            return json.load(exc)
        raise RuntimeError("8180 端口的服务没有正常响应，请检查 service/logs/gateway-console.log。") from None
    except (urllib.error.URLError, TimeoutError, ConnectionError):
        return None


def ensure_service():
    state = health()
    if state is None:
        if not (SERVICE / ".env").is_file():
            raise RuntimeError("尚未配置模型。请先双击 service/Configure-LLM.command。")
        log_dir = SERVICE / "logs"
        log_dir.mkdir(exist_ok=True)
        with (log_dir / "gateway-console.log").open("ab") as log:
            process = subprocess.Popen(
                [sys.executable, "-m", "uvicorn", "app.main:app", "--host", "127.0.0.1",
                 "--port", "8180", "--workers", "1", "--no-access-log"],
                cwd=SERVICE, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                start_new_session=True,
            )
        for _ in range(60):
            if process.poll() is not None:
                raise RuntimeError("本地对话服务未能启动。详情见 service/logs/gateway-console.log。")
            state = health()
            if state:
                break
            time.sleep(0.25)
        if not state:
            raise RuntimeError("本地对话服务启动超时，请稍后重试。")
    if not state.get("ready") or state.get("mock_mode"):
        raise RuntimeError("当前服务不是已配置的真实模型，请检查 service/.env 后重启服务。")
    print("对话服务已连接：" + str(state.get("model", "已配置模型")))
    print("游戏会自动连接。窗口关闭后，本地服务可继续供下次试玩使用。")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-service", action="store_true", help="Start/check the gateway without opening the game.")
    args = parser.parse_args()
    player = PROJECT / "Builds" / "Before1800-Mac" / "Before1800.app"
    if not args.check_service and not player.is_dir():
        raise RuntimeError("Mac 试玩版本尚未生成。请在 Unity 的 Before 18 菜单中生成 Mac player。")
    ensure_service()
    if not args.check_service:
        subprocess.run(["/usr/bin/open", "-a", str(player)], check=True)
        print("《18 点之前》已打开。WASD 移动，E 交互，J 查看记录，Esc 暂停。")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
