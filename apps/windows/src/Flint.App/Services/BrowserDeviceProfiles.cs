namespace Flint.App.Services;

public sealed record BrowserDeviceProfile(BrowserProfileId Id, string Name);
public sealed record BrowserDeviceProfilesSnapshot(BrowserProfileId Selected, IReadOnlyList<BrowserDeviceProfile> Profiles)
{
    public static BrowserDeviceProfilesSnapshot Default => new(BrowserProfileLibraryStoreLocation.CurrentDeviceProfile,
        [new(BrowserProfileLibraryStoreLocation.CurrentDeviceProfile, "Personal")]);
}

public abstract partial class BrowserProfileLibraryStoreBase
{
    public BrowserDeviceProfilesSnapshot DeviceProfiles()
    {
        lock (gate)
        {
            var metadata = Get(BrowserProfileLibraryStoreLocation.CurrentDeviceProfile);
            var names = NormalizeProfileNames(metadata.DeviceProfiles);
            var selected = metadata.SelectedDeviceProfile is { } id && names.ContainsKey(id)
                ? new BrowserProfileId(id) : BrowserProfileLibraryStoreLocation.CurrentDeviceProfile;
            return new(selected, names.Select(pair => new BrowserDeviceProfile(new(pair.Key), pair.Value)).ToArray());
        }
    }

    public BrowserProfileId? CreateDeviceProfile(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl)) return null;
        lock (gate)
        {
            var owner = BrowserProfileLibraryStoreLocation.CurrentDeviceProfile;
            var current = Get(owner);
            var names = NormalizeProfileNames(current.DeviceProfiles);
            if (names.Count >= 8 || names.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
            var id = Guid.NewGuid();
            names.Add(id, name);
            return Commit(owner, current with { DeviceProfiles = names }) ? new BrowserProfileId(id) : null;
        }
    }

    public bool SelectDeviceProfile(BrowserProfileId profile)
    {
        lock (gate)
        {
            var owner = BrowserProfileLibraryStoreLocation.CurrentDeviceProfile;
            var current = Get(owner);
            if (!NormalizeProfileNames(current.DeviceProfiles).ContainsKey(profile.Value)) return false;
            return Commit(owner, current with { SelectedDeviceProfile = profile.Value });
        }
    }

    internal static Dictionary<Guid, string> NormalizeProfileNames(Dictionary<Guid, string>? source)
    {
        var names = new Dictionary<Guid, string> { [BrowserProfileLibraryStoreLocation.CurrentDeviceProfile.Value] = "Personal" };
        foreach (var pair in source ?? [])
        {
            if (names.Count >= 8) break;
            if (pair.Key == Guid.Empty || pair.Value is not { Length: > 0 and <= 32 } || pair.Value.Any(char.IsControl)) continue;
            if (!names.Values.Contains(pair.Value, StringComparer.OrdinalIgnoreCase)) names[pair.Key] = pair.Value;
        }
        return names;
    }
}
