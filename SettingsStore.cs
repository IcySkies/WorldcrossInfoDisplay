using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace WorldcrossInfoDisplay;

public sealed class SettingsStore
{
    public static string RootPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SVC-AS", "WorldcrossInfoDisplay");
    private static string PathName => Path.Combine(RootPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<WorldcrossSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(PathName)) return new();
            await using var stream = File.OpenRead(PathName);
            var dto = await JsonSerializer.DeserializeAsync<SettingsDto>(stream, JsonOptions) ?? new();
            var settings = dto.ToSettings();
            settings.ObsPassword = Unprotect(dto.EncryptedObsPassword);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            try { if (File.Exists(PathName)) File.Move(PathName, PathName + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", true); } catch { }
            return new();
        }
    }

    public async Task SaveAsync(WorldcrossSettings settings)
    {
        var dto = SettingsDto.FromSettings(settings);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(RootPath);
            var temp = PathName + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, dto, JsonOptions);
                await stream.FlushAsync();
            }
            File.Move(temp, PathName, true);
        }
        finally { _gate.Release(); }
    }

    private static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string value) => string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));

    private sealed class SettingsDto
    {
        public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
        public string EncryptedObsPassword { get; set; } = "";
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
        public WorldcrossSettings ToSettings() => new() { ObsUrl = ObsUrl, GamePath = GamePath, RelayExecutablePath = RelayExecutablePath, ObsSourceName = ObsSourceName, Prefix = Prefix, Repeat = Repeat, Suffix = Suffix, RepeatCap = RepeatCap is >= -1 and <= 100000 ? RepeatCap : -1, DefaultDisplayShown = DefaultDisplayShown, PlayCount = PlayCount, PlayerVisibility = PlayerVisibility ?? new(StringComparer.Ordinal) };
        public static SettingsDto FromSettings(WorldcrossSettings s) => new() { ObsUrl = s.ObsUrl, EncryptedObsPassword = Protect(s.ObsPassword), GamePath = s.GamePath, RelayExecutablePath = s.RelayExecutablePath, ObsSourceName = s.ObsSourceName, Prefix = s.Prefix, Repeat = s.Repeat, Suffix = s.Suffix, RepeatCap = s.RepeatCap, DefaultDisplayShown = s.DefaultDisplayShown, PlayCount = s.PlayCount, PlayerVisibility = s.PlayerVisibility };
    }
}
