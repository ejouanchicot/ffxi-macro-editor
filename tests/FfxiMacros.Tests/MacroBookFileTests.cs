using System.Security.Cryptography;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using Xunit;

namespace FfxiMacros.Tests;

public class MacroBookFileTests
{
    [Theory]
    [MemberData(nameof(SampleFiles.Books), MemberType = typeof(SampleFiles))]
    public void RoundTrip_ProducesAByteIdenticalFile(string fileName)
    {
        byte[] original = File.ReadAllBytes(SampleFiles.Path_(fileName));

        var book = MacroBookFile.Read(original);
        byte[] rewritten = MacroBookFile.ToBytes(book);

        Assert.Equal(MacroBookFile.FileSize, rewritten.Length);
        Assert.True(
            original.AsSpan().SequenceEqual(rewritten),
            HexDump.Diff(original, rewritten, "original", "rewritten"));
    }

    [Theory]
    [MemberData(nameof(SampleFiles.Books), MemberType = typeof(SampleFiles))]
    public void StoredDigest_MatchesTheDataBlock(string fileName)
    {
        byte[] original = File.ReadAllBytes(SampleFiles.Path_(fileName));

        byte[] recomputed = MD5.HashData(original.AsSpan(FfxiContainer.HeaderSize));

        Assert.Equal(FfxiContainer.StoredDigest(original), recomputed);
        Assert.True(MacroBookFile.Read(original).DigestWasValid);
    }

    [Theory]
    [MemberData(nameof(SampleFiles.Books), MemberType = typeof(SampleFiles))]
    public void Version_IsCopiedThroughUnchanged(string fileName)
    {
        byte[] original = File.ReadAllBytes(SampleFiles.Path_(fileName));

        var book = MacroBookFile.Read(original);
        byte[] rewritten = MacroBookFile.ToBytes(book);

        Assert.Equal(original.AsSpan(0, 8).ToArray(), rewritten.AsSpan(0, 8).ToArray());
    }

    [Fact]
    public void Load_ReadsMacroNamesAndLines()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        Assert.Equal("BuffSelf", book.Macros[0].Name);
        Assert.Equal("/con gs c smartbuff", book.Macros[0].Lines[0]);
        Assert.Equal("", book.Macros[0].Lines[1]);
    }

    [Fact]
    public void Load_KeepsAutoTranslatePhrasesAsEscapes()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        Assert.Equal("/ja \"«02021F97»\" <t>", book.Macros[1].Lines[0]);
    }

    [Fact]
    public void Load_SurfacesStrayNulsInsteadOfTruncatingTheLine()
    {
        // Written by the old 2014 editor: the leading '/' was replaced with a NUL byte.
        // Cutting the string at the first NUL would silently drop the rest of the command.
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        Assert.Equal("{00}con send Kaelith \"Healing Waltz\" <laststid>", book.Macros[6].Lines[1]);
    }

    [Fact]
    public void ReservedBytes_AreZeroInEveryObservedFile()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        foreach (var macro in book.Macros)
        {
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, macro.Header);
            Assert.Equal(0, macro.Trailer);
        }
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheEditAndTheFileSize()
    {
        string path = TempFile();
        try
        {
            var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
            book.Macros[3].Name = "Edited";
            book.Macros[3].Lines[2] = "/ma \"Cure IV\" <t>";
            MacroBookFile.Save(book, path);

            Assert.Equal(MacroBookFile.FileSize, new FileInfo(path).Length);

            var reloaded = MacroBookFile.Load(path);
            Assert.Equal("Edited", reloaded.Macros[3].Name);
            Assert.Equal("/ma \"Cure IV\" <t>", reloaded.Macros[3].Lines[2]);
            Assert.True(reloaded.DigestWasValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_RecomputesTheDigestAfterAnEdit()
    {
        string path = TempFile();
        try
        {
            var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
            book.Macros[0].Lines[0] = "/echo changed";
            MacroBookFile.Save(book, path);

            byte[] raw = File.ReadAllBytes(path);
            Assert.Equal(
                FfxiContainer.StoredDigest(raw),
                MD5.HashData(raw.AsSpan(FfxiContainer.HeaderSize)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_RejectsAFileOfTheWrongSize()
    {
        var ex = Assert.Throws<MacroFileException>(() => MacroBookFile.Read(new byte[7000]));
        Assert.Contains("7624", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToBytes_RejectsAnOverlongLine()
    {
        var book = new MacroBook();
        book.Macros[0].Lines[0] = new string('a', MacroBookFile.MaxLineBytes + 1);

        var ex = Assert.Throws<MacroFileException>(() => MacroBookFile.ToBytes(book));
        Assert.Contains("line 1 of macro Ctrl-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToBytes_RejectsAnOverlongName()
    {
        var book = new MacroBook();
        book.Macros[10].Name = "TooLongName";

        var ex = Assert.Throws<MacroFileException>(() => MacroBookFile.ToBytes(book));
        Assert.Contains("name of macro Alt-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToBytes_TruncatesCleanlyWhenAsked()
    {
        var book = new MacroBook();
        book.Macros[0].Name = "TooLongName";
        book.Macros[0].Lines[0] = new string('a', 100);

        var reloaded = MacroBookFile.Read(MacroBookFile.ToBytes(book, truncate: true));

        Assert.Equal("TooLongN", reloaded.Macros[0].Name);
        Assert.Equal(new string('a', MacroBookFile.MaxLineBytes), reloaded.Macros[0].Lines[0]);
    }

    [Fact]
    public void ToBytes_NeverSplitsAnAutoTranslatePhraseWhenTruncating()
    {
        var book = new MacroBook();
        // 57 characters, then a 6-byte phrase: the phrase does not fit in the remaining 3 bytes.
        book.Macros[0].Lines[0] = new string('x', 57) + "«02021F97»";

        var reloaded = MacroBookFile.Read(MacroBookFile.ToBytes(book, truncate: true));

        Assert.Equal(new string('x', 57), reloaded.Macros[0].Lines[0]);
    }

    [Fact]
    public void EmptyBook_RoundTripsToAllZeroData()
    {
        byte[] raw = MacroBookFile.ToBytes(new MacroBook());

        Assert.Equal(MacroBookFile.FileSize, raw.Length);
        Assert.True(MacroBookFile.Read(raw).IsEmpty);
        Assert.All(raw.Skip(FfxiContainer.HeaderSize), b => Assert.Equal(0, b));
    }

    [Fact]
    public void LayoutConstants_AddUpToTheOnDiskSizes()
    {
        Assert.Equal(380, MacroBookFile.MacroSize);
        Assert.Equal(7600, MacroBookFile.DataSize);
        Assert.Equal(7624, MacroBookFile.FileSize);
        Assert.Equal(60, MacroBookFile.MaxLineBytes);
        Assert.Equal(8, MacroBookFile.MaxNameBytes);
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"ffxi-macro-test-{Guid.NewGuid():N}.dat");
}
