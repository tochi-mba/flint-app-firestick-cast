using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Flint.App.Tests.TestAppBuilder))]

namespace Flint.App.Tests;

/// <summary>
/// Boots the real Flint application on the headless platform.
/// </summary>
/// <remarks>
/// The application under test is the actual one, styles and control themes included, so these tests
/// exercise the REX design system rather than a stand-in. A token renamed in
/// <c>FlintColors.axaml</c> fails here.
/// </remarks>
public static class TestAppBuilder
{
    /// <summary>Builds the headless application.</summary>
    /// <remarks>
    /// Real Skia rendering rather than the headless drawing stub. The stub cannot load a font, and
    /// these tests assert on rendered text — upper-casing, tracking, the type scale — so a stub
    /// would make every one of them vacuous.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FlintApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
