using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class FolderWatchService:IDisposable
{
    private readonly SearchStore store;
    private readonly Func<bool> busy;
    private readonly Action<string> report;
    private readonly System.Windows.Forms.Timer timer=new(){Interval=2500};
    private readonly List<FileSystemWatcher> watchers=[];
    private readonly Dictionary<string,string> changed=new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate=new();
    private bool processing,disposed;
    private WorkerClient? activeWorker;
    private string? warning;
    public FolderWatchService(SearchStore store,Func<bool> busy,Action<string> report){this.store=store;this.busy=busy;this.report=report;timer.Tick+=async(_,_)=>await Flush();}
    public void Configure(IEnumerable<string> paths,bool enabled)
    {
        foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();lock(gate)changed.Clear();timer.Stop();
        if(!enabled)return;
        foreach(var root in TextRules.ReduceRoots(paths))
        {
            if(!Directory.Exists(root)||Path.TrimEndingDirectorySeparator(root)==Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!)){report("변경 자동 반영은 지정한 업무 폴더에 적용합니다. 전체 드라이브는 수동 색인을 사용하세요.");continue;}
            try
            {
                var watcher=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=32768};
                void Changed(string path)
                {
                    if(!DocumentExtractor.Extensions.Contains(Path.GetExtension(path))||Path.GetFileName(path).StartsWith("~$")||Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar).Any(p=>p.StartsWith('.')||p is "node_modules" or "AppData"))return;
                    lock(gate){if(changed.Count<2000)changed[path]=root;else warning="변경이 많습니다. 색인 시작으로 전체 위치를 다시 확인하세요.";}
                }
                watcher.Created+=(_,e)=>Changed(e.FullPath);watcher.Changed+=(_,e)=>Changed(e.FullPath);watcher.Renamed+=(_,e)=>Changed(e.FullPath);
                watcher.Error+=(_,_)=>{lock(gate)warning="폴더 감시에서 누락이 발생할 수 있습니다. 색인 시작으로 다시 확인하세요.";};watcher.EnableRaisingEvents=true;watchers.Add(watcher);
            }
            catch(IOException){report("일부 폴더는 실시간 감시를 시작하지 못했습니다. 수동 색인을 사용할 수 있습니다.");}
            catch(UnauthorizedAccessException){report("접근할 수 없는 폴더는 자동 반영하지 않습니다.");}
        }
        timer.Start();
    }
    private async Task Flush()
    {
        if(disposed||processing||busy())return;Dictionary<string,string> batch;
        lock(gate){batch=changed.Take(20).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);foreach(var path in batch.Keys)changed.Remove(path);if(warning is not null){report(warning);warning=null;}}
        if(batch.Count==0)return;processing=true;var count=0;
        try
        {
            using var worker=new WorkerClient();activeWorker=worker;
            foreach(var pair in batch)
            {
                if(disposed)break;
                try
                {
                    var info=new FileInfo(pair.Key);if(!info.Exists)continue;var record=new FileRecord(info.FullName,info.Length,info.LastWriteTimeUtc.Ticks,pair.Value);
                    if(!store.NeedsUpdate(record))continue;
                    var attributes=(uint)info.Attributes;if(store.GetSetting("online")!="true"&&((attributes&0x400000)!=0||(attributes&0x1000)!=0))continue;
                    var extracted=await worker.Read(pair.Key,CancellationToken.None);if(disposed)break;store.Update(record,extracted);count++;
                }
                catch(IOException){}catch(UnauthorizedAccessException){}
            }
            if(count>0&&!disposed)report($"변경 문서 {count}개를 자동 반영했습니다. 검색어를 다시 입력하면 갱신된 결과를 볼 수 있습니다.");
        }
        finally{activeWorker=null;processing=false;}
    }
    public void Dispose(){disposed=true;timer.Stop();timer.Dispose();activeWorker?.Dispose();foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();}
}
