from __future__ import annotations

from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, Response
from .speech import SpeechRequest, synthesize
from pydantic import ValidationError

from .config import Settings
from .coordinator import Coordinator
from .errors import ServiceError
from .provider import CompletionProvider
from .schemas import CancelRequest, DecisionRequest, DecisionResponse, strict_json_loads
from .tracing import TraceRecorder


class BodyLimitMiddleware:
    """Bound incoming bodies before JSON parsing, including chunked requests."""
    def __init__(self, app, limit: int = 65536):
        self.app = app
        self.limit = limit

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        chunks = []
        size = 0
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            chunk = message.get("body", b"")
            size += len(chunk)
            if size > self.limit:
                response = JSONResponse({"error": {"code": "body_too_large", "message": "Maximum body size is 65536 bytes.", "attempts": 0}}, status_code=413)
                return await response(scope, receive, send)
            chunks.append(chunk)
            if not message.get("more_body", False):
                break
        delivered = False
        async def buffered_receive():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": b"".join(chunks), "more_body": False}
            return await receive()
        await self.app(scope, buffered_receive, send)


async def parse_model(request: Request, model):
    if request.headers.get("content-type", "").split(";", 1)[0].strip().lower() != "application/json":
        raise ServiceError(415, "unsupported_media_type", "Send application/json.")
    try:
        return model.model_validate(strict_json_loads((await request.body()).decode("utf-8")))
    except (ValidationError, ValueError, TypeError, RecursionError):
        # No reflected input: JSON validation errors can contain credentials entered by mistake.
        raise ServiceError(422, "invalid_request", "Request JSON does not match the documented strict contract.") from None


def inline_schema(model) -> dict:
    schema = model.model_json_schema()
    definitions = schema.pop("$defs", {})
    def expand(value):
        if isinstance(value, dict):
            if "$ref" in value:
                return expand(definitions[value["$ref"].split("/")[-1]])
            return {key: expand(item) for key, item in value.items()}
        if isinstance(value, list):
            return [expand(item) for item in value]
        return value
    return expand(schema)


def create_app(settings: Settings | None = None, *, provider_transport: httpx.AsyncBaseTransport | None = None) -> FastAPI:
    try:
        configuration = settings or Settings.from_environment()
    except ValidationError:
        # Pydantic's default repr includes input values, which may contain a pasted credential.
        raise RuntimeError("Invalid service configuration. Check variable types and the API base URL; values are intentionally omitted.") from None

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        trace = TraceRecorder(configuration.log_dir, configuration.api_key.get_secret_value())
        async with httpx.AsyncClient(transport=provider_transport, timeout=httpx.Timeout(configuration.timeout_seconds),
                follow_redirects=False, trust_env=False) as client:
            provider = CompletionProvider(configuration, client, trace)
            provider.enable_live_verification = provider_transport is None
            app.state.coordinator = Coordinator(provider, configuration)
            try:
                yield
            finally:
                await app.state.coordinator.close()
                trace.close()

    app = FastAPI(title="Before 18:00 NPC Gateway", version="0.1.0", lifespan=lifespan)
    app.state.settings = configuration
    app.add_middleware(BodyLimitMiddleware)

    @app.exception_handler(ServiceError)
    async def service_error(_request: Request, exc: ServiceError):
        return JSONResponse(exc.body(), status_code=exc.status)

    @app.get("/health")
    async def health():
        missing = configuration.missing_config
        return JSONResponse({"status": "not_configured" if missing else "ready", "ready": not missing,
            "provider": configuration.provider, "model": configuration.model,
            "api_key_configured": "LLM_API_KEY" not in missing, "missing_config": missing,
            "live_verified": bool(getattr(getattr(getattr(app.state, "coordinator", None), "provider", None), "live_verified", False)),
            "mock_mode": configuration.provider == "TEST_FIXTURE"}, status_code=503 if missing else 200)

    @app.post("/npc/decide", response_model=DecisionResponse,
              openapi_extra={"requestBody": {"required": True, "content": {"application/json": {"schema": inline_schema(DecisionRequest)}}}})
    async def decide(request: Request):
        body = await parse_model(request, DecisionRequest)
        if configuration.missing_config:
            raise ServiceError(503, "not_configured", "Set LLM_API_KEY and LLM_MODEL in service/.env before using real decisions.")
        result = await request.app.state.coordinator.decide(body)
        return result

    @app.post("/npc/cancel", openapi_extra={"requestBody": {"required": True,
              "content": {"application/json": {"schema": inline_schema(CancelRequest)}}}})
    async def cancel(request: Request):
        body = await parse_model(request, CancelRequest)
        status = await request.app.state.coordinator.cancel(body.session_id, body.request_id)
        return {"session_id": body.session_id, "request_id": body.request_id, "status": status}

    @app.post("/speech")
    async def speech(request: Request):
        body = await parse_model(request, SpeechRequest)
        return Response(await synthesize(body), media_type="audio/wav", headers={"Cache-Control": "no-store"})

    return app


app = create_app()
