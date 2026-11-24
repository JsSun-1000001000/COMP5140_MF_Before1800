"""Fetch the pinned, public Kokoro speech assets; no API key is used."""
from pathlib import Path
import hashlib
import json
import urllib.request


def main():
    root = Path(__file__).resolve().parent
    target = root / 'voice-models'
    target.mkdir(exist_ok=True)
    manifest = json.loads((root / 'voice-model-manifest.json').read_text())
    for name, item in manifest.items():
        path = target / name
        if path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == item['sha256']:
            print('Ready:', name)
            continue
        partial = path.with_suffix(path.suffix + '.part')
        print('Downloading:', name, flush=True)
        try:
            digest = hashlib.sha256()
            request = urllib.request.Request(item['url'], headers={'User-Agent': 'Before1800-VoiceSetup/1.0'})
            with urllib.request.urlopen(request, timeout=90) as response, partial.open('wb') as output:
                while chunk := response.read(1024 * 1024):
                    digest.update(chunk)
                    output.write(chunk)
            if digest.hexdigest() != item['sha256']:
                raise RuntimeError('Asset checksum mismatch: ' + name)
            partial.replace(path)
        finally:
            partial.unlink(missing_ok=True)
    print('Local Chinese and English speech assets are ready.')


if __name__ == '__main__':
    main()
