"""Offline neural speech for Lin. No dialogue or credentials leave this Mac."""
from __future__ import annotations
import asyncio
import hashlib
import io
import re
import threading
from pathlib import Path
from typing import Literal
from pydantic import Field
from .schemas import StrictModel
from .errors import ServiceError

class SpeechRequest(StrictModel):
    text: str = Field(min_length=1, max_length=1200)
    language: Literal['zh', 'en'] = 'zh'

MODEL_DIR = Path(__file__).resolve().parents[1] / 'voice-models'
_lock = asyncio.Semaphore(1)
_thread_lock = threading.Lock()
_cache: dict[str, bytes] = {}
_engine = None
_chinese = None

class _TypedSession:
    """Respect model input types (the 0.5 wrapper hard-codes int speed for new exports)."""
    def __init__(self, session): self.session = session
    def get_inputs(self): return self.session.get_inputs()
    def run(self, outputs, inputs):
        import numpy as np
        for spec in self.session.get_inputs():
            if spec.type == 'tensor(float)': inputs[spec.name] = np.asarray(inputs[spec.name], dtype=np.float32)
        return self.session.run(outputs, inputs)

def _load():
    global _engine, _chinese
    if _engine is not None: return
    import onnxruntime as ort
    ort.disable_telemetry_events()
    from kokoro_onnx import Kokoro
    from misaki import zh
    engine = Kokoro(str(MODEL_DIR/'kokoro-v1.1-zh.onnx'), str(MODEL_DIR/'voices-v1.1-zh.bin'), vocab_config=str(MODEL_DIR/'config.json'))
    engine.sess = _TypedSession(engine.sess)
    _chinese = zh.ZHG2P(version='1.1', en_callable=lambda text: engine.tokenizer.phonemize(text, lang='en-us'))
    _engine = engine

def _render(text: str, language: str) -> bytes:
    import numpy as np
    import soundfile as sf
    with _thread_lock:
        _load()
        chunks = []
        # Sentence-sized chunks preserve pauses and avoid silently truncating longer replies.
        sentences = re.split(r'(?<=[。！？.!?])\s*', text.strip())
        for sentence in sentences:
            if not sentence.strip(): continue
            for offset in range(0, len(sentence), 120 if language=='zh' else 250):
                part = sentence[offset:offset+(120 if language=='zh' else 250)]
                if language == 'zh':
                    phonemes, _ = _chinese(part)
                    samples, rate = _engine.create(phonemes, 'zf_001', speed=1.04, is_phonemes=True)
                else:
                    samples, rate = _engine.create(part, 'af_maple', speed=1.0, lang='en-us')
                chunks.extend([samples, np.zeros(int(rate*.12),dtype=np.float32)])
        if not chunks: raise ValueError('No spoken text')
        samples=np.concatenate(chunks)
        if len(samples)<100 or not np.isfinite(samples).all(): raise ValueError('Invalid audio')
        # Voice is clear without clipping; keep breath and phrase dynamics intact.
        peak=float(np.max(np.abs(samples)))
        if peak > .95: samples*=.95/peak
        output=io.BytesIO();sf.write(output,samples,rate,format='WAV',subtype='PCM_16')
        return output.getvalue()

async def synthesize(request: SpeechRequest) -> bytes:
    key=hashlib.sha256(('neural-v1.1-zf001-'+request.language+request.text).encode()).hexdigest()
    async with _lock:
        if key in _cache: return _cache[key]
        try:
            data=await asyncio.wait_for(asyncio.to_thread(_render,request.text,request.language),25)
        except Exception:
            # Never fall back silently to the old robotic system voice.
            raise ServiceError(503,'voice_unavailable','Voice is temporarily unavailable. Subtitles remain available.') from None
        if len(_cache)>=64:_cache.pop(next(iter(_cache)))
        _cache[key]=data
        return data
