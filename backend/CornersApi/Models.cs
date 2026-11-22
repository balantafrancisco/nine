using Microsoft.EntityFrameworkCore;
namespace CornersApi;

public class Team { public int Id { get; set; } public string Name { get; set; } = ""; public int? LeagueId { get; set; } }

public class League { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }

// Partido de liga. Finished y CornersLoaded evitan volver a consultar/actualizar lo ya registrado.
public class Game
{
    public int Id { get; set; }
    public int LeagueId { get; set; }
    public string Season { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public DateTime Date { get; set; }
    public int HomeTeamId { get; set; }
    public int AwayTeamId { get; set; }
    public bool Finished { get; set; }
    public bool CornersLoaded { get; set; }
    public int HomeCorners { get; set; }
    public int AwayCorners { get; set; }
    public int Home1T { get; set; }
    public int Away1T { get; set; }
    public int Home2T { get; set; }
    public int Away2T { get; set; }
}

// Un partido pasado de un equipo. Venue: "L" (local) o "V" (visita)
public class MatchHistory
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Venue { get; set; } = "L";
    public int CornersFor { get; set; }
    public int CornersAgainst { get; set; }
    public int For1T { get; set; }
    public int Against1T { get; set; }
    public int For2T { get; set; }
    public int Against2T { get; set; }
}

public class Fixture { public int Id { get; set; } public int HomeTeamId { get; set; } public int AwayTeamId { get; set; } }

// Subject: MATCH|HOME|AWAY  Period: FT|1T|2T  Direction: OVER|UNDER
public class Odd
{
    public int Id { get; set; }
    public int FixtureId { get; set; }
    public string Subject { get; set; } = "MATCH";
    public string Period { get; set; } = "FT";
    public string Direction { get; set; } = "OVER";
    public double Line { get; set; }
    public double Price { get; set; }
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<MatchHistory> History => Set<MatchHistory>();
    public DbSet<Fixture> Fixtures => Set<Fixture>();
    public DbSet<Odd> Odds => Set<Odd>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Game> Games => Set<Game>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>().HasIndex(t => t.Name).IsUnique();
        b.Entity<Game>().HasIndex(g => g.ExternalId).IsUnique();
    }
}
