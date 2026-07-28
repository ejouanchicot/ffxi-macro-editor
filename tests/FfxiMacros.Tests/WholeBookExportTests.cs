using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Serialization;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Exporting a book rather than the one set on screen: what a player calls their macros for a job
/// is the ten sets, and leaving nine of them behind was the whole complaint.
/// </summary>
public class WholeBookExportTests
{
    private static MacroBook Set(string name, string line)
    {
        // Version 1 like a real file: the stamp belongs to the file, not to the export, so the
        // byte comparison below is about the twenty macros and nothing else.
        var book = new MacroBook { Version = 1 };
        book.Macros[0].Name = name;
        book.Macros[0].Lines[0] = line;
        return book;
    }

    private static readonly MacroSetExport[] ThreeSets =
    [
        new(1, Set("One", "/ma \"Cure\" <t>")),
        new(2, Set("Two", "/ja \"«02021F97»\" <me>")),
        new(5, Set("Five", "/con gs c idle")),
    ];

    // ---------------------------------------------------------------- text

    [Fact]
    public void EverySetIsWrittenUnderItsOwnHeader()
    {
        string text = MacroTextFormat.Export(ThreeSets, "char · book 3 (ThfRdm)");

        Assert.Contains("[Set 1]", text, StringComparison.Ordinal);
        Assert.Contains("[Set 2]", text, StringComparison.Ordinal);
        Assert.Contains("[Set 5]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Set 3]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSetsComeBackWithTheirNumbers()
    {
        var read = MacroTextFormat.ImportSets(MacroTextFormat.Export(ThreeSets));

        Assert.Equal([1, 2, 5], read.Select(s => s.SetNumber));
        Assert.Equal("One", read[0].Book.Macros[0].Name);
        Assert.Equal("Five", read[2].Book.Macros[0].Name);
    }

    [Fact]
    public void AWholeBookRoundTripsToTheByte()
    {
        var read = MacroTextFormat.ImportSets(MacroTextFormat.Export(ThreeSets));

        Assert.Equal(ThreeSets.Length, read.Count);
        for (int i = 0; i < ThreeSets.Length; i++)
            Assert.Equal(MacroBookFile.ToBytes(ThreeSets[i].Book), MacroBookFile.ToBytes(read[i].Book));
    }

    [Fact]
    public void ASingleSetFileStillHasNoHeaderAndReadsAsUnnumbered()
    {
        // What the editor wrote before this, and what one exported set still looks like. The 0 tells
        // the caller "wherever you were pointing", which is what keeps those files working.
        string text = MacroTextFormat.Export(Set("Solo", "/ma \"Cure\" <t>"));

        Assert.DoesNotContain("[Set ", text, StringComparison.Ordinal);

        var read = MacroTextFormat.ImportSets(text);
        Assert.Equal(0, Assert.Single(read).SetNumber);
        Assert.Equal("Solo", read[0].Book.Macros[0].Name);
    }

    [Fact]
    public void AnEmptySetOfTheBookIsMarkedRatherThanSilentlyDropped()
    {
        var sets = new MacroSetExport[] { new(1, Set("One", "/ma \"Cure\" <t>")), new(2, new MacroBook { Version = 1 }) };

        string text = MacroTextFormat.Export(sets);

        Assert.Contains("[Set 2]", text, StringComparison.Ordinal);
        Assert.Contains("# (empty)", text, StringComparison.Ordinal);
        Assert.Equal([1, 2], MacroTextFormat.ImportSets(text).Select(s => s.SetNumber));
    }

    [Theory]
    [InlineData("[Set 0]")]
    [InlineData("[Set 11]")]
    [InlineData("[Set three]")]
    public void AnImpossibleSetNumberIsRefusedWithItsLine(string header)
    {
        var ex = Assert.Throws<MacroFileException>(
            () => MacroTextFormat.ImportSets($"# FFXI macro set\n\n{header}\n\n[Ctrl-1] X\n/ma \"Cure\" <t>\n"));

        Assert.Contains("Line 3", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- json

    [Fact]
    public void TheJsonExportCarriesTheSameSets()
    {
        string json = MacroJsonFormat.Export(ThreeSets, "a1b2c3d", 3, "ThfRdm");

        var read = MacroJsonFormat.ImportSets(json);

        Assert.Equal([1, 2, 5], read.Select(s => s.SetNumber));
        for (int i = 0; i < ThreeSets.Length; i++)
            Assert.Equal(MacroBookFile.ToBytes(ThreeSets[i].Book), MacroBookFile.ToBytes(read[i].Book));
    }

    [Fact]
    public void TheJsonExportKeepsWhichBookItCameFrom()
    {
        string json = MacroJsonFormat.Export(ThreeSets, "a1b2c3d", 3, "ThfRdm");

        Assert.Contains("\"character\": \"a1b2c3d\"", json, StringComparison.Ordinal);
        Assert.Contains("\"book\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"ThfRdm\"", json, StringComparison.Ordinal);
    }
}
