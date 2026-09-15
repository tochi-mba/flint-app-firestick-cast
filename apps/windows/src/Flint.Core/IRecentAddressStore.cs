namespace Flint.Core;

/// <summary>Stores direct Fire TV endpoints for reuse between launches.</summary>
public interface IRecentAddressStore
{
    /// <summary>Reads saved endpoints, newest first.</summary>
    IReadOnlyList<RecentAddress> Load();

    /// <summary>Places an endpoint at the front of the history.</summary>
    void Remember(RecentAddress address);

    /// <summary>Deletes every remembered endpoint.</summary>
    void Clear();
}
