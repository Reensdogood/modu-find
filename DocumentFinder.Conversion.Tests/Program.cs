using DocumentFinder.Core;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

if(args.Length==0)throw new ArgumentException("Pass the prepared Filee runtime directory.");
var folder=Path.Combine(Path.GetTempPath(),"ModuFind-conversion-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var output=Path.Combine(folder,"output");var client=new FileeConversionClient(args[0],Path.Combine(folder,"private-engine-data"));var checks=0;
void Check(bool condition,string title){if(!condition)throw new Exception("FAIL: "+title);checks++;Console.WriteLine("PASS: "+title);}
var formats=await client.FormatsAsync();
Check(new[]{"hwp","hwpx","docx","xlsx","pptx","pdf","txt","csv"}.All(id=>formats.Any(f=>f.Id==id)),"Filee document catalog exposes HWP/Office/PDF/text");
Check(formats.All(f=>f.Category is not "video" and not "audio" and not "archive"),"document flow excludes unrelated executables/media/archive actions");
var source=Path.Combine(folder,"원본 sample.txt");await File.WriteAllTextAsync(source,"ModuFind conversion test\n한글 문서 변환 검증\nSecond line.",new UTF8Encoding(false));
var originalHash=System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));var originalTime=File.GetLastWriteTimeUtc(source);
var targets=await client.TargetsAsync([source]);Check(targets.Any(t=>t.Id=="pdf"&&!t.NeedsEngine)&&targets.Any(t=>t.Id=="hwpx"&&!t.NeedsEngine),"native document routes are immediately available");
async Task<string> Convert(string from,string to)
{
    var batch=await client.ConvertAsync([from],to,output);var result=batch.Files.Single();
    Check(result.State=="done"&&result.Outputs.Length>0&&result.Outputs.All(File.Exists),Path.GetExtension(from)+" → "+to+" creates real output: "+result.Message);
    Check(result.Outputs.All(p=>TextRules.IsWithin(p,batch.Directory)&&!p.Equals(from,StringComparison.OrdinalIgnoreCase)),"output remains inside its new result folder");return result.Outputs[0];
}
var docx=await Convert(source,"docx");Check(DocumentExtractor.Extract(docx).Content.Contains("한글 문서 변환 검증"),"DOCX preserves Korean text");
var hwpx=await Convert(docx,"hwpx");Check(DocumentExtractor.Extract(hwpx).Content.Contains("ModuFind conversion test"),"HWPX result has readable body text");
var hwp=await Convert(hwpx,"hwp");var text=await Convert(hwp,"txt");Check((await File.ReadAllTextAsync(text)).Contains("ModuFind conversion test"),"HWP binary round trip preserves text");
var pdf=await Convert(hwpx,"pdf");Check(DocumentExtractor.Extract(pdf).Content.Contains("ModuFind"),"rendered PDF contains extractable text");
var fromPdf=await Convert(pdf,"docx");Check(DocumentExtractor.Extract(fromPdf).Content.Contains("ModuFind"),"PDF to editable DOCX extracts text");
var csv=Path.Combine(folder,"표.csv");await File.WriteAllTextAsync(csv,"이름,금액\r\n샘플,1234\r\n",new UTF8Encoding(true));
var xlsx=await Convert(csv,"xlsx");var backCsv=await Convert(xlsx,"csv");Check((await File.ReadAllTextAsync(backCsv)).Contains("1234"),"spreadsheet round trip preserves values");
var pptx=await Convert(pdf,"pptx");using(var zip=ZipFile.OpenRead(pptx))Check(zip.GetEntry("ppt/slides/slide1.xml") is not null,"PDF to PPTX creates a slide package");
_ = await Convert(pptx,"pdf");
var image=await Convert(hwpx,"png");Check(File.ReadAllBytes(image).Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"document page rendered as PNG");
var secondFolder=Path.Combine(folder,"same-names");Directory.CreateDirectory(secondFolder);var second=Path.Combine(secondFolder,Path.GetFileName(source));File.Copy(source,second);
var same=await client.ConvertAsync([source,second],"txt",output);
Check(same.Files.All(f=>f.State=="done")&&same.Files.SelectMany(f=>f.Outputs).Distinct(StringComparer.OrdinalIgnoreCase).Count()==2,"same-named inputs cannot overwrite each other's results");
var broken=Path.Combine(folder,"broken.pdf");File.WriteAllText(broken,"not a PDF");
var failed=await client.ConvertAsync([broken],"docx",output);Check(failed.Files.Single().State=="failed"&&failed.Files.Single().Outputs.Length==0,"malformed document is reported without publishing partial output");
using(var cancel=new CancellationTokenSource(100))
{
    var cancelled=await client.ConvertAsync([source,second],"pdf",output,token:cancel.Token);
    Check(cancelled.Cancelled&&cancelled.Files.Any(f=>f.State=="cancelled"),"active conversion is cancellable");
    Check(!Directory.GetDirectories(output,".work-*",SearchOption.AllDirectories).Any(),"cancelled staging output is removed");
}
var rejected=false;try{await client.ConvertAsync([source],"../../bad",output);}catch(ArgumentException){rejected=true;}
Check(rejected,"unregistered output format is rejected before executing a converter");
Check(System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==originalHash&&File.GetLastWriteTimeUtc(source)==originalTime,"original content and modification time are unchanged");
Check(Directory.Exists(Path.Combine(folder,"private-engine-data")),"conversion settings are isolated from an existing Filee installation");
if(args.Contains("--install-pandoc"))
{
    await client.InstallEngineAsync("pandoc");var engines=await client.EnginesAsync();Check(engines.Any(e=>e.Id=="pandoc"&&e.Status=="installed"),"optional engine downloads and installs with Filee verification");
    _ = await Convert(docx,"rtf");
}
Console.WriteLine($"All {checks} conversion checks passed. Artifacts: {folder}");
