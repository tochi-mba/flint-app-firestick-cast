using Flint.App.Services;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Remembered TVs on disk: logins readable only by this Windows account, and failures that cost a code at most.</summary>
public sealed class FileKnownTvStoreTests : IDisposable
{
    private static readonly string Login = new('q', 43);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string folder = Path.Combine(Path.GetTempPath(), $"flint-tvs-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(folder, FileKnownTvStore.FileName);

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ATv_IsKeptBetweenRuns_WithItsLogin()
    {
        new FileKnownTvStore(folder).Save(new KnownTv("Living Room", "10.0.0.5", 47855, Login, Now));

        var tv = new FileKnownTvStore(folder).Load().ShouldHaveSingleItem();
        tv.ShouldBe(new KnownTv("Living Room", "10.0.0.5", 47855, Login, Now));
    }

    [Fact]
    public void TheLogin_IsNeverWrittenInClearText()
    {
        new FileKnownTvStore(folder).Save(new KnownTv("Living Room", "10.0.0.5", 47855, Login, Now));

        var text = File.ReadAllText(FilePath);
        text.ShouldContain("Living Room");
        text.ShouldNotContain(Login);
    }

    [Fact]
    public void ALoginThatCannotBeUnprotected_LeavesTheTvWithoutOne()
    {
        // As a file copied from another Windows account reads: the TV is kept and asks for a code once.
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, """
            [{"Name":"Living Room","Address":"10.0.0.5","ReceiverPort":47855,"Login":"bm90IHByb3RlY3RlZA==","LastConnected":"2026-10-07T09:00:00+00:00"},
             {"Name":"Bedroom","Address":"10.0.0.6","ReceiverPort":47855,"Login":"%%not base64%%","LastConnected":"2026-10-06T09:00:00+00:00"},
             {"Name":"Kitchen","Address":"10.0.0.7","ReceiverPort":47855,"Login":null,"LastConnected":"2026-10-05T09:00:00+00:00"}]
            """);

        var tvs = new FileKnownTvStore(folder).Load();

        tvs.Select(tv => tv.Name).ShouldBe(["Living Room", "Bedroom", "Kitchen"]);
        tvs.ShouldAllBe(tv => !tv.HasLogin);
    }

    [Fact]
    public void AProtectedLoginThatIsNotALogin_IsLeftOut()
    {
        var garbled = Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.ASCII.GetBytes("too short"),
            System.Text.Encoding.UTF8.GetBytes("flint-known-tv-v1"),
            System.Security.Cryptography.DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, $$"""[{"Name":"Living Room","Address":"10.0.0.5","ReceiverPort":47855,"Login":"{{garbled}}","LastConnected":"2026-10-07T09:00:00+00:00"}]""");

        new FileKnownTvStore(folder).Load().ShouldHaveSingleItem().HasLogin.ShouldBeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""[{"Name":"","Address":"10.0.0.5","ReceiverPort":47855,"LastConnected":"2026-10-07T09:00:00+00:00"},{"Name":"TV","Address":"10.0.0.5","ReceiverPort":0,"LastConnected":"2026-10-07T09:00:00+00:00"},{"Name":"TV","Address":"10.0.0.5","ReceiverPort":70000,"LastConnected":"2026-10-07T09:00:00+00:00"},{"Name":"TV","Address":" ","ReceiverPort":47855,"LastConnected":"2026-10-07T09:00:00+00:00"}]""")]
    public void AFileThatCannotBeRead_OrHoldsNothingValid_IsAnEmptyList(string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, content);

        new FileKnownTvStore(folder).Load().ShouldBeEmpty();
    }

    [Fact]
    public void TheStoreInFlintsOwnFolder_CanBeRead()
    {
        // Reads whatever this PC has; it never writes in this test.
        Should.NotThrow(() => new FileKnownTvStore().Load());
    }

    [Fact]
    public void ForgettingOneOrAll_IsWrittenThrough()
    {
        var store = new FileKnownTvStore(folder);
        store.Save(new KnownTv("Living Room", "10.0.0.5", 47855, Login, Now));
        store.Save(new KnownTv("Bedroom", "10.0.0.6", 47855, null, Now.AddHours(-1)));

        store.Forget("Living Room");
        new FileKnownTvStore(folder).Load().ShouldHaveSingleItem().Name.ShouldBe("Bedroom");
        store.ForgetAll();
        new FileKnownTvStore(folder).Load().ShouldBeEmpty();
    }

    [Fact]
    public void AFolderThatCannotBeWritten_KeepsTheTvsForThisRun()
    {
        Directory.CreateDirectory(folder);
        var blocked = Path.Combine(folder, "blocked");
        File.WriteAllText(blocked, "a file where the folder should be");
        var store = new FileKnownTvStore(blocked);

        Should.NotThrow(() => store.Save(new KnownTv("Living Room", "10.0.0.5", 47855, Login, Now)));
        store.Load().ShouldHaveSingleItem().HasLogin.ShouldBeTrue();
        new FileKnownTvStore(blocked).Load().ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => new FileKnownTvStore(" "));
        Should.Throw<ArgumentNullException>(() => store.Save(null!));
    }
}
