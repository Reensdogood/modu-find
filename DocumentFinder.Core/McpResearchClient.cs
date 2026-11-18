using System.Net.Http.Json;
using System.Text.Json;

namespace DocumentFinder.Core;

/// <summary>Read-only MCP calls to the selected upstream public-data connectors.</summary>
public sealed class McpResearchClient:IDisposable
{
    private readonly HttpClient http;
    public McpResearchClient(HttpMessageHandler? handler=null){http=handler is null?new():new(handler);http.Timeout=TimeSpan.FromSeconds(85);}
    public async Task<string> CallAsync(string service,string tool,object arguments,string key="",CancellationToken token=default)
    {
        if(service is not ("law" or "school" or "stats"))throw new ArgumentException("지원하지 않는 공공자료 연결입니다.");
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://mcp.gomdori.app/"+service);
        request.Headers.Accept.ParseAdd("application/json");request.Headers.Accept.ParseAdd("text/event-stream");request.Headers.Add("MCP-Protocol-Version","2025-03-26");
        if(key.Length>0)request.Headers.Add("apikey",key);
        request.Content=JsonContent.Create(new{jsonrpc="2.0",id=1,method="tools/call",@params=new{name=tool,arguments}},options:new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw new IOException(service+" 조회 실패 (HTTP "+(int)response.StatusCode+"). "+(response.StatusCode==System.Net.HttpStatusCode.TooManyRequests?"공용 조회 한도입니다. 잠시 뒤 재시도하거나 개인 인증키를 설정하세요.":"연결 상태를 확인하세요."));
        var raw=await ReadLimited(response.Content,4*1024*1024,token);JsonDocument? document=null;
        if(raw.TrimStart().StartsWith('{'))document=JsonDocument.Parse(raw);
        else foreach(var line in raw.Split('\n'))if(line.StartsWith("data:")){var candidate=JsonDocument.Parse(line[5..].Trim());if(candidate.RootElement.TryGetProperty("id",out _)){document?.Dispose();document=candidate;}else candidate.Dispose();}
        using(document)
        {
            if(document is null)throw new InvalidDataException("공공자료 응답을 읽지 못했습니다.");var root=document.RootElement;
            if(root.TryGetProperty("error",out var error))throw new IOException("공공자료 도구 오류: "+error.ToString()[..Math.Min(error.ToString().Length,500)]);
            var result=root.GetProperty("result");var text=string.Join("\n",result.GetProperty("content").EnumerateArray().Where(e=>e.TryGetProperty("text",out _)).Select(e=>e.GetProperty("text").GetString()));
            if(result.TryGetProperty("isError",out var failed)&&failed.ValueKind==JsonValueKind.True)throw new IOException(text[..Math.Min(text.Length,600)]);
            try
            {
                using var embedded=JsonDocument.Parse(text);
                if(embedded.RootElement.ValueKind==JsonValueKind.Object&&embedded.RootElement.TryGetProperty("success",out var success)&&success.ValueKind==JsonValueKind.False)
                    throw new IOException("조회 서비스가 자료를 확인하지 못했습니다: "+text[..Math.Min(text.Length,600)]);
            }
            catch(JsonException){}
            return text;
        }
    }
    internal static async Task<string> ReadLimited(HttpContent content,int maximum,CancellationToken token)
    {
        if(content.Headers.ContentLength>maximum)throw new InvalidDataException("조회 응답이 허용 크기를 넘었습니다.");
        await using var input=await content.ReadAsStreamAsync(token);using var data=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await input.ReadAsync(buffer,token))>0){if(data.Length+read>maximum)throw new InvalidDataException("조회 응답이 허용 크기를 넘었습니다.");data.Write(buffer,0,read);}
        return System.Text.Encoding.UTF8.GetString(data.ToArray());
    }
    public void Dispose()=>http.Dispose();
}
