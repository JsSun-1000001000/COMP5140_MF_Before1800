# Service verification — 2026-11-17

Status: implemented and verified with explicit simulated transports and a local HTTP fixture. **No live external LLM call was made.** The normal `.env` still has an empty key and model, per the user's instruction.

## Automated tests

Command: `.venv/Scripts/python.exe -m pytest -q`

Result: **54 passed** on Python 3.12.14. The test suite uses explicit `httpx.MockTransport` or fixture ASGI transport. It does not use the real network provider. `pip check` also reported no broken requirements.

Revalidated after replacing the retired application-bundled interpreter with the fixed local `C:\Python314\python.exe` (Python 3.14.3): **54 passed**, `pip check` clean. Machine-readable results: `../artifacts/service-final.xml`. A separate broken-venv fixture confirmed that setup archives the unavailable environment, recreates it successfully, and preserves the local `.env`.

Covered behavior:

- No credentials → health/decision 503; provider is not called.
- Chat Completions endpoint, bearer header, messages, configurable JSON response format and output token parameter.
- Required six-key decision, explicit nulls, strict extra-field rejection, action/target and proactive constraints.
- Response echoes session/request/turn/version; context distinguishes verified evidence from player claims.
- Exactly one format/contract repair; no HTTP/network/quota retry; total deadline also bounds the repair.
- Malformed provider envelope, refusal, oversized upstream output, timeout and connectivity errors.
- Concurrent identical requests share a call; same request ID with different content returns 409.
- Success/error cache, capacity/expiry, concurrency limit, explicit cancellation and shared-waiter cancellation behavior.
- Incoming media type/body/length/nesting/duplicate-key limits.
- Sanitized logs retain context and raw decision output while redacting known secrets, bearer strings, common key assignments and email addresses.
- The explicit fixture is labeled TEST_FIXTURE and completes inspect → engine E1 feedback → reply without resolving C1.

## Actual local HTTP smoke check

The following used real localhost HTTP sockets through Uvicorn, not an in-process ASGI shortcut:

1. `http://127.0.0.1:8000/health` → 503, `mock_mode:false`, missing `LLM_API_KEY` and `LLM_MODEL`.
2. Explicit test gateway `http://127.0.0.1:8001/health` → 200, `provider:TEST_FIXTURE`, `model:deterministic-test-not-llm`, `mock_mode:true`.
3. First sample request via gateway to fixture at port 8011 → `inspect`, `camera_01`.
4. Second sample request containing an engine-reported E1 observation → `reply`, `concern_status:keep`, null concern/basis IDs. Dialogue correctly refers to the camera tracking Lin himself as he moves between the two positions.

The sanitized machine-readable smoke result is at `logs/http_fixture_smoke.json` (local ignored runtime artifact). The sample second request is a contract test, not evidence that Unity executed the experiment. Unity scene/action execution must be verified separately.

## Remaining live acceptance

After the user selects a provider/model and configures their own key locally: restart the normal service, run the Unity camera sequence with a fresh session, confirm provider/model identify the selected real service, and record the real response plus executed movement and subsequent E1 feedback. Evaluate Lin's factual consistency and action suitability across several phrasings. These real-model checks remain pending and are not replaced by the deterministic fixture.
