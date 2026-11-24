import pytest
from pydantic import ValidationError
from app.schemas import DecisionRequest, validate_context_action
from app.speech import SpeechRequest
from tests.test_last_visit import turn, proposal

@pytest.mark.parametrize('language',['zh','en'])
def test_language_survives_context_without_changing_evidence(language):
    req=turn();data=req.model_dump();data['context']['output_language']=language
    parsed=DecisionRequest.model_validate(data)
    assert parsed.context.output_language==language
    assert parsed.context.verified_evidence==req.context.verified_evidence

@pytest.mark.parametrize('target',['memory_sofa','memory_books','memory_plant','memory_table','memory_shoes','memory_lamp','memory_mirror','memory_fridge','memory_chair','memory_window','memory_bedside','memory_wardrobe','memory_sink'])
def test_everyday_inspection_needs_known_reachable_target(target):
    req=turn();decision=proposal(req,'inspect',target)
    with pytest.raises(ValueError):validate_context_action(decision,req)
    req.context.known_targets.append(target);validate_context_action(decision,req)
    assert not req.context.verified_evidence

@pytest.mark.parametrize('payload',[{'text':''},{'text':'a'*1201},{'text':'hello','language':'xx'},{'text':'hello','path':'/tmp/test'}])
def test_speech_payload_is_bounded_and_cannot_choose_files(payload):
    with pytest.raises(ValidationError):SpeechRequest.model_validate(payload)


def test_witnessed_facts_cannot_be_inferred_from_player_discovery():
    from app.provider import witnessed_facts
    req=turn();req.context.verified_evidence=[];req.context.player_discovered=['camera_test','living_tv']
    assert '没有这些实验的亲见记录' in witnessed_facts(req)
    req.context.verified_evidence=['E1','E4']
    facts=witnessed_facts(req)
    assert '已亲眼看见镜头' in facts and '已亲眼看见电视' in facts
    assert '已亲眼看清布景' not in facts
