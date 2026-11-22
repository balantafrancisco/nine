using CornersApi;
using CornersApi.Analysis;
using Microsoft.EntityFrameworkCore;
using CornersApi.ApiFootball;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("Default")!;
// ELIMINA O COMENTA ESTAS LÍNEAS:
// var useSqlServer = builder.Configuration["Database:Provider"] == "SqlServer";
// builder.Services.AddDbContext<AppDb>(o => { if (useSqlServer) o.UseSqlServer(cs); else o.UseSqlite(cs); });

// AGREGA ESTA LÍNEA PARA USAR POSTGRESQL:
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<Store>();
builder.Services.AddSingleton<SyncBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SyncBackgroundService>());

// ---- API-Football client (necesario para el método GetSeasonGamesFromHtml) ----
builder.Services.AddHttpClient<ApiFootballClient>(client =>
{
    var cfg = builder.Configuration.GetSection("ApiFootball");
    client.BaseAddress = new Uri(cfg["BaseUrl"]!.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("x-apisports-key", cfg["ApiKey"]);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ---- Flashscore (depende de HttpClient y de ApiFootballClient) ----
builder.Services.AddHttpClient<FlashscoreSource>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip
                          | System.Net.DecompressionMethods.Deflate
                          | System.Net.DecompressionMethods.Brotli
});

// IMatchSource se resuelve a FlashscoreSource (ya tiene sus dependencias)
builder.Services.AddScoped<IMatchSource>(sp => sp.GetRequiredService<FlashscoreSource>());

// 1. Agregar CORS
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// 2. Usar CORS
app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    Seed.Run(db);
}

var api = app.MapGroup("/api");

api.MapGet("/teams", (AppDb db) => db.Teams.OrderBy(t => t.Name).ToListAsync());

api.MapGet("/teams/league/{leagueCode}", (AppDb db, string leagueCode) =>
    db.Teams.Where(t => t.LeagueId == db.Leagues.First(l => l.Code == leagueCode).Id)
      .OrderBy(t => t.Name).ToListAsync());

api.MapPost("/teams", async (AppDb db, Team t) =>
{
    t.Id = 0; db.Teams.Add(t); await db.SaveChangesAsync(); return Results.Ok(t);
});

api.MapGet("/teams/{id:int}/history", (AppDb db, int id) => db.History.Where(h => h.TeamId == id).OrderBy(h => h.Id).ToListAsync());
api.MapPost("/teams/{id:int}/history", async (AppDb db, int id, List<MatchHistory> rows) =>
{
    foreach (var r in rows) { r.Id = 0; r.TeamId = id; db.History.Add(r); }
    await db.SaveChangesAsync(); return Results.Ok(rows.Count);
});
api.MapDelete("/history/{id:int}", async (AppDb db, int id) =>
{
    await db.History.Where(h => h.Id == id).ExecuteDeleteAsync(); return Results.NoContent();
});

api.MapGet("/fixtures", (AppDb db) => db.Fixtures.Select(f => new
{
    f.Id, f.HomeTeamId, f.AwayTeamId,
    Home = db.Teams.First(t => t.Id == f.HomeTeamId).Name,
    Away = db.Teams.First(t => t.Id == f.AwayTeamId).Name
}).ToListAsync());
api.MapPost("/fixtures", async (AppDb db, Fixture f) =>
{
    f.Id = 0; db.Fixtures.Add(f); await db.SaveChangesAsync(); return Results.Ok(f);
});

api.MapGet("/fixtures/{id:int}/odds", (AppDb db, int id) => db.Odds.Where(o => o.FixtureId == id).OrderBy(o => o.Id).ToListAsync());
api.MapPost("/fixtures/{id:int}/odds", async (AppDb db, int id, List<Odd> rows) =>
{
    foreach (var r in rows) { r.Id = 0; r.FixtureId = id; db.Odds.Add(r); }
    await db.SaveChangesAsync(); return Results.Ok(rows.Count);
});
api.MapDelete("/odds/{id:int}", async (AppDb db, int id) =>
{
    await db.Odds.Where(o => o.Id == id).ExecuteDeleteAsync(); return Results.NoContent();
});

