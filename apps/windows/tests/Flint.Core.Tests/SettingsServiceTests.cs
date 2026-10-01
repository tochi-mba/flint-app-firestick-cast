using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void ChangeRaisesOnceWithOldAndNew_AndNoOpRaisesNothing()
    {
        var store = new InMemoryAppSettingsStore();
        using var service = new SettingsService(store, (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var changes = new List<SettingsChangedEventArgs>();
        service.Changed += (_, change) => changes.Add(change);

        service.Update(value => value with { General = value.General with { KeepAwake = false } });
        service.Update(value => value);

        changes.Count.ShouldBe(1);
        changes[0].Previous.General.KeepAwake.ShouldBeTrue();
        changes[0].Current.General.KeepAwake.ShouldBeFalse();
        store.Saves.ShouldBe(0);
    }

    [Fact]
    public void DisposeFlushesOnlyTheNewestRapidChange()
    {
        var store = new InMemoryAppSettingsStore();
        var service = new SettingsService(store, (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        for (var percent = 90; percent < 110; percent++)
        {
            service.Update(value => value with { General = value.General with { InterfaceScalePercent = percent } });
        }

        service.Dispose();

        store.Saves.ShouldBe(1);
        store.Saved.General.InterfaceScalePercent.ShouldBe(115);
    }

    [Fact]
    public void AQuietSpell_WritesTheChange()
    {
        var store = new InMemoryAppSettingsStore();
        var waits = new List<TaskCompletionSource>();
        using var service = new SettingsService(store, (delay, token) =>
        {
            delay.ShouldBe(SettingsService.SaveDelay);
            var wait = new TaskCompletionSource();
            token.Register(() => wait.TrySetCanceled(token));
            waits.Add(wait);
            return wait.Task;
        });

        service.Update(value => value with { General = value.General with { KeepAwake = false } });
        service.Update(value => value with { General = value.General with { AskBeforeSwitching = false } });
        waits[0].Task.IsCanceled.ShouldBeTrue("a second change restarts the wait");
        store.Saves.ShouldBe(0);

        waits[1].SetResult();

        store.Saves.ShouldBe(1);
        store.Saved.General.KeepAwake.ShouldBeFalse();
        store.Saved.General.AskBeforeSwitching.ShouldBeFalse();
    }

    [Fact]
    public void Flush_WithNothingWaiting_WritesNothing()
    {
        var store = new InMemoryAppSettingsStore();
        using var service = new SettingsService(store, Never);

        service.Flush();

        store.Saves.ShouldBe(0);
    }

    [Fact]
    public void AChangeThatReturnsNothing_ChangesNothing()
    {
        var store = new InMemoryAppSettingsStore();
        using var service = new SettingsService(store, Never);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Update(_ => null!);

        raised.ShouldBe(0);
        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void TheLoadedSettings_AreNormalised()
    {
        var store = new InMemoryAppSettingsStore(new AppSettings { General = new GeneralSettings { InterfaceScalePercent = 1000 } });
        using var service = new SettingsService(store, Never);

        service.Current.General.InterfaceScalePercent.ShouldBe(130);
    }

    [Fact]
    public void AfterDispose_ChangesAreRefused_AndDisposingAgainIsHarmless()
    {
        var service = new SettingsService(new InMemoryAppSettingsStore());
        service.Dispose();

        Should.Throw<ObjectDisposedException>(() => service.Update(value => value));
        service.Dispose();
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsService(null!));
        using var service = new SettingsService(new InMemoryAppSettingsStore(), Never);
        Should.Throw<ArgumentNullException>(() => service.Update(null!));
        Should.Throw<ArgumentNullException>(() => new InMemoryAppSettingsStore().Save(null!));
    }

    [Fact]
    public void AStoreWithNothingToGive_StartsFromTheDefaults()
    {
        using var service = new SettingsService(new EmptyStore(), Never);

        service.Current.ShouldBe(AppSettings.Default);
    }

    private sealed class EmptyStore : IAppSettingsStore
    {
        public AppSettings Load() => null!;

        public void Save(AppSettings settings)
        {
        }
    }

    private static Task Never(TimeSpan delay, CancellationToken token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
}
