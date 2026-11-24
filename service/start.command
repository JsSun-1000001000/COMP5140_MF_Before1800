#!/bin/zsh
set -eu
service_dir="${0:A:h}"
cd "$service_dir"
if [[ ! -x .venv/bin/python ]]; then
  echo '首次启动：正在准备本地对话服务。'
  ./setup.command
fi
if [[ ! -f .env ]]; then
  echo '尚未配置对话模型。请先运行 Configure-LLM.command。'
  read -r '?按回车关闭'; exit 1
fi
echo '林的对话服务正在运行。保留此窗口；关闭会断开模型。'
echo '游戏会自动连接；以后可直接双击项目里的 Play-Before1800.command。'
exec .venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8180 --workers 1 --no-access-log
