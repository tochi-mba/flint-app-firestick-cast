namespace Flint.Core.Settings;

/// <summary>The live settings every part of Flint reads, and the one way to change them.</summary>
/// <remarks>
/// A change takes effect the moment it is made: whatever cares listens to <see cref="Changed"/>
/// rather than reading the settings once at startup. There is no Save button anywhere in Flint, so
/// nothing may wait for one.
/// </remarks>
public interface ISettingsService
{
    /// <summary>The settings as they are now. Always normalised.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after every change that altered a value, on the thread that made it.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>Applies <paramref name="change"/> to the current settings.</summary>
    /// <remarks>The result is normalised. A change that alters nothing raises nothing and writes nothing.</remarks>
    void Update(Func<AppSettings, AppSettings> change);
}

/// <summary>What a settings change replaced, and with what.</summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>Records one change.</summary>
    /// <param name="previous">The settings before the change.</param>
    /// <param name="current">The settings after it.</param>
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>The settings before the change.</summary>
    public AppSettings Previous { get; }

    /// <summary>The settings after it.</summary>
    public AppSettings Current { get; }
}

/// <summary>Where settings are kept between runs.</summary>
public interface IAppSettingsStore
{
    /// <summary>The saved settings, or the defaults when there are none or they cannot be read.</summary>
    AppSettings Load();

    /// <summary>Keeps <paramref name="settings"/>. A failure to keep them is not an error worth stopping for.</summary>
    void Save(AppSettings settings);
}

/// <summary>A store that keeps settings only for as long as the process runs.</summary>
/// <remarks>For tests and design-time shells, which must not read or write a person's real settings.</remarks>
public sealed class InMemoryAppSettingsStore(AppSettings? initial = null) : IAppSettingsStore
{
    /// <summary>What was last saved, or what the store started with.</summary>
    public AppSettings Saved { get; private set; } = initial ?? AppSettings.Default;

    /// <summary>How many times <see cref="Save"/> was called.</summary>
    public int Saves { get; private set; }

    /// <inheritdoc />
    public AppSettings Load() => Saved;

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Saved = settings;
        Saves++;
    }
}
