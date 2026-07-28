using FfxiMacros.App.ViewModels;
using FfxiMacros.Core.Settings;
using FfxiMacros.Core.Text;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The phrase picker: type a few letters, get the matching auto-translate phrases, drop one into
/// the line being edited.
/// </summary>
public class PhrasePickerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ffxi-pick-{Guid.NewGuid():N}");
    private readonly TempUserFolder _temp = new();

    private AutoTranslateDictionary BuildDictionary()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "auto_translates.lua"), """
            return {
                [7937] = {id=7937,en="Provoke",ja="x"},
                [7938] = {id=7938,en="Mighty Strikes",ja="x"},
                [7939] = {id=7939,en="Berserk",ja="x"},
                [7940] = {id=7940,en="Aggressor",ja="x"},
                [7941] = {id=7941,en="Warcry",ja="x"},
                [7942] = {id=7942,en="Restraint",ja="x"},
            }
            """);
        File.WriteAllText(Path.Combine(_folder, "items.lua"), """
            return { [1234] = {id=1234,en="Mighty Pinion",ja="x"} }
            """);

        return AutoTranslateDictionary.LoadFromWindower(_folder);
    }

    private MainWindowViewModel NewViewModel()
    {
        _temp.AddCharacter("aaaa1", 0);
        var viewModel = new MainWindowViewModel(new EditorSettings
        {
            UserFolder = _temp.UserFolder,
            BackupBeforeSave = false,
        })
        {
            ProbeRunningClients = () => [],
        };
        viewModel.Initialize();
        return viewModel;
    }

    // ---------------------------------------------------------------- searching

    [Fact]
    public void TypingAFewLettersFindsThePhrase()
    {
        var found = BuildDictionary().Search("mighty", includeItems: false);

        Assert.Equal("Mighty Strikes", Assert.Single(found).Name);
        Assert.Equal("{AT:Mighty Strikes}", found[0].Escape);
    }

    [Fact]
    public void TheSearchIgnoresCaseAndMatchesInsideANameToo()
    {
        var dictionary = BuildDictionary();

        Assert.Contains(dictionary.Search("PROVOKE"), m => m.Name == "Provoke");
        Assert.Contains(dictionary.Search("strikes"), m => m.Name == "Mighty Strikes");
    }

    [Fact]
    public void NamesThatStartWithTheQueryComeFirst()
    {
        var dictionary = BuildDictionary();

        // "Warcry" starts with it; "Mighty Strikes" only contains the "r".
        var found = dictionary.Search("war", includeItems: false);

        Assert.Equal("Warcry", found[0].Name);
    }

    [Fact]
    public void ItemsAreSearchedOnlyWhenAskedFor()
    {
        var dictionary = BuildDictionary();

        Assert.DoesNotContain(dictionary.Search("mighty", includeItems: false), m => m.IsItem);

        var withItems = dictionary.Search("mighty", includeItems: true);
        var item = Assert.Single(withItems, m => m.IsItem);
        Assert.Equal("Mighty Pinion", item.Name);
        Assert.Equal("{AT:item Mighty Pinion}", item.Escape);
    }

    [Fact]
    public void PhrasesComeBeforeItems()
    {
        var found = BuildDictionary().Search("mighty", includeItems: true);

        Assert.False(found[0].IsItem);
    }

    [Fact]
    public void AnEmptyQueryReturnsNothing()
    {
        Assert.Empty(BuildDictionary().Search("   "));
        Assert.Empty(BuildDictionary().Search(""));
    }

    [Fact]
    public void TheResultCountIsCapped()
    {
        Assert.True(BuildDictionary().Search("r", max: 2).Count <= 2);
    }

    // ---------------------------------------------------------------- the panel

    [Fact]
    public void TypingInThePanelNarrowsTheList()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = BuildDictionary();
            var viewModel = NewViewModel();

            viewModel.PhraseQuery = "mighty";

            Assert.Equal("Mighty Strikes", viewModel.PhraseResults[0].Name);
            Assert.Contains("phrase", viewModel.PhraseSummary, StringComparison.Ordinal);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void PickingAPhraseSendsItsEscapeToTheFieldBeingEdited()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = BuildDictionary();
            var viewModel = NewViewModel();
            string? inserted = null;
            viewModel.InsertIntoFocusedField = text => { inserted = text; return true; };

            viewModel.PhraseQuery = "provoke";
            viewModel.InsertPhraseCommand.Execute(viewModel.PhraseResults[0]);

            Assert.Equal("{AT:Provoke}", inserted);
            Assert.False(viewModel.StatusIsError);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void PickingWithNoFieldFocusedSaysWhatToDo()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = BuildDictionary();
            var viewModel = NewViewModel();
            viewModel.InsertIntoFocusedField = _ => false;

            viewModel.PhraseQuery = "provoke";
            viewModel.InsertPhraseCommand.Execute(viewModel.PhraseResults[0]);

            Assert.True(viewModel.StatusIsError);
            Assert.Contains("Click in the line", viewModel.Status, StringComparison.Ordinal);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void AnInsertedPhraseEncodesToTheSixBytesTheGameUses()
    {
        var dictionary = BuildDictionary();
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = dictionary;
            var phrase = dictionary.Search("mighty", includeItems: false)[0];

            byte[] field = FfxiText.Encode($"/ja \"{phrase.Escape}\" <me>", 61);

            int start = Array.IndexOf(field, (byte)0xFD);
            Assert.True(start >= 0);
            Assert.Equal(0xFD, field[start + 5]);
            Assert.Equal($"/ja \"«{phrase.Name}»\" <me>", FfxiText.Decode(field, dictionary));
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void ThePanelTogglesAndReportsAMissingDictionary()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = AutoTranslateDictionary.Empty;
            var viewModel = NewViewModel();

            viewModel.TogglePhrasesCommand.Execute(null);

            Assert.True(viewModel.PhrasePanelOpen);
            Assert.False(viewModel.HasPhraseDictionary);
            Assert.True(viewModel.StatusIsError);

            viewModel.TogglePhrasesCommand.Execute(null);
            Assert.False(viewModel.PhrasePanelOpen);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    public void Dispose()
    {
        _temp.Dispose();
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
