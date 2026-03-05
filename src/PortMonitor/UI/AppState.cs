namespace PortMonitor.UI;

/// <summary>Columns available for sorting the connection table.</summary>
public enum SortColumn { Port, Pid, State, Process }

/// <summary>Connection-type filter modes.</summary>
public enum ViewMode { All, ListenOnly, EstablishedOnly }

/// <summary>Holds all mutable interactive-UI state shared between the render loop and key-input task.</summary>
public class AppState
{
    /// <summary>Active filter string matched against any column (IP, port, process).</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Currently active sort column.</summary>
    public SortColumn Sort { get; set; } = SortColumn.Port;

    /// <summary>Current connection-type view filter.</summary>
    public ViewMode View { get; set; } = ViewMode.All;

    /// <summary>Zero-based page index for pagination.</summary>
    public int Page { get; set; } = 0;

    /// <summary>Number of connection rows that fit in the terminal's visible area.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>True while the user is typing a filter string.</summary>
    public bool IsEnteringFilter { get; set; } = false;

    /// <summary>Buffer for the in-progress filter string before it is committed.</summary>
    public string FilterInput { get; set; } = string.Empty;

    /// <summary>Maximum page index from the most recent render, used to clamp scroll-down.</summary>
    public int MaxPage { get; set; } = 0;

    // ── Mutations ────────────────────────────────────────────────────────────

    /// <summary>Advances the sort column to the next option in the cycle.</summary>
    public void CycleSortColumn()
    {
        Sort = Sort switch
        {
            SortColumn.Port    => SortColumn.Pid,
            SortColumn.Pid     => SortColumn.State,
            SortColumn.State   => SortColumn.Process,
            SortColumn.Process => SortColumn.Port,
            _                  => SortColumn.Port
        };
        Page = 0;
    }

    /// <summary>Scrolls the view up by one page.</summary>
    public void ScrollUp()   => Page = Math.Max(0, Page - 1);

    /// <summary>Scrolls the view down by one page, clamped to <see cref="MaxPage"/>.</summary>
    public void ScrollDown() => Page = Math.Min(MaxPage, Page + 1);

    /// <summary>Resets filter, view mode, sort column and page to defaults.</summary>
    public void ResetFilters()
    {
        Filter = string.Empty;
        View   = ViewMode.All;
        Sort   = SortColumn.Port;
        Page   = 0;
    }
}
