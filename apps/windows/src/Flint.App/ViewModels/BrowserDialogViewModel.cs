using CommunityToolkit.Mvvm.ComponentModel;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>One native page dialog. Its text is bounded by the protocol before reaching here.</summary>
public sealed partial class BrowserDialogViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(IsPrompt))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    private BrowserDialogMessage? active;

    [ObservableProperty]
    private string promptText = string.Empty;

    public bool IsVisible => Active is not null;
    public bool IsPrompt => Active?.Type == BrowserDialogType.Prompt;
    public bool CanCancel => Active?.Type != BrowserDialogType.Alert;
    public string ConfirmLabel => Active?.Type == BrowserDialogType.Alert ? "OK" : "CONTINUE";

    internal void Show(BrowserDialogMessage dialog)
    {
        Active = dialog;
        PromptText = dialog.Type == BrowserDialogType.Prompt ? dialog.DefaultValue : string.Empty;
    }

    internal void Clear()
    {
        Active = null;
        PromptText = string.Empty;
    }
}
