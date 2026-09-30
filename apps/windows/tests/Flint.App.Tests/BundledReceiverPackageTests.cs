using System.Net;
using System.Security.Cryptography;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The bundled receiver is described only while the APK beside the executable is provably the
/// file the packager described; anything else is "no receiver bundled", never a guess.
/// </summary>
public sealed class BundledReceiverPackageTests : IDisposable
{
    private static readonly byte[] Apk = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8];
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "flint-bundled-receiver-tests",
        Guid.NewGuid().ToString("N"));

    public BundledReceiverPackageTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AMatchingApkAndSidecar_DescribeTheBundleAndReadItsBytes()
    {
        WriteApk();
        WriteSidecar(Apk);

        var package = new BundledReceiverPackage(directory);
        var described = package.Describe();
        var bytes = await package.ReadAsync(TestContext.Current.CancellationToken);

        described.ShouldNotBeNull();
        described.PackageName.ShouldBe(BundledReceiver.DebugPackage);
        described.VersionName.ShouldBe("0.1.0-debug");
        described.VersionCode.ShouldBe(1118);
        described.SizeBytes.ShouldBe(Apk.Length);
        bytes.ShouldBe(Apk);
        package.Describe().ShouldBeSameAs(described, "the look is taken once");
    }

    [Fact]
    public void NoFiles_MeansNothingIsBundled()
    {
        new BundledReceiverPackage(directory).Describe().ShouldBeNull();
        new BundledReceiverPackage(Path.Combine(directory, "missing")).Describe().ShouldBeNull();
    }

    [Fact]
    public void ASidecarWithoutItsApk_DescribesNothing()
    {
        WriteSidecar(Apk);

        new BundledReceiverPackage(directory).Describe().ShouldBeNull();
    }

    [Fact]
    public void AnApkThatNoLongerMatchesTheDigest_DescribesNothing()
    {
        WriteApk();
        WriteSidecar([9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9]);

        new BundledReceiverPackage(directory).Describe().ShouldBeNull();
    }

    [Fact]
    public void AnApkOfAnotherSize_DescribesNothing()
    {
        WriteApk();
        WriteSidecar(Apk, sizeBytes: Apk.Length + 1);

        new BundledReceiverPackage(directory).Describe().ShouldBeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"packageName":"","versionName":"0.1.0","versionCode":1,"sizeBytes":12,"sha256":"00"}""")]
    [InlineData("""{"packageName":"p","versionName":" ","versionCode":1,"sizeBytes":12,"sha256":"00"}""")]
    [InlineData("""{"packageName":"p","versionName":"0.1.0","versionCode":-1,"sizeBytes":12,"sha256":"00"}""")]
    [InlineData("""{"packageName":"p","versionName":"0.1.0","versionCode":1,"sizeBytes":12,"sha256":""}""")]
    public void AnUnreadableOrIncompleteSidecar_DescribesNothing(string sidecar)
    {
        WriteApk();
        File.WriteAllText(Path.Combine(directory, BundledReceiverPackage.SidecarFileName), sidecar);

        new BundledReceiverPackage(directory).Describe().ShouldBeNull();
    }

    [Fact]
    public async Task ReadingWithNothingBundled_Throws()
    {
        var package = new BundledReceiverPackage(directory);

        await Should.ThrowAsync<InvalidOperationException>(() => package.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadingAnApkThatChangedAfterItWasDescribed_Throws()
    {
        WriteApk();
        WriteSidecar(Apk);
        var package = new BundledReceiverPackage(directory);
        package.Describe().ShouldNotBeNull();
        File.WriteAllBytes(Path.Combine(directory, BundledReceiverPackage.ApkFileName), [1, 2, 3]);

        await Should.ThrowAsync<InvalidOperationException>(() => package.ReadAsync(TestContext.Current.CancellationToken));
    }

    private void WriteApk() =>
        File.WriteAllBytes(Path.Combine(directory, BundledReceiverPackage.ApkFileName), Apk);

    private void WriteSidecar(byte[] digestOf, long? sizeBytes = null)
    {
        var digest = Convert.ToHexString(SHA256.HashData(digestOf)).ToLowerInvariant();
        File.WriteAllText(
            Path.Combine(directory, BundledReceiverPackage.SidecarFileName),
            $$"""{"packageName":"{{BundledReceiver.DebugPackage}}","versionName":"0.1.0-debug","versionCode":1118,"sizeBytes":{{sizeBytes ?? Apk.Length}},"sha256":"{{digest}}"}""");
    }
}

/// <summary>The ADB-backed installer and launcher refuse a television that has not authorised the PC.</summary>
public sealed class ReceiverAdbGuardTests
{
    private static readonly FireTvDevice Unauthorised =
        new(IPAddress.Loopback, "TV", DiscoverySource.Manual) { AdbState = AdbConnectionState.Unauthorized, AdbPort = 5555 };

    /// <summary>Authorised, but at an address nothing can connect to: the call reaches ADB and fails there.</summary>
    private static readonly FireTvDevice AuthorisedButUnreachable =
        new(IPAddress.Any, "TV", DiscoverySource.Manual) { AdbState = AdbConnectionState.Connected, AdbPort = 5555 };

    [Fact]
    public async Task AnAuthorisedTv_IsAskedOverAdb()
    {
        var installer = new AdbReceiverInstaller(new AdbProbeClient());

        await Should.ThrowAsync<IOException>(() =>
            installer.FindInstalledAsync(AuthorisedButUnreachable, [BundledReceiver.DebugPackage], TestContext.Current.CancellationToken));
        await Should.ThrowAsync<IOException>(() =>
            installer.InstallAsync(AuthorisedButUnreachable, new byte[1], null, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<IOException>(() =>
            new AdbReceiverLauncher(new AdbProbeClient()).LaunchAsync(AuthorisedButUnreachable, BundledReceiver.DebugPackage, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAuthorisedTvWithNoKnownPort_IsRefusedBeforeConnecting()
    {
        var noPort = AuthorisedButUnreachable with { AdbPort = null };

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new AdbReceiverInstaller().FindInstalledAsync(noPort, [BundledReceiver.DebugPackage], TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            new AdbReceiverLauncher().LaunchAsync(noPort, BundledReceiver.DebugPackage, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheInstaller_RefusesAnUnauthorisedTv()
    {
        var installer = new AdbReceiverInstaller();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            installer.FindInstalledAsync(Unauthorised, [BundledReceiver.DebugPackage], TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            installer.InstallAsync(Unauthorised, new byte[1], null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheLauncher_RefusesAnUnauthorisedTvAndABlankPackage()
    {
        var launcher = new AdbReceiverLauncher();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            launcher.LaunchAsync(Unauthorised, BundledReceiver.DebugPackage, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentException>(() =>
            launcher.LaunchAsync(Unauthorised, " ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheOfflineInstaller_SaysItCannotLookRatherThanAnswering()
    {
        var offline = new OfflineReceiverInstaller();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            offline.FindInstalledAsync(Unauthorised, [BundledReceiver.DebugPackage], TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            offline.InstallAsync(Unauthorised, new byte[1], null, TestContext.Current.CancellationToken));
    }
}
