using FfxiMacros.Core.Text;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Covers naming auto-translate phrases. Each test builds its own tiny resource folder, so nothing
/// depends on a Windower install being present.
/// </summary>
public class AutoTranslateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ffxi-at-{Guid.NewGuid():N}");

    private const byte Phrase = AutoTranslateDictionary.PhraseCategory;
    private const byte Item = AutoTranslateDictionary.ItemCategory;

    private AutoTranslateDictionary Build(string? phrases = null, string? items = null)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "auto_translates.lua"), phrases ?? """
            return {
                [7937] = {id=7937,en="Provoke",ja="挑発"},
                [7943] = {id=7943,en="Shield Bash",ja="シールドバッシュ"},
                [8156] = {id=8156,en="Vallation",ja="ヴァレション"},
                [8178] = {id=8178,en="Vallation",ja="ヴァレション"},
                [5102] = {id=5102,en="Foil",ja="フォイル"},
            }
            """);

        if (items is not null)
            File.WriteAllText(Path.Combine(_folder, "items.lua"), items);

        return AutoTranslateDictionary.LoadFromWindower(_folder);
    }

    // ---------------------------------------------------------------- the dictionary

    [Fact]
    public void LoadFromWindower_ReadsTheEnglishNames()
    {
        var dictionary = Build();

        Assert.Equal(5, dictionary.Count);
        Assert.True(dictionary.TryGetName([Phrase, 0x02, 0x1F, 0x01], out string name));
        Assert.Equal("Provoke", name);
    }

    [Fact]
    public void LoadFromWindower_OfAMissingFolderYieldsAnEmptyDictionary()
    {
        var dictionary = AutoTranslateDictionary.LoadFromWindower(Path.Combine(_folder, "nope"));

        Assert.True(dictionary.IsEmpty);
        Assert.Null(dictionary.SourceDescription);
    }

    [Fact]
    public void ANameUsedTwiceInOneTableResolvesToItsFirstId()
    {
        var dictionary = Build();

        Assert.True(dictionary.IsAmbiguous("Vallation", Phrase));
        Assert.True(dictionary.TryGetPayload("Vallation", Phrase, out byte[] payload));

        // 8156 is 0x1FDC, the first of the two — not 8178.
        Assert.Equal([Phrase, 0x02, 0x1F, 0xDC], payload);
        Assert.True(dictionary.TryGetPayload("Provoke", Phrase, out _));
    }

    [Fact]
    public void TheSameNameInTwoTablesDoesNotShadowItself()
    {
        // "Foil" is both a spell and a scroll; each table keeps its own entry.
        var dictionary = Build(items: """
            return { [5102] = {id=5102,en="Foil",ja="フォイル",category="Usable"} }
            """);

        Assert.True(dictionary.TryGetPayload("Foil", Phrase, out byte[] spell));
        Assert.True(dictionary.TryGetPayload("Foil", Item, out byte[] scroll));
        Assert.Equal(Phrase, spell[0]);
        Assert.Equal(Item, scroll[0]);
    }

    // ---------------------------------------------------------------- decoding

    [Fact]
    public void APhraseIsDecodedToItsName()
    {
        byte[] field = FfxiText.Encode("/ja \"{AT:02021F01}\" <t>", 61);

        Assert.Equal("/ja \"«Provoke»\" <t>", FfxiText.Decode(field, Build()));
    }

    [Fact]
    public void ANameContainingASpaceIsDecodedInOnePiece()
    {
        string text = FfxiText.Decode(FfxiText.Encode("{AT:02021F07}", 61), Build());

        Assert.Equal("«Shield Bash»", text);
    }

    [Fact]
    public void AnAmbiguousNameKeepsItsIdSoTheBytesSurvive()
    {
        var dictionary = Build();

        string text = FfxiText.Decode(FfxiText.Encode("{AT:02021FF2}", 61), dictionary);

        Assert.Equal("«Vallation#1FF2»", text);
    }

    [Fact]
    public void AnItemPhraseIsMarkedAsSuch()
    {
        var dictionary = Build(items: """
            return { [2490] = {id=2490,en="Forbidden Key",ja="禁断の宝のカギ"} }
            """);

        string text = FfxiText.Decode(FfxiText.Encode("{AT:070209BA}", 61), dictionary);

        Assert.Equal("«item Forbidden Key»", text);
    }

    [Fact]
    public void AnUnknownPhraseStaysInHexForm()
    {
        var dictionary = Build();

        Assert.Equal("«0202FFFF»", FfxiText.Decode(FfxiText.Encode("{AT:0202FFFF}", 61), dictionary));
    }

    [Fact]
    public void WithNoDictionaryEveryPhraseStaysInHexForm()
    {
        Assert.Equal(
            "«02021F01»",
            FfxiText.Decode(FfxiText.Encode("{AT:02021F01}", 61), AutoTranslateDictionary.Empty));
    }

    // ---------------------------------------------------------------- round trip

    [Theory]
    [InlineData("{AT:02021F01}")]     // Provoke, a unique name
    [InlineData("{AT:02021F07}")]     // Shield Bash, a name with a space
    [InlineData("{AT:02021FF2}")]     // Vallation, ambiguous
    [InlineData("{AT:02028156}")]     // an id absent from the dictionary
    [InlineData("/ja \"{AT:02021F01}\" <t>")]
    public void DecodingThenEncodingReproducesTheOriginalBytes(string hexForm)
    {
        var dictionary = Build();
        byte[] original = FfxiText.Encode(hexForm, 61);

        string readable = FfxiText.Decode(original, dictionary);

        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = dictionary;
            Assert.Equal(original, FfxiText.Encode(readable, 61));
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void EncodingAnUnknownNameIsReportedClearly()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = Build();
            var ex = Assert.Throws<FfxiTextException>(() => FfxiText.Encode("{AT:Nonsense}", 61));
            Assert.Contains("Unknown auto-translate phrase", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void OnlyTheLaterOfTwoIdenticalNamesCarriesItsId()
    {
        var dictionary = Build();

        // Writing the id on every reused name filled real macros with "«Animated Flourish#1F9E»".
        // The first id reads plainly; only the ones it would not round-trip to keep the suffix.
        Assert.Equal("«Vallation»", FfxiText.Decode(FfxiText.Encode("{AT:02021FDC}", 61), dictionary));
        Assert.Equal("«Vallation#1FF2»", FfxiText.Decode(FfxiText.Encode("{AT:02021FF2}", 61), dictionary));
    }

    [Fact]
    public void TypingAReusedNameTakesItsFirstId()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = Build();

            byte[] field = FfxiText.Encode("{AT:Vallation}", 61);

            Assert.Equal([0xFD, Phrase, 0x02, 0x1F, 0xDC, 0xFD], field[..6]);
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void APhraseNameStillCountsAsSixBytes()
    {
        var previous = FfxiText.DefaultAutoTranslate;
        try
        {
            FfxiText.DefaultAutoTranslate = Build();
            Assert.Equal(6, FfxiText.MeasureBytes("«Provoke»"));
            Assert.Equal(6, FfxiText.MeasureBytes("«Vallation#1FF2»"));
        }
        finally
        {
            FfxiText.DefaultAutoTranslate = previous;
        }
    }

    public void Dispose()
    {
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
