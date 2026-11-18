using System.Net;
using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

public enum ResearchDomain { Auto,Local,Law,School,Statistics,Support,Dart }
public sealed record Evidence(string Id,string Title,string Source,string Url,string Locator,string Text,DateTimeOffset RetrievedAt,bool Local=false);
public sealed record ResearchResult(string Question,ResearchDomain Domain,IReadOnlyList<Evidence> Evidence,IReadOnlyList<string> Notices,string Answer);
public sealed record ResearchConfiguration(string LawKey="",string DartKey="",string BizinfoKey="",string LocalModel="",bool UseLocalAi=false);

public static class ResearchText
{
    private static readonly Regex Tags=new("<[^>]*>",RegexOptions.Singleline,TimeSpan.FromSeconds(2));
    public static string Plain(string html)
    {
        html=Regex.Replace(html,@"<(script|style|noscript)\b[^>]*>.*?</\1>","",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(2));
        html=Regex.Replace(html,@"<(br\b[^>]*|/p|/div|/li|/tr|/h[1-6])>","\n",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        return Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(html," ")),@"[ \t\r\f]+"," ").Trim();
    }
    public static string[] Keywords(string question)
    {
        var stop=new HashSet<string>{"알려줘","알려주세요","찾아줘","찾아주세요","검색","검색해줘","어떻게","무엇","뭐야","있나요","해줘","관련","대한","최근","최신","기준","내용","정보","모두찾아줘","문서","파일"};
        return Regex.Matches(question,@"[가-힣A-Za-z0-9_]{2,}").Select(m=>m.Value).Select(t=>t.Length>=3?Regex.Replace(t,@"(에서는|에서|으로|관련된|인가요|은|는|이|가|을|를|의)$",""):t).Where(t=>t.Length>=2&&!stop.Contains(t)).Distinct().Take(12).ToArray();
    }
    public static int Score(string text,IEnumerable<string> words)=>words.Sum(t=>text.Contains(t,StringComparison.OrdinalIgnoreCase)?1:0);
    public static ResearchDomain Detect(string q)=>Regex.IsMatch(q,"지원사업|지원금|사업공고|창업|중소기업|소상공인|k-startup|기업지원",RegexOptions.IgnoreCase)?ResearchDomain.Support:
        Regex.IsMatch(q,"공시|재무|매출|영업이익|DART|주주",RegexOptions.IgnoreCase)?ResearchDomain.Dart:
        Regex.IsMatch(q,"학교|급식|학사일정|학생수|학교알리미")?ResearchDomain.School:
        Regex.IsMatch(q,"통계|인구|출산율|실업률|고용률|KOSIS|GDP",RegexOptions.IgnoreCase)?ResearchDomain.Statistics:
        Regex.IsMatch(q,"법령|법률|조례|판례|제\\d+조|수의계약|개인정보|정보통신공사|근로기준|신구조문")?ResearchDomain.Law:ResearchDomain.Local;
    public static Evidence Create(string title,string source,string url,string locator,string text,bool local=false)
    {
        var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url+locator+text)))[..16];
        return new(id,title,source,url,locator,text,DateTimeOffset.UtcNow,local);
    }
    public static string Excerpt(string text,string question,int maximum=1400)
    {
        if(text.Length<=maximum)return text;
        var words=Keywords(question);var lines=Regex.Split(text,@"\n\s*\n|(?=제\d+조)").Where(s=>s.Trim().Length>15).ToArray();
        var best=lines.OrderByDescending(s=>Score(s,words)).FirstOrDefault()??text;
        var found=text.IndexOf(best,StringComparison.Ordinal);var start=Math.Max(0,found-80);
        return text.Substring(start,Math.Min(maximum,text.Length-start));
    }
    public static string Compose(string question,IReadOnlyList<Evidence> evidence,IReadOnlyList<string> notices)
    {
        var b=new System.Text.StringBuilder();b.AppendLine("# 원문 근거 답변").AppendLine().AppendLine("질문: "+question).AppendLine();
        b.AppendLine("조회일: "+DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm")+" (한국 시간)").AppendLine();
        if(evidence.Count==0)b.AppendLine("확인할 수 있는 원문 근거를 찾지 못했습니다. 검색 조건이나 연결 상태를 확인하세요.");
        else
        {
            b.AppendLine("아래는 질문과 관련된 조회 원문 발췌입니다. 자료의 연도·시행일·신청기간을 함께 확인하세요.").AppendLine();
            for(var i=0;i<evidence.Count;i++)
            {
                var e=evidence[i];b.AppendLine($"## [{i+1}] {e.Title}").AppendLine($"출처: {e.Source} · {e.Locator}").AppendLine();
                foreach(var line in Excerpt(e.Text,question).Split('\n'))b.AppendLine("> "+line).AppendLine();
                b.AppendLine().AppendLine($"[원문 열기]({e.Url})").AppendLine();
            }
        }
        if(notices.Count>0){b.AppendLine("## 조회 안내");foreach(var note in notices)b.AppendLine("- "+note);}
        return b.ToString();
    }
}
