using System.Runtime.InteropServices;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>Monitor names from Windows' display configuration, including when Windows will not say.</summary>
public sealed class DisplayNamesTests
{
    [Fact]
    public void TheStructures_MatchWindowsOwn()
    {
        Marshal.SizeOf<DisplayNames.PathInfo>().ShouldBe(72);
        Marshal.SizeOf<DisplayNames.DeviceInfoHeader>().ShouldBe(20);
        Marshal.SizeOf<DisplayNames.SourceDeviceName>().ShouldBe(84);
        Marshal.SizeOf<DisplayNames.TargetDeviceName>().ShouldBe(420);
    }

    [Fact]
    public void EachActiveDisplay_IsNamedByItsDeviceName()
    {
        var api = new FakeConfig
        {
            Paths = [Path(source: 1, target: 11), Path(source: 2, target: 12)],
            Sources = { [1] = @"\\.\DISPLAY1", [2] = @"\\.\DISPLAY2" },
            Targets = { [11] = ("DELL U2720Q", @"\\?\DISPLAY#DEL41A8"), [12] = (string.Empty, @"\\?\DISPLAY#BOE0A1B") },
        };

        var names = new DisplayNames(api).Read();

        names.Count.ShouldBe(2);
        names[@"\\.\display1"].ShouldBe(new Core.MonitorName("DELL U2720Q", @"\\?\DISPLAY#DEL41A8"), "device names match whatever their case");
        names[@"\\.\DISPLAY2"].FriendlyName.ShouldBeEmpty();
        api.Requests.ShouldAllBe(request => request.Size == 84 || request.Size == 420);
    }

    [Fact]
    public void ALaptopsOwnScreen_IsCalledTheBuiltInDisplay_WhenItHasNoName()
    {
        var api = new FakeConfig
        {
            Paths = [Path(1, 11), Path(2, 12), Path(3, 13)],
            Sources = { [1] = "D1", [2] = "D2", [3] = "D3" },
            Targets = { [11] = (" ", "p1"), [12] = (string.Empty, "p2"), [13] = ("Panel", "p3") },
            Technologies = { [11] = 0x8000_0000, [12] = 10, [13] = 11 },
        };

        var names = new DisplayNames(api).Read();

        names["D1"].FriendlyName.ShouldBe(DisplayNames.BuiltInName);
        names["D2"].FriendlyName.ShouldBeEmpty("an external monitor with no name is numbered instead");
        names["D3"].FriendlyName.ShouldBe("Panel", "a built-in panel that names itself keeps its name");
        DisplayNames.IsBuiltIn(11).ShouldBeTrue();
        DisplayNames.IsBuiltIn(13).ShouldBeTrue();
        DisplayNames.IsBuiltIn(5).ShouldBeFalse();
    }

    [Fact]
    public void ADisplayWindowsCannotDescribe_IsLeftOut()
    {
        var api = new FakeConfig
        {
            Paths = [Path(1, 11), Path(2, 12), Path(3, 13)],
            Sources = { [1] = @"\\.\DISPLAY1", [3] = string.Empty },
            Targets = { [11] = ("A", "a"), [13] = ("C", "c") },
        };
        api.Paths.Add(Path(1, 14));
        api.Targets[14] = ("Duplicate", "d");
        api.Paths.Add(Path(4, 15));
        api.Sources[4] = "D4";

        var names = new DisplayNames(api).Read();

        names.ShouldHaveSingleItem().Value.FriendlyName.ShouldBe("A", "the first path for a display wins; one with no source or target name is skipped");
    }

    [Fact]
    public void ADisplayChangeDuringTheQuery_IsRetried_ThenGivenUpOn()
    {
        var api = new FakeConfig { Paths = [Path(1, 11)], Sources = { [1] = "D1" }, Targets = { [11] = ("A", "a") } };
        api.QueryResults.Enqueue(122);

        new DisplayNames(api).Read().Count.ShouldBe(1);
        api.Queries.ShouldBe(2);

        var stubborn = new FakeConfig { Paths = [Path(1, 11)] };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            stubborn.QueryResults.Enqueue(122);
        }

