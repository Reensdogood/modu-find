using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

public sealed record AppRelease(Version Version,string Tag,string Notes,Uri ReleaseUrl,Uri DownloadUrl,string FileName,long Size,string Sha256);

/// <summary>Reads public release metadata. Never uploads documents or sends account credentials.</summary>
public sealed class GitHubReleaseClient : IDisposable
{
    public const string DefaultRepository="Reensdogood/modu-find";
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string repository;
    public GitHubReleaseClient(string repository=DefaultRepository,HttpClient? client=null)
    {
        if(!Regex.IsMatch(repository,@"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$")||repository.Split('/')[1] is "." or "..")
            throw new ArgumentException("잘못된 GitHub 저장소입니다.",nameof(repository));
        this.repository=repository;ownsClient=client is null;
        this.client=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=TimeSpan.FromMinutes(10),MaxResponseContentBufferSize=2*1024*1024};
    }
    private static HttpRequestMessage Request(Uri url)
    {
        var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ModuFind","1.1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version","2022-11-28");return request;
    }
    public async Task<AppRelease?> CheckAsync(Version current,CancellationToken token=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request=Request(new Uri($"https://api.github.com/repos/{repository}/releases/latest"));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseContentRead,timeout.Token);
        if(response.StatusCode==HttpStatusCode.NotFound)throw new InvalidOperationException("공개된 정식 버전이 아직 없거나 저장소에 접근할 수 없습니다.");
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub 조회 한도에 도달했습니다. 잠시 후 다시 확인해 주세요.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token),repository,current);
    }
    public static AppRelease? ParseRelease(string json,string repository,Version current)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        var tag=root.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+$")||!Version.TryParse(tag.TrimStart('v'),out var version))
            throw new InvalidDataException("정식 버전 번호를 읽지 못했습니다.");
        if(version<=new Version(current.Major,current.Minor,Math.Max(0,current.Build)))return null;
        var name=$"ModuFind-Setup-{version}-win-x64.exe";
        var releaseUrl=new Uri($"https://github.com/{repository}/releases/tag/{tag}");
        var expected=new Uri($"https://github.com/{repository}/releases/download/{tag}/{name}");
        foreach(var asset in root.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            var size=asset.GetProperty("size").GetInt64();
            if(size<=0||size>300L*1024*1024)throw new InvalidDataException("설치 파일 크기가 유효하지 않습니다.");
            if(!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(),UriKind.Absolute,out var url)||url!=expected)
                throw new InvalidDataException("이 저장소의 정식 설치 파일 주소가 아닙니다.");
            var digest=asset.TryGetProperty("digest",out var hash)?hash.GetString():null;
            if(digest is null||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("설치 파일의 SHA-256 검증 정보가 없습니다. GitHub 릴리스를 확인해 주세요.");
            var notes=root.TryGetProperty("body",out var body)?body.GetString()??"":"";
            return new(version,tag,notes,releaseUrl,url,name,size,digest[7..].ToLowerInvariant());
        }
        throw new InvalidDataException("이 버전의 Windows 64비트 설치 파일을 찾지 못했습니다.");
    }
    public async Task<string> DownloadAsync(AppRelease release,string directory,IProgress<int>? progress=null,CancellationToken token=default)
    {
        // Revalidate immutable metadata before creating a path or launching anything.
        if(!Regex.IsMatch(release.Tag,@"^v?\d+\.\d+\.\d+$")||!Version.TryParse(release.Tag.TrimStart('v'),out var tagVersion)||tagVersion!=release.Version||
            release.FileName!=$"ModuFind-Setup-{release.Version}-win-x64.exe"||
            release.DownloadUrl!=new Uri($"https://github.com/{repository}/releases/download/{release.Tag}/{release.FileName}")||
            !Regex.IsMatch(release.Sha256,@"^[a-fA-F0-9]{64}$")||release.Size<=0||release.Size>300L*1024*1024)
            throw new InvalidDataException("설치 파일 정보가 유효하지 않습니다.");
        var folder=Path.Combine(Path.GetFullPath(directory),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var destination=Path.Combine(folder,release.FileName);var partial=destination+".download";
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,release.DownloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ModuFind","1.1"));
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long length && length!=release.Size)
                throw new InvalidDataException("설치 파일 크기가 릴리스 정보와 다릅니다.");
            await using var input=await response.Content.ReadAsStreamAsync(token);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long read=0;var buffer=new byte[81920];
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,buffer.Length,true))
            {
                int count;while((count=await input.ReadAsync(buffer,token))>0)
                {
                    read+=count;if(read>release.Size)throw new InvalidDataException("다운로드 크기가 허용 범위를 넘었습니다.");
                    hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),token);
                    progress?.Report((int)(read*100/release.Size));
                }
                await output.FlushAsync(token);
            }
            if(read!=release.Size||!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(release.Sha256)))
                throw new InvalidDataException("설치 파일의 SHA-256 검증에 실패했습니다. 다운로드를 다시 시도해 주세요.");
            File.Move(partial,destination);return destination;
        }
        catch{if(File.Exists(partial))File.Delete(partial);throw;}
    }
    public void Dispose(){if(ownsClient)client.Dispose();}
}
