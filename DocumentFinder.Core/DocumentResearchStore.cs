using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

public sealed partial class SearchStore
{
    private void SaveChunks(Microsoft.Data.Sqlite.SqliteConnection c,Microsoft.Data.Sqlite.SqliteTransaction tx,string path,ExtractedDocument document)
    {
        using var command=c.CreateCommand();command.Transaction=tx;
        command.CommandText="DELETE FROM document_chunks WHERE path=$p";command.Parameters.AddWithValue("$p",path);command.ExecuteNonQuery();
        var chunks=BuildSections(document.Content,document.Sections);if(chunks.Count==0)chunks=[new(null,"본문 없음","")];
        command.CommandText="INSERT INTO document_chunks(path,ordinal,page,anchor,text) VALUES($p,$n,$page,$a,$t)";
        for(var i=0;i<chunks.Count;i++)
        {
            command.Parameters.Clear();command.Parameters.AddWithValue("$p",path);command.Parameters.AddWithValue("$n",i);
            command.Parameters.AddWithValue("$page",(object?)chunks[i].Page??DBNull.Value);command.Parameters.AddWithValue("$a",chunks[i].Anchor);command.Parameters.AddWithValue("$t",chunks[i].Text);command.ExecuteNonQuery();
        }
    }
    public static List<DocumentSection> BuildSections(string text,IReadOnlyList<DocumentSection>? native=null)
    {
        var output=new List<DocumentSection>();var source=native is {Count:>0}?native:[new DocumentSection(null,"",text)];
        foreach(var section in source)
        {
            int? page=section.Page;var anchor=section.Anchor;var current=new System.Text.StringBuilder();
            void Flush(){if(current.Length==0)return;output.Add(new(page,anchor.Length>0?anchor:"문단 "+(output.Count+1),current.ToString().Trim()));current.Clear();}
            foreach(var line in section.Text.Replace("\r","").Split('\n'))
            {
                var marker=Regex.Match(line,@"^\[(\d+)쪽\]$");
                if(marker.Success){Flush();page=int.Parse(marker.Groups[1].Value);anchor=page+"쪽";continue;}
                if(Regex.IsMatch(line,@"^#{1,6}\s|^\[시트:|^제\d+조")){Flush();anchor=line.Trim();}
                if(current.Length+line.Length>1800)Flush();
                if(line.Length>2400){foreach(var part in line.Chunk(1800))output.Add(new(page,anchor.Length>0?anchor:"문단 "+(output.Count+1),new string(part)));}
                else current.AppendLine(line);
            }
            Flush();if(output.Count>=500)break;
        }
        return output.Take(500).ToList();
    }
    public List<Evidence> FindEvidence(string question,int maximum=8)
    {
        var words=ResearchText.Keywords(question);var candidates=new Dictionary<string,SearchHit>(StringComparer.OrdinalIgnoreCase);
        foreach(var word in words)
            foreach(var hit in Search(new(word,Limit:80),out _))candidates[hit.Path]=hit;
        var ordered=candidates.Values.OrderByDescending(h=>ResearchText.Score(h.Name+" "+h.Content+" "+h.Sheets,words)).Take(30);
        var evidence=new List<(Evidence Value,int Score)>();
        foreach(var hit in ordered)
        {
            var chunks=GetSections(hit.Path);if(chunks.Count==0)chunks=BuildSections(hit.Content);
            foreach(var chunk in chunks.Where(c=>!string.IsNullOrWhiteSpace(c.Text)))
            {
                var score=ResearchText.Score(chunk.Text,words)*3+ResearchText.Score(hit.Name,words);
                if(score>0)evidence.Add((ResearchText.Create(hit.Name,"내 PC 문서",new Uri(hit.Path).AbsoluteUri,chunk.Anchor,chunk.Text,true),score));
            }
        }
        return evidence.OrderByDescending(e=>e.Score).Select(e=>e.Value).DistinctBy(e=>e.Id).Take(maximum).ToList();
    }
    public List<DocumentSection> GetSections(string path)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT page,anchor,text FROM document_chunks WHERE path=$p ORDER BY ordinal";cmd.Parameters.AddWithValue("$p",path);
        using var r=cmd.ExecuteReader();var result=new List<DocumentSection>();while(r.Read())result.Add(new(r.IsDBNull(0)?null:r.GetInt32(0),r.GetString(1),r.GetString(2)));return result;
    }
    public (bool Bookmark,string Tags) GetNote(string path)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT bookmark,tags FROM document_notes WHERE path=$p";cmd.Parameters.AddWithValue("$p",path);using var r=cmd.ExecuteReader();return r.Read()?(r.GetBoolean(0),r.GetString(1)):(false,"");
    }
    public void SetNote(string path,bool bookmark,string tags)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO document_notes(path,bookmark,tags) VALUES($p,$b,$t) ON CONFLICT(path) DO UPDATE SET bookmark=$b,tags=$t";
        cmd.Parameters.AddWithValue("$p",path);cmd.Parameters.AddWithValue("$b",bookmark?1:0);cmd.Parameters.AddWithValue("$t",tags[..Math.Min(tags.Length,1000)]);cmd.ExecuteNonQuery();
    }
    public IReadOnlySet<string> Bookmarks()
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT path FROM document_notes WHERE bookmark=1";using var r=cmd.ExecuteReader();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(r.Read())paths.Add(r.GetString(0));return paths;
    }
    public IReadOnlyList<string> PathsByTag(string tag)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT path FROM document_notes WHERE instr(lower(tags),lower($t))>0";cmd.Parameters.AddWithValue("$t",tag);using var r=cmd.ExecuteReader();var paths=new List<string>();while(r.Read())paths.Add(r.GetString(0));return paths;
    }
}

public static class DocumentComparison
{
    public static string Compare(string left,string right,string leftName="이전 문서",string rightName="이후 문서")
    {
        var a=left.Replace("\r","").Split('\n');var b=right.Replace("\r","").Split('\n');
        if(a.Length>4000||b.Length>4000)throw new ArgumentException("문서 비교는 각 4,000줄까지 지원합니다. 비교할 부분을 나눠 주세요.");
        // Bounded line LCS; preserve duplicate lines and source order.
        var lengths=new ushort[a.Length+1,b.Length+1];
        for(var i=a.Length-1;i>=0;i--)for(var j=b.Length-1;j>=0;j--)lengths[i,j]=a[i]==b[j]?(ushort)(lengths[i+1,j+1]+1):Math.Max(lengths[i+1,j],lengths[i,j+1]);
        var output=new System.Text.StringBuilder($"# 문서 비교\n\n이전: {leftName}\n이후: {rightName}\n\n추가(+), 삭제(-), 유지(공백)\n\n");var x=0;var y=0;
        while(x<a.Length||y<b.Length)
        {
            if(x<a.Length&&y<b.Length&&a[x]==b[y]){output.AppendLine("  "+a[x]);x++;y++;}
            else if(y<b.Length&&(x==a.Length||lengths[x,y+1]>=lengths[x+1,y]))output.AppendLine("+ "+b[y++]);
            else output.AppendLine("- "+a[x++]);
        }
        return output.ToString();
    }
}
