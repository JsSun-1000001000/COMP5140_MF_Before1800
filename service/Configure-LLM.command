#!/bin/zsh
set -eu
service_dir="${0:A:h}"
cd "$service_dir"
if [[ ! -x .venv/bin/python ]]; then
  ./setup.command
fi
.venv/bin/python configure_llm.py
