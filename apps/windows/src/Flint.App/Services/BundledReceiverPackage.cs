using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>The receiver package this copy of Flint carries, if it carries one.</summary>
public interface IBundledReceiverSource
{
    /// <summary>What is bundled, or <see langword="null"/> for a build that carries no receiver.</summary>
    BundledReceiver? Describe();

    /// <summary>The package bytes, read when an install is actually about to happen.</summary>
    Task<byte[]> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The receiver APK beside the executable, described by the sidecar the packager wrote next to it.
/// </summary>
/// <remarks>
/// <para>
/// The packager reads the version out of the APK the Android build produced and writes it, with
/// the file's size and digest, to <c>Flint.Receiver.json</c>. Flint trusts the sidecar only while
/// the APK still matches that digest: the description shown above "this is what will be
/// installed" is then provably a description of these bytes, not of whatever was there when
/// somebody wrote a properties file.
/// </para>
/// <para>
/// A local or pull-request build has neither file and offers nothing, which is what is true.
/// </para>
/// </remarks>
public sealed class BundledReceiverPackage(string directory) : IBundledReceiverSource
{
    /// <summary>The package file name the packager uses.</summary>
    public const string ApkFileName = "Flint.Receiver.apk";

    /// <summary>The sidecar the packager writes beside the package.</summary>
    public const string SidecarFileName = "Flint.Receiver.json";

    private readonly string _apkPath = Path.Combine(directory, ApkFileName);
    private readonly string _sidecarPath = Path.Combine(directory, SidecarFileName);
    private BundledReceiver? _described;
    private bool _looked;

    /// <summary>The package beside the running executable.</summary>
    public static BundledReceiverPackage BesideTheApp() => new(AppContext.BaseDirectory);

    /// <inheritdoc />
    public BundledReceiver? Describe()
    {
        if (_looked)
        {
            return _described;
        }

        _described = Read();
        _looked = true;
        return _described;
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        var described = Describe()
            ?? throw new InvalidOperationException("This copy of Flint carries no receiver package.");
        var bytes = await File.ReadAllBytesAsync(_apkPath, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != described.SizeBytes)
        {
            throw new InvalidOperationException("The bundled receiver changed since Flint described it.");
        }

        return bytes;
    }

    private BundledReceiver? Read()
    {
        if (!File.Exists(_apkPath) || !File.Exists(_sidecarPath))
        {
            return null;
        }

        Sidecar? sidecar;
        try
        {
            sidecar = JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(_sidecarPath));
        }
        catch (JsonException)
        {
            return null;
        }

        if (sidecar is null
            || string.IsNullOrWhiteSpace(sidecar.PackageName)
            || string.IsNullOrWhiteSpace(sidecar.VersionName)
            || sidecar.VersionCode < 0
            || string.IsNullOrWhiteSpace(sidecar.Sha256))
        {
            return null;
        }

        var apk = new FileInfo(_apkPath);
        if (apk.Length <= 0 || apk.Length != sidecar.SizeBytes || !DigestMatches(apk, sidecar.Sha256))
        {
            return null;
        }

        return new BundledReceiver(sidecar.PackageName, sidecar.VersionName, sidecar.VersionCode, apk.Length);
    }

    private static bool DigestMatches(FileInfo apk, string expected)
    {
        using var stream = apk.OpenRead();
        var digest = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What the packager writes. Field names match <c>tools/scripts/package.ps1</c>.</summary>
    internal sealed record Sidecar(
        [property: JsonPropertyName("packageName")] string? PackageName,
        [property: JsonPropertyName("versionName")] string? VersionName,
        [property: JsonPropertyName("versionCode")] long VersionCode,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("sha256")] string? Sha256);
}
