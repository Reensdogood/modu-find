using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DocumentFinder.Core;

public static class KordocParser
{
    public static string RuntimeDirectory=>Environment.GetEnvironmentVariable("MODUFIND_KORDOC_HOME")??Path.Combine(AppContext.BaseDirectory,"parsing","kordoc");
    public static bool Available=>File.Exists(Path.Combine(RuntimeDirectory,"node.exe"))&&File.Exists(Path.Combine(RuntimeDirectory,"bridge.mjs"));
    public static ExtractedDocument Parse(string path)
    {
        var info=new ProcessStartInfo(Path.Combine(RuntimeDirectory,"node.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=RuntimeDirectory};
        info.ArgumentList.Add(Path.Combine(RuntimeDirectory,"bridge.mjs"));info.ArgumentList.Add(Path.GetFullPath(path));info.Environment["KORDOC_OFFLINE"]="1";
        using var process=Process.Start(info)??throw new IOException("kordoc 파서를 실행하지 못했습니다.");
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(25000)){process.Kill(true);throw new TimeoutException("kordoc 파싱 시간이 25초를 넘었습니다.");}
        var text=output.GetAwaiter().GetResult();_=errors.GetAwaiter().GetResult();
        if(text.Length>8*1024*1024)throw new InvalidDataException("kordoc 응답이 너무 큽니다.");
        using var json=JsonDocument.Parse(text);var r=json.RootElement;
        if(!r.TryGetProperty("success",out var ok)||!ok.GetBoolean())throw new InvalidDataException(r.TryGetProperty("error",out var e)?e.ToString():"문서 파싱 실패");
        var content=r.TryGetProperty("markdown",out var md)?md.GetString()??"":"";
        if(string.IsNullOrWhiteSpace(content))throw new InvalidDataException("kordoc이 읽을 수 있는 본문을 찾지 못했습니다.");
        var sections=new List<DocumentSection>();
        if(r.TryGetProperty("blocks",out var blocks))
            foreach(var b in blocks.EnumerateArray())
            {
                var value=b.TryGetProperty("text",out var t)?t.GetString()??"":"";
                if(b.TryGetProperty("table",out var table))value+="\n"+TableText(table);
                if(string.IsNullOrWhiteSpace(value))continue;
                int? page=b.TryGetProperty("pageNumber",out var pn)&&pn.TryGetInt32(out var n)&&n>0?n:null;
                sections.Add(new(page,page is null?"문단 "+(sections.Count+1):page+"쪽",value));
            }
        var warnings=r.TryGetProperty("warnings",out var w)&&w.ValueKind==JsonValueKind.Array?string.Join(" · ",w.EnumerateArray().Select(x=>x.ToString())):"";
        return new(content[..Math.Min(content.Length,TextRules.MaxTextLength)],[],content.Length>=TextRules.MaxTextLength?"일부 내용":"완료","kordoc 4.24.83"+(warnings.Length>0?" · "+warnings:""),sections);
    }
    private static string TableText(JsonElement table)
    {
        var values=new List<string>();
        void Walk(JsonElement e){if(e.ValueKind==JsonValueKind.Object)foreach(var p in e.EnumerateObject()){if(p.Name=="text"&&p.Value.ValueKind==JsonValueKind.String)values.Add(p.Value.GetString()!);else Walk(p.Value);}else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())Walk(item);}
        Walk(table);return string.Join(" | ",values);
    }
}
