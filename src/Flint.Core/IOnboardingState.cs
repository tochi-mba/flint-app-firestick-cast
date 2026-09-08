namespace Flint.Core;

/// <summary>
/// Remembers whether the user has been through the introduction.
/// </summary>
/// <remarks>
/// An interface so the onboarding flow can be tested without touching the disk, and so a failure
/// to persist never blocks the application from starting.
/// </remarks>
public interface IOnboardingState
{
    /// <summary>
    /// Whether the introduction has been completed or deliberately skipped.
    /// </summary>
    /// <remarks>
    /// Implementations must return <see langword="false"/> when the answer cannot be read. Showing
    /// the introduction twice is a small annoyance; hiding it from someone who has never seen it
    /// leaves them with no explanation of the one fact that decides whether Flint can work at all.
    /// </remarks>
    bool HasCompleted { get; }

    /// <summary>Records that the introduction is done.</summary>
    void MarkCompleted();

    /// <summary>Forgets the completion, so the introduction shows again.</summary>
    void Reset();
}
