using System.Text;
using System.Text.Json;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal static class Program
{
    internal static string DataDirectory=>Environment.GetEnvironmentVariable("DOCUMENT_FINDER_DATA")
        ??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FunnetDocumentFinder");
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Contains("--extract-worker"))
        {
            // WinExe has redirected pipes but no console. Encoding setters call
            // SetConsoleCP and fail without a console; use explicit pipe streams.
            using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false));
            using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
            string? line;
            while((line=input.ReadLine()) is not null)
            {
                ExtractedDocument result;
                try{var path=JsonSerializer.Deserialize<string>(line)??throw new InvalidDataException();result=DocumentExtractor.Extract(path);}
                catch(Exception ex){result=new("",[],"읽기 실패",ex.Message.Length>400?ex.Message[..400]:ex.Message);}
                output.WriteLine(JsonSerializer.Serialize(result));
            }
            return;
        }
        if(args.Contains("--seed"))
        {
            Seed(args);return;
        }
        if(args.Contains("--index"))
        {
            RunIndex(args);return;
        }
        if(args.Contains("--check-update"))
        {
            CheckUpdate(args);return;
        }
        if(args.Contains("--convert")||args.Contains("--conversion-formats"))
        {
            RunConversion(args);return;
        }
        ApplicationConfiguration.Initialize();
        Application.ThreadException+=(_,e)=>MessageBox.Show("작업 중 오류가 발생했습니다. 다시 시도해 주세요.\n\n"+e.Exception.Message,"모두찾아줘",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        try
        {
            var mutexName="Local\\FunnetDocumentFinder-"+Environment.UserName;
            if(Environment.GetEnvironmentVariable("DOCUMENT_FINDER_DATA") is {Length:>0})
                mutexName+="-"+System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(DataDirectory))))[..12];
            using var mutex=new Mutex(true,mutexName,out var first);
            if(!first){MessageBox.Show("모두찾아줘가 이미 실행 중입니다. 작업 표시줄에서 창을 열어 주세요.","모두찾아줘");return;}
            using var main=new MainForm(new SearchStore(Path.Combine(DataDirectory,"documents.db")));
            Application.Run(main);
        }
        catch(Exception ex){MessageBox.Show("모두찾아줘를 시작하지 못했습니다.\n\n"+ex.Message,"모두찾아줘",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private static void RunConversion(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);
        string? Arg(string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
        var report=Arg("--report")??Path.Combine(DataDirectory,"conversion-result.json");
        try
        {
            var client=ConversionForm.CreateClient();object result;
            using var cancellation=new CancellationTokenSource();if(int.TryParse(Arg("--cancel-after-ms"),out var ms))cancellation.CancelAfter(ms);
            if(args.Contains("--conversion-formats"))
            {
                var all=client.FormatsAsync(cancellation.Token).GetAwaiter().GetResult();
                var source=Arg("--input");result=source is null?all:client.TargetsAsync([source],cancellation.Token).GetAwaiter().GetResult();
            }
            else
            {
                var inputs=new List<string>();for(var i=0;i<args.Length-1;i++)if(args[i]=="--input")inputs.Add(args[++i]);
                result=client.ConvertAsync(inputs,Arg("--to")??throw new ArgumentException("--to가 필요합니다."),Arg("--output")??throw new ArgumentException("--output이 필요합니다."),Arg("--pages"),token:cancellation.Token).GetAwaiter().GetResult();
            }
            File.WriteAllText(report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(OperationCanceledException){File.WriteAllText(report,JsonSerializer.Serialize(new{Cancelled=true}));}
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Error=ex.ToString()}));Environment.ExitCode=1;}
    }

    private static void CheckUpdate(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);
        string? Arg(string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
        var report=Arg("--report")??Path.Combine(DataDirectory,"update-result.json");
        try
        {
            using var client=new GitHubReleaseClient();
            var current=Version.TryParse(Arg("--current-version"),out var version)?version:AppBrand.Version;
            var release=client.CheckAsync(current).GetAwaiter().GetResult();string? downloaded=null;
            if(release is not null&&args.Contains("--download"))downloaded=client.DownloadAsync(release,Path.Combine(DataDirectory,"updates")).GetAwaiter().GetResult();
            File.WriteAllText(report,JsonSerializer.Serialize(new{Current=current.ToString(),Available=release,Downloaded=downloaded},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Error=ex.ToString()}));Environment.ExitCode=1;}
    }

    // Also useful for repeatable, unattended indexing without opening a window.
    private static void RunIndex(string[] args)
    {
        var report=Path.Combine(DataDirectory,"index-result.json");Directory.CreateDirectory(DataDirectory);
        try
        {
            string? Arg(string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
            report=Arg("--report")??report;
            var root=Arg("--index")??throw new ArgumentException("색인할 폴더가 필요합니다.");
            if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            var store=new SearchStore(Path.Combine(DataDirectory,"documents.db"));ScanProgress? last=null;
            var progress=new ImmediateProgress(p=>last=p);
            using var token=new CancellationTokenSource();
            if(int.TryParse(Arg("--cancel-after-ms"),out var milliseconds))token.CancelAfter(milliseconds);
            var cancelled=false;
            try{Indexer.Run(store,[root],new ScanOptions(),progress,token.Token).GetAwaiter().GetResult();}
            catch(OperationCanceledException){cancelled=true;}
            var hits=store.Search(new SearchOptions(Arg("--query")??""),out var total);
            File.WriteAllText(report,JsonSerializer.Serialize(new{Cancelled=cancelled,Last=last,Stats=store.Stats(),Total=total,Hits=hits},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Error=ex.ToString()}));Environment.ExitCode=1;}
    }
    private sealed class ImmediateProgress(Action<ScanProgress> callback):IProgress<ScanProgress>
    {
        public void Report(ScanProgress value)=>callback(value);
    }

    private static void Seed(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var pos=Array.IndexOf(args,"--seed");var path=args[pos+1];var store=new SearchStore(Path.Combine(DataDirectory,"documents.db"));
            using var json=JsonDocument.Parse(File.ReadAllText(path));var all=new List<FileRecord>();
            foreach(var item in json.RootElement.EnumerateArray())
            {
                var p=item.GetProperty("path").GetString()!;if(!File.Exists(p))continue;var f=new FileInfo(p);
                all.Add(new FileRecord(f.FullName,f.Length,f.LastWriteTimeUtc.Ticks,Path.GetPathRoot(f.FullName)!));
            }
            foreach(var batch in all.Chunk(250))store.RegisterFiles(batch);
            // The existing verified Gwanak search is user data from this session, not demo data.
            var inspect=Array.IndexOf(args,"--content");var processed=0;
            if(inspect>=0)
            {
                using var candidates=JsonDocument.Parse(File.ReadAllText(args[inspect+1]));
                foreach(var item in candidates.RootElement.EnumerateArray())
                {
                    var p=item.GetProperty("path").GetString()!;if(!File.Exists(p))continue;var f=new FileInfo(p);
                    try{store.Update(new(f.FullName,f.Length,f.LastWriteTimeUtc.Ticks,Path.GetPathRoot(p)!),DocumentExtractor.Extract(p));processed++;}
                    catch(Exception ex){File.AppendAllText(Path.Combine(DataDirectory,"seed-errors.txt"),p+": "+ex.Message+"\n");}
                }
            }
            store.SetSetting("roots",JsonSerializer.Serialize(all.Select(f=>f.Root).Distinct(StringComparer.OrdinalIgnoreCase)));
            store.SetSetting("lastScan","2026-11-06 · 기존 검색 결과 불러옴");
            File.WriteAllText(Path.Combine(DataDirectory,"seed-result.json"),JsonSerializer.Serialize(new{Registered=all.Count,Read=processed,Stats=store.Stats()}));
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"seed-failure.txt"),ex.ToString());Environment.ExitCode=1;}
    }
}
