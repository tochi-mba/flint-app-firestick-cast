using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Flint.Core.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as the text of the saved file.
/// </summary>
/// <remarks>
/// <para>
/// Reading never throws. A section that cannot be read falls back to its defaults on its own, so a
/// mistake made by hand in one section does not cost a person every other setting. Anything that is
/// read is normalised before it is returned.
/// </para>
/// <para>
/// Numbers written as text are accepted, because that is the most likely hand edit, and property
/// names are matched without regard to case. Choices are written by name rather than number, so the
/// file reads plainly and reordering a choice in a later build cannot change what an old file means.
/// </para>
/// </remarks>
public static class AppSettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    /// <summary>The text of the saved file for <paramref name="settings"/>.</summary>
    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings.Normalize(), Options);
    }

    /// <summary>The settings in <paramref name="text"/>, or the defaults for whatever cannot be read.</summary>
    public static AppSettings Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AppSettings.Default;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject;
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }

        if (root is null)
        {
            return AppSettings.Default;
        }

        return new AppSettings
        {
            General = Section<GeneralSettings>(root, "general"),
            Screen = Section<ScreenSettings>(root, "screen"),
            Media = Section<MediaSettings>(root, "media"),
            Shortcuts = Section<ShortcutSettings>(root, "shortcuts"),
            Tray = Section<TraySettings>(root, "tray"),
        }.Normalize();
    }

    /// <summary>Whether <paramref name="text"/> is a settings file at all, for refusing an import.</summary>
    /// <remarks>
    /// Only the outer shape is checked. A file with the right shape and a bad section is still
    /// imported, the bad section as its defaults, exactly as it would load at launch.
    /// </remarks>
    public static bool LooksLikeSettings(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) is JsonObject root
                && root.ContainsKey("schemaVersion");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static T Section<T>(JsonObject root, string name)
        where T : new()
    {
        if (!root.TryGetPropertyValue(name, out var node) || node is not JsonObject section)
        {
            return new T();
        }

        try
        {
            return section.Deserialize<T>(Options) ?? new T();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return new T();
        }
    }
}
