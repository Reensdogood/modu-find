using System.Buffers.Binary;
using System.Text;

namespace DocumentFinder.Core;

/// <summary>Bounded read-only CFB container reader for HWP 5. Never executes document code.</summary>
internal sealed class CompoundDocument
{
    private readonly byte[] data;
    private readonly int sectorSize, miniSize;
    private readonly uint[] fat, miniFat;
    private readonly byte[] miniStream;
    private readonly Dictionary<string, Entry> streams = new(StringComparer.OrdinalIgnoreCase);
    private record Entry(string Name, byte Type, uint Left, uint Right, uint Child, uint Start, ulong Size);
    private const uint End = 0xFFFFFFFE;
    private const uint Free = 0xFFFFFFFF;

    public CompoundDocument(byte[] bytes)
    {
        data=bytes;
        if(bytes.Length<512 || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1}))throw new InvalidDataException("HWP 5 형식이 아닙니다.");
        var shift=U16(30); if(shift is not 9 and not 12 || U16(32)!=6)throw new InvalidDataException("지원하지 않는 저장 형식입니다.");
        sectorSize=1<<shift;miniSize=64;
        var fatSectors=new List<uint>();
        for(var i=0;i<109;i++){var s=U32(76+i*4);if(s<0xFFFFFFFA)fatSectors.Add(s);}
        var difat=U32(68);var difatCount=U32(72);var visited=new HashSet<uint>();
        for(var i=0;i<difatCount && difat<0xFFFFFFFA;i++)
        {
            if(!visited.Add(difat))throw new InvalidDataException("손상된 FAT 연결입니다.");
            var sec=Sector(difat);
            for(var j=0;j<sectorSize/4-1;j++){var id=BinaryPrimitives.ReadUInt32LittleEndian(sec.Slice(j*4,4));if(id<0xFFFFFFFA)fatSectors.Add(id);}
            difat=BinaryPrimitives.ReadUInt32LittleEndian(sec[^4..]);
        }
        if(fatSectors.Count>data.Length/sectorSize)throw new InvalidDataException("잘못된 FAT 길이입니다.");
        fat=fatSectors.SelectMany(s=>DecodeUInts(Sector(s).ToArray())).ToArray();
        var directory=ReadChain(U32(48),fat,false,80*1024*1024);
        var entries=new List<Entry>();
        for(var i=0;i+128<=directory.Length;i+=128)
        {
            var e=directory.AsSpan(i,128);var n=BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(64,2));
            var name=n is >=2 and <=64?Encoding.Unicode.GetString(e[..(n-2)]):"";
            entries.Add(new Entry(name,e[66],Read(e,68),Read(e,72),Read(e,76),Read(e,116),BinaryPrimitives.ReadUInt64LittleEndian(e.Slice(120,8))));
        }
        if(entries.Count==0 || entries[0].Type!=5)throw new InvalidDataException("HWP 루트 저장소가 없습니다.");
        miniStream=ReadSized(entries[0].Start,entries[0].Size,fat,false);
        miniFat=DecodeUInts(ReadChain(U32(60),fat,false,Math.Min(80*1024*1024,checked((int)Math.Min(U32(64),100000)*sectorSize)))).ToArray();
        var nodes=new HashSet<uint>();
        void Walk(uint id,string parent,int depth)
        {
            if(id==Free || id>=entries.Count)return;
            if(depth>150 || !nodes.Add(id))throw new InvalidDataException("손상된 디렉터리입니다.");
            var e=entries[(int)id];Walk(e.Left,parent,depth+1);
            var path=parent.Length==0?e.Name:parent+"/"+e.Name;
            if(e.Type==2)streams[path]=e;
            if(e.Type==1)Walk(e.Child,path,depth+1);
            Walk(e.Right,parent,depth+1);
        }
        Walk(entries[0].Child,"",0);
    }
    public IEnumerable<string> Names=>streams.Keys;
    public byte[] ReadStream(string path)
    {
        if(!streams.TryGetValue(path,out var e))throw new InvalidDataException("HWP 본문 저장소가 없습니다.");
        return ReadSized(e.Start,e.Size,e.Size<4096?miniFat:fat,e.Size<4096);
    }
    private byte[] ReadSized(uint start,ulong size,uint[] allocation,bool mini)
    {
        if(size>80*1024*1024)throw new InvalidDataException("문서 저장소가 너무 큽니다.");
        if(size==0)return [];
        var b=ReadChain(start,allocation,mini,checked((int)size));
        if(b.Length<(int)size)throw new InvalidDataException("잘린 문서 저장소입니다.");
        return b.Length==(int)size?b:b.AsSpan(0,(int)size).ToArray();
    }
    private byte[] ReadChain(uint start,uint[] allocation,bool mini,int limit)
    {
        using var output=new MemoryStream();var seen=new HashSet<uint>();var id=start;
        while(id!=End && id!=Free && output.Length<limit)
        {
            if(id>=allocation.Length || !seen.Add(id))throw new InvalidDataException("손상된 문서 블록입니다.");
            ReadOnlySpan<byte> s;
            if(mini)
            {
                var at=checked((long)id*miniSize);if(at+miniSize>miniStream.Length)throw new InvalidDataException("잘린 미니 블록입니다.");
                s=miniStream.AsSpan((int)at,miniSize);
            }
            else s=Sector(id);
            output.Write(s[..Math.Min(s.Length,limit-(int)output.Length)]);id=allocation[id];
        }
        return output.ToArray();
    }
    private ReadOnlySpan<byte> Sector(uint id)
    {
        var at=checked(((long)id+1)*sectorSize);
        if(at<0 || at+sectorSize>data.Length)throw new InvalidDataException("잘린 문서 섹터입니다.");
        return data.AsSpan((int)at,sectorSize);
    }
    private ushort U16(int at)=>BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at,2));
    private uint U32(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at,4));
    private static uint Read(ReadOnlySpan<byte> b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at,4));
    private static uint[] DecodeUInts(byte[] b)
    {
        var result=new uint[b.Length/4];for(var i=0;i<result.Length;i++)result[i]=BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i*4,4));return result;
    }
}
