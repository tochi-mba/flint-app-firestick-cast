using System.ComponentModel;
using Avalonia.Controls;
using Flint.Platform.Windows;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flint.App.ViewModels;

namespace Flint.App.Views;

/// <summary>The Flint shell window.</summary>
public partial class MainWindow : Window
{
    private SurfaceSwitchPrompt? prompt;
    private MainWindowViewModel? shell;
    private bool enterDown;
    private bool enterOpenedTheQuestion;

    /// <summary>Initialises the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A key released while another window has focus never comes up here.
        Deactivated += (_, _) => ReleaseEnter();
        Opened += (_, _) => WatchForLocking();
        Closing += OnClosing;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>Offers to take files and folders; text, links and anything else are refused.</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = DroppedFiles.HasFiles(e.DataTransfer);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        if (DataContext is MainWindowViewModel shell)
        {
            shell.IsDropTarget = files;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel shell)
        {
            shell.IsDropTarget = false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel shell)
        {
            return;
        }

        shell.IsDropTarget = false;
        if (DroppedFiles.HasFiles(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.Copy;
            await shell.DropFilesAsync(DroppedFiles.PathsOf(e.DataTransfer));
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (shell is not null)
        {
            shell.MinimiseRequested -= OnMinimiseRequested;
        }

        shell = DataContext as MainWindowViewModel;
        if (shell is not null)
        {
            shell.MinimiseRequested += OnMinimiseRequested;
        }

        if (prompt is not null)
        {
            prompt.PropertyChanged -= OnPromptChanged;
        }

        prompt = (DataContext as MainWindowViewModel)?.SwitchPrompt;
        if (prompt is not null)
        {
            prompt.PropertyChanged += OnPromptChanged;
        }
    }

    /// <summary>
    /// Moves focus to the switch button once the question is showing.
    /// </summary>
    /// <remarks>
    /// Posted, so the question is laid out before focus lands on it; a screen reader then reads
    /// the button that has focus. An Enter still held from opening the question does not count as
    /// an answer: see <see cref="OnWindowKeyDown"/>.
    /// </remarks>
    private void OnMinimiseRequested(object? sender, EventArgs args) => WindowState = WindowState.Minimized;

    /// <summary>
    /// Turns the person's own close into the shell's decision: hide to the tray, ask, or quit.
    /// Windows shutting down, and Flint quitting, go straight through.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (shell is { HasWindowControl: true, IsQuitting: false } && e.CloseReason is WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            _ = shell.CloseWindowAsync();
        }
    }

    /// <summary>Escape puts the closing question away without choosing.</summary>
    private void OnCloseOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape && shell is not null)
        {
            shell.CloseQuestion.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Asks Windows to tell this window when the PC locks and unlocks, and passes that to the Screen
    /// page, which pauses a share as the settings say.
    /// </summary>
    /// <remarks>Only a real Windows window can be told; a headless one has no handle.</remarks>
    private void WatchForLocking()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == 0)
        {
            return;
        }

        // Lives exactly as long as this window, which signs it out when it closes.
        var watcher = new SessionLockWatcher(handle);
        watcher.Locked += (_, _) => shell?.Screen.OnPcLocked();
        watcher.Unlocked += (_, _) => shell?.Screen.OnPcUnlocked();
        Win32Properties.AddWndProcHookCallback(
            this,
            (nint window, uint message, nint wParam, nint lParam, ref bool handled) =>
            {
                watcher.OnMessage(message, wParam);
                return 0;
            });
        Closed += (_, _) => watcher.Dispose();
    }

    private void OnPromptChanged(object? sender, PropertyChangedEventArgs args)
    {
        // This handler is attached only to SurfaceSwitchPrompt.PropertyChanged. Reading the event
        // sender avoids a nullable-field branch that cannot occur while the subscription is live.
        var changedPrompt = (SurfaceSwitchPrompt)sender!;
        if (args.PropertyName == nameof(SurfaceSwitchPrompt.IsOpen) && changedPrompt.IsOpen)
        {
            enterOpenedTheQuestion = enterDown;
            Dispatcher.UIThread.Post(() => SwitchConfirm.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Keeps an Enter held from before the question opened from answering it.
    /// </summary>
    /// <remarks>
    /// Enter in the Web address bar can open the question, and focus then moves to the switch
    /// button. Held a moment too long, that same Enter repeats onto the button and would switch the
    /// TV before the question was read. Repeats are dropped until the key comes up; the next press
    /// answers as usual.
    /// </remarks>
    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.Handled && shell?.OnWindowKey(args.Key, args.KeyModifiers) == true)
        {
            args.Handled = true;
            return;
        }

        if (args.Key != Key.Enter)
        {
            return;
        }

        enterDown = true;
        if (enterOpenedTheQuestion && prompt is { IsOpen: true })
        {
            args.Handled = true;
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            ReleaseEnter();
        }
    }

    private void ReleaseEnter()
    {
        enterDown = false;
        enterOpenedTheQuestion = false;
    }

    /// <remarks>The overlay shows, and so takes keys, only while a shell's question is open.</remarks>
    private void OnSwitchOverlayKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            prompt!.KeepCommand.Execute(null);
            args.Handled = true;
        }
    }
}
