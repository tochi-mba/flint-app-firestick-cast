using System.Text.Json;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>Persists the bounded history of direct Fire TV endpoints.</summary>
public sealed class FileRecentAddressStore : IRecentAddressStore
{
    public const int MaxEntries = 8;
    public const string FileName = "recent-addresses.json";

    private readonly string filePath;

    public FileRecentAddressStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FileOnboardingState.VendorFolder,
            FileOnboardingState.ProductFolder))
    {
    }

    public FileRecentAddressStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        filePath = Path.Combine(directory, FileName);
    }

    public IReadOnlyList<RecentAddress> Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var addresses = JsonSerializer.Deserialize<List<RecentAddress>>(
                File.ReadAllText(filePath)) ?? [];
            return addresses
                .Where(IsValid)
                .DistinctBy(Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToArray();
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return [];
        }
    }

    public void Remember(RecentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsValid(address))
        {
            return;
        }

        try
        {
            var addresses = Load()
                .Where(existing => !string.Equals(Key(existing), Key(address), StringComparison.OrdinalIgnoreCase))
                .Prepend(address)
                .Take(MaxEntries)
                .ToArray();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(addresses));
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
        }
    }

    private static bool IsValid(RecentAddress address) =>
        ManualProbeEndpoint.TryParse(address.Address, address.Port?.ToString(), out _, out _);

    private static string Key(RecentAddress address) =>
        $"{address.Address.Trim()}:{address.Port?.ToString() ?? string.Empty}";

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException;
}
