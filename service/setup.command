#!/bin/zsh
set -eu
service_dir="${0:A:h}"
cd "$service_dir"
if [[ ! -x .venv/bin/python ]]; then
  selected_python=''
  for candidate in "${BEFORE1800_PYTHON:-python3}" python3.13 python3.12 python3.11 "$HOME/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3"; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3,11) else 1)' >/dev/null 2>&1; then
      selected_python="$candidate"
      break
    fi
  done
  if [[ -z "$selected_python" ]]; then
    echo '需要 Python 3.11 或更新版本。安装后重新打开本文件。'
    exit 1
  fi
  "$selected_python" -m venv .venv
fi
.venv/bin/python -m pip install -r requirements.txt
echo '本地对话服务依赖已准备好。'