        new DisplayNames(stubborn).Read().ShouldBeEmpty();
        stubborn.Queries.ShouldBe(3);
    }

    [Fact]
    public void AnyOtherRefusal_MeansNoNames()
    {
        new DisplayNames(new FakeConfig { SizesResult = 87 }).Read().ShouldBeEmpty();

        var refusing = new FakeConfig { Paths = [Path(1, 11)] };
        refusing.QueryResults.Enqueue(87);
        new DisplayNames(refusing).Read().ShouldBeEmpty();
    }

    [Fact]
    public void FewerPathsThanSized_AreAllThatIsRead()
    {
        var api = new FakeConfig { Paths = [Path(1, 11)], ExtraSized = 2, Sources = { [1] = "D1" }, Targets = { [11] = ("A", "a") } };

        new DisplayNames(api).Read().Count.ShouldBe(1);
    }

    [Fact]
    public unsafe void Text_StopsAtItsTerminator_OrFillsTheBuffer()
    {
        var full = "ABCD".ToCharArray();
        var cut = "AB\0D".ToCharArray();
        fixed (char* pointer = full)
        {
            DisplayNames.TextUpToNul(pointer, 4).ShouldBe("ABCD");
        }

        fixed (char* pointer = cut)
        {
            DisplayNames.TextUpToNul(pointer, 4).ShouldBe("AB");
        }
    }

    [Fact]
    public void ThisPcsDisplays_AreReadWithoutThrowing()
    {
        var names = new DisplayNames().Read();

        names.Keys.ShouldAllBe(name => name.Length > 0);
    }

    private static DisplayNames.PathInfo Path(uint source, uint target) =>
        new() { SourceId = source, SourceAdapterLow = 5, TargetId = target, TargetAdapterLow = 5 };

    private sealed class FakeConfig : DisplayNames.IDisplayConfigApi
    {
        public List<DisplayNames.PathInfo> Paths { get; set; } = [];

        public Dictionary<uint, string> Sources { get; } = [];

        public Dictionary<uint, (string Friendly, string Path)> Targets { get; } = [];

        public Dictionary<uint, uint> Technologies { get; } = [];

        public Queue<int> QueryResults { get; } = new();

        public List<DisplayNames.DeviceInfoHeader> Requests { get; } = [];

        public int SizesResult { get; set; }

        public int ExtraSized { get; set; }

        public int Queries { get; private set; }

        public int BufferSizes(out uint pathCount, out uint modeCount)
        {
            pathCount = (uint)(Paths.Count + ExtraSized);
            modeCount = 1;
            return SizesResult;
        }

        public int Query(ref uint pathCount, DisplayNames.PathInfo[] paths, ref uint modeCount, byte[] modes)
        {
            Queries++;
            modes.Length.ShouldBe(64);
            if (QueryResults.TryDequeue(out var result) && result != 0)
            {
                return result;
            }

            Paths.CopyTo(paths);
            pathCount = (uint)Paths.Count;
            return 0;
        }

        public unsafe int SourceName(ref DisplayNames.SourceDeviceName request)
        {
            Requests.Add(request.Header);
            if (!Sources.TryGetValue(request.Header.Id, out var name))
            {
                return 31;
            }

            fixed (char* target = request.GdiDeviceName)
            {
                name.AsSpan().CopyTo(new Span<char>(target, DisplayNames.SourceDeviceName.NameCapacity));
            }

            return 0;
        }

        public unsafe int TargetName(ref DisplayNames.TargetDeviceName request)
        {
            Requests.Add(request.Header);
            if (!Targets.TryGetValue(request.Header.Id, out var monitor))
            {
                return 31;
            }

            request.OutputTechnology = Technologies.GetValueOrDefault(request.Header.Id);

            fixed (char* friendly = request.FriendlyName)
            {
                monitor.Friendly.AsSpan().CopyTo(new Span<char>(friendly, DisplayNames.TargetDeviceName.FriendlyNameCapacity));
            }

            fixed (char* path = request.DevicePath)
            {
                monitor.Path.AsSpan().CopyTo(new Span<char>(path, DisplayNames.TargetDeviceName.DevicePathCapacity));
            }

            return 0;
        }
    }
}
