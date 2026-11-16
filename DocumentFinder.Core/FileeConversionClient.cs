using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DocumentFinder.Core;

public sealed record ConversionFormat(string Id,string Name,string Category,string[] Extensions,bool Writable);
public sealed record ConversionTarget(string Id,string Name,bool NeedsEngine)
{
    public override string ToString()=>Name+" (."+Id+")"+(NeedsEngine?" · 추가 엔진 필요":"");
}
public sealed record ConversionProgress(string Source,string State,int Percent,string Message="");
public sealed record ConversionFileResult(string Source,string State,string[] Outputs,string Message,string[] NeededEngines);
public sealed record ConversionBatchResult(string Directory,IReadOnlyList<ConversionFileResult> Files,bool Cancelled);
public sealed record ConversionEngine(string Id,string Name,string Status,string Version,long DownloadSize,bool Downloadable);

/// <summary>Document conversion through the unmodified, pinned Filee CLI. Originals are never replaced.</summary>
public sealed class FileeConversionClient
{
    public const string EngineVersion="2.4.1";
    private static readonly HashSet<string> DocumentCategories=new(StringComparer.OrdinalIgnoreCase){"document","spreadsheet","presentation","hwp","pdf","text","ebook"};
    private static readonly HashSet<string> ImageIds=new(StringComparer.OrdinalIgnoreCase){"png","jpg","webp","tiff","bmp","gif"};
    private static readonly HashSet<string> DataIds=new(StringComparer.OrdinalIgnoreCase){"json","jsonl","yaml","toml","xml"};
    public static readonly IReadOnlyDictionary<string,string> OptionalEngines=new Dictionary<string,string>{["libreoffice"]="LibreOffice · 구형 Office·OpenDocument",["pandoc"]="Pandoc · 문서·마크업",["calibre"]="calibre · 전자책"};
    private readonly string executable,dataDirectory;
    private List<ConversionFormat>? formats;
    private readonly Dictionary<string,(HashSet<string> Now,HashSet<string> Later)> routes=new(StringComparer.OrdinalIgnoreCase);
    public FileeConversionClient(string engineDirectory,string dataDirectory)
    {
        executable=Path.Combine(Path.GetFullPath(engineDirectory),"filee-cli.exe");
        this.dataDirectory=Path.GetFullPath(dataDirectory);
    }
    public bool IsInstalled=>File.Exists(executable);
    public string EngineDirectory=>Path.GetDirectoryName(executable)!;
    private static bool IsDocument(ConversionFormat f)=>DocumentCategories.Contains(f.Category)||ImageIds.Contains(f.Id)||DataIds.Contains(f.Id);
    public async Task<IReadOnlyList<ConversionFormat>> FormatsAsync(CancellationToken token=default)
    {
        if(formats is not null)return formats;
        var run=await RunAsync(["formats","--json"],null,token);
        using var json=JsonDocument.Parse(run.Json);
        formats=json.RootElement.GetProperty("formats").EnumerateArray().Select(e=>new ConversionFormat(
            e.GetProperty("id").GetString()!,e.GetProperty("name").GetString()!,e.GetProperty("category").GetString()!,
            Strings(e,"extensions"),e.GetProperty("writable").GetBoolean())).Where(IsDocument).ToList();
        return formats;
    }
    public async Task<IReadOnlyList<ConversionTarget>> TargetsAsync(IEnumerable<string> sources,CancellationToken token=default)
    {
        var all=await FormatsAsync(token);var sourceFormats=sources.Select(p=>FindFormat(all,p)).Where(f=>f is not null).Cast<ConversionFormat>().DistinctBy(f=>f.Id).ToList();
        if(sourceFormats.Count==0)return [];
        HashSet<string>? common=null;var needs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var from in sourceFormats)
        {
            if(!routes.TryGetValue(from.Id,out var route))
            {
                var run=await RunAsync(["formats",from.Id,"--json"],null,token);using var json=JsonDocument.Parse(run.Json);
                route=(new(Strings(json.RootElement,"targets"),StringComparer.OrdinalIgnoreCase),new(Strings(json.RootElement,"withEngines"),StringComparer.OrdinalIgnoreCase));
                if(from.Writable)route.Item1.Add(from.Id);routes[from.Id]=route;
            }
            var possible=new HashSet<string>(route.Now,StringComparer.OrdinalIgnoreCase);possible.UnionWith(route.Later);
            if(common is null)common=possible;else common.IntersectWith(possible);needs.UnionWith(route.Later);
        }
        return all.Where(f=>f.Writable&&common!.Contains(f.Id)).Select(f=>new ConversionTarget(f.Id,f.Name,needs.Contains(f.Id)))
            .OrderBy(f=>f.Id=="pdf"?0:f.Id=="hwpx"?1:f.Id=="docx"?2:f.Id=="xlsx"?3:f.Id=="txt"?4:5).ThenBy(f=>f.Name).ToList();
    }
    public static ConversionFormat? FindFormat(IEnumerable<ConversionFormat> all,string path)
    {
        var ext=Path.GetExtension(path).TrimStart('.');return all.FirstOrDefault(f=>f.Extensions.Contains(ext,StringComparer.OrdinalIgnoreCase));
    }
    public void RefreshRoutes()=>routes.Clear();
    public async Task<ConversionBatchResult> ConvertAsync(IReadOnlyList<string> sources,string target,string outputDirectory,string? pages=null,
        IProgress<ConversionProgress>? progress=null,CancellationToken token=default)
    {
        var all=await FormatsAsync(token);
        if(!all.Any(f=>f.Writable&&f.Id==target))throw new ArgumentException("선택한 출력 형식을 지원하지 않습니다.");
        var inputs=sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(inputs.Count==0)throw new ArgumentException("변환할 파일을 추가하세요.");
        foreach(var path in inputs)
        {
            if(!File.Exists(path))throw new FileNotFoundException("파일이 없거나 연결된 드라이브에 접근할 수 없습니다.",path);
            if(FindFormat(all,path) is null)throw new ArgumentException("지원하지 않는 문서 형식입니다: "+Path.GetFileName(path));
        }
        token.ThrowIfCancellationRequested();
        var parent=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(parent);
        var jobRoot=Path.Combine(parent,"모두찾아줘_변환_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(jobRoot);
        var results=new List<ConversionFileResult>();var cancelled=false;
        foreach(var source in inputs)
        {
            if(token.IsCancellationRequested){cancelled=true;break;}
            var stage=Path.Combine(jobRoot,".work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            progress?.Report(new(source,"running",0));
            try
            {
                var args=new List<string>{"convert",source,"--to",target,"--output",stage,"--json"};
                if(!string.IsNullOrWhiteSpace(pages)){args.Add("--pages");args.Add(pages.Trim());}
                var run=await RunAsync(args,progress,token);
                using var json=JsonDocument.Parse(run.Json);var file=json.RootElement.GetProperty("files").EnumerateArray().FirstOrDefault();
                if(file.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("변환 결과가 비어 있습니다.");
                var state=file.GetProperty("state").GetString()??"failed";var outputs=Strings(file,"outputs");
                var needed=Strings(file,"needsEngines");var message=String(file,"error")??String(file,"notice")??"";
                var fonts=Strings(file,"missingFonts");if(fonts.Length>0)message+=(message.Length>0?" · ":"")+"누락 글꼴: "+string.Join(", ",fonts);
                if(state=="done")
                {
                    if(outputs.Length==0||outputs.Any(p=>!TextRules.IsWithin(Path.GetFullPath(p),stage)||(!File.Exists(p)&&!Directory.Exists(p))))
                        throw new InvalidDataException("변환 결과 파일을 확인하지 못했습니다.");
                    var final=UniqueFolder(jobRoot,Path.GetFileNameWithoutExtension(source));
                    // Move the entire per-file result together, preserving image links and sidecar folders.
                    var relative=outputs.Select(p=>Path.GetRelativePath(stage,p)).ToArray();Directory.Move(stage,final);
                    results.Add(new(source,"done",relative.Select(p=>Path.Combine(final,p)).ToArray(),message,[]));
                }
                else results.Add(new(source,state,[],needed.Length>0?"추가 엔진 필요: "+string.Join(", ",needed):message,needed));
                progress?.Report(new(source,state,100,results[^1].Message));
            }
            catch(OperationCanceledException){results.Add(new(source,"cancelled",[],"변환을 중지했습니다.",[]));cancelled=true;break;}
            catch(Exception ex){results.Add(new(source,"failed",[],ex.Message,[]));progress?.Report(new(source,"failed",0,ex.Message));}
            finally
            {
                // Only a GUID staging folder made by this invocation is removable; never a source or final result.
                if(TextRules.IsWithin(stage,jobRoot)&&Path.GetFileName(stage).StartsWith(".work-",StringComparison.Ordinal)&&Directory.Exists(stage))
                    try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        foreach(var source in inputs.Skip(results.Count))results.Add(new(source,"cancelled",[],"변환을 중지했습니다.",[]));
        if(!Directory.EnumerateFileSystemEntries(jobRoot).Any())Directory.Delete(jobRoot);
        return new(jobRoot,results,cancelled);
    }
    private static string UniqueFolder(string parent,string name)
    {
        if(string.IsNullOrWhiteSpace(name))name="문서";
        for(var i=0;;i++){var path=Path.Combine(parent,name+(i==0?"":" ("+i+")"));if(!Directory.Exists(path)&&!File.Exists(path))return path;}
    }
    public async Task<IReadOnlyList<ConversionEngine>> EnginesAsync(CancellationToken token=default)
    {
        var run=await RunAsync(["engines","--json"],null,token);using var json=JsonDocument.Parse(run.Json);
        return json.RootElement.GetProperty("engines").EnumerateArray().Where(e=>OptionalEngines.ContainsKey(e.GetProperty("id").GetString()!))
            .Select(e=>new ConversionEngine(String(e,"id")!,OptionalEngines[String(e,"id")!],String(e,"status")!,String(e,"version")??"",
                e.TryGetProperty("downloadSize",out var size)?size.GetInt64():0,e.TryGetProperty("downloadable",out var d)&&d.GetBoolean())).ToList();
    }
    public async Task InstallEngineAsync(string engine,IProgress<ConversionProgress>? progress=null,CancellationToken token=default)
    {
        if(!OptionalEngines.ContainsKey(engine))throw new ArgumentException("이 문서 변환 화면에서 지원하지 않는 추가 엔진입니다.");
        var run=await RunAsync(["engines","install",engine,"--json"],progress,token,TimeSpan.FromMinutes(25));using var json=JsonDocument.Parse(run.Json);
        if(String(json.RootElement,"state") is not ("installed" or "skipped"))throw new IOException(String(json.RootElement,"error")??"추가 엔진을 설치하지 못했습니다.");
        RefreshRoutes();
    }
    private async Task<(string Json,int ExitCode)> RunAsync(IReadOnlyList<string> args,IProgress<ConversionProgress>? progress,CancellationToken token,TimeSpan? limit=null)
    {
        if(!IsInstalled)throw new FileNotFoundException("변환 엔진이 없습니다. 최신 설치 프로그램으로 모두찾아줘를 다시 설치해 주세요.",executable);
        Directory.CreateDirectory(dataDirectory);
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false),WorkingDirectory=EngineDirectory};
        info.Environment["FILEE_DATA_DIR"]=dataDirectory;foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException("변환 엔진을 시작하지 못했습니다.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(limit??TimeSpan.FromMinutes(10));
        using var registration=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        var stdout=ReadBounded(process.StandardOutput,8*1024*1024,timeout.Token);
        var errors=new StringBuilder();
        async Task ReadEvents()
        {
            string? line;while((line=await process.StandardError.ReadLineAsync(timeout.Token)) is not null)
            {
                if(errors.Length<16000)errors.AppendLine(line[..Math.Min(line.Length,16000-errors.Length)]);
                try
                {
                    using var json=JsonDocument.Parse(line);var e=json.RootElement;
                    var source=String(e,"source")??"";var state=String(e,"state")??String(e,"event")??"running";
                    var fraction=e.TryGetProperty("fraction",out var f)&&f.TryGetDouble(out var n)?n:0;
                    progress?.Report(new(source,state,(int)Math.Clamp(fraction*100,0,100),String(e,"message")??String(e,"phase")??""));
                }
                catch(JsonException){/* Native diagnostics remain in the bounded error buffer. */}
            }
        }
        var stderr=ReadEvents();
        try
        {
            await process.WaitForExitAsync(timeout.Token);var output=(await stdout).TrimStart('\uFEFF');await stderr;
            if(string.IsNullOrWhiteSpace(output))throw new IOException("변환 엔진이 결과를 반환하지 않았습니다. "+errors.ToString());
            if(process.ExitCode is not 0 and not 1)throw new IOException("변환 요청을 처리하지 못했습니다. "+errors.ToString());
            return(output,process.ExitCode);
        }
        catch
        {
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            try{await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));}catch{}
            try{await Task.WhenAll(stdout,stderr);}catch{}
            if(timeout.IsCancellationRequested&&!token.IsCancellationRequested)throw new TimeoutException("변환 엔진의 처리 시간이 제한을 넘었습니다.");
            token.ThrowIfCancellationRequested();throw;
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader,int maximum,CancellationToken token)
    {
        var text=new StringBuilder();var buffer=new char[8192];int count;
        while((count=await reader.ReadAsync(buffer,token))>0){if(text.Length+count>maximum)throw new InvalidDataException("변환 엔진 응답이 너무 큽니다.");text.Append(buffer,0,count);}return text.ToString();
    }
    private static string[] Strings(JsonElement element,string name)=>element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Array?value.EnumerateArray().Select(e=>e.GetString()??"").ToArray():[];
    private static string? String(JsonElement element,string name)=>element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
}
