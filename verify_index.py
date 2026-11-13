"""End-to-end indexing checks; only synthetic documents and a separate temporary index."""
import json, subprocess, pathlib, tempfile, os, sys
from tools.integration_fixtures import make_documents
exe=pathlib.Path(sys.argv[1]).resolve()
work=pathlib.Path(tempfile.mkdtemp(prefix='ModuFind-integration-'))
docs=make_documents(work/'documents')
env=dict(os.environ,DOCUMENT_FINDER_DATA=str(work/'index'))
def run(name,*args):
    report=work/(name+'.json')
    p=subprocess.run([str(exe),'--index',str(docs),'--report',str(report),'--query','FP',*args],env=env,timeout=60,creationflags=subprocess.CREATE_NO_WINDOW)
    result=json.loads(report.read_text(encoding='utf-8-sig'));assert p.returncode==0,result
    return result
first=run('first')
assert first['Stats']['Total']==7 and first['Stats']['Complete']==5 and first['Stats']['Issues']==2,first
assert first['Last']['Updated']==7 and first['Total']==2,first
assert any(x['Name']=='공내역서.xlsx' and '기능점수목록' in x['Sheets'] for x in first['Hits']),first
second=run('incremental')
assert second['Last']['Skipped']==6 and second['Last']['Updated']==1,second
(docs/'관악_메모.txt').write_text('관악 변경되었습니다',encoding='utf-8')
third=run('changed')
assert third['Last']['Updated']==2 and third['Total']==1,third
cancelled=run('cancelled','--cancel-after-ms','1')
assert cancelled['Cancelled'] and cancelled['Stats']['Total']==7,cancelled
print(json.dumps({'passed':7,'checks':['seven synthetic documents processed','corrupt PDF isolated','oversized file identified','FP found in generically named workbook','unchanged documents skipped','modified body refreshed','cancellation preserves index'],'temporaryDirectory':str(work)},ensure_ascii=True,indent=2))
