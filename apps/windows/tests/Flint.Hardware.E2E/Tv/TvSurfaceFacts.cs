namespace Flint.Hardware.E2E;

/// <summary>Non-secret facts scraped from the live Flint Receiver pairing surface.</summary>
public sealed record TvSurfaceFacts(
    string PairingCode,
    string Address,
    int ReceiverPort,
    int? BrowserPort,
    string? BrowserFingerprint);
