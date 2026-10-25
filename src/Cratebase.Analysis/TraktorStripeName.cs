using System.Buffers.Binary;
using System.Numerics;

namespace Cratebase.Analysis;

/// <summary>Traktor's AUDIO_ID digest: four MD5 blocks plus a zero block, then its base-32 filename alphabet.</summary>
public static class TraktorStripeName
{
    private static readonly int[] Shifts=[7,12,17,22,5,9,14,20,4,11,16,23,6,10,15,21];
    private static readonly uint[] Constants=Enumerable.Range(1,64).Select(i=>(uint)(Math.Abs(Math.Sin(i))*4294967296.0)).ToArray();
    public static string? FromAudioId(string audioId)
    {
        byte[] bytes;
        try { bytes=Convert.FromBase64String(audioId); } catch(FormatException) { return null; }
        if(bytes.Length<256) return null;
        uint[] state=[0x67452301,0xefcdab89,0x98badcfe,0x10325476];
        Span<uint> words=stackalloc uint[16];
        for(var block=0;block<5;block++)
        {
            for(var i=0;i<16;i++) words[i]=block<4?BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(block*64+i*4,4)):0;
            var a=state[0];var b=state[1];var c=state[2];var d=state[3];
            unchecked
            {
                for(var i=0;i<64;i++)
                {
                    var round=i/16;
                    var f=round switch { 0=>(b&c)|(~b&d),1=>(d&b)|(~d&c),2=>b^c^d,_=>c^(b|~d) };
                    var index=round switch { 0=>i,1=>(5*i+1)%16,2=>(3*i+5)%16,_=>(7*i)%16 };
                    var next=b+BitOperations.RotateLeft(a+f+Constants[i]+words[index],Shifts[round*4+i%4]);
                    a=d;d=c;c=b;b=next;
                }
                state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;
            }
        }
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ012345";
        var name=new char[28];
        for(var word=0;word<4;word++) for(var part=0;part<7;part++) name[word*7+part]=alphabet[(int)((state[word]>>(5*part))&31)];
        return Path.Combine((state[0]&127).ToString("D3"),new string(name));
    }
}
