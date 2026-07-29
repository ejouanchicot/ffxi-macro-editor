using FfxiMacros.Core.Discovery;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// What can be checked without a client running: the probe has to be silent and harmless when there
/// is nothing to read. Finding a title table in a live process is proven against the game itself.
/// </summary>
public class GameMemoryProbeTests
{
    [Fact]
    public void ReadingWithNoClientRunningIsEmpty_NotAnError()
    {
        using var probe = new GameMemoryProbe();

        Assert.Empty(probe.Read());
    }

    [Fact]
    public void ScanningWithNothingToLookForDoesNothing()
    {
        using var probe = new GameMemoryProbe();

        probe.Scan([]);
        probe.Scan([new CharacterSignature("aaaa1", [])]);          // no usable title
        probe.Scan([new CharacterSignature("aaaa1", ["Only"])]);    // one is not enough to be sure

        Assert.Empty(probe.Read());
    }

    [Fact]
    public void WritingATitleIntoNothingIsRefused()
    {
        // No client placed, so there is nowhere to write: it has to answer no rather than reach for
        // an address it never found.
        using var probe = new GameMemoryProbe();

        Assert.False(probe.TryWriteTitle("aaaa1", 1, "ThfRdm", "Nin"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    [InlineData(-3)]
    public void WritingOutsideTheFortyBooksIsRefused(int book)
    {
        using var probe = new GameMemoryProbe();

        Assert.False(probe.TryWriteTitle("aaaa1", book, "ThfRdm", "Nin"));
    }

    [Fact]
    public void ATitleIsEncodedIntoTheWholeFieldTheGameReads()
    {
        // Sixteen bytes, terminator and padding included: writing fewer would leave the tail of the
        // previous name behind, which is exactly the mess the game itself leaves.
        byte[] encoded = FfxiMacros.Core.Io.BookTitleSet.EncodeTitle("Nin");

        Assert.Equal(FfxiMacros.Core.Io.BookTitleSet.TitleFieldSize, encoded.Length);
        Assert.Equal((byte)'N', encoded[0]);
        Assert.Equal(0, encoded[3]);
        Assert.All(encoded[3..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void TheNamesAClientIsRecognisedByAreConsecutiveBooks()
    {
        // They are matched sixteen bytes apart, so the run has to be books that actually sit side by
        // side. Picking out the usable names and closing the gaps would look for book 4's name where
        // book 2's belongs, and find the table nowhere.
        var signature = GameMemoryProbe.SignatureFor(["Pld", "", "Blm", "Bst", "Thf"]);

        Assert.NotNull(signature);
        Assert.Equal(3, signature.FirstBook);                        // starts after the nameless one
        Assert.Equal(["Blm", "Bst", "Thf"], signature.Names);
    }

    [Fact]
    public void ANameWithADeadTailIsLookedForAsTheGameHoldsIt()
    {
        // A book renamed and then cleared reads as "Book08{00}dm" on disk: six letters, a
        // terminator, and the tail of the name it used to have. The client holds nine bytes, one of
        // them zero — looking for those thirteen characters found nothing, and a character whose
        // third book read like that could not be recognised at all. Which is how a shelf of books
        // kept the game's old names while their macros moved.
        var signature = GameMemoryProbe.SignatureFor(["PldRun", "PldBluR", "Book08{00}dm"]);

        Assert.NotNull(signature);
        Assert.Equal(1, signature.FirstBook);
        Assert.Equal(["PldRun", "PldBluR", "Book08"], signature.Names);
        Assert.Equal(6, signature.Titles[2].Length);                 // stops at the terminator
    }

    [Fact]
    public void ANamelessFirstBookMovesTheRunRatherThanBreakingIt()
    {
        var signature = GameMemoryProbe.SignatureFor(["", "Pld", "Blm"]);

        Assert.NotNull(signature);
        Assert.Equal(2, signature.FirstBook);
        Assert.Equal(6 + 16, signature.Lead);                        // book number, then book 1
    }

    [Fact]
    public void RenamingABookInTheRunKeepsTheRunUsable()
    {
        // This is what the editor does to a client it is writing to, and the run has to follow: a
        // signature still spelling the old name stops recognising the very table it just changed.
        var signature = GameMemoryProbe.SignatureFor(["Pld", "Blm", "Bst"])!;

        var renamed = signature.With(2, "Dnc");

        Assert.NotNull(renamed);
        Assert.Equal(["Pld", "Dnc", "Bst"], renamed.Names);
        Assert.Equal(["Pld", "Blm", "Bst"], signature.Names);        // the old one is left alone
    }

    [Fact]
    public void EmptyingABookInTheRunLeavesNothingToRecogniseItBy()
    {
        var signature = GameMemoryProbe.SignatureFor(["Pld", "Blm", "Bst"])!;

        Assert.Null(signature.With(1, ""));                          // no proof left: forget it
        Assert.NotNull(signature.With(9, ""));                       // outside the run: still valid
    }

    [Fact]
    public void ACharacterWithNoNamesToSearchForIsSkipped()
    {
        // Titles that are blank, over-long or not plain ASCII cannot be looked for as they are, and
        // a character offering nothing else must not send the scan off reading gigabytes for nothing.
        using var probe = new GameMemoryProbe();

        probe.Scan([new CharacterSignature("aaaa1", ["", "  ", "a name far too long for the field"])]);

        Assert.Empty(probe.Read());
    }
}
