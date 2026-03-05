using PortMonitor.Models;

namespace PortMonitor.Gui.ViewModels;

/// <summary>
/// View model wrapping a <see cref="ConnectionEntry"/> for WPF data binding.
/// All display formatting lives here so the XAML stays clean.
/// </summary>
public sealed class ConnectionViewModel
{
    public ConnectionViewModel(ConnectionEntry entry)
    {
        Tag           = entry.IsNew ? "[NEW]" : entry.IsClosed ? "[CLS]" : string.Empty;
        Protocol      = entry.Protocol;
        LocalAddress  = entry.LocalAddress;
        LocalPort     = entry.LocalPort;
        RemoteAddress = entry.RemoteAddress;
        RemotePortDisplay = entry.RemotePort > 0
            ? entry.RemotePort.ToString()
            : entry.State == ConnectionState.Udp ? "*" : "0";
        StateDisplay  = entry.StateDisplay;
        Pid           = entry.Pid > 0 ? entry.Pid.ToString() : "-";
        ProcessName   = entry.ProcessName;
        RemoteHost    = entry.RemoteHost;
        IsNew         = entry.IsNew;
        IsClosed      = entry.IsClosed;
    }

    /// <summary>[NEW], [CLS], or empty string.</summary>
    public string Tag           { get; }
    public string Protocol      { get; }
    public string LocalAddress  { get; }
    public int    LocalPort     { get; }
    public string RemoteAddress { get; }
    public string RemotePortDisplay { get; }
    public string StateDisplay  { get; }
    public string Pid           { get; }
    public string ProcessName   { get; }
    public string RemoteHost    { get; }

    /// <summary>Used by DataTrigger in App.xaml to colour the row green.</summary>
    public bool IsNew    { get; }
    /// <summary>Used by DataTrigger in App.xaml to dim the row.</summary>
    public bool IsClosed { get; }
}
