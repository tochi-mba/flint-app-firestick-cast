using Shouldly;
using Xunit;

namespace Flint.Hardware.E2E;

/// <summary>Cross-device input through Windows UIA and TV ADB, without application-model shortcuts.</summary>
public sealed class CastPairHardwareFlowTests
{
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task WindowsPairsThenWebReconnectsAndTvReceivesNavigation()
    {
        HardwareGate.IsEnabled.ShouldBeTrue(HardwareGate.SkipReason);
        var serial = HardwareGate.Serial ?? throw new InvalidOperationException(HardwareGate.SkipReason);
        var expectedPageText = HardwareGate.BrowseExpectedText;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await AdbTvSurfaceReader.LaunchReceiverAsync(serial, timeout.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
        var facts = await AdbTvSurfaceReader.ReadAsync(serial, timeout.Token);
        facts.BrowserPort.ShouldNotBeNull("A running TLS browser is required; absence must not pass as tested.");
        var root = FindProjectRoot();
        var executable = Environment.GetEnvironmentVariable("FLINT_HARDWARE_APP_PATH")
            ?? Path.Combine(root, "apps", "windows", "src", "Flint.App", "bin", "Debug", "net10.0-windows", "Flint.App.exe");
        using var windows = new WindowsUiDriver(executable, timeout.Token);
        try
        {
            var skip = windows.Window.FindFirstDescendant(cf => cf.ByName("SKIP"));
            if (skip is { IsOffscreen: false }) skip.Click();
            await windows.TypeAsync("TV IP address", facts.Address);
            await windows.ClickAsync("Connect to TV");
            await windows.TypeAsync("Pairing code", facts.PairingCode);
            await windows.TypeAsync("Receiver port", facts.ReceiverPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await windows.ClickAsync("Pair with TV");
            await windows.WaitAsync("Pairing status", enabled: false, helpText: "Paired and ready to cast.");
            await WaitForTvAsync(serial, "PC CONNECTED", timeout.Token);
            await windows.ClickAsync("Web");
            // Returning trust must reconnect itself. Never press Verify to hide a broken handoff
            // or silently accept a first-use certificate in this returning-user flow.
            await windows.WaitAsync("TV address");
            await windows.TypeAsync("TV address", HardwareGate.BrowseUrl);
            await windows.ClickAsync("Open address on TV");
            await WaitForTvAsync(serial, expectedPageText, timeout.Token);
            await AdbTvSurfaceReader.PressKeyAsync(serial, 82, timeout.Token);
            await WaitForTvAsync(serial, "Browser menu", timeout.Token);
            await AdbTvSurfaceReader.PressKeyAsync(serial, 4, timeout.Token);
            await windows.WaitAsync("TV address");
        }
        catch
        {
            var artifacts = Path.Combine(root, "artifacts", "cross-device", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(artifacts);
            try { windows.CaptureFailure(Path.Combine(artifacts, "windows.png")); } catch { /* Keep original failure. */ }
            try { await File.WriteAllTextAsync(Path.Combine(artifacts, "tv.xml"),
                await AdbTvSurfaceReader.ReadHierarchyAsync(serial, CancellationToken.None)); } catch { /* Keep original failure. */ }
            throw;
        }
    }

    private static async Task WaitForTvAsync(string serial, string expected, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var hierarchy = await AdbTvSurfaceReader.ReadHierarchyAsync(serial, cancellationToken);
            if (hierarchy.Contains(expected, StringComparison.Ordinal)) return;
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException($"TV did not show '{expected}'.");
    }

    internal static string FindProjectRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Flint.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run this test from a Flint build tree.");
    }
}
