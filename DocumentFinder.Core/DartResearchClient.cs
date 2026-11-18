using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DocumentFinder.Core;

public sealed class DartResearchClient:IDisposable
{
    private readonly string key,cache;
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(40)};
    public DartResearchClient(string key,string dataDirectory){this.key=key;cache=Path.Combine(dataDirectory,"public-data","dart-corporations.xml");}
    private async Task<JsonDocument> Api(string name,Dictionary<string,string> args,CancellationToken token)
    {
        args["crtfc_key"]=key;
        using var response=await http.GetAsync("https://opendart.fss.or.kr/api/"+name+".json?"+string.Join("&",args.Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value))),HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw new IOException("OpenDART 조회 실패 (HTTP "+(int)response.StatusCode+")");
        var result=JsonDocument.Parse(await McpResearchClient.ReadLimited(response.Content,5*1024*1024,token));
        if(result.RootElement.TryGetProperty("status",out var s)&&s.GetString()!="000"){var message=result.RootElement.TryGetProperty("message",out var m)?m.GetString():"응답 오류";result.Dispose();throw new IOException("OpenDART: "+message);}
        return result;
    }
    public async Task<IReadOnlyList<Evidence>> SearchAsync(string question,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(key))throw new IOException("OpenDART 개인 인증키가 필요합니다. ‘연결 설정’에 입력하면 회사명으로 공시·재무를 조회할 수 있습니다. 발급: https://opendart.fss.or.kr/");
        if(!File.Exists(cache)||DateTime.UtcNow-File.GetLastWriteTimeUtc(cache)>TimeSpan.FromDays(7))
        {
            using var response=await http.GetAsync("https://opendart.fss.or.kr/api/corpCode.xml?crtfc_key="+Uri.EscapeDataString(key),HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode)throw new IOException("OpenDART 회사 목록 조회 실패");
            await using var input=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();await input.CopyToAsync(memory,token);
            if(memory.Length>15*1024*1024)throw new IOException("OpenDART 회사 목록이 너무 큽니다.");
            memory.Position=0;using var zip=new ZipArchive(memory);var entry=zip.GetEntry("CORPCODE.xml")??throw new IOException("OpenDART 회사 목록 형식을 확인하지 못했습니다.");
            if(entry.Length>40*1024*1024)throw new IOException("OpenDART 압축 해제 크기를 넘었습니다.");Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            using var reader=new StreamReader(entry.Open());var xml=await reader.ReadToEndAsync(token);await File.WriteAllTextAsync(cache,xml,token);
        }
        using var xr=XmlReader.Create(cache,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=40*1024*1024});
        var companies=XDocument.Load(xr).Descendants("list").Select(e=>new{Name=e.Element("corp_name")?.Value??"",Code=e.Element("corp_code")?.Value??""}).Where(c=>c.Name.Length>=2&&question.Contains(c.Name,StringComparison.OrdinalIgnoreCase)).OrderByDescending(c=>c.Name.Length).ToArray();
        if(companies.Length==0)throw new IOException("질문에 정확한 회사명을 넣으세요. 예: ‘삼성전자 최근 공시’ 또는 ‘삼성전자 2025년 매출’");
        var company=companies[0];var items=new List<Evidence>();
        if(Regex.IsMatch(question,"매출|이익|재무|자산|부채"))
        {
            var year=ResearchService.Year(question)??DateTime.UtcNow.Year-1;
            using var data=await Api("fnlttSinglAcntAll",new(){["corp_code"]=company.Code,["bsns_year"]=year.ToString(),["reprt_code"]="11011",["fs_div"]="CFS"},token);
            var rows=data.RootElement.GetProperty("list").EnumerateArray().Where(e=>Regex.IsMatch(e.GetProperty("account_nm").ToString(),"매출|영업이익|당기순이익|자산총계|부채총계")).Take(15).ToArray();
            var text=string.Join("\n",rows.Select(e=>e.GetProperty("account_nm")+": "+e.GetProperty("thstrm_amount")+" "+(e.TryGetProperty("currency",out var currency)?currency.ToString():"")+" · "+e.GetProperty("thstrm_nm")));
            var receipt=rows.FirstOrDefault().TryGetProperty("rcept_no",out var r)?r.ToString():"";
            items.Add(ResearchText.Create(company.Name+" "+year+"년 연결재무","금융감독원 OpenDART",receipt.Length>0?"https://dart.fss.or.kr/dsaf001/main.do?rcpNo="+receipt:"https://opendart.fss.or.kr/",year+"년 사업보고서 · 연결(CFS)",text));
        }
        else
        {
            using var data=await Api("list",new(){["corp_code"]=company.Code,["bgn_de"]=DateTime.UtcNow.AddYears(-1).ToString("yyyyMMdd"),["end_de"]=DateTime.UtcNow.ToString("yyyyMMdd"),["page_count"]="8",["sort"]="date",["sort_mth"]="desc"},token);
            foreach(var row in data.RootElement.GetProperty("list").EnumerateArray())
            {
                var receipt=row.GetProperty("rcept_no").ToString();var title=row.GetProperty("report_nm").ToString();
                items.Add(ResearchText.Create(company.Name+" · "+title,"금융감독원 OpenDART","https://dart.fss.or.kr/dsaf001/main.do?rcpNo="+receipt,"접수번호 "+receipt,"공시명: "+title+"\n접수일: "+row.GetProperty("rcept_dt")+"\n제출인: "+row.GetProperty("flr_nm")+"\n비고: "+(row.TryGetProperty("rm",out var rm)?rm.ToString():"")));
            }
        }
        return items;
    }
    public void Dispose()=>http.Dispose();
}
