using System.Security.Cryptography;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.Session.Tests;

public sealed class BrowserFingerprintTests
{
    [Fact]
    public void FromSubjectPublicKeyInfo_UsesTheFullSha256PinAndAStableShortDisplayCode()
    {
        var spki = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();

        var fingerprint = BrowserFingerprint.FromSubjectPublicKeyInfo(spki);

        fingerprint.FullPin.ShouldBe("sha256/" + Convert.ToBase64String(SHA256.HashData(spki)));
        fingerprint.DisplayCode.ShouldMatch("^[0-9A-F]{4}(?:-[0-9A-F]{4}){2}$");
        fingerprint.Matches(BrowserFingerprint.FromSubjectPublicKeyInfo(spki)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256/not base64!")]
    [InlineData("SHA256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("sha256/AAAA")]
    public void ParseFullPin_RejectsMalformedOrWrongLengthValues(string value)
    {
        Should.Throw<BrowserTrustException>(() => BrowserFingerprint.ParseFullPin(value));
    }

    [Fact]
    public void Matches_DoesNotTreatTheHumanDisplayCodeAsTheIdentity()
    {
        var first = BrowserFingerprint.FromSubjectPublicKeyInfo(new byte[32]);
        var second = BrowserFingerprint.FromSubjectPublicKeyInfo(Enumerable.Repeat((byte)1, 32).ToArray());

        first.DisplayCode.ShouldNotBe(second.DisplayCode);
        first.Matches(second).ShouldBeFalse();
    }
}
