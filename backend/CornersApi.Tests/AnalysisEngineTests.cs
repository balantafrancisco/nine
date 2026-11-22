using CornersApi.Analysis;
using Xunit;

public class AnalysisEngineTests
{
    static Sample[] S(params int[][] r) => r.Select(n => new Sample(n[0], n[1], n[2], n[3], n[4], n[5])).ToArray();

    static readonly Sample[] TouLocal = S(new[] {4,2,2,1,2,1}, new[] {6,7,1,5,5,2}, new[] {6,2,5,1,1,1}, new[] {6,5,3,3,3,2});
    static readonly Sample[] StrVisita = S(new[] {4,2,2,0,2,2}, new[] {3,2,1,1,2,1}, new[] {0,3,0,2,0,1}, new[] {0,11,0,7,0,4}, new[] {4,3,4,3,0,0});

    static OddsResult Run(string subj, string period, string dir, double line, double price) =>
        AnalysisEngine.Analyze(TouLocal, StrVisita, new[] { new OddsInput(1, subj, period, dir, line, price) }).Single();

    [Fact] public void Partido2T_Menos55_ReproduceAnalisisManual()
    {
        var r = Run("MATCH", "2T", "UNDER", 5.5, 1.45);
        Assert.Equal((18, 20), (r.Hits, r.N));
        Assert.Equal((8, 9), (r.HitsB, r.NB));
        Assert.True(r.HasValue);
    }

    [Fact] public void Partido_Mas85_ReproduceAnalisisManual()
    {
        var r = Run("MATCH", "FT", "OVER", 8.5, 1.74);
        Assert.Equal((9, 20), (r.Hits, r.N));
        Assert.Equal((3, 9), (r.HitsB, r.NB));
        Assert.False(r.HasValue);
    }

    [Fact] public void Estrasburgo2T_Menos25_Cumple5de5()
    {
        var r = Run("AWAY", "2T", "UNDER", 2.5, 1.40);
        Assert.Equal((5, 5), (r.Hits, r.N));
        Assert.Equal(0.40, r.Ev, 2);
    }

    [Fact] public void Toulouse_Mas35_Cuota123()
    {
        var r = Run("HOME", "FT", "OVER", 3.5, 1.23);
        Assert.Equal((4, 4), (r.Hits, r.N));
        Assert.Equal(0.23, r.Ev, 2);
        Assert.Equal("Bajo", r.Confidence);
    }

    [Fact] public void Toulouse_Mas45_Cumple3de4()
    {
        var r = Run("HOME", "FT", "OVER", 4.5, 1.70);
        Assert.Equal((3, 4), (r.Hits, r.N));
        Assert.Equal(0.275, r.Ev, 3);
    }

    [Fact] public void SinDatos_NoRompe()
    {
        var r = AnalysisEngine.Analyze(Array.Empty<Sample>(), Array.Empty<Sample>(), new[] { new OddsInput(1, "HOME", "FT", "OVER", 3.5, 1.5) }).Single();
        Assert.Equal("Sin datos", r.Confidence);
    }

    [Fact] public void Scan_ProbabilidadConservadoraYCuotaMinima()
    {
        var m = AnalysisEngine.Scan(TouLocal, StrVisita).Single(x => x.Subject == "MATCH" && x.Period == "2T" && x.Direction == "UNDER" && x.Line == 5.5);
        Assert.Equal(8.0 / 9, m.Prob, 3);
        Assert.Equal(1 / m.Prob, m.MinOdds, 3);
    }
}
