namespace Flint.App.Services;

/// <summary>Profile-owned navigation state, without remote IDs or page credentials.</summary>
public sealed record BrowserSavedPage(string Url, string Title = "", bool Muted = false);

/// <summary>Both modes survive switching and reconnecting; indices refer to saved order.</summary>
public sealed record BrowserSavedSession(
    BrowserSavedPage[] Tabs, int ActiveTab, BrowserSavedPage[] Panes, int ActivePane,
    string Layout = "Single", int Column = 5000, int Row = 5000, bool WorkspaceMode = false)
{
    public static BrowserSavedSession Empty => new([], 0, [], 0);

    public BrowserSavedSession Normalize()
    {
        static BrowserSavedPage[] Pages(BrowserSavedPage[]? pages, int maximum) => (pages ?? [])
            .Take(maximum).Where(p => p is not null && p.Url is { Length: <= 4096 } &&
                (p.Url == "about:blank" || Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo)))
            .Select(p => p with { Title = (p.Title ?? "")[..Math.Min(p.Title?.Length ?? 0, 512)] }).ToArray();
        var tabs = Pages(Tabs, 8);
        var panes = Pages(Panes, 4);
        var layout = panes.Length switch
        {
            <= 1 => "Single",
            2 => Layout == "TwoRows" ? "TwoRows" : "TwoColumns",
            _ => "FourGrid",
        };
        return new(tabs, Math.Clamp(ActiveTab, 0, Math.Max(0, tabs.Length - 1)),
            panes, Math.Clamp(ActivePane, 0, Math.Max(0, panes.Length - 1)), layout,
            Math.Clamp(Column, 1500, 8500), Math.Clamp(Row, 1500, 8500), WorkspaceMode && panes.Length > 0);
    }

    public bool SameAs(BrowserSavedSession other) => ActiveTab == other.ActiveTab && ActivePane == other.ActivePane &&
        Layout == other.Layout && Column == other.Column && Row == other.Row && WorkspaceMode == other.WorkspaceMode &&
        Tabs.SequenceEqual(other.Tabs) && Panes.SequenceEqual(other.Panes);
}
