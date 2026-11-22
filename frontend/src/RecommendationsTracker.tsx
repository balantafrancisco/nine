import React, { useState, useEffect } from 'react';

// Ajusta la URL base según tu configuración
const API_BASE = 'http://localhost:5080/api';

interface Recommendation {
  id: number;
  date: string;
  league: string;
  home: string;
  away: string;
  recommendation: string;
  prob: number;
  actualCorners: number;
  isHit: boolean;
}

export default function RecommendationsTracker() {
  const [data, setData] = useState<Recommendation[]>([]);
  const [loading, setLoading] = useState(false);
  
  // Estados para filtros
  const [league, setLeague] = useState('');
  const [season, setSeason] = useState('');
  const [team, setTeam] = useState('');
  
  // Opciones para los selects
  const [leagues, setLeagues] = useState<any[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [teams, setTeams] = useState<string[]>([]);

  // 1. Cargar ligas al inicio
  useEffect(() => {
    fetch(`${API_BASE}/leagues`)
      .then(res => res.json())
      .then(d => setLeagues(d))
      .catch(console.error);
  }, []);

  // 2. Cargar temporadas y equipos cuando cambia la liga
  useEffect(() => {
    if (!league) {
      setSeasons([]); setTeams([]); setSeason(''); setTeam('');
      return;
    }
    fetch(`${API_BASE}/games/filters?league=${league}`)
      .then(res => res.json())
      .then(d => {
        setSeasons(d.seasons || []);
        setTeams(d.teams || []);
      })
      .catch(console.error);
  }, [league]);

  // 3. Cargar recomendaciones cuando cambian los filtros
  useEffect(() => {
    setLoading(true);
    const params = new URLSearchParams();
    if (league) params.append('league', league);
    if (season) params.append('season', season);
    if (team) params.append('team', team);

    fetch(`${API_BASE}/recommendations/history?${params.toString()}`)
      .then(res => res.json())
      .then(d => setData(d))
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [league, season, team]);

  return (
    <div style={{ padding: '20px', fontFamily: 'sans-serif' }}>
      <h2>Historial de Recomendaciones vs Resultados</h2>
      
      {/* Filtros */}
      <div style={{ display: 'flex', gap: '15px', marginBottom: '20px', flexWrap: 'wrap' }}>
        <div>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '4px' }}>Liga</label>
          <select value={league} onChange={e => setLeague(e.target.value)} style={{ padding: '8px', borderRadius: '4px', border: '1px solid #ccc' }}>
            <option value="">Todas las ligas</option>
            {leagues.map(l => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </div>

        <div>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '4px' }}>Temporada</label>
          <select value={season} onChange={e => setSeason(e.target.value)} disabled={!league} style={{ padding: '8px', borderRadius: '4px', border: '1px solid #ccc' }}>
            <option value="">Todas</option>
            {seasons.map(s => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>

        <div>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '4px' }}>Equipo</label>
          <select value={team} onChange={e => setTeam(e.target.value)} disabled={!league} style={{ padding: '8px', borderRadius: '4px', border: '1px solid #ccc' }}>
            <option value="">Todos los equipos</option>
            {teams.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>
      </div>

      {/* Tabla de Resultados */}
      {loading ? (
        <p>Cargando recomendaciones...</p>
      ) : data.length === 0 ? (
        <p>No hay recomendaciones registradas para estos filtros.</p>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '14px' }}>
          <thead>
            <tr style={{ backgroundColor: '#f4f4f5', borderBottom: '2px solid #e4e4e7' }}>
              <th style={{ padding: '12px' }}>Fecha</th>
              <th style={{ padding: '12px' }}>Liga</th>
              <th style={{ padding: '12px' }}>Partido</th>
              <th style={{ padding: '12px' }}>Recomendación</th>
              <th style={{ padding: '12px' }}>Prob.</th>
              <th style={{ padding: '12px', textAlign: 'center' }}>Córners Reales</th>
              <th style={{ padding: '12px', textAlign: 'center' }}>Resultado</th>
            </tr>
          </thead>
          <tbody>
            {data.map((row, idx) => (
              <tr key={idx} style={{ borderBottom: '1px solid #e4e4e7' }}>
                <td style={{ padding: '12px' }}>{new Date(row.date).toLocaleDateString()}</td>
                <td style={{ padding: '12px' }}>{row.league}</td>
                <td style={{ padding: '12px' }}>{row.home} vs {row.away}</td>
                <td style={{ padding: '12px', fontWeight: 500 }}>{row.recommendation}</td>
                <td style={{ padding: '12px' }}>{(row.prob * 100).toFixed(1)}%</td>
                <td style={{ padding: '12px', textAlign: 'center', fontWeight: 'bold' }}>{row.actualCorners}</td>
                <td style={{ padding: '12px', textAlign: 'center' }}>
                  {row.isHit ? (
                    <span style={{ color: 'green', fontWeight: 'bold' }}>✅ Acertado</span>
                  ) : (
                    <span style={{ color: 'red', fontWeight: 'bold' }}>❌ Fallado</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}