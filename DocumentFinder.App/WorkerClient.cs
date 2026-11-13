using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class WorkerClient : IDisposable
{
    private Process? process;
    private void Start()
    {
        if(process is {HasExited:false})return;
        Dispose();
        var info=new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        info.ArgumentList.Add("--extract-worker");
        process=Process.Start(info)??throw new IOException("문서 읽기 작업을 시작하지 못했습니다.");
        process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();
    }
    public async Task<ExtractedDocument> Read(string path,CancellationToken token)
    {
        try
        {
            Start();
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(path));
            await process.StandardInput.FlushAsync(token);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
            string? line;
            try {line=await process.StandardOutput.ReadLineAsync(timeout.Token);}
            catch(OperationCanceledException)
            {
                Dispose();token.ThrowIfCancellationRequested();return new("",[],"시간 초과","30초 안에 읽지 못했습니다. 다시 색인하면 재시도합니다.");
            }
            return line is null?new("",[],"읽기 실패","문서 읽기 작업이 종료되었습니다.")
                :JsonSerializer.Deserialize<ExtractedDocument>(line)??new("",[],"읽기 실패","문서 응답이 비어 있습니다.");
        }
        catch(OperationCanceledException){Dispose();throw;}
        catch(Exception ex){Dispose();return new("",[],"읽기 실패",ex.Message);}
    }
    public void Dispose()
    {
        if(process is null)return;
        try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        process.Dispose();process=null;
    }
}

internal static class Indexer
{
    public static async Task Run(SearchStore store,IEnumerable<string> roots,ScanOptions options,IProgress<ScanProgress> progress,CancellationToken token)
    {
        var files=await Task.Run(()=>FileScanner.Discover(roots,p=>progress.Report(p),token),token);
        await Task.Run(()=>
        {
            foreach(var chunk in files.Chunk(200)){token.ThrowIfCancellationRequested();store.RegisterFiles(chunk);}
        },token);
        var processed=0;var updated=0;var skipped=0;var issues=0;
        // Independent reader processes keep corrupt files and remote providers off the UI process.
        var queue=new System.Collections.Concurrent.ConcurrentQueue<FileRecord>(files.OrderBy(f=>
            TextRules.Kind(Path.GetExtension(f.Path))=="엑셀"?0:1));
        async Task Consume()
        {
            using var worker=new WorkerClient();
            while(queue.TryDequeue(out var f))
            {
                token.ThrowIfCancellationRequested();var error=false;
                if(!store.NeedsUpdate(f))Interlocked.Increment(ref skipped);
                else
                {
                    ExtractedDocument doc;
                    try
                    {
                        var attrs=(uint)File.GetAttributes(f.Path);
                        if(!options.IncludeOnlineFiles && ((attrs&0x400000)!=0 || (attrs&0x1000)!=0))
                            doc=new("",[],"온라인 전용","클라우드 파일 내용 읽기를 켜고 다시 색인하세요.");
                        else if(f.Size>options.MaximumFileBytes)doc=new("",[],"용량 초과","80MB 초과 파일은 파일명만 색인합니다.");
                        else doc=await worker.Read(f.Path,token);
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex){doc=new("",[],"읽기 실패",ex.Message);}
                    store.Update(f,doc);Interlocked.Increment(ref updated);error=doc.Status is not "완료" and not "일부 내용";
                }
                if(error)Interlocked.Increment(ref issues);
                var count=Interlocked.Increment(ref processed);
                progress.Report(new ScanProgress("내용 색인",files.Count,count,Volatile.Read(ref updated),Volatile.Read(ref skipped),Volatile.Read(ref issues),f.Path));
            }
        }
        await Task.WhenAll(Consume(),Consume());
        store.SetSetting("lastScan",DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        progress.Report(new ScanProgress("색인 완료",files.Count,processed,updated,skipped,issues,"변경된 파일만 다음에 다시 읽습니다.",true));
    }
}
