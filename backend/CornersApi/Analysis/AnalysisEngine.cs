namespace CornersApi.Analysis;

public record Sample(int For, int Against, int For1T, int Against1T, int For2T, int Against2T);
public record OddsInput(int Id, string Subject, string Period, string Direction, double Line, double Price);
public record OddsResult(
    int OddsId, string Subject, string Period, string Direction, double Line, double Price,
    int Hits, int N, double HistProb, double Implied, double Edge, double Ev,
    int? HitsB, int? NB, double? EvB, string Confidence, bool HasValue);

public record Market(string Subject, string Period, string Direction, double Line, int Hits, int N, double ProbA,
    int? HitsB, int? NB, double? ProbB, double Prob, double MinOdds, string Confidence);

/// Función pura: sin acceso a BD. Reproduce el análisis manual (Método A y B).
public static class AnalysisEngine
{
    static int Own(Sample s, string p) => p switch { "1T" => s.For1T, "2T" => s.For2T, _ => s.For };
    static int Total(Sample s, string p) => p switch
    {
        "1T" => s.For1T + s.Against1T, "2T" => s.For2T + s.Against2T, _ => s.For + s.Against
    };
    static bool Hit(double v, string dir, double line) => dir == "OVER" ? v > line : v < line;

    public static List<OddsResult> Analyze(IReadOnlyList<Sample> home, IReadOnlyList<Sample> away, IEnumerable<OddsInput> odds)
    {
        var list = new List<OddsResult>();
        foreach (var o in odds)
        {
            bool match = o.Subject == "MATCH";
            // Método A: corners a favor de cada equipo (cruce local x visita para el partido)
            List<int> a = o.Subject switch
            {
                "HOME" => home.Select(s => Own(s, o.Period)).ToList(),
                "AWAY" => away.Select(s => Own(s, o.Period)).ToList(),
                _ => (from h in home from v in away select Own(h, o.Period) + Own(v, o.Period)).ToList()
            };
            // Método B: totales reales (a favor + en contra) de los partidos jugados
            List<int>? b = match ? home.Concat(away).Select(s => Total(s, o.Period)).ToList() : null;

            double implied = 1 / o.Price;
            if (a.Count == 0)
            {
                list.Add(new(o.Id, o.Subject, o.Period, o.Direction, o.Line, o.Price, 0, 0, 0, implied, 0, 0, null, null, null, "Sin datos", false));
                continue;
            }
            int hits = a.Count(v => Hit(v, o.Direction, o.Line));
            double pA = (double)hits / a.Count;
            double ev = pA * o.Price - 1;

            int? hitsB = null; double? evB = null; double? pB = null;
            if (b is { Count: > 0 })
            {
                hitsB = b.Count(v => Hit(v, o.Direction, o.Line));
                pB = (double)hitsB / b.Count;
                evB = pB * o.Price - 1;
            }

            int nConf = match ? home.Count + away.Count : a.Count;
            int level = nConf < 5 ? 0 : nConf < 10 ? 1 : 2;
            if (pB is not null && Math.Abs(pA - pB.Value) > 0.15) level = Math.Max(0, level - 1);
            string conf = level switch { 0 => "Bajo", 1 => "Medio", _ => "Alto" };
            bool value = ev > 0 && (evB is null || evB > 0);

            list.Add(new(o.Id, o.Subject, o.Period, o.Direction, o.Line, o.Price,
                hits, a.Count, pA, implied, pA - implied, ev, hitsB, b?.Count, evB, conf, value));
        }
        return list;
    }

    static double Avg(IReadOnlyList<Sample> s, string p) => s.Count == 0 ? double.NaN : s.Average(x => Own(x, p));

    /// Sin cuotas: evalúa 5 líneas alrededor del promedio para cada sujeto/periodo/tipo.
    /// Prob = la menor entre método A y B (conservadora). MinOdds = cuota mínima para tener valor (1/Prob).
    public static List<Market> Scan(IReadOnlyList<Sample> home, IReadOnlyList<Sample> away)
    {
        var inputs = new List<OddsInput>(); int id = 0;
        foreach (var subj in new[] { "MATCH", "HOME", "AWAY" })
            foreach (var per in new[] { "FT", "1T", "2T" })
            {
                double mean = subj switch { "HOME" => Avg(home, per), "AWAY" => Avg(away, per), _ => Avg(home, per) + Avg(away, per) };
                if (double.IsNaN(mean)) continue;
                double c = Math.Floor(mean) + 0.5;
                foreach (var k in new[] { -2, -1, 0, 1, 2 })
                {
                    if (c + k < 0.5) continue;
                    foreach (var d in new[] { "OVER", "UNDER" }) inputs.Add(new(++id, subj, per, d, c + k, 2.0));
                }
            }
        return Analyze(home, away, inputs).Where(r => r.N > 0).Select(r =>
        {
            double? pB = r.HitsB is null ? null : (double)r.HitsB / r.NB!.Value;
            double p = Math.Min(r.HistProb, pB ?? r.HistProb);
            return new Market(r.Subject, r.Period, r.Direction, r.Line, r.Hits, r.N, r.HistProb, r.HitsB, r.NB, pB, p, p > 0 ? 1 / p : 0, r.Confidence);
        }).ToList();
    }
}
