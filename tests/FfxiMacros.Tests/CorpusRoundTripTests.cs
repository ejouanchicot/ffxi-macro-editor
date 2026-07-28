using System.Text;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Sweeps a whole real <c>USER</c> folder instead of the handful of committed samples.
/// Point <c>FFXI_USER_DIR</c> at one to run it; the test is skipped when the variable is unset,
/// so the suite stays green on a machine without the game installed.
/// </summary>
public class CorpusRoundTripTests
{
    private static string? CorpusDirectory
    {
        get
        {
            string? dir = Environment.GetEnvironmentVariable("FFXI_USER_DIR");
            return string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) ? null : dir;
        }
    }

    [Fact]
    public void EveryMacroBookInTheCorpusRoundTripsExactly()
    {
        string? root = CorpusDirectory;
        if (root is null)
            return;   // No corpus on this machine: set FFXI_USER_DIR to a real USER folder to run this.

        var failures = new StringBuilder();
        int checkedFiles = 0;

        foreach (string path in Directory.EnumerateFiles(root!, MacroFileNaming.SearchPattern, SearchOption.AllDirectories))
        {
            if (!MacroFileNaming.TryParseFileName(path, out _))
                continue;

            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);

            try
            {
                byte[] rewritten = MacroBookFile.ToBytes(MacroBookFile.Read(original));
                if (!original.AsSpan().SequenceEqual(rewritten))
                    failures.AppendLine(path).AppendLine(HexDump.Diff(original, rewritten, "original", "rewritten", 8));
            }
            catch (MacroFileException ex)
            {
                failures.AppendLine($"{path}: {ex.Message}");
            }
        }

        Assert.True(checkedFiles > 0, $"No {MacroFileNaming.SearchPattern} files found under {root}.");
        Assert.True(failures.Length == 0, $"{checkedFiles} files checked:\n{failures}");
    }

    /// <summary>
    /// The whole point of reading the game's own data: names without Windower, and a round trip
    /// that stays byte-exact with that dictionary in place.
    /// </summary>
    [Fact]
    public void TheGameSuppliesItsOwnAutoTranslateNames()
    {
        string? install = Core.GameData.FfxiDatIndex.InstallRootFor(CorpusDirectory);
        if (install is null)
            return;   // No FFXI install next to the corpus.

        var dictionary = Core.Text.AutoTranslateDictionary.LoadFromGame(install);

        Assert.False(dictionary.IsEmpty);
        Assert.True(dictionary.Count > 2000, $"only {dictionary.Count} phrase(s) read from the game.");

        // Phrases taken from the reference macros, named by the game alone.
        Assert.True(dictionary.TryGetName([0x02, 0x02, 0x1F, 0x01], out string provoke));
        Assert.Equal("Provoke", provoke);
        Assert.True(dictionary.TryGetName([0x02, 0x02, 0x1F, 0x07], out string shieldBash));
        Assert.Equal("Shield Bash", shieldBash);
        Assert.True(dictionary.TryGetName([0x02, 0x02, 0x1B, 0x74], out string flash));
        Assert.Equal("Flash", flash);

        var failures = new StringBuilder();
        int checkedFiles = 0;
        int named = 0;

        var previous = Core.Text.FfxiText.DefaultAutoTranslate;
        try
        {
            Core.Text.FfxiText.DefaultAutoTranslate = dictionary;

            foreach (string path in Directory.EnumerateFiles(CorpusDirectory!, MacroFileNaming.SearchPattern, SearchOption.AllDirectories))
            {
                if (!MacroFileNaming.TryParseFileName(path, out _))
                    continue;

                checkedFiles++;
                byte[] original = File.ReadAllBytes(path);
                var book = MacroBookFile.Read(original);

                named += book.Macros.Sum(m => m.Lines.Count(l => l.Contains('«', StringComparison.Ordinal)));

                if (!original.AsSpan().SequenceEqual(MacroBookFile.ToBytes(book)))
                    failures.AppendLine(path);
            }
        }
        finally
        {
            Core.Text.FfxiText.DefaultAutoTranslate = previous;
        }

        Assert.True(checkedFiles > 0);
        Assert.True(named > 0, "no auto-translate phrase was rendered.");
        Assert.True(failures.Length == 0, $"{checkedFiles} files checked with the game dictionary:\n{failures}");
    }

    /// <summary>
    /// Typing a phrase by name must produce the very bytes FFXI stores for an auto-translate
    /// phrase — otherwise the game would show plain text where the player expects a phrase.
    /// </summary>
    [Fact]
    public void TypingAPhraseNameProducesTheBytesTheGameUses()
    {
        string? install = Core.GameData.FfxiDatIndex.InstallRootFor(CorpusDirectory);
        if (install is null)
            return;

        var dictionary = Core.Text.AutoTranslateDictionary.LoadFromGame(install);
        var previous = Core.Text.FfxiText.DefaultAutoTranslate;
        try
        {
            Core.Text.FfxiText.DefaultAutoTranslate = dictionary;

            foreach (string phrase in new[] { "Berserk", "Box Step", "Provoke", "Haste Samba" })
            {
                // Both the typed form and the displayed form must encode identically.
                byte[] typed = Core.Text.FfxiText.Encode($"/ja \"{{AT:{phrase}}}\" <me>", 61);
                byte[] shown = Core.Text.FfxiText.Encode($"/ja \"«{phrase}»\" <me>", 61);
                Assert.Equal(shown, typed);

                // The phrase itself: FD, four payload bytes, FD — the game's own layout.
                int start = Array.IndexOf(typed, (byte)0xFD);
                Assert.True(start >= 0, $"'{phrase}' did not encode to an auto-translate phrase.");
                Assert.Equal(0x02, typed[start + 1]);
                Assert.Equal(0x02, typed[start + 2]);
                Assert.Equal(0xFD, typed[start + 5]);

                // And it reads back as the same name.
                Assert.Equal($"/ja \"«{phrase}»\" <me>", Core.Text.FfxiText.Decode(typed, dictionary));
            }
        }
        finally
        {
            Core.Text.FfxiText.DefaultAutoTranslate = previous;
        }
    }

    [Fact]
    public void EveryTitleFileInTheCorpusRoundTripsExactly()
    {
        string? root = CorpusDirectory;
        if (root is null)
            return;   // No corpus on this machine: set FFXI_USER_DIR to a real USER folder to run this.

        var failures = new StringBuilder();
        int checkedFiles = 0;

        foreach (string path in Directory.EnumerateFiles(root!, "mcr*.ttl", SearchOption.AllDirectories))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);

            try
            {
                byte[] rewritten = BookTitleSet.Read(original).ToBytes();
                if (!original.AsSpan().SequenceEqual(rewritten))
                    failures.AppendLine(path).AppendLine(HexDump.Diff(original, rewritten, "original", "rewritten", 8));
            }
            catch (MacroFileException ex)
            {
                failures.AppendLine($"{path}: {ex.Message}");
            }
        }

        Assert.True(checkedFiles > 0, $"No mcr*.ttl files found under {root}.");
        Assert.True(failures.Length == 0, $"{checkedFiles} files checked:\n{failures}");
    }
}
