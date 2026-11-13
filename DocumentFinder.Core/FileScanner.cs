namespace DocumentFinder.Core;

public static class FileScanner
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows","Program Files","Program Files (x86)","ProgramData","AppData","Application Data","Local Settings",
        "$RECYCLE.BIN","System Volume Information","Recovery","WindowsApps","WpSystem","node_modules",".git",
        ".codex",".cache",".venv","venv","__pycache__","Docker","DeliveryOptimization","WUDownloadCache","휴지통"
    };

    public static List<FileRecord> Discover(IEnumerable<string> roots, Action<ScanProgress>? progress, CancellationToken token)
    {
        var found=new List<FileRecord>();var issues=0;var last=DateTime.MinValue;
        foreach(var root in TextRules.ReduceRoots(roots))
        {
            var pending=new Stack<string>();pending.Push(root);
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();var folder=pending.Pop();
                try
                {
                    foreach(var path in Directory.EnumerateFileSystemEntries(folder))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attrs=File.GetAttributes(path);var name=Path.GetFileName(path);
                            if(attrs.HasFlag(FileAttributes.Directory))
                            {
                                // Cloud folders are reparse points too. Skip only actual links/junctions.
                                if(Excluded.Contains(name)||name.StartsWith('.'))continue;
                                var info=new DirectoryInfo(path);if(info.LinkTarget is not null)continue;
                                pending.Push(path);
                            }
                            else if(!name.StartsWith("~$") && DocumentExtractor.Extensions.Contains(Path.GetExtension(path)))
                            {
                                var info=new FileInfo(path);found.Add(new FileRecord(info.FullName,info.Length,info.LastWriteTimeUtc.Ticks,root));
                            }
                        }
                        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){issues++;}
                    }
                }
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){issues++;}
                if((DateTime.UtcNow-last).TotalMilliseconds>300)
                {
                    progress?.Invoke(new ScanProgress("파일 목록 확인",found.Count,0,0,0,issues,folder));last=DateTime.UtcNow;
                }
            }
        }
        progress?.Invoke(new ScanProgress("파일 목록 확인",found.Count,0,0,0,issues,"파일 목록 등록"));
        return found;
    }
}
