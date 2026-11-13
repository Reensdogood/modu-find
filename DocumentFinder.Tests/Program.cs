using DocumentFinder.Core;
using System.IO.Compression;
using System.Text;

var checks=0;
void Check(bool condition,string title){if(!condition)throw new Exception("FAIL: "+title);Console.WriteLine("PASS: "+title);checks++;}
var dir=Path.Combine(Path.GetTempPath(),"DocumentFinder-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
FileRecord Record(string name,long ticks=1)=>new(Path.Combine(dir,name),10,ticks,dir);
var db=new SearchStore(Path.Combine(dir,"test.db"));
var a=Record("관악_최종.xlsx",10);var b=Record("사업설계.xlsx",20);var c=Record("관악_참고.pdf",30);
db.Update(a,new("소프트웨어 개발 FP 산정",["기능점수목록"]));
db.Update(b,new("관악구의 SW FP 산출근거",["FP산정(간이법)"]));
db.Update(c,new("관악 SW 관련 참고",[]));
List<SearchHit> Find(string q,string kind="전체",bool names=false,bool newest=false)=>db.Search(new(q,kind,names,newest),out _);
Check(Find("관악 FP").Count==2,"Korean two-character term + FP use AND");
Check(Find("관악 FP").First().Path==a.Path,"filename match ranks above body match");
Check(Find("관악구 SW FP").Single().Path==b.Path,"trigram and short terms work together");
Check(Find("소프트웨어").Single().Path==a.Path,"long Korean body term uses trigram");
Check(Find("\"FP 산출근거\"").Single().Path==b.Path,"quoted exact phrase");
Check(Find("FP","엑셀").Count==2 && Find("FP","PDF").Count==0,"document type filter");
Check(Find("FP",names:true).Count==0,"filename-only excludes body and sheet matches");
Check(Find("FP",newest:true).First().Path==b.Path,"date ordering");
Check(Find("간이법").Single().Path==b.Path,"sheet name is searchable");
Check(Find("' OR 1=1 --").Count==0,"query is parameterized");
Check(Find("ＳＷ ｆｐ").Single().Path==b.Path,"case and full-width normalization");
Check(!db.NeedsUpdate(a) && db.NeedsUpdate(a with{ModifiedTicks=11}),"incremental freshness uses modification time");
db.RegisterFiles([a with{ModifiedTicks=11}]);
Check(db.NeedsUpdate(a with{ModifiedTicks=11}),"discovery does not hide pending content change");
db.Update(a with{ModifiedTicks=11},new("새로운내용",["새 시트"]));
Check(Find("소프트웨어").Count==0 && Find("새로운내용").Count==1,"update removes stale FTS text");
db.SetSetting("roots","한국어 설정");Check(db.GetSetting("roots")=="한국어 설정","settings persist Unicode");
Check(TextRules.ReduceRoots([dir,Path.Combine(dir,"sub"),dir.ToUpperInvariant()]).Length==1,"overlapping roots are deduplicated");
Check(!TextRules.IsWithin(dir+"-other/a",dir),"root prefix cannot match sibling path");
var offline=Path.Combine(dir,"disconnected");db.Update(new(Path.Combine(offline,"keep.xlsx"),1,1,offline),new("오프라인",[]));
Check(db.RemoveMissing([offline],CancellationToken.None)==0,"unavailable root keeps saved index");
Check(db.RemoveMissing([dir],CancellationToken.None)==4 && db.Stats().Total==0,"missing entries remove corresponding FTS rows");

void Zip(string name,Dictionary<string,string> entries)
{
    using var zip=ZipFile.Open(Path.Combine(dir,name),ZipArchiveMode.Create);
    foreach(var (path,xml) in entries){using var w=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false));w.Write(xml);}
}
Zip("synthetic.xlsx",new(){
 ["xl/workbook.xml"]="""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="FP산정" sheetId="1" r:id="rId1"/></sheets></workbook>""",
 ["xl/_rels/workbook.xml.rels"]="""<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>""",
 ["xl/sharedStrings.xml"]="""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>관악 소프트웨어</t></si></sst>""",
 ["xl/worksheets/sheet1.xml"]="""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>FP 개발비</t></is></c><c r="C1"><f>1+2</f><v>3</v></c></row></sheetData></worksheet>"""});
var x=DocumentExtractor.Extract(Path.Combine(dir,"synthetic.xlsx"));
Check(x.Sheets.Single()=="FP산정"&&x.Content.Contains("A1: 관악 소프트웨어")&&x.Content.Contains("B1: FP 개발비")&&x.Content.Contains("C1: 3"),"Excel shared strings, inline strings and cached formula values");
Zip("synthetic.docx",new(){["word/document.xml"]="""<w:document xmlns:w="urn:word"><w:body><w:p><w:r><w:t>문서</w:t></w:r><w:r><w:t> 본문</w:t></w:r></w:p></w:body></w:document>"""});
Check(DocumentExtractor.Extract(Path.Combine(dir,"synthetic.docx")).Content.Contains("문서 본문"),"DOCX text runs");
Zip("synthetic.pptx",new(){["ppt/slides/slide1.xml"]="""<root xmlns:a="urn:ppt"><a:p><a:r><a:t>발표자료 FP</a:t></a:r></a:p></root>"""});
Check(DocumentExtractor.Extract(Path.Combine(dir,"synthetic.pptx")).Content.Contains("발표자료 FP"),"PPTX slide text");
Zip("synthetic.hwpx",new(){["Contents/section0.xml"]="""<root><p><run><t>바깥 문단</t><table><p><run><t>표 안 텍스트</t></run></p></table></run></p></root>"""});
var h=DocumentExtractor.Extract(Path.Combine(dir,"synthetic.hwpx")).Content;
Check(h.Contains("바깥 문단")&&h.Split("표 안 텍스트").Length==2,"HWPX nested table text is read once");
File.WriteAllText(Path.Combine(dir,"large.txt"),new string('가',450000),new UTF8Encoding(false));
var large=DocumentExtractor.Extract(Path.Combine(dir,"large.txt"));
Check(large.Status=="일부 내용"&&large.Content.Length<=TextRules.MaxTextLength+2,"long text is bounded and identified as partial");
File.WriteAllText(Path.Combine(dir,"~$lock.xlsx"),"ignored");Directory.CreateDirectory(Path.Combine(dir,"node_modules"));File.WriteAllText(Path.Combine(dir,"node_modules","ignore.txt"),"ignored");
var scanned=FileScanner.Discover([dir,dir],null,CancellationToken.None);
Check(scanned.Count==5&&!scanned.Any(f=>f.Path.Contains("node_modules")||Path.GetFileName(f.Path).StartsWith("~$")),"scan excludes duplicate roots, lock files and dependencies");
try{FileScanner.Discover([dir],null,new CancellationToken(true));throw new Exception("FAIL cancellation");}catch(OperationCanceledException){checks++;Console.WriteLine("PASS: scanner cancellation");}

// Optional real-world fixture checks: each pair is file path and expected text.
for(var i=0;i+1<args.Length;i+=2)
{
    var result=DocumentExtractor.Extract(args[i]);Check(result.Status=="완료"||result.Status=="일부 내용","real fixture readable: "+Path.GetFileName(args[i]));
    Check(result.Content.Contains(args[i+1],StringComparison.OrdinalIgnoreCase),"real fixture text: "+args[i+1]);
}
await UpdateChecks.Run(Check);
Console.WriteLine($"All {checks} checks passed. Temporary fixtures: {dir}");
