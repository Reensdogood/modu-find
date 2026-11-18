using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;

namespace DocumentFinder.Core;

internal static class MailDocumentParser
{
    public static string Parse(string raw)
    {
        var output=new StringBuilder();var count=0;
        void Part(string value,int depth)
        {
            if(depth>12||count++>100||output.Length>=TextRules.MaxTextLength)return;
            var split=Regex.Match(value,@"\r?\n\r?\n");if(!split.Success)return;
            var headers=Regex.Replace(value[..split.Index],@"\r?\n[ \t]+"," ");var body=value[(split.Index+split.Length)..];
            string Header(string name)=>Regex.Match(headers,"^"+Regex.Escape(name)+@":\s*(.*)$",RegexOptions.IgnoreCase|RegexOptions.Multiline).Groups[1].Value.Trim();
            if(depth==0)foreach(var key in new[]{"Subject","From","To","Date"})output.AppendLine(key+": "+DecodeWords(Header(key)));
            var disposition=Header("Content-Disposition");if(disposition.StartsWith("attachment",StringComparison.OrdinalIgnoreCase))return;
            ContentType type;try{type=new(Header("Content-Type").Length>0?Header("Content-Type"):"text/plain");}catch(FormatException){return;}
            if(type.MediaType.StartsWith("multipart/",StringComparison.OrdinalIgnoreCase))
            {
                if(string.IsNullOrEmpty(type.Boundary))return;
                foreach(var child in body.Split("--"+type.Boundary).Skip(1)){if(child.StartsWith("--"))break;Part(child.TrimStart('\r','\n'),depth+1);}return;
            }
            if(type.MediaType is not ("text/plain" or "text/html"))return;
            var charset=type.CharSet??"utf-8";Encoding encoding;try{encoding=Encoding.GetEncoding(charset);}catch(ArgumentException){encoding=Encoding.UTF8;}
            var transfer=Header("Content-Transfer-Encoding");string decoded;
            try
            {
                decoded=transfer.Equals("base64",StringComparison.OrdinalIgnoreCase)?encoding.GetString(Convert.FromBase64String(body)):
                    transfer.Equals("quoted-printable",StringComparison.OrdinalIgnoreCase)?encoding.GetString(QuotedPrintable(body)):body;
            }
            catch(FormatException){decoded=body;}
            if(type.MediaType=="text/html")decoded=ResearchText.Plain(decoded);
            output.AppendLine(decoded[..Math.Min(decoded.Length,TextRules.MaxTextLength-output.Length)]);
        }
        Part(raw,0);return output.ToString();
    }
    private static string DecodeWords(string value)=>Regex.Replace(value,@"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=",m=>
    {
        try{var encoding=Encoding.GetEncoding(m.Groups[1].Value);return encoding.GetString(m.Groups[2].Value.Equals("b",StringComparison.OrdinalIgnoreCase)?Convert.FromBase64String(m.Groups[3].Value):QuotedPrintable(m.Groups[3].Value.Replace('_',' ')));}
        catch(Exception ex) when(ex is ArgumentException or FormatException){return m.Value;}
    });
    private static byte[] QuotedPrintable(string value)
    {
        value=Regex.Replace(value,@"=\r?\n","");using var bytes=new MemoryStream();
        for(var i=0;i<value.Length;i++){if(value[i]=='='&&i+2<value.Length&&byte.TryParse(value.AsSpan(i+1,2),System.Globalization.NumberStyles.HexNumber,null,out var b)){bytes.WriteByte(b);i+=2;}else if(value[i]<=255)bytes.WriteByte((byte)value[i]);else bytes.Write(Encoding.UTF8.GetBytes(value[i].ToString()));}return bytes.ToArray();
    }
}
