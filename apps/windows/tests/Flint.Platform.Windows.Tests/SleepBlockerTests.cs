using Shouldly;

namespace Flint.Platform.Windows.Tests;

public sealed class SleepBlockerTests
{
    [Fact]
    public void LevelsUseTheExactWindowsFlags()
    {
        SleepBlocker.FlagsFor(KeepAwakeLevel.None).ShouldBe(SleepBlocker.Continuous);
        SleepBlocker.FlagsFor(KeepAwakeLevel.System).ShouldBe(SleepBlocker.Continuous | SleepBlocker.SystemRequired);
        SleepBlocker.FlagsFor(KeepAwakeLevel.SystemAndDisplay).ShouldBe(
            SleepBlocker.Continuous | SleepBlocker.SystemRequired | SleepBlocker.DisplayRequired);
    }

    [Fact]
    public void ApplySendsOnlyChanges_AndDisposeClearsTheRequest()
    {
        var api = new RecordingApi();
        var blocker = new SleepBlocker(api);

        blocker.Apply(KeepAwakeLevel.System).ShouldBeTrue();
        blocker.Apply(KeepAwakeLevel.System).ShouldBeTrue();
        blocker.Apply(KeepAwakeLevel.SystemAndDisplay).ShouldBeTrue();
        blocker.Dispose();

        api.Flags.ShouldBe([
            SleepBlocker.Continuous | SleepBlocker.SystemRequired,
            SleepBlocker.Continuous | SleepBlocker.SystemRequired | SleepBlocker.DisplayRequired,
            SleepBlocker.Continuous,
        ]);
    }

    [Fact]
    public void RefusalLeavesThePreviousLevelInForce()
    {
        var blocker = new SleepBlocker(new RecordingApi { Result = false });

        blocker.Apply(KeepAwakeLevel.System).ShouldBeFalse();

        blocker.Level.ShouldBe(KeepAwakeLevel.None);
    }

    [Fact]
    public void UnknownLevelIsRefusedBeforeCallingWindows()
    {
        var api = new RecordingApi();
        var blocker = new SleepBlocker(api);

        Should.Throw<ArgumentOutOfRangeException>(() => blocker.Apply((KeepAwakeLevel)99));
        api.Flags.ShouldBeEmpty();
    }

    private sealed class RecordingApi : IExecutionStateApi
    {
        public List<uint> Flags { get; } = [];
        public bool Result { get; init; } = true;

        public bool TrySet(uint flags)
        {
            Flags.Add(flags);
            return Result;
        }
    }

    [Fact]
    public void ABlockerNeedsSomewhereToSendItsRequest() =>
        Should.Throw<ArgumentNullException>(() => new SleepBlocker(null!));
}
