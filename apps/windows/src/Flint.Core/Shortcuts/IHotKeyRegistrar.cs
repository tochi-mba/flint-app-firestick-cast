namespace Flint.Core.Shortcuts;

/// <summary>Claims key combinations from Windows so they work wherever the person is.</summary>
public interface IHotKeyRegistrar
{
    /// <summary>Raised with a combination's identity each time it is pressed.</summary>
    event Action<int>? Pressed;

    /// <summary>Claims a combination under <paramref name="id"/>, letting go of whatever that id held before.</summary>
    /// <returns>Whether Windows gave it; false when another program already has it.</returns>
    bool Register(int id, HotKeyModifiers modifiers, int virtualKey);

    /// <summary>Lets go of the combination held under <paramref name="id"/>, if any.</summary>
    void Unregister(int id);
}
