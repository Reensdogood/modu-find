using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFinder.Core;

internal static class UpdateChecks
{
    private const string Repo=GitHubReleaseClient.DefaultRepository;
    private static readonly byte[] Payload=Encoding.UTF8.GetBytes("ModuFind test installer bytes; never executed.");
    private static readonly string Hash=Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private static string Json(string tag="v1.2.0",string? digest=null,bool prerelease=false,bool draft=false,string? url=null,long size=-1,string? name=null)=>JsonSerializer.Serialize(new{
        tag_name=tag,prerelease,draft,body="새 기능",assets=new[]{new{
            name=name??"ModuFind-Setup-1.2.0-win-x64.exe",size=size<0?Payload.Length:size,
            browser_download_url=url??$"https://github.com/{Repo}/releases/download/{tag}/ModuFind-Setup-1.2.0-win-x64.exe",
            digest=digest??"sha256:"+Hash}}
    });
    private static bool Fails(Action action){try{action();return false;}catch(Exception ex)when(ex is InvalidDataException or ArgumentException){return true;}}
    public static async Task Run(Action<bool,string> check)
    {
        var current=new Version(1,1,0,0);
        var update=GitHubReleaseClient.ParseRelease(Json(),Repo,current)!;
        check(update.Version==new Version(1,2,0)&&update.Sha256==Hash,"stable release selects exact x64 installer and SHA-256");
        check(GitHubReleaseClient.ParseRelease(Json(tag:"v1.1.0"),Repo,current) is null,"same normalized assembly version is not an update");
        check(GitHubReleaseClient.ParseRelease(Json(tag:"v1.0.0"),Repo,current) is null,"downgrade is never offered");
        check(GitHubReleaseClient.ParseRelease(Json(prerelease:true),Repo,current) is null&&GitHubReleaseClient.ParseRelease(Json(draft:true),Repo,current) is null,"drafts and prereleases are excluded");
        check(Fails(()=>GitHubReleaseClient.ParseRelease(Json(tag:"v1.2.0-beta"),Repo,current)),"invalid stable tag rejected");
        check(Fails(()=>GitHubReleaseClient.ParseRelease(Json(digest:"sha256:invalid"),Repo,current)),"missing or malformed digest rejected");
        check(Fails(()=>GitHubReleaseClient.ParseRelease(Json(url:"https://example.com/installer.exe"),Repo,current)),"external installer URL rejected");
        check(Fails(()=>GitHubReleaseClient.ParseRelease(Json(size:0),Repo,current))&&Fails(()=>GitHubReleaseClient.ParseRelease(Json(size:400L*1024*1024),Repo,current)),"empty and oversized assets rejected");
        check(Fails(()=>GitHubReleaseClient.ParseRelease(Json(name:"Other-Setup.exe"),Repo,current)),"unrelated release asset rejected");
        check(Fails(()=>new GitHubReleaseClient("owner/../../repo")),"invalid repository rejected");
        var requested=false;
        using(var http=new HttpClient(new Handler(request=>{
            requested=request.Method==HttpMethod.Get&&request.RequestUri==new Uri($"https://api.github.com/repos/{Repo}/releases/latest")
                &&request.Content is null&&request.Headers.Authorization is null&&request.Headers.UserAgent.ToString().Contains("ModuFind");
            return new(HttpStatusCode.OK){Content=new StringContent(Json())};
        })))
        using(var client=new GitHubReleaseClient(Repo,http))
            check((await client.CheckAsync(current))?.Version==update.Version&&requested,"GitHub check sends only an anonymous metadata GET");
        foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests})
        {
            using var http=new HttpClient(new Handler(_=>new(status)));using var client=new GitHubReleaseClient(Repo,http);
            var failed=false;try{await client.CheckAsync(current);}catch(InvalidOperationException){failed=true;}
            check(failed,"release HTTP "+(int)status+" yields an actionable error");
        }
        var temp=Path.Combine(Path.GetTempPath(),"ModuFind-update-tests-"+Guid.NewGuid().ToString("N"));
        using(var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)})))
        using(var client=new GitHubReleaseClient(Repo,http))
        {
            var path=await client.DownloadAsync(update,temp);
            check(File.ReadAllBytes(path).SequenceEqual(Payload)&&Path.GetFileName(path)==update.FileName,"download bytes verified before installer becomes available");
            var failed=false;try{await client.DownloadAsync(update with{Sha256=new string('0',64)},temp);}catch(InvalidDataException){failed=true;}
            check(failed&&Directory.GetFiles(temp,"*.download",SearchOption.AllDirectories).Length==0,"hash mismatch refuses installer and removes partial file");
            failed=false;try{await client.DownloadAsync(update,temp,token:new CancellationToken(true));}catch(OperationCanceledException){failed=true;}
            check(failed&&Directory.GetFiles(temp,"*.download",SearchOption.AllDirectories).Length==0,"cancelled download leaves current app unchanged");
            failed=false;try{await client.DownloadAsync(update with{Tag="../../other"},temp);}catch(InvalidDataException){failed=true;}
            check(failed,"download revalidates tag and rejects path traversal");
        }
        using(var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])})))
        using(var client=new GitHubReleaseClient(Repo,http))
        {
            var failed=false;try{await client.DownloadAsync(update,temp);}catch(InvalidDataException){failed=true;}
            check(failed,"truncated download is not accepted");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();return Task.FromResult(response(request));
        }
    }
}
