using CornersApi.Analysis;
using Microsoft.EntityFrameworkCore;
namespace CornersApi;

public record SourceCorners(int Home, int Away, int Home1T, int Away1T, int Home2T, int Away2T);
public record SourceGame(string ExternalId, string Season, DateTime Date, string Home, string Away, bool Finished, SourceCorners? Corners = null);
public record ImportRequest(string LeagueCode, List<SourceGame> Games);
public record GameAnalysis(int GameId, string League, DateTime Date, string Home, string Away, bool Finished, int HomeGames, int AwayGames, List<Market> Markets);

public interface IMatchSource
{
    Task<List<SourceGame>> GetSeasonGames(string leagueCode, string season);
    Task<List<SourceGame>> GetGamesOn(string leagueCode, DateOnly day);
    Task<SourceCorners?> GetCorners(string externalId);
}

public class UnconfiguredSource : IMatchSource
{
    static NotSupportedException Ex() => new("No hay fuente de datos configurada: implementa IMatchSource o usa POST /api/import/games.");
    public Task<List<SourceGame>> GetSeasonGames(string l, string s) => throw Ex();
    public Task<List<SourceGame>> GetGamesOn(string l, DateOnly d) => throw Ex();
    public Task<SourceCorners?> GetCorners(string id) => throw Ex();
}

public class Store(AppDb db)
{
    async Task<Team> TeamOf(string name, int leagueId)
    {
        name = name.Trim();
        var t = await db.Teams.FirstOrDefaultAsync(x => x.Name == name);
        if (t is null) { t = new Team { Name = name, LeagueId = leagueId }; db.Teams.Add(t); await db.SaveChangesAsync(); }
        else if (t.LeagueId is null) { t.LeagueId = leagueId; await db.SaveChangesAsync(); }
        return t;
    }

    static void Fill(Game g, SourceCorners c)
    {
        g.HomeCorners = c.Home; g.AwayCorners = c.Away; g.Home1T = c.Home1T; g.Away1T = c.Away1T;
        g.Home2T = c.Home2T; g.Away2T = c.Away2T; g.CornersLoaded = true;
        g.Finished = true; // <-- Asegura que se marque como finalizado
    }

    public async Task<(int Added, int Updated)> Upsert(string leagueCode, IEnumerable<SourceGame> games)
    {
        var league = await db.Leagues.FirstAsync(l => l.Code == leagueCode);
        int add = 0, upd = 0;
        foreach (var s in games)
        {
            var h = await TeamOf(s.Home, league.Id); var a = await TeamOf(s.Away, league.Id);
            var g = await db.Games.FirstOrDefaultAsync(x => x.ExternalId == s.ExternalId);
            if (g is null)
            {
                g = new Game { LeagueId = league.Id, Season = s.Season, ExternalId = s.ExternalId, Date = s.Date, HomeTeamId = h.Id, AwayTeamId = a.Id, Finished = s.Finished };
                db.Games.Add(g); add++;
            }
            else if (g.CornersLoaded) continue;
            else { 
                g.Date = s.Date; 
                g.Finished = s.Finished; 
                g.Season = s.Season; // <-- AGREGA ESTA LÍNEA AQUÍ
                upd++; 
            }
            if (g.Finished && !g.CornersLoaded && s.Corners is not null) Fill(g, s.Corners);
            await db.SaveChangesAsync();
        }
        return (add, upd);
    }

