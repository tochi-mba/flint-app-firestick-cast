using Shouldly;
using Xunit;

namespace Flint.Hardware.E2E;

/// <summary>Real Windows mouse/keyboard walkthrough coverage; no TV or application-model access.</summary>
public sealed class WindowsIntroductionHardwareTests
{
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task IntroductionCanBeReplayedNavigatedAndDismissed()
    {
        HardwareGate.IsEnabled.ShouldBeTrue(HardwareGate.SkipReason);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var root = CastPairHardwareFlowTests.FindProjectRoot();
        var executable = Environment.GetEnvironmentVariable("FLINT_HARDWARE_APP_PATH")
            ?? Path.Combine(root, "apps", "windows", "src", "Flint.App", "bin", "Debug", "net10.0-windows", "Flint.App.exe");
        using var windows = new WindowsUiDriver(executable, timeout.Token);
        var artifacts = Path.Combine(root, "artifacts", "windows-introduction", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(artifacts);
        try
        {
            var skip = windows.Window.FindFirstDescendant(cf => cf.ByName("SKIP"));
            if (skip is { IsOffscreen: false }) skip.Click();
            await windows.ClickAsync("SHOW INTRODUCTION");
            await windows.WaitAsync("Flint casts this PC to your TV", enabled: false);
            windows.CaptureFailure(Path.Combine(artifacts, "introduction.png"));
            await windows.ClickAsync("NEXT");
            await windows.WaitAsync("Your Fire TV model decides what is possible", enabled: false);
            await windows.ClickAsync("BACK");
            await windows.WaitAsync("Flint casts this PC to your TV", enabled: false);
            await windows.ClickAsync("SKIP");
            await windows.ClickAsync("SHOW INTRODUCTION");
            await windows.WaitAsync("Flint casts this PC to your TV", enabled: false);
            await windows.ClickAsync("SKIP");
            await windows.WaitAsync("SHOW INTRODUCTION");
        }
        catch
        {
            try { windows.CaptureFailure(Path.Combine(artifacts, "failure.png")); } catch { /* Preserve original failure. */ }
            throw;
        }
    }
}
