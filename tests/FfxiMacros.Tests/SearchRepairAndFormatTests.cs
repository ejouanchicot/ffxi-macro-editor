using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Operations;
using FfxiMacros.Core.Serialization;
using Xunit;

namespace FfxiMacros.Tests;

public class MacroSearchTests : IDisposable
{
    private readonly TempUserFolder _temp = new();

    public MacroSearchTests()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
    }

    private MacroLibrary Library => MacroLibrary.Scan(_temp.UserFolder);

    [Fact]
    public void Search_FindsAPhraseInsideCommandLines()
    {
        var hits = MacroSearch.Search(Library, "smartbuff");

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(MacroSearchField.MacroLine, h.Field));
        Assert.Contains(hits, h => h.Text.Contains("/con gs c smartbuff", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_FindsMacroNames()
    {
        var hits = MacroSearch.Search(Library, "BuffSelf",
            new MacroSearchOptions { SearchLines = false, SearchTitles = false });

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(MacroSearchField.MacroName, h.Field));
    }

    [Fact]
    public void Search_FindsBookTitles()
    {
        var hits = MacroSearch.Search(Library, "ThfRdm",
            new MacroSearchOptions { SearchLines = false, SearchNames = false });

        var hit = Assert.Single(hits);
        Assert.Equal(MacroSearchField.BookTitle, hit.Field);
        Assert.Equal(1, hit.BookNumber);
    }

    [Fact]
    public void Search_IsCaseInsensitiveByDefault()
    {
        Assert.NotEmpty(MacroSearch.Search(Library, "SMARTBUFF"));
        Assert.Empty(MacroSearch.Search(Library, "SMARTBUFF", new MacroSearchOptions { MatchCase = true }));
    }

    [Fact]
    public void Search_ReportsAReadableLocation()
    {
        var hit = MacroSearch.Search(Library, "smartbuff")[0];

        Assert.Contains("Book 1", hit.Location, StringComparison.Ordinal);
        Assert.Contains("Set 1", hit.Location, StringComparison.Ordinal);
        Assert.Contains("Ctrl-1", hit.Location, StringComparison.Ordinal);
        Assert.Contains("ligne 1", hit.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_StopsAtTheHitLimit()
    {
        var hits = MacroSearch.Search(Library, "/", new MacroSearchOptions { MaxHits = 3 });

        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public void Search_CanBeLimitedToOneCharacter()
    {
        _temp.AddCharacter("bbbb2", 0);
        var library = MacroLibrary.Scan(_temp.UserFolder);

        var hits = MacroSearch.Search(library, "smartbuff",
            new MacroSearchOptions { OnlyCharacter = library.ById("bbbb2") });

        Assert.All(hits, h => Assert.Equal("bbbb2", h.Character.Id));
    }

    [Fact]
    public void Search_OfAnEmptyQueryFindsNothing()
    {
        Assert.Empty(MacroSearch.Search(Library, ""));
    }

    public void Dispose() => _temp.Dispose();
}

public class MacroRepairTests
{
    [Theory]
    [InlineData("{00}con send Sylvane Erase <laststid>", "/con send Sylvane Erase <laststid>")]
    [InlineData("{00}wait 1", "/wait 1")]
    [InlineData("/ma \"Dispel\" <t>{00}stid>", "/ma \"Dispel\" <t>")]
    [InlineData("{00}con send Sylvane /ma \"Dispel\" <laststid>{00}stid>", "/con send Sylvane /ma \"Dispel\" <laststid>")]
    public void TryRepair_RestoresWhatTheOldEditorBroke(string broken, string expected)
    {
        Assert.True(MacroRepair.TryRepair(broken, out string repaired, out string reason));
        Assert.Equal(expected, repaired);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/ma \"Cure IV\" <t>")]
    [InlineData("{AT:02021F01}")]
    [InlineData("{00}")]                 // nothing left once the NUL is gone
    public void TryRepair_LeavesHealthyTextAlone(string text)
    {
        Assert.False(MacroRepair.TryRepair(text, out _, out _));
    }

    [Theory]
    // The whole point: a longer word that merely starts the same must be left alone.
    [InlineData("/con send Kaorie Fira", "Kaorie", "Sylvane", "/con send Sylvane Fira")]
    [InlineData("/con send Sylvane Fira", "Kaorie", "Sylvane", "/con send Sylvane Fira")]
    [InlineData("/ma \"Addle II\" <lastid>", "<lastid>", "<laststid>", "/ma \"Addle II\" <laststid>")]
    [InlineData("/ma \"Addle II\" <laststid>", "<lastid>", "<laststid>", "/ma \"Addle II\" <laststid>")]
    [InlineData("Kaorie and Kaorie", "Kaorie", "Sylvane", "Sylvane and Sylvane")]
    [InlineData("/echo nothing here", "Kaorie", "Sylvane", "/echo nothing here")]
    public void Substitute_ReplacesWholeWordsOnly(string text, string search, string replacement, string expected)
    {
        Assert.Equal(expected, MacroRepair.Substitute(text, search, replacement));
    }

    [Fact]
    public void Substitute_DoesNotTreatTheReplacementAsARegexGroup()
    {
        Assert.Equal("cout: $1", MacroRepair.Substitute("cout: X", "X", "$1"));
    }

    [Fact]
    public void Inspect_FindsTheDamageInARealFile()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        var suggestions = MacroRepair.Inspect(book);

        Assert.NotEmpty(suggestions);
        var line = Assert.Single(suggestions, s => s.MacroIndex == 6 && s.LineIndex == 1);
        Assert.Equal("/con send Kaelith \"Healing Waltz\" <laststid>", line.After);
        Assert.Contains("Ctrl-7", line.Where, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_AlsoLooksAtMacroNames()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
        Assert.Equal("SA{00}se", book.Macros[6].Name);

        var suggestion = Assert.Single(MacroRepair.Inspect(book), s => s.LineIndex < 0 && s.MacroIndex == 6);

        Assert.Equal("SA", suggestion.After);
        Assert.Contains("nom", suggestion.Where, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_AppliesEverySuggestionAndTheResultIsClean()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        int changed = MacroRepair.Repair(book);

        Assert.True(changed > 0);
        Assert.Empty(MacroRepair.Inspect(book));
        Assert.Equal("/con send Kaelith \"Healing Waltz\" <laststid>", book.Macros[6].Lines[1]);
    }

    [Fact]
    public void ARepairedBookStillSavesToTheRightSize()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
        MacroRepair.Repair(book);

        Assert.Equal(MacroBookFile.FileSize, MacroBookFile.ToBytes(book).Length);
    }
}

public class MacroFormatTests
{
    [Fact]
    public void TextExport_IsReadable()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        string text = MacroTextFormat.Export(book, "book 1 (ThfRdm) / set 1");

        Assert.Contains("[Ctrl-1] BuffSelf", text, StringComparison.Ordinal);
        Assert.Contains("/con gs c smartbuff", text, StringComparison.Ordinal);
        Assert.Contains("# book 1 (ThfRdm) / set 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextExport_ThenImport_ReproducesTheSameBytes()
    {
        var original = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        var reimported = MacroTextFormat.Import(MacroTextFormat.Export(original));
        reimported.Version = original.Version;

        Assert.Equal(MacroBookFile.ToBytes(original), MacroBookFile.ToBytes(reimported));
    }

    [Fact]
    public void TextImport_MergesIntoAnExistingSet()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        MacroTextFormat.Import("[Alt-0] Ajout\n/echo ajouté\n", book);

        Assert.Equal("Ajout", book.Macros[19].Name);
        Assert.Equal("BuffSelf", book.Macros[0].Name);   // untouched
    }

    [Theory]
    [InlineData("[Ctrl-1", "matching ']'")]
    [InlineData("/echo orphan", "before any")]
    [InlineData("[Shift-1] x", "unknown palette")]
    [InlineData("[Ctrl-99] x", "not a key digit")]
    public void TextImport_ReportsMalformedInput(string text, string expected)
    {
        var ex = Assert.Throws<MacroFileException>(() => MacroTextFormat.Import(text));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextImport_RejectsAMacroWithMoreThanSixLines()
    {
        string text = "[Ctrl-1] Trop\n" + string.Join("\n", Enumerable.Range(1, 7).Select(i => $"/echo {i}"));

        var ex = Assert.Throws<MacroFileException>(() => MacroTextFormat.Import(text));
        Assert.Contains("has 7 lines, 6 maximum", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExport_ThenImport_ReproducesTheSameBytes()
    {
        var original = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        string json = MacroJsonFormat.Export(original, "a1b2c3d", 1, "ThfRdm");
        var reimported = MacroJsonFormat.Import(json);
        reimported.Version = original.Version;

        Assert.Equal(MacroBookFile.ToBytes(original), MacroBookFile.ToBytes(reimported));
    }

    [Fact]
    public void JsonExport_CarriesTheContextAndKeepsPhrasesReadable()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        string json = MacroJsonFormat.Export(book, "a1b2c3d", 1, "ThfRdm");
        var document = MacroJsonFormat.Parse(json);

        Assert.Equal("ffxi-macros", document.Format);
        Assert.Equal("a1b2c3d", document.Character);
        Assert.Equal(1, document.Book);
        Assert.Equal("ThfRdm", document.Title);
        Assert.Contains("Ctrl-1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);   // no escaped quotes soup
    }

    [Fact]
    public void JsonImport_RejectsSomethingElseEntirely()
    {
        var ex = Assert.Throws<MacroFileException>(() => MacroJsonFormat.Parse("""{"format":"other"}"""));
        Assert.Contains("not an FFXI macro export", ex.Message, StringComparison.Ordinal);

        Assert.Throws<MacroFileException>(() => MacroJsonFormat.Parse("{ broken"));
    }

    [Fact]
    public void JsonImport_RejectsAnOutOfRangeSlot()
    {
        string json = """{"format":"ffxi-macros","sets":[{"set":1,"macros":[{"index":42,"name":"x","lines":[]}]}]}""";

        var ex = Assert.Throws<MacroFileException>(() => MacroJsonFormat.Import(json));
        Assert.Contains("outside 0..19", ex.Message, StringComparison.Ordinal);
    }
}
