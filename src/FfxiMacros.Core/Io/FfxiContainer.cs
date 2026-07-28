using System.Security.Cryptography;

namespace FfxiMacros.Core.Io;

/// <summary>
/// The 24-byte header shared by every FFXI macro-related file (<c>mcr*.dat</c>, <c>mcr*.ttl</c>,
/// <c>mcr.sys</c>): an 8-byte version stamp followed by the MD5 of everything after it.
/// </summary>
/// <remarks>
/// Verified against 493 real <c>.dat</c> files and 10 <c>.ttl</c> files: the stored digest matched
/// the payload in every single one.
/// </remarks>
public static class FfxiContainer
{
    public const int VersionSize = 8;
    public const int DigestSize = 16;
    public const int HeaderSize = VersionSize + DigestSize;

    /// <summary>Splits a raw file into its header fields and payload, and checks the digest.</summary>
    /// <param name="raw">Whole file contents.</param>
    /// <param name="expectedPayloadSize">Required payload size, or -1 to accept any size.</param>
    /// <param name="description">File kind, used in error messages (e.g. "macro book").</param>
    /// <exception cref="MacroFileException">The file is too short or has the wrong size.</exception>
    public static (ulong Version, byte[] Payload, bool DigestValid) Read(
        ReadOnlySpan<byte> raw, int expectedPayloadSize, string description)
    {
        if (raw.Length < HeaderSize)
            throw new MacroFileException(
                $"Not a valid {description} file: {raw.Length} bytes, expected at least {HeaderSize}.");

        int payloadSize = raw.Length - HeaderSize;
        if (expectedPayloadSize >= 0 && payloadSize != expectedPayloadSize)
            throw new MacroFileException(
                $"Not a valid {description} file: {raw.Length} bytes, expected {HeaderSize + expectedPayloadSize}.");

        ulong version = BitConverter.ToUInt64(raw[..VersionSize]);
        byte[] payload = raw[HeaderSize..].ToArray();
        bool digestValid = MD5.HashData(payload).AsSpan().SequenceEqual(raw.Slice(VersionSize, DigestSize));

        return (version, payload, digestValid);
    }

    /// <summary>Builds a complete file from a version stamp and a payload, recomputing the MD5.</summary>
    public static byte[] Write(ulong version, ReadOnlySpan<byte> payload)
    {
        var raw = new byte[HeaderSize + payload.Length];
        BitConverter.TryWriteBytes(raw.AsSpan(0, VersionSize), version);
        payload.CopyTo(raw.AsSpan(HeaderSize));
        MD5.HashData(payload).CopyTo(raw.AsSpan(VersionSize, DigestSize));
        return raw;
    }

    /// <summary>The MD5 stored in a raw file's header.</summary>
    public static byte[] StoredDigest(ReadOnlySpan<byte> raw) => raw.Slice(VersionSize, DigestSize).ToArray();
}
