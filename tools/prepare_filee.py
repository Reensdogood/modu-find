"""Bundle Filee's unmodified CLI and runtime, verifying a pinned official release first."""
import argparse, hashlib, json, os, pathlib, shutil, urllib.request, zipfile

root=pathlib.Path(__file__).resolve().parent.parent
config=json.loads((root/'third_party/filee.json').read_text(encoding='utf-8-sig'))
parser=argparse.ArgumentParser();parser.add_argument('--publish-dir',type=pathlib.Path,required=True);args=parser.parse_args()
cache=pathlib.Path(os.environ.get('LOCALAPPDATA',str(pathlib.Path.home()/'.cache')))/'ModuFindBuildCache'/('Filee-'+config['version']);cache.mkdir(parents=True,exist_ok=True)
archive=cache/config['asset']
def sha256(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
url=f"https://github.com/{config['repository']}/releases/download/v{config['version']}/{config['asset']}"
if not archive.exists() or archive.stat().st_size!=config['size'] or sha256(archive)!=config['sha256']:
    temporary=archive.with_suffix('.download')
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'ModuFind-build'}),timeout=180) as response,temporary.open('wb') as output:shutil.copyfileobj(response,output)
    if temporary.stat().st_size!=config['size'] or sha256(temporary)!=config['sha256']:raise SystemExit('Filee package SHA-256 mismatch')
    temporary.replace(archive)
destination=args.publish_dir.resolve()/'conversion'/('filee-'+config['version']);destination.mkdir(parents=True,exist_ok=True)
excluded={'Filee.exe','Filee.dll','Filee.deps.json','Filee.runtimeconfig.json','Filee.xml','FileeExplorerMenu.dll','FileeExplorerMenu.msix'}
count=0
with zipfile.ZipFile(archive) as z:
    for entry in z.infolist():
        relative=pathlib.PurePosixPath(entry.filename.replace('\\','/'))
        if relative.is_absolute() or '..' in relative.parts or any(':' in p for p in relative.parts):raise SystemExit('Unsafe upstream ZIP path')
        if entry.is_dir() or relative.suffix.lower()=='.pdb' or (len(relative.parts)==1 and relative.name in excluded) or relative.parts[0]=='cli':continue
        target=destination.joinpath(*relative.parts);target.parent.mkdir(parents=True,exist_ok=True)
        with z.open(entry) as source,target.open('wb') as output:shutil.copyfileobj(source,output)
        count+=1
source=f"https://raw.githubusercontent.com/{config['repository']}/{config['commit']}/"
for source_name,output_name in [('LICENSE','FILEE-LICENSE.txt'),('THIRD-PARTY-NOTICES.md','FILEE-THIRD-PARTY-NOTICES.md')]:
    with urllib.request.urlopen(source+source_name,timeout=30) as response:(destination/output_name).write_bytes(response.read())
runtime=json.loads((destination/'filee-cli.runtimeconfig.json').read_text(encoding='utf-8-sig'))
framework=runtime['runtimeOptions']['includedFrameworks'][0]['version']
for name in ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT']:
    with urllib.request.urlopen(f'https://raw.githubusercontent.com/dotnet/runtime/v{framework}/{name}',timeout=30) as response:(destination/('DOTNET-'+name)).write_bytes(response.read())
(destination/'MODUFIND-PROVENANCE.json').write_text(json.dumps(config,indent=2),encoding='utf-8')
if not (destination/'filee-cli.exe').exists() or not (destination/'engines/rhwp/rhwp.exe').exists():raise SystemExit('Required Filee CLI or rhwp engine is missing')
print(json.dumps({'fileeVersion':config['version'],'verifiedSha256':config['sha256'],'filesBundled':count,'destination':str(destination)},ensure_ascii=True))
