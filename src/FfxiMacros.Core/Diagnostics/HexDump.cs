using System.Globalization;
using System.Text;

namespace FfxiMacros.Core.Diagnostics;

/// <summary>Hex dumping and byte-level diffing, for eyeballing two macro files side by side.</summary>
public static class HexDump
{
    private const int BytesPerRow = 16;

    public static string Format(ReadOnlySpan<byte> bytes, int offset = 0, int length = -1, int baseAddress = 0)
    {
        if (length < 0)
            length = bytes.Length - offset;
        length = Math.Clamp(length, 0, Math.Max(0, bytes.Length - offset));

        var sb = new StringBuilder();
        for (int row = 0; row < length; row += BytesPerRow)
        {
            int count = Math.Min(BytesPerRow, length - row);
            ReadOnlySpan<byte> slice = bytes.Slice(offset + row, count);

            sb.Append((baseAddress + offset + row).ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            for (int i = 0; i < BytesPerRow; i++)
            {
                sb.Append(i < count ? slice[i].ToString("X2", CultureInfo.InvariantCulture) : "  ").Append(' ');
                if (i == 7)
                    sb.Append(' ');
            }

            sb.Append(" |");
            foreach (byte b in slice)
                sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            sb.Append('|').AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Offsets where two buffers differ (plus the tail if their lengths differ).</summary>
    public static IReadOnlyList<int> DiffOffsets(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var diffs = new List<int>();
        int common = Math.Min(a.Length, b.Length);
        for (int i = 0; i < common; i++)
        {
            if (a[i] != b[i])
                diffs.Add(i);
        }
        for (int i = common; i < Math.Max(a.Length, b.Length); i++)
            diffs.Add(i);
        return diffs;
    }

    /// <summary>Human-readable report of the differences between two buffers.</summary>
    public static string Diff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, string nameA = "A", string nameB = "B", int maxRows = 32)
    {
        var offsets = DiffOffsets(a, b);
        if (offsets.Count == 0)
            return $"{nameA} and {nameB} are byte-identical ({a.Length} bytes).";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{offsets.Count} differing byte(s) between {nameA} ({a.Length} bytes) and {nameB} ({b.Length} bytes):");
        sb.AppendLine("  offset    " + nameA + "   " + nameB);

        foreach (int offset in offsets.Take(maxRows))
        {
            string va = offset < a.Length ? a[offset].ToString("X2", CultureInfo.InvariantCulture) : "--";
            string vb = offset < b.Length ? b[offset].ToString("X2", CultureInfo.InvariantCulture) : "--";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {offset:X8}  {va}   {vb}");
        }

        if (offsets.Count > maxRows)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  ... and {offsets.Count - maxRows} more");

        return sb.ToString();
    }
}
