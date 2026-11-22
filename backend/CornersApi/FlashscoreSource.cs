using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using CornersApi.ApiFootball;

namespace CornersApi;

public record LeagueInfo(string Name, string Path, string Key);

public class FlashscoreSource(HttpClient http, ApiFootballClient apiFootball) : IMatchSource
{
    const string Feed = "https://global.flashscore.ninja/2/x/feed/";
    static string? _sign;
    readonly Dictionary<int, List<Dictionary<string, string>>> _days = new();

    async Task<string> Sign()
    {
        if (_sign is not null) return _sign;
        _sign = "SW9D1eZo";
        try
        {
            var html = await http.GetStringAsync("https://www.flashscore.com/");
            var m = Regex.Match(html, @"window\.environment\s*=\s*(\{.*\})");
            if (m.Success)
            {
                using var d = JsonDocument.Parse(m.Groups[1].Value);
                _sign = d.RootElement.GetProperty("config").GetProperty("app").GetProperty("feed_sign").GetString() ?? _sign;
            }
        }
        catch { }
        return _sign;
    }

    public async Task<string> Raw(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Feed + path);
        req.Headers.Add("X-Fsign", await Sign());
        req.Headers.Referrer = new Uri("https://www.flashscore.com/");

        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Flashscore respondió {(int)res.StatusCode} en {path}");

