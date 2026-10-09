using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core.Shortcuts;

namespace Flint.App.ViewModels.Settings;

/// <summary>One action's shortcut: what it is, changing it, and clearing it.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly ShortcutsSettingsViewModel section;
    private HotKeyGesture? pendingMove;
    private ShortcutRowViewModel? pendingOwner;

    internal ShortcutRowViewModel(ShortcutsSettingsViewModel section, ShortcutAction action, string title)
    {
        this.section = section;
        Action = action;
        Text = new SettingText(title, $"The shortcut to {char.ToLowerInvariant(title[0])}{title[1..]}.");
    }

    /// <summary>The action.</summary>
    public ShortcutAction Action { get; }

    /// <summary>The row's name and what it does, for the page and its search.</summary>
    public SettingText Text { get; }

    /// <summary>The shortcut in force, or "None".</summary>
    public string Shortcut => HotKeyService.TextFor(section.Current, Action) ?? "None";

    /// <summary>Why the shortcut in force is not working, or null.</summary>
    public string? Problem => section.ProblemFor(Action);

    /// <summary>Whether the row is waiting for a key combination.</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>What the row says while changing: the prompt, or why a combination was refused.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>Whether the row asks to take a combination another action has.</summary>
    [ObservableProperty]
    private bool _isAskingToMove;

    /// <summary>Waits for the new combination.</summary>
    [RelayCommand]
    private void Change()
    {
        section.BeginCapture(this);
        ForgetMove();
        IsCapturing = true;
        Status = "Press the new shortcut. Escape cancels.";
    }

    /// <summary>Leaves the action without a shortcut.</summary>
    [RelayCommand]
    private void Clear()
    {
        CancelCapture();
        section.Set(Action, null);
    }

    /// <summary>Takes the combination from the other action, which is left without one.</summary>
    [RelayCommand]
    private void Move()
    {
        if (pendingMove is not { } gesture || pendingOwner is not { } owner)
        {
            return;
        }

        section.Set(owner.Action, null);
        ForgetMove();
        Assign(gesture);
    }

    /// <summary>Leaves the combination where it is.</summary>
    [RelayCommand]
    private void KeepIt() => CancelCapture();

    /// <summary>Takes a combination pressed while the row is waiting for one.</summary>
    public void Capture(HotKeyGesture pressed)
    {
        if (!IsCapturing)
        {
            return;
        }

        if (!HotKeyGesture.Check(pressed, out var gesture, out var problem))
        {
            Status = problem;
            return;
        }

        if (section.OwnerOf(gesture, Action) is { } owner)
        {
            pendingMove = gesture;
            pendingOwner = owner;
            IsAskingToMove = true;
            Status = $"{gesture} is already the shortcut to {char.ToLowerInvariant(owner.Text.Title[0])}{owner.Text.Title[1..]}. Move it here?";
            return;
        }

        Assign(gesture);
    }

    /// <summary>Stops waiting for a combination, changing nothing.</summary>
    public void CancelCapture()
    {
        ForgetMove();
        IsCapturing = false;
        Status = null;
    }

    /// <summary>Reads the shortcut and its problem again.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(Shortcut));
        OnPropertyChanged(nameof(Problem));
    }

    private void Assign(HotKeyGesture gesture)
    {
        var refused = section.Assign(Action, gesture);
        IsCapturing = false;
        Status = refused;
    }

    private void ForgetMove()
    {
        pendingMove = null;
        pendingOwner = null;
        IsAskingToMove = false;
    }
}
