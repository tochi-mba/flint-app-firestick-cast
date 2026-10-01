using Flint.App.Services;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

public sealed class FileAppSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "flint-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingInvalidAndWrongShapeFilesLoadDefaults()
    {
        var store = new FileAppSettingsStore(directory);
        store.Load().ShouldBe(AppSettings.Default);

        Directory.CreateDirectory(directory);
        foreach (var text in new[] { string.Empty, "broken", "[]" })
        {
            File.WriteAllText(Path.Combine(directory, FileAppSettingsStore.FileName), text);
            store.Load().ShouldBe(AppSettings.Default);
        }
    }

    [Fact]
    public void SaveCreatesTheDirectory_RoundTripsEverySection_AndLeavesNoTemporaryFile()
    {
        var store = new FileAppSettingsStore(directory);
        var value = new AppSettings
        {
            General = new GeneralSettings { KeepAwake = false },
            Screen = new ScreenSettings { ShareSound = false },
            Media = new MediaSettings { Shuffle = true },
            Shortcuts = new ShortcutSettings { Disconnect = "Ctrl+D" },
            Tray = new TraySettings { SingleClickOpens = false },
        };

        store.Save(value);

        store.Load().ShouldBe(value.Normalize());
        File.Exists(Path.Combine(directory, FileAppSettingsStore.FileName + ".tmp")).ShouldBeFalse();
    }

    [Fact]
    public void AnAbandonedTemporaryFileDoesNotBlockTheNextSave()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileAppSettingsStore.FileName + ".tmp"), "half a write");
        var store = new FileAppSettingsStore(directory);

        store.Save(new AppSettings { General = new GeneralSettings { InterfaceScalePercent = 130 } });

        store.Load().General.InterfaceScalePercent.ShouldBe(130);
        File.Exists(Path.Combine(directory, FileAppSettingsStore.FileName + ".tmp")).ShouldBeFalse();
    }

    [Fact]
    public void StoredJsonContainsNoSessionSecretsOrMediaPaths()
    {
        var store = new FileAppSettingsStore(directory);
        store.Save(AppSettings.Default);
        var text = File.ReadAllText(Path.Combine(directory, FileAppSettingsStore.FileName));

        text.ShouldNotContain("token", Case.Insensitive);
        text.ShouldNotContain("pairing", Case.Insensitive);
        text.ShouldNotContain("mediaFile", Case.Insensitive);
    }

    [Fact]
    public void TheDefaultStore_LivesInFlintsDataFolder() =>
        new FileAppSettingsStore().FilePath.ShouldBe(Path.Combine(FlintDataFolder.Path, FileAppSettingsStore.FileName));

    [Fact]
    public void AFileHeldByAnotherProgram_LoadsAsDefaults()
    {
        var store = new FileAppSettingsStore(directory);
        store.Save(new AppSettings { General = new GeneralSettings { KeepAwake = false } });
        using var held = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        store.Load().ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void ASaveWhoseFolderCannotBeMade_IsSwallowed()
    {
        // A file where the folder should be: neither the folder nor the temporary file can be made,
        // and cleaning up a temporary file that never existed must not throw either.
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        File.WriteAllText(directory, "in the way");
        try
        {
            Should.NotThrow(() => new FileAppSettingsStore(directory).Save(AppSettings.Default));
        }
        finally
        {
            File.Delete(directory);
        }
    }

    [Fact]
    public void ASaveThatCannotReplaceTheFile_KeepsWhatWasThere_AndRemovesItsTemporaryFile()
    {
        // A folder where the settings file should be cannot be replaced by a file.
        var store = new FileAppSettingsStore(directory);
        Directory.CreateDirectory(store.FilePath);

        Should.NotThrow(() => store.Save(AppSettings.Default));

        Directory.Exists(store.FilePath).ShouldBeTrue();
        File.Exists(store.FilePath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        Should.Throw<ArgumentException>(() => new FileAppSettingsStore(" "));
        Should.Throw<ArgumentNullException>(() => new FileAppSettingsStore(directory).Save(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
