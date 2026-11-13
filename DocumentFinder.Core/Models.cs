using System.Text;
using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

public record FileRecord(string Path, long Size, long ModifiedTicks, string Root);
public record ExtractedDocument(string Content, string[] Sheets, string Status = "완료", string Note = "");
public record SearchHit(long Id, string Name, string Path, string Extension, long Size, long ModifiedTicks,
    string Sheets, string Content, string Status, string Note, int Rank);
public record IndexStats(int Total, int Complete, int Pending, int Issues, long DatabaseBytes);
public record ScanProgress(string Phase, int Found, int Processed, int Updated, int Skipped, int Issues, string Current, bool Finished = false);
public record ScanOptions(bool IncludeOnlineFiles = true, long MaximumFileBytes = 80 * 1024 * 1024);
public record SearchOptions(string Query, string Kind = "전체", bool NamesOnly = false, bool NewestFirst = false, int Limit = 500);

public static class TextRules
{
    public const int MaxTextLength = 400_000;
    public static string Normalize(string text) => text.Replace("\0", "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    public static string[] Terms(string query) => Regex.Matches(query, "\"([^\"]+)\"|([^\\s]+)")
        .Select(m => Normalize(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
        .Where(t => t.Length > 0).Distinct().Take(12).ToArray();

    public static string Kind(string extension) => extension.ToLowerInvariant() switch
    {
        ".xlsx" or ".xlsm" or ".xls" or ".xlsb" or ".xltx" or ".xltm" or ".xlt" => "엑셀",
        ".pdf" => "PDF", ".hwpx" or ".hwp" => "한글", ".docx" or ".doc" => "Word",
        ".pptx" or ".ppt" => "PowerPoint", _ => "텍스트"
    };

    public static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string[] ReduceRoots(IEnumerable<string> roots)
    {
        var result = new List<string>();
        foreach (var p in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length))
            if (!result.Any(root => IsWithin(p, root))) result.Add(p);
        return result.ToArray();
    }

    public static string Snippet(string content, string[] terms)
    {
        if (string.IsNullOrWhiteSpace(content)) return "본문 색인이 없습니다.";
        var normalized = Normalize(content);
        var first = terms.Select(t => normalized.IndexOf(t, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, Math.Min(content.Length - 1, first) - 80);
        return (start > 0 ? "… " : "") + content.Substring(start, Math.Min(900, content.Length - start));
    }
}
