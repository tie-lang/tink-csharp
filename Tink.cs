// Tink.cs —— tink data-flow node frame protocol (universal, language-agnostic).
//
// Frame = [len u32 BE][payload][crc u32 BE]; crc = CRC32-IEEE (0xEDB88320).
// Mirrors std/tink.tie (tie standard library) and the Rust / C / Python / JS /
// C++ / Java tink libraries; pure functions over byte buffers, IO
// (stdin/stdout) left to the caller. .NET (C#), no dependencies.
//
//   var frame = Tink.FrameEncode(new byte[] { 1, 2, 3 });
//   var got = Tink.FrameNext(frame, 0);   // FrameInfo?
//   if (got is { } g) { /* g.Payload, g.Next */ }

using System;

namespace Tink;

/// <summary>
/// tink data-flow node frame protocol: CRC32-IEEE and zd frame framing.
/// All methods are pure functions over byte buffers; IO is the caller's job.
/// </summary>
public static class Tink
{
    /// <summary>Result of parsing a frame: payload (a copy) and the position
    /// right after the frame (payload length + 8).</summary>
    public sealed record FrameInfo(byte[] Payload, int Next);

    /// <summary>CRC32-IEEE over a byte span (bit-loop, no table; matches
    /// zlib.crc32). Check vector: <c>crc32("123456789") == 0xCBF43926</c>.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
        }
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Encode a payload into a full frame: [len u32 BE][payload][crc].</summary>
    public static byte[] FrameEncode(ReadOnlySpan<byte> payload)
    {
        var out_ = new byte[payload.Length + 8];
        uint n = (uint)payload.Length;
        out_[0] = (byte)(n >> 24);
        out_[1] = (byte)(n >> 16);
        out_[2] = (byte)(n >> 8);
        out_[3] = (byte)n;
        payload.CopyTo(out_.AsSpan(4));
        uint c = Crc32(payload);
        out_[^4] = (byte)(c >> 24);
        out_[^3] = (byte)(c >> 16);
        out_[^2] = (byte)(c >> 8);
        out_[^1] = (byte)c;
        return out_;
    }

    /// <summary>Parse one frame at <paramref name="pos"/> (verifies CRC).
    /// <see cref="FrameInfo"/> with a copy of the payload and <c>Next</c> =
    /// position after the frame; <c>null</c> on out-of-bounds / CRC mismatch.</summary>
    public static FrameInfo? FrameNext(ReadOnlySpan<byte> bytes, int pos)
    {
        if (bytes.Length < pos + 8)
            return null;
        uint n = ReadBe32(bytes, pos);
        long end = (long)pos + 8 + n;
        if (bytes.Length < end)
            return null;
        byte[] payload = bytes.Slice(pos + 4, (int)n).ToArray();
        uint want = ReadBe32(bytes, (int)(end - 4));
        if (Crc32(payload) != want)
            return null;
        return new FrameInfo(payload, (int)end);
    }

    /// <summary>Skip one frame at <paramref name="pos"/> without copying or
    /// verifying (zero-copy). Returns the position after the frame, or
    /// <c>null</c> on out-of-bounds.</summary>
    public static int? FrameSkip(ReadOnlySpan<byte> bytes, int pos)
    {
        if (bytes.Length < pos + 8)
            return null;
        uint n = ReadBe32(bytes, pos);
        long end = (long)pos + 8 + n;
        if (bytes.Length < end)
            return null;
        return (int)end;
    }

    private static uint ReadBe32(ReadOnlySpan<byte> b, int off)
    {
        return ((uint)b[off] << 24) | ((uint)b[off + 1] << 16) |
               ((uint)b[off + 2] << 8) | b[off + 3];
    }
}