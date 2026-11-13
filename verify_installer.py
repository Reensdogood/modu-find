"""Exercise install/repair/remove in a temporary app folder, preserving existing index data."""
import hashlib, json, os, pathlib, re, subprocess, sys, tempfile, winreg

setup=pathlib.Path(sys.argv[1]).resolve()
version=re.fullmatch(r'ModuFind-Setup-(\d+\.\d+\.\d+)-win-x64\.exe',setup.name).group(1)
work=pathlib.Path(tempfile.mkdtemp(prefix='ModuFind-installer-'));target=work/'installed'
key=r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{49C94EA7-7F80-4BAC-BB30-2E23BA2D5F4A}_is1'
def registration():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key) as k:
            return {name:winreg.QueryValueEx(k,name)[0] for name in ['DisplayName','DisplayVersion','InstallLocation','UninstallString']}
    except FileNotFoundError:return None
assert registration() is None,'Existing installed app found; this temporary install test must not overwrite its registration.'
data=pathlib.Path(os.environ['LOCALAPPDATA'])/'FunnetDocumentFinder/documents.db'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest() if p.exists() else None
before=digest(data)
for phase in ['install','repair']:
    args=[str(setup),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TESTINSTALL','/TASKS=!desktopicon',f'/DIR={target}',f'/LOG={work/(phase+".log")}']
    if phase=='repair':args.append('/UPDATE')
    result=subprocess.run(args,timeout=120,creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode==0,(phase,result.returncode,work)
    record=registration();assert record and record['DisplayName']=='모두찾아줘' and record['DisplayVersion']==version,record
    assert (target/'ModuFind.exe').exists() and (target/'unins000.exe').exists(),list(target.iterdir())
    assert digest(data)==before,'Existing index changed during setup.'
    if phase=='install':(target/'user-note.txt').write_text('This user file must survive removal.',encoding='utf-8')
result=subprocess.run([str(target/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',f'/LOG={work/"uninstall.log"}'],timeout=90,creationflags=subprocess.CREATE_NO_WINDOW)
assert result.returncode==0,(result.returncode,work)
assert registration() is None,'Uninstall registration not removed.'
assert not (target/'ModuFind.exe').exists(),'Installed executable not removed.'
assert (target/'user-note.txt').exists(),'Uninstaller removed an unrelated user file.'
assert digest(data)==before,'Existing search index changed during removal.'
print(json.dumps({'passed':7,'checks':['per-user install succeeds','Windows app entry has correct name and version','uninstaller registered','repair/update succeeds','uninstall removes managed files and registration','unrelated user file survives','existing search index remains byte-identical'],'temporaryDirectory':str(work)},ensure_ascii=True,indent=2))
