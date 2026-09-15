using System.Text;
using System.Security.Cryptography;
using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserProfileLibraryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "flint-browser-profile-library-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void NamedProfilesAndSelectionSurviveRestartWithoutSharingPages()
    {
        var store = new FileBrowserProfileLibraryStore(directory);
        var work = store.CreateDeviceProfile("Work");
        work.ShouldNotBeNull();
        store.CreateDeviceProfile("work").ShouldBeNull();
        store.SelectDeviceProfile(work!.Value).ShouldBeTrue();
        store.SaveSession(work.Value, new BrowserSavedSession([new("https://work.test/")], 0, [], 0)).ShouldBeTrue();
        var restarted = new FileBrowserProfileLibraryStore(directory);
        restarted.DeviceProfiles().Selected.ShouldBe(work.Value);
        restarted.DeviceProfiles().Profiles.Select(p => p.Name).ShouldBe(["Personal", "Work"]);
        restarted.Session(BrowserProfileLibraryStoreLocation.CurrentDeviceProfile).Tabs.ShouldBeEmpty();
        restarted.Session(work.Value).Tabs.Single().Url.ShouldBe("https://work.test/");
    }

    [Fact]
    public void Session_RestartPreservesBothModesAndKeepsProfilesIndependent()
    {
        var alice = new BrowserProfileId(Guid.NewGuid());
        var bob = new BrowserProfileId(Guid.NewGuid());
        var store = new FileBrowserProfileLibraryStore(directory);
        var saved = new BrowserSavedSession(
            [new("https://example.test/tab")], 0,
            [new("https://example.test/left"), new("https://example.test/right", "Right", true)],
            1, "TwoRows", 7000, 3000, true);
        store.SaveSession(alice, saved).ShouldBeTrue();
        store.AddBookmark(alice, "https://example.test/bookmark", "Saved").ShouldBeTrue();
        var restarted = new FileBrowserProfileLibraryStore(directory);
        restarted.Session(alice).SameAs(saved).ShouldBeTrue();
        restarted.Session(bob).SameAs(BrowserSavedSession.Empty).ShouldBeTrue();
        restarted.ClearHistory(alice).ShouldBeTrue();
        new FileBrowserProfileLibraryStore(directory).Session(alice).SameAs(saved).ShouldBeTrue();
        restarted.Session(alice).Tabs[0] = new("https://changed.test/");
        restarted.Session(alice).Tabs[0].Url.ShouldBe("https://example.test/tab");
    }

    [Fact]
    public void Session_RejectsCredentialUrlsAndBoundsGeometry()
    {
        var normalized = new BrowserSavedSession(
            [new("https://user:password@example.test/"), new("javascript:alert(1)"), new("about:blank")],
            100, [new("https://example.test/")], -1, "garbage", int.MaxValue, int.MinValue, true).Normalize();
        normalized.Tabs.Select(p => p.Url).ShouldBe(["about:blank"]);
        normalized.ActiveTab.ShouldBe(0);
        normalized.ActivePane.ShouldBe(0);
        normalized.Layout.ShouldBe("Single");
        normalized.Column.ShouldBe(8500);
        normalized.Row.ShouldBe(1500);
    }

    [Fact]
    public void FileStore_PersistsEachProfileIndependently()
    {
        var alice = Profile("10000000-0000-0000-0000-000000000001");
        var bob = Profile("20000000-0000-0000-0000-000000000002");
        var store = new FileBrowserProfileLibraryStore(directory);

        store.AddBookmark(alice, "HTTPS://Example.Test:443/a/../saved", "Alice", visitedAt: At(10)).ShouldBeTrue();
        store.RecordVisit(alice, "https://history.test/", "History", visitedAt: At(20)).ShouldBeTrue();
        store.AddBookmark(bob, "https://example.test/saved", "Bob", visitedAt: At(30)).ShouldBeTrue();

        var reopened = new FileBrowserProfileLibraryStore(directory);
        reopened.Snapshot(alice).Bookmarks.Single().Title.ShouldBe("Alice");
        reopened.Snapshot(alice).History.Single().Title.ShouldBe("History");
        reopened.Snapshot(bob).Bookmarks.Single().Title.ShouldBe("Bob");
        reopened.Snapshot(bob).History.ShouldBeEmpty();
        reopened.Snapshot(alice).Bookmarks.Single().Url.ShouldBe("https://example.test/saved");
    }

    [Fact]
    public void FileStore_ProtectsBrowsingDataToTheCurrentWindowsUser()
    {
        var profile = Profile("11000000-0000-0000-0000-000000000001");
        var store = new FileBrowserProfileLibraryStore(directory);

        store.AddBookmark(profile, "https://private.example/secret", "Private title", visitedAt: At(10))
            .ShouldBeTrue();

        var bytes = File.ReadAllBytes(ProfilePath(profile));
        var visible = Encoding.UTF8.GetString(bytes);
        visible.ShouldNotContain("private.example", Case.Insensitive);
        visible.ShouldNotContain("Private title", Case.Sensitive);
        var plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Encoding.UTF8.GetString(plain).ShouldContain("private.example");
        CryptographicOperations.ZeroMemory(plain);
    }

    [Fact]
    public void AddBookmark_CanonicalizesAndDeduplicatesNewestFirst()
    {
        var profile = Profile("30000000-0000-0000-0000-000000000003");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();

        store.AddBookmark(profile, "HTTPS://Example.Test:443/a/../page?q=1#part", "Old", visitedAt: At(10)).ShouldBeTrue();
        store.AddBookmark(profile, "https://example.test/page?q=1#part", "New", visitedAt: At(30)).ShouldBeTrue();
        store.AddBookmark(profile, "https://second.test/", "Second", visitedAt: At(20)).ShouldBeTrue();

        var bookmarks = store.Snapshot(profile).Bookmarks;
        bookmarks.Select(item => item.Title).ShouldBe(["New", "Second"]);
        bookmarks[0].Url.ShouldBe("https://example.test/page?q=1#part");
    }

    [Fact]
    public void RecordVisit_DeduplicatesWithoutChangingBookmarks()
    {
        var profile = Profile("40000000-0000-0000-0000-000000000004");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();
        store.AddBookmark(profile, "https://example.test/", "Saved", faviconId: 4, visitedAt: At(5));

        store.RecordVisit(profile, "https://example.test/", "First", faviconId: 5, visitedAt: At(10)).ShouldBeTrue();
        store.RecordVisit(profile, "HTTPS://EXAMPLE.TEST:443", "Latest", faviconId: 6, visitedAt: At(20)).ShouldBeTrue();

        var snapshot = store.Snapshot(profile);
        snapshot.Bookmarks.Single().Title.ShouldBe("Saved");
        snapshot.Bookmarks.Single().FaviconId.ShouldBe(4);
        snapshot.History.ShouldHaveSingleItem();
        snapshot.History.Single().Title.ShouldBe("Latest");
        snapshot.History.Single().FaviconId.ShouldBe(6);
    }

    [Fact]
    public void Collections_StayWithinTheWireCompatibleBounds()
    {
        var profile = Profile("50000000-0000-0000-0000-000000000005");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();

        for (var index = 0; index < BrowserProfileLibraryLimits.MaxBookmarks + 5; index++)
        {
            store.AddBookmark(profile, $"https://b{index}.test/", $"B{index}", visitedAt: At(index));
        }
        for (var index = 0; index < BrowserProfileLibraryLimits.MaxHistoryEntries + 5; index++)
        {
            store.RecordVisit(profile, $"https://h{index}.test/", $"H{index}", visitedAt: At(index));
        }

        var snapshot = store.Snapshot(profile);
        snapshot.Bookmarks.Count.ShouldBe(200);
        snapshot.History.Count.ShouldBe(255);
        snapshot.Bookmarks[0].Title.ShouldBe("B204");
        snapshot.History[0].Title.ShouldBe("H259");
        snapshot.Bookmarks[^1].Title.ShouldBe("B5");
        snapshot.History[^1].Title.ShouldBe("H5");
    }

    public static TheoryData<string> RejectedAddresses => new()
    {
        "",
        "http://example.test/",
        "file:///windows/system32",
        "javascript:alert(1)",
        "https://localhost/",
        "https://printer/",
        "https://device.local/",
        "https://device.lan/",
        "https://device.internal/",
        "https://device.home/",
        "https://device.localdomain/",
        "https://127.0.0.1/",
        "https://[::1]/",
        "https://user@example.test/",
        "https://appassets.androidplatform.net/assets/flint-browser-fixture.html",
        "https://example.test/a b",
        "https://example.test/\nsecret",
        "https://" + new string('a', BrowserProfileLibraryLimits.MaxUrlBytes) + ".test/",
    };

    [Theory]
    [MemberData(nameof(RejectedAddresses))]
    public void Mutations_RejectAddressesTheReceiverWouldNotOpen(string address)
    {
        var profile = Profile("60000000-0000-0000-0000-000000000006");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();

        store.AddBookmark(profile, address, "Blocked").ShouldBeFalse();
        store.RecordVisit(profile, address, "Blocked").ShouldBeFalse();
        store.RemoveBookmark(profile, address).ShouldBeFalse();
        store.UpdateFavicon(profile, address, 1).ShouldBeFalse();
        store.Snapshot(profile).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
    }

    [Fact]
    public void Mutations_RejectUnpairedUnicodeBeforeItCanBePersisted()
    {
        var profile = Profile("61000000-0000-0000-0000-000000000006");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();
        var address = "https://example.test/\uD800";

        store.AddBookmark(profile, address, "Blocked").ShouldBeFalse();
        store.RecordVisit(profile, address, "Blocked").ShouldBeFalse();
        store.Snapshot(profile).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
    }

    [Fact]
    public void PublicInternationalAddress_IsStoredInReceiverCanonicalForm()
    {
        var profile = Profile("70000000-0000-0000-0000-000000000007");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();

        store.AddBookmark(
            profile,
            "https://B\u00dcCHER.example:443/a/../b?x=1#result",
            "Books",
            visitedAt: At(1)).ShouldBeTrue();

        store.Snapshot(profile).Bookmarks.Single().Url
            .ShouldBe("https://xn--bcher-kva.example/b?x=1#result");
    }

    [Fact]
    public void Titles_ArePrintableCollapsedAndCappedByWireUtf8Bytes()
    {
        var profile = Profile("80000000-0000-0000-0000-000000000008");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();
        var longTitle = "  Before\n\t\u0000  \uD800After  " + string.Concat(Enumerable.Repeat("\U0001F642", 200));

        store.AddBookmark(profile, "https://example.test/", longTitle, visitedAt: At(1)).ShouldBeTrue();

        var title = store.Snapshot(profile).Bookmarks.Single().Title;
        title.ShouldStartWith("Before \uFFFD After ");
        title.ShouldNotContain('\n');
        title.ShouldNotContain('\0');
        Encoding.UTF8.GetByteCount(title).ShouldBeLessThanOrEqualTo(BrowserProfileLibraryLimits.MaxTitleBytes);
        title.EndsWith('\uFFFD').ShouldBeFalse();
    }

    [Fact]
    public void BlankSanitizedTitle_FallsBackToABoundedCanonicalAddress()
    {
        var profile = Profile("90000000-0000-0000-0000-000000000009");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();
        var longPath = new string('x', 700);

        store.RecordVisit(profile, $"https://example.test/{longPath}", " \r\n\t ", visitedAt: At(1)).ShouldBeTrue();

        var title = store.Snapshot(profile).History.Single().Title;
        title.ShouldStartWith("https://example.test/");
        Encoding.UTF8.GetByteCount(title).ShouldBeLessThanOrEqualTo(BrowserProfileLibraryLimits.MaxTitleBytes);
    }

    [Fact]
    public void UpdateFavicon_UpdatesBothMatchingCollectionsAndPersists()
    {
        var profile = Profile("a0000000-0000-0000-0000-00000000000a");
        var store = new FileBrowserProfileLibraryStore(directory);
        store.AddBookmark(profile, "https://example.test/", "Saved", visitedAt: At(1));
        store.RecordVisit(profile, "https://example.test/", "Visited", visitedAt: At(2));

        store.UpdateFavicon(profile, "HTTPS://EXAMPLE.TEST:443/", 42).ShouldBeTrue();
        store.UpdateFavicon(profile, "https://example.test/", 42).ShouldBeFalse();
        store.UpdateFavicon(profile, "https://missing.test/", 9).ShouldBeFalse();
        store.UpdateFavicon(profile, "https://example.test/", -1).ShouldBeFalse();

        var reopened = new FileBrowserProfileLibraryStore(directory).Snapshot(profile);
        reopened.Bookmarks.Single().FaviconId.ShouldBe(42);
        reopened.History.Single().FaviconId.ShouldBe(42);
    }

    [Fact]
    public void RemoveAndClearOperations_AreProfileScopedAndDurable()
    {
        var first = Profile("b0000000-0000-0000-0000-00000000000b");
        var second = Profile("c0000000-0000-0000-0000-00000000000c");
        var store = new FileBrowserProfileLibraryStore(directory);
        foreach (var profile in new[] { first, second })
        {
            store.AddBookmark(profile, "https://example.test/", "Saved", visitedAt: At(1));
            store.RecordVisit(profile, "https://example.test/", "Visited", visitedAt: At(2));
        }

        store.RemoveBookmark(first, "https://example.test/").ShouldBeTrue();
        store.RemoveBookmark(first, "https://example.test/").ShouldBeFalse();
        store.ClearHistory(first).ShouldBeTrue();

        // Re-open for the durability read, then mutate through that instance so the write path is
        // exercised against a freshly loaded cache (the original in-memory view is not the point).
        var reopened = new FileBrowserProfileLibraryStore(directory);
        reopened.Snapshot(first).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
        reopened.Snapshot(second).Bookmarks.ShouldHaveSingleItem();
        reopened.Snapshot(second).History.ShouldHaveSingleItem();

        reopened.ClearBookmarks(second).ShouldBeTrue();
        reopened.ClearAll(second).ShouldBeTrue();
        reopened = new FileBrowserProfileLibraryStore(directory);
        reopened.Snapshot(second).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
    }

    [Fact]
    public void Snapshot_IsDefensiveAgainstCallerMutation()
    {
        var profile = Profile("d0000000-0000-0000-0000-00000000000d");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();
        store.AddBookmark(profile, "https://example.test/", "Saved", visitedAt: At(1));

        var first = store.Snapshot(profile);
        ((BrowserProfileLibraryEntry[])first.Bookmarks)[0] = first.Bookmarks[0] with { Title = "Changed" };

        store.Snapshot(profile).Bookmarks.Single().Title.ShouldBe("Saved");
    }

    [Fact]
    public void CorruptFutureAndOversizedFiles_RecoverEmptyAndTheNextWriteRepairsThem()
    {
        var corrupt = Profile("e0000000-0000-0000-0000-00000000000e");
        var future = Profile("e1000000-0000-0000-0000-00000000000e");
        var oversized = Profile("e2000000-0000-0000-0000-00000000000e");
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProfilePath(corrupt), "{ not json");
        WriteProtectedJson(ProfilePath(future), "{\"version\":999,\"bookmarks\":[],\"history\":[]}");
        using (var stream = File.Create(ProfilePath(oversized)))
        {
            stream.SetLength(BrowserProfileLibraryLimits.MaxFileBytes + 1L);
        }
        var store = new FileBrowserProfileLibraryStore(directory);

        store.Snapshot(corrupt).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
        store.Snapshot(future).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
        store.Snapshot(oversized).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
        store.RecordVisit(corrupt, "https://repaired.test/", "Repaired", visitedAt: At(1)).ShouldBeTrue();
        store.ClearAll(future).ShouldBeTrue();
        store.ClearAll(oversized).ShouldBeTrue();

        new FileBrowserProfileLibraryStore(directory).Snapshot(corrupt).History.Single().Title.ShouldBe("Repaired");
        new FileInfo(ProfilePath(future)).Length.ShouldBeLessThan(BrowserProfileLibraryLimits.MaxFileBytes);
        new FileInfo(ProfilePath(oversized)).Length.ShouldBeLessThan(BrowserProfileLibraryLimits.MaxFileBytes);
    }

    [Fact]
    public void LoadedData_IsValidatedDeduplicatedSortedAndBounded()
    {
        var profile = Profile("e3000000-0000-0000-0000-00000000000e");
        Directory.CreateDirectory(directory);
        WriteProtectedJson(
            ProfilePath(profile),
            """
            {"version":1,"bookmarks":[
              null,
              {"url":"https://example.test/","title":"Old","faviconId":-4,"lastVisitedMilliseconds":1},
              {"url":"HTTPS://EXAMPLE.TEST:443/","title":"New","faviconId":4,"lastVisitedMilliseconds":2},
              {"url":"http://blocked.test/","title":"Blocked","faviconId":1,"lastVisitedMilliseconds":3}
            ],"history":[]}
            """);

        var snapshot = new FileBrowserProfileLibraryStore(directory).Snapshot(profile);

        snapshot.Bookmarks.ShouldHaveSingleItem();
        snapshot.Bookmarks.Single().Title.ShouldBe("New");
        snapshot.Bookmarks.Single().FaviconId.ShouldBe(4);
    }

    [Fact]
    public void FailedPersistence_DoesNotPublishTheMutation()
    {
        var occupiedPath = Path.Combine(directory, "occupied");
        Directory.CreateDirectory(directory);
        File.WriteAllText(occupiedPath, "not a directory");
        var profile = Profile("f0000000-0000-0000-0000-00000000000f");
        var store = new FileBrowserProfileLibraryStore(occupiedPath);

        store.AddBookmark(profile, "https://example.test/", "Never published", visitedAt: At(1)).ShouldBeFalse();
        store.RecordVisit(profile, "https://example.test/", "Never published", visitedAt: At(1)).ShouldBeFalse();
        store.ClearAll(profile).ShouldBeFalse();
        store.Snapshot(profile).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
    }

    [Fact]
    public void FileWrites_ReplaceAtomicallyWithoutLeavingTemporaryFiles()
    {
        var profile = Profile("f1000000-0000-0000-0000-00000000000f");
        var store = new FileBrowserProfileLibraryStore(directory);

        store.RecordVisit(profile, "https://one.test/", "One", visitedAt: At(1)).ShouldBeTrue();
        store.RecordVisit(profile, "https://two.test/", "Two", visitedAt: At(2)).ShouldBeTrue();

        Directory.GetFiles(directory, "*.tmp").ShouldBeEmpty();
        new FileBrowserProfileLibraryStore(directory).Snapshot(profile).History.Count.ShouldBe(2);
    }

    [Fact]
    public void InMemoryStore_IsThreadSafeAndStillBounded()
    {
        var profile = Profile("f2000000-0000-0000-0000-00000000000f");
        IBrowserProfileLibraryStore store = new InMemoryBrowserProfileLibraryStore();

        Parallel.For(0, 500, index =>
            store.RecordVisit(profile, $"https://parallel-{index}.test/", $"Visit {index}", visitedAt: At(index)));

        var snapshot = store.Snapshot(profile);
        snapshot.History.Count.ShouldBe(BrowserProfileLibraryLimits.MaxHistoryEntries);
        snapshot.History.Select(entry => entry.Url).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(BrowserProfileLibraryLimits.MaxHistoryEntries);
    }

    [Fact]
    public void EmptyProfileIdentifier_IsRejected()
    {
        Should.Throw<ArgumentException>(() => new BrowserProfileId(Guid.Empty));
    }

    [Fact]
    public void DefaultLocation_IsUnderCurrentUsersLocalApplicationData()
    {
        var local = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var resolved = Path.GetFullPath(BrowserProfileLibraryStoreLocation.DefaultDirectory);

        resolved.ShouldStartWith(local, Case.Insensitive);
        BrowserProfileLibraryStoreLocation.Open().ShouldBeOfType<FileBrowserProfileLibraryStore>();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string ProfilePath(BrowserProfileId profile) =>
        Path.Combine(directory, profile.Value.ToString("N") + FileBrowserProfileLibraryStore.FileExtension);

    private static void WriteProtectedJson(string path, string json)
    {
        var plain = Encoding.UTF8.GetBytes(json);
        try
        {
            File.WriteAllBytes(
                path,
                ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static BrowserProfileId Profile(string value) => new(Guid.Parse(value));

    private static DateTimeOffset At(int seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);
}
