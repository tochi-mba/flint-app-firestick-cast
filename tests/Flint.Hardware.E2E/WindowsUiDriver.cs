using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Flint.Hardware.E2E;

/// <summary>Out-of-process Windows automation. No view models or application commands are accessed.</summary>
internal sealed class WindowsUiDriver : IDisposable
{
    private readonly Application application;
    private readonly UIA3Automation automation = new();
    private readonly CancellationToken cancellationToken;
    internal Window Window { get; }

    internal WindowsUiDriver(string executable, CancellationToken cancellationToken = default)
    {
        this.cancellationToken = cancellationToken;
        if (!File.Exists(executable)) throw new FileNotFoundException("Build Flint.App before headed testing.", executable);
        application = Application.Launch(executable);
        try
        {
            Window = application.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new InvalidOperationException("Flint did not expose a Windows automation window.");
            Window.SetForeground();
        }
        catch
        {
            try { application.Close(); }
            finally { application.Dispose(); automation.Dispose(); }
            throw;
        }
    }

    internal async Task<AutomationElement> WaitAsync(string name, bool enabled = true, string? helpText = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = Window.FindAllDescendants(cf => cf.ByName(name))
                .FirstOrDefault(element => !element.IsOffscreen && (!enabled || element.IsEnabled)
                    && (helpText is null || element.Properties.HelpText.ValueOrDefault == helpText));
            if (match is not null) return match;
            await Task.Delay(150, cancellationToken);
        }
        throw new TimeoutException($"Visible Windows control '{name}' did not become ready.");
    }

    internal async Task ClickAsync(string name)
    {
        var element = await WaitAsync(name);
        Window.SetForeground();
        element.Click();
        await Task.Delay(HardwareGate.StepPause, cancellationToken);
    }

    internal async Task TypeAsync(string name, string value)
    {
        var element = await WaitAsync(name);
        Window.SetForeground();
        element.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
        // Defocus to commit any LostFocus-bound edits, as a keyboard user would.
        Keyboard.Type(VirtualKeyShort.TAB);
        await Task.Delay(HardwareGate.StepPause, cancellationToken);
    }

    internal void CaptureFailure(string path) => Capture.Element(Window).ToFile(path);

    public void Dispose()
    {
        try { application.Close(); }
        finally { application.Dispose(); automation.Dispose(); }
    }
}
