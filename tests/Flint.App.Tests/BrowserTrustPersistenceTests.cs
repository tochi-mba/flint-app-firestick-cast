using Flint.App.ViewModels;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Remembering a receiver, so setting one up is a thing you do once.
/// </summary>
/// <remarks>
/// <para>
/// Verification asks a person to compare a fingerprint on the television with one on the desktop.
/// That is a reasonable thing to ask once. Asking it on every launch is not: it is tedious, and it
/// actively erodes the protection it exists to provide, because someone asked to compare codes
/// daily stops comparing and starts clicking.
/// </para>
/// <para>
/// So the pin has to outlive the process. The default store used to be the in-memory one, which
/// meant the desktop met every receiver as a stranger each time it started.
/// </para>
/// </remarks>
public sealed class BrowserTrustPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"flint-trust-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultStore_IsPersistent_NotInMemory()
    {
        // Built without a store on purpose: passing one is what every other test does, which is
        // exactly why an in-memory default went unnoticed. It only showed up on a real desktop, as
        // "why does it keep asking me to verify?"
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        using var browser = new BrowserPageViewModel(shell.Cast);

        browser.TrustStore.ShouldBeOfType<ProtectedFileBrowserTrustStore>();
    }

    [Fact]
    public async Task ATrustedReceiver_IsStillTrusted_AfterAFreshStore()
    {
        // A second store over the same directory stands in for a relaunched app.
        var receiver = new BrowserTrustedReceiver(
            "living-room",
            BrowserFingerprint.ParseFullPin(
                "sha256/" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())),
            DateTimeOffset.UtcNow);

        var first = new ProtectedFileBrowserTrustStore(Path.Combine(directory, "trust.json"));
        await first.SaveAsync(receiver);

        var second = new ProtectedFileBrowserTrustStore(Path.Combine(directory, "trust.json"));
        var found = await second.FindAsync("living-room");

        found.ShouldNotBeNull();
        found.Fingerprint.FullPin.ShouldBe(receiver.Fingerprint.FullPin);
    }

    [Fact]
    public async Task ForgettingAReceiver_RequiresVerificationAgain()
    {
        // The escape hatch has to work: a receiver that was reinstalled has a new identity, and the
        // user must be able to say so rather than being stuck with a mismatch forever.
        var receiver = new BrowserTrustedReceiver(
            "living-room",
            BrowserFingerprint.ParseFullPin(
                "sha256/" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())),
            DateTimeOffset.UtcNow);

        var store = new ProtectedFileBrowserTrustStore(Path.Combine(directory, "trust.json"));
        await store.SaveAsync(receiver);
        await store.ForgetAsync("living-room");

        (await store.FindAsync("living-room")).ShouldBeNull();
    }

    [Fact]
    public async Task AnUnknownReceiver_IsNotTrusted()
    {
        var store = new ProtectedFileBrowserTrustStore(Path.Combine(directory, "trust.json"));

        (await store.FindAsync("never-seen")).ShouldBeNull();
    }
}
