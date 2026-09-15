using Flint.App.Services;
using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserNetworkViewModelTests
{
    private const string Config = """
        [Interface]
        PrivateKey = local-secret

        [Peer]
        PublicKey = peer
        Endpoint = 203.0.113.1:51820
        AllowedIPs = 203.0.113.0/24
        """;

    [Fact]
    public void SwitchingProfilesClearsTheSensitiveDraft()
    {
        var network = new BrowserNetworkViewModel();
        network.ApplyActiveProfile(televisionActive: true, tvProfileId: "family", epoch: 7);
        network.ConfigText = Config;

        network.ApplyActiveProfile(televisionActive: true, tvProfileId: "guest", epoch: 7);

        network.ConfigText.ShouldBeEmpty();
    }

    [Fact]
    public void StaleOrWrongProfileSnapshotsCannotReplaceTheActiveProfileState()
    {
        var network = new BrowserNetworkViewModel();
        network.ApplyActiveProfile(televisionActive: true, tvProfileId: "family", epoch: 7);
        network.ConfigText = Config;

        network.Apply(Snapshot(epoch: 6, revision: 1, profileId: "family", configPresent: true));
        network.Apply(Snapshot(epoch: 7, revision: 2, profileId: "guest", configPresent: true));

        network.ConfigText.ShouldBe(Config);
        network.ConfigPresentOnTv.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmedProvisionClearsTheSensitiveDraft()
    {
        var remote = new RecordingCockpitRemote();
        remote.Announce(BrowserCockpitFeatures.Network);
        var network = new BrowserNetworkViewModel();
        network.ApplyActiveProfile(televisionActive: true, tvProfileId: "family", epoch: 7);
        network.Bind(remote);
        network.VpnEnabled = true;
        network.ConfigText = Config;

        await network.SaveCommand.ExecuteAsync(null);
        network.ConfigText.ShouldBe(Config);

        network.Apply(Snapshot(epoch: 7, revision: 1, profileId: "family", configPresent: true));

        network.ConfigText.ShouldBeEmpty();
        network.ConfigPresentOnTv.ShouldBeTrue();
    }

    [Fact]
    public void OlderNetworkRevisionCannotRollBackConfirmedState()
    {
        var network = new BrowserNetworkViewModel();
        network.ApplyActiveProfile(televisionActive: true, tvProfileId: "family", epoch: 7);

        network.Apply(Snapshot(epoch: 7, revision: 2, profileId: "family", configPresent: true));
        network.Apply(Snapshot(epoch: 7, revision: 1, profileId: "family", configPresent: false));

        network.ConfigPresentOnTv.ShouldBeTrue();
    }

    private static BrowserNetworkSnapshot Snapshot(
        long epoch,
        long revision,
        string profileId,
        bool configPresent) =>
        new(
            Epoch: epoch,
            Revision: revision,
            ProfileId: profileId,
            VpnEnabled: configPresent,
            Provider: configPresent ? BrowserVpnProviderKind.WireGuard : BrowserVpnProviderKind.None,
            AutoConnectOnBrowserStart: false,
            RequireVpnBeforeBrowse: false,
            ConfigPresent: configPresent,
            CapabilityPreparable: true,
            CapabilityReason: string.Empty,
            SessionState: BrowserVpnSessionKind.Idle);
}
