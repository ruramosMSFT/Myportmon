using PortMonitor.Models;
using PortMonitor.Services;
using Xunit;

namespace PortMonitor.Tests;

/// <summary>Unit tests for <see cref="DiffEngine"/> new/closed detection logic.</summary>
public class DiffEngineTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ConnectionEntry MakeEntry(string proto, string localAddr, int localPort,
                                             string remoteAddr = "0.0.0.0", int remotePort = 0,
                                             ConnectionState state = ConnectionState.Established,
                                             int pid = 100, string process = "test")
        => new()
        {
            Protocol      = proto,
            LocalAddress  = localAddr,
            LocalPort     = localPort,
            RemoteAddress = remoteAddr,
            RemotePort    = remotePort,
            State         = state,
            Pid           = pid,
            ProcessName   = process
        };

    // ── Tests: new entries ────────────────────────────────────────────────────

    [Fact]
    public void FirstPoll_AllEntriesMarkedNew()
    {
        var engine  = new DiffEngine();
        var entries = new List<ConnectionEntry>
        {
            MakeEntry("TCP", "0.0.0.0", 80),
            MakeEntry("TCP", "0.0.0.0", 443)
        };

        var result = engine.Apply(entries);

        Assert.All(result, e => Assert.True(e.IsNew));
    }

    [Fact]
    public void SecondPoll_ExistingEntriesNotMarkedNew()
    {
        var engine = new DiffEngine();
        var entry  = MakeEntry("TCP", "0.0.0.0", 80);

        engine.Apply(new List<ConnectionEntry> { entry });  // first poll

        var entry2 = MakeEntry("TCP", "0.0.0.0", 80);      // same key
        var result = engine.Apply(new List<ConnectionEntry> { entry2 });

        Assert.Single(result);
        Assert.False(result[0].IsNew);
    }

    [Fact]
    public void NewEntryAppearsOnSecondPoll_IsMarkedNew()
    {
        var engine = new DiffEngine();
        var first  = MakeEntry("TCP", "0.0.0.0", 80);

        engine.Apply(new List<ConnectionEntry> { first });

        var second    = MakeEntry("TCP", "0.0.0.0", 80);
        var brandNew  = MakeEntry("TCP", "192.168.1.10", 52000, "8.8.8.8", 443);
        var result    = engine.Apply(new List<ConnectionEntry> { second, brandNew });

        var newEntry = result.Single(e => e.LocalPort == 52000);
        Assert.True(newEntry.IsNew);

        var oldEntry = result.Single(e => e.LocalPort == 80);
        Assert.False(oldEntry.IsNew);
    }

    // ── Tests: closed entries ─────────────────────────────────────────────────

    [Fact]
    public void EntryThatDisappears_IsMarkedClosed()
    {
        var engine = new DiffEngine();
        var entry  = MakeEntry("TCP", "0.0.0.0", 8080);

        engine.Apply(new List<ConnectionEntry> { entry });

        // Second poll: entry is gone
        var result = engine.Apply(new List<ConnectionEntry>());

        Assert.Single(result);
        Assert.True(result[0].IsClosed);
        Assert.Equal(1, result[0].ClosedCycles);
    }

    [Fact]
    public void ClosedEntry_FadesAfterTwoCycles()
    {
        var engine = new DiffEngine();
        var entry  = MakeEntry("TCP", "0.0.0.0", 9000);

        engine.Apply(new List<ConnectionEntry> { entry });

        var cycle1 = engine.Apply(new List<ConnectionEntry>());  // cycle 1 → ClosedCycles=1
        Assert.Single(cycle1.Where(e => e.IsClosed));

        var cycle2 = engine.Apply(new List<ConnectionEntry>());  // cycle 2 → ClosedCycles=2
        Assert.Single(cycle2.Where(e => e.IsClosed));

        var cycle3 = engine.Apply(new List<ConnectionEntry>());  // cycle 3 → faded out
        Assert.DoesNotContain(cycle3, e => e.IsClosed);
    }

    [Fact]
    public void ClosedEntry_IsNeverMarkedNew()
    {
        var engine = new DiffEngine();
        engine.Apply(new List<ConnectionEntry> { MakeEntry("TCP", "0.0.0.0", 7070) });

        var result = engine.Apply(new List<ConnectionEntry>());

        var closed = result.Single(e => e.IsClosed);
        Assert.False(closed.IsNew);
    }

    // ── Tests: reset ─────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsPreviousState_AllNewOnNextPoll()
    {
        var engine = new DiffEngine();
        var entry  = MakeEntry("TCP", "0.0.0.0", 80);

        engine.Apply(new List<ConnectionEntry> { entry }); // establishes state
        engine.Reset();

        var entry2 = MakeEntry("TCP", "0.0.0.0", 80);
        var result = engine.Apply(new List<ConnectionEntry> { entry2 });

        Assert.Single(result);
        Assert.True(result[0].IsNew, "After reset, the same entry should be treated as new.");
    }

    // ── Tests: UDP ───────────────────────────────────────────────────────────

    [Fact]
    public void UdpEntry_TrackedCorrectly()
    {
        var engine = new DiffEngine();
        var udp    = MakeEntry("UDP", "0.0.0.0", 53, "*", 0, ConnectionState.Udp);

        var first = engine.Apply(new List<ConnectionEntry> { udp });
        Assert.True(first[0].IsNew);
        Assert.Equal(ConnectionState.Udp, first[0].State);

        var udp2   = MakeEntry("UDP", "0.0.0.0", 53, "*", 0, ConnectionState.Udp);
        var second = engine.Apply(new List<ConnectionEntry> { udp2 });
        Assert.False(second[0].IsNew);
    }

    // ── Tests: mixed pool ─────────────────────────────────────────────────────

    [Fact]
    public void MixedPool_NewAndClosedCorrectlyAnnotated()
    {
        var engine = new DiffEngine();

        var initial = new List<ConnectionEntry>
        {
            MakeEntry("TCP", "0.0.0.0", 80,   state: ConnectionState.Listen),
            MakeEntry("TCP", "0.0.0.0", 443,  state: ConnectionState.Listen),
            MakeEntry("TCP", "192.168.1.1", 5000, state: ConnectionState.Established),
        };
        engine.Apply(initial);

        // Port 80 closed, port 8080 opened
        var second = new List<ConnectionEntry>
        {
            MakeEntry("TCP", "0.0.0.0", 443,  state: ConnectionState.Listen),
            MakeEntry("TCP", "192.168.1.1", 5000, state: ConnectionState.Established),
            MakeEntry("TCP", "0.0.0.0", 8080, state: ConnectionState.Listen),
        };
        var result = engine.Apply(second);

        Assert.True(result.Single(e => e.LocalPort == 8080).IsNew,   "8080 should be new");
        Assert.True(result.Single(e => e.LocalPort == 80).IsClosed,  "80 should be closed");
        Assert.False(result.Single(e => e.LocalPort == 443).IsNew,   "443 should not be new");
        Assert.False(result.Single(e => e.LocalPort == 443).IsClosed,"443 should not be closed");
    }
}
