using System.Runtime.InteropServices;
using Flint.Core;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

/// <summary>Reading the engine's display list, where a wrong entry would share the wrong screen.</summary>
public sealed class NativeDisplayOutputsTests
{
    [Fact]
    public void NativeOutput_MatchesTheEngineLayout()
    {
        Marshal.SizeOf<NativeOutput>().ShouldBe(104);
        Marshal.OffsetOf<NativeOutput>(nameof(NativeOutput.AdapterLuid)).ShouldBe(8);
        Marshal.OffsetOf<NativeOutput>(nameof(NativeOutput.Left)).ShouldBe(16);
        Marshal.OffsetOf<NativeOutput>(nameof(NativeOutput.Attached)).ShouldBe(32);
        Marshal.OffsetOf<NativeOutput>(nameof(NativeOutput.DeviceNameLength)).ShouldBe(36);
        Marshal.OffsetOf<NativeOutput>("_deviceName").ShouldBe(40);
    }

    [Fact]
    public void AWellFormedDisplay_ReadsAsOne()
    {
        var display = Output(0, left: -1920, main: false, rotation: 2).ToModel();

        display.ShouldBe(new DisplayOutput(0, @"\\.\DISPLAY1", -1920, 0, 1920, 1080, DisplayRotation.QuarterClockwise, IsMain: false));
    }

    [Theory]
    [InlineData("attached")]
    [InlineData("main")]
    [InlineData("main-detached")]
    [InlineData("rotation-low")]
    [InlineData("rotation-high")]
    [InlineData("no-name")]
    [InlineData("long-name")]
    [InlineData("no-width")]
    [InlineData("no-height")]
    [InlineData("too-wide")]
    public void AMalformedDisplay_IsRefused(string fault)
    {
        var output = Output(0);
        switch (fault)
        {
            case "attached": output.Attached = 2; break;
            case "main": output.Main = 2; break;
            case "main-detached": output.Attached = 0; break;
            case "rotation-low": output.Rotation = 0; break;
            case "rotation-high": output.Rotation = 5; break;
            case "no-name": output.DeviceNameLength = 0; break;
            case "long-name": output.DeviceNameLength = NativeOutput.DeviceNameCapacity + 1; break;
            case "no-width": output.Right = output.Left; break;
            case "no-height": output.Bottom = output.Top - 1; break;
            default: output.Left = int.MinValue; output.Right = int.MaxValue; break;
        }

        output.ToModel().ShouldBeNull();
    }

    [Fact]
    public void TheList_KeepsAttachedDisplays_InTheEnginesOrder()
    {
        var detached = Output(1);
        detached.Attached = 0;
        detached.Main = 0;
        detached.Right = detached.Left;

        var displays = NativeDisplayOutputs.Convert([Output(0), detached, Output(2, left: 1920, main: false)], 3);

        displays.Select(display => display.Index).ShouldBe([0u, 2u], "a detached display is left out, and nothing is renumbered");
    }

    [Fact]
    public void OneBadEntry_RefusesTheWholeList()
    {
        var misnumbered = Output(5);
        var malformed = Output(1);
        malformed.Rotation = 9;
        var oddlyAttached = Output(1);
        oddlyAttached.Attached = 3;

        NativeDisplayOutputs.Convert([Output(0), misnumbered], 2).ShouldBeEmpty();
        NativeDisplayOutputs.Convert([Output(0), malformed], 2).ShouldBeEmpty();
        NativeDisplayOutputs.Convert([Output(0), oddlyAttached], 2).ShouldBeEmpty();
    }

    [Fact]
    public void ACountBeyondTheBuffer_ReadsOnlyWhatWasWritten()
    {
        NativeDisplayOutputs.Convert([Output(0)], 40).ShouldHaveSingleItem().Index.ShouldBe(0u);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("export")]
    [InlineData("image")]
    [InlineData("marshal")]
    public void AnEngineThatCannotBeCalled_ListsNoDisplays(string failure)
    {
        Exception thrown = failure switch
        {
            "missing" => new DllNotFoundException(),
            "export" => new EntryPointNotFoundException(),
            "image" => new BadImageFormatException(),
            _ => new MarshalDirectiveException(),
        };
        var outputs = new NativeDisplayOutputs((Span<NativeOutput> _, out uint found) =>
        {
            found = 0;
            throw thrown;
        });

        outputs.ListOutputs().ShouldBeEmpty();
    }

    [Fact]
    public void AnEngineThatRefuses_ListsNoDisplays_EvenWithEntriesWritten()
    {
        var outputs = new NativeDisplayOutputs((Span<NativeOutput> slots, out uint found) =>
        {
            slots[0] = Output(0);
            found = 1;
            return (int)FlintStatus.PlatformError;
        });

        outputs.ListOutputs().ShouldBeEmpty();
    }

    [Fact]
    public void WhatTheEngineWrites_IsWhatIsListed()
    {
        var outputs = new NativeDisplayOutputs((Span<NativeOutput> slots, out uint found) =>
        {
            slots.Length.ShouldBe(NativeDisplayOutputs.MaxOutputs);
            slots[0] = Output(0);
            slots[1] = Output(1, left: 1920, main: false);
            found = 2;
            return (int)FlintStatus.Ok;
        });

        outputs.ListOutputs().Select(display => display.DeviceName).ShouldBe([@"\\.\DISPLAY1", @"\\.\DISPLAY2"]);
    }

    [Fact]
    public void ThisHostsDisplays_AreListedWithoutThrowing()
    {
        // A build agent without a desktop lists none; a missing engine lists none. Neither throws.
        var displays = new NativeDisplayOutputs().ListOutputs();

        displays.ShouldAllBe(display => display.Width > 0 && display.Height > 0 && display.DeviceName.Length > 0);
        displays.Count(display => display.IsMain).ShouldBeLessThanOrEqualTo(1);
    }

    private static NativeOutput Output(uint index, int left = 0, bool main = true, uint rotation = 1)
    {
        var output = new NativeOutput
        {
            Index = index,
            Rotation = rotation,
            AdapterLuid = 7,
            Left = left,
            Top = 0,
            Right = left + 1920,
            Bottom = 1080,
            Attached = 1,
            Main = main ? (byte)1 : (byte)0,
        };
        output.SetDeviceName($@"\\.\DISPLAY{index + 1}");
        return output;
    }
}
