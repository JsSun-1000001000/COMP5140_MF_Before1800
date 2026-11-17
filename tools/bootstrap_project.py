"""Create the project from the installed official URP template, preserving existing work."""
from pathlib import Path
import json
import tarfile

root = Path(__file__).resolve().parents[1]
unity = root / 'Unity'
template = Path(r'C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Data\Resources\PackageManager\ProjectTemplates\com.unity.template.3d-cross-platform-17.0.14.tgz')
prefix = 'package/ProjectData~/'
count = 0
with tarfile.open(template) as archive:
    for member in archive.getmembers():
        if not member.isfile() or not member.name.startswith(prefix):
            continue
        relative = Path(member.name[len(prefix):])
        if relative.parts[0] not in ('Assets', 'Packages', 'ProjectSettings') or '..' in relative.parts:
            continue
        target = unity / relative
        if target.exists():
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(archive.extractfile(member).read())
        count += 1
manifest = unity / 'Packages/manifest.json'
data = json.loads(manifest.read_text(encoding='utf-8-sig'))
for unused in ('com.unity.collab-proxy', 'com.unity.ide.rider', 'com.unity.ide.visualstudio', 'com.unity.visualscripting'):
    data['dependencies'].pop(unused, None)
data['dependencies']['com.unity.nuget.newtonsoft-json'] = '3.2.1'
data['dependencies'].update({'com.unity.inputsystem':'1.20.0', 'com.unity.ai.navigation':'2.0.14', 'com.unity.render-pipelines.universal':'17.3.0', 'com.unity.test-framework':'1.6.0', 'com.unity.timeline':'1.8.13', 'com.unity.modules.unitywebrequest':'1.0.0', 'com.unity.modules.screencapture':'1.0.0'})
manifest.write_text(json.dumps(data, indent=2)+'\n', encoding='utf-8')
(unity/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.25f1\n', encoding='utf-8')
print(json.dumps({'copied':count,'project':str(unity),'dependencies':data['dependencies']},ensure_ascii=False))
