"""Bundle the locked MIT kordoc parser with an official Node.js runtime."""
import argparse, json, pathlib, shutil, subprocess, urllib.request, os

root = pathlib.Path(__file__).resolve().parent.parent
p = argparse.ArgumentParser(); p.add_argument('--publish-dir', type=pathlib.Path, required=True); args = p.parse_args()
destination = args.publish_dir.resolve() / 'parsing' / 'kordoc'
destination.mkdir(parents=True, exist_ok=True)
node = shutil.which('node'); npm = shutil.which('npm.cmd') or shutil.which('npm')
if not node or not npm: raise SystemExit('Node.js 24 and npm are required to build the parsing runtime.')
version = subprocess.check_output([node, '--version'], text=True).strip()
if not version.startswith('v24.'): raise SystemExit('Use Node.js 24 LTS to build the parser.')
for name in ['package.json', 'package-lock.json', 'bridge.mjs']:
    shutil.copy2(root/'parsing'/name, destination/name)
shutil.copy2(node, destination/'node.exe')
subprocess.run([npm, 'ci', '--omit=dev', '--omit=optional', '--ignore-scripts', '--no-audit', '--no-fund'], cwd=destination, check=True,
               creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
with urllib.request.urlopen(f'https://raw.githubusercontent.com/nodejs/node/{version}/LICENSE', timeout=30) as r:
    (destination/'NODE-LICENSE.txt').write_bytes(r.read())
packages=[]
for package in (destination/'node_modules').rglob('package.json'):
    try:
        value=json.loads(package.read_text(encoding='utf-8'))
        if value.get('name'): packages.append({'name':value['name'],'version':value.get('version'),'license':value.get('license'),'repository':value.get('repository')})
    except (ValueError, OSError): pass
(destination/'THIRD-PARTY-PACKAGES.json').write_text(json.dumps(packages,ensure_ascii=False,indent=2),encoding='utf-8')
(destination/'MODUFIND-PROVENANCE.json').write_text(json.dumps({'parser':'kordoc','version':'4.24.83','node':version,'source':'https://github.com/chrisryugj/kordoc','mode':'offline; optional OCR/PDF dependencies omitted'},indent=2),encoding='utf-8')
print(json.dumps({'parser':'kordoc 4.24.83','node':version,'packages':len(packages),'destination':str(destination)},ensure_ascii=True))
