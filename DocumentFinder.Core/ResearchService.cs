using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

public sealed class ResearchService:IDisposable
{
    private readonly SearchStore store;
    private readonly ResearchConfiguration configuration;
    private readonly McpResearchClient mcp=new();
    private readonly PublicSupportClient publicSources=new();
    public ResearchService(SearchStore store,ResearchConfiguration configuration){this.store=store;this.configuration=configuration;}
    public async Task<ResearchResult> AskAsync(string question,ResearchDomain domain=ResearchDomain.Auto,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(question))throw new ArgumentException("질문을 입력하세요.");
        if(question.Length>2000)throw new ArgumentException("질문은 2,000자 이내로 입력하세요.");
        if(domain==ResearchDomain.Auto)domain=ResearchText.Detect(question);
        var evidence=new List<Evidence>();var notices=new List<string>();
        try
        {
            switch(domain)
            {
                case ResearchDomain.Local:evidence.AddRange(await Task.Run(()=>store.FindEvidence(question),token));break;
                case ResearchDomain.Law:await Law(question,evidence,notices,token);break;
                case ResearchDomain.School:await School(question,evidence,notices,token);break;
                case ResearchDomain.Statistics:await Statistics(question,evidence,notices,token);break;
                case ResearchDomain.Support:
                    var support=await publicSources.SearchAsync(question,configuration.BizinfoKey,token);evidence.AddRange(support.Evidence);notices.AddRange(support.Notices);break;
                case ResearchDomain.Dart:
                    using(var dart=new DartResearchClient(configuration.DartKey,Path.GetDirectoryName(store.DatabasePath)!))evidence.AddRange(await dart.SearchAsync(question,token));break;
            }
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){notices.Add(ex.Message);}
        for(var i=0;i<notices.Count;i++)foreach(var secret in new[]{configuration.LawKey,configuration.DartKey,configuration.BizinfoKey}.Where(s=>s.Length>0))notices[i]=notices[i].Replace(secret,"[인증키]");
        var ordered=evidence.GroupBy(e=>e.Id).Select(g=>g.First()).Take(8).ToArray();
        var answer=ResearchText.Compose(question,ordered,notices);
        return new(question,domain,ordered,notices,answer);
    }
    private async Task Law(string question,List<Evidence> evidence,List<string> notices,CancellationToken token)
    {
        var names=LawNames(question);var article=Regex.Match(question,@"제\s*\d+\s*조(?:의\s*\d+)?").Value.Replace(" ","");
        try
        {
            var parameters=new Dictionary<string,object>{{"query",question},{"task",Regex.IsMatch(question,"비교|개정|신구")?"amendment_track":"full_research"}};
            var dates=Regex.Matches(question,@"20\d{2}[-./]\d{2}[-./]\d{2}").Select(m=>Regex.Replace(m.Value,@"[-./]","")).ToArray();
            if(dates.Length>=2){parameters["scenario"]="time_travel";parameters["task"]="amendment_track";parameters["fromDate"]=dates[0];parameters["toDate"]=dates[1];}
            var text=await mcp.CallAsync("law","legal_research",parameters,configuration.LawKey,token);
            evidence.Add(ResearchText.Create("법령 조회 결과","국가법령정보센터 자료 · korean-law-mcp 중계",FirstOfficialUrl(text,"law.go.kr")??"https://www.law.go.kr/lsSc.do?query="+Uri.EscapeDataString(question),"시행일·조문은 조회 결과 참조",Readable(text)));
        }
        catch(Exception ex) when(ex is not OperationCanceledException){notices.Add(ex.Message+" 국가법령정보센터 공개 본문을 직접 확인합니다.");}
        // Current official text is independently fetched; community summaries never replace it.
        foreach(var name in names.Take(2))
        {
            try
            {
                var url="https://www.law.go.kr/법령/"+Uri.EscapeDataString(name);
                var html=await publicSources.GetOfficialAsync(url,token);var text=ResearchText.Plain(html);
                if(!text.Contains("제1조"))
                {
                    var sequence=Regex.Match(html,"id=[\"']lsiSeq[\"'][^>]*value=[\"'](\\d+)").Groups[1].Value;
                    if(sequence.Length>0)
                    {
                        var effective=Regex.Match(html,"var\\s+efYd\\s*=\\s*[\"'](\\d{8})").Groups[1].Value;
                        url="https://www.law.go.kr/LSW/lsInfoR.do?lsiSeq="+sequence+"&efYd="+effective+"&chrClsCd=010202";
                        html=await publicSources.GetOfficialAsync(url,token);text=ResearchText.Plain(html);
                    }
                }
                var firstArticle=Regex.Match(text,@"제1조\s*[（(]");var start=firstArticle.Success?firstArticle.Index:-1;
                if(start<0)throw new IOException(name+": 공식 조문 본문을 확인하지 못했습니다.");
                var header=Regex.Match(text,@"\[시행[^\]]+\]").Value;
                var body=text[start..];var matchedArticle=false;
                if(article.Length>0)
                {
                    var found=Regex.Match(body,@"(?m)^\s*"+Regex.Escape(article)+@"(?=\s*[（(])");
                    if(found.Success)
                    {
                        body=body[found.Index..].TrimStart();matchedArticle=true;
                        var next=Regex.Matches(body,@"(?m)^\s*제\d+조(?:의\d+)?\s*[（(]").Skip(1).FirstOrDefault();
                        if(next is not null)body=body[..next.Index];
                    }
                    else notices.Add(name+"에서 요청한 "+article+"의 본문 위치를 확인하지 못해 현행 본문을 제시합니다.");
                }
                if(Regex.IsMatch(question,"비교|신구|개정")&&notices.Any(n=>n.Contains("공용 조회 한도")))notices.Add("시점 비교 원문을 확보하지 못했습니다. 직접 확인한 자료는 위 시행일의 현행 본문이며 신구 비교 결과가 아닙니다.");
                evidence.Add(ResearchText.Create(name,"국가법령정보센터",url,header+(matchedArticle?" · "+article:" · 현행 본문"),body[..Math.Min(body.Length,180000)]));
            }
            catch(Exception ex) when(ex is not OperationCanceledException){notices.Add(ex.Message);}
        }
        if(evidence.Count==0)notices.Add("현재 법령 원문을 확보하지 못해 법적 결론을 작성하지 않았습니다. 국가법령정보센터에서 법령명으로 확인하세요: https://www.law.go.kr");
    }
    public static string[] LawNames(string q)
    {
        var names=new List<string>();
        foreach(var (term,name) in new[]{("개인정보","개인정보보호법"),("수의계약","지방자치단체를당사자로하는계약에관한법률시행령"),("정보통신공사","정보통신공사업법"),("근로","근로기준법"),("저작권","저작권법"),("창업","중소기업창업지원법"),("중소기업","중소기업기본법"),("학교","초중등교육법")})if(q.Contains(term))names.Add(name);
        foreach(Match m in Regex.Matches(q,@"[가-힣]+(?:법률|법)(?:시행령|시행규칙)?"))if(m.Value.Length>2)names.Add(m.Value);
        return names.Distinct().ToArray();
    }
    private async Task School(string question,List<Evidence> evidence,List<string> notices,CancellationToken token)
    {
        var name=Regex.Match(question,@"[가-힣A-Za-z0-9]+(?:초등학교|중학교|고등학교|특수학교)").Value;
        if(name.Length==0){notices.Add("학교명을 정확히 입력하세요. 예: ‘대전 한밭초등학교 학생수 알려줘’");return;}
        var found=await mcp.CallAsync("school","find_school",new{name},token:token);
        var candidates=Regex.Matches(found,@"-\s*(?<name>[^\n—]+)\s*—\s*(?<sido>\S+)\s+(?<sgg>[^·\n]+)·\s*(?<kind>[^·\n]+)").Select(m=>new{name=m.Groups["name"].Value.Trim(),sido=m.Groups["sido"].Value.Trim(),sgg=m.Groups["sgg"].Value.Trim(),kind=m.Groups["kind"].Value.Trim()}).ToArray();
        var region=Regex.Match(question,@"서울|부산|대구|인천|광주|대전|울산|세종|경기|강원|충청북도|충청남도|전라남도|전북|경상북도|경상남도|제주").Value;
        if(region.Length>0)candidates=candidates.Where(c=>c.sido.Contains(region)||c.sgg.Contains(region)).ToArray();
        if(candidates.Length!=1){evidence.Add(ResearchText.Create(name+" 학교 검색","학교알리미·NEIS · schoolinfo-mcp 중계","https://www.schoolinfo.go.kr/", "동명이교 확인 필요",found));notices.Add("학교가 하나로 특정되지 않았습니다. 시도·시군구를 함께 입력하세요.");return;}
        var school=candidates[0];var year=Year(question);var requestedYear=year??DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Year;string text;
        if(Regex.IsMatch(question,"평가계획|수행평가"))text=await mcp.CallAsync("school","get_evaluation_plan",new{school.sido,school.sgg,school.kind,school.name,year=requestedYear,full=false,all=false},token:token);
        else if(Regex.IsMatch(question,"교육과정|편제"))text=await mcp.CallAsync("school","get_curriculum_plan",new{school.sido,school.sgg,school.kind,school.name,year=requestedYear,full=false,all=false},token:token);
        else if(question.Contains("급식"))text=await mcp.CallAsync("school","get_school_meal",new{school.sido,school.sgg,school.name,date=Date(question),days=1},token:token);
        else if(Regex.IsMatch(question,"일정|방학|시험"))text=await mcp.CallAsync("school","get_school_schedule",new{school.sido,school.sgg,school.name},token:token);
        else if(Regex.IsMatch(question,"학생|학급|교원|급식비|예산"))text=await mcp.CallAsync("school","get_disclosure",new{school.sido,school.sgg,school.kind,school.name,item=Regex.IsMatch(question,"학생|학급")?"09":question,year=year??DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Year},token:token);
        else text=await mcp.CallAsync("school","get_parent_digest",new{school.sido,school.sgg,school.kind,school.name,year=year??DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Year},token:token);
        var url=FirstOfficialUrl(text,"schoolinfo.go.kr")??"https://www.schoolinfo.go.kr/ei/ss/pneiss_a03_s0.do";
        var card=await mcp.CallAsync("school","search_school",new{school.sido,school.sgg,school.kind,school.name},token:token);
        var code=Regex.Match(card,@"학교코드\s*\|\s*([A-Za-z0-9]+)").Groups[1].Value;
        evidence.Add(ResearchText.Create(school.name+" 공시정보","학교알리미·NEIS · schoolinfo-mcp 중계",url,school.sido+" "+school.sgg+" · 요청 공시연도 "+requestedYear+(code.Length>0?" · 학교코드 "+code:""),Readable(text)+"\n\n"+card));
        if(!text.Contains("https://"))notices.Add("조회 응답에 개별 공시 화면의 직접 링크가 없어 학교별 공시 검색 화면을 연결했습니다. 학교명·학교코드·공시연도로 원문을 확인할 수 있습니다.");
    }
    private async Task Statistics(string question,List<Evidence> evidence,List<string> notices,CancellationToken token)
    {
        var region=Regex.Match(question,@"[가-힣]+구\b|[가-힣]+시\b|서울|부산|대구|인천|광주|대전|울산|세종|경기|강원|충북|충남|전북|전남|경북|경남|제주").Value;
        string text;
        if(Regex.IsMatch(question,"추세|변화|증감|증가|감소"))text=await mcp.CallAsync("stats","quick_trend",new{keyword=question,region=region.Length>0?region:null},token:token);
        else text=await mcp.CallAsync("stats","quick_stats",new{query=question,region=region.Length>0?region:null,year=Year(question)},token:token);
        var table=Regex.Match(text,@"DT_[A-Za-z0-9_]+").Value;var org="101";
        try{using var json=JsonDocument.Parse(text);if(json.RootElement.TryGetProperty("source",out var s)&&s.TryGetProperty("orgId",out var o))org=o.ToString();}catch(JsonException){}
        var url=FirstOfficialUrl(text,"kosis.kr")??(table.Length>0?"https://kosis.kr/statHtml/statHtml.do?orgId="+Uri.EscapeDataString(org)+"&tblId="+Uri.EscapeDataString(table):"https://kosis.kr/search/search.do?query="+Uri.EscapeDataString(question));
        evidence.Add(ResearchText.Create("국가통계 조회","KOSIS · korean-stats-mcp 중계",url,table.Length>0?"통계표 "+table:"응답의 통계표·기간 참조",Readable(text)));
    }
    public static int? Year(string text)=>Regex.Match(text,@"\b(20\d{2})\s*년?") is {Success:true} m?int.Parse(m.Groups[1].Value):null;
    private static string Date(string question)
    {
        var m=Regex.Match(question,@"(20\d{2})[-./](\d{1,2})[-./](\d{1,2})");
        return m.Success&&DateOnly.TryParse($"{m.Groups[1]}-{m.Groups[2]}-{m.Groups[3]}",out var date)?date.ToString("yyyyMMdd"):DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd");
    }
    private static string Readable(string text)
    {
        try
        {
            using var doc=JsonDocument.Parse(text);
            if(doc.RootElement.TryGetProperty("answer",out var answer)&&answer.ValueKind==JsonValueKind.String)
                return answer.GetString()+"\n\n"+(doc.RootElement.TryGetProperty("source",out var source)?"자료 정보: "+source.ToString():"");
            return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        }
        catch(JsonException){return text;}
    }
    private static string? FirstOfficialUrl(string text,string host)=>Regex.Matches(text,@"https?://[^\s<>\""\]\)]+").Select(m=>WebUtility.HtmlDecode(m.Value)).FirstOrDefault(u=>Uri.TryCreate(u,UriKind.Absolute,out var uri)&&(uri.Host==host||uri.Host.EndsWith("."+host)));
    public void Dispose(){mcp.Dispose();publicSources.Dispose();}
}
