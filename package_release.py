"""Gather upstream notices and make the portable package. Does not include any index."""
import argparse, json, os, pathlib, shutil, urllib.request, xml.etree.ElementTree as ET, zipfile

root=pathlib.Path(__file__).resolve().parent
parser=argparse.ArgumentParser()
parser.add_argument('--publish-dir',type=pathlib.Path,default=root/'artifacts/app')
parser.add_argument('--release-dir',type=pathlib.Path,default=root/'artifacts/release')
parser.add_argument('--version',default='1.1.0')
parser.add_argument('--no-zip',action='store_true')
args=parser.parse_args()
out=args.publish_dir.resolve()
if not (out/'ModuFind.exe').is_file():raise SystemExit('Publish ModuFind.exe before packaging.')
if any(p.suffix in {'.db','.db-wal','.db-shm','.pdb'} for p in out.rglob('*') if p.is_file()):
    raise SystemExit('Publish folder contains an unexpected database or debug file.')
licenses=out/'licenses';licenses.mkdir(exist_ok=True)
cache=pathlib.Path(os.environ.get('NUGET_PACKAGES',str(pathlib.Path.home()/'.nuget/packages')))
sources={
 'ExcelDataReader-LICENSE.txt':'https://raw.githubusercontent.com/ExcelDataReader/ExcelDataReader/4de9116c9b13ebe71cc5ec40374fe6b7e9d91730/LICENSE',
 'PdfPig-LICENSE.txt':'https://raw.githubusercontent.com/UglyToad/PdfPig/a7bb35662bbbf405efddad50aedc9bcdcf515afc/LICENSE',
 'SQLitePCLRaw-LICENSE.txt':'https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v2.1.12/LICENSE.TXT',
 'SQLitePCLRaw-NOTICE.txt':'https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v2.1.12/NOTICE.TXT',
}
for name,url in sources.items():
    with urllib.request.urlopen(url,timeout=20) as response:body=response.read()
    (licenses/name).write_bytes(body)

assets=json.loads((root/'DocumentFinder.App/obj/project.assets.json').read_text(encoding='utf-8-sig'))
notes=[f'모두찾아줘 {args.version} — Third-party software notices','',
       'The original application is licensed under LICENSE.txt. The following components retain their own licenses.',
       'No local document index, user documents, or search history is included in this distribution.','']
for key,value in assets['libraries'].items():
    if value['type']!='package':continue
    package=cache/value['path']
    specs=list(package.glob('*.nuspec'))
    if not specs:continue
    xml=ET.parse(specs[0]).getroot(); metadata=next(e for e in xml if e.tag.split('}')[-1]=='metadata')
    fields={e.tag.split('}')[-1]:(e.text or '').strip() for e in metadata}
    notes += [key,'Authors: '+fields.get('authors',''),'License: '+fields.get('license',fields.get('licenseUrl','see package metadata'))]
    for field in ('copyright','projectUrl'):
        if fields.get(field):notes.append(field+': '+fields[field])
    for file in package.iterdir():
        if file.is_file() and ('license' in file.name.lower() or 'notice' in file.name.lower()):
            dest=licenses/(key.replace('/','-')+'-'+file.name)
            shutil.copyfile(file,dest)
    notes.append('')
notes += ['SQLite native engine is in the public domain: https://www.sqlite.org/copyright.html',
          'PdfPig: Copyright (c) UglyToad and contributors. See PdfPig-LICENSE.txt.',
          'SQLitePCLRaw: Copyright (c) 2014-2024 SourceGear, LLC. See SQLitePCLRaw-LICENSE.txt.',
          'ExcelDataReader: See the complete upstream copyright and license in ExcelDataReader-LICENSE.txt.',
          '.NET and Windows Desktop runtime: Microsoft Corporation and contributors. Included runtime license/third-party notices are in licenses/.']
for framework in assets['project']['frameworks'].values():
    for dep in framework.get('downloadDependencies',[]):
        if dep['name'].lower() not in {'microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64'}:continue
        version=dep['version'].strip('[]').split(',')[0].strip()
        folder=cache/dep['name'].lower()/version
        notes.extend(['',dep['name']+'/'+version,'Copyright (c) .NET Foundation and Contributors; Microsoft Corporation.'])
        for file in folder.iterdir():
            if file.is_file() and ('license' in file.name.lower() or 'notice' in file.name.lower()):
                shutil.copyfile(file,licenses/(dep['name']+'-'+version+'-'+file.name))
(out/'THIRD_PARTY_NOTICES.txt').write_text('\n'.join(notes),encoding='utf-8-sig')
shutil.copyfile(root/'README.md',out/'사용설명서.md');shutil.copyfile(root/'LICENSE.txt',out/'LICENSE.txt')
shutil.copyfile(root/'assets/modufind.ico',out/'modufind.ico')
if not args.no_zip:
    args.release_dir.mkdir(parents=True,exist_ok=True)
    archive=args.release_dir/f'ModuFind-{args.version}-win-x64-portable.zip'
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for f in out.rglob('*'):
            if f.is_file():z.write(f,pathlib.Path('모두찾아줘')/f.relative_to(out))
print(json.dumps({'executable':str(out/'ModuFind.exe'),'notices':len(list(licenses.iterdir()))},ensure_ascii=True,indent=2))
