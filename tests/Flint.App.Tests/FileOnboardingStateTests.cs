using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Persistence, including the failure behaviour that matters most: an unwritable profile must
/// never stop the application starting.
/// </summary>
public sealed class FileOnboardingStateTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "flint-onboarding-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ANewProfile_HasNotCompleted()
    {
        // Act
        var state = new FileOnboardingState(_directory);

        // Assert
        state.HasCompleted.ShouldBeFalse();
    }

    [Fact]
    public void MarkCompleted_Persists()
    {
        // Arrange
        var state = new FileOnboardingState(_directory);

        // Act
        state.MarkCompleted();

        // Assert
        state.HasCompleted.ShouldBeTrue();
    }

    [Fact]
    public void CompletionSurvivesANewInstance()
    {
        // The point of persisting at all: the next launch must not show the introduction again.

        // Arrange
        new FileOnboardingState(_directory).MarkCompleted();

        // Act
        var reopened = new FileOnboardingState(_directory);

        // Assert
        reopened.HasCompleted.ShouldBeTrue();
    }

    [Fact]
    public void MarkCompleted_CreatesTheDirectoryItNeeds()
    {
        // Arrange
        var nested = Path.Combine(_directory, "deeper", "still");
        var state = new FileOnboardingState(nested);

        // Act
        state.MarkCompleted();

        // Assert
        Directory.Exists(nested).ShouldBeTrue();
        state.HasCompleted.ShouldBeTrue();
    }

    [Fact]
    public void Reset_ForgetsCompletion()
    {
        // Arrange
        var state = new FileOnboardingState(_directory);
        state.MarkCompleted();

        // Act
        state.Reset();

        // Assert
        state.HasCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Reset_OnAProfileThatNeverCompleted_DoesNotThrow()
    {
        // Arrange
        var state = new FileOnboardingState(_directory);

        // Act & Assert
        Should.NotThrow(state.Reset);
        state.HasCompleted.ShouldBeFalse();
    }

    [Fact]
    public void MarkCompleted_IsIdempotent()
    {
        // Arrange
        var state = new FileOnboardingState(_directory);

        // Act
        state.MarkCompleted();
        state.MarkCompleted();

        // Assert
        state.HasCompleted.ShouldBeTrue();
    }

    [Fact]
    public void AnUnwritableLocation_FailsQuietlyRatherThanBlockingStartup()
    {
        // Seeing the introduction twice is recoverable; a crash on launch is not. The path below is
        // invalid on Windows, so creating and writing it must fail.

        // Arrange
        var state = new FileOnboardingState(Path.Combine(_directory, "in|valid\0name"));

        // Act & Assert
        Should.NotThrow(() => state.MarkCompleted());
        Should.NotThrow(() => state.Reset());
        state.HasCompleted.ShouldBeFalse();
    }

    [Fact]
    public void AnUnreadableStateIsTreatedAsNotCompleted()
    {
        // "Unknown" must resolve to showing the introduction, never to hiding it.

        // Arrange
        var state = new FileOnboardingState(Path.Combine(_directory, "in|valid\0name"));

        // Act & Assert
        state.HasCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_BlankDirectory_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new FileOnboardingState("  "));
    }

    [Fact]
    public void TheStoreLivesUnderTheRexVendorFolder()
    {
        // Shared with the other REX products on this machine.

        // Act & Assert
        FileOnboardingState.VendorFolder.ShouldBe("REX Technologies");
        FileOnboardingState.ProductFolder.ShouldBe("Flint");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }
}
