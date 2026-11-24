#!/bin/zsh
set -eu
project_dir="${0:A:h}"
cd "$project_dir/service"
if [[ ! -x .venv/bin/python ]]; then
  ./setup.command
fi
if ! .venv/bin/python play_mac.py "$@"; then
  read -r '?按回车关闭'
  exit 1
fi
