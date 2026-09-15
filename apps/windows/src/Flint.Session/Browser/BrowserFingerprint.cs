using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Flint.Session.Browser;

/// <summary>
/// The receiver's full SPKI SHA-256 pin and its deliberately shorter TV comparison code.
/// </summary>
/// <remarks>
/// The display code helps a person compare two physical screens during first pairing. It is never
/// an identity: only <see cref="FullPin"/> participates in trust decisions.
/// </remarks>
public sealed class BrowserFingerprint : IEquatable<BrowserFingerprint>
{
    private const string Prefix = "sha256/";
    private readonly byte[] hash;

    private BrowserFingerprint(byte[] hash)
    {
        this.hash = hash;
        FullPin = Prefix + Convert.ToBase64String(hash);
        DisplayCode = string.Create(14, hash, static (destination, source) =>
        {
            var hex = Convert.ToHexString(source.AsSpan(0, 6));
            hex.AsSpan(0, 4).CopyTo(destination);
            destination[4] = '-';
            hex.AsSpan(4, 4).CopyTo(destination[5..]);
            destination[9] = '-';
            hex.AsSpan(8, 4).CopyTo(destination[10..]);
        });
    }

    /// <summary>Canonical full identity pin, encoded as <c>sha256/&lt;base64&gt;</c>.</summary>
    public string FullPin { get; }

    /// <summary>Short 12-hex-digit comparison code, formatted as <c>ABCD-EF01-2345</c>.</summary>
    public string DisplayCode { get; }

    /// <summary>Hashes a DER SubjectPublicKeyInfo value without retaining its raw certificate.</summary>
    public static BrowserFingerprint FromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
        {
            throw new BrowserTrustException("The receiver certificate has no subject public key information.");
        }

        return new BrowserFingerprint(SHA256.HashData(subjectPublicKeyInfo));
    }

    /// <summary>Computes the pin from the certificate's SubjectPublicKeyInfo, not certificate bytes.</summary>
    public static BrowserFingerprint FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is not null)
        {
            return FromSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo());
        }

        using var ecdsa = certificate.GetECDsaPublicKey();
        if (ecdsa is not null)
        {
            return FromSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo());
        }

        throw new BrowserTrustException("The receiver certificate key algorithm is unsupported.");
    }

    /// <summary>Parses exactly a canonical full SHA-256 pin.</summary>
    public static BrowserFingerprint ParseFullPin(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new BrowserTrustException("The stored browser pin has an invalid format.");
        }

        try
        {
            var parsed = Convert.FromBase64String(value[Prefix.Length..]);
            if (parsed.Length != SHA256.HashSizeInBytes || !string.Equals(Prefix + Convert.ToBase64String(parsed), value, StringComparison.Ordinal))
            {
                throw new BrowserTrustException("The stored browser pin has an invalid length or encoding.");
            }

            return new BrowserFingerprint(parsed);
        }
        catch (FormatException exception)
        {
            throw new BrowserTrustException("The stored browser pin has an invalid encoding.", exception);
        }
    }

    /// <summary>Constant-time full-pin comparison used for every reconnect decision.</summary>
    public bool Matches(BrowserFingerprint? other) => other is not null
        && CryptographicOperations.FixedTimeEquals(hash, other.hash);

    /// <inheritdoc />
    public bool Equals(BrowserFingerprint? other) => Matches(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BrowserFingerprint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(hash, 0);

    /// <summary>Does not expose the full pin in incidental diagnostics.</summary>
    public override string ToString() => $"Browser fingerprint {DisplayCode}";
}

/// <summary>Safe failure raised when certificate pin handling cannot establish a trusted identity.</summary>
public class BrowserTrustException : IOException
{
    /// <inheritdoc />
    public BrowserTrustException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public BrowserTrustException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
