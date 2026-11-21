from __future__ import annotations

import asyncio
import hashlib
import json
import time
from dataclasses import dataclass

from .config import Settings
from .errors import ServiceError
from .provider import CompletionProvider
from .schemas import DecisionRequest, DecisionResponse


@dataclass
class Entry:
    fingerprint: str
    task: asyncio.Task
    completed_at: float | None = None


class Coordinator:
    """Process-local idempotence; run one Uvicorn worker. Never retries transport errors."""

    def __init__(self, provider: CompletionProvider, settings: Settings):
        self.provider = provider
        self.settings = settings
        self.entries: dict[tuple[str, str], Entry] = {}
        self.lock = asyncio.Lock()

    def _prune(self) -> None:
        now = time.monotonic()
        for key, entry in list(self.entries.items()):
            if entry.completed_at is not None and now - entry.completed_at >= self.settings.cache_ttl_seconds:
                del self.entries[key]

    async def _run(self, request: DecisionRequest) -> DecisionResponse | ServiceError:
        try:
            return await self.provider.decide(request)
        except asyncio.CancelledError:
            return ServiceError(409, "request_cancelled", "This request was cancelled. Use a new request_id for a new decision.")
        except ServiceError as exc:
            self.provider.trace.write("request_error", session_id=request.session_id, request_id=request.request_id,
                                      code=exc.code, attempts=exc.attempts)
            return ServiceError(exc.status, exc.code, exc.message, attempts=exc.attempts)
        except Exception:
            # Do not log exception repr: a provider/library may embed secret request headers.
            self.provider.trace.write("request_error", session_id=request.session_id, request_id=request.request_id,
                                      code="internal_error")
            return ServiceError(500, "internal_error", "The local service encountered an internal error.")

    async def decide(self, request: DecisionRequest) -> DecisionResponse:
        key = (request.session_id, request.request_id)
        fingerprint = hashlib.sha256(json.dumps(request.model_dump(mode="json"), sort_keys=True,
            separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")).hexdigest()
        async with self.lock:
            self._prune()
            entry = self.entries.get(key)
            if entry:
                if entry.fingerprint != fingerprint:
                    raise ServiceError(409, "request_id_conflict", "This session/request_id was already used with a different payload.")
                self.provider.trace.write("cache_hit" if entry.task.done() else "inflight_join", session_id=key[0], request_id=key[1])
            else:
                if sum(not item.task.done() for item in self.entries.values()) >= self.settings.max_inflight:
                    raise ServiceError(503, "service_busy", "The local service has reached its concurrent request limit.")
                if len(self.entries) >= self.settings.max_cache_entries:
                    completed = [(k, v) for k, v in self.entries.items() if v.task.done()]
                    if not completed:
                        raise ServiceError(503, "service_busy", "The local request cache is full.")
                    oldest = min(completed, key=lambda pair: pair[1].completed_at or 0)[0]
                    del self.entries[oldest]
                task = asyncio.create_task(self._run(request))
                entry = Entry(fingerprint, task)
                self.entries[key] = entry
                task.add_done_callback(lambda _task, item=entry: setattr(item, "completed_at", time.monotonic()))
        try:
            # A disconnected waiter must not cancel the same work awaited by another client.
            result = await asyncio.shield(entry.task)
        except asyncio.CancelledError:
            if entry.task.cancelled():
                raise ServiceError(409, "request_cancelled", "This request was cancelled.") from None
            raise
        if isinstance(result, ServiceError):
            # Raising the same cached Exception repeatedly retains growing traceback chains.
            raise ServiceError(result.status, result.code, result.message, attempts=result.attempts)
        return result

    async def cancel(self, session_id: str, request_id: str) -> str:
        async with self.lock:
            self._prune()
            entry = self.entries.get((session_id, request_id))
            if entry is None:
                return "not_found"
            if entry.task.done():
                return "already_finished"
            entry.task.cancel()
            return "cancelled"

    async def close(self) -> None:
        tasks = [entry.task for entry in self.entries.values() if not entry.task.done()]
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
