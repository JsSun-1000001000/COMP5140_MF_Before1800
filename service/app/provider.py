from __future__ import annotations

import asyncio
import json
import time
from pathlib import Path

import httpx
from pydantic import ValidationError

from .config import Settings
from .errors import ServiceError
from .schemas import Decision, DecisionRequest, DecisionResponse, strict_json_loads, validate_context_action
from .tracing import TraceRecorder


class CompletionProvider:
    def __init__(self, settings: Settings, client: httpx.AsyncClient, trace: TraceRecorder):
        self.settings = settings
        self.client = client
        self.trace = trace
        self.system_prompt = (Path(__file__).resolve().parents[1] / "prompts" / "lin_system.txt").read_text(encoding="utf-8")

    async def decide(self, request: DecisionRequest) -> DecisionResponse:
        if self.settings.missing_config:
            raise ServiceError(503, "not_configured", "Set LLM_API_KEY and LLM_MODEL in service/.env before using real decisions.")
        started = time.monotonic()
        attempts = 0
        identifiers = {key: getattr(request, key) for key in ("session_id", "request_id", "turn_id", "state_version")}
        self.trace.write("request", **identifiers, trigger=request.trigger, player_input=request.player_input,
                         context=request.context.model_dump(mode="json"))
        messages = [
            {"role": "system", "content": self.system_prompt},
            {"role": "user", "content": json.dumps({"turn_id": request.turn_id, "trigger": request.trigger,
                "player_input": request.player_input, "engine_snapshot": request.context.model_dump(mode="json")}, ensure_ascii=False)},
        ]
        try:
            # One end-to-end deadline includes both the original request and the sole repair.
            async with asyncio.timeout(self.settings.timeout_seconds):
                for attempt in (1, 2):
                    attempts = attempt
                    raw = await self._completion(messages)
                    self.trace.write("provider_output", **identifiers, attempt=attempt, raw_output=raw)
                    try:
                        decision = Decision.model_validate(strict_json_loads(raw))
                        validate_context_action(decision, request)
                    except (ValueError, TypeError, RecursionError) as exc:
                        # Do not echo untrusted response text into exception messages or repair instructions.
                        if isinstance(exc, ValidationError):
                            reasons = [{"field": ".".join(map(str, e["loc"])), "type": e["type"]}
                                       for e in exc.errors(include_input=False, include_context=False, include_url=False)]
                        else:
                            reasons = [{"type": "invalid_json_or_action_contract"}]
                        self.trace.write("validation_rejected", **identifiers, attempt=attempt, reasons=reasons)
                        if attempt == 2:
                            raise ServiceError(502, "invalid_model_output", "The provider failed the decision contract after one repair.", attempts=attempt)
                        messages.extend([
                            {"role": "assistant", "content": raw},
                            {"role": "user", "content": "Repair the previous response as ONE JSON object with exactly the six required keys. "
                             "All nullable keys must be present with null. Obey the M1 action/target contract and this snapshot's allowed_actions and known_targets. "
                             "Keep concern_status=keep, concern_id=null and basis_turn_id=null. This is a format/contract repair, not a new player turn. "
                             "Validation issues: " + json.dumps(reasons)},
                        ])
                        continue
                    response = DecisionResponse(**identifiers, decision=decision, provider=self.settings.provider,
                        model=self.settings.model, latency_ms=round((time.monotonic() - started) * 1000), attempts=attempt)
                    self.trace.write("accepted", **identifiers, decision=decision.model_dump(mode="json"),
                                     attempts=attempt, latency_ms=response.latency_ms, provider=self.settings.provider, model=self.settings.model)
                    return response
        except (TimeoutError, httpx.TimeoutException):
            raise ServiceError(504, "provider_timeout", "The provider exceeded the configured decision timeout.", attempts=attempts) from None
        except httpx.RequestError:
            raise ServiceError(502, "provider_unreachable", "The configured provider could not be reached.", attempts=attempts) from None
        except asyncio.CancelledError:
            self.trace.write("cancelled", **identifiers, attempts=attempts)
            raise
        except ServiceError as exc:
            exc.attempts = attempts
            raise
        raise RuntimeError("Unreachable decision path")

    async def _completion(self, messages: list[dict[str, str]]) -> str:
        payload: dict = {"model": self.settings.model, "messages": messages, "stream": False}
        if self.settings.token_limit_parameter != "off":
            payload[self.settings.token_limit_parameter] = self.settings.max_output_tokens
        if self.settings.response_format == "json_object":
            payload["response_format"] = {"type": "json_object"}
        elif self.settings.response_format == "json_schema":
            payload["response_format"] = {"type": "json_schema", "json_schema": {
                "name": "lin_m1_decision", "strict": True, "schema": Decision.model_json_schema()}}
        headers = {"Authorization": "Bearer " + self.settings.api_key.get_secret_value(), "Content-Type": "application/json"}
        async with self.client.stream("POST", self.settings.base_url + "/chat/completions", json=payload, headers=headers) as response:
            if response.status_code in (401, 403):
                raise ServiceError(503, "provider_auth_failed", "The provider rejected authentication or access; check local configuration.")
            if response.status_code == 429:
                raise ServiceError(503, "provider_rate_limited", "The provider reported a rate or quota limit. No automatic retry was made.")
            if response.status_code >= 400:
                raise ServiceError(502, "provider_http_error", "The provider rejected the completion request; check model and API compatibility.")
            if response.status_code != 200:
                raise ServiceError(502, "provider_protocol_error", "The provider returned an unexpected HTTP status.")
            chunks = []
            size = 0
            async for chunk in response.aiter_bytes():
                size += len(chunk)
                if size > 131072:
                    raise ServiceError(502, "provider_response_too_large", "The provider response exceeded the local limit.")
                chunks.append(chunk)
        try:
            body = strict_json_loads(b"".join(chunks).decode("utf-8"))
            message = body["choices"][0]["message"]
            if message.get("refusal"):
                raise ServiceError(502, "provider_refusal", "The provider declined this request.")
            raw = message["content"]
            if not isinstance(raw, str) or len(raw) > 16000:
                raise ValueError("Invalid response content")
            return raw
        except (ValueError, KeyError, IndexError, TypeError, AttributeError, RecursionError):
            raise ServiceError(502, "provider_protocol_error", "The provider did not return a supported chat completion envelope.") from None
