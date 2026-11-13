using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocumentFinder.Core;

public static class DocumentExtractor
{
    private static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".xlsx",".xlsm",".xls",".xlsb",".xltx",".xltm",".xlt",".pdf",".hwpx",".hwp",".docx",".doc",".pptx",".ppt",".txt",".csv",".tsv",".md",".log",".rtf" };

    static DocumentExtractor()=>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private sealed class TextSink
    {
        public readonly StringBuilder Value=new();public bool Full=>Value.Length>=TextRules.MaxTextLength;
        public void Add(string? text)
        {
            if(string.IsNullOrWhiteSpace(text)||Full)return;
            var clean=text.Replace("\0","");Value.AppendLine(clean[..Math.Min(clean.Length,TextRules.MaxTextLength-Value.Length)]);
        }
    }
    public static ExtractedDocument Extract(string path)
    {
        var ext=Path.GetExtension(path).ToLowerInvariant();var sink=new TextSink();string[] sheets=[];var note="";
        if(ext is ".doc" or ".ppt" or ".rtf")return new("",[],"파일명만","이 형식은 파일명으로 검색합니다. DOCX·PPTX·TXT로 변환하면 본문을 읽을 수 있습니다.");
        if(new FileInfo(path).Length>80*1024*1024)return new("",[],"용량 초과","80MB를 초과하여 파일명만 색인했습니다.");
        if(ext is ".xlsx" or ".xlsm" or ".xltx" or ".xltm")sheets=ReadExcelXml(path,sink);
        else if(ext is ".xls" or ".xlsb" or ".xlt")
        {
            using var input=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=ExcelReaderFactory.CreateReader(input,new ExcelReaderConfiguration{LeaveOpen=false});
            var names=new List<string>();
            do
            {
                names.Add(reader.Name);sink.Add("[시트: "+reader.Name+"]");var row=0;
                while(reader.Read() && !sink.Full)
                {
                    row++;var values=new List<string>();
                    for(var c=0;c<reader.FieldCount;c++)
                    {
                        var v=reader.GetValue(c);if(v is null)continue;
                        values.Add((c+1)+": "+Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if(values.Count>0)sink.Add(row+"행 | "+string.Join(" | ",values));
                }
            }while(reader.NextResult() && !sink.Full);sheets=names.ToArray();
        }
        else if(ext==".pdf")
        {
            using var pdf=PdfDocument.Open(path);
            foreach(var page in pdf.GetPages()){sink.Add("["+page.Number+"쪽]");sink.Add(ContentOrderTextExtractor.GetText(page));if(sink.Full)break;}
            if(Regex.Replace(sink.Value.ToString(),@"\[\d+쪽\]|\s","").Length<10)
                return new("",[],"OCR 필요","스캔 이미지 PDF이거나 텍스트가 거의 없습니다. 파일명으로는 검색할 수 있습니다.");
        }
        else if(ext==".hwp")ReadHwp(path,sink);
        else if(ext is ".hwpx" or ".docx" or ".pptx")ReadOfficeXml(path,ext,sink);
        else ReadText(path,sink);
        if(sink.Full)note="본문이 길어 앞부분 "+TextRules.MaxTextLength.ToString("N0")+"자까지 색인했습니다.";
        return new(sink.Value.ToString(),sheets,sink.Full?"일부 내용":"완료",note);
    }

    private static XDocument Xml(ZipArchiveEntry entry)
    {
        if(entry.Length>30*1024*1024)throw new InvalidDataException("압축 해제된 문서 항목이 너무 큽니다.");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=30*1024*1024});
        return XDocument.Load(reader);
    }
    private static string[] ReadExcelXml(string path,TextSink sink)
    {
        using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var zip=new ZipArchive(file,ZipArchiveMode.Read);
        var book=Xml(zip.GetEntry("xl/workbook.xml")??throw new InvalidDataException("엑셀 통합 문서가 없습니다."));
        var relationships=Xml(zip.GetEntry("xl/_rels/workbook.xml.rels")??throw new InvalidDataException("시트 연결 정보가 없습니다."));
        var map=relationships.Root!.Elements().ToDictionary(e=>(string)e.Attribute("Id")!,e=>(string)e.Attribute("Target")!);
        var strings=new List<string>();var ss=zip.GetEntry("xl/sharedStrings.xml");
        if(ss is not null)strings=Xml(ss).Root!.Elements(S+"si").Select(e=>string.Concat(e.Descendants(S+"t").Select(t=>t.Value))).ToList();
        var sheets=book.Descendants(S+"sheet").ToArray();
        foreach(var sheet in sheets)
        {
            if(sink.Full)break;var name=(string?)sheet.Attribute("name")??"시트";sink.Add("[시트: "+name+"]");
            var rid=(string?)sheet.Attribute(XName.Get("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            if(rid is null || !map.TryGetValue(rid,out var target))continue;
            var uri=new Uri(new Uri("http://local/xl/"),target);var entry=zip.GetEntry(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));if(entry is null)continue;
            foreach(var row in Xml(entry).Descendants(S+"row"))
            {
                if(sink.Full)break;var cells=new List<string>();
                foreach(var cell in row.Elements(S+"c"))
                {
                    var value=cell.Element(S+"v")?.Value??"";var type=(string?)cell.Attribute("t");
                    if(type=="s" && int.TryParse(value,out var n))value=n>=0&&n<strings.Count?strings[n]:"";
                    else if(type=="inlineStr")value=string.Concat(cell.Descendants(S+"t").Select(t=>t.Value));
                    if(!string.IsNullOrWhiteSpace(value))cells.Add((string?)cell.Attribute("r")+": "+value);
                }
                if(cells.Count>0)sink.Add(string.Join(" | ",cells));
            }
        }
        return sheets.Select(s=>(string?)s.Attribute("name")??"").ToArray();
    }
    private static void ReadOfficeXml(string path,string extension,TextSink sink)
    {
        using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var zip=new ZipArchive(file,ZipArchiveMode.Read);
        var entries=zip.Entries.Where(e=>extension switch
        {
            ".hwpx"=>Regex.IsMatch(e.FullName,@"^Contents/section\d+\.xml$"),
            ".docx"=>e.FullName=="word/document.xml"||Regex.IsMatch(e.FullName,@"^word/(header|footer|footnotes|endnotes)\d*\.xml$"),
            _=>Regex.IsMatch(e.FullName,@"^ppt/(slides/slide|notesSlides/notesSlide)\d+\.xml$")
        }).OrderBy(e=>Regex.Replace(e.FullName,@"\d+",m=>m.Value.PadLeft(8,'0')));
        foreach(var entry in entries)
        {
            if(sink.Full)break;var xml=Xml(entry);
            foreach(var p in xml.Descendants().Where(e=>e.Name.LocalName=="p"))
            {
                // In HWPX paragraphs may contain tables. Read each text element once.
                var pieces=p.Descendants().Where(e=>e.Name.LocalName=="t" && e.Ancestors().FirstOrDefault(a=>a.Name.LocalName=="p")==p).Select(e=>e.Value);
                sink.Add(string.Concat(pieces));if(sink.Full)break;
            }
        }
    }
    private static void ReadHwp(string path,TextSink sink)
    {
        var compound=new CompoundDocument(File.ReadAllBytes(path));var header=compound.ReadStream("FileHeader");
        if(header.Length<40)throw new InvalidDataException("잘린 HWP 헤더입니다.");var flags=BitConverter.ToUInt32(header,36);
        if((flags&2)!=0)throw new InvalidDataException("암호로 보호된 HWP 문서입니다.");
        if((flags&4)!=0)throw new InvalidDataException("배포용 HWP 문서는 본문 읽기를 지원하지 않습니다.");
        foreach(var name in compound.Names.Where(n=>Regex.IsMatch(n,@"^BodyText/Section\d+$")).OrderBy(n=>n.Length).ThenBy(n=>n))
        {
            if(sink.Full)break;var bytes=compound.ReadStream(name);
            if((flags&1)!=0)
            {
                using var z=new DeflateStream(new MemoryStream(bytes),CompressionMode.Decompress);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
                while((count=z.Read(chunk))>0){if(buffer.Length+count>30*1024*1024)throw new InvalidDataException("HWP 본문이 너무 큽니다.");buffer.Write(chunk,0,count);}bytes=buffer.ToArray();
            }
            var pos=0;
            while(pos+4<=bytes.Length && !sink.Full)
            {
                var record=BitConverter.ToUInt32(bytes,pos);pos+=4;var size=(int)(record>>20);
                if(size==4095){if(pos+4>bytes.Length)break;size=checked((int)BitConverter.ToUInt32(bytes,pos));pos+=4;}
                if(size<0 || size>bytes.Length-pos)throw new InvalidDataException("잘린 HWP 본문 레코드입니다.");
                if((record&1023)==67)
                {
                    var text=Encoding.Unicode.GetString(bytes,pos,size);var clean=new StringBuilder();
                    for(var i=0;i<text.Length;i++)
                    {
                        var ch=(int)text[i];
                        if(ch is >=1 and <=9 or 11 or 12 or >=14 and <=23){if(ch==9)clean.Append(' ');i+=7;}
                        else if(ch is 10 or 13)clean.Append('\n');else if(ch is 30 or 31)clean.Append(' ');else if(ch>=32)clean.Append((char)ch);
                    }
                    sink.Add(clean.ToString());
                }
                pos+=size;
            }
        }
        if(sink.Value.Length==0)throw new InvalidDataException("HWP에서 본문 텍스트를 찾지 못했습니다.");
    }
    private static void ReadText(string path,TextSink sink)
    {
        var bytes=File.ReadAllBytes(path);string text;
        if(bytes.Length>=2 && bytes[0]==0xFF && bytes[1]==0xFE)text=Encoding.Unicode.GetString(bytes);
        else if(bytes.Length>=2 && bytes[0]==0xFE && bytes[1]==0xFF)text=Encoding.BigEndianUnicode.GetString(bytes);
        else {try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){text=Encoding.GetEncoding(949).GetString(bytes);}}
        sink.Add(text.TrimStart('\uFEFF'));
    }
}
