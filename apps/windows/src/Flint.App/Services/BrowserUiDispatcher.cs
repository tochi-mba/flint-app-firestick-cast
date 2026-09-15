using Avalonia.Threading;

namespace Flint.App.Services;

/// <summary>Small seam that keeps receive-loop callbacks away from bound Avalonia properties.</summary>
public interface IBrowserUiDispatcher
{
    /// <summary>Runs now on the UI thread or queues the work there.</summary>
    void Dispatch(Action action);
}

/// <summary>Production Avalonia dispatcher.</summary>
internal sealed class AvaloniaBrowserUiDispatcher : IBrowserUiDispatcher
{
    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);
        }
    }
}
