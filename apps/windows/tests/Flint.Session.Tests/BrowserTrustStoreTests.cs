using Flint.Session.Browser;
using Shouldly;

namespace Flint.Session.Tests;

public sealed class BrowserTrustStoreTests
{
    [Fact]
    public async Task InMemoryStore_UsesTheReceiverIdentityAndSupportsExplicitForget()
    {
        var store = new InMemoryBrowserTrustStore();
        var fingerprint = BrowserFingerprint.FromSubjectPublicKeyInfo([1, 2, 3]);
        var trusted = new BrowserTrustedReceiver("receiver-a", fingerprint, DateTimeOffset.UnixEpoch);

        await store.SaveAsync(trusted, TestContext.Current.CancellationToken);

        (await store.FindAsync("receiver-a", TestContext.Current.CancellationToken)).ShouldBe(trusted);
        (await store.FindAsync("receiver-b", TestContext.Current.CancellationToken)).ShouldBeNull();

        await store.ForgetAsync("receiver-a", TestContext.Current.CancellationToken);

        (await store.FindAsync("receiver-a", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task FileStore_RoundTripsAnAtomicTrustEntryAndDetectsTampering()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"flint-browser-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "browser-trust.bin");
        try
        {
            var fingerprint = BrowserFingerprint.FromSubjectPublicKeyInfo([4, 5, 6]);
            var trusted = new BrowserTrustedReceiver("receiver-a", fingerprint, DateTimeOffset.UnixEpoch);
            var store = new ProtectedFileBrowserTrustStore(path);

            await store.SaveAsync(trusted, TestContext.Current.CancellationToken);
            (await new ProtectedFileBrowserTrustStore(path).FindAsync("receiver-a", TestContext.Current.CancellationToken))
                .ShouldBe(trusted);

            await File.WriteAllBytesAsync(path, [0x01, 0x02], TestContext.Current.CancellationToken);
            await Should.ThrowAsync<BrowserTrustStoreCorruptException>(async () =>
                await store.FindAsync("receiver-a", TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
