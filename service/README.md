# Before 18:00 — local NPC service

For the current Unity 2021.3 / DeepSeek demo, follow [Part C 本地演示](../docs/PartC_本地演示.md). F2 enables the gateway on port 8180. The adapter sends reachable inspection targets (`camera_test`, `living_tv`, or `set_back`) and can request a player-turn-based C2 transition.

For a built residence player, run `service\Play-With-Lin.cmd "path\to\Before1800.exe"` from the repository root. The launcher uses a private input prompt if the environment key is missing or rejected, locates an available DeepSeek chat model and starts a loopback gateway for this play session. It closes the gateway when the game exits. To distribute the launcher with a build, place `Play-With-Lin.cmd` beside `Before1800.exe`. Copy `play_with_lin.py`, `requirements.txt`, `app/*.py` and `prompts/lin_system.txt` to their corresponding paths inside a sibling `service` folder. Opening the executable directly starts with local topics. [住宅试玩说明](../docs/PartB_住宅试玩.md)

In the residence, `end_conversation` displays Lin's last line and returns control to the player. A client that sends `allow_resolved_closing: true` can record an eligible concern in that same turn. During a conversation break, `allow_break_followup: true` permits one concrete clarifying question about new information. Unity grants that opportunity once and keeps the input open for the answer. Both capabilities default to false.

The sections below describe the original E1 client and its configuration.

This is the real HTTP integration for Lin's decisions. The default application calls an OpenAI-compatible `POST /chat/completions` endpoint. It has **no automatic mock or canned-response fallback**. At delivery, `.env` deliberately has no API key and no model. `/health` and `/npc/decide` therefore return HTTP 503 until configured.

M1 supports `reply`, `follow → player`, and `inspect → camera_01`. The model proposes an action; Unity validates and executes it. Only Unity can add verified evidence or change world state. E1 means **Lin himself moves between A and B and personally sees the camera track Lin**. E1 alone does not resolve C1 or permit escape.

## Start the normal service

Use Python 3.12–3.14. Both Python 3.12.14 and 3.14.3 were verified; the delivered local environment uses the fixed installation at `C:\Python314\python.exe`. From PowerShell:

```powershell
.\setup.ps1 -PythonExe "C:\path\to\python.exe"
.\start.ps1
```

`setup.ps1` installs pinned dependencies into `.venv`, and copies `.env.example` only if `.env` does not exist. It never overwrites local configuration. `start.ps1` binds `127.0.0.1:8000` with one worker. Stop a foreground instance with Ctrl+C. Keep this service on loopback; it is a local development gateway, not an authenticated public deployment.

When a provider has been selected, edit `service/.env` locally:

```dotenv
LLM_BASE_URL=https://api.openai.com/v1
LLM_MODEL=YOUR_SELECTED_MODEL
LLM_API_KEY=YOUR_LOCAL_KEY
LLM_PROVIDER=YOUR_PROVIDER_LABEL
```

Set the API **base** URL, not a URL ending `/chat/completions`. Process environment variables override `.env`. HTTPS is required except for loopback HTTP. No credential discovery or other credential-store search occurs. Restart after changing configuration. `.env`, `.venv`, and logs are ignored by this service's `.gitignore`.

Provider compatibility settings:

| Setting | Default | Choices / meaning |
|---|---|---|
| `LLM_RESPONSE_FORMAT` | `json_object` | `json_object`, `json_schema`, or `off` for a provider that does not accept the parameter |
| `LLM_TOKEN_LIMIT_PARAMETER` | `max_tokens` | `max_tokens`, `max_completion_tokens`, or `off` |
| `LLM_MAX_OUTPUT_TOKENS` | `600` | Output token limit, when enabled |
| `LLM_TIMEOUT_SECONDS` | `30` | Total deadline including any single repair; also bounds HTTP I/O |
| `MAX_INFLIGHT` | `4` | Return 503 rather than queue beyond this count |
| `CACHE_TTL_SECONDS` | `900` | Completed result/error retention in this process |
| `CACHE_MAX_ENTRIES` | `256` | Bounded cache; oldest completed entry can be evicted |

