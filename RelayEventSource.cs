using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldcrossInfoDisplay;

public interface IWorldcrossEventSource : IAsyncDisposable
{
    bool IsListening { get; }
    int Port { get; }
    string Status { get; }
    event EventHandler<WorldcrossEventEnvelope>? EventReceived;
    event EventHandler<string>? StatusChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task FindRelayAsync();
    Task StopAsync();
}

public sealed class RelayEventSource : IWorldcrossEventSource
{
    private const int MaxFrameBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _gamePath;
    private readonly string _relayExecutable;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private DateTimeOffset _lastLaunch = DateTimeOffset.MinValue;
    public bool IsListening { get; private set; }
    public int Port { get; private set; }
    public string Status { get; private set; } = "Stopped";
    public event EventHandler<WorldcrossEventEnvelope>? EventReceived;
    public event EventHandler<string>? StatusChanged;

    public RelayEventSource(string gamePath, string relayExecutable)
    {
        _gamePath = gamePath ?? "";
        _relayExecutable = string.IsNullOrWhiteSpace(relayExecutable) ? Path.Combine(AppContext.BaseDirectory, "VividStasisGameInfoRelay.exe") : relayExecutable;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening) return Task.CompletedTask;
        IsListening = true;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var discovery = await ReadDiscoveryAsync(cancellationToken);
                if (discovery is null)
                {
                    LaunchRelayIfConfigured();
                    SetStatus(string.IsNullOrWhiteSpace(_gamePath) ? "Game path is not configured." : "Waiting for relay discovery.");
                    await DelayOrWakeAsync(cancellationToken);
                    continue;
                }
                Port = discovery.SubscriberPort;
                using var client = new TcpClient();
                await client.ConnectAsync(discovery.Host, discovery.SubscriberPort, cancellationToken);
                SetStatus($"Connected to relay {discovery.Host}:{discovery.SubscriberPort}.");
                await ReadFramesAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { SetStatus($"Relay disconnected: {ex.Message}"); }
            if (!cancellationToken.IsCancellationRequested) await DelayOrWakeAsync(cancellationToken);
        }
        SetStatus("Stopped");
    }

    private async Task ReadFramesAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var lastSequence = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            var header = new byte[4];
            if (!await ReadExactlyAsync(stream, header, cancellationToken)) return;
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > MaxFrameBytes) throw new InvalidDataException("Invalid relay frame length.");
            var payload = new byte[length];
            if (!await ReadExactlyAsync(stream, payload, cancellationToken)) return;
            var envelope = JsonSerializer.Deserialize<WorldcrossEventEnvelope>(payload, JsonOptions);
            if (envelope is null || envelope.ProtocolVersion != 1 || envelope.Sequence <= lastSequence) continue;
            lastSequence = envelope.Sequence;
            if (envelope.Kind is WorldcrossEventKind.WorldcrossRoom or WorldcrossEventKind.WorldcrossGameplay) EventReceived?.Invoke(this, envelope);
        }
    }

    private async Task<RelayDiscovery?> ReadDiscoveryAsync(CancellationToken cancellationToken)
    {
        foreach (var path in GetDiscoveryPaths())
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var value = await JsonSerializer.DeserializeAsync<RelayDiscovery>(stream, JsonOptions, cancellationToken);
                if (value is not null &&
                    value.ProtocolVersion == 1 &&
                    value.GamePort == 28745 &&
                    value.SubscriberPort is >= 1 and <= 65535 &&
                    !string.IsNullOrWhiteSpace(value.Host))
                    return value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }
        return null;
    }

    private IEnumerable<string> GetDiscoveryPaths()
    {
        if (!string.IsNullOrWhiteSpace(_gamePath))
            yield return Path.Combine(_gamePath, "AutoChartSwitchV2", "bridge-relay.json");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SVC-AS", "VividStasisGameInfoRelay", "bridge-relay.json");
    }

    public Task FindRelayAsync()
    {
        SetStatus("Searching for a running API relay...");
        if (_wake.CurrentCount == 0) _wake.Release();
        return Task.CompletedTask;
    }

    private void LaunchRelayIfConfigured()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_gamePath) || !File.Exists(_relayExecutable) || DateTimeOffset.UtcNow - _lastLaunch < TimeSpan.FromSeconds(5)) return;
            Process.Start(new ProcessStartInfo { FileName = _relayExecutable, Arguments = $"--game-path \"{_gamePath}\"", WorkingDirectory = Path.GetDirectoryName(_relayExecutable) ?? AppContext.BaseDirectory, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
            _lastLaunch = DateTimeOffset.UtcNow;
            SetStatus("Relay launch requested.");
        }
        catch (Exception ex) { SetStatus($"Relay launch failed: {ex.Message}"); }
    }

    private void SetStatus(string value) { Status = value; StatusChanged?.Invoke(this, value); }
    public async Task StopAsync()
    {
        if (!IsListening) return;
        IsListening = false;
        _stop?.Cancel();
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        _stop?.Dispose(); _stop = null;
        _wake.Dispose();
    }
    public async ValueTask DisposeAsync() => await StopAsync();
    private async Task DelayOrWakeAsync(CancellationToken cancellationToken)
    {
        var delay = Task.Delay(1000, cancellationToken);
        var signal = _wake.WaitAsync(cancellationToken);
        await Task.WhenAny(delay, signal);
        if (delay.IsCompletedSuccessfully) await delay;
    }
    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken token) { var offset = 0; while (offset < buffer.Length) { var count = await stream.ReadAsync(buffer.AsMemory(offset), token); if (count == 0) return false; offset += count; } return true; }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new FlexibleWorldcrossEventKindConverter());
        options.Converters.Add(new FlexibleDecimalConverter());
        options.Converters.Add(new FlexibleInt32JsonConverter());
        options.Converters.Add(new FlexibleInt64JsonConverter());
        return options;
    }
    private sealed class FlexibleDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var n)) return n;
            if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            return 0m;
        }
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}
