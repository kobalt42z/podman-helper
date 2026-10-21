using System.Text.Json;

namespace PodmanHelper.Mutagen;

/// <summary>The subset of <c>mutagen sync list --template '{{json .}}'</c> the helper looks at.</summary>
public sealed record MutagenSession(
    string Identifier,
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    bool Paused,
    string Status,
    string? LastError,
    long SuccessfulCycles,
    int ConflictCount,
    bool AlphaConnected,
    bool BetaConnected,
    int ProblemCount,
    string AlphaPath,
    string BetaPath,
    string BetaHost)
{
    public bool IsWatching => Status == "watching";
    public bool IsHalted => Status.StartsWith("halted", StringComparison.Ordinal);

    /// <summary>Synced at least once and currently idle with nothing wrong.</summary>
    public bool IsSettled => !Paused && IsWatching && SuccessfulCycles > 0 && string.IsNullOrEmpty(LastError) && ConflictCount == 0;

    public string Summary
    {
        get
        {
            if (Paused) return "paused";
            var s = Status;
            if (ConflictCount > 0) s += $", {ConflictCount} conflict(s)";
            if (ProblemCount > 0) s += $", {ProblemCount} problem(s)";
            if (!string.IsNullOrEmpty(LastError)) s += $", error: {LastError}";
            return s;
        }
    }

    public static List<MutagenSession> ParseList(string json)
    {
        var sessions = new List<MutagenSession>();
        json = json.Trim();
        if (json.Length == 0 || json == "null")
        {
            return sessions;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return sessions;
        }

        foreach (var s in doc.RootElement.EnumerateArray())
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (s.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in l.EnumerateObject())
                {
                    labels[p.Name] = p.Value.GetString() ?? "";
                }
            }

            var alpha = s.GetProperty("alpha");
            var beta = s.GetProperty("beta");

            sessions.Add(new MutagenSession(
                Identifier: Str(s, "identifier"),
                Name: Str(s, "name"),
                Labels: labels,
                Paused: s.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True,
                Status: Str(s, "status"),
                LastError: s.TryGetProperty("lastError", out var err) ? err.GetString() : null,
                SuccessfulCycles: s.TryGetProperty("successfulCycles", out var cycles) && cycles.TryGetInt64(out var c) ? c : 0,
                ConflictCount: ArrayLength(s, "conflicts"),
                AlphaConnected: Bool(alpha, "connected"),
                BetaConnected: Bool(beta, "connected"),
                ProblemCount: ArrayLength(alpha, "scanProblems") + ArrayLength(alpha, "transitionProblems")
                              + ArrayLength(beta, "scanProblems") + ArrayLength(beta, "transitionProblems"),
                AlphaPath: Str(alpha, "path"),
                BetaPath: Str(beta, "path"),
                BetaHost: Str(beta, "host")));
        }

        return sessions;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int ArrayLength(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;
}
