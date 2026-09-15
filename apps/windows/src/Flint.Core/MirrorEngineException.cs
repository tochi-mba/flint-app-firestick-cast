namespace Flint.Core;

/// <summary>Thrown when the engine cannot start or continue a mirror session.</summary>
public sealed class MirrorEngineException : Exception
{
    /// <summary>Creates the exception with a user-facing explanation.</summary>
    public MirrorEngineException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an explanation and an underlying cause.</summary>
    public MirrorEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
