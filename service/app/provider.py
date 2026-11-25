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


def repair_contract(request: DecisionRequest) -> str:
    """Use trusted schema/snapshot facts, never reflect untrusted exception messages."""
    context = request.context
    resolution_actions = ("reply or end_conversation"
                          if context.allow_resolved_closing and "end_conversation" in context.allowed_actions
                          else "reply")
    movement_rule = (
        "This client closes the conversation after inspect, follow, withdraw or agree_escape. "
        "When acting now, end the line with a statement about Lin's next step. If she actually "
        "needs an answer first, use reply when permitted and ask her question. "
        if context.world_actions_close_dialogue else ""
    )
    break_rule = ""
    if context.conversation_needs_break:
        break_rule = (
            "conversation_needs_break=true. First check whether this turn earned a concern resolution. "
            "Record it with an eligible action if so. Otherwise choose a justified action or answer "
            "briefly and close. "
        )
        if context.allow_break_followup:
            break_rule += (
                "One reply/keep is permitted only for genuinely new, incomplete information. Ask the "
                "one concrete question Lin still wants answered. Choose the most important missing "
                "detail and drop any other question. End with ? or ？ and ask no earlier question. "
            )
        else:
            break_rule += "A plain reply/keep is invalid this turn. "
    return (
        "Repair the previous response as ONE JSON object with exactly six required keys. "
        "EXACT TARGET RULES: action=reply => target_id=null ALWAYS, even if the dialogue mentions an object. "
        "action=follow => target_id=player; inspect => one listed target; "
        "withdraw/agree_escape/end_conversation => target_id=null. "
        "EXACT CONCERN RULES: keep => concern_id=null AND basis_turn_id=null ALWAYS. "
        "resolved => action=" + resolution_actions + " AND target_id=null AND concern_id=one currently open eligible C1/C2/C3 "
        "AND basis_turn_id=" + request.turn_id + ". "
        "NEVER combine resolved with agree_escape. If this turn resolves C3, record C3 now; "
        "consent can occur on a later turn when the snapshot has all concerns resolved. "
        "Only select actions from " + json.dumps(context.allowed_actions) + ". "
        "When end_conversation is available: first record an eligible concern genuinely resolved by "
        "this turn, including new concrete accountability for C2. If Lin already asked for space "
        "and the player only repeats the same demand for forgiveness or a decision, honor her boundary "
        "with end_conversation now. Its last sentence must be a statement, even in English; "
        "rewrite a final question (including one ending in ? or ？ inside quotes) as Lin's decision. "
        "Only select targets from " + json.dumps(context.known_targets) + ". "
        "Current concerns: " + json.dumps(context.concerns.model_dump()) + ". "
        "Current witnessed facts: " + json.dumps(context.verified_evidence) + "; route_confirmed="
        + str(context.route_confirmed) + ". C1 needs 3 physical facts including E5; C3 needs the route. "
        "The original player's words and original turn remain the only basis for resolution. "
        "This is a repair of that response, not a new player turn. " + movement_rule + break_rule
    )


def witnessed_facts(request: DecisionRequest) -> str:
    facts={
        "E1":"林已亲眼看见镜头随她换位而转动。",
        "E4":"林已亲眼看见电视响应她点播及改口后的节目。",
        "E5":"林已亲眼看清布景背面的支架和制作标记。",
    }
    present=[facts[k] for k in request.context.verified_evidence if k in facts]
    if not present:return "本轮没有这些实验的亲见记录。不可把玩家的描述当成完成实验。"
    return "以下是引擎确认已完成的亲见事实，不是玩家自称，不能否认或要求重做："+"".join(present)+"其他尚未完成的前提仍必须遵守。"


class CompletionProvider:
    def __init__(self, settings: Settings, client: httpx.AsyncClient, trace: TraceRecorder):
        self.settings = settings
        self.client = client
        self.trace = trace
        self.live_verified = False
        self.enable_live_verification = False
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
            {"role": "system", "content": self.system_prompt + "\n" + witnessed_facts(request)},
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
                            {"role": "user", "content": repair_contract(request) + " Validation issues: " + json.dumps(reasons)},
                        ])
                        continue
                    response = DecisionResponse(**identifiers, decision=decision, provider=self.settings.provider,
                        model=self.settings.model, latency_ms=round((time.monotonic() - started) * 1000), attempts=attempt)
                    self.trace.write("accepted", **identifiers, decision=decision.model_dump(mode="json"),
                                     attempts=attempt, latency_ms=response.latency_ms, provider=self.settings.provider, model=self.settings.model)
                    if self.enable_live_verification and self.settings.provider != "TEST_FIXTURE":
                        self.live_verified = True
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
        if self.settings.provider == "deepseek":
            payload["thinking"] = {"type": "disabled"}
        if self.settings.token_limit_parameter != "off":
            payload[self.settings.token_limit_parameter] = self.settings.max_output_tokens
        if self.settings.response_format == "json_object":
            payload["response_format"] = {"type": "json_object"}
        elif self.settings.response_format == "json_schema":
            payload["response_format"] = {"type": "json_schema", "json_schema": {
                "name": "lin_last_visit_decision", "strict": True, "schema": Decision.model_json_schema()}}
        headers = {"Authorization": "Bearer " + self.settings.api_key.get_secret_value(), "Content-Type": "application/json"}
        async with self.client.stream("POST", self.settings.base_url + "/chat/completions", json=payload, headers=headers) as response:
            if response.status_code in (401, 403):
                raise ServiceError(503, "provider_auth_failed", "The provider rejected authentication or access; check local configuration.")
            if response.status_code == 402:
                raise ServiceError(503, "provider_balance", "DeepSeek reports insufficient account balance.")
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
