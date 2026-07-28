using System.Text;
using FfxiMacros.Core.GameData;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Text;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Reading auto-translate names out of the game's own data files. The fixtures are built by hand in
/// the same layout as the real files, so these run without FFXI installed.
/// </summary>
public class GameDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ffxi-game-{Guid.NewGuid():N}");

    // ---------------------------------------------------------------- fixtures

    /// <summary>Writes VTABLE/FTABLE for the given ids, and returns the folder.</summary>
    private string BuildIndex(params (int Id, int Volume, int Directory, int File)[] files)
    {
        Directory.CreateDirectory(_root);
        int count = files.Max(f => f.Id) + 1;

        var volumes = new byte[count];
        var table = new byte[count * 2];
        foreach (var (id, volume, dir, file) in files)
        {
            volumes[id] = (byte)volume;
            int packed = (dir << 7) | file;
            table[id * 2] = (byte)packed;
            table[(id * 2) + 1] = (byte)(packed >> 8);
        }

        File.WriteAllBytes(Path.Combine(_root, FfxiDatIndex.VTableFileName), volumes);
        File.WriteAllBytes(Path.Combine(_root, FfxiDatIndex.FTableFileName), table);
        return _root;
    }

    private string DataPath(int volume, int dir, int file)
    {
        string folder = Path.Combine(_root, volume == 1 ? "ROM" : $"ROM{volume}", dir.ToString());
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{file}.DAT");
    }

    /// <summary>A fixed-size d_msg table: 64-byte header, 80-byte entries, text at +40.</summary>
    private static byte[] BuildDMsg(params string[] entries)
    {
        const int header = 64, entrySize = 80;
        var raw = new byte[header + (entries.Length * entrySize)];
        "d_msg"u8.CopyTo(raw);
        BitConverter.TryWriteBytes(raw.AsSpan(0x14), raw.Length);
        BitConverter.TryWriteBytes(raw.AsSpan(0x18), header);
        BitConverter.TryWriteBytes(raw.AsSpan(0x20), entrySize);
        BitConverter.TryWriteBytes(raw.AsSpan(0x24), entries.Length * entrySize);
        BitConverter.TryWriteBytes(raw.AsSpan(0x28), entries.Length);

        for (int i = 0; i < entries.Length; i++)
            Encoding.Latin1.GetBytes(entries[i]).CopyTo(raw.AsSpan(header + (i * entrySize) + 40));

        return raw;
    }

    /// <summary>
    /// An auto-translate dictionary: per group, a 76-byte header followed by one record per phrase,
    /// exactly as the game writes them.
    /// </summary>
    private static byte[] BuildDictionary(params (byte Group, string Name, string[] Phrases)[] groups)
    {
        var raw = new List<byte>();

        foreach (var (group, name, phrases) in groups)
        {
            var header = new byte[76];
            header[0] = 0x02;
            header[1] = 0x02;
            header[2] = group;
            header[3] = 0x00;
            Encoding.Latin1.GetBytes(name).CopyTo(header.AsSpan(4));
            raw.AddRange(header);

            for (int i = 0; i < phrases.Length; i++)
            {
                byte[] text = Encoding.Latin1.GetBytes(phrases[i]);
                raw.AddRange([0x02, 0x02, group, (byte)(i + 1), (byte)(text.Length + 1)]);
                raw.AddRange(text);
                raw.Add(0x00);
            }
        }

        return [.. raw];
    }

    /// <summary>
    /// A dictionary big enough to pass the loader's sanity check, with the caller's phrases in
    /// group 0x1F and filler elsewhere — the shape of the real file, in miniature.
    /// </summary>
    private static byte[] BuildRealisticDictionary(params string[] abilities)
    {
        var groups = new List<(byte, string, string[])>
        {
            (0x1F, "Job Abilities", abilities),
        };

        for (byte g = 1; g <= 5; g++)
            groups.Add((g, $"Group {g}", Enumerable.Range(1, 25).Select(i => $"Phrase {g}-{i}").ToArray()));

        return BuildDictionary([.. groups]);
    }

    // ---------------------------------------------------------------- the file index

    [Fact]
    public void TheIndexResolvesAnIdToItsRomPath()
    {
        BuildIndex((7, 1, 181, 72));
        File.WriteAllBytes(DataPath(1, 181, 72), [1, 2, 3]);

        var index = FfxiDatIndex.Load(_root);

        Assert.Equal(Path.Combine(_root, "ROM", "181", "72.DAT"), index.PathOf(7));
    }

    [Fact]
    public void TheIndexUsesTheVolumeNumberForTheFolderName()
    {
        BuildIndex((3, 4, 12, 5));
        File.WriteAllBytes(DataPath(4, 12, 5), [0]);

        Assert.Equal(Path.Combine(_root, "ROM4", "12", "5.DAT"), FfxiDatIndex.Load(_root).PathOf(3));
    }

    [Fact]
    public void AnUnusedOrMissingIdResolvesToNothing()
    {
        BuildIndex((7, 1, 181, 72));      // the file itself is never created

        var index = FfxiDatIndex.Load(_root);

        Assert.Null(index.PathOf(7));     // listed but absent from disk
        Assert.Null(index.PathOf(0));     // volume 0: unused
        Assert.Null(index.PathOf(9999));  // past the end of the table
    }

    [Fact]
    public void TheIndexRejectsTablesThatAreNotTwoToOne()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, FfxiDatIndex.VTableFileName), new byte[10]);
        File.WriteAllBytes(Path.Combine(_root, FfxiDatIndex.FTableFileName), new byte[15]);

        var ex = Assert.Throws<MacroFileException>(() => FfxiDatIndex.Load(_root));
        Assert.Contains("twice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstallIsRecognisedByItsTwoTables()
    {
        BuildIndex((1, 1, 0, 0));

        Assert.True(FfxiDatIndex.LooksLikeInstall(_root));
        Assert.Equal(_root, FfxiDatIndex.InstallRootFor(Path.Combine(_root, "USER")));
        Assert.Equal(_root, FfxiDatIndex.InstallRootFor(Path.Combine(_root, "USER") + Path.DirectorySeparatorChar));
        Assert.Null(FfxiDatIndex.InstallRootFor(Path.Combine(_root, "ROM", "USER")));
        Assert.Null(FfxiDatIndex.InstallRootFor(null));
    }

    // ---------------------------------------------------------------- the string tables

    [Fact]
    public void ADMsgTableReadsItsEntries()
    {
        var table = DMsgTable.TryRead(BuildDMsg("Provoke", "", "Shield Bash"));

        Assert.NotNull(table);
        Assert.Equal(3, table.Count);
        Assert.Equal("Provoke", table[0]);
        Assert.Equal("Shield Bash", table[2]);
        Assert.False(table.TryGet(1, out _));
        Assert.Equal("", table[99]);
    }

    [Fact]
    public void SomethingThatIsNotADMsgTableIsRejected()
    {
        Assert.Null(DMsgTable.TryRead(new byte[128]));
        Assert.Null(DMsgTable.TryRead("not a table"u8));
    }

    [Fact]
    public void ATruncatedDMsgTableIsRejectedRatherThanReadPastItsEnd()
    {
        byte[] raw = BuildDMsg("Provoke", "Flash");

        Assert.Null(DMsgTable.TryRead(raw.AsSpan(0, raw.Length - 40)));
    }

    // ---------------------------------------------------------------- the dictionary

    [Fact]
    public void TheDictionaryReadsPhrasesAndTheirIds()
    {
        var dat = AutoTranslateDat.TryRead(BuildDictionary((1, "Greetings", ["Hello", "Good bye."])));

        Assert.NotNull(dat);
        Assert.Equal("Greetings", dat.Groups[1]);
        Assert.Equal("Hello", dat.Phrases[0x0101]);        // group 1, index 1
        Assert.Equal("Good bye.", dat.Phrases[0x0102]);
    }

    [Fact]
    public void ThePhraseIdIsTheGroupAndIndexAMacroStores()
    {
        var dat = AutoTranslateDat.TryRead(BuildDictionary((0x1F, "Job Abilities", ["Provoke"])));

        // A macro carries FD 02 02 1F 01 FD, so the id it names is 0x1F01.
        Assert.Equal("Provoke", dat!.Phrases[0x1F01]);
    }

    [Fact]
    public void SomethingThatIsNotTheDictionaryIsRejected()
    {
        Assert.Null(AutoTranslateDat.TryRead("d_msg"u8));
        Assert.Null(AutoTranslateDat.TryRead(new byte[2]));
    }

    [Theory]
    [InlineData("@Y2", "Shield Bash")]      // index 2 of the ability table
    [InlineData("@C1", "Flash")]            // index 1 of the spell table
    [InlineData("Plain text", "Plain text")]
    public void AClientMarkerIsExpandedFromTheRightTable(string stored, string expected)
    {
        var abilities = DMsgTable.TryRead(BuildDMsg("a", "b", "Shield Bash"));
        var spells = DMsgTable.TryRead(BuildDMsg("x", "Flash"));

        Assert.Equal(expected, AutoTranslateDat.Resolve(stored, abilities, spells));
    }

    [Theory]
    [InlineData("@A12")]      // place names: a table this reader does not decode
    [InlineData("@J6")]       // job names: likewise
    [InlineData("@Y999")]     // past the end of the table
    [InlineData("@")]
    public void AMarkerThatCannotBeExpandedIsReportedAsSuch(string stored)
    {
        var abilities = DMsgTable.TryRead(BuildDMsg("a", "b", "c"));

        Assert.Null(AutoTranslateDat.Resolve(stored, abilities, null));
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public void TheWholeChainNamesAPhraseWithoutWindower()
    {
        // The dictionary, plus the two tables its markers point into, laid out like a real install.
        BuildIndex((100, 1, 168, 25), (101, 1, 181, 72), (102, 1, 181, 73));
        File.WriteAllBytes(DataPath(1, 168, 25), BuildRealisticDictionary("@Y2", "Sneak Attack"));
        File.WriteAllBytes(DataPath(1, 181, 72), BuildDMsg("a", "b", "Provoke"));
        File.WriteAllBytes(DataPath(1, 181, 73), BuildDMsg("Fire", "Flash"));

        var loaded = GameAutoTranslateLoader.TryLoad(_root);

        Assert.NotNull(loaded);
        Assert.Equal("Provoke", loaded.Phrases[0x1F01]);        // marker expanded
        Assert.Equal("Sneak Attack", loaded.Phrases[0x1F02]);   // stored as plain text
        Assert.Equal(0, loaded.Unresolved);
    }

    [Fact]
    public void AFolderThatIsNotAnInstallYieldsNothingRatherThanThrowing()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(GameAutoTranslateLoader.TryLoad(_root));
        Assert.Null(GameAutoTranslateLoader.TryLoad(Path.Combine(_root, "nowhere")));
    }

    [Fact]
    public void TheDictionaryFromTheGameFeedsTheTextCodec()
    {
        BuildIndex((100, 1, 168, 25), (101, 1, 181, 72), (102, 1, 181, 73));
        File.WriteAllBytes(DataPath(1, 168, 25), BuildRealisticDictionary("@Y2"));
        File.WriteAllBytes(DataPath(1, 181, 72), BuildDMsg("a", "b", "Provoke"));
        File.WriteAllBytes(DataPath(1, 181, 73), BuildDMsg("Fire"));

        var dictionary = AutoTranslateDictionary.LoadFromGame(_root);

        Assert.Equal("«Provoke»", FfxiText.Decode(FfxiText.Encode("{AT:02021F01}", 61), dictionary));
        Assert.True(dictionary.TryGetPayload("Provoke", AutoTranslateDictionary.PhraseCategory, out byte[] payload));
        Assert.Equal(new byte[] { 0x02, 0x02, 0x1F, 0x01 }, payload);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
