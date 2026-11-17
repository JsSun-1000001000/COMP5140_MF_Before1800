from __future__ import annotations

import os
from pathlib import Path
from urllib.parse import urlsplit

from dotenv import dotenv_values
from pydantic import BaseModel, ConfigDict, Field, SecretStr, field_validator

SERVICE_DIR = Path(__file__).resolve().parents[1]


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid")
    base_url: str = "https://api.openai.com/v1"
    model: str = ""
    api_key: SecretStr = SecretStr("")
    provider: str = "openai-compatible"
    response_format: str = "json_object"
    timeout_seconds: float = Field(default=30, ge=0.1, le=180)
    max_output_tokens: int = Field(default=600, ge=64, le=4096)
    token_limit_parameter: str = "max_tokens"
    cache_ttl_seconds: float = Field(default=900, ge=1, le=86400)
    max_cache_entries: int = Field(default=256, ge=1, le=4096)
    max_inflight: int = Field(default=4, ge=1, le=16)
    log_dir: Path = SERVICE_DIR / "logs"

    @field_validator("base_url")
    @classmethod
    def safe_base_url(cls, value: str) -> str:
        value = value.strip().rstrip("/")
        parsed = urlsplit(value)
        local = parsed.hostname in ("localhost", "127.0.0.1", "::1")
        if not parsed.hostname or (parsed.scheme != "https" and not (parsed.scheme == "http" and local)):
            raise ValueError("Use HTTPS, or HTTP for a loopback provider")
        if parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise ValueError("Base URL must not contain credentials, query or fragment")
        if parsed.path.endswith("/chat/completions"):
            raise ValueError("Set the API base URL (usually ending /v1), not the completion endpoint")
        return value

    @field_validator("response_format")
    @classmethod
    def format_mode(cls, value: str) -> str:
        if value not in ("json_object", "json_schema", "off"):
            raise ValueError("LLM_RESPONSE_FORMAT must be json_object, json_schema or off")
        return value

    @field_validator("token_limit_parameter")
    @classmethod
    def token_parameter(cls, value: str) -> str:
        if value not in ("max_tokens", "max_completion_tokens", "off"):
            raise ValueError("Unsupported token limit parameter")
        return value

    @field_validator("model", "provider")
    @classmethod
    def bounded_name(cls, value: str) -> str:
        value = value.strip()
        if len(value) > 200 or any(ord(c) < 32 for c in value):
            raise ValueError("Invalid model/provider name")
        return value

    @property
    def missing_config(self) -> list[str]:
        missing = []
        if not self.api_key.get_secret_value().strip():
            missing.append("LLM_API_KEY")
        if not self.model:
            missing.append("LLM_MODEL")
        return missing

    @classmethod
    def from_environment(cls, env_file: Path | None = None) -> Settings:
        # Only read our explicit local configuration. Never search other credential stores.
        file_values = dotenv_values(env_file or SERVICE_DIR / ".env")
        mapping = {
            "LLM_BASE_URL": "base_url", "LLM_MODEL": "model", "LLM_API_KEY": "api_key",
            "LLM_PROVIDER": "provider", "LLM_RESPONSE_FORMAT": "response_format",
            "LLM_TIMEOUT_SECONDS": "timeout_seconds", "LLM_MAX_OUTPUT_TOKENS": "max_output_tokens",
            "LLM_TOKEN_LIMIT_PARAMETER": "token_limit_parameter", "CACHE_TTL_SECONDS": "cache_ttl_seconds",
            "CACHE_MAX_ENTRIES": "max_cache_entries", "MAX_INFLIGHT": "max_inflight", "LOG_DIR": "log_dir",
        }
        values = {}
        for external, internal in mapping.items():
            value = os.environ.get(external, file_values.get(external))
            if value is not None:
                values[internal] = value
        if "log_dir" in values:
            path = Path(values["log_dir"])
            values["log_dir"] = path if path.is_absolute() else SERVICE_DIR / path
        return cls.model_validate(values)
