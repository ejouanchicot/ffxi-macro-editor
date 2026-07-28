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
    public void ACharacterWithNoNamesToSearchForIsSkipped()
    {
        // Titles that are blank, over-long or not plain ASCII cannot be looked for as they are, and
        // a character offering nothing else must not send the scan off reading gigabytes for nothing.
        using var probe = new GameMemoryProbe();

        probe.Scan([new CharacterSignature("aaaa1", ["", "  ", "a name far too long for the field"])]);

        Assert.Empty(probe.Read());
    }
}
