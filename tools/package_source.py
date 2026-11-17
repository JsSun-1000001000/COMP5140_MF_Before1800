"""Package only reviewable project sources; never include runtime credentials/caches."""
from pathlib import Path
import hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
out=root/'Builds'/'Before1800_Source_2026-11-17.zip'
files=set()
for folder in ['Unity/Assets','Unity/Packages','Unity/ProjectSettings','service/app','service/tests','service/examples','docs']:
    for p in (root/folder).rglob('*'):
        if p.is_file() and '__pycache__' not in p.parts and p.suffix not in ('.pyc','.log'):
            files.add(p)
for pattern in ['*.cmd','README.md','.gitignore','service/*.ps1','service/*.md','service/requirements.txt','service/.env.example','service/.gitignore','Tests/CoreHarness/*.cs','Tests/CoreHarness/*.ps1','tools/*.py']:
    files.update(root.glob(pattern))
manifest={str(p.relative_to(root)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
if out.exists():raise SystemExit(f'Archive already exists: {out}')
out.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in sorted(files):z.write(p,'Before1800/'+p.relative_to(root).as_posix())
    z.writestr('Before1800/SOURCE_SHA256.json',json.dumps(manifest,indent=2,ensure_ascii=False))
with zipfile.ZipFile(out) as z:
    assert z.testzip() is None
    assert not any('/.env' in n and not n.endswith('/.env.example') for n in z.namelist())
print(json.dumps({'archive':str(out),'files':len(files),'bytes':out.stat().st_size,'sha256':hashlib.sha256(out.read_bytes()).hexdigest()},ensure_ascii=False))
