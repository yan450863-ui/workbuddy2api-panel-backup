"""Create the Windows portable release using an explicit runtime allowlist."""
import argparse
import hashlib
import json
import shutil
import zipfile
import uuid
from pathlib import Path

root = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--version', default='1.5.2')
p.add_argument('--output', type=Path, required=True)
args = p.parse_args()
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
name = f'WorkBuddy2API-windows-x64-v{args.version}'
stage = out / ('staging-' + uuid.uuid4().hex[:8]) / name
stage.parent.mkdir()
stage.mkdir(exist_ok=False)
deploy = root / '部署'
files = [
    'WorkBuddy控制台.exe', 'WorkBuddy控制台.exe.config', 'wb2api.exe',
    'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll',
    '管理工具/管理服务.ps1', '管理工具/配置手机连接.ps1', '管理工具/开启手机连接.cmd',
    '管理工具/启动.cmd', '管理工具/停止.cmd', '管理工具/WorkBuddyRemote.exe',
]
for rel in files:
    dest = stage / rel
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(deploy / rel, dest)
shutil.copytree(deploy / '管理工具/远程组件许可证', stage / 'licenses/Tailscale')
shutil.copy2(root / 'LICENSE', stage / 'LICENSE')
shutil.copy2(deploy / '控制台源码/WebView2-LICENSE.txt', stage / 'licenses/WebView2-LICENSE.txt')
shutil.copy2(root / 'scripts/Windows-release-README.md', stage / '使用说明.md')
config = json.loads((root / 'config.example.json').read_text(encoding='utf-8-sig'))
config['listen'] = '127.0.0.1:7863'
config['api_key'] = ''
(stage / 'config.example.json').write_text(json.dumps(config, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

# Compare staged bytes with real known credentials in memory. Never emit their values.
secrets = set()
def collect(obj):
    if isinstance(obj, dict):
        for key, value in obj.items():
            if any(word in key.lower() for word in ('token','secret','api_key','password')) and isinstance(value,str) and len(value)>=16:
                secrets.add(value.encode())
            collect(value)
    elif isinstance(obj,list):
        for value in obj: collect(value)
for path in [deploy/'config.json', *list((deploy/'auths').glob('*.json'))]:
    if path.is_file():
        try: collect(json.loads(path.read_text(encoding='utf-8-sig')))
        except (ValueError,UnicodeError): pass
manifest = []
for path in sorted(stage.rglob('*')):
    if not path.is_file(): continue
    rel = path.relative_to(stage)
    if any(part.lower() in ('.git','auths','data') for part in rel.parts) or path.name == 'config.json':
        raise RuntimeError('Private path found in package')
    content = path.read_bytes()
    if any(secret in content for secret in secrets): raise RuntimeError('Credential match found; package not published')
    manifest.append({'path':rel.as_posix(),'bytes':len(content),'sha256':hashlib.sha256(content).hexdigest()})
(out/'package-manifest.json').write_text(json.dumps({'version':args.version,'credential_scan':'PASS','files':manifest},ensure_ascii=False,indent=2),encoding='utf-8')
archive = out/(name+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for path in sorted(stage.rglob('*')):
        if path.is_file(): z.write(path,Path(name)/path.relative_to(stage))
with zipfile.ZipFile(archive) as z:
    if z.testzip() is not None: raise RuntimeError('ZIP verification failed')
print(json.dumps({'package':str(archive),'files':len(manifest),'bytes':archive.stat().st_size,'credential_scan':'PASS'}))
