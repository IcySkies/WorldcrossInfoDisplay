using System;
using System.Threading;
using System.Threading.Tasks;
using WorldcrossInfoDisplay;
using Xunit;

namespace WorldcrossInfoDisplay.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void First_completed_play_is_recorded_when_final_score_arrives_in_room()
    {
        var viewModel = CreateViewModel();
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 0, 0));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 90.3m, 0));
        Assert.Equal(0, viewModel.Settings.PlayCount);

        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 90.3m, 90.3m));

        Assert.Equal(1, viewModel.Settings.PlayCount);
        Assert.Equal(90.3m, Assert.Single(viewModel.Players).Player.LastPlayScore);
        Assert.Equal("1:A:90.30;", viewModel.PreviewText);
    }

    [Fact]
    public void Repeated_room_and_gameplay_snapshots_do_not_count_a_result_twice()
    {
        var viewModel = CreateViewModel();
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 90.3m, 0));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 90.3m, 90.3m));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 90.3m, 90.3m));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 0, 90.3m));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 50, 90.3m));

        Assert.Equal(1, viewModel.Settings.PlayCount);
        Assert.Equal("1:A:90.30;", viewModel.PreviewText);
    }

    [Fact]
    public void Subsequent_finalized_room_scores_increment_the_existing_count()
    {
        var viewModel = CreateViewModel(5);
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 90.3m, 0));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 90.3m, 90.3m));
        Assert.Equal(6, viewModel.Settings.PlayCount);
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 50, 90.3m));

        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 95.1m, 95.1m));

        Assert.Equal(7, viewModel.Settings.PlayCount);
        Assert.Equal("7:A:95.10;", viewModel.PreviewText);
    }

    [Fact]
    public void Finalized_score_in_first_gameplay_snapshot_is_still_recorded()
    {
        var viewModel = CreateViewModel();

        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 90.3m, 90.3m));

        Assert.Equal(1, viewModel.Settings.PlayCount);
        Assert.Equal("1:A:90.30;", viewModel.PreviewText);
    }

    [Fact]
    public void Startup_and_live_scores_without_a_finalized_result_do_not_increment_count()
    {
        var viewModel = CreateViewModel();
        viewModel.OnEnvelope(new WorldcrossEventEnvelope
        {
            Kind = WorldcrossEventKind.WorldcrossRoom,
            Worldcross = new WorldcrossSnapshot()
        });
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossRoom, 0, 0));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 50, 0));
        viewModel.OnEnvelope(Snapshot(WorldcrossEventKind.WorldcrossGameplay, 90.3m, 0));

        Assert.Equal(0, viewModel.Settings.PlayCount);
        Assert.Equal("0:A:0.00;", viewModel.PreviewText);
    }

    private static MainViewModel CreateViewModel(int playCount = 0) => new(
        new WorldcrossSettings { PlayCount = playCount, Prefix = "{%tpc}:", Repeat = "{%n}:{%S};" },
        new SettingsStore(), new StubEventSource(), new DisconnectedObsPublisher());

    private static WorldcrossEventEnvelope Snapshot(WorldcrossEventKind kind, decimal score, decimal lastPlayScore) => new()
    {
        Kind = kind,
        Worldcross = new WorldcrossSnapshot
        {
            Players = [new WorldcrossPlayer { SteamId64 = "1", Name = "A", Score = score, LastPlayScore = lastPlayScore }]
        }
    };

    private sealed class StubEventSource : IWorldcrossEventSource
    {
        public bool IsListening => false;
        public int Port => 0;
        public string Status => "Stopped";
        public event EventHandler<WorldcrossEventEnvelope>? EventReceived { add { } remove { } }
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FindRelayAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisconnectedObsPublisher : IObsTextPublisher
    {
        public bool IsConnected => false;
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
        public Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ObsDiscovery([]));
        public Task<PublishResult> PublishTextAsync(string sourceName, string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishResult(false, "Disconnected"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
