import pytest

from app.schemas import Decision, DecisionRequest, validate_context_action
from tests.test_service import request_data


def trust_turn():
    data = request_data()
    data["context"]["allow_trust_resolution"] = True
    req = DecisionRequest.model_validate(data)
    decision = Decision(dialogue="我明白你为什么现在告诉我了。", action="reply", target_id=None,
                        concern_id="C2", concern_status="resolved", basis_turn_id=req.turn_id)
    return req, decision


def test_trust_is_only_accepted_for_the_open_concern_and_current_player_turn():
    req, decision = trust_turn()
    validate_context_action(decision, req)
    with pytest.raises(ValueError):
        validate_context_action(decision.model_copy(update={"basis_turn_id": "old-turn"}), req)
    req.context.concerns.C2 = "resolved"
    with pytest.raises(ValueError):
        validate_context_action(decision, req)
    req.context.concerns.C2 = "open"
    req.context.allow_trust_resolution = False
    with pytest.raises(ValueError):
        validate_context_action(decision, req)


@pytest.mark.parametrize("target", ["camera_test", "set_back"])
def test_new_inspection_targets_still_must_exist_in_the_scene(target):
    req, _ = trust_turn()
    decision = Decision(dialogue="我去亲眼看看。", action="inspect", target_id=target,
                        concern_id=None, concern_status="keep", basis_turn_id=None)
    with pytest.raises(ValueError):
        validate_context_action(decision, req)
    req.context.known_targets.append(target)
    validate_context_action(decision, req)