        var bytes = await res.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes);
    }

    static List<Dictionary<string, string>> ParseGames(string text)
    {
        var list = new List<Dictionary<string, string>>();
        Dictionary<string, string>? cur = null;
        var lg = new Dictionary<string, string>();
        foreach (var part in text.Split('~').SelectMany(b => b.Split('¬')))
        {
            int i = part.IndexOf('÷'); if (i <= 0) continue;
            string k = part[..i], v = part[(i + 1)..];
            if (k == "ZA") lg = new() { ["ZA"] = v };
            else if (k is "ZL" or "ZB" or "ZC") lg[k] = v;
            else if (k == "AA") { cur = new(lg) { ["id"] = v }; list.Add(cur); }
            else if (cur is not null && (k is "AE" or "AF" or "AD" or "AB")) cur[k] = v;
        }
        return list;
    }

    static string SeasonOf(DateTime d) => d.Month >= 7 ? $"{d.Year}-{d.Year + 1}" : $"{d.Year - 1}-{d.Year}";

    public async Task<List<SourceGame>> GetSeasonGames(string leagueCode, string season)
    {
        var leagueNames = new Dictionary<string, string>
        {
            { "FRA1", "Ligue 1" }, { "ENG1", "Premier League" }, { "ESP1", "LaLiga" },
            { "ITA1", "Serie A" }, { "GER1", "Bundesliga" }
        };
        var targetName = leagueNames.GetValueOrDefault(leagueCode, "");

        Console.WriteLine($"🔍 [FLASHSCORE] Buscando ID dinámico para {targetName}...");
        var todayLeagues = await Leagues(0, null);
        var match = todayLeagues.FirstOrDefault(x => x.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        if (match == null)
        {
            Console.WriteLine($"⚠️ [FLASHSCORE] No se encontró {targetName} jugando hoy. Usando fallback de Playwright directamente.");
            return await GetSeasonGamesFromHtml(leagueCode, season);
        }

        var ids = match.Key;
        Console.WriteLine($"✅ [FLASHSCORE] ID encontrado: {ids}. Solicitando feed de temporada...");

        try
        {
            var path = $"t_1_{ids}_5_es-co_1";
            var text = await Raw(path);
            var games = ParseGames(text);
            
            Console.WriteLine($"📡 [FLASHSCORE] El feed t_1_ devolvió {games.Count} partidos crudos.");
            
            if (games.Count == 0)
            {
                Console.WriteLine("⚠️ [FLASHSCORE] El feed t_1_ devolvió 0 partidos. Usando Playwright (HTML) como respaldo...");
                return await GetSeasonGamesFromHtml(leagueCode, season);
            }

            var res = new List<SourceGame>();
            foreach (var g in games)
            {
                if (!g.TryGetValue("AE", out var h) || !g.TryGetValue("AF", out var a)) continue;
                DateTime date = DateTime.Today;
                if (g.TryGetValue("AD", out var ts) && long.TryParse(ts, out var unix))
                    date = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
                bool finished = g.GetValueOrDefault("AB") == "3" || date.AddHours(3) < DateTime.Now;
                res.Add(new SourceGame(g["id"], season, date, h, a, finished));
            }
            return res;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [FLASHSCORE] Error en feed t_1_: {ex.Message}. Usando Playwright...");
            return await GetSeasonGamesFromHtml(leagueCode, season);
        }
    }

    public async Task<List<SourceGame>> GetSeasonGamesFromHtml(string leagueCode, string season)
    {
        if (!LeaguePath.TryGetValue(leagueCode, out var path))
            throw new NotSupportedException($"Liga {leagueCode} no mapeada.");

        var games = new List<SourceGame>();
        var uniqueIds = new HashSet<string>();

        Console.WriteLine($"🌐 [PLAYWRIGHT] Iniciando navegador para extraer historial de {leagueCode} ({season})...");

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        foreach (var section in new[] { "results", "fixtures" })
        {
            var url = $"https://www.flashscore.com{path}{section}/";
            Console.WriteLine($"🌐 [PLAYWRIGHT] Cargando {url}");
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            var cookies = page.Locator("#onetrust-accept-btn-handler");
            if (await cookies.CountAsync() > 0)
                await cookies.ClickAsync();

            try { await page.WaitForSelectorAsync(".event__match", new() { Timeout = 15000 }); }
            catch { continue; }

            var more = page.Locator("a.event__more, [data-testid='wcl-buttonLink']:has-text('Show more')");
            int clicks = 0;
            while (await more.CountAsync() > 0 && clicks < 5)
            {
                var before = await page.Locator(".event__match").CountAsync();
                await more.First.ClickAsync();
                await page.WaitForFunctionAsync(
                    "n => document.querySelectorAll('.event__match').length > n", before,
                    new() { Timeout = 5000 }).ContinueWith(_ => { });
                if (await page.Locator(".event__match").CountAsync() == before) break;
                clicks++;
            }

            // Extraemos todo el texto de la fila para no perder la fecha
            var rows = await page.EvaluateAsync<RowDto[]>(@"() => {
                const rows = [];
                document.querySelectorAll('.event__match').forEach(el => {
                    let timeText = '';
                    const timeEl = el.querySelector('.event__time');
                    if (timeEl) timeText = timeEl.textContent.trim();

                    rows.push({
                        id: (el.id || '').split('_').pop(),
                        time: timeText,
                        home: el.querySelector('.event__homeParticipant, .event__participant--home')?.textContent?.trim(),
                        away: el.querySelector('.event__awayParticipant, .event__participant--away')?.textContent?.trim(),
                        fullRowText: el.innerText.replace(/\n/g, ' | ') // Todo el texto en una sola línea
                    });
                });
                return rows;
            }");

            foreach (var r in rows)
            {
                if (string.IsNullOrEmpty(r.Id) || !uniqueIds.Add(r.Id)) continue;

                Console.WriteLine($"🔍 [DEBUG] Texto completo de la fila: '{r.FullRowText}'");

                DateTime matchDate = ParseFecha(r.Time, r.FullRowText, season);

                games.Add(new SourceGame(r.Id, season, matchDate, r.Home ?? "Por definir", r.Away ?? "Por definir", section == "results"));
            }
        }

        Console.WriteLine($"✅ [PLAYWRIGHT] Extraídos {games.Count} partidos únicos para la temporada {season}.");
        return games;
    }

    private class RowDto
    {
        public string Id { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Home { get; set; } = string.Empty;
        public string Away { get; set; } = string.Empty;
        public string FullRowText { get; set; } = string.Empty;
    }

    private static DateTime ParseFecha(string? t, string? fullRowText, string season)
    {
        // 1. Extraer el año de inicio de la temporada seleccionada
        int startYear = DateTime.Today.Year;
        if (!string.IsNullOrEmpty(season) && season.Contains("-"))
        {
            var parts = season.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var sy)) startYear = sy;
        }

        // 2. Buscar una fecha en el texto completo de la fila usando Expresión Regular (ej: 30.05.2027 o 21.11.)
        if (!string.IsNullOrWhiteSpace(fullRowText))
        {
            // Busca "dd.mm.yyyy" o "dd.mm."
            var match = Regex.Match(fullRowText, @"(\d{2}\.\d{2}\.\d{4}|\d{2}\.\d{2}\.)");
            if (match.Success)
            {
                var dateStr = match.Value;
                
                // Si ya trae el año completo (ej: 30.05.2027)
                if (DateTime.TryParseExact(dateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dYear))
                    return dYear;

                // Si solo trae dd.MM. (ej: 21.11.)
                if (DateTime.TryParseExact(dateStr, "dd.MM.", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dNoYear))
                {
                    var year = dNoYear.Month >= 7 ? startYear : startYear + 1;
                    return new DateTime(year, dNoYear.Month, dNoYear.Day);
                }
            }
        }

        // 3. Si la expresión regular falló, intentar parsear el campo .event__time (por si tiene la hora)
        if (!string.IsNullOrWhiteSpace(t))
        {
            string cleanTime = t.Replace('\u00A0', ' ').Trim();
            
            if (TimeSpan.TryParse(cleanTime, out var onlyTime))
            {
                // Si es solo la hora (ej: "14:00"), significa que es hoy
                return DateTime.Today.Date.Add(onlyTime);
            }
        }

        // 4. Si no se encontró nada, se usa la fecha de hoy como último recurso
        return DateTime.Today;
    }

    const string Tz = "3";

    static readonly Dictionary<string, string> LeaguePath = new()
    {
        ["FRA1"] = "/football/france/ligue-1/", ["ENG1"] = "/football/england/premier-league/",
        ["ESP1"] = "/football/spain/laliga/", ["ITA1"] = "/football/italy/serie-a/", ["GER1"] = "/football/germany/bundesliga/",
    };

    static readonly Dictionary<string, string> LeagueNames = new()
    {
        ["FRA1"] = "Ligue 1", ["ENG1"] = "Premier League", ["ESP1"] = "LaLiga", ["ITA1"] = "Serie A", ["GER1"] = "Bundesliga",
    };

    async Task<List<Dictionary<string, string>>> Day(int offset)
    {
        if (_days.TryGetValue(offset, out var g)) return g;
        try { await Task.Delay(300); g = ParseGames(await Raw($"f_1_{offset}_{Tz}_en_1")); }
        catch (HttpRequestException) { g = new(); }
        return _days[offset] = g;
    }

    public async Task<IEnumerable<LeagueInfo>> Leagues(int offset, string? q) =>
        (await Day(offset))
            .Select(g => new LeagueInfo(
                g.GetValueOrDefault("ZA") ?? "",
                g.GetValueOrDefault("ZL") ?? "",
                $"{g.GetValueOrDefault("ZB")}_{g.GetValueOrDefault("ZC")}"
            ))
            .Distinct()
            .Where(x => q is null || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

    public async Task<List<SourceGame>> GetGamesOn(string leagueCode, DateOnly day)
    {
        int offset = day.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        if (!LeaguePath.TryGetValue(leagueCode, out var path)) return new();
        
        var name = LeagueNames.GetValueOrDefault(leagueCode, "");
        var res = new List<SourceGame>();
        
        foreach (var g in await Day(offset))
        {
            var zl = g.GetValueOrDefault("ZL") ?? "";
            var za = g.GetValueOrDefault("ZA") ?? "";
            
            bool isExactMatch = za.Equals(name, StringComparison.OrdinalIgnoreCase);
            bool isSafeStartWith = za.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                                   !za.Contains("U21", StringComparison.OrdinalIgnoreCase) &&
                                   !za.Contains("U19", StringComparison.OrdinalIgnoreCase) &&
                                   !za.Contains("U20", StringComparison.OrdinalIgnoreCase) &&
                                   !za.Contains("Women", StringComparison.OrdinalIgnoreCase) &&
                                   !za.Contains("Femenina", StringComparison.OrdinalIgnoreCase);

            if (zl != path && !isExactMatch && !isSafeStartWith) continue;
            
            if (!g.TryGetValue("AE", out var h) || !g.TryGetValue("AF", out var a)) continue;
            if (!g.TryGetValue("AD", out var ts) || !long.TryParse(ts, out var unix)) continue;
            
            var date = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            bool finished = g.GetValueOrDefault("AB") == "3" || date.AddHours(3) < DateTime.Now;
            
            res.Add(new SourceGame(g["id"], SeasonOf(date), date, h, a, finished));
        }
        return res;
    }

    public async Task<SourceCorners?> GetCorners(string externalId)
    {
        await Task.Delay(600);
        
        string text;
        try { text = await Raw($"df_st_1_{externalId}"); }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [CORNERS] Error de red al consultar {externalId}: {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(text)) return null;

        (int H, int A)? Extract(string period)
        {
            var match = Regex.Match(text, $@"SE÷{period}(.*?)(?:SE÷|$)", RegexOptions.Singleline);
            if (!match.Success) return null;
            var block = match.Groups[1].Value;
            var cornerRow = Regex.Match(block, @"SG÷Corner kicks.*?SH÷(\d+).*?SI÷(\d+)", RegexOptions.Singleline);
            if (!cornerRow.Success) return null;
            return (int.Parse(cornerRow.Groups[1].Value), int.Parse(cornerRow.Groups[2].Value));
        }

        var match = Extract("Match");
        var first = Extract("1st Half");
        var second = Extract("2nd Half");

        if (match is null || first is null || second is null) 
        {
            Console.WriteLine($"⚠️ [CORNERS] No se encontró la estructura Regex para {externalId}.");
            return null;
        }

        var (tH, tA) = match.Value;
        var (fH, fA) = first.Value;
        var (sH, sA) = second.Value;

        if (fH + sH != tH || fA + sA != tA) return null;

        return new SourceCorners(tH, tA, fH, fA, sH, sA);
    }
}