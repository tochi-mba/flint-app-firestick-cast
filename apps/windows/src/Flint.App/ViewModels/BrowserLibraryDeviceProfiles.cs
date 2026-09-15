using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;

namespace Flint.App.ViewModels;

public sealed partial class BrowserLibraryViewModel
{
    public ObservableCollection<BrowserDeviceProfile> DeviceProfiles { get; } = [];
    [ObservableProperty] private string newDeviceProfileName = "";
    [ObservableProperty] private BrowserDeviceProfile? selectedDeviceProfile;
    internal Guid DeviceProfileKey => deviceProfile.Value;

    private void RefreshDeviceProfiles()
    {
        DeviceProfiles.Clear();
        foreach (var profile in deviceStore.DeviceProfiles().Profiles) DeviceProfiles.Add(profile);
        SelectedDeviceProfile = DeviceProfiles.FirstOrDefault(p => p.Id == deviceProfile);
    }

    [RelayCommand]
    private async Task CreateDeviceProfileAsync(CancellationToken cancellationToken)
    {
        var created = deviceStore.CreateDeviceProfile(NewDeviceProfileName);
        if (created is null) { SyncError = "Use a unique name, up to 32 characters. You can keep eight Windows profiles."; return; }
        NewDeviceProfileName = "";
        RefreshDeviceProfiles();
        SelectedDeviceProfile = DeviceProfiles.First(p => p.Id == created.Value);
        await UseDeviceProfileAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task UseDeviceProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedDeviceProfile is not { } selected) return;
        var previous = deviceProfile;
        PendingProfileName = selected.Name;
        if (!deviceStore.SelectDeviceProfile(selected.Id))
        {
            PendingProfileName = null;
            SyncError = "This profile could not be selected.";
            return;
        }
        deviceProfile = selected.Id;
        if (remote is null) { PendingProfileName = null; RefreshDeviceView(); return; }
        try
        {
            await remote.SendProfileCommandAsync(new BrowserProfileRequest(BrowserProfileOperation.SelectWindowsDevice), cancellationToken);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            deviceProfile = previous;
            deviceStore.SelectDeviceProfile(previous);
            RefreshDeviceProfiles();
            PendingProfileName = null;
            SyncError = "The profile switch could not reach the TV. Try again after reconnecting.";
        }
    }
}
