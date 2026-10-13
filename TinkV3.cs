// TinkV3.cs —— tink v3 frame skeleton + message fragmentation (C# / .NET, no dependencies).
//
// Mirrors the C / Python / Rust / Java ports and the tie reference
// implementation (tlib std/tink.tie); KAT vectors are byte-exact across
// languages.
//
// Frame (16-byte fixed header, big-endian; v3 design doc §3):
//   [magic 0x74 0x6B][ver=3][type=0x04][flags u16][stream_id u32][seq u32][len u16]
//   [ext region: ext_len u16 + TLV bytes]?   (flags.EXT)
//   [payload: len bytes]                     (flags.OFFSET: first 4 bytes = u32 BE offset)
//   [tail: CRC32C 4B | AEAD tag 16B | none]
// flags: 1=EXT 2=PROTECTED 4=CRC 8=FIN 16=OFFSET 32=URGENT.
//
// Implemented subset: CRC32C tail, EXT region, single frames up to 65535
// bytes, message fragmentation (> 65531 bytes split into OFFSET+FIN
// fragments, seq = fragment index) with transparent sequential reassembly on
// decode. PROTECTED frames are rejected (crypto layer is out of scope).

using System;
using System.Buffers.Binary;
using System.IO;

namespace Tink;

/// <summary>
/// tink v3 frame skeleton: CRC32C, 16-byte fixed header, EXT region, message
/// fragmentation and transparent reassembly. Pure functions over byte spans;
/// IO is the caller's job.
/// </summary>
public static class TinkV3
{
    /// <summary>MESSAGE frame type (0x04).</summary>
    public const byte TMessage = 0x04;

    public const int FExt = 1;
    public const int FProtected = 2;
    public const int FCrc = 4;
    public const int FFin = 8;
    public const int FOffset = 16;
    public const int FUrgent = 32;

    /// <summary>Max fragment data (65535 - 4-byte offset prefix).</summary>
    public const int ChunkMax = 65531;

