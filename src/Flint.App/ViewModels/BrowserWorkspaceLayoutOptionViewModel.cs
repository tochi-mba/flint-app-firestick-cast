using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>One arrangement choice in the workspace toolbar.</summary>
public sealed partial class BrowserWorkspaceLayoutOptionViewModel : ObservableObject
{
    private readonly Func<BrowserWorkspaceLayout, Task> select;
    private string availabilityReason = "NOT SUPPORTED BY THIS TV";

    internal BrowserWorkspaceLayoutOptionViewModel(
        BrowserWorkspaceLayout layout,
        Func<BrowserWorkspaceLayout, Task> select)
    {
        Layout = layout;
        this.select = select;
    }

    /// <summary>Layout requested if this option is selected.</summary>
    public BrowserWorkspaceLayout Layout { get; }

    /// <summary>Compact chip text for the dense HUD toolbar.</summary>
    public string Label => Layout.Label();

    /// <summary>Full arrangement name for tooltips and accessibility.</summary>
    public string DisplayName => $"{Layout.DisplayName()} · {AvailabilityLabel}";

    /// <summary>
    /// Whether this chip is a valid choice right now (TV advertises it and open pane count fits).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool isSupported;

    /// <summary>Whether this is the current receiver-reported arrangement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool isSelected;

    /// <summary>Whether a layout request can be sent on the currently bound receiver channel.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool canSend;

    /// <summary>Plain explanation for disabled layout actions and screen readers.</summary>
    public string AvailabilityLabel => availabilityReason;

    internal void Apply(bool supported, bool selected, bool canSend, string availabilityReason)
    {
        this.availabilityReason = string.IsNullOrWhiteSpace(availabilityReason)
            ? "NOT SUPPORTED BY THIS TV"
            : availabilityReason;
        IsSupported = supported;
        IsSelected = selected;
        CanSend = canSend;
        OnPropertyChanged(nameof(AvailabilityLabel));
        OnPropertyChanged(nameof(DisplayName));
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private Task SelectAsync() => select(Layout);

    private bool CanSelect() => IsSupported && !IsSelected && CanSend;
}
