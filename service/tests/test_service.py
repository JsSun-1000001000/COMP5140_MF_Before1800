from __future__ import annotations

import asyncio
import copy
import json
from contextlib import asynccontextmanager
from pathlib import Path

import httpx
import pytest
from pydantic import ValidationError

from app.config import Settings
from app.main import create_app
from app.schemas import Decision, DecisionRequest, strict_json_loads
from app.tracing import TraceRecorder

SAMPLE = json.loads((Path(__file__).resolve().parents[1] / "examples" / "m1_request.json").read_text(encoding="utf-8"))
VALID = {"dialogue": "I'll check the camera.", "action": "inspect", "target_id": "camera_01",
         "concern_id": None, "concern_status": "keep", "basis_turn_id": None}


def request_data(**changes):
    result = copy.deepcopy(SAMPLE)
    result.update(changes)
    return result


def provider_response(value=VALID):
    content = value if isinstance(value, str) else json.dumps(value)
    return httpx.Response(200, json={"choices": [{"message": {"content": content}}]})


@asynccontextmanager
async def gateway(tmp_path, handler=None, **settings_overrides):
    settings = Settings(model="test-model-not-live", api_key="test-secret-not-live", log_dir=tmp_path / "logs", **settings_overrides)
    app = create_app(settings, provider_transport=httpx.MockTransport(handler or (lambda _: provider_response())))
    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            yield client, app


async def test_no_credentials_is_explicit_503(tmp_path):
    def must_not_call(_):
        pytest.fail("Unconfigured service must not contact a provider")
    app = create_app(Settings(log_dir=tmp_path), provider_transport=httpx.MockTransport(must_not_call))
    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            health = await client.get("/health")
            assert health.status_code == 503
            assert health.json() == {"status": "not_configured", "ready": False, "provider": "openai-compatible", "model": "",
                "api_key_configured": False, "missing_config": ["LLM_API_KEY", "LLM_MODEL"], "live_verified": False, "mock_mode": False}
            result = await client.post("/npc/decide", json=request_data())
            assert result.status_code == 503
            assert result.json()["error"]["code"] == "not_configured"


async def test_real_http_contract_with_explicit_fake_transport(tmp_path):
    seen = []
    def handler(request):
        seen.append(request)
        return provider_response()
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 200
        result = response.json()
        assert result["decision"] == VALID
        assert result["session_id"] == SAMPLE["session_id"]
        assert result["request_id"] == SAMPLE["request_id"]
        assert result["turn_id"] == SAMPLE["turn_id"]
        assert result["state_version"] == 0 and result["attempts"] == 1
        assert result["provider"] == "openai-compatible" and result["latency_ms"] >= 0
        assert set(result["decision"]) == set(VALID)
        assert seen[0].url == "https://api.openai.com/v1/chat/completions"
        assert seen[0].headers["authorization"] == "Bearer test-secret-not-live"
        sent = json.loads(seen[0].content)
        assert sent["response_format"] == {"type": "json_object"}
        assert sent["stream"] is False
        assert sent["max_tokens"] == 600
        assert "E1" in sent["messages"][0]["content"]
        assert json.loads(sent["messages"][1]["content"])["engine_snapshot"]["verified_evidence"] == []


@pytest.mark.parametrize("mode", ["off", "json_schema"])
async def test_provider_format_can_be_configured(tmp_path, mode):
    sent = []
    def handler(request):
        sent.append(json.loads(request.content))
        return provider_response()
    async with gateway(tmp_path, handler, response_format=mode, token_limit_parameter="max_completion_tokens") as (client, _):
        assert (await client.post("/npc/decide", json=request_data())).status_code == 200
    if mode == "off":
        assert "response_format" not in sent[0]
    else:
        schema = sent[0]["response_format"]["json_schema"]
        assert schema["strict"] is True
        assert schema["schema"]["additionalProperties"] is False
        assert set(schema["schema"]["required"]) == set(VALID)
    assert "max_completion_tokens" in sent[0] and "max_tokens" not in sent[0]


async def test_invalid_model_output_has_exactly_one_repair(tmp_path):
    sent = []
    def handler(request):
        sent.append(json.loads(request.content))
        return provider_response("not JSON" if len(sent) == 1 else VALID)
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 200
        assert response.json()["attempts"] == 2
    assert len(sent) == 2 and len(sent[1]["messages"]) == 4


