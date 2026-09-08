using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserWorkspaceLayoutLabelTests
{
    [Theory]
    [InlineData(BrowserWorkspaceLayout.Single, "1", "One pane")]
    [InlineData(BrowserWorkspaceLayout.TwoColumns, "2 COL", "Side by side")]
    [InlineData(BrowserWorkspaceLayout.TwoRows, "2 ROW", "Stacked")]
    [InlineData(BrowserWorkspaceLayout.FourGrid, "4", "Four-pane grid")]
    public void CompactLabels_KeepDisplayNamesForTooltips(
        BrowserWorkspaceLayout layout,
        string compact,
        string display)
    {
        layout.Label().ShouldBe(compact);
        layout.DisplayName().ShouldBe(display);
    }
}
