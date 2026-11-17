from __future__ import annotations

import json
import logging
import re
from datetime import datetime, timezone
from logging.handlers import RotatingFileHandler
from pathlib import Path
from typing import Any


class TraceRecorder:
    def __init__(self, directory: Path, secret: str = ""):
        directory.mkdir(parents=True, exist_ok=True)
        self.secret = secret
        self.logger = logging.Logger(f"before1800.trace.{id(self)}")
        self.handler = RotatingFileHandler(directory / "requests.jsonl", maxBytes=2_000_000, backupCount=4, encoding="utf-8")
        self.handler.setFormatter(logging.Formatter("%(message)s"))
        self.logger.addHandler(self.handler)
        self.logger.propagate = False

    def redact(self, value: Any) -> Any:
        if isinstance(value, dict):
            return {k: "[REDACTED]" if re.search(r"api.?key|authorization|password|secret|access.?token", k, re.I)
                    else self.redact(v) for k, v in value.items()}
        if isinstance(value, list):
            return [self.redact(v) for v in value]
        if isinstance(value, str):
            if self.secret:
                value = value.replace(self.secret, "[REDACTED]")
            value = re.sub(r"(?i)Bearer\s+[A-Za-z0-9._~+/=-]+", "Bearer [REDACTED]", value)
            value = re.sub(r"\bsk-[A-Za-z0-9_-]{8,}\b", "[REDACTED_KEY]", value)
            value = re.sub(r'''(?i)(["']?(?:api[_-]?key|password|secret|access[_-]?token)["']?\s*[:=]\s*["']?)[^\s,"'\\}]+''', r"\1[REDACTED]", value)
            value = re.sub(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "[REDACTED_EMAIL]", value)
            return value
        return value

    def write(self, event: str, **fields: Any) -> None:
        record = {"utc": datetime.now(timezone.utc).isoformat(), "event": event, **fields}
        self.logger.info(json.dumps(self.redact(record), ensure_ascii=False, allow_nan=False))

    def close(self) -> None:
        self.handler.close()
        self.logger.removeHandler(self.handler)
