namespace CornersApi;

// Datos de ejemplo: Toulouse vs Estrasburgo (los de tu análisis). Formato fila: a favor, en contra, 1T fav, 1T contra, 2T fav, 2T contra
public static class Seed
{
    static readonly Dictionary<string, string> Subj = new() { ["M"] = "MATCH", ["H"] = "HOME", ["A"] = "AWAY" };
    public static Odd ParseOdd(string line)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Odd
        {
            Subject = Subj[t[0]], Period = t[1], Direction = t[2] == "O" ? "OVER" : "UNDER",
            Line = double.Parse(t[3], System.Globalization.CultureInfo.InvariantCulture),
            Price = double.Parse(t[4], System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static void Run(AppDb db)
    {
        if (!db.Leagues.Any())
        {
            db.Leagues.AddRange(new League { Code = "FRA1", Name = "Ligue 1" }, new League { Code = "ITA1", Name = "Serie A" },
                new League { Code = "GER1", Name = "Bundesliga" }, new League { Code = "ENG1", Name = "Premier League" }, new League { Code = "ESP1", Name = "LaLiga" });
            db.SaveChanges();
        }
        if (db.Teams.Any()) return;
        var fra = db.Leagues.First(l => l.Code == "FRA1");
        var tou = new Team { Name = "Toulouse", LeagueId = fra.Id }; var str = new Team { Name = "Estrasburgo", LeagueId = fra.Id };
        db.Teams.AddRange(tou, str); db.SaveChanges();

        void H(Team t, string venue, string rows)
        {
            foreach (var l in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var n = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                db.History.Add(new MatchHistory { TeamId = t.Id, Venue = venue, CornersFor = n[0], CornersAgainst = n[1],
                    For1T = n[2], Against1T = n[3], For2T = n[4], Against2T = n[5] });
            }
        }
        H(tou, "L", "4 2 2 1 2 1\n6 7 1 5 5 2\n6 2 5 1 1 1\n6 5 3 3 3 2");
        H(tou, "V", "3 4 0 4 3 0\n2 2 1 0 1 2\n4 4 2 1 2 3\n3 4 2 1 1 3\n2 7 0 3 2 4");
        H(str, "L", "5 0 2 0 3 0\n3 6 2 3 1 3\n2 7 2 2 0 5\n3 6 1 1 2 5");
        H(str, "V", "4 2 2 0 2 2\n3 2 1 1 2 1\n0 3 0 2 0 1\n0 11 0 7 0 4\n4 3 4 3 0 0");

        var f = new Fixture { HomeTeamId = tou.Id, AwayTeamId = str.Id };
        db.Fixtures.Add(f); db.SaveChanges();

        const string odds = @"M FT O 8.5 1.74
M FT U 8.5 1.96
M 1T O 4.5 2.23
M 1T U 3.5 2.08
M 1T U 4.5 1.50
M 2T O 4.5 1.71
M 2T U 4.5 1.89
M 2T U 5.5 1.45
H FT O 3.5 1.23
H FT O 4.5 1.70
H FT U 4.5 2.02
H 1T O 2.5 2.12
H 1T U 1.5 2.40
H 1T U 2.5 1.55
H 2T O 2.5 1.81
H 2T U 2.5 1.78
A FT O 3.5 1.83
A FT U 3.5 1.85
A 1T O 1.5 1.82
A 1T U 1.5 1.77
A 1T U 2.5 1.26
A 2T O 1.5 1.60
A 2T O 2.5 2.50
A 2T U 1.5 2.05
A 2T U 2.5 1.40";
        foreach (var l in odds.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        { var o = ParseOdd(l); o.FixtureId = f.Id; db.Odds.Add(o); }
        db.SaveChanges();
        // Partido DEMO de hoy para ver la pantalla de análisis (usa el historial manual de arriba)
        db.Games.Add(new Game { LeagueId = fra.Id, Season = "demo", ExternalId = "demo-tou-str", Date = DateTime.UtcNow.AddHours(20), HomeTeamId = tou.Id, AwayTeamId = str.Id });
        db.SaveChanges();
    }
}
