using Avalonia.Controls;

namespace Flint.App.Controls;

/// <summary>A compact, single-choice row whose arrow-key behaviour comes from <see cref="ListBox"/>.</summary>
public sealed class SegmentedChoice : ListBox
{
    /// <summary>Creates a single-choice list.</summary>
    public SegmentedChoice() => SelectionMode = SelectionMode.Single;
}