No provider request is retried automatically on a network, authentication, quota, or HTTP error. One additional request is permitted only when the returned decision JSON fails its schema or action contract. This repair can consume provider tokens. `attempts` reports 1 or 2 for completed decisions.

## API contract

OpenAPI is available at `http://127.0.0.1:8000/docs`. `/health` does not contact a provider. `ready=true` means local configuration is present; it does not prove the key, model, network, or LLM behavior works. `live_verified` is always false in this health endpoint because it performs no live verification.

Unconfigured health response (HTTP 503):

```json
{"status":"not_configured","ready":false,"provider":"openai-compatible","model":"","api_key_configured":false,"missing_config":["LLM_API_KEY","LLM_MODEL"],"live_verified":false,"mock_mode":false}
```

`POST /npc/decide` requires `Content-Type: application/json`. See the complete examples in `examples/m1_request.json` and `examples/m1_feedback_request.json`. The second is a sample of the **actual engine feedback after successful execution**, not a shortcut for granting E1.

| Request field | Contract |
|---|---|
| `session_id`, `request_id`, `turn_id` | Required strings, 1–96 ASCII letters/digits or `_ . : -` |
| `state_version` | Required integer, 0–2147483647; strings and booleans rejected |
| `trigger` | `user` or `proactive` |
| `player_input` | Required string, max 2000 characters; nonblank for `user`, blank for `proactive` |
| `context.verified_evidence` | Required unique evidence ID strings; max 16; only personally verified engine facts belong here |
| `context.concerns` | Required object with exactly `C1`, `C2`, `C3`, each `open` or `resolved` |
| `context.cooperation_mode` | `talking`, `withdrawn`, or `following` |
| `context.escape_agreed`, `context.route_confirmed` | Required booleans |
| `context.known_targets` | Required unique target IDs; max 32; known does not mean inspected |
| `context.allowed_actions` | Required nonempty unique subset of `reply`, `follow`, `inspect` |
| `context.recent_dialogue` | Required array, max 12 entries; each exactly `{role: user/assistant, content: string}`; content max 2000 |
| `context.last_action_result` | Required JSON object or `null`; object max 8192 UTF-8 bytes and six nesting levels |
| `context.game_time` | Optional string or null, max 32 characters |
| `context.observations` | Optional string array, max 24; each max 1000 characters; actual engine observations |
| `context.player_discovered` | Optional unique ID array, max 16; player discovery does not grant Lin knowledge |

The body limit is 64 KiB. Extra fields are rejected at every typed object boundary. Duplicate JSON keys, nonfinite numbers, absent nullable decision fields, and wrong primitive types are rejected. `last_action_result` is the deliberately extensible JSON-object exception, with size/depth limits.

Successful response (example shape only):

```json
{
  "session_id":"session-001", "request_id":"request-001", "turn_id":"turn-001", "state_version":0,
  "decision": {
    "dialogue":"I'll check whether the camera tracks me.",
    "action":"inspect", "target_id":"camera_01",
    "concern_id":null, "concern_status":"keep", "basis_turn_id":null
  },
  "provider":"YOUR_PROVIDER_LABEL", "model":"YOUR_SELECTED_MODEL", "latency_ms":800, "attempts":1
}
```

All six decision fields are required. `reply` has `target_id:null`; `follow` requires `player`; `inspect` requires `camera_01`. A non-null target must be in `known_targets`, and the action must be in `allowed_actions`. A proactive request can only reply or inspect. M1 requires `concern_id:null`, `concern_status:"keep"`, and `basis_turn_id:null`.

Unity must compare all returned IDs and `state_version` with the pending request before applying anything, reject stale returns, and recheck action preconditions. The gateway echoes versions but does not own the evolving world state. A valid JSON decision is not proof that movement or observation occurred. Natural-language factual quality still requires real-model evaluation; schema validation cannot prove dialogue is truthful.

