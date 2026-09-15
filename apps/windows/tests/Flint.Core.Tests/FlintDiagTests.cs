using System.Diagnostics;
using System.Text;
using Flint.Core;
using Shouldly;

namespace Flint.Core.Tests;

public sealed class FlintDiagTests
{
    [Theory]
    [InlineData(null, "(none)")]
    [InlineData("", "(none)")]
    [InlineData("   ", "(none)")]
    [InlineData("not-a-url", "(opaque)")]
    [InlineData("https://example.com/path?q=1#frag", "example.com")]
    [InlineData("http://10.1.2.3:43099/secret", "10.1.2.3")]
    public void SafeHost_DropsPathQueryAndFragment(string? url, string expected) =>
        FlintDiag.SafeHost(url).ShouldBe(expected);

    [Fact]
    public void FormatLine_CollapsesNewlinesAndCapsLength()
    {
        var longBody = new string('x', 600);
        var line = FlintDiag.FormatLine("INFO", "Flint\nBrowser", "a\rb\n" + longBody);

        line.ShouldStartWith(FlintDiag.TracePrefix + "|INFO|Flint Browser|");
        line.ShouldNotContain("\n");
        line.ShouldNotContain("\r");
        var message = line.Split('|', 4)[3];
        message.Length.ShouldBe(480);
    }

    [Fact]
    public void Info_WritesPrefixedTraceLine()
    {
        using var capture = new CaptureListener();
        Trace.Listeners.Add(capture);
        try
        {
            FlintDiag.Info("FlintBrowser", "state load=Loading");
        }
        finally
        {
            Trace.Listeners.Remove(capture);
        }

        capture.Lines.ShouldContain(FlintDiag.TracePrefix + "|INFO|FlintBrowser|state load=Loading");
    }

    [Fact]
    public void WarnAndError_UseTheirLevels()
    {
        using var capture = new CaptureListener();
        Trace.Listeners.Add(capture);
        try
        {
            FlintDiag.Warn("FlintTrust", "tls rejected");
            FlintDiag.Error("FlintCast", "probe failed");
        }
        finally
        {
            Trace.Listeners.Remove(capture);
        }

        capture.Lines.ShouldContain(FlintDiag.TracePrefix + "|WARN|FlintTrust|tls rejected");
        capture.Lines.ShouldContain(FlintDiag.TracePrefix + "|ERROR|FlintCast|probe failed");
    }

    [Fact]
    public void Write_IgnoresBlankTagOrMessage()
    {
        using var capture = new CaptureListener();
        Trace.Listeners.Add(capture);
        try
        {
            FlintDiag.Info(" ", "message");
            FlintDiag.Info("tag", " ");
        }
        finally
        {
            Trace.Listeners.Remove(capture);
        }

        capture.Lines.ShouldBeEmpty();
    }

    private sealed class CaptureListener : TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Lines.Add(message);
            }
        }
    }
}

public sealed class DevLogRetentionTests
{
    [Fact]
    public void KeepNewest_ReturnsWholeContentWhenUnderRetain()
    {
        var bytes = Encoding.UTF8.GetBytes("one\ntwo\n");
        DevLogRetention.KeepNewest(bytes, retainBytes: 100).ShouldBe(bytes);
    }

    [Fact]
    public void KeepNewest_DropsOldestAndAlignsToNewline()
    {
        // 10 + 10 + 12 = 32 bytes; keep 20 → start near "line-two\nline-three\n"
        var content = Encoding.UTF8.GetBytes("line-zero\nline-one!\nline-two!\nline-three\n");
        var kept = Encoding.UTF8.GetString(DevLogRetention.KeepNewest(content, retainBytes: 22));

        kept.ShouldNotContain("line-zero");
        kept.ShouldContain("line-three");
        kept.ShouldStartWith("line-");
    }

    [Fact]
    public void KeepNewest_EmptyInputReturnsEmpty() =>
        DevLogRetention.KeepNewest(ReadOnlySpan<byte>.Empty, 10).ShouldBeEmpty();

    [Fact]
    public void ValidateBounds_RejectsRetainAtOrAboveMaximum()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DevLogRetention.ValidateBounds(100, 100));
        Should.Throw<ArgumentOutOfRangeException>(() => DevLogRetention.ValidateBounds(100, 101));
        Should.Throw<ArgumentOutOfRangeException>(() => DevLogRetention.ValidateBounds(0, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => DevLogRetention.ValidateBounds(10, 0));
    }

    [Fact]
    public void TrimOldestIfNeeded_NoOpWhenUnderCap()
    {
        var path = Path.Combine(Path.GetTempPath(), "flint-log-retention", Guid.NewGuid().ToString("N") + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "fresh\n");
            DevLogRetention.TrimOldestIfNeeded(path, maximumBytes: 1024, retainBytes: 512).ShouldBeFalse();
            File.ReadAllText(path).ShouldBe("fresh\n");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TrimOldestIfNeeded_RemovesOldestLinesAndKeepsNewest()
    {
        var path = Path.Combine(Path.GetTempPath(), "flint-log-retention", Guid.NewGuid().ToString("N") + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 200; i++)
            {
                builder.AppendLine($"OLD-{i:D3}-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
            }

            for (var i = 0; i < 40; i++)
            {
                builder.AppendLine($"NEW-{i:D3}-keep-me-please-xxxxxxxxxxxxxx");
            }

            File.WriteAllText(path, builder.ToString());
            var before = new FileInfo(path).Length;
            before.ShouldBeGreaterThan(2048);

            DevLogRetention.TrimOldestIfNeeded(path, maximumBytes: 2048, retainBytes: 1024).ShouldBeTrue();

            var after = File.ReadAllText(path);
            new FileInfo(path).Length.ShouldBeLessThanOrEqualTo(2048);
            after.ShouldContain("NEW-");
            after.ShouldNotContain("OLD-000");
            File.Exists(path + ".trim.tmp").ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TrimOldestIfNeeded_MissingFileIsNoOp() =>
        DevLogRetention.TrimOldestIfNeeded(
            Path.Combine(Path.GetTempPath(), "flint-missing-" + Guid.NewGuid().ToString("N") + ".log"),
            100,
            50).ShouldBeFalse();

    [Fact]
    public void DefaultRetainIsStrictlyBelowDefaultMaximum()
    {
        DevLogRetention.DefaultRetainBytes.ShouldBeLessThan(DevLogRetention.DefaultMaximumBytes);
        Should.NotThrow(() =>
            DevLogRetention.ValidateBounds(
                DevLogRetention.DefaultMaximumBytes,
                DevLogRetention.DefaultRetainBytes));
    }
}