api.MapGet("/fixtures/{id:int}/analysis", async (AppDb db, int id) =>
{
    var f = await db.Fixtures.FindAsync(id);
    if (f is null) return Results.NotFound();
    async Task<List<Sample>> Load(int team, string venue) =>
        (await db.History.Where(h => h.TeamId == team && h.Venue == venue).ToListAsync())
        .Select(h => new Sample(h.CornersFor, h.CornersAgainst, h.For1T, h.Against1T, h.For2T, h.Against2T)).ToList();
    var home = await Load(f.HomeTeamId, "L");
    var away = await Load(f.AwayTeamId, "V");
    var odds = (await db.Odds.Where(o => o.FixtureId == id).ToListAsync())
        .Select(o => new OddsInput(o.Id, o.Subject, o.Period, o.Direction, o.Line, o.Price));
    return Results.Ok(new { homeGames = home.Count, awayGames = away.Count, results = AnalysisEngine.Analyze(home, away, odds) });
});

async Task<IResult> Guard<T>(Func<Task<T>> f)
{
    try { return Results.Ok(await f()); }
    catch (NotSupportedException e) { return Results.Problem(e.Message, statusCode: 501); }
}

api.MapGet("/leagues", (AppDb db) => db.Leagues.OrderBy(l => l.Name).ToListAsync());

// Puente manual/idempotente
api.MapPost("/import/games", async (Store s, ImportRequest r) =>
{
    var (a, u) = await s.Upsert(r.LeagueCode, r.Games); return Results.Ok(new { added = a, updated = u });
});

api.MapPost("/sync/fixtures", (SyncBackgroundService worker, string league, string season) =>
{
    var jobId = worker.EnqueueSeasonSync(league, season);
    return Results.Ok(new { jobId, message = "Sincronización de temporada iniciada en segundo plano" });
});

api.MapPost("/sync/corners", (SyncBackgroundService worker, string? league) =>
{
    var jobId = worker.EnqueueJob(league);
    return Results.Ok(new { jobId, message = "Sincronización iniciada en segundo plano" });
});

api.MapGet("/sync/corners/status/{jobId}", (string jobId) =>
{
    if (SyncBackgroundService.Jobs.TryGetValue(jobId, out var job))
    {
        return Results.Ok(job);
    }
    return Results.NotFound();
});

api.MapPost("/sync/recent", (AppDb db, Store s, IMatchSource src, int? days) => Guard(async () =>
{
    int a = 0, u = 0;
    foreach (var l in await db.Leagues.ToListAsync())
        for (int i = 0; i <= (days ?? 2); i++)
        {
            var r = await s.Upsert(l.Code, await src.GetGamesOn(l.Code, DateOnly.FromDateTime(DateTime.Today.AddDays(-i))));
            a += r.Added; u += r.Updated;
        }
    return new { added = a, updated = u, cornersLoaded = await s.LoadPendingCorners(src, null) };
}));

api.MapGet("/analysis/day", async (AppDb db, Store s, DateTime? date, double? minProb) =>
{
    var d = (date ?? DateTime.Today).Date;
    var ids = await db.Games.Where(g => g.Date >= d && g.Date < d.AddDays(1)).OrderBy(g => g.Date).Select(g => g.Id).ToListAsync();
    var list = new List<object>();
    foreach (var id in ids)
    {
        var a = await s.AnalyzeGame(id); if (a is null) continue;
        var c = Store.Candidates(a.Markets, minProb ?? 0.7);
        if (c.Count > 0) list.Add(new { a.GameId, a.League, a.Date, a.Home, a.Away, a.Finished, a.HomeGames, a.AwayGames, Candidates = c });
    }
    return Results.Ok(new { total = ids.Count, matches = list });
});
api.MapGet("/games/{id:int}/analysis", async (Store s, int id) => await s.AnalyzeGame(id) is { } a ? Results.Ok(a) : Results.NotFound());

// Diagnóstico: texto crudo de un feed de Flashscore
api.MapGet("/flashscore/raw", async (FlashscoreSource fs, string path, int? max) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(path, "^[A-Za-z0-9_-]+$")) return Results.BadRequest("path inválido");
    try { var t = await fs.Raw(path); var n = max ?? 4000; return Results.Text(t.Length > n ? t[..n] : t, "text/plain; charset=utf-8"); }
    catch (Exception e) { return Results.Problem(e.Message, statusCode: 502); }
});

