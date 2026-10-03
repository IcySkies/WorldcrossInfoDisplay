using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace WorldcrossInfoDisplay;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _store;
    private readonly IWorldcrossEventSource _relay;
    private readonly IObsTextPublisher _obs;
    private readonly TemplateRenderer _renderer = new();
    private string _relayStatus = "Starting...";
    private string _obsStatus = "Disconnected";
    private string _eventStatus = "No Worldcross event received.";
    private string _previewText = "";
    private string _statusText = "Ready.";
    private PlayerRow? _selectedPlayer;
    private IReadOnlyList<WorldcrossPlayer> _players = [];
    private string _lastPlaySignature = "";

    public WorldcrossSettings Settings { get; }
    public ObservableCollection<PlayerRow> Players { get; } = [];
    public ObservableCollection<string> ObsTextSources { get; } = [];
    public PlayerRow? SelectedPlayer { get => _selectedPlayer; set => Set(ref _selectedPlayer, value); }
    public string RelayStatus { get => _relayStatus; private set => Set(ref _relayStatus, value); }
    public string ObsStatus { get => _obsStatus; private set => Set(ref _obsStatus, value); }
    public string EventStatus { get => _eventStatus; private set => Set(ref _eventStatus, value); }
    public string PreviewText { get => _previewText; private set => Set(ref _previewText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public int TotalPlayers => Players.Count;
    public int ShownPlayers => Players.Count(x => x.IsShown);
    public int HiddenPlayers => TotalPlayers - ShownPlayers;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(WorldcrossSettings settings, SettingsStore store, IWorldcrossEventSource relay, IObsTextPublisher obs)
    {
        Settings = settings; _store = store; _relay = relay; _obs = obs;
        _relay.StatusChanged += (_, message) => RunOnUi(() => RelayStatus = message);
        _relay.EventReceived += (_, envelope) => RunOnUi(() => OnEnvelope(envelope));
        _obs.StatusChanged += (_, message) => RunOnUi(() => StatusText = message);
        _obs.ConnectionChanged += (_, connected) => RunOnUi(() => ObsStatus = connected ? "Connected" : "Disconnected");
    }

    public async Task InitializeAsync()
    {
        await _relay.StartAsync();
        ApplyPreview();
        StatusText = "Live monitor ready. Connect to OBS when you want to publish.";
    }

    public async Task FindRelayAsync()
    {
        await _relay.FindRelayAsync();
        RelayStatus = "Searching for a running API relay...";
    }

    public async Task ToggleObsAsync()
    {
        try
        {
            if (_obs.IsConnected) { await _obs.DisconnectAsync(); return; }
            await _store.SaveAsync(Settings);
            await _obs.ConnectAsync(Settings.ObsUrl.Trim(), Settings.ObsPassword);
            await RefreshObsSourcesAsync();
            await ApplyAsync();
        }
        catch (Exception ex) { StatusText = $"OBS connection failed: {ex.Message}"; }
    }

    public async Task RefreshObsSourcesAsync()
    {
        if (!_obs.IsConnected) { StatusText = "Connect to OBS before refreshing sources."; return; }
        try
        {
            var discovery = await _obs.DiscoverAsync();
            ObsTextSources.Clear(); foreach (var input in discovery.Inputs) ObsTextSources.Add(input.Name);
            StatusText = $"Loaded {ObsTextSources.Count} compatible OBS text sources.";
        }
        catch (Exception ex) { StatusText = $"OBS source refresh failed: {ex.Message}"; }
    }

    public async Task ApplyAsync()
    {
        if (Settings.RepeatCap < -1) { StatusText = "Repeat cap must be -1 or a non-negative integer."; return; }
        ApplyPreview();
        await _store.SaveAsync(Settings);
        if (_obs.IsConnected)
        {
            var result = await _obs.PublishTextAsync(Settings.ObsSourceName.Trim(), PreviewText);
            StatusText = result.Message;
        }
        else StatusText = "Preview updated. OBS is disconnected.";
    }

    public async Task ToggleSelectedVisibilityAsync()
    {
        if (SelectedPlayer is null) { StatusText = "Select a player first."; return; }
        var id = SelectedPlayer.Player.SteamId64;
        var current = SelectedPlayer.IsShown;
        Settings.PlayerVisibility[id] = !current;
        RebuildRows(_players);
        SelectedPlayer = Players.FirstOrDefault(x => x.Player.SteamId64 == id);
        await ApplyAsync();
    }

    internal void OnEnvelope(WorldcrossEventEnvelope envelope)
    {
        EventStatus = $"Sequence {envelope.Sequence}: {envelope.Kind}";
        if (envelope.Worldcross is null) return;
        // Finalized scores arrive in room snapshots after gameplay has ended.
        var signature = string.Join("|", envelope.Worldcross.Players.OrderBy(x => x.SteamId64, StringComparer.Ordinal).Select(x => $"{x.SteamId64}:{x.LastPlayScore.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        if (signature != _lastPlaySignature && envelope.Worldcross.Players.Any(x => x.LastPlayScore != 0)) Settings.PlayCount++;
        _lastPlaySignature = signature;
        _players = envelope.Worldcross.Players.Where(x => !string.IsNullOrWhiteSpace(x.SteamId64)).OrderByDescending(x => x.Score).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SteamId64, StringComparer.Ordinal).ToList();
        RebuildRows(_players);
        ApplyPreview();
        _ = PublishIfConnectedAsync();
    }

    private void RebuildRows(IReadOnlyList<WorldcrossPlayer> players)
    {
        Players.Clear();
        var standing = 0;
        foreach (var player in players)
        {
            standing++;
            var shown = Settings.PlayerVisibility.TryGetValue(player.SteamId64, out var value) ? value : Settings.DefaultDisplayShown;
            if (!Settings.PlayerVisibility.ContainsKey(player.SteamId64)) Settings.PlayerVisibility[player.SteamId64] = shown;
            Players.Add(new PlayerRow(player, shown, standing));
        }
        Raise(nameof(TotalPlayers)); Raise(nameof(ShownPlayers)); Raise(nameof(HiddenPlayers));
    }

    private void ApplyPreview()
    {
        var result = _renderer.Render(Settings, Players, DateTimeOffset.Now);
        PreviewText = result.Text;
        if (result.Warnings.Count > 0) StatusText = string.Join("; ", result.Warnings);
    }

    private async Task PublishIfConnectedAsync()
    {
        if (!_obs.IsConnected || string.IsNullOrWhiteSpace(Settings.ObsSourceName)) return;
        var result = await _obs.PublishTextAsync(Settings.ObsSourceName, PreviewText);
        RunOnUi(() => StatusText = result.Message);
    }

    private void RunOnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Raise(name); }
    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public async Task ShutdownAsync() { await _store.SaveAsync(Settings); await _relay.StopAsync(); await _obs.DisposeAsync(); }
}
