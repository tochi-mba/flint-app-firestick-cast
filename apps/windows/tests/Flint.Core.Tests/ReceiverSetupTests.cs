using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// The receiver button's decision table. Nothing is installed or replaced on a guess: an unknown
/// version is opened rather than updated, and a newer build is never downgraded.
/// </summary>
public sealed class ReceiverSetupTests
{
    private static readonly BundledReceiver Bundled = new(BundledReceiver.DebugPackage, "0.1.0-debug", 1118, 12 * 1024 * 1024);

    [Fact]
    public void Decide_NothingInstalled_Installs()
    {
        ReceiverSetup.Decide(Bundled, installed: null).ShouldBe(ReceiverSetupAction.Install);
    }

    [Fact]
    public void Decide_NothingInstalledAndNothingBundled_HasNothingToOffer()
    {
        ReceiverSetup.Decide(bundled: null, installed: null).ShouldBe(ReceiverSetupAction.NothingBundled);
    }

    [Theory]
    [InlineData(1117L, ReceiverSetupAction.Update)]
    [InlineData(1118L, ReceiverSetupAction.Open)]
    [InlineData(1119L, ReceiverSetupAction.OpenNewer)]
    public void Decide_SamePackage_ComparesVersionCodes(long installedCode, ReceiverSetupAction expected)
    {
        var installed = new InstalledReceiver(Bundled.PackageName, "0.1.0-debug", installedCode);

        ReceiverSetup.Decide(Bundled, installed).ShouldBe(expected);
    }

    [Fact]
    public void Decide_UnknownInstalledVersion_OpensRatherThanUpdating()
    {
        var installed = new InstalledReceiver(Bundled.PackageName, VersionName: null, VersionCode: null);

        ReceiverSetup.Decide(Bundled, installed).ShouldBe(ReceiverSetupAction.OpenUnverified);
    }

    [Fact]
    public void Decide_DifferentPackage_OpensWithoutOfferingAnUpdate()
    {
        var installed = new InstalledReceiver(BundledReceiver.ReleasePackage, "0.1.0", 2000);

        ReceiverSetup.Decide(Bundled, installed).ShouldBe(ReceiverSetupAction.OpenDifferentPackage);
    }

    [Fact]
    public void Decide_InstalledButNothingBundled_OpensWhatIsThere()
    {
        var installed = new InstalledReceiver(BundledReceiver.ReleasePackage, "0.1.0", 2000);

        ReceiverSetup.Decide(bundled: null, installed).ShouldBe(ReceiverSetupAction.OpenUnverified);
    }

    [Theory]
    [InlineData(ReceiverSetupAction.Install, "INSTALL FLINT ON TV", true, true)]
    [InlineData(ReceiverSetupAction.Update, "UPDATE FLINT ON TV", true, true)]
    [InlineData(ReceiverSetupAction.Open, "OPEN RECEIVER ON TV", false, true)]
    [InlineData(ReceiverSetupAction.OpenNewer, "OPEN RECEIVER ON TV", false, true)]
    [InlineData(ReceiverSetupAction.OpenUnverified, "OPEN RECEIVER ON TV", false, true)]
    [InlineData(ReceiverSetupAction.OpenDifferentPackage, "OPEN RECEIVER ON TV", false, true)]
    [InlineData(ReceiverSetupAction.Unknown, "OPEN RECEIVER ON TV", false, false)]
    [InlineData(ReceiverSetupAction.NothingBundled, "OPEN RECEIVER ON TV", false, false)]
    public void EveryAction_HasALabelAndKnowsWhetherItInstalls(
        ReceiverSetupAction action,
        string label,
        bool installs,
        bool actionable)
    {
        ReceiverSetup.ActionLabel(action).ShouldBe(label);
        ReceiverSetup.Installs(action).ShouldBe(installs);
        ReceiverSetup.IsActionable(action).ShouldBe(actionable);
    }

    [Fact]
    public void Describe_Install_DisclosesExactlyWhatWillBeInstalled()
    {
        var text = ReceiverSetup.Describe(ReceiverSetupAction.Install, Bundled, null, "Living Room");

        text.ShouldContain("Living Room");
        text.ShouldContain(BundledReceiver.DebugPackage);
        text.ShouldContain("0.1.0-debug (1118)");
        text.ShouldContain("12.0 MB");
        text.ShouldContain("same commit");
        text.ShouldContain("debug build");
    }

    [Fact]
    public void Describe_Update_NamesBothVersions()
    {
        var installed = new InstalledReceiver(Bundled.PackageName, "0.1.0-debug", 1100);

        var text = ReceiverSetup.Describe(ReceiverSetupAction.Update, Bundled, installed, "Living Room");

        text.ShouldContain("0.1.0-debug (1100)");
        text.ShouldContain("0.1.0-debug (1118)");
        text.ShouldContain("update it in place");
    }

    [Fact]
    public void Describe_Newer_SaysItWillNotDowngrade()
    {
        var installed = new InstalledReceiver(Bundled.PackageName, "0.2.0-debug", 1500);

        ReceiverSetup.Describe(ReceiverSetupAction.OpenNewer, Bundled, installed, "Living Room")
            .ShouldContain("will not downgrade");
    }

    [Fact]
    public void Describe_DifferentPackage_SaysWhyItCannotUpdateAndWhatToDo()
    {
        var installed = new InstalledReceiver(BundledReceiver.ReleasePackage, "0.1.0", 2000);

        var text = ReceiverSetup.Describe(ReceiverSetupAction.OpenDifferentPackage, Bundled, installed, "Living Room");

        text.ShouldContain("different package");
        text.ShouldContain("Remove the other one");
    }

    [Fact]
    public void Describe_UnknownAndNothingBundled_SayWhatIsMissing()
    {
        ReceiverSetup.Describe(ReceiverSetupAction.Unknown, null, null, "Living Room")
            .ShouldContain("not identified");
        ReceiverSetup.Describe(ReceiverSetupAction.NothingBundled, null, null, "Living Room")
            .ShouldContain("carries no receiver");
        ReceiverSetup.Describe(ReceiverSetupAction.OpenUnverified, null, new InstalledReceiver("x.y", null, null), "TV")
            .ShouldContain("did not report a version");
        ReceiverSetup.Describe(ReceiverSetupAction.Open, Bundled, new InstalledReceiver(Bundled.PackageName, "0.1.0-debug", 1118), "TV")
            .ShouldContain("matches this PC");
    }

    [Fact]
    public void Describe_RejectsABlankDeviceName()
    {
        Should.Throw<ArgumentException>(() => ReceiverSetup.Describe(ReceiverSetupAction.Install, Bundled, null, " "));
    }

    [Theory]
    [InlineData("0.1.0", 7L, "0.1.0 (7)")]
    [InlineData("0.1.0", null, "0.1.0")]
    [InlineData(null, 7L, "build 7")]
    [InlineData(null, null, "version unknown")]
    public void InstalledReceiver_VersionLabel_ReadsWhatTheTvGave(string? name, long? code, string expected)
    {
        new InstalledReceiver("p", name, code).VersionLabel.ShouldBe(expected);
    }

    [Fact]
    public void BundledReceiver_KnowsItsSizeAndBuildType()
    {
        Bundled.SizeLabel.ShouldBe("12.0 MB");
        Bundled.IsDebugPackage.ShouldBeTrue();
        (Bundled with { PackageName = BundledReceiver.ReleasePackage }).IsDebugPackage.ShouldBeFalse();
        BundledReceiver.KnownPackages.ShouldBe([BundledReceiver.ReleasePackage, BundledReceiver.DebugPackage], ignoreOrder: true);
    }
}
