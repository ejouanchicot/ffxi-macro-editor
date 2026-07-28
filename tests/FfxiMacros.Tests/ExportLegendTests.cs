using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Serialization;
using FfxiMacros.Core.Text;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The text export is meant to be read by a person, so the three notations it uses explain
/// themselves — but only the ones a given file actually contains.
/// </summary>
public class ExportLegendTests
{
    private static MacroBook Book(Action<MacroBook> fill)
    {
        var book = new MacroBook();
        fill(book);
        return book;
    }

    [Fact]
    public void APlainSetGetsNoLegendAtAll()
    {
        var book = Book(b =>
        {
            b.Macros[0].Name = "Cure";
            b.Macros[0].Lines[0] = "/ma \"Cure IV\" <t>";
        });

        string text = MacroTextFormat.Export(book);

        Assert.DoesNotContain("auto-translate phrase", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stops reading", text, StringComparison.Ordinal);
        Assert.DoesNotContain("spacing counts", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APhraseIsExplained()
    {
        var book = Book(b =>
        {
            b.Macros[0].Name = "Prov";
            b.Macros[0].Lines[0] = "/ja \"«02021F97»\" <t>";
        });

        string text = MacroTextFormat.Export(book);

        Assert.Contains("an auto-translate phrase", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stops reading", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineTheGameIgnoresIsExplained()
    {
        // These read as empty in the editor, because that is what the game makes of them. The export
        // has to carry the bytes, so the line reappears and needs saying.
        var book = Book(b =>
        {
            b.Macros[0].Name = "SA";
            b.Macros[0].Lines[0] = "{00}con send Someone Erase <laststid>";
        });

        string text = MacroTextFormat.Export(book);

        Assert.Contains("a byte the game stops reading at", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuotedNameIsExplained()
    {
        var book = Book(b =>
        {
            b.Macros[0].Name = "Box ";
            b.Macros[0].Lines[0] = "/ja \"Box Step\" <t>";
        });

        string text = MacroTextFormat.Export(book);

        Assert.Contains("a name whose spacing counts", text, StringComparison.Ordinal);
        Assert.Contains("[Ctrl-1] \"Box \"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLegendDoesNotDisturbTheRoundTrip()
    {
        // The whole point of the export is that it comes back identical. A legend that the parser
        // choked on, or counted as a macro line, would be worse than no legend.
        var book = Book(b =>
        {
            b.Macros[0].Name = "Box ";
            b.Macros[0].Lines[0] = "/ja \"«02021F97»\" <t>";
            b.Macros[0].Lines[2] = "{00}con send Someone Erase <laststid>";
            b.Macros[5].Name = "Cure";
            b.Macros[5].Lines[0] = "/ma \"Cure IV\" <t>";
        });

        string text = MacroTextFormat.Export(book);
        var reloaded = new MacroBook();
        MacroTextFormat.Import(text, reloaded);

        Assert.Equal(MacroBookFile.ToBytes(book), MacroBookFile.ToBytes(reloaded));
    }

    [Fact]
    public void TheLegendSitsInTheHeaderNotAmongTheMacros()
    {
        var book = Book(b =>
        {
            b.Macros[0].Name = "Prov";
            b.Macros[0].Lines[0] = "/ja \"«02021F97»\" <t>";
        });

        string text = MacroTextFormat.Export(book);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        int legend = lines.FindIndex(l => l.Contains("auto-translate phrase", StringComparison.Ordinal));
        int firstSlot = lines.FindIndex(l => l.StartsWith('['));

        Assert.True(legend >= 0 && firstSlot > legend, "the legend must come before the first macro");
        Assert.All(lines.Take(firstSlot).Where(l => l.Length > 0), l => Assert.StartsWith("#", l, StringComparison.Ordinal));
    }
}
