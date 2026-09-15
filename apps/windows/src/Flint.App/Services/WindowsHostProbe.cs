using System.Runtime.Versioning;
using Flint.Core;
using Microsoft.Win32;

namespace Flint.App.Services;

/// <summary>
/// Reports the host's display adapters and Windows build.
/// </summary>
/// <remarks>
/// <para>
/// Adapters are read from the driver registry, which is where the display class stores every
/// adapter the system has bound a driver to. That is enough to tell a single-GPU desktop from a
/// hybrid laptop, which is the decision the capture strategy turns on.
/// </para>
/// <para>
/// Encoders are deliberately <em>not</em> reported. Proving an encoder exists means creating a
/// device and querying it, which belongs in <c>flint-engine</c> alongside the code that will use
/// it. Until that probe is wired, this returns <see cref="HostCapabilities.EncodersProbed"/> as
/// <see langword="false"/> so the report says "not asked" rather than inventing either answer.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostProbe : IHostProbe
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const string CurrentVersionKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <inheritdoc />
    public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var adapters = ReadAdapters();
        var primary = adapters.FirstOrDefault(adapter => adapter.DrivesDisplay) ?? adapters.FirstOrDefault();

        return Task.FromResult(new HostCapabilities(
            adapters,
            Encoders: [],
            primary?.Luid ?? 0,
            ReadWindowsBuild(),
            EncodersProbed: false));
    }

    /// <summary>
    /// Reads bound display adapters from the driver registry, and asks Windows which of them are
    /// actually attached to the desktop.
    /// </summary>
    /// <remarks>
    /// The subkey index stands in for a real DXGI LUID. It is stable for the life of a boot, which
    /// is all the capture strategy needs to say "these two are the same adapter", and it avoids
    /// pulling a native dependency into a probe that runs before anything is initialised. The real
    /// LUID arrives with the engine's own enumeration.
    /// </remarks>
    private static List<DisplayAdapter> ReadAdapters()
    {
        var adapters = new List<DisplayAdapter>();
        var attached = DisplayDeviceEnumerator.AttachedAdapterDescriptions();

        using var displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (displayClass is null)
        {
            return adapters;
        }

        foreach (var name in displayClass.GetSubKeyNames())
        {
            if (!int.TryParse(name, out var index))
            {
                continue;
            }

            using var adapterKey = displayClass.OpenSubKey(name);
            if (adapterKey?.GetValue("DriverDesc") is not string description)
            {
                continue;
            }

            adapters.Add(new DisplayAdapter(index, description, attached.Contains(description)));
        }

        return adapters;
    }

    /// <summary>
    /// Reads the Windows build number.
    /// </summary>
    /// <remarks>
    /// Taken from the registry rather than <see cref="Environment.OSVersion"/>, which reports a
    /// shimmed version for applications without a matching compatibility manifest.
    /// </remarks>
    private static int ReadWindowsBuild()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);

        if (key?.GetValue("CurrentBuildNumber") is string text
            && int.TryParse(text, out var build))
        {
            return build;
        }

        return Environment.OSVersion.Version.Build;
    }
}
