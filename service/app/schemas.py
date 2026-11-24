from __future__ import annotations

import json
from typing import Annotated, Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

Identifier = Annotated[str, Field(min_length=1, max_length=96, pattern=r"^[A-Za-z0-9_.:-]+$")]
Action = Literal["reply", "follow", "inspect", "withdraw", "agree_escape"]
Concern = Literal["C1", "C2", "C3"]
Observation = Annotated[str, Field(max_length=1000)]
STORY_TARGETS = ("sealed_letter", "parent_reply", "parents_diary", "friend_postcard", "exit_door", "memory_sofa", "memory_books", "memory_plant", "memory_table", "memory_shoes", "memory_lamp", "memory_mirror", "memory_fridge", "memory_chair", "memory_window")


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
    output_language: Literal["zh", "en"] = "zh"
    allow_trust_resolution: bool = False
    withdraw_used: bool = False
    verified_evidence: Annotated[list[Identifier], Field(max_length=16)]
    concerns: Concerns
    cooperation_mode: Literal["talking", "withdrawn", "following"]
    escape_agreed: bool
    route_confirmed: bool
    known_targets: Annotated[list[Identifier], Field(max_length=32)]
    allowed_actions: Annotated[list[Action], Field(min_length=1, max_length=5)]
    recent_dialogue: Annotated[list[DialogueEntry], Field(max_length=40)]
    last_action_result: dict[str, Any] | None
    game_time: Annotated[str, Field(max_length=32)] | None = None
    observations: Annotated[list[Observation], Field(max_length=48)] = Field(default_factory=list)
    # Supplied by Unity after a physical event or an accepted conversation, never by
    # a model-generated omniscient summary. Player discoveries are not Lin's knowledge.
    narrative_observations: Annotated[list[Observation], Field(max_length=32)] = Field(default_factory=list)
    relationship_memory: Annotated[list[Observation], Field(max_length=24)] = Field(default_factory=list)
    lin_private_knowledge: Annotated[list[Observation], Field(max_length=24)] = Field(default_factory=list)
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
    concern_id: Concern | None
    concern_status: Literal["keep", "resolved"]
    basis_turn_id: Identifier | None

    @model_validator(mode="after")
    def target_matches_action(self) -> Decision:
        targets = {"reply": (None,), "follow": ("player",),
                   "inspect": ("camera_test", "living_tv", "set_back", "camera_01") + STORY_TARGETS,
                   "withdraw": (None,), "agree_escape": (None,)}
        if self.target_id not in targets[self.action]:
            raise ValueError("Target does not match the action contract")
        if self.concern_status == "keep" and (self.concern_id is not None or self.basis_turn_id is not None):
            raise ValueError("No concern transition on keep")
        if self.concern_status == "resolved" and (self.concern_id is None or not self.basis_turn_id or self.action != "reply"):
            raise ValueError("Concern resolution requires a reply tied to the current turn")
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
    context = request.context
    facts = set(context.verified_evidence) & {"E1", "E2", "E4", "E5"}
    sufficient_facts = "E5" in facts and len(facts) >= 3
    if decision.concern_status == "resolved":
        if (request.trigger != "user" or not request.player_input.strip()
                or decision.basis_turn_id != request.turn_id or decision.concern_id is None
                or getattr(context.concerns, decision.concern_id) != "open"):
            raise ValueError("Concern proposal is not valid for this turn")
        if decision.concern_id == "C1" and not sufficient_facts:
            raise ValueError("C1 requires three witnessed facts including E5")
        if decision.concern_id == "C2" and not context.allow_trust_resolution:
            raise ValueError("Trust proposal is not allowed for this turn")
        if decision.concern_id == "C3" and not context.route_confirmed:
            raise ValueError("C3 requires a physically confirmed route")
    if decision.action not in request.context.allowed_actions:
        raise ValueError("Action is not allowed by this snapshot")
    if decision.target_id is not None and decision.target_id not in request.context.known_targets:
        raise ValueError("Target is not known in this snapshot")
    if request.trigger == "proactive" and decision.action not in ("reply", "inspect"):
        raise ValueError("A proactive request may only reply or inspect")
    if decision.action == "withdraw" and (context.withdraw_used or context.escape_agreed
            or context.cooperation_mode == "withdrawn"):
        raise ValueError("Withdrawal is available only once before consent")
    if context.cooperation_mode == "withdrawn" and decision.action not in ("reply",):
        raise ValueError("Restore conversation before starting another action")
    if context.escape_agreed and decision.action == "inspect":
        raise ValueError("Do not introduce another investigation after consent")
    if decision.action == "agree_escape" and not (sufficient_facts and context.route_confirmed
            and all(value == "resolved" for value in context.concerns.model_dump().values())):
        raise ValueError("Escape consent requires facts, route and all three concerns")


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
