using Avalonia;
using Avalonia.Controls;

namespace Flint.App.Controls;

/// <summary>A setting's name and explanation beside the control that changes it.</summary>
public sealed class SettingRow : ContentControl
{
    /// <summary>The setting's short name.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string>(nameof(Title), string.Empty);

    /// <summary>The one-sentence consequence of changing it.</summary>
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string>(nameof(Description), string.Empty);

    /// <summary>The setting's short name.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The one-sentence consequence of changing it.</summary>
    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
