using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace CornersApi;

public class SyncJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Loaded { get; set; }
    public bool IsCompleted { get; set; }
    public string? Error { get; set; }
}

public class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentQueue<(string JobId, string? Payload)> _queue = new();
    public static readonly ConcurrentDictionary<string, SyncJob> Jobs = new();

    public SyncBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string EnqueueJob(string? leagueCode)
    {
        var job = new SyncJob();
        Jobs[job.Id] = job;
        _queue.Enqueue((job.Id, leagueCode));
        return job.Id;
    }

    public string EnqueueSeasonSync(string leagueCode, string season)
    {
        var job = new SyncJob();
        Jobs[job.Id] = job;
        _queue.Enqueue((job.Id, $"SEASON:{leagueCode}:{season}"));
        return job.Id;
    }

    public string EnqueueHtmlSync(string leagueCode, string season)
    {
        var job = new SyncJob();
        Jobs[job.Id] = job;
        _queue.Enqueue((job.Id, $"HTML:{leagueCode}:{season}"));
        return job.Id;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var item))
            {
                var (jobId, payload) = item;
                var job = Jobs[jobId];
                
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    var src = scope.ServiceProvider.GetRequiredService<IMatchSource>();
                    var store = scope.ServiceProvider.GetRequiredService<Store>();
                    var fs = scope.ServiceProvider.GetRequiredService<FlashscoreSource>();

                    if (payload != null && payload.StartsWith("HTML:"))
                    {
                        Console.WriteLine("⏳ [HTML] Iniciando extracción de historial desde HTML...");
                        var parts = payload.Split(':');
                        var league = parts[1];
                        var season = parts.Length > 2 ? parts[2] : "2026-2027";
                        
                        job.Total = 1;
                        
                        try
                        {
                            var games = await fs.GetSeasonGamesFromHtml(league, season);
                            if (games.Any())
                            {
                                Console.WriteLine($"✅ [HTML] Insertando {games.Count} partidos...");
                                await store.ForceUpsert(league, games);
                                job.Loaded = games.Count;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ [HTML] Error en Upsert: {ex.Message}");
                        }
                        
                        job.Processed = 1;
                        Console.WriteLine("✅ [HTML] Finalizado.");
                    }
                    else if (payload != null && payload.StartsWith("SEASON:"))
                    {
                        Console.WriteLine("⏳ [SEASON] Iniciando sincronización de temporada (Feed completo)...");
                        var parts = payload.Split(':');
                        var league = parts[1];
                        var season = parts[2];
                        
                        job.Total = 1;
                        try
                        {
                            var games = await fs.GetSeasonGames(league, season);
                            if (games.Any())
                            {
                                Console.WriteLine($"✅ [SEASON] Guardando {games.Count} partidos en la BD...");
                                await store.ForceUpsert(league, games);
                                job.Loaded += games.Count;
                            }
                            else
                            {
                                Console.WriteLine("⚠️ [SEASON] El feed t_1_ devolvió 0 partidos. Es probable que el ID de la liga en Flashscore haya cambiado.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ [SEASON] Error: {ex.Message}");
                        }
                        job.Processed = 1;
                        Console.WriteLine("✅ [SEASON] Finalizado.");
                    }
                    else
                    {
                        Console.WriteLine("⏳ [CORNERS] Iniciando carga de corners pendientes...");
                        var leagueCode = payload; 
                        var query = db.Games.Where(g => !g.CornersLoaded);
                        if (leagueCode != null)
                            query = query.Where(g => db.Leagues.Any(l => l.Id == g.LeagueId && l.Code == leagueCode));

                        var gamesToProcess = await query.ToListAsync(stoppingToken);
                        job.Total = gamesToProcess.Count;
                        Console.WriteLine($"🔍 [CORNERS] Se encontraron {job.Total} partidos pendientes.");

                        foreach (var game in gamesToProcess)
                        {
                            if (stoppingToken.IsCancellationRequested) break;
                            var corners = await src.GetCorners(game.ExternalId);
                            if (corners != null)
                            {
                                game.HomeCorners = corners.Home; game.AwayCorners = corners.Away;
                                game.Home1T = corners.Home1T; game.Away1T = corners.Away1T;
                                game.Home2T = corners.Home2T; game.Away2T = corners.Away2T;
                                game.CornersLoaded = true;
                                game.Finished = true;
                                job.Loaded++;
                                await db.SaveChangesAsync(stoppingToken);
                                Console.WriteLine($"✅ [CORNERS] Guardado {game.ExternalId}: {game.HomeCorners}-{game.AwayCorners}");
                            }
                            job.Processed++;
                        }
                        Console.WriteLine($"✅ [CORNERS] Finalizado. {job.Loaded} cargados de {job.Total} intentados.");
                    }
                    job.IsCompleted = true;
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"❌ ERROR EN BACKGROUND: {ex.Message}");
                    job.Error = ex.Message; 
                    job.IsCompleted = true; 
                }
            }
            else { await Task.Delay(1000, stoppingToken); }
        }
    }
}