using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using WorldcrossInfoDisplay;
using Xunit;

namespace WorldcrossInfoDisplay.Tests;

public sealed class TemplateRendererTests
{
    [Fact]
    public async Task RelaySourceUsesDiscoveryWithoutRequiringProcessId()
    {
        var root = Path.Combine(Path.GetTempPath(), "wid-relay", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "AutoChartSwitchV2");
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var subscriberPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        await File.WriteAllTextAsync(Path.Combine(directory, "bridge-relay.json"), JsonSerializer.Serialize(new
        {
            protocolVersion = 1,
            host = "127.0.0.1",
            gamePort = 28745,
            subscriberPort,
            processId = int.MaxValue
        }));

        try
        {
            await using var source = new RelayEventSource(root, Path.Combine(root, "missing-relay.exe"));
            await source.StartAsync();
            using var subscriber = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(subscriberPort, source.Port);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, true);
        }
    }
    private static WorldcrossPlayer Player(string id, string name, decimal score, decimal last, string label = "") => new() { SteamId64 = id, Name = name, Score = score, LastPlayScore = last, Label = label };

    [Fact]
    public void Hidden_players_do_not_affect_placeholder_standings()
    {
        var settings = new WorldcrossSettings { Repeat = "{%r}:{%rth}:{%R}:{%n}\\n" };
        var rows = new[] { new PlayerRow(Player("1", "A", 100, 90), true, 1), new PlayerRow(Player("2", "B", 90, 80), false, 2), new PlayerRow(Player("3", "C", 80, 70), true, 3) };
        var result = new TemplateRenderer().Render(settings, rows, new DateTimeOffset(2026, 5, 7, 21, 3, 6, TimeSpan.Zero));
        Assert.Contains("1:1st:First:A", result.Text);
        Assert.Contains("2:2nd:Second:C", result.Text);
    }

    [Fact]
    public void Uses_winner_aliases_and_repeat_cap()
    {
        var settings = new WorldcrossSettings { Repeat = "{%r1}/{%rth1}/{%R1}:{%n};", RepeatCap = 1 };
        var rows = new[] { new PlayerRow(Player("1", "A", 100, 90), true, 1), new PlayerRow(Player("2", "B", 90, 80), true, 2) };
        var result = new TemplateRenderer().Render(settings, rows, DateTimeOffset.UtcNow);
        Assert.Equal("W/WIN/WINNER:A;", result.Text);
    }

    [Fact]
    public void Leaves_unknown_tokens_visible_and_warns()
    {
        var result = new TemplateRenderer().Render(new WorldcrossSettings { Prefix = "{%unknown}" }, [], DateTimeOffset.UtcNow);
        Assert.Contains("{%unknown}", result.Text);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("FC", "FC|FULL COMBO|[FC]")]
    [InlineData(" fc ", "FC|FULL COMBO|[FC]")]
    [InlineData("", "|CLEARED|")]
    [InlineData("   ", "|CLEARED|")]
    [InlineData("CLEAR", "|CLEARED|")]
    [InlineData(" clear ", "|CLEARED|")]
    [InlineData("AC", "AC|ALL CRITICAL|[AC]")]
    [InlineData(" ac ", "AC|ALL CRITICAL|[AC]")]
    [InlineData("VS", "VS|PERFECT|[VS]")]
    [InlineData(" vs ", "VS|PERFECT|[VS]")]
    [InlineData("S", "??|UNKNOWN CLEAR|[??]")]
    public void Expands_label_placeholders(string label, string expected)
    {
        var settings = new WorldcrossSettings { Repeat = "{%l}|{%L}|{%lx}" };
        var rows = new[] { new PlayerRow(Player("1", "A", 100, 90, label), true, 1) };

        var result = new TemplateRenderer().Render(settings, rows, DateTimeOffset.UtcNow);

        Assert.Equal(expected, result.Text);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("99.49", "99|99.49")]
    [InlineData("99.50", "100|99.50")]
    [InlineData("100", "100|100.00")]
    [InlineData("-1.50", "-2|-1.50")]
    public void Expands_rounded_and_two_decimal_score_placeholders(string scoreText, string expected)
    {
        var score = decimal.Parse(scoreText, System.Globalization.CultureInfo.InvariantCulture);
        var settings = new WorldcrossSettings { Repeat = "{%s}|{%S}" };
        var rows = new[] { new PlayerRow(Player("1", "A", score, score), true, 1) };

        var result = new TemplateRenderer().Render(settings, rows, DateTimeOffset.UtcNow);

        Assert.Equal(expected, result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Protocol_version_is_numeric_in_the_wire_contract()
    {
        const string json = "{\"protocolVersion\":1,\"sequence\":7,\"kind\":\"WorldcrossRoom\",\"worldcross\":{\"players\":[]}}";
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new FlexibleWorldcrossEventKindConverter());
        var envelope = System.Text.Json.JsonSerializer.Deserialize<WorldcrossEventEnvelope>(json, options);
        Assert.NotNull(envelope);
        Assert.Equal(1, envelope!.ProtocolVersion);
        Assert.Equal(7, envelope.Sequence);
    }

    [Theory]
    [InlineData("1.0", "7.0")]
    [InlineData("\"1.0\"", "\"7.0\"")]
    public void Protocol_version_and_sequence_accept_integral_decimal_wire_values(string protocolVersion, string sequence)
    {
        var json = $"{{\"protocolVersion\":{protocolVersion},\"sequence\":{sequence},\"kind\":\"WorldcrossRoom\",\"worldcross\":{{\"players\":[]}}}}";
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleWorldcrossEventKindConverter());
        options.Converters.Add(new FlexibleInt32JsonConverter());
        options.Converters.Add(new FlexibleInt64JsonConverter());

        var envelope = System.Text.Json.JsonSerializer.Deserialize<WorldcrossEventEnvelope>(json, options);

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope!.ProtocolVersion);
        Assert.Equal(7, envelope.Sequence);
    }

    [Theory]
    [InlineData("\"Selection\"", WorldcrossEventKind.Selection)]
    [InlineData("7.0", WorldcrossEventKind.WorldcrossGameplay)]
    [InlineData("\"7.0\"", WorldcrossEventKind.WorldcrossGameplay)]
    [InlineData("\"FutureEvent\"", WorldcrossEventKind.Unknown)]
    public void Event_kind_accepts_protocol_encodings_and_ignores_unknown_values(string kind, WorldcrossEventKind expected)
    {
        var json = $"{{\"protocolVersion\":1,\"sequence\":7,\"kind\":{kind},\"worldcross\":{{\"players\":[]}}}}";
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleWorldcrossEventKindConverter());
        options.Converters.Add(new FlexibleInt32JsonConverter());
        options.Converters.Add(new FlexibleInt64JsonConverter());

        var envelope = System.Text.Json.JsonSerializer.Deserialize<WorldcrossEventEnvelope>(json, options);

        Assert.NotNull(envelope);
        Assert.Equal(expected, envelope!.Kind);
    }
}
