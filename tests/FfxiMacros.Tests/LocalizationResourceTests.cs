using Avalonia;
using Avalonia.Headless;
using FfxiMacros.App.Localization;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The half of the translation the other tests cannot reach: the labels written in XAML resolve
/// through a resource dictionary, not through <see cref="Loc.T"/>. This runs the real application
/// without a screen and reads the dictionary back the way a <c>{DynamicResource}</c> binding would,
/// which is the only way to prove that switching language actually reaches the interface.
/// </summary>
[Collection(nameof(HeadlessApplication))]
public class LocalizationResourceTests : IDisposable
{
    private readonly Language _original = Loc.Current;

    public LocalizationResourceTests(HeadlessApplication _)
    {
    }

    [Fact]
    public void SwitchingLanguageSwapsTheLabelsTheXamlBindsTo()
    {
        Loc.Apply(Language.English);
        Assert.Equal("Save", Lookup("Set.Save"));
        Assert.Equal("BOOKS", Lookup("Books.Title"));

        Loc.Apply(Language.French);
        Assert.Equal("Enregistrer", Lookup("Set.Save"));

        Loc.Apply(Language.English);
        Assert.Equal("Save", Lookup("Set.Save"));
    }

    [Fact]
    public void TheOldLanguageIsRemoved()
    {
        // Merging the second table over the first would leave whichever key the new one happens to
        // miss showing the previous language. Every key has to move together.
        Loc.Apply(Language.English);
        Loc.Apply(Language.French);

        foreach (var key in Loc.TableFor(Language.French).Keys)
            Assert.Equal(Loc.TableFor(Language.French)[key], Lookup(key));
    }

    [Fact]
    public void TheBrushesSurviveALanguageSwitch()
    {
        // The labels are merged into the same dictionary the palette lives in.
        Loc.Apply(Language.French);

        Assert.True(Application.Current!.TryGetResource("Accent", null, out object? accent));
        Assert.NotNull(accent);
    }

    private static string? Lookup(string key) =>
        Application.Current!.TryGetResource(key, null, out object? value) ? value as string : null;

    public void Dispose() => Loc.Apply(_original);
}

/// <summary>
/// One Avalonia application per test process: <c>AppBuilder</c> refuses to configure a second one,
/// so the tests that need it share this fixture and run one at a time.
/// </summary>
public sealed class HeadlessApplication
{
    private static readonly object Gate = new();
    private static bool _started;

    public HeadlessApplication()
    {
        lock (Gate)
        {
            if (_started)
                return;

            AppBuilder.Configure<FfxiMacros.App.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();

            _started = true;
        }
    }
}

[CollectionDefinition(nameof(HeadlessApplication))]
public sealed class HeadlessApplicationCollection : ICollectionFixture<HeadlessApplication>;
