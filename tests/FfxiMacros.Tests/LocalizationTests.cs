using System.Text.RegularExpressions;
using FfxiMacros.App.Localization;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The two languages have to stay in step. A missing key shows up as a raw <c>Status.Saved</c> in
/// the status bar, and a placeholder that drifted throws at the moment the message is formatted —
/// both of which are the kind of thing nobody notices until a user hits it.
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly Language _original = Loc.Current;

    private static IReadOnlyDictionary<string, string> English => Loc.TableFor(Language.English);

    private static IReadOnlyDictionary<string, string> French => Loc.TableFor(Language.French);

    // ---------------------------------------------------------------- the two tables agree

    [Fact]
    public void EveryEnglishKeyIsTranslated()
    {
        var missing = English.Keys.Where(key => !French.ContainsKey(key)).Order().ToList();

        Assert.True(missing.Count == 0, $"Not translated into French: {string.Join(", ", missing)}");
    }

    [Fact]
    public void NoFrenchKeyIsLeftOver()
    {
        var extra = French.Keys.Where(key => !English.ContainsKey(key)).Order().ToList();

        Assert.True(extra.Count == 0, $"French keys with no English original: {string.Join(", ", extra)}");
    }

    [Fact]
    public void ThePlaceholdersMatchInBothLanguages()
    {
        foreach (var (key, english) in English)
        {
            // Same set, not the same order: a translation is free to reorder {0} and {1}.
            Assert.Equal(Placeholders(english), Placeholders(French[key]));
        }
    }

    [Fact]
    public void NothingIsBlank()
    {
        foreach (var (key, text) in English)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is blank in English");
            Assert.False(string.IsNullOrWhiteSpace(French[key]), $"{key} is blank in French");
        }
    }

    [Fact]
    public void NoTranslationWasLeftAsACopyOfTheEnglish()
    {
        // Words that are the same in both languages are fine — "Phrases", "BOOKS", "Ctrl+S". A long
        // sentence sharing every word is not a translation, it is a line someone forgot.
        var untranslated = English
            .Where(entry => entry.Value.Length > 40 && string.Equals(entry.Value, French[entry.Key], StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(untranslated.Count == 0, $"Still in English: {string.Join(", ", untranslated)}");
    }

    // ---------------------------------------------------------------- lookup

    [Fact]
    public void TheDefaultLanguageIsEnglish()
    {
        Assert.Equal(Language.English, Loc.Parse(null));
        Assert.Equal(Language.English, Loc.Parse(""));
        Assert.Equal(Language.English, Loc.Parse("de"));      // a language we do not have
        Assert.Equal(Language.English, Loc.Parse("en"));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("FR")]
    [InlineData("  fr  ")]
    public void AStoredFrenchCodeIsRecognised(string code) => Assert.Equal(Language.French, Loc.Parse(code));

    [Fact]
    public void TheCodeRoundTripsThroughTheSettingsFile()
    {
        Assert.Equal(Language.French, Loc.Parse(Loc.CodeOf(Language.French)));
        Assert.Equal(Language.English, Loc.Parse(Loc.CodeOf(Language.English)));
    }

    [Fact]
    public void LookingUpTextFollowsTheCurrentLanguage()
    {
        Loc.Apply(Language.English);
        Assert.Equal("Save", Loc.T("Set.Save"));

        Loc.Apply(Language.French);
        Assert.Equal("Enregistrer", Loc.T("Set.Save"));
    }

    [Fact]
    public void ArgumentsAreFilledIn()
    {
        Loc.Apply(Language.English);

        Assert.Equal("Set 4", Loc.T("Tree.Set", 4));
        Assert.Equal("mcr140.dat opened.", Loc.T("Status.Opened", "mcr140.dat"));
    }

    [Fact]
    public void AnUnknownKeyReadsAsItself()
    {
        // Visible in the interface, and impossible to mistake for a real label — better than a
        // blank status bar or an exception when a key gets renamed on one side only.
        Assert.Equal("Nope.NotAKey", Loc.T("Nope.NotAKey"));
    }

    [Fact]
    public void SwitchingLanguageRaisesTheChangedEvent()
    {
        Loc.Apply(Language.English);
        int raised = 0;
        void Count() => raised++;

        Loc.Changed += Count;
        try
        {
            Loc.Apply(Language.French);
            Loc.Apply(Language.English);
        }
        finally
        {
            Loc.Changed -= Count;
        }

        Assert.Equal(2, raised);
    }

    private static IReadOnlySet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    public void Dispose() => Loc.Apply(_original);
}