api.MapGet("/flashscore/leagues", async (FlashscoreSource fs, string? q, int? offset) =>
    Results.Ok(await fs.Leagues(offset ?? 0, q)));

api.MapGet("/games", async (AppDb db, string? league, string? season, string? team, int? limit) =>
{
    var q = db.Games.AsQueryable();

    if (!string.IsNullOrEmpty(league))
    {
        var leagueId = await db.Leagues.Where(l => l.Code == league).Select(l => l.Id).FirstOrDefaultAsync();
        if (leagueId == 0) return Results.Ok(new List<object>());
        q = q.Where(g => g.LeagueId == leagueId);
    }

    if (!string.IsNullOrEmpty(season))
        q = q.Where(g => g.Season == season);

    if (!string.IsNullOrEmpty(team))
    {
        var teamId = await db.Teams.Where(t => t.Name.Contains(team)).Select(t => t.Id).FirstOrDefaultAsync();
        if (teamId != 0)
            q = q.Where(g => g.HomeTeamId == teamId || g.AwayTeamId == teamId);
    }

    var games = await q
        .OrderByDescending(g => g.Date)
        .Take(limit ?? 250)
        .Select(g => new
        {
            g.Id, g.ExternalId, g.Date, g.Season, g.Finished, g.CornersLoaded,
            g.HomeCorners, g.AwayCorners,
            League = db.Leagues.First(l => l.Id == g.LeagueId).Name,
            Home = db.Teams.First(t => t.Id == g.HomeTeamId).Name,
            Away = db.Teams.First(t => t.Id == g.AwayTeamId).Name
        })
        .ToListAsync();

    return Results.Ok(games);
});

api.MapGet("/debug/stats", async (AppDb db) =>
{
    var total = await db.Games.CountAsync();
    var byLeague = await db.Games
        .GroupBy(g => g.LeagueId)
        .Select(g => new { LeagueId = g.Key, Count = g.Count() })
        .ToListAsync();

    var cornersLoaded = await db.Games.CountAsync(g => g.CornersLoaded);

    return Results.Ok(new { TotalPartidos = total, CornersCargados = cornersLoaded, PorLiga = byLeague });
});

api.MapGet("/debug/league-ids", async (FlashscoreSource fs) =>
{
    var html = await fs.Raw("f_1_0_3_en_1");
    var matches = System.Text.RegularExpressions.Regex.Matches(html, @"ZA÷(.*?)¬ZB÷(\d+)¬ZC÷(\d+)");
    var list = matches.Select(m => new
    {
        League = m.Groups[1].Value,
        Id = $"{m.Groups[2].Value}_{m.Groups[3].Value}"
    }).ToList();
    return Results.Ok(list);
});

api.MapGet("/games/filters", async (AppDb db, string league) =>
{
    var leagueId = await db.Leagues.Where(l => l.Code == league).Select(l => l.Id).FirstOrDefaultAsync();
    if (leagueId == 0) return Results.Ok(new { seasons = new List<string>(), teams = new List<string>() });

    var seasons = await db.Games.Where(g => g.LeagueId == leagueId)
        .Select(g => g.Season).Distinct().OrderByDescending(s => s).ToListAsync();

    var teams = await db.Teams.Where(t => t.LeagueId == leagueId)
        .Select(t => t.Name).OrderBy(t => t).ToListAsync();

    return Results.Ok(new { seasons, teams });
});

// Endpoint para el botón de Historial HTML
api.MapPost("/sync/season-html", (SyncBackgroundService worker, string league, string season) =>
{
    var jobId = worker.EnqueueHtmlSync(league, season);
    return Results.Ok(new { jobId, message = "Sincronización HTML iniciada" });
});

