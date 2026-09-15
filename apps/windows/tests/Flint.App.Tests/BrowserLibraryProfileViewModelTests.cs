using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserLibraryProfileViewModelTests
{
    private static readonly BrowserProfileId DeviceProfile =
        new(Guid.Parse("71000000-0000-0000-0000-000000000001"));

    [Fact]
    public void ProfileSnapshot_ShowsNamedTvProfilesAndAnExplicitDeviceChoice()
    {
        var viewModel = Bound(out var remote, out _);

        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.Television, "family"));

        viewModel.ProfileOptions.Select(option => option.Name)
            .ShouldBe(["This Windows device", "Family", "Guests"]);
        viewModel.ProfileOptions[0].StorageLabel.ShouldContain("Windows device");
        viewModel.ProfileOptions[1].StorageLabel.ShouldContain("this TV");
        viewModel.ActiveProfileName.ShouldBe("Family");
        viewModel.TvProfileCountLabel.ShouldBe("2 of 8 TV profiles");
        viewModel.CanManageActiveTvProfile.ShouldBeTrue();
        viewModel.CanDeleteActiveTvProfile.ShouldBeTrue();
        viewModel.StorageHeading.ShouldBe("SAVED IN FAMILY ON THE TV");
        viewModel.StorageDetail.ShouldContain("available without a connected device");
        BrowserLibraryViewModel.ProfilePrivacyNote.ShouldContain("do not separate cookies");
        BrowserLibraryViewModel.ProfilePrivacyNote.ShouldContain("sign-ins");
    }

    [Fact]
    public async Task CreatingRenamingAndDeletingTvProfiles_SendsTheMatchingOperations()
    {
        var viewModel = Bound(out var remote, out _);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.Television, "family"));

        viewModel.DraftTvProfileName = "Kids";
        await viewModel.CreateTvProfileCommand.ExecuteAsync(null);
        remote.ProfileRequests.Last().ShouldBe(
            new BrowserProfileRequest(BrowserProfileOperation.CreateTvProfile, Name: "Kids"));

        remote.PublishProfiles(new BrowserProfilesSnapshot(
            7,
            4,
            BrowserProfileStorageLocation.Television,
            "kids",
            "Office PC",
            [
                new BrowserTvProfile("family", "Family"),
                new BrowserTvProfile("guests", "Guests"),
                new BrowserTvProfile("kids", "Kids"),
            ]));
        viewModel.DraftTvProfileName = "Kids room";
        await viewModel.RenameActiveTvProfileCommand.ExecuteAsync(null);
        remote.ProfileRequests.Last().ShouldBe(
            new BrowserProfileRequest(BrowserProfileOperation.RenameTvProfile, "kids", "Kids room"));

        viewModel.ShowDeleteActiveTvProfileCommand.Execute(null);
        viewModel.DeleteTvProfileConfirmationVisible.ShouldBeTrue();
        await viewModel.ConfirmDeleteActiveTvProfileCommand.ExecuteAsync(null);
        remote.ProfileRequests.Last().ShouldBe(
            new BrowserProfileRequest(BrowserProfileOperation.DeleteTvProfile, "kids"));
    }

    [Fact]
    public async Task SelectingAProfile_WaitsForReceiverConfirmationAndUsesTheRightOperation()
    {
        var viewModel = Bound(out var remote, out _);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.Television, "family"));
        var guests = viewModel.ProfileOptions.Single(option => option.ProfileId == "guests");

        await viewModel.SelectProfileCommand.ExecuteAsync(guests);

        remote.ProfileRequests.Single().ShouldBe(
            new BrowserProfileRequest(BrowserProfileOperation.SelectTvProfile, "guests"));
        viewModel.ActiveProfileName.ShouldBe("Family");
        viewModel.ProfileStatusLabel.ShouldContain("Switching to Guests");

        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.Television, "guests") with { Revision = 4 });
        viewModel.ActiveProfileName.ShouldBe("Guests");
        viewModel.ProfileStatusLabel.ShouldBe("Guests · TV");

        await viewModel.SelectProfileCommand.ExecuteAsync(viewModel.ProfileOptions[0]);
        remote.ProfileRequests.Last().Operation.ShouldBe(BrowserProfileOperation.SelectWindowsDevice);
    }

    [Fact]
    public void DeviceSelection_LoadsLocalDataAndSendsOnlyASessionProjectionToTheTv()
    {
        var viewModel = Bound(out var remote, out var store);
        store.AddBookmark(DeviceProfile, "https://device.example/saved", "On Windows", visitedAt: At(1));

        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.WindowsDevice));

        viewModel.IsUsingWindowsDevice.ShouldBeTrue();
        viewModel.Bookmarks.Single().Title.ShouldBe("On Windows");
        viewModel.StorageHeading.ShouldBe("SAVED ON THIS WINDOWS DEVICE");
        viewModel.StorageDetail.ShouldContain("does not save it as a TV profile");
        remote.DeviceLibrarySnapshots.ShouldHaveSingleItem();
        remote.DeviceLibrarySnapshots.Single().Entries.Single().Title.ShouldBe("On Windows");

        remote.PublishLibrary(new BrowserLibrarySnapshot(
            7,
            4,
            [new BrowserLibraryItem(BrowserLibraryItemKind.Bookmark, "https://tv.example/", "Wrong owner", At(2))]));
        viewModel.Bookmarks.Single().Title.ShouldBe("On Windows");
    }

    [Fact]
    public async Task NamedWindowsProfiles_KeepSeparateSessions()
    {
        var viewModel = Bound(out var remote, out var store);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.WindowsDevice));
        store.SaveSession(DeviceProfile, new BrowserSavedSession([new("https://personal.test/")], 0, [], 0))
            .ShouldBeTrue();

        viewModel.NewDeviceProfileName = "Work";
        await viewModel.CreateDeviceProfileCommand.ExecuteAsync(null);

        viewModel.DeviceProfiles.Select(profile => profile.Name).ShouldBe(["Personal", "Work"]);
        viewModel.SelectedDeviceProfile!.Name.ShouldBe("Work");
        store.Session(DeviceProfile).Tabs.Single().Url.ShouldBe("https://personal.test/");
        store.Session(viewModel.SelectedDeviceProfile.Id).Tabs.ShouldBeEmpty();
        remote.ProfileRequests.Last().Operation.ShouldBe(BrowserProfileOperation.SelectWindowsDevice);
    }

    [Fact]
    public async Task ReceiverMutation_ForDeviceProfilePersistsLocallyAndReturnsAReplacementSnapshot()
    {
        var viewModel = Bound(out var remote, out var store);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.WindowsDevice));
        var sentBefore = remote.DeviceLibrarySnapshots.Count;

        remote.RequestDeviceLibrary(new BrowserLibraryRequest(
            BrowserLibraryOperation.AddBookmark,
            "https://device.example/new",
            "New",
            Epoch: 7,
            CommandId: 8));

        store.Snapshot(DeviceProfile).Bookmarks.Single().Title.ShouldBe("New");
        remote.DeviceLibrarySnapshots.Count.ShouldBe(sentBefore + 1);
        remote.DeviceLibrarySnapshots.Last().Entries.Single().Title.ShouldBe("New");

        // A replay cannot reapply an older receiver-side command or force another projection.
        remote.RequestDeviceLibrary(new BrowserLibraryRequest(
            BrowserLibraryOperation.ClearBookmarks,
            Epoch: 7,
            CommandId: 8));
        store.Snapshot(DeviceProfile).Bookmarks.ShouldHaveSingleItem();
        remote.DeviceLibrarySnapshots.Count.ShouldBe(sentBefore + 1);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        remote.DeviceLibrarySnapshots.Count.ShouldBe(sentBefore + 2);
    }

    [Fact]
    public async Task TvProfileUsesTvSnapshotsAndRoutesMutationsBackToTheTv()
    {
        var viewModel = Bound(out var remote, out var store);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.Television, "guests"));
        remote.PublishLibrary(new BrowserLibrarySnapshot(
            7,
            9,
            [new BrowserLibraryItem(BrowserLibraryItemKind.Bookmark, "https://tv.example/", "On TV", At(2))]));

        viewModel.Bookmarks.Single().Title.ShouldBe("On TV");
        await viewModel.ClearHistoryCommand.ExecuteAsync(null);

        remote.LibraryRequests.Last().Operation.ShouldBe(BrowserLibraryOperation.ClearHistory);
        store.Snapshot(DeviceProfile).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
        remote.RequestDeviceLibrary(new BrowserLibraryRequest(
            BrowserLibraryOperation.AddBookmark,
            "https://wrong-owner.example/",
            "Wrong",
            Epoch: 7,
            CommandId: 1));
        store.Snapshot(DeviceProfile).ShouldBe(BrowserProfileLibrarySnapshot.Empty);
    }

    [Fact]
    public async Task LoadedPageIsRecordedOnceOnlyWhenWindowsOwnsTheActiveLibrary()
    {
        var viewModel = Bound(out var remote, out var store);
        remote.PublishProfiles(Profiles(BrowserProfileStorageLocation.WindowsDevice));
        var state = new BrowserStateMessage(
            7, 3, 41, 2, 0, BrowserLoadState.Loaded,
            "https://visited.example/page", "Visited", 100, false, false, 1920, 1080,
            BrowserPreviewState.Disabled);

        await viewModel.ObservePageAsync(state);
        await viewModel.ObservePageAsync(state with { Revision = 4 });

        store.Snapshot(DeviceProfile).History.ShouldHaveSingleItem();
        remote.DeviceLibrarySnapshots.Last().Entries.Count(item => item.Kind == BrowserLibraryItemKind.History)
            .ShouldBe(1);
    }

    private static BrowserLibraryViewModel Bound(
        out RecordingCockpitRemote remote,
        out InMemoryBrowserProfileLibraryStore store)
    {
        store = new InMemoryBrowserProfileLibraryStore();
        remote = new RecordingCockpitRemote();
        var viewModel = new BrowserLibraryViewModel(store, DeviceProfile);
        var binding = new BrowserCockpitBinding(
            new ImmediateDispatcher(),
            new BrowserTabsViewModel(),
            new BrowserViewControlsViewModel(),
            viewModel,
            new BrowserNetworkViewModel(),
            new BrowserWorkspaceViewModel());
        binding.Attach(remote);
        return viewModel;
    }

    private static BrowserProfilesSnapshot Profiles(
        BrowserProfileStorageLocation active,
        string activeId = "") =>
        new(
            Epoch: 7,
            Revision: 3,
            active,
            activeId,
            DeviceName: "Office PC",
            [new BrowserTvProfile("family", "Family"), new BrowserTvProfile("guests", "Guests")]);

    private static DateTimeOffset At(int seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    private sealed class ImmediateDispatcher : IBrowserUiDispatcher
    {
        public void Dispatch(Action action) => action();
    }
}
