using FfxiMacros.App.ViewModels;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Colouring a book by the job its title leads with. This only ever tints a chip, so the interesting
/// cases are the ones where it must stay quiet: a title the game invented, or one the player did.
/// </summary>
public class JobPaletteTests
{
    [Theory]
    [InlineData("PldRunR", "tank")]
    [InlineData("ThfRdm", "melee")]
    [InlineData("WarsamR", "melee")]
    [InlineData("CorDnc", "ranged")]
    [InlineData("blmschcor", "caster")]     // the player's own casing
    [InlineData("RDMBLM", "healer")]
    [InlineData("BrdDncC", "support")]
    public void ABookTakesTheRoleOfTheJobItLeadsWith(string title, string expected) =>
        Assert.Equal(expected, JobPalette.RoleOf(title));

    [Theory]
    [InlineData("Book07")]                  // what the game writes for an untitled book
    [InlineData("Farming")]
    [InlineData("Pl")]                      // too short to name a job
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ATitleThatNamesNoJobStaysNeutral(string? title)
    {
        Assert.Null(JobPalette.RoleOf(title));
        Assert.Equal(JobPalette.BackgroundFor(null), JobPalette.BackgroundFor(title));
    }

    [Fact]
    public void LeadingAndTrailingSpaceDoesNotHideTheJob() =>
        Assert.Equal("tank", JobPalette.RoleOf("  PldRunR "));

    [Fact]
    public void TwoRolesAreDrawnInDifferentColours()
    {
        Assert.NotEqual(JobPalette.BackgroundFor("PldRunR"), JobPalette.BackgroundFor("ThfRdm"));
        Assert.NotEqual(JobPalette.ForegroundFor("PldRunR"), JobPalette.ForegroundFor("Book07"));
    }

    [Fact]
    public void EveryBookOfARoleSharesOneBrushInstance()
    {
        // Forty rows rebind these on every refresh; they must not allocate a brush each time.
        Assert.Same(JobPalette.BackgroundFor("PldRunR"), JobPalette.BackgroundFor("RunPldC"));
        Assert.Same(JobPalette.ForegroundFor("Book07"), JobPalette.ForegroundFor("Farming"));
    }
}
