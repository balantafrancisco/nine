using System.Net;
using System.Text.Json;

namespace CornersApi.ApiFootball;

// ---------- DTOs simples (equivalen a los dicts de Python) ----------
public record ApiTeam(int? ExternalId, string? Name);

public record ApiFixture(
    int? ExternalId,
    DateTimeOffset? KickoffAt,
    string? Status,
    ApiTeam HomeTeam,
    ApiTeam AwayTeam,
    int? HomeGoals,
    int? AwayGoals);

public record ApiTeamCorners(int? TeamExternalId, int? Corners);

public class ApiFootballException : Exception
{
    public ApiFootballException(string message) : base(message) { }
}

// ---------- Parseo puro (sin red) ----------
public static class ApiFootballParser
{
    public static List<ApiFixture> ParseFixtures(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<ApiFixture>();

        if (!doc.RootElement.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in response.EnumerateArray())
        {
            var fixture = item.GetProperty("fixture");
            var teams = item.GetProperty("teams");
            var goals = item.TryGetProperty("goals", out var g) ? g : default;

            result.Add(new ApiFixture(
                ExternalId: GetInt(fixture, "id"),
                KickoffAt: fixture.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetDateTimeOffset() : null,
                Status: fixture.TryGetProperty("status", out var s) && s.TryGetProperty("short", out var sh)
                    ? sh.GetString() : null,
                HomeTeam: ParseTeam(teams.GetProperty("home")),
                AwayTeam: ParseTeam(teams.GetProperty("away")),
                HomeGoals: goals.ValueKind == JsonValueKind.Object ? GetInt(goals, "home") : null,
                AwayGoals: goals.ValueKind == JsonValueKind.Object ? GetInt(goals, "away") : null));
        }
        return result;
    }

    // "Corner Kicks" puede venir null si el proveedor no tiene el dato.
    public static List<ApiTeamCorners> ParseStatistics(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<ApiTeamCorners>();

        if (!doc.RootElement.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var block in response.EnumerateArray())
        {
            int? teamId = block.TryGetProperty("team", out var t) ? GetInt(t, "id") : null;
            int? corners = null;

            if (block.TryGetProperty("statistics", out var stats) && stats.ValueKind == JsonValueKind.Array)
            {
                foreach (var stat in stats.EnumerateArray())
                {
                    if (stat.TryGetProperty("type", out var type) && type.GetString() == "Corner Kicks")
                    {
                        corners = stat.TryGetProperty("value", out var v) ? ToNullableInt(v) : null;
                        break;
                    }
                }
            }
            result.Add(new ApiTeamCorners(teamId, corners));
        }
        return result;
    }

    private static ApiTeam ParseTeam(JsonElement e) =>
        new(GetInt(e, "id"), e.TryGetProperty("name", out var n) ? n.GetString() : null);

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? ToNullableInt(v) : null;

    // El valor puede ser número, string numérico o null
    private static int? ToNullableInt(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetInt32(),
        JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
        _ => null
    };
}

// ---------- Cliente HTTP ----------
public class ApiFootballClient
{
    private readonly HttpClient _http;

    public ApiFootballClient(HttpClient http) => _http = http;

    /// <summary>Partidos de una liga/temporada. status="FT" para solo finalizados.</summary>
    public async Task<List<ApiFixture>> GetFixturesAsync(
        int leagueId, int season, string? status = null, CancellationToken ct = default)
    {
        var url = $"fixtures?league={leagueId}&season={season}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";

        var json = await SendAsync(url, ct);
        return ApiFootballParser.ParseFixtures(json);
    }

    /// <summary>Estadísticas (incluye córners) de un partido.</summary>
    public async Task<List<ApiTeamCorners>> GetFixtureStatisticsAsync(
        int fixtureId, CancellationToken ct = default)
    {
        var json = await SendAsync($"fixtures/statistics?fixture={fixtureId}", ct);
        return ApiFootballParser.ParseStatistics(json);
    }

        private async Task<string> SendAsync(string relativeUrl, CancellationToken ct)
    {
        using var response = await _http.GetAsync(relativeUrl, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Console.WriteLine("⚠️ [API-FOOTBALL] Cuota agotada. Se omitirá esta petición para no detener el proceso.");
            return "{\"response\": []}"; // Retorna un JSON vacío válido para que no rompa el Parser
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Console.WriteLine($"⚠️ [API-FOOTBALL] Acceso denegado ({(int)response.StatusCode}). Verifica tu API Key.");
            return "{\"response\": []}";
        }

        response.EnsureSuccessStatusCode();

        // API-Football puede devolver 200 con "errors" en el body
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && HasErrors(errors))
        {
            Console.WriteLine($"⚠️ [API-FOOTBALL] Error devuelto: {errors.GetRawText()}. Se ignorará.");
            return "{\"response\": []}";
        }

        return body;
    }

    // "errors" viene como [] cuando no hay error, o como objeto/lista con mensajes
    private static bool HasErrors(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.Object => e.EnumerateObject().Any(),
        _ => false
    };
}