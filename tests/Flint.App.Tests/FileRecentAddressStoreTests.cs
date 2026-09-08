using Flint.App.Services;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

public sealed class FileRecentAddressStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "flint-address-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Remember_PersistsAcrossInstances()
    {
        new FileRecentAddressStore(directory).Remember(new RecentAddress("10.0.0.42", 5555));

        var reopened = new FileRecentAddressStore(directory);

        reopened.Load().ShouldBe([new RecentAddress("10.0.0.42", 5555)]);
    }

    [Fact]
    public void Remember_MovesDuplicateToTheFront()
    {
        var store = new FileRecentAddressStore(directory);
        store.Remember(new RecentAddress("10.0.0.42", 5555));
        store.Remember(new RecentAddress("10.0.0.43", 5555));
        store.Remember(new RecentAddress("10.0.0.42", 5555));

        store.Load().ShouldBe([
            new RecentAddress("10.0.0.42", 5555),
            new RecentAddress("10.0.0.43", 5555),
        ]);
    }

    [Fact]
    public void Remember_KeepsTheHistoryBounded()
    {
        var store = new FileRecentAddressStore(directory);
        for (var index = 1; index <= FileRecentAddressStore.MaxEntries + 2; index++)
        {
            store.Remember(new RecentAddress($"10.0.0.{index}", 5555));
        }

        store.Load().Count.ShouldBe(FileRecentAddressStore.MaxEntries);
        store.Load()[0].Address.ShouldBe("10.0.0.10");
        store.Load()[^1].Address.ShouldBe("10.0.0.3");
    }

    [Fact]
    public void Load_InvalidJson_ReturnsEmptyHistory()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileRecentAddressStore.FileName), "not json");

        new FileRecentAddressStore(directory).Load().ShouldBeEmpty();
    }

    [Fact]
    public void Remember_RejectsInvalidAddresses()
    {
        var store = new FileRecentAddressStore(directory);

        store.Remember(new RecentAddress("224.0.0.1", 5555));

        store.Load().ShouldBeEmpty();
    }

    [Fact]
    public void Clear_DeletesRememberedAddresses()
    {
        var store = new FileRecentAddressStore(directory);
        store.Remember(new RecentAddress("10.0.0.42", 5555));

        store.Clear();

        store.Load().ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