// Endpoint de historial normal (sin timeout)
api.MapPost("/sync/season-history", (AppDb db, Store s, IMatchSource src, string league, int daysBack) =>
{
    return Guard(async () =>
    {
        var l = await db.Leagues.FirstOrDefaultAsync(x => x.Code == league);
        if (l == null) throw new NotSupportedException("Liga no encontrada");

        int totalAdded = 0, totalUpdated = 0, totalLoaded = 0;

        Console.WriteLine($"⏳ [HISTORY] Iniciando descarga de {daysBack} días hacia atrás para {league}...");

        for (int i = 1; i <= daysBack; i++)
        {
            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(-i));
            var games = await src.GetGamesOn(league, date);

            if (games.Any())
            {
                var res = await s.ForceUpsert(league, games);
                totalAdded += res.Added;
                totalUpdated += res.Updated;
                Console.WriteLine($"📅 [HISTORY] {date}: {games.Count} partidos encontrados. Añadidos: {res.Added}");
            }
        }

        for (int i = 0; i <= 15; i++)
        {
            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(i));
            var games = await src.GetGamesOn(league, date);

            if (games.Any())
            {
                var res = await s.ForceUpsert(league, games);
                totalAdded += res.Added;
                totalUpdated += res.Updated;
                Console.WriteLine($"📅 [HISTORY] {date} (Futuro): {games.Count} partidos encontrados. Añadidos: {res.Added}");
            }
        }

        Console.WriteLine($"⏳ [HISTORY] Descarga finalizada. Buscando corners pendientes...");
        totalLoaded = await s.LoadPendingCorners(src, league);
        Console.WriteLine($"✅ [HISTORY] Completado. {totalLoaded} corners cargados.");

        return new { added = totalAdded, updated = totalUpdated, cornersLoaded = totalLoaded };
    });
});


// Endpoint para agregar un partido manualmente a la base de datos
api.MapPost("/games", async (AppDb db, GameRequest req) =>
{
    var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == req.LeagueCode);
    if (league == null) return Results.BadRequest($"La liga {req.LeagueCode} no existe.");

    var homeTeam = await db.Teams.FindAsync(req.HomeTeamId);
    var awayTeam = await db.Teams.FindAsync(req.AwayTeamId);
    if (homeTeam == null || awayTeam == null) return Results.BadRequest("Uno de los equipos no existe.");

    // Verificar si ya tiene corners cargados para marcar CornersLoaded
    bool hasCorners = req.HomeCorners.HasValue || req.AwayCorners.HasValue;

    var game = new Game
    {
        LeagueId = league.Id,
        HomeTeamId = req.HomeTeamId,
        AwayTeamId = req.AwayTeamId,
        Date = req.Date,
        Season = req.Season,
        
        ExternalId = !string.IsNullOrWhiteSpace(req.ExternalId) 
            ? req.ExternalId.Trim() 
            : Guid.NewGuid().ToString().Substring(0, 8),
            
        Finished = req.Finished ?? false,
        
        // Datos de corners (si vienen)
        CornersLoaded = hasCorners,
        HomeCorners = req.HomeCorners ?? 0,
        AwayCorners = req.AwayCorners ?? 0,
        Home1T = req.Home1T ?? 0,
        Away1T = req.Away1T ?? 0,
        Home2T = req.Home2T ?? 0,
        Away2T = req.Away2T ?? 0
    };

    db.Games.Add(game);
    await db.SaveChangesAsync();

    return Results.Ok(game);
});

// Endpoint para guardar corners manualmente
api.MapPost("/games/{id:int}/manual-corners", async (AppDb db, int id, int homeCorners, int awayCorners) =>
{
    var game = await db.Games.FindAsync(id);
    if (game == null) return Results.NotFound();

    game.HomeCorners = homeCorners;
    game.AwayCorners = awayCorners;
    game.CornersLoaded = true;
    game.Finished = true; // Lo marcamos como finalizado porque ya tiene resultado

    await db.SaveChangesAsync();
    return Results.Ok(game);
});



