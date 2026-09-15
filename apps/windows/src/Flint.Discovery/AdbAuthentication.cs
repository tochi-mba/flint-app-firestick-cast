using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Flint.Discovery;

/// <summary>Android's legacy RSA-2048 ADB host authentication format.</summary>
internal static class AdbAuthentication
{
    internal const uint TokenType = 1;
    internal const uint SignatureType = 2;
    internal const uint PublicKeyType = 3;
    internal const int TokenLength = 20;
    internal const int ModulusBytes = 256;
    internal const int PublicKeyBinaryLength = 4 + 4 + ModulusBytes + ModulusBytes + 4;

    internal static AdbMessage SignatureMessage(AdbIdentity identity, ReadOnlySpan<byte> token)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (token.Length != TokenLength)
        {
            throw new CryptographicException($"An ADB token must be {TokenLength} bytes.");
        }

        byte[] signature;
        lock (identity.Rsa)
        {
            signature = identity.Rsa.SignHash(token, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        return new AdbMessage(AdbCommand.Auth, SignatureType, 0, signature);
    }

    internal static AdbMessage PublicKeyMessage(AdbIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new AdbMessage(AdbCommand.Auth, PublicKeyType, 0, PublicKeyPayload(identity));
    }

    internal static byte[] PublicKeyPayload(AdbIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RSAParameters parameters;
        lock (identity.Rsa)
        {
            parameters = identity.Rsa.ExportParameters(includePrivateParameters: false);
        }

        var modulusBigEndian = parameters.Modulus
            ?? throw new CryptographicException("The ADB RSA key has no modulus.");
        if (modulusBigEndian.Length != ModulusBytes || parameters.Exponent is null)
        {
            throw new CryptographicException("ADB authentication requires an RSA-2048 key.");
        }

        var modulus = new BigInteger(modulusBigEndian, isUnsigned: true, isBigEndian: true);
        var modulusLittleEndian = ToFixedLittleEndian(modulus, ModulusBytes);
        var lowWord = BinaryPrimitives.ReadUInt32LittleEndian(modulusLittleEndian);
        var inverse = MultiplicativeInverse(lowWord);
        var n0Inverse = unchecked(0u - inverse);
        var rSquared = (BigInteger.One << (ModulusBytes * 16)) % modulus;
        var rSquaredLittleEndian = ToFixedLittleEndian(rSquared, ModulusBytes);
        var exponent = checked((uint)new BigInteger(parameters.Exponent, isUnsigned: true, isBigEndian: true));

        var binary = new byte[PublicKeyBinaryLength];
        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(0, 4), ModulusBytes / 4);
        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(4, 4), n0Inverse);
        modulusLittleEndian.CopyTo(binary.AsSpan(8, ModulusBytes));
        rSquaredLittleEndian.CopyTo(binary.AsSpan(8 + ModulusBytes, ModulusBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(binary.Length - 4), exponent);

        var line = Convert.ToBase64String(binary) + " " + identity.Comment + '\0';
        return Encoding.ASCII.GetBytes(line);
    }

    private static uint MultiplicativeInverse(uint odd)
    {
        if ((odd & 1) == 0)
        {
            throw new CryptographicException("An RSA modulus must be odd.");
        }

        var inverse = odd;
        unchecked
        {
            inverse *= 2 - odd * inverse;
            inverse *= 2 - odd * inverse;
            inverse *= 2 - odd * inverse;
            inverse *= 2 - odd * inverse;
            inverse *= 2 - odd * inverse;
        }

        return inverse;
    }

    private static byte[] ToFixedLittleEndian(BigInteger value, int size)
    {
        var encoded = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (encoded.Length > size)
        {
            throw new CryptographicException("RSA value does not fit the ADB key format.");
        }

        var result = new byte[size];
        encoded.CopyTo(result, 0);
        return result;
    }
}

/// <summary>One persistent ADB host key and the label shown on the television.</summary>
internal sealed class AdbIdentity
{
    internal AdbIdentity(RSA rsa, string comment)
    {
        Rsa = rsa ?? throw new ArgumentNullException(nameof(rsa));
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        if (comment.Any(character => character is '\0' or '\r' or '\n'))
        {
            throw new ArgumentException("The ADB key label contains a control character.", nameof(comment));
        }

        if (rsa.KeySize != 2048)
        {
            throw new CryptographicException("ADB authentication requires an RSA-2048 key.");
        }

        Comment = comment;
    }

    internal RSA Rsa { get; }

    internal string Comment { get; }
}

internal interface IAdbIdentityProvider
{
    AdbIdentity GetIdentity();
}

/// <summary>Loads or creates the stable per-user key that makes authorization survive restarts.</summary>
internal sealed class FileAdbIdentityProvider : IAdbIdentityProvider
{
    private const string KeyComment = "flint@rex-technologies";
    private readonly string _keyPath;
    private readonly Lazy<AdbIdentity> _identity;

    internal FileAdbIdentityProvider(string? keyPath = null)
    {
        _keyPath = keyPath ?? ResolveDefaultKeyPath();
        _identity = new Lazy<AdbIdentity>(LoadOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AdbIdentity GetIdentity() => _identity.Value;

    private static string ResolveDefaultKeyPath()
    {
        var standardPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".android",
            "adbkey");
        if (File.Exists(standardPath))
        {
            return standardPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "REX Technologies",
            "Flint",
            "adbkey.pem");
    }

    private AdbIdentity LoadOrCreate()
    {
        if (File.Exists(_keyPath))
        {
            return Load(_keyPath);
        }

        var directory = Path.GetDirectoryName(_keyPath)
            ?? throw new IOException("The ADB key path has no parent directory.");
        Directory.CreateDirectory(directory);

        using var generated = RSA.Create(2048);
        var temporary = _keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, generated.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
        try
        {
            File.Move(temporary, _keyPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(_keyPath))
        {
            File.Delete(temporary);
        }

        return Load(_keyPath);
    }

    private static AdbIdentity Load(string path)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(path, Encoding.ASCII));
            return new AdbIdentity(rsa, KeyComment);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }
}
