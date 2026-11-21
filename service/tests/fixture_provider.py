"""Deterministic HTTP fixture, NOT an LLM. Start only through the explicit test script.

This exercises the real gateway HTTP/parser path and Unity action plumbing.
It cannot validate LLM reasoning, prompt quality, or course live-LLM acceptance.
"""
from __future__ import annotations

import json

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

app = FastAPI(title="TEST FIXTURE — NOT AN LLM", version="0.1.0")


@app.get("/health")
async def health():
    return {"provider": "TEST_FIXTURE", "model": "deterministic-test-not-llm", "mock_mode": True}


@app.post("/v1/chat/completions")
async def completion(request: Request):
    if request.headers.get("authorization") != "Bearer test-fixture-not-a-secret":
        return JSONResponse({"error": "Fixture requires the explicit dummy test key."}, status_code=401)
    payload = await request.json()
    if payload.get("model") != "deterministic-test-not-llm":
        return JSONResponse({"error": "Fixture model must be deterministic-test-not-llm."}, status_code=400)
    try:
        user = next(json.loads(message["content"]) for message in payload["messages"]
                    if message["role"] == "user" and "engine_snapshot" in message["content"])
        snapshot = user["engine_snapshot"]
        words = user.get("player_input", "").lower()
        actions = snapshot["allowed_actions"]
        targets = snapshot["known_targets"]
    except (KeyError, ValueError, StopIteration, TypeError):
        return JSONResponse({"error": "Malformed fixture request."}, status_code=400)
    decision = {"dialogue": "[TEST FIXTURE] I need to check the room before drawing a conclusion.",
                "action": "reply", "target_id": None, "concern_id": None, "concern_status": "keep", "basis_turn_id": None}
    if "E1" in snapshot.get("verified_evidence", []):
        decision["dialogue"] = "[TEST FIXTURE] I saw the camera track me as I moved between the two spots. That suggests monitoring, but does not prove the whole world is fake."
    elif any(word in words for word in ("camera", "inspect", "摄像", "相机")) and "inspect" in actions and "camera_01" in targets:
        decision.update(action="inspect", target_id="camera_01", dialogue="[TEST FIXTURE] I'll move between the two spots and check whether the camera tracks me.")
    elif user["trigger"] == "user" and any(word in words for word in ("follow", "come with", "跟", "同行")) and "follow" in actions and "player" in targets:
        decision.update(action="follow", target_id="player", dialogue="[TEST FIXTURE] I'll follow you.")
    return {"id": "test-fixture-completion", "object": "chat.completion", "model": "deterministic-test-not-llm",
            "choices": [{"index": 0, "finish_reason": "stop", "message": {"role": "assistant", "content": json.dumps(decision)}}]}
