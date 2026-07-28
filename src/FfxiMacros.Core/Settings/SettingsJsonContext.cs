using System.Text.Json.Serialization;

namespace FfxiMacros.Core.Settings;

/// <summary>Build-time description of <see cref="EditorSettings"/>; see <see cref="SettingsStore"/>.</summary>
[JsonSerializable(typeof(EditorSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
