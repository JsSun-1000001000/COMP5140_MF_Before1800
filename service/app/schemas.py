from __future__ import annotations

import json
from typing import Annotated, Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

Identifier = Annotated[str, Field(min_length=1, max_length=96, pattern=r"^[A-Za-z0-9_.:-]+$")]
Action = Literal["reply", "follow", "inspect"]


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


class Concerns(StrictModel):
    C1: Literal["open", "resolved"]
    C2: Literal["open", "resolved"]
    C3: Literal["open", "resolved"]


class DialogueEntry(StrictModel):
    role: Literal["user", "assistant"]
    content: Annotated[str, Field(max_length=2000)]


class SceneContext(StrictModel):
    verified_evidence: Annotated[list[Identifier], Field(max_length=16)]
    concerns: Concerns
    cooperation_mode: Literal["talking", "withdrawn", "following"]
    escape_agreed: bool
    route_confirmed: bool
    known_targets: Annotated[list[Identifier], Field(max_length=32)]
    allowed_actions: Annotated[list[Action], Field(min_length=1, max_length=3)]
    recent_dialogue: Annotated[list[DialogueEntry], Field(max_length=12)]
    last_action_result: dict[str, Any] | None
    game_time: Annotated[str, Field(max_length=32)] | None = None
    observations: Annotated[list[Annotated[str, Field(max_length=1000)]], Field(max_length=24)] = Field(default_factory=list)
    player_discovered: Annotated[list[Identifier], Field(max_length=16)] = Field(default_factory=list)

    @field_validator("verified_evidence", "known_targets", "allowed_actions", "player_discovered")
    @classmethod
    def no_duplicates(cls, value: list[str]) -> list[str]:
        if len(value) != len(set(value)):
            raise ValueError("Duplicate entries are not allowed")
        return value

    @field_validator("last_action_result")
    @classmethod
    def bounded_result(cls, value: dict[str, Any] | None) -> dict[str, Any] | None:
        if value is not None:
            encoded = json.dumps(value, ensure_ascii=False, allow_nan=False)
            if len(encoded.encode("utf-8")) > 8192:
                raise ValueError("last_action_result exceeds 8192 UTF-8 bytes")
            def depth(obj: Any, level: int = 0) -> None:
                if level > 6:
                    raise ValueError("last_action_result nesting exceeds 6 levels")
                if isinstance(obj, dict):
                    for item in obj.values():
                        depth(item, level + 1)
                elif isinstance(obj, list):
                    for item in obj:
                        depth(item, level + 1)
            depth(value)
        return value


class DecisionRequest(StrictModel):
    session_id: Identifier
    request_id: Identifier
    turn_id: Identifier
    state_version: Annotated[int, Field(ge=0, le=2147483647)]
    trigger: Literal["user", "proactive"]
    player_input: Annotated[str, Field(max_length=2000)]
    context: SceneContext

    @model_validator(mode="after")
    def valid_trigger(self) -> DecisionRequest:
        if self.trigger == "user" and not self.player_input.strip():
            raise ValueError("User requests need non-empty player_input")
        if self.trigger == "proactive" and self.player_input.strip():
            raise ValueError("Proactive requests must not invent player input")
        return self


class Decision(StrictModel):
    # Every key is required, including nullable keys. No defaults here.
    dialogue: Annotated[str, Field(min_length=1, max_length=1200)]
    action: Action
    target_id: Identifier | None
    concern_id: None
    concern_status: Literal["keep"]
    basis_turn_id: None

    @model_validator(mode="after")
    def target_matches_action(self) -> Decision:
        required_target = {"reply": None, "follow": "player", "inspect": "camera_01"}[self.action]
        if self.target_id != required_target:
            raise ValueError("Target does not match the M1 action contract")
        if not self.dialogue.strip():
            raise ValueError("Dialogue must not be blank")
        return self


class DecisionResponse(StrictModel):
    session_id: Identifier
    request_id: Identifier
    turn_id: Identifier
    state_version: int
    decision: Decision
    provider: str
    model: str
    latency_ms: int
    attempts: int


class CancelRequest(StrictModel):
    session_id: Identifier
    request_id: Identifier


def validate_context_action(decision: Decision, request: DecisionRequest) -> None:
    if decision.action not in request.context.allowed_actions:
        raise ValueError("Action is not allowed by this snapshot")
    if decision.target_id is not None and decision.target_id not in request.context.known_targets:
        raise ValueError("Target is not known in this snapshot")
    if request.trigger == "proactive" and decision.action not in ("reply", "inspect"):
        raise ValueError("A proactive request may only reply or inspect")


def strict_json_loads(text: str) -> Any:
    def pairs(items: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON key")
            result[key] = value
        return result
    def invalid_constant(_: str) -> None:
        raise ValueError("Non-finite JSON number")
    return json.loads(text, object_pairs_hook=pairs, parse_constant=invalid_constant)