@pytest.mark.parametrize("bad", [
    {**VALID, "action": "agree_escape"}, {**VALID, "concern_status": "resolved"},
    {**VALID, "concern_id": "C1"}, {**VALID, "basis_turn_id": "turn-1"},
    {**VALID, "target_id": "unknown"}, {**VALID, "action": "follow", "target_id": "camera_01"},
    {**VALID, "invented_field": True}, {k: v for k, v in VALID.items() if k != "basis_turn_id"},
    {**VALID, "dialogue": " "}, "```json\n{}\n```", '{"dialogue":"x","dialogue":"y"}',
])
async def test_invalid_contract_never_executes(tmp_path, bad):
    calls = []
    def handler(_):
        calls.append(1)
        return provider_response(bad)
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 502
        assert response.json()["error"]["code"] == "invalid_model_output"
        assert response.json()["error"]["attempts"] == 2
    assert len(calls) == 2


@pytest.mark.parametrize("modifier", ["allowlist", "unknown_target", "proactive_follow"])
async def test_context_gates_are_enforced(tmp_path, modifier):
    body = request_data()
    decision = copy.deepcopy(VALID)
    if modifier == "allowlist":
        body["context"]["allowed_actions"] = ["reply"]
    elif modifier == "unknown_target":
        body["context"]["known_targets"] = ["player"]
    else:
        body.update(trigger="proactive", player_input="")
        decision.update(action="follow", target_id="player")
    async with gateway(tmp_path, lambda _: provider_response(decision)) as (client, _):
        assert (await client.post("/npc/decide", json=body)).status_code == 502


@pytest.mark.parametrize("status,code", [(401, "provider_auth_failed"), (403, "provider_auth_failed"),
    (429, "provider_rate_limited"), (500, "provider_http_error"), (302, "provider_protocol_error")])
async def test_provider_http_failures_are_not_retried_or_leaked(tmp_path, status, code):
    calls = []
    def handler(_):
        calls.append(1)
        return httpx.Response(status, text="dangerous raw upstream error test-secret-not-live")
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code in (502, 503)
        assert response.json()["error"]["code"] == code
        assert "dangerous" not in response.text
        assert response.json()["error"]["attempts"] == 1
    assert len(calls) == 1


async def test_deadline_stops_slow_provider(tmp_path):
    async def handler(_):
        await asyncio.sleep(0.5)
        return provider_response()
    async with gateway(tmp_path, handler, timeout_seconds=0.1) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 504
        assert response.json()["error"]["code"] == "provider_timeout"


async def test_deadline_stops_the_repair_attempt(tmp_path):
    calls = []
    async def handler(_):
        calls.append(1)
        if len(calls) == 1:
            return provider_response("broken")
        await asyncio.sleep(0.5)
        return provider_response()
    async with gateway(tmp_path, handler, timeout_seconds=0.1) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 504
        assert response.json()["error"]["attempts"] == 2
    assert len(calls) == 2


@pytest.mark.parametrize("body,code", [
    ({"choices": []}, "provider_protocol_error"),
    ({"choices": [{"message": {"content": None}}]}, "provider_protocol_error"),
    ({"choices": [{"message": {"refusal": "test refusal", "content": None}}]}, "provider_refusal"),
])
async def test_invalid_envelope_or_refusal_is_not_repaired(tmp_path, body, code):
    calls = []
    def handler(_):
        calls.append(1)
        return httpx.Response(200, json=body)
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 502
        assert response.json()["error"]["code"] == code
    assert len(calls) == 1


async def test_oversized_provider_envelope_rejected(tmp_path):
    async with gateway(tmp_path, lambda _: httpx.Response(200, content=b"x" * 131073)) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 502
        assert response.json()["error"]["code"] == "provider_response_too_large"


async def test_network_failure_is_sanitized(tmp_path):
    def handler(_):
        raise httpx.ConnectError("test-secret-not-live")
    async with gateway(tmp_path, handler) as (client, _):
        response = await client.post("/npc/decide", json=request_data())
        assert response.status_code == 502
        assert "test-secret-not-live" not in response.text


async def test_cache_concurrent_duplicates_and_payload_conflict(tmp_path):
    calls = []
    async def handler(_):
        calls.append(1)
        await asyncio.sleep(0.03)
        return provider_response()
    async with gateway(tmp_path, handler) as (client, _):
        first, second = await asyncio.gather(*(client.post("/npc/decide", json=request_data()) for _ in range(2)))
        assert first.status_code == second.status_code == 200
        assert first.json() == second.json()
        assert (await client.post("/npc/decide", json=request_data())).json() == first.json()
        conflict = await client.post("/npc/decide", json=request_data(player_input="A different turn"))
        assert conflict.status_code == 409
        assert conflict.json()["error"]["code"] == "request_id_conflict"
        assert (await client.post("/npc/decide", json=request_data(session_id="different-session"))).status_code == 200
    assert len(calls) == 2