    private static readonly uint[] CrcTab = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? (c >> 1) ^ 0x82F63B78u : c >> 1;
            }
            t[i] = c;
        }
        return t;
    }

    /// <summary>CRC32C (Castagnoli, reflected poly 0x82F63B78; init/xorout
    /// 0xFFFFFFFF). Check vector: <c>Crc32C("123456789") == 0xE3069283</c>.</summary>
    public static uint Crc32C(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            c = CrcTab[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }

    private static byte[] FrameOne(ReadOnlySpan<byte> payload, int flags, uint seq, ReadOnlySpan<byte> ext)
    {
        bool hasExt = (flags & FExt) != 0;
        var buf = new byte[16 + payload.Length + 4 + (hasExt ? 2 + ext.Length : 0)];
        buf[0] = 0x74;
        buf[1] = 0x6B;
        buf[2] = 3;
        buf[3] = TMessage;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4), (ushort)flags);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(6), 0); // stream_id
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(10), seq);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(14), (ushort)payload.Length);
        int pos = 16;
        if (hasExt)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(pos), (ushort)ext.Length);
            pos += 2;
            ext.CopyTo(buf.AsSpan(pos));
            pos += ext.Length;
        }
        payload.CopyTo(buf.AsSpan(pos));
        pos += payload.Length;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(pos), Crc32C(buf.AsSpan(0, pos)));
        return buf;
    }

    /// <summary>Single frame without an extension region (CRC|FIN; payload up to 65535).</summary>
    public static byte[] FrameEncode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 65535)
        {
            throw new ArgumentException("payload too large for a single frame");
        }
        return FrameOne(payload, FCrc | FFin, 0, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>Single frame carrying an extension region (raw TLV bytes).</summary>
    public static byte[] FrameEncodeExt(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> ext)
    {
        if (payload.Length > 65535 || ext.Length > 65535)
        {
            throw new ArgumentException("payload or ext too large");
        }
        int flags = FCrc | FFin | (ext.Length > 0 ? FExt : 0);
        return FrameOne(payload, flags, 0, ext);
    }

    /// <summary>Message layer: single frame when payload length &lt;= 65531, else
    /// OFFSET fragments (each up to 65531 payload bytes; last fragment FIN,
    /// seq = fragment index).</summary>
    public static byte[] MsgEncode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= ChunkMax)
        {
            return FrameOne(payload, FCrc | FFin, 0, ReadOnlySpan<byte>.Empty);
        }
        var ms = new MemoryStream();
        int off = 0;
        uint idx = 0;
        while (off < payload.Length)
        {
            int len = Math.Min(ChunkMax, payload.Length - off);
            var body = new byte[4 + len];
            BinaryPrimitives.WriteUInt32BigEndian(body, (uint)off);
            payload.Slice(off, len).CopyTo(body.AsSpan(4));
            int flags = FCrc | FOffset | (off + len == payload.Length ? FFin : 0);
            var fr = FrameOne(body, flags, idx, ReadOnlySpan<byte>.Empty);
            ms.Write(fr, 0, fr.Length);
            off += len;
            idx++;
        }
        return ms.ToArray();
    }

    /// <summary>One parsed frame: flags, payload view, next position.</summary>
    private readonly record struct FrameView(int Flags, int PayloadOffset, int PayloadLen, int Next);

    private static FrameView? FrameRead(ReadOnlySpan<byte> b, int pos)
    {
        if (pos + 16 > b.Length)
        {
            return null;
        }
        if (b[pos] != 0x74 || b[pos + 1] != 0x6B || b[pos + 2] != 3 || b[pos + 3] != TMessage)
        {
            return null;
        }
        int flags = (b[pos + 4] << 8) | b[pos + 5];
        if ((flags & 0xFFC0) != 0 || (flags & FProtected) != 0)
        {
            return null;
        }
        int eoff = pos + 16;
        if ((flags & FExt) != 0)
        {
            if (pos + 18 > b.Length)
            {
                return null;
            }
            int elen = (b[pos + 16] << 8) | b[pos + 17];
            eoff = pos + 18 + elen;
            if (eoff > b.Length)
            {
                return null;
            }
        }
        int ln = (b[pos + 14] << 8) | b[pos + 15];
        int tlen = (flags & FCrc) != 0 ? 4 : 0;
        if (eoff + ln + tlen > b.Length)
        {
            return null;
        }
        if (tlen == 4)
        {
            uint want = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(eoff + ln));
            uint got = Crc32C(b.Slice(pos, (eoff - pos) + ln));
            if (want != got)
            {
                return null;
            }
        }
        return new FrameView(flags, eoff, ln, eoff + ln + tlen);
    }

    /// <summary>Decoded logical message: payload (a copy) and position right
    /// after the last fragment frame.</summary>
    public sealed record Decoded(byte[] Payload, int Next);

    /// <summary>Decode one logical message at <paramref name="pos"/>, reassembling
    /// fragments (sequential, per OFFSET). Returns <c>null</c> on malformed
    /// frame / CRC mismatch / broken fragment chain.</summary>
    public static Decoded? MsgDecode(ReadOnlySpan<byte> b, int pos)
    {
        var r = FrameRead(b, pos);
        if (r is null)
        {
            return null;
        }
        var f = r.Value;
        if ((f.Flags & FOffset) == 0)
        {
            return new Decoded(b.Slice(f.PayloadOffset, f.PayloadLen).ToArray(), f.Next);
        }
        if (f.PayloadLen < 4 || BinaryPrimitives.ReadUInt32BigEndian(b.Slice(f.PayloadOffset)) != 0)
        {
            return null;
        }
        var ms = new MemoryStream();
        ms.Write(b.Slice(f.PayloadOffset + 4, f.PayloadLen - 4));
        int cur = f.Next;
        bool fin = (f.Flags & FFin) != 0;
        while (!fin)
        {
            var r2 = FrameRead(b, cur);
            if (r2 is null)
            {
                return null;
            }
            var f2 = r2.Value;
            if ((f2.Flags & FOffset) == 0 || f2.PayloadLen < 4)
            {
                return null;
            }
            long off2 = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(f2.PayloadOffset));
            if (off2 != ms.Length)
            {
                return null;
            }
            ms.Write(b.Slice(f2.PayloadOffset + 4, f2.PayloadLen - 4));
            fin = (f2.Flags & FFin) != 0;
            cur = f2.Next;
        }
        return new Decoded(ms.ToArray(), cur);
    }
}
