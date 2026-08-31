// TestTink.cs —— unit tests for the tink frame protocol (C#), zero deps.
// Run: dotnet run

using System;
using System.Text;
using static Tink.Tink;

internal static class Program
{
    private static int s_failures;

    private static void Check(bool cond, string name)
    {
        if (cond)
            Console.WriteLine("[PASS] " + name);
        else
        {
            s_failures++;
            Console.WriteLine("[FAIL] " + name);
        }
    }

    private static void Main()
    {
        // crc32 check vector
        Check(Crc32(Encoding.UTF8.GetBytes("123456789")) == 0xCBF43926u, "crc32 vector");

        // frame roundtrip
        byte[] p = { 1, 2, 3 };
        byte[] frame = FrameEncode(p);
        var got = FrameNext(frame, 0);
        Check(got is not null, "frame present");
        if (got is not null)
        {
            Check(got.Next == frame.Length, "frame next == length");
            Check(got.Payload.AsSpan().SequenceEqual(p), "frame payload roundtrip");
        }

        // empty frame roundtrip
        byte[] fe = FrameEncode(ReadOnlySpan<byte>.Empty);
        var ge = FrameNext(fe, 0);
        Check(ge is { Next: not 0 } && ge.Next == fe.Length && ge.Payload.Length == 0, "empty frame roundtrip");

        // CRC tamper rejected
        byte[] ft = FrameEncode(p);
        ft[4] += 1; // payload[0] tampered
        Check(FrameNext(ft, 0) is null, "crc tamper rejected");

        // frameSkip matches length
        byte[] fs = FrameEncode(p);
        int? nxt = FrameSkip(fs, 0);
        Check(nxt is not null && nxt.Value == fs.Length, "frameSkip matches length");

        // out of bounds
        Check(FrameNext(frame, frame.Length) is null, "frameNext out of bounds");
        Check(FrameSkip(frame, frame.Length) is null, "frameSkip out of bounds");

        if (s_failures > 0)
        {
            Console.WriteLine(s_failures + " checks FAILED");
            Environment.Exit(1);
        }
        Console.WriteLine("all tests passed");
    }
}
