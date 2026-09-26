using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

namespace WorldcrossInfoDisplay;

public sealed record ObsInputInfo(string Name, string Kind, string UnversionedKind, bool IsText);
public sealed record ObsDiscovery(IReadOnlyList<ObsInputInfo> Inputs);
public sealed record PublishResult(bool Succeeded, string Message);

public interface IObsTextPublisher : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<string>? StatusChanged;
    event EventHandler<bool>? ConnectionChanged;
    Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<PublishResult> PublishTextAsync(string sourceName, string text, CancellationToken cancellationToken = default);
}

public sealed class ObsTextPublisher : IObsTextPublisher
{
    private readonly OBSWebsocket _client = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    public bool IsConnected => _client.IsConnected;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<bool>? ConnectionChanged;

    public ObsTextPublisher()
    {
        _client.Connected += (_, _) => { ConnectionChanged?.Invoke(this, true); StatusChanged?.Invoke(this, "Connected to OBS."); };
        _client.Disconnected += (_, _) => { ConnectionChanged?.Invoke(this, false); StatusChanged?.Invoke(this, "Disconnected from OBS."); };
    }

    public async Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "ws") throw new ArgumentException("OBS URL must be an absolute ws:// URL.", nameof(url));
        if (_client.IsConnected) _client.Disconnect();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => connected.TrySetResult();
        _client.Connected += handler;
        try { _client.ConnectAsync(url, password); await connected.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken); }
        finally { _client.Connected -= handler; }
    }

    public Task DisconnectAsync()
    {
        if (_client.IsConnected) _client.Disconnect();
        ConnectionChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public async Task<ObsDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected) throw new InvalidOperationException("OBS is not connected.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var inputs = await Task.Run(() => _client.GetInputList().Select(ToInput).Where(x => x is not null).Cast<ObsInputInfo>().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken);
            return new ObsDiscovery(inputs);
        }
        finally { _gate.Release(); }
    }

    public async Task<PublishResult> PublishTextAsync(string sourceName, string text, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected) return new(false, "OBS is not connected.");
        if (string.IsNullOrWhiteSpace(sourceName)) return new(false, "OBS source name is required.");
        await _gate.WaitAsync(cancellationToken);
        try { await Task.Run(() => _client.SetInputSettings(sourceName, new JObject { ["text"] = text }, true), cancellationToken); return new(true, "OBS text updated."); }
        catch (Exception ex) { return new(false, $"OBS publish failed: {ex.Message}"); }
        finally { _gate.Release(); }
    }

    private static ObsInputInfo? ToInput(InputBasicInfo input)
    {
        var kind = string.IsNullOrWhiteSpace(input.UnversionedKind) ? input.InputKind : input.UnversionedKind;
        var isText = kind is "text_gdiplus" or "text_ft2_source";
        return isText ? new(input.InputName, input.InputKind, kind, true) : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync();
        _gate.Dispose();
    }
}