    public async Task<(int Added, int Updated)> ForceUpsert(string leagueCode, IEnumerable<SourceGame> games)
    {
        var league = await db.Leagues.FirstAsync(l => l.Code == leagueCode);
        int add = 0, upd = 0;

        var teamCache = new Dictionary<string, Team>();

        async Task<Team> GetOrCreateTeam(string name)
        {
            if (teamCache.TryGetValue(name, out var cached)) return cached;
            
            var t = await db.Teams.FirstOrDefaultAsync(x => x.Name == name);
            if (t is null) 
            { 
                t = new Team { Name = name, LeagueId = league.Id }; 
                db.Teams.Add(t); 
            }
            else if (t.LeagueId is null) 
            { 
                t.LeagueId = league.Id; 
            }
            teamCache[name] = t;
            return t;
        }

        foreach (var s in games)
        {
            var h = await GetOrCreateTeam(s.Home);
            var a = await GetOrCreateTeam(s.Away);

            await db.SaveChangesAsync(); 

            var g = await db.Games.FirstOrDefaultAsync(x => x.ExternalId == s.ExternalId);
            if (g is null)
            {
                g = new Game { LeagueId = league.Id, Season = s.Season, ExternalId = s.ExternalId, Date = s.Date, HomeTeamId = h.Id, AwayTeamId = a.Id, Finished = s.Finished };
                db.Games.Add(g); add++;
            }
            else if (g.CornersLoaded) continue;
            else { 
                g.Date = s.Date; 
                g.Finished = s.Finished; 
                g.Season = s.Season; // <-- AGREGA ESTA LÍNEA AQUÍ
                upd++; 
            }
            
            if (g.Finished && !g.CornersLoaded && s.Corners is not null) Fill(g, s.Corners);
            await db.SaveChangesAsync();
        }
        return (add, upd);
    }

    public async Task<int> LoadPendingCorners(IMatchSource src, string? leagueCode)
    {
        var q = db.Games.Where(g => g.Finished && !g.CornersLoaded);
        if (leagueCode is not null) q = q.Where(g => db.Leagues.Any(l => l.Id == g.LeagueId && l.Code == leagueCode));
        int ok = 0;
        foreach (var g in await q.ToListAsync())
        {
            var c = await src.GetCorners(g.ExternalId);
            if (c is null) continue;
            Fill(g, c); ok++; await db.SaveChangesAsync();
        }
        return ok;
    }

    public async Task<List<Sample>> Samples(int teamId, string venue, DateTime before)
    {
        var manual = (await db.History.Where(h => h.TeamId == teamId && h.Venue == venue).ToListAsync())
            .Select(h => new Sample(h.CornersFor, h.CornersAgainst, h.For1T, h.Against1T, h.For2T, h.Against2T));
        var games = await db.Games.Where(x => x.CornersLoaded && x.Date < before && (venue == "L" ? x.HomeTeamId == teamId : x.AwayTeamId == teamId))
            .OrderByDescending(x => x.Date).Take(10).ToListAsync();
        return manual.Concat(games.Select(x => venue == "L"
            ? new Sample(x.HomeCorners, x.AwayCorners, x.Home1T, x.Away1T, x.Home2T, x.Away2T)
            : new Sample(x.AwayCorners, x.HomeCorners, x.Away1T, x.Home1T, x.Away2T, x.Home2T))).ToList();
    }

    public async Task<GameAnalysis?> AnalyzeGame(int id)
    {
        var g = await db.Games.FindAsync(id);
        if (g is null) return null;
        var home = await Samples(g.HomeTeamId, "L", g.Date); var away = await Samples(g.AwayTeamId, "V", g.Date);
        return new GameAnalysis(g.Id, (await db.Leagues.FindAsync(g.LeagueId))!.Name, g.Date,
            (await db.Teams.FindAsync(g.HomeTeamId))!.Name, (await db.Teams.FindAsync(g.AwayTeamId))!.Name,
            g.Finished, home.Count, away.Count, AnalysisEngine.Scan(home, away));
    }

    public static List<Market> Candidates(IEnumerable<Market> ms, double minProb) =>
        ms.Where(m => m.Prob >= minProb && m.Prob <= 0.9 && m.Confidence != "Bajo")
          .GroupBy(m => (m.Subject, m.Period, m.Direction)).Select(g => g.OrderBy(m => m.Prob).First())
          .OrderByDescending(m => m.Prob).Take(4).ToList();
}