async def test_cached_error_does_not_trigger_charge_retry(tmp_path):
    calls = []
    def handler(_):
        calls.append(1)
        return httpx.Response(429)
    async with gateway(tmp_path, handler) as (client, _):
        first = await client.post("/npc/decide", json=request_data())
        second = await client.post("/npc/decide", json=request_data())
        assert first.json() == second.json()
    assert len(calls) == 1


async def test_cancel_inflight_is_terminal_and_capacity_is_bounded(tmp_path):
    started = asyncio.Event()
    stopped = asyncio.Event()
    async def handler(_):
        started.set()
        try:
            await asyncio.sleep(5)
        finally:
            stopped.set()
        return provider_response()
    async with gateway(tmp_path, handler, max_inflight=1) as (client, _):
        task = asyncio.create_task(client.post("/npc/decide", json=request_data()))
        await asyncio.wait_for(started.wait(), 1)
        busy = await client.post("/npc/decide", json=request_data(request_id="request-2"))
        assert busy.status_code == 503 and busy.json()["error"]["code"] == "service_busy"
        cancelled = await client.post("/npc/cancel", json={"session_id": SAMPLE["session_id"], "request_id": SAMPLE["request_id"]})
        assert cancelled.json()["status"] == "cancelled"
        response = await task
        assert response.status_code == 409 and response.json()["error"]["code"] == "request_cancelled"
        await asyncio.wait_for(stopped.wait(), 1)
        assert (await client.post("/npc/decide", json=request_data())).status_code == 409
        assert (await client.post("/npc/cancel", json={"session_id": "absent", "request_id": "absent"})).json()["status"] == "not_found"


async def test_cancelling_one_waiter_keeps_shared_work(tmp_path):
    started, release = asyncio.Event(), asyncio.Event()
    calls = []
    async def handler(_):
        calls.append(1)
        started.set()
        await release.wait()
        return provider_response()
    async with gateway(tmp_path, handler) as (_, app):
        request = DecisionRequest.model_validate(request_data())
        task1 = asyncio.create_task(app.state.coordinator.decide(request))
        await started.wait()
        task2 = asyncio.create_task(app.state.coordinator.decide(request))
        await asyncio.sleep(0)
        task1.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task1
        release.set()
        assert (await task2).decision.action == "inspect"
    assert len(calls) == 1


async def test_cache_ttl_and_size_evict_only_completed(tmp_path):
    calls = []
    def handler(_):
        calls.append(1)
        return provider_response()
    async with gateway(tmp_path, handler, max_cache_entries=1, cache_ttl_seconds=1) as (client, app):
        await client.post("/npc/decide", json=request_data())
        await client.post("/npc/decide", json=request_data(request_id="request-2"))
        assert len(app.state.coordinator.entries) == 1
        entry = next(iter(app.state.coordinator.entries.values()))
        entry.completed_at -= 2
        await client.post("/npc/decide", json=request_data(request_id="request-2"))
    assert len(calls) == 3


@pytest.mark.parametrize("change", ["extra", "string_version", "context_extra", "long_input", "bad_trigger", "duplicate_targets"])
async def test_strict_request_validation(tmp_path, change):
    body = request_data()
    if change == "extra": body["unexpected"] = True
    if change == "string_version": body["state_version"] = "0"
    if change == "context_extra": body["context"]["unexpected"] = True
    if change == "long_input": body["player_input"] = "x" * 2001
    if change == "bad_trigger": body["trigger"] = "proactive"
    if change == "duplicate_targets": body["context"]["known_targets"] = ["player", "player"]
    def must_not_call(_): pytest.fail("Invalid request reached provider")
    async with gateway(tmp_path, must_not_call) as (client, _):
        response = await client.post("/npc/decide", json=body)
        assert response.status_code == 422


async def test_body_media_type_and_strict_json_limits(tmp_path):
    async with gateway(tmp_path) as (client, _):
        assert (await client.post("/npc/decide", content="{}", headers={"content-type": "text/plain"})).status_code == 415
        for text in ('{"x":1,"x":2}', '{"x":NaN}', '[]', '\ufeff{}'):
            assert (await client.post("/npc/decide", content=text.encode("utf-8"), headers={"content-type": "application/json"})).status_code == 422
        assert (await client.post("/npc/decide", content=b"x" * 65537)).status_code == 413


