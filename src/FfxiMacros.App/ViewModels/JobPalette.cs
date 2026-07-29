using Avalonia.Media;

namespace FfxiMacros.App.ViewModels;

/// <summary>
/// Book titles are job combinations — <c>ThfRdm</c>, <c>PldRunR</c>, <c>blmschcor</c> — so the first
/// three letters say what the book is for. Tinting a book by the role of the job it leads with turns
/// forty near-identical names into a list you can find your place in without reading it.
/// </summary>
/// <remarks>
/// A title the game wrote itself (<c>Book07</c>) or one the player invented resolves to no job and
/// stays neutral: this only ever adds colour, it never withholds a book or renames one.
/// </remarks>
public static class JobPalette
{
    /// <summary>The colour a role is drawn in. Muted enough to sit behind text on a dark surface.</summary>
    private static readonly Dictionary<string, Color> RoleColours = new(StringComparer.Ordinal)
    {
        ["tank"] = Color.FromRgb(0x5E, 0x97, 0xEA),
        ["melee"] = Color.FromRgb(0xE3, 0x7D, 0x63),
        ["ranged"] = Color.FromRgb(0xE0, 0xA6, 0x3C),
        ["caster"] = Color.FromRgb(0xAB, 0x84, 0xEA),
        ["healer"] = Color.FromRgb(0x5C, 0xC4, 0x94),
        ["support"] = Color.FromRgb(0x55, 0xC2, 0xD6),
    };

    /// <summary>The three-letter code the game uses for each job, grouped by what it does.</summary>
    private static readonly Dictionary<string, string> RoleByJob = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pld"] = "tank", ["run"] = "tank", ["nin"] = "tank",
        ["war"] = "melee", ["mnk"] = "melee", ["thf"] = "melee", ["drk"] = "melee",
        ["sam"] = "melee", ["drg"] = "melee", ["bst"] = "melee", ["pup"] = "melee",
        ["dnc"] = "melee", ["blu"] = "melee",
        ["rng"] = "ranged", ["cor"] = "ranged",
        ["blm"] = "caster", ["smn"] = "caster", ["sch"] = "caster", ["geo"] = "caster",
        ["whm"] = "healer", ["rdm"] = "healer",
        ["brd"] = "support",
    };

    private static readonly Dictionary<string, IBrush> Backgrounds = Build(0x30);
    private static readonly Dictionary<string, IBrush> Foregrounds = Build(0xFF);

    private static readonly IBrush NeutralBackground = Frozen(Color.FromArgb(0x2E, 0x8E, 0x9A, 0xB4));
    private static readonly IBrush NeutralForeground = Frozen(Color.FromRgb(0x8E, 0x9A, 0xB4));

    /// <summary>The role of the job a title leads with, or null when it names no job.</summary>
    public static string? RoleOf(string? title)
    {
        string trimmed = title?.Trim() ?? "";
        return trimmed.Length >= 3 && RoleByJob.TryGetValue(trimmed[..3], out string? role) ? role : null;
    }

    /// <summary>Fill for the book's number chip: the role's colour at a fraction of its strength.</summary>
    public static IBrush BackgroundFor(string? title) =>
        RoleOf(title) is { } role ? Backgrounds[role] : NeutralBackground;

    /// <summary>Ink for the book's number chip.</summary>
    public static IBrush ForegroundFor(string? title) =>
        RoleOf(title) is { } role ? Foregrounds[role] : NeutralForeground;

    /// <summary>
    /// The wash laid over a book's card: the role's colour, brighter at the top, and faint.
    /// </summary>
    /// <remarks>
    /// Faint on purpose. Forty cards at full strength would be a chart rather than a list — at this
    /// weight the colour is felt while scrolling and disappears when reading a name. The gradient
    /// follows the light, so the tint does not flatten the relief it sits on.
    /// </remarks>
    public static IBrush WashFor(string? title) =>
        RoleOf(title) is { } role ? Washes[role] : NeutralWash;

    private static readonly Dictionary<string, IBrush> Washes = RoleColours.ToDictionary(
        entry => entry.Key,
        entry => Wash(entry.Value),
        StringComparer.Ordinal);

    private static readonly IBrush NeutralWash = Wash(Color.FromRgb(0x8E, 0x9A, 0xB4));

    private static IBrush Wash(Color colour)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x2A, colour.R, colour.G, colour.B), 0),
                new GradientStop(Color.FromArgb(0x0E, colour.R, colour.G, colour.B), 1),
            },
        };

        return brush.ToImmutable();
    }

    private static Dictionary<string, IBrush> Build(byte alpha) =>
        RoleColours.ToDictionary(
            entry => entry.Key,
            entry => Frozen(Color.FromArgb(alpha, entry.Value.R, entry.Value.G, entry.Value.B)),
            StringComparer.Ordinal);

    /// <summary>Brushes are shared by every row, so they are built once and never mutated.</summary>
    private static IBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.ToImmutable();
        return brush.ToImmutable();
    }
}
