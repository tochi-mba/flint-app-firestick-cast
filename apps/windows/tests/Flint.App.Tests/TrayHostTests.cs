using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flint.App.Services;
using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The tray model shown as Windows' notification-area icon, and the icon drawn for each state.</summary>
public sealed class TrayHostTests
{
    [AvaloniaFact]
    public void TheIcon_ShowsTheModel_AndFollowsItAndTheSettings()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var window = new RecordingWindow();
        shell.UseWindow(window);
        using var tray = new TrayViewModel(shell);
        using var host = new TrayHost(Application.Current!, tray, shell.SettingsService);

        host.Icon.ToolTipText.ShouldBe("Flint: not connected to a TV");
        host.Icon.IsVisible.ShouldBeTrue();
        host.Icon.Icon.ShouldNotBeNull();

        shell.Cast.IsMirroring = true;
        host.Icon.ToolTipText.ShouldBe("Flint: sharing your screen to the TV");
        shell.SettingsService.Update(current => current with { Tray = current.Tray with { ShowIcon = false } });
        host.Icon.IsVisible.ShouldBeFalse();
        shell.Cast.IsMirroring = false;
    }

    [AvaloniaFact]
    public void AClickOnTheIcon_GoesToTheTray()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var window = new RecordingWindow();
        shell.UseWindow(window);
        using var tray = new TrayViewModel(shell);
        using var host = new TrayHost(Application.Current!, tray, shell.SettingsService);

        host.OnClicked(null, EventArgs.Empty);

        window.Shown.ShouldBe(1);
    }

    [AvaloniaFact]
    public void TheMenu_HasTheStatusLineApart_AndSettingsAndQuitAtTheFoot()
    {
        var open = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        var menu = TrayHost.MenuFor(
        [
            new("Connected to Living Room", null),
            new("Open Flint", open),
            new("Settings", open),
            new("Quit Flint", open),
        ]);

        menu.Items.Select(Describe).ShouldBe(["Connected to Living Room (off)", "-", "Open Flint", "-", "Settings", "Quit Flint"]);
    }

    [AvaloniaFact]
    public void Disposing_TakesTheIconAway()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        using var tray = new TrayViewModel(shell);
        var host = new TrayHost(Application.Current!, tray, shell.SettingsService);

        host.Dispose();

        host.Icon.IsVisible.ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => new TrayHost(null!, tray, shell.SettingsService));
        Should.Throw<ArgumentNullException>(() => new TrayHost(Application.Current!, null!, shell.SettingsService));
        Should.Throw<ArgumentNullException>(() => new TrayHost(Application.Current!, tray, null!));
    }

    [AvaloniaFact]
    public void EachState_IsDrawnFromTheMark_WithItsDot()
    {
        var idle = Pixels(TrayIconArt.Draw(TrayState.Idle));
        var connected = Pixels(TrayIconArt.Draw(TrayState.Connected));
        var sharing = Pixels(TrayIconArt.Draw(TrayState.Sharing));
        var paused = Pixels(TrayIconArt.Draw(TrayState.Paused));

        Brightness(idle).ShouldBeLessThan(Brightness(connected), "idle is the mark dimmed");
        Dot(connected).ShouldNotBe(TrayIconArt.Live);
        Dot(sharing).ShouldBe(TrayIconArt.Live);
        Dot(paused).ShouldBe(TrayIconArt.Held);
        TrayIconArt.DotFor(TrayState.Connected).ShouldBeNull();
        TrayIconArt.DotFor(TrayState.Idle).ShouldBeNull();
    }

    private static string Describe(NativeMenuItemBase item) => item switch
    {
        NativeMenuItemSeparator => "-",
        NativeMenuItem { IsEnabled: false } menuItem => $"{menuItem.Header} (off)",
        NativeMenuItem menuItem => menuItem.Header!,
        _ => "?",
    };

    /// <summary>The drawn icon's pixels, as premultiplied BGRA.</summary>
    private static byte[] Pixels(Bitmap bitmap)
    {
        var size = TrayIconArt.Size;
        var pixels = new byte[size * size * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size, size), handle.AddrOfPinnedObject(), pixels.Length, size * 4);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    private static double Brightness(byte[] pixels) =>
        pixels.Where((_, index) => index % 4 != 3).Average(value => (double)value);

    /// <summary>The colour at the dot's centre, near the bottom right.</summary>
    private static Color Dot(byte[] pixels)
    {
        var at = ((TrayIconArt.Size - 7) * TrayIconArt.Size + (TrayIconArt.Size - 7)) * 4;
        return Color.FromRgb(pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
