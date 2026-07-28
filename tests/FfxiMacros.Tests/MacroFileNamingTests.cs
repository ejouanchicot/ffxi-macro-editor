using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using Xunit;

namespace FfxiMacros.Tests;

public class MacroFileNamingTests
{
    [Theory]
    [InlineData(0, "mcr.dat")]
    [InlineData(1, "mcr1.dat")]
    [InlineData(10, "mcr10.dat")]
    [InlineData(399, "mcr399.dat")]
    public void FileName_MatchesWhatTheGameWrites(int index, string expected)
    {
        Assert.Equal(expected, MacroFileNaming.FileName(index));
    }

    [Theory]
    [InlineData("mcr.dat", 0)]
    [InlineData("mcr1.dat", 1)]
    [InlineData("MCR199.DAT", 199)]
    [InlineData("mcr399.dat", 399)]
    public void TryParseFileName_AcceptsGameFiles(string name, int expected)
    {
        Assert.True(MacroFileNaming.TryParseFileName(name, out int index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("mcr.sys")]
    [InlineData("mcr.ttl")]
    [InlineData("mcr_2.ttl")]
    [InlineData("mcrx.dat")]
    [InlineData("mcr07.dat")]      // the game never zero-pads
    [InlineData("mcr400.dat")]     // past the last book
    [InlineData("mcr-1.dat")]
    [InlineData("cmb0.dat")]
    [InlineData("")]
    public void TryParseFileName_RejectsEverythingElse(string name)
    {
        Assert.False(MacroFileNaming.TryParseFileName(name, out _));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(9, 1, 10)]
    [InlineData(10, 2, 1)]
    [InlineData(149, 15, 10)]
    [InlineData(399, 40, 10)]
    public void FileIndex_MapsToBookAndSet(int index, int book, int set)
    {
        Assert.Equal(book, MacroFileNaming.BookOf(index));
        Assert.Equal(set, MacroFileNaming.SetOf(index));
        Assert.Equal(index, MacroFileNaming.FileIndex(book, set));
    }

    [Fact]
    public void EveryFileIndexRoundTripsThroughItsFileName()
    {
        for (int i = 0; i < MacroFileNaming.FileCount; i++)
        {
            Assert.True(MacroFileNaming.TryParseFileName(MacroFileNaming.FileName(i), out int parsed));
            Assert.Equal(i, parsed);
        }
    }

    [Fact]
    public void FileIndex_RejectsOutOfRangeCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroFileNaming.FileIndex(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroFileNaming.FileIndex(41, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroFileNaming.FileIndex(1, 11));
    }

    [Theory]
    [InlineData(0, "Ctrl-1")]
    [InlineData(8, "Ctrl-9")]
    [InlineData(9, "Ctrl-0")]
    [InlineData(10, "Alt-1")]
    [InlineData(19, "Alt-0")]
    public void MacroSlot_DescribesTheKeyPalette(int index, string expected)
    {
        Assert.Equal(expected, MacroSlot.Describe(index));
    }
}