async def test_log_has_context_output_and_redacts_credentials(tmp_path):
    value = {**VALID, "dialogue": "test-secret-not-live Bearer abcdef sk-abcdefghijkl contact test@example.com"}
    body = request_data(player_input="test-secret-not-live")
    body["context"]["last_action_result"] = {"api_key": "some-other-secret"}
    async with gateway(tmp_path, lambda _: provider_response(value)) as (client, _):
        assert (await client.post("/npc/decide", json=body)).status_code == 200
    log = (tmp_path / "logs" / "requests.jsonl").read_text(encoding="utf-8")
    for secret in ("test-secret-not-live", "abcdef", "sk-abcdefghijkl", "test@example.com", "some-other-secret"):
        assert secret not in log
    records = [json.loads(line) for line in log.splitlines()]
    assert any("context" in record for record in records)
    assert any("raw_output" in record for record in records)
    assert any(record["event"] == "accepted" for record in records)


def test_trace_redacts_inline_key_assignments(tmp_path):
    recorder = TraceRecorder(tmp_path)
    try:
        output = recorder.redact('api_key=other-value password: hunter2 {"access_token":"a-token-value"}')
        assert "other-value" not in output
        assert "hunter2" not in output
        assert "a-token-value" not in output
    finally:
        recorder.close()


def test_last_action_result_depth_size_and_nonfinite_limits():
    deep = {"nested": {"nested": {"nested": {"nested": {"nested": {"nested": {"nested": 1}}}}}}}
    for value in (deep, {"large": "x" * 8193}, {"value": float("inf")}):
        body = request_data()
        body["context"]["last_action_result"] = value
        with pytest.raises(ValidationError):
            DecisionRequest.model_validate(body)


def test_env_reads_only_explicit_file_and_process_override(tmp_path, monkeypatch):
    env = tmp_path / "test.env"
    env.write_text("LLM_API_KEY=local-placeholder\nLLM_MODEL=local-model\n", encoding="utf-8")
    monkeypatch.setenv("LLM_MODEL", "process-model")
    settings = Settings.from_environment(env)
    assert settings.model == "process-model"
    assert settings.api_key.get_secret_value() == "local-placeholder"
    assert "local-placeholder" not in repr(settings)


@pytest.mark.parametrize("url", ["http://example.com/v1", "https://user:pass@example.com/v1", "https://example.com/v1?key=x", "https://example.com/v1/chat/completions"])
def test_unsafe_or_misconfigured_base_url_rejected(url):
    with pytest.raises(ValidationError):
        Settings(base_url=url)


def test_startup_configuration_error_omits_secret_values(monkeypatch):
    monkeypatch.setenv("LLM_BASE_URL", "https://username:private-pasted-password@example.com/v1")
    with pytest.raises(RuntimeError) as error:
        create_app()
    assert "private-pasted-password" not in str(error.value)
    assert error.value.__suppress_context__ is True


async def test_openapi_contains_valid_inline_request_schema(tmp_path):
    async with gateway(tmp_path) as (client, _):
        schema = (await client.get("/openapi.json")).json()
        request_schema = schema["paths"]["/npc/decide"]["post"]["requestBody"]["content"]["application/json"]["schema"]
        assert "$ref" not in json.dumps(request_schema)
        assert request_schema["properties"]["context"]["additionalProperties"] is False


async def test_explicit_fixture_is_marked_and_completes_two_http_turns(tmp_path):
    from tests.fixture_provider import app as fixture_app
    configuration = Settings(model="deterministic-test-not-llm", api_key="test-fixture-not-a-secret",
        provider="TEST_FIXTURE", base_url="http://127.0.0.1:8011/v1", log_dir=tmp_path)
    app = create_app(configuration, provider_transport=httpx.ASGITransport(app=fixture_app))
    async with app.router.lifespan_context(app):
        async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
            assert (await client.get("/health")).json()["mock_mode"] is True
            first = await client.post("/npc/decide", json=request_data())
            assert first.json()["decision"]["action"] == "inspect"
            second_body = json.loads((Path(__file__).resolve().parents[1] / "examples" / "m1_feedback_request.json").read_text(encoding="utf-8"))
            second = await client.post("/npc/decide", json=second_body)
            assert second.json()["provider"] == "TEST_FIXTURE"
            assert second.json()["decision"]["action"] == "reply"
            assert second.json()["decision"]["concern_status"] == "keep"
            assert "does not prove" in second.json()["decision"]["dialogue"]
            assert "track me" in second.json()["decision"]["dialogue"]
