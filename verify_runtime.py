"""Validate the shipped executable's worker protocol with generated PDF documents."""
import json, subprocess, pathlib, sys, time, tempfile
from tools.integration_fixtures import make_pdf
exe=pathlib.Path(sys.argv[1]).resolve()
work=pathlib.Path(tempfile.mkdtemp(prefix='ModuFind-worker-'))
pdf=work/'text.pdf';scan=work/'image.pdf';missing=work/'missing.xlsx'
make_pdf(pdf);make_pdf(scan,image_only=True)
started=time.monotonic()
p=subprocess.Popen([str(exe),'--extract-worker'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',errors='replace',creationflags=subprocess.CREATE_NO_WINDOW)
output,err=p.communicate(''.join(json.dumps(str(x),ensure_ascii=True)+'\n' for x in [pdf,scan,missing,pdf]),timeout=50)
records=[json.loads(x) for x in output.splitlines() if x.strip()]
assert p.returncode==0 and len(records)==4,(p.returncode,output,err)
assert records[0]['Status']=='완료' and 'ModuFind FP sample' in records[0]['Content'],records[0]
assert records[1]['Status']=='OCR 필요',records[1]
assert records[2]['Status']=='읽기 실패',records[2]
assert records[3]['Status']=='완료',records[3]
print(json.dumps({'passed':4,'checks':['text PDF','image-only PDF marked OCR required','missing file error','worker recovers after error'],'seconds':round(time.monotonic()-started,2)},ensure_ascii=True,indent=2))
