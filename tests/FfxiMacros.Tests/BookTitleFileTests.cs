using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;
using Xunit;

namespace FfxiMacros.Tests;

public class BookTitleFileTests
{
    [Theory]
    [MemberData(nameof(SampleFiles.TitleFiles), MemberType = typeof(SampleFiles))]
    public void RoundTrip_ProducesAByteIdenticalFile(string fileName)
    {
        byte[] original = File.ReadAllBytes(SampleFiles.Path_(fileName));

        byte[] rewritten = BookTitleSet.Read(original).ToBytes();

        Assert.Equal(BookTitleSet.FileSize, rewritten.Length);
        Assert.True(
            original.AsSpan().SequenceEqual(rewritten),
            HexDump.Diff(original, rewritten, "original", "rewritten"));
    }

    [Fact]
    public void Load_ReadsTheTwentyTitlesOfTheFirstHalf()
    {
        var set = BookTitleSet.Load(SampleFiles.Path_("mcr.ttl"));

        Assert.False(set.IsSecondary);
        Assert.Equal(20, set.Titles.Length);
        Assert.Equal("ThfRdm", set.Titles[0]);
        Assert.Equal("PldBluC", set.Titles[19]);
        Assert.Equal(1, set.BookNumberAt(0));
        Assert.Equal(20, set.BookNumberAt(19));
    }

    [Fact]
    public void Load_MapsTheSecondFileOntoBooks21To40()
    {
        var set = BookTitleSet.Load(SampleFiles.Path_("mcr_2.ttl"));

        Assert.True(set.IsSecondary);
        Assert.Equal("PldRR", set.Titles[0]);
        Assert.Equal(21, set.BookNumberAt(0));
        Assert.Equal(40, set.BookNumberAt(19));
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheEdit()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ffxi-title-test-{Guid.NewGuid():N}.ttl");
        try
        {
            var set = BookTitleSet.Load(SampleFiles.Path_("mcr.ttl"));
            set.Titles[4] = "WhmSch";
            set.Save(path);

            Assert.Equal(BookTitleSet.FileSize, new FileInfo(path).Length);

            var reloaded = BookTitleSet.Load(path);
            Assert.Equal("WhmSch", reloaded.Titles[4]);
            Assert.True(reloaded.DigestWasValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_RejectsAFileOfTheWrongSize()
    {
        Assert.Throws<MacroFileException>(() => BookTitleSet.Read(new byte[300]));
    }

    [Fact]
    public void ToBytes_RejectsAnOverlongTitle()
    {
        var set = new BookTitleSet();
        set.Titles[2] = new string('t', BookTitleSet.MaxTitleBytes + 1);

        var ex = Assert.Throws<MacroFileException>(() => set.ToBytes());
        Assert.Contains("book 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultTitle_MatchesWhatTheGameWrites()
    {
        Assert.Equal("Book01", BookTitleSet.DefaultTitle(1));
        Assert.Equal("Book40", BookTitleSet.DefaultTitle(40));
    }
}
