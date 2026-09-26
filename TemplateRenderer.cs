using System.Globalization;
using System.Text.RegularExpressions;

namespace WorldcrossInfoDisplay;

public sealed class TemplateRenderer
{
    private static readonly Regex Token = new(@"\{%[^}]+\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public TemplateResult Render(WorldcrossSettings settings, IReadOnlyList<PlayerRow> rows, DateTimeOffset now)
    {
        var shown = rows.Where(x => x.IsShown).ToList();
        var cap = settings.RepeatCap == -1 ? shown.Count : Math.Min(Math.Max(settings.RepeatCap, 0), shown.Count);
        var warnings = new List<string>();
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{%YYYY}"] = now.ToString("yyyy", CultureInfo.InvariantCulture), ["{%YY}"] = now.ToString("yy", CultureInfo.InvariantCulture),
            ["{%MM}"] = now.ToString("MM", CultureInfo.InvariantCulture), ["{%M}"] = now.Month.ToString(CultureInfo.InvariantCulture),
            ["{%m}"] = now.ToString("MMM", CultureInfo.InvariantCulture), ["{%DD}"] = now.ToString("dd", CultureInfo.InvariantCulture),
            ["{%D}"] = now.Day.ToString(CultureInfo.InvariantCulture), ["{%d}"] = Ordinal(now.Day),
            ["{%12h}"] = now.ToString("hh:mm tt", CultureInfo.InvariantCulture), ["{%24h}"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["{%12s}"] = now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture), ["{%24s}"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["{%plc}"] = rows.Count.ToString(CultureInfo.InvariantCulture), ["{%splc}"] = shown.Count.ToString(CultureInfo.InvariantCulture),
            ["{%hplc}"] = (rows.Count - shown.Count).ToString(CultureInfo.InvariantCulture), ["{%tpc}"] = settings.PlayCount.ToString(CultureInfo.InvariantCulture)
        };
        var prefix = Expand(settings.Prefix, context, warnings);
        var suffix = Expand(settings.Suffix, context, warnings);
        var repeat = string.Concat(shown.Take(cap).Select((row, index) => ExpandRepeat(settings.Repeat, row, rows, index + 1, context, warnings)));
        return new(prefix + repeat + suffix, warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private static string ExpandRepeat(string value, PlayerRow row, IReadOnlyList<PlayerRow> rows, int standing, IReadOnlyDictionary<string, string> context, ICollection<string> warnings)
    {
        var label = FormatLabel(row.Player.Label);
        var map = new Dictionary<string, string>(context, StringComparer.Ordinal)
        {
            ["{%n}"] = row.Player.Name,
            ["{%s}"] = FormatRoundedScore(row.Player.LastPlayScore), ["{%S}"] = FormatPreciseScore(row.Player.LastPlayScore),
            ["{%l}"] = label.Short, ["{%L}"] = label.Long, ["{%lx}"] = label.Decorated,
            ["{%r}"] = standing.ToString(CultureInfo.InvariantCulture), ["{%rth}"] = Ordinal(standing), ["{%R}"] = Word(standing)
        };
        if (standing == 1) { map["{%r1}"] = "W"; map["{%rth1}"] = "WIN"; map["{%R1}"] = "WINNER"; }
        else { map["{%r1}"] = map["{%r}"]; map["{%rth1}"] = map["{%rth}"]; map["{%R1}"] = map["{%R}"]; }
        return Expand(value, map, warnings);
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> map, ICollection<string> warnings) => Token.Replace(value ?? "", match =>
    {
        if (map.TryGetValue(match.Value, out var replacement)) return replacement;
        warnings.Add($"Unknown placeholder {match.Value}");
        return match.Value;
    });

    private static (string Short, string Long, string Decorated) FormatLabel(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "FC" => ("FC", "FULL COMBO", "[FC]"),
        null or "" or "CLEAR" => ("", "CLEARED", ""),
        "AC" => ("AC", "ALL CRITICAL", "[AC]"),
        "VS" => ("VS", "PERFECT", "[VS]"),
        _ => ("??", "UNKNOWN CLEAR", "[??]")
    };
    private static string FormatRoundedScore(decimal score) => decimal.Round(score, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    private static string FormatPreciseScore(decimal score) => score.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Ordinal(int number) => number % 100 is 11 or 12 or 13 ? $"{number}th" : $"{number}{(number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }}";
    private static string Word(int number) => number switch { 1 => "First", 2 => "Second", 3 => "Third", 4 => "Fourth", 5 => "Fifth", 6 => "Sixth", 7 => "Seventh", 8 => "Eighth", 9 => "Ninth", 10 => "Tenth", _ => number.ToString(CultureInfo.InvariantCulture) };
}