// NUEVO ENDPOINT: Historial de recomendaciones vs resultados reales
api.MapGet("/recommendations/history", async (AppDb db, Store s, string? league, string? season, string? team, string? day, string? date, int? limit) =>
{
    var q = db.Games.AsQueryable(); 

    if (!string.IsNullOrEmpty(league))
        q = q.Where(g => db.Leagues.Any(l => l.Id == g.LeagueId && l.Code == league));
    
    if (!string.IsNullOrEmpty(season))
        q = q.Where(g => g.Season == season);
    
    if (!string.IsNullOrEmpty(team))
    {
        var teamId = await db.Teams.Where(t => t.Name.Contains(team)).Select(t => t.Id).FirstOrDefaultAsync();
        if (teamId != 0)
            q = q.Where(g => g.HomeTeamId == teamId || g.AwayTeamId == teamId);
    }

    var today = DateTime.Today;

    // Si el usuario selecciona una fecha específica en el calendario
    if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var specificDate))
    {
        q = q.Where(g => g.Date >= specificDate.Date && g.Date < specificDate.Date.AddDays(1));
    }
    // Si usa los botones rápidos
    else if (day == "today") q = q.Where(g => g.Date >= today && g.Date < today.AddDays(1));
    else if (day == "yesterday") q = q.Where(g => g.Date >= today.AddDays(-1) && g.Date < today);
    else if (day == "tomorrow") q = q.Where(g => g.Date >= today.AddDays(1) && g.Date < today.AddDays(2));
    else q = q.Where(g => g.Date >= today.AddDays(-14) && g.Date <= today.AddDays(7)); // "Todos"

    var games = await q.OrderByDescending(g => g.Date).Take(limit ?? 150).ToListAsync();
    var results = new List<object>();

    foreach (var g in games)
    {
        var analysis = await s.AnalyzeGame(g.Id);
        if (analysis == null) continue;

        var candidates = Store.Candidates(analysis.Markets, 0.7);

        foreach (var m in candidates)
        {
            int? actual = null;
            bool? isHit = null;

            if (g.Finished && g.CornersLoaded)
            {
                actual = m.Subject switch
                {
                    "MATCH" => m.Period switch { "1T" => g.Home1T + g.Away1T, "2T" => g.Home2T + g.Away2T, _ => g.HomeCorners + g.AwayCorners },
                    "HOME" => m.Period switch { "1T" => g.Home1T, "2T" => g.Home2T, _ => g.HomeCorners },
                    "AWAY" => m.Period switch { "1T" => g.Away1T, "2T" => g.Away2T, _ => g.AwayCorners },
                    _ => 0
                };
                isHit = m.Direction == "OVER" ? actual > m.Line : actual < m.Line;
            }

            string subjectStr = m.Subject switch { "MATCH" => "Total", "HOME" => analysis.Home, "AWAY" => analysis.Away, _ => m.Subject };
            string periodStr = m.Period == "FT" ? "Partido completo" : $"{m.Period} tiempo";
            string dirStr = m.Direction == "OVER" ? "Más de" : "Menos de";
            string recText = $"{subjectStr} · {periodStr} · {dirStr} {m.Line}";

            results.Add(new
            {
                g.Id,
                g.Date,
                analysis.League,
                analysis.Home,
                analysis.Away,
                Recommendation = recText,
                Prob = m.Prob,
                ActualCorners = actual,
                IsHit = isHit,
                IsFinished = g.Finished && g.CornersLoaded
            });
        }
    }

    return Results.Ok(results);
});

api.MapPost("/games/{id:int}/sync-corners", async (AppDb db, IMatchSource src, int id) =>
{
    var game = await db.Games.FindAsync(id);
    if (game == null) return Results.NotFound("Partido no encontrado");

    try
    {
        var corners = await src.GetCorners(game.ExternalId);
        if (corners != null)
        {
            game.HomeCorners = corners.Home;
            game.AwayCorners = corners.Away;
            game.Home1T = corners.Home1T;
            game.Away1T = corners.Away1T;
            game.Home2T = corners.Home2T;
            game.Away2T = corners.Away2T;
            game.CornersLoaded = true;
            game.Finished = true;
            await db.SaveChangesAsync();
            return Results.Ok(new { loaded = 1, message = "Corners cargados exitosamente." });
        }
        return Results.Ok(new { loaded = 0, message = "Flashscore no devolvió datos de corners para este partido." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error de red o scraping: {ex.Message}");
    }
});

app.Run();

// Al final del archivo, reemplaza el GameRequest por este:
public record GameRequest(
    int HomeTeamId,
    int AwayTeamId,
    DateTime Date,
    string LeagueCode,
    string Season,
    string? ExternalId,
    int? HomeCorners,
    int? AwayCorners,
    int? Home1T,
    int? Away1T,
    int? Home2T,
    int? Away2T,
    bool? Finished
);