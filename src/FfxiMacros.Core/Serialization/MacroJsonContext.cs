using System.Text.Json.Serialization;

namespace FfxiMacros.Core.Serialization;

/// <summary>
/// The build-time description of the export format, used in place of reflection so that a trimmed
/// build still reads and writes it. Adding a type to the document tree means adding it here.
/// </summary>
[JsonSerializable(typeof(MacroDocument))]
internal sealed partial class MacroJsonContext : JsonSerializerContext;
