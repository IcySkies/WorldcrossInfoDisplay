using System.Text.Json.Serialization;

namespace WorldcrossInfoDisplay;

public enum WorldcrossEventKind
{
    Unknown = -1,
    Selection = 0,
    ChartLoadingStarted = 1,
    ChartStarted = 2,
    ChartExitTransitionStarted = 3,
    GameplayEnded = 4,
    LobbySelection = 5,
    WorldcrossRoom = 6,
    WorldcrossGameplay = 7
}

public sealed record WorldcrossPlayer
{
    public string SteamId64 { get; init; } = "";
    public string Name { get; init; } = "";
    public string State { get; init; } = "unready";
    public decimal Rating { get; init; }
    public int Class { get; init; }
    public decimal Score { get; init; }
    public decimal LastPlayScore { get; init; }
    public string Label { get; init; } = "";
}

public sealed record WorldcrossSnapshot { public IReadOnlyList<WorldcrossPlayer> Players { get; init; } = []; }

public sealed record WorldcrossEventEnvelope
{
    public int ProtocolVersion { get; init; } = 1;
    public long Sequence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public WorldcrossEventKind Kind { get; init; }
    public WorldcrossSnapshot? Worldcross { get; init; }
}

public sealed record RelayDiscovery
{
    public int ProtocolVersion { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int SubscriberPort { get; init; }
    public int ProcessId { get; init; }
}

public sealed class WorldcrossSettings
{
    public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
    public string ObsPassword { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string RelayExecutablePath { get; set; } = "";
    public string ObsSourceName { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Repeat { get; set; } = "{%rth} {%n}: {%s}\\n";
    public string Suffix { get; set; } = "";
    public int RepeatCap { get; set; } = -1;
    public bool DefaultDisplayShown { get; set; } = true;
    public int PlayCount { get; set; }
    public Dictionary<string, bool> PlayerVisibility { get; set; } = new(StringComparer.Ordinal);
}

public sealed record PlayerRow(WorldcrossPlayer Player, bool IsShown, int Standing)
{
    [JsonIgnore] public string DisplayState => IsShown ? "Shown" : "Hidden";
    [JsonIgnore] public string DisplayLabel => string.IsNullOrWhiteSpace(Player.Label) ? "CLEAR" : Player.Label;
}

public sealed record TemplateResult(string Text, IReadOnlyList<string> Warnings);