## Idempotence, cancellation, and errors

Identical `(session_id, request_id)` requests share one in-flight provider call and return the same cached response. Reusing that pair with a different body returns 409 `request_id_conflict`. Both successes and failures are cached. A deliberate retry after fixing a failure needs a **new request_id**. Cache state expires, may be evicted at capacity, and is lost on restart; this is not durable exactly-once delivery. Run one worker.

`POST /npc/cancel` with `{"session_id":"...","request_id":"..."}` returns `status: cancelled`, `already_finished`, or `not_found`. Cancelling an active task closes/cancels its HTTP work and produces 409 `request_cancelled` for its waiters. This cannot guarantee that a remote provider has not already billed the request. A disconnected waiter alone does not cancel shared work; call `/npc/cancel` explicitly when cancelling the decision. Always discard stale responses on the Unity side.

Errors have the shape `{"error":{"code":"...","message":"...","attempts":0}}`. No provider error body, authorization header, or raw validation input is echoed.

| HTTP | Representative codes |
|---|---|
| 409 | `request_id_conflict`, `request_cancelled` |
| 413 / 415 / 422 | `body_too_large`, `unsupported_media_type`, `invalid_request` |
| 502 | `invalid_model_output`, `provider_unreachable`, `provider_http_error`, `provider_protocol_error`, `provider_refusal`, `provider_response_too_large` |
| 503 | `not_configured`, `provider_auth_failed`, `provider_rate_limited`, `service_busy` |
| 504 | `provider_timeout` |

`logs/requests.jsonl` records IDs, sanitized context/player input, sanitized raw decision output, validation rejection, final accepted decision, latency, and errors. Files rotate at 2 MB with four backups. The configured API key, bearer strings, common secret patterns, secret-named object fields, and email addresses are redacted. Do not enter unrelated personal information or credentials into game dialogue. Access logs are disabled in the provided launch scripts. Test gateway logs use `logs/test-fixture`.

## Explicit test fixture — not live LLM evidence

The separate `tests/fixture_provider.py` is a deterministic HTTP test server. It is never imported by the production application. Its responses are visibly prefixed `[TEST FIXTURE]`. Start it explicitly in one PowerShell terminal and start the test gateway in another:

```powershell
.\start-test-fixture.ps1  # upstream fixture: 127.0.0.1:8011
.\start-test-gateway.ps1  # gateway: 127.0.0.1:8001; separate terminal
```

The test gateway script sets process-local configuration only; it does not modify `.env`. It reports `provider=TEST_FIXTURE`, `model=deterministic-test-not-llm`, and `health.mock_mode=true`. Point the Unity test entry explicitly to `http://127.0.0.1:8001`. Return to `http://127.0.0.1:8000` for the normal unconfigured/real-provider service. The fixture uses a public dummy token, `test-fixture-not-a-secret`, which is not an API credential.

This fixture can verify HTTP contracts, movement execution, E1 feedback plumbing, and UI error handling. It cannot establish that a real LLM understands evidence, chooses appropriate actions, resists misleading claims, or meets the course's live-LLM requirement.

Run isolated tests with `./test.ps1` or `.venv/Scripts/python.exe -m pytest -q`. Tests use `httpx.MockTransport` or explicitly mounted fixture ASGI transport and make no external LLM requests.

## API references checked

- [OpenAI Chat Completions create](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create): messages, `/chat/completions`, response formats, and completion envelope.
- [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat): JSON mode requires JSON instructions and does not replace local schema validation.
- [HTTPX timeouts](https://www.python-httpx.org/advanced/timeouts/): connect/read/write/pool timeouts; the service adds one total deadline for the entire decision.
- [FastAPI asynchronous code](https://fastapi.tiangolo.com/async/) and [async tests](https://fastapi.tiangolo.com/advanced/async-tests/).
- [Pydantic model extra-data rules](https://docs.pydantic.dev/latest/concepts/models/#extra-data): strict local contract and forbidden extra fields.
