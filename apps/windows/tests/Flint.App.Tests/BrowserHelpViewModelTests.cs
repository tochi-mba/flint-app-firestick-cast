using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserHelpViewModelTests
{
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(6, 6)]
    [InlineData(7, 6)]
    [InlineData(int.MaxValue, 6)]
    public void ExternalIndexIsBounded(int requested, int expected)
    {
        var help = new BrowserHelpViewModel(false) { Index = requested };
        help.Index.ShouldBe(expected);
        help.Title.ShouldNotBeNullOrWhiteSpace();
        help.Body.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeverAutomaticallyOpens(bool dismissed)
    {
        var help = new BrowserHelpViewModel(dismissed);
        help.IsExpanded.ShouldBeFalse();
        help.Dismissed.ShouldBe(dismissed);
    }

    [Fact]
    public void CollapseDoesNotMarkCompletedAndKeepsPosition()
    {
        var saved = 0;
        var help = new BrowserHelpViewModel(false, () => saved++);
        help.ToggleCommand.Execute(null);
        help.NextCommand.Execute(null);
        help.ToggleCommand.Execute(null);
        help.Index.ShouldBe(1);
        saved.ShouldBe(0);
    }

    [Fact]
    public void CompletionPersistsOnceAndCanBeReplayed()
    {
        var saved = 0;
        var help = new BrowserHelpViewModel(false, () => saved++);
        help.ToggleCommand.Execute(null);
        for (var i = 0; i < 7; i++) help.NextCommand.Execute(null);
        help.Dismissed.ShouldBeTrue();
        help.IsExpanded.ShouldBeFalse();
        saved.ShouldBe(1);
        help.ToggleCommand.Execute(null);
        help.Index.ShouldBe(0);
        help.IsExpanded.ShouldBeTrue();
        help.DismissCommand.Execute(null);
        saved.ShouldBe(1);
    }

    [Fact]
    public void BackAtStartDoesNotUnderflow()
    {
        var help = new BrowserHelpViewModel(false);
        help.BackCommand.Execute(null);
        help.Index.ShouldBe(0);
        help.CanGoBack.ShouldBeFalse();
        help.Progress.ShouldBe("1 / 7");
    }
}
