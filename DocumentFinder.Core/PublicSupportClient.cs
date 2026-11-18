using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DocumentFinder.Core;

public sealed class PublicSupportClient:IDisposable
{
    private readonly HttpClient http;
    private static readonly string[] Hosts=["law.go.kr","bizinfo.go.kr","k-startup.go.kr","mss.go.kr"];
    public PublicSupportClient(HttpMessageHandler? handler=null)
    {
        http=handler is null?new(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}):new(handler);
        http.Timeout=TimeSpan.FromSeconds(35);http.DefaultRequestHeaders.UserAgent.ParseAdd("ModuFind/1.3 (+https://github.com/Reensdogood/modu-find)");
    }
    public static bool Allowed(Uri uri)=>uri.Scheme=="https"&&uri.IsDefaultPort&&string.IsNullOrEmpty(uri.UserInfo)&&Hosts.Any(h=>uri.Host==h||uri.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    public async Task<string> GetOfficialAsync(string url,CancellationToken token)
    {
        var uri=new Uri(url);
        for(var i=0;i<5;i++)
        {
            if(!Allowed(uri))throw new IOException("허용된 공식 자료 주소가 아닙니다.");
            using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is >=300 and <400&&response.Headers.Location is { } location){uri=new(uri,location);continue;}
            if(!response.IsSuccessStatusCode)throw new IOException(uri.Host+" 조회 실패 (HTTP "+(int)response.StatusCode+")");
            return await McpResearchClient.ReadLimited(response.Content,6*1024*1024,token);
        }
        throw new IOException("공식 자료 주소의 이동 횟수가 너무 많습니다.");
    }
    public async Task<(IReadOnlyList<Evidence> Evidence,IReadOnlyList<string> Notices)> SearchAsync(string question,string apiKey="",CancellationToken token=default)
    {
        var entries=new List<SupportEntry>();var notices=new List<string>();
        foreach(var (name,url) in new[]{("K-Startup","https://www.k-startup.go.kr/web/contents/rss/bizpbanc-ongoing.do"),("중소벤처기업부","https://www.mss.go.kr/rss/smba/board/310.do")})
        {
            try{var xml=await GetOfficialAsync(url,token);entries.AddRange(ParseFeed(xml,name));}
            catch(Exception ex) when(ex is not OperationCanceledException){notices.Add(name+": "+ex.Message);}
        }
        try
        {
            // Public listing includes central ministries, local authorities and affiliated agencies.
            var term=ResearchText.Keywords(question).FirstOrDefault(t=>!Regex.IsMatch(t,"지원|사업|기업|창업|공고|중소|소상"))??"";
            var url="https://www.bizinfo.go.kr/sii/siia/selectSIIA200View.do?rows=100&condition=searchPblancNm&condition1=AND&schEndAt=N&keyword="+Uri.EscapeDataString(term);
            var html=await GetOfficialAsync(url,token);entries.AddRange(ParseBizinfo(html));
            if(apiKey.Length>0)
            {
                var raw=await GetOfficialAsync("https://www.bizinfo.go.kr/uss/rss/bizinfoApi.do?dataType=json&searchCnt=100&crtfcKey="+Uri.EscapeDataString(apiKey),token);
                using var json=JsonDocument.Parse(raw);CollectApiEntries(json.RootElement,entries);
            }
        }
        catch(Exception ex) when(ex is not OperationCanceledException){notices.Add("기업마당: "+ex.Message);}
        var words=ResearchText.Keywords(question).Where(t=>!Regex.IsMatch(t,@"^(지원사업|지원금|기업지원|중소기업|사업공고|소상공인)$")).ToArray();
        var region=Regex.Match(question,@"서울|부산|대구|인천|광주|대전|울산|세종|경기|강원|충북|충남|전북|전남|경북|경남|제주|[가-힣]+구\b").Value;
        var ranked=entries.GroupBy(e=>e.Url).Select(g=>g.First()).Where(e=>region.Length==0||(e.Title+" "+e.Description).Contains(region)).Select(e=>(Entry:e,Score:ResearchText.Score(e.Title+" "+e.Description,words)))
            .Where(x=>words.Length==0||x.Score>0).OrderByDescending(x=>x.Score).ThenByDescending(x=>Published(x.Entry.Published)).Take(6).ToArray();
        var evidence=new List<Evidence>();
        foreach(var (entry,_) in ranked)
        {
            token.ThrowIfCancellationRequested();var body=entry.Title+"\n"+entry.Description;
            try
            {
                var html=await GetOfficialAsync(entry.Url,token);var plain=ResearchText.Plain(html);
                var titleAt=plain.LastIndexOf(entry.Title,StringComparison.Ordinal);
                if(titleAt<0)
                {
                    var positions=plain.Select((ch,index)=>(ch,index)).Where(p=>!char.IsWhiteSpace(p.ch)).ToArray();
                    var compact=new string(positions.Select(p=>p.ch).ToArray());var title=new string(entry.Title.Where(ch=>!char.IsWhiteSpace(ch)).ToArray());
                    var match=compact.LastIndexOf(title,StringComparison.OrdinalIgnoreCase);if(match>=0)titleAt=positions[match].index;
                }
                if(titleAt>=0)plain=plain[titleAt..];
                else
                {
                    var section=new[]{"신청방법 및 대상","사업개요","지원대상","모집기간"}.Select(s=>plain.IndexOf(s,StringComparison.Ordinal)).Where(i=>i>=0).DefaultIfEmpty(-1).Min();
                    if(section>=0)plain=entry.Title+"\n"+plain[Math.Max(0,section-120)..];
                }
                if(plain.Length>80)body=plain[..Math.Min(plain.Length,18000)];
            }
            catch(Exception ex) when(ex is not OperationCanceledException){notices.Add(entry.Source+" 상세 본문 조회 실패. 공고 제목·게시일만 확보했습니다.");}
            evidence.Add(ResearchText.Create(entry.Title,entry.Source,entry.Url,"게시일 "+(entry.Published.Length>0?entry.Published:"미제공")+" · 신청기간·대상은 원문 확인",body));
        }
        notices.Add("공개 공고 피드·검색 목록에 수록된 범위에서 조회했습니다. 지자체·지역 기관 공고는 기업마당 통합 공고도 함께 확인합니다. 목록이 전체 지원사업을 보장하지 않으며, 마감·예산 소진·자격 조건은 상세 공고 기준입니다.");
        if(ranked.Length==0)notices.Add("관련 공고를 확보하지 못했습니다. 지역명·업종·지원 분야를 줄여 다시 검색하세요.");
        return(evidence,notices);
    }
    public sealed record SupportEntry(string Title,string Url,string Source,string Published,string Description);
    private static DateTimeOffset Published(string value)
    {
        if(DateTimeOffset.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AllowWhiteSpaces,out var parsed))return parsed;
        return DateTimeOffset.TryParseExact(value,"yyyyMMddHHmmss",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out parsed)?parsed:DateTimeOffset.MinValue;
    }
    public static IReadOnlyList<SupportEntry> ParseFeed(string xml,string source)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=6*1024*1024});
        var doc=XDocument.Load(reader);var entries=new List<SupportEntry>();
        foreach(var item in doc.Descendants().Where(e=>e.Name.LocalName=="item"))
        {
            string Get(string key)=>item.Elements().FirstOrDefault(e=>e.Name.LocalName==key)?.Value??"";
            var link=Get("link").Trim();if(link.StartsWith("http://"))link="https://"+link[7..];
            if(!Uri.TryCreate(link,UriKind.Absolute,out var uri)||!Allowed(uri))continue;
            entries.Add(new(WebUtility.HtmlDecode(Get("title")),uri.AbsoluteUri,source,Get("pubDate"),ResearchText.Plain(Get("description"))));
        }
        return entries;
    }
    public static IReadOnlyList<SupportEntry> ParseBizinfo(string html)
    {
        var entries=new List<SupportEntry>();
        foreach(Match m in Regex.Matches(html,"<a\\b[^>]*href\\s*=\\s*[\"'](?<url>[^\"']*pblancId=[^\"']+)[\"'][^>]*>(?<text>.*?)</a>",RegexOptions.Singleline|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)))
        {
            var title=ResearchText.Plain(m.Groups["text"].Value);if(title.Length<4)continue;
            var link=new Uri(new Uri("https://www.bizinfo.go.kr"),WebUtility.HtmlDecode(m.Groups["url"].Value));if(!Allowed(link))continue;
            var id=Regex.Match(link.Query,@"pblancId=(PBLN_[A-Za-z0-9_]+)").Groups[1].Value;
            if(id.Length>0)entries.Add(new(title,"https://www.bizinfo.go.kr/sii/siia/selectSIIA200Detail.do?pblancId="+id,"기업마당 · 중앙부처·지자체·유관기관","",""));
        }
        return entries;
    }
    private static void CollectApiEntries(JsonElement value,List<SupportEntry> entries)
    {
        if(value.ValueKind==JsonValueKind.Array){foreach(var v in value.EnumerateArray())CollectApiEntries(v,entries);return;}
        if(value.ValueKind!=JsonValueKind.Object)return;
        if(value.TryGetProperty("pblancNm",out var n)&&value.TryGetProperty("pblancUrl",out var u))
        {
            var url=u.ToString();if(url.StartsWith('/'))url="https://www.bizinfo.go.kr"+url;
            if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&Allowed(uri))entries.Add(new(n.ToString(),url,"기업마당 OpenAPI",value.TryGetProperty("creatPnttm",out var date)?date.ToString():"",value.TryGetProperty("bsnsSumryCn",out var text)?ResearchText.Plain(text.ToString()):""));
        }
        else foreach(var property in value.EnumerateObject())CollectApiEntries(property.Value,entries);
    }
    public void Dispose()=>http.Dispose();
}
