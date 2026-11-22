import { useEffect, useState } from 'react';
import { api } from './api';

type Team = { id: number; name: string };
type Fixture = { id: number; homeTeamId: number; awayTeamId: number; home: string; away: string };
type Game = {
  id: number;
  externalId: string;
  date: string;
  season: string;
  finished: boolean;
  cornersLoaded: boolean;
  homeCorners: number;
  awayCorners: number;
  league: string;
  home: string;
  away: string;
};

type Recommendation = {
  id: number;
  date: string;
  league: string;
  home: string;
  away: string;
  recommendation: string;
  prob: number;
  actualCorners: number | null;
  isHit: boolean | null;
  isFinished: boolean;
};

const pct = (x: number) => (x * 100).toFixed(1) + '%';

export default function App() {
  // 1. Quitamos 'teams' de las opciones válidas de pestaña
  const [tab, setTab] = useState<'analysis' | 'data' | 'recommendations' | 'games'>('analysis');
  const [leagues, setLeagues] = useState<{ code: string; name: string }[]>([]);

  useEffect(() => { 
    api.get('/leagues').then(setLeagues);
  }, []);

  const T = (k: typeof tab, label: string) => <button className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{label}</button>;
  
  return (
    <>
      <header><h1>⚽ Analizador de corners</h1><span>Probabilidad histórica vs. cuota de la casa</span></header>
      <nav>
        {T('analysis', 'Análisis del día')}
        {T('data', 'Datos y sincronización')}
        {T('recommendations', 'Historial Recomendaciones')}
        {/* 2. Quitamos el botón de Equipos */}
        {T('games', 'Partidos (Base de datos)')}
      </nav>
      {tab === 'analysis' && <AnalysisTab />}
      {tab === 'data' && <DataTab />}
      {tab === 'recommendations' && <RecommendationsTab />}
      {tab === 'games' && <GamesTab />}
    </>
  );
}

function RecommendationsTab() {
  const [data, setData] = useState<Recommendation[]>([]);
  const [loading, setLoading] = useState(false);
  
  const [league, setLeague] = useState('');
  const [season, setSeason] = useState('');
  const [team, setTeam] = useState('');
  // Nuevos estados para la fecha
  const [quickDate, setQuickDate] = useState<'all' | 'yesterday' | 'today' | 'tomorrow'>('today');
  const [customDate, setCustomDate] = useState('');
  
  const [leagues, setLeagues] = useState<{ code: string; name: string }[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [teams, setTeams] = useState<string[]>([]);

  useEffect(() => {
    api.get('/leagues').then(setLeagues);
  }, []);

  useEffect(() => {
    if (!league) {
      setSeasons([]); setTeams([]); setSeason(''); setTeam('');
      return;
    }
    api.get(`/games/filters?league=${league}`).then(d => {
      setSeasons(d.seasons || []);
      setTeams(d.teams || []);
      setSeason('');
      setTeam('');
    });
  }, [league]);

  useEffect(() => {
    setLoading(true);
    const params = new URLSearchParams();
    if (league) params.append('league', league);
    if (season) params.append('season', season);
    if (team) params.append('team', team);
    
    // Si hay una fecha manual seleccionada, enviamos 'date'. Si no, enviamos el botón rápido.
    if (customDate) {
        params.append('date', customDate);
    } else if (quickDate !== 'all') {
        params.append('day', quickDate);
    }

    api.get(`/recommendations/history?${params.toString()}`)
      .then(d => setData(d))
      .catch(console.error)
      .finally(() => setLoading(false));
    }, [league, season, team, quickDate, customDate]);

    // Función para cambiar entre botones rápidos y calendario
    const handleQuickDate = (val: 'all' | 'yesterday' | 'today' | 'tomorrow') => {
        setCustomDate(''); // Limpia la fecha manual
        setQuickDate(val);
    };

    return (
    <div className="card">
      <h2>Historial de Recomendaciones vs Resultados</h2>
      
      {/* Fila 1: Filtros Rápidos de Fecha */}
      <div className="row" style={{ marginBottom: '10px', display: 'flex', gap: '5px' }}>
        {(['yesterday', 'today', 'tomorrow', 'all'] as const).map(d => (
          <button 
            key={d}
            onClick={() => handleQuickDate(d)}
            style={{
              backgroundColor: (!customDate && quickDate === d) ? '#007bff' : '#f0f0f0',
              color: (!customDate && quickDate === d) ? 'white' : 'black',
              border: '1px solid #ccc',
              padding: '6px 12px',
              borderRadius: '4px',
              fontWeight: (!customDate && quickDate === d) ? 'bold' : 'normal',
              cursor: 'pointer'
            }}
          >
            {d === 'yesterday' ? 'Ayer' : d === 'today' ? 'Hoy' : d === 'tomorrow' ? 'Mañana' : 'Todos'}
          </button>
        ))}

        {/* Selector de fecha personalizado */}
        <div style={{ marginLeft: '10px', display: 'flex', alignItems: 'center', gap: '5px' }}>
          <span style={{ fontSize: '14px', color: '#666' }}>O buscar fecha:</span>
          <input 
            type="date" 
            value={customDate} 
            onChange={e => {
                setCustomDate(e.target.value);
                if (e.target.value) setQuickDate('all'); // Desactiva los botones
            }} 
            style={{ padding: '5px', borderRadius: '4px', border: '1px solid #ccc' }}
          />
          {customDate && (
              <button onClick={() => setCustomDate('')} style={{ padding: '5px', cursor: 'pointer' }}>✕</button>
          )}
        </div>
      </div>

      {/* Fila 2: Filtros de Liga, Temporada y Equipo */}
      <div className="row" style={{ marginBottom: '1rem', display: 'flex', gap: '15px', flexWrap: 'wrap' }}>
        <label>
          Liga: 
          <select value={league} onChange={e => setLeague(e.target.value)} style={{ marginLeft: '5px' }}>
            <option value="">Todas las ligas</option>
            {leagues.map(l => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>

        <label>
          Temporada: 
          <select value={season} onChange={e => setSeason(e.target.value)} disabled={!league} style={{ marginLeft: '5px' }}>
            <option value="">Todas</option>
            {seasons.map(s => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>

        <label>
          Equipo: 
          <select value={team} onChange={e => setTeam(e.target.value)} disabled={!league} style={{ marginLeft: '5px' }}>
            <option value="">Todos los equipos</option>
            {teams.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
      </div>

      {loading ? (
        <p>⏳ Cargando historial...</p>
      ) : data.length === 0 ? (
        <p className="empty">No hay recomendaciones registradas para estos filtros.</p>
      ) : (
        <div className="scroll">
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Liga</th>
                <th>Partido</th>
                <th>Recomendación</th>
                <th>Prob.</th>
                <th style={{ textAlign: 'center' }}>Córners Reales</th>
                <th style={{ textAlign: 'center' }}>Resultado</th>
              </tr>
            </thead>
            <tbody>
              {data.map((row, idx) => (
                <tr key={idx}>
                  <td>{new Date(row.date).toLocaleDateString('es-CO')}</td>
                  <td>{row.league}</td>
                  <td>{row.home} vs {row.away}</td>
                  <td style={{ fontWeight: 500 }}>{row.recommendation}</td>
                  <td>{pct(row.prob)}</td>
                  <td style={{ textAlign: 'center', fontWeight: 'bold' }}>
                    {row.actualCorners !== null ? row.actualCorners : '—'}
                  </td>
                  <td style={{ textAlign: 'center' }}>
                    {row.isFinished ? (
                      row.isHit ? (
                        <span className="ok" style={{ fontWeight: 'bold' }}>✅ Acertado</span>
                      ) : (
                        <span className="bad" style={{ fontWeight: 'bold', color: '#dc3545' }}>❌ Fallado</span>
                      )
                    ) : (
                      <span className="warn" style={{ fontWeight: 'bold', color: '#fd7e14' }}>⏳ Pendiente</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

type Market = { subject: string; period: string; direction: string; line: number; hits: number; n: number; probA: number; hitsB: number | null; nB: number | null; probB: number | null; prob: number; minOdds: number; confidence: string };
type Day = { gameId: number; league: string; date: string; home: string; away: string; homeGames: number; awayGames: number; candidates: Market[] };
type Detail = { league: string; date: string; home: string; away: string; homeGames: number; awayGames: number; markets: Market[] };

const PER: Record<string, string> = { FT: 'Partido completo', '1T': '1er tiempo', '2T': '2do tiempo' };
const who = (m: Market, h: string, a: string) => (m.subject === 'MATCH' ? 'Total' : m.subject === 'HOME' ? h : a);
const mkt = (m: Market, h: string, a: string) => `${who(m, h, a)} · ${PER[m.period]} · ${m.direction === 'OVER' ? 'Más' : 'Menos'} de ${m.line}`;
const hour = (d: string) => new Date(d).toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit' });

function AnalysisTab() {
  const [date, setDate] = useState(new Date().toLocaleDateString('en-CA'));
  const [minProb, setMinProb] = useState(70);
  const [data, setData] = useState<{ total: number; matches: Day[] } | null>(null);
  const [open, setOpen] = useState(0);
  useEffect(() => { api.get(`/analysis/day?date=${date}&minProb=${minProb / 100}`).then(setData); }, [date, minProb]);
  if (open) return <DetailView id={open} back={() => setOpen(0)} />;
  return (
    <div>
      <div className="row">
        <label>Fecha <input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <label>Probabilidad mínima <select value={minProb} onChange={e => setMinProb(+e.target.value)}>
          {[60, 65, 70, 75, 80].map(p => <option key={p} value={p}>{p}%</option>)}</select></label>
        {data && <small>{data.matches.length} de {data.total} partidos con apuestas para revisar</small>}
      </div>
      {data?.matches.length === 0 && <p className="empty">Ningún partido de esta fecha tiene apuestas que superen el umbral. Revisa que partidos y corners estén sincronizados en «Datos y sincronización».</p>}
      <div className="grid">
        {data?.matches.map(m => (
          <div key={m.gameId} className="card click" onClick={() => setOpen(m.gameId)}>
            <div className="meta"><span className="badge">{m.league}</span><span>{hour(m.date)}</span></div>
            <h3>{m.home} <small>vs</small> {m.away}</h3>
            {m.candidates.map((c, i) => (
              <div key={i} className="cand"><span>{mkt(c, m.home, m.away)}</span><b className="ok">{pct(c.prob)}</b><small>cuota mín. {c.minOdds.toFixed(2)}</small></div>
            ))}
            <small className="foot">Muestra: {m.homeGames} de local + {m.awayGames} de visita · Ver análisis →</small>
          </div>
        ))}
      </div>
    </div>
  );
}

function DetailView({ id, back }: { id: number; back: () => void }) {
  const [d, setD] = useState<Detail | null>(null);
  const [odds, setOdds] = useState<Record<string, string>>({});
  useEffect(() => { api.get(`/games/${id}/analysis`).then(setD); }, [id]);
  if (!d) return <p>Cargando…</p>;
  return (
    <div>
      <button onClick={back}>← Volver</button>
      <h2>{d.home} vs {d.away} <small>· {d.league} · {hour(d.date)}</small></h2>
      <p>Muestra: {d.home} {d.homeGames} partidos de local · {d.away} {d.awayGames} de visita. Escribe la cuota que ves en la casa de apuestas: hay valor si supera la cuota mínima.</p>
      <div className="scroll"><table>
        <thead><tr><th>Mercado</th><th>Aciertos A</th><th>Aciertos B</th><th>Prob. conservadora</th><th>Cuota mínima</th><th>Cuota casa</th><th>EV</th></tr></thead>
        <tbody>{d.markets.filter(m => m.prob >= 0.5).map(m => {
          const k = `${m.subject}${m.period}${m.direction}${m.line}`;
          const o = parseFloat((odds[k] ?? '').replace(',', '.'));
          const ev = isNaN(o) ? null : m.prob * o - 1;
          return (
            <tr key={k}>
              <td>{mkt(m, d.home, d.away)}</td><td>{m.hits}/{m.n}</td><td>{m.hitsB == null ? '—' : `${m.hitsB}/${m.nB}`}</td>
              <td>{pct(m.prob)}</td><td>{m.minOdds.toFixed(2)}</td>
              <td><input className="odds" inputMode="decimal" value={odds[k] ?? ''} onChange={e => setOdds({ ...odds, [k]: e.target.value })} /></td>
              <td className={ev == null ? '' : ev > 0 ? 'ok' : 'bad'}>{ev == null ? '' : `${ev > 0 ? '✔' : '✘'} ${pct(ev)}`}</td>
            </tr>);
        })}</tbody>
      </table></div>
      <p><small>Prob. = el menor porcentaje entre el Método A (cruce local×visita) y el B (totales reales). Con pocos partidos es una estimación débil, no una garantía.</small></p>
    </div>
  );
}

function DataTab() {
  const [leagues, setLeagues] = useState<{ code: string; name: string }[]>([]);
  const [league, setLeague] = useState('');
  const [season, setSeason] = useState('2026-2027');
  const [msg, setMsg] = useState('');
  const [progress, setProgress] = useState<string>('');
  
  useEffect(() => { api.get('/leagues').then(setLeagues); }, []);
  
  const run = (url: string, desc: string) => { 
    if (!league) return alert("Por favor selecciona una liga primero.");
    setMsg(`⏳ ${desc}...`); 
    setProgress('');
    api.post(url, {}).then(r => setMsg(JSON.stringify(r))).catch(e => setMsg(String(e.message))); 
  };

  const runSeasonSync = async () => {
    if (!league) return alert("Por favor selecciona una liga primero.");
    setMsg('⏳ Iniciando sincronización de temporada...');
    setProgress('');
    
    try {
        const { jobId } = await api.post(`/sync/fixtures?league=${league}&season=${season}`, {});
        
        const interval = setInterval(async () => {
            try {
                const status = await api.get(`/sync/corners/status/${jobId}`);
                
                if (status.isCompleted) {
                    clearInterval(interval);
                    if (status.error) {
                      setMsg(`❌ Error: ${status.error}`);
                    } else {
                      setMsg(`✅ Temporada sincronizada. ${status.loaded} partidos guardados/actualizados.`);
                    }
                    setProgress('');
                } else {
                  setProgress(`Procesando día ${status.processed} de ${status.total}...`);
                }
            } catch (e: any) {
                clearInterval(interval);
                setMsg(`❌ Error consultando estado: ${e.message}`);
            }
        }, 1500);
        
    } catch (e: any) {
        setMsg(`❌ Error: ${e.message}`);
    }
  };

  const runSyncCorners = async () => {
    if (!league) return alert("Por favor selecciona una liga primero.");
    setMsg('⏳ Iniciando sincronización de corners...');
    setProgress('');
    
    try {
        const { jobId } = await api.post(`/sync/corners?league=${league}`, {});
        
        const interval = setInterval(async () => {
            try {
                const status = await api.get(`/sync/corners/status/${jobId}`);
                
                if (status.isCompleted) {
                    clearInterval(interval);
                    if (status.error) {
                        setMsg(`❌ Error: ${status.error}`);
                    } else if (status.loaded === 0) {
                        setMsg(`⚠️ Sincronización finalizada, pero Flashscore no devolvió corners. Revisa la consola de tu backend para ver los logs.`);
                    } else {
                        setMsg(`✅ Sincronización completada. ${status.loaded} corners cargados de ${status.total} partidos.`);
                    }
                    setProgress('');
                } else {
                    setProgress(`Procesando ${status.processed} de ${status.total}...`);
                }
            } catch (e: any) {
                clearInterval(interval);
                setMsg(`❌ Error consultando estado: ${e.message}`);
            }
        }, 1500);
        
    } catch (e: any) {
        setMsg(`❌ Error: ${e.message}`);
    }
  };

  return (
    <div className="card">
      <div className="row">
        <select value={league} onChange={e => setLeague(e.target.value)}>
          <option value="">-- Seleccione Liga --</option>
          {leagues.map(l => <option key={l.code} value={l.code}>{l.name}</option>)}
        </select>
        <input value={season} onChange={e => setSeason(e.target.value)} style={{ width: 110 }} disabled={!league} />
      </div>
      
      <div className="row">
        <button onClick={runSeasonSync} disabled={!league}>1. Registrar equipos y partidos de la temporada</button>
        <button onClick={runSyncCorners} disabled={!league}>2. Cargar corners pendientes</button>
        
        <button onClick={() => {
            setMsg('⏳ Actualizando últimos días...');
            setProgress('');
            api.post('/sync/recent?days=2', {}).then(r => {
                if (r.cornersLoaded === 0) {
                    setMsg(`⚠️ Se actualizaron partidos, pero no se cargaron nuevos corners.`);
                } else {
                    setMsg(`✅ Actualización completada. ${JSON.stringify(r)}`);
                }
            }).catch(e => setMsg(`❌ Error: ${e.message}`));
        }}>
            3. Actualizar últimos 2 días (todas las ligas)
        </button>
        
        <button disabled={!league} onClick={async () => {
            setMsg("⏳ Buscando historial completo en Flashscore...");
            setProgress("");
            try {
                const res = await api.post(`/sync/season-html?league=${league}&season=${season}`, {});
                if (res && res.jobId) {
                    const interval = setInterval(async () => {
                        try {
                            const status = await api.get(`/sync/corners/status/${res.jobId}`);
                            if (status.isCompleted) {
                                clearInterval(interval);
                                setMsg(status.error ? `❌ Error: ${status.error}` : `✅ Historial HTML cargado. ${status.loaded} partidos.`);
                                setProgress("");
                            } else {
                                setProgress(`Extrayendo: ${status.processed}/${status.total}...`);
                            }
                        } catch (e: any) {
                            clearInterval(interval);
                            setMsg(`❌ Error consultando estado: ${e.message}`);
                        }
                    }, 1500);
                } else {
                    setMsg("❌ El backend no devolvió un Job ID.");
                }
            } catch (e: any) {
                setMsg(`❌ Error de conexión: ${e.message}`);
            }
          }}>Sincronizar historial completo (desde HTML)
        </button>

        <button disabled={!league} onClick={async () => {
            setMsg("⏳ Iniciando historial de 1 año...");
            setProgress("");
            try {
                const res = await api.post(`/sync/season-history?league=${league}&daysBack=365`, {});
                setMsg(`✅ Historial cargado. Añadidos: ${res.added}, Actualizados: ${res.updated}`);
            } catch (e: any) {
                setMsg(`❌ Error: ${e.message}`);
            }
        }}>Sincronizar historial (último año)</button>
      </div>
      
      {progress && <p className="warn" style={{ margin: '0.5rem 0', fontWeight: 'bold' }}>{progress}</p>}
      <pre className="msg">{msg}</pre>
    </div>
  );
}

function GamesTab() {
  const [games, setGames] = useState<Game[]>([]);
  const [league, setLeague] = useState('');
  const [season, setSeason] = useState('');
  const [teamFilter, setTeamFilter] = useState('');
  const [loading, setLoading] = useState(false);
  const [progress, setProgress] = useState('');

  const [availableSeasons, setAvailableSeasons] = useState<string[]>([]);
  const [availableTeams, setAvailableTeams] = useState<string[]>([]);
  const [leagueTeams, setLeagueTeams] = useState<Team[]>([]);

  const [showForm, setShowForm] = useState(false);
  const [newHome, setNewHome] = useState(0);
  const [newAway, setNewAway] = useState(0);
  const [newDate, setNewDate] = useState(new Date().toISOString().slice(0, 16));
  const [newExternalId, setNewExternalId] = useState('');
  const [newSeason, setNewSeason] = useState('2026-2027');
  
  const [newFinished, setNewFinished] = useState(false);
  const [cHome, setCHome] = useState<number | ''>('');
  const [cAway, setCAway] = useState<number | ''>('');
  const [c1Home, setC1Home] = useState<number | ''>('');
  const [c1Away, setC1Away] = useState<number | ''>('');
  const [c2Home, setC2Home] = useState<number | ''>('');
  const [c2Away, setC2Away] = useState<number | ''>('');

  const [editingGame, setEditingGame] = useState<number | null>(null);
  const [manualHome, setManualHome] = useState<number | ''>('');
  const [manualAway, setManualAway] = useState<number | ''>('');

  useEffect(() => {
    if (!league) {
      setAvailableSeasons([]);
      setAvailableTeams([]);
      setLeagueTeams([]);
      setGames([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    api.get(`/games/filters?league=${league}`).then(data => {
      setAvailableSeasons(data.seasons);
      setAvailableTeams(data.teams);
      if (data.seasons.length > 0) setSeason(data.seasons[0]);
      else setSeason('');
      setTeamFilter('');
    }).catch(console.error);

    api.get(`/teams/league/${league}`).then(setLeagueTeams).catch(console.error);
    setNewHome(0);
    setNewAway(0);
  }, [league]);

  const loadGames = () => {
    if (!league) return;
    setLoading(true);
    const params = new URLSearchParams({ league, limit: '250' });
    if (season) params.append('season', season);
    if (teamFilter) params.append('team', teamFilter);
    
    api.get(`/games?${params.toString()}`)
      .then(setGames)
      .catch(e => console.error(e))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadGames(); }, [season, teamFilter]);

  const syncAll = async () => {
    if (!league) return;
    setLoading(true);
    setProgress('Iniciando...');
    try {
      const { jobId } = await api.post(`/sync/corners?league=${league}`, {});
      const interval = setInterval(async () => {
        try {
          const status = await api.get(`/sync/corners/status/${jobId}`);
          if (status.isCompleted) {
            clearInterval(interval);
            setProgress('');
            loadGames();
          } else {
            setProgress(`Cargando: ${status.processed}/${status.total}`);
          }
        } catch {
          clearInterval(interval);
          setProgress('');
          setLoading(false);
        }
      }, 1500);
    } catch (e: any) {
      alert("Error: " + e.message);
      setLoading(false);
      setProgress('');
    }
  };

  const handleAddGame = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newHome || !newAway) return alert("Debes seleccionar equipo local y visitante.");
    
    try {
        await api.post('/games', {
            homeTeamId: newHome,
            awayTeamId: newAway,
            date: newDate,
            leagueCode: league,
            season: newSeason,
            externalId: newExternalId.trim() || null,
            homeCorners: cHome !== '' ? Number(cHome) : null,
            awayCorners: cAway !== '' ? Number(cAway) : null,
            home1T: c1Home !== '' ? Number(c1Home) : null,
            away1T: c1Away !== '' ? Number(c1Away) : null,
            home2T: c2Home !== '' ? Number(c2Home) : null,
            away2T: c2Away !== '' ? Number(c2Away) : null,
            finished: newFinished || (cHome !== '' && cAway !== '')
        });
        
        setShowForm(false);
        setNewHome(0); setNewAway(0); setNewExternalId('');
        setCHome(''); setCAway(''); setC1Home(''); setC1Away(''); setC2Home(''); setC2Away('');
        setNewFinished(false);
        loadGames();
    } catch (e: any) {
        alert("Error al agregar partido: " + e.message);
    }
  };

  const handleSaveManualCorners = async (id: number) => {
    if (manualHome === '' || manualAway === '') return;
    try {
        await api.post(`/games/${id}/manual-corners?homeCorners=${manualHome}&awayCorners=${manualAway}`, {});
        setEditingGame(null);
        setManualHome('');
        setManualAway('');
        loadGames();
    } catch (e: any) {
        alert("Error al guardar corners: " + e.message);
    }
  };

  return (
    <div className="card">
      <div className="row">
        <label>Liga:</label>
        <select value={league} onChange={e => setLeague(e.target.value)}>
          <option value="">-- Seleccione Liga --</option>
          <option value="FRA1">Ligue 1</option>
          <option value="ENG1">Premier League</option>
          <option value="ESP1">LaLiga</option>
          <option value="ITA1">Serie A</option>
          <option value="GER1">Bundesliga</option>
        </select>

        <label>Temporada:</label>
        <select value={season} onChange={e => setSeason(e.target.value)} style={{ width: 150 }} disabled={!league}>
          {availableSeasons.length === 0 && <option value="">(Sin temporadas)</option>}
          {availableSeasons.map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        <label>Equipo:</label>
        <select value={teamFilter} onChange={e => setTeamFilter(e.target.value)} style={{ width: 180 }} disabled={!league}>
          <option value="">(Todos)</option>
          {availableTeams.map(t => <option key={t} value={t}>{t}</option>)}
        </select>

        <button onClick={loadGames} disabled={!league}>Refrescar</button>
        <button onClick={syncAll} disabled={!league}>Cargar todos los corners pendientes</button>
        <button onClick={() => { setSeason(''); setTeamFilter(''); }} disabled={!league}>Limpiar filtros</button>
        
        <button onClick={() => setShowForm(!showForm)} style={{ backgroundColor: '#28a745', color: 'white' }} disabled={!league}>
          {showForm ? 'Cancelar' : '+ Agregar Partido'}
        </button>
      </div>

      {showForm && (
        <form onSubmit={handleAddGame} style={{ marginTop: '10px', padding: '15px', backgroundColor: '#f8f9fa', borderRadius: '5px' }}>
          <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', marginBottom: '10px' }}>
            <select value={newHome} onChange={e => {
                setNewHome(+e.target.value);
                if (+e.target.value === newAway) setNewAway(0);
            }} required>
                <option value={0}>-- Equipo Local --</option>
                {leagueTeams.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
            
            <span style={{ alignSelf: 'center' }}>vs</span>
            
            <select value={newAway} onChange={e => setNewAway(+e.target.value)} required>
                <option value={0}>-- Equipo Visita --</option>
                {leagueTeams.filter(t => t.id !== newHome).map(t => (
                    <option key={t.id} value={t.id}>{t.name}</option>
                ))}
            </select>

            <input type="datetime-local" value={newDate} onChange={e => setNewDate(e.target.value)} required />
            
            <input 
                type="text" 
                placeholder="Temporada (ej. 2026-2027)" 
                value={newSeason}
                onChange={e => setNewSeason(e.target.value)}
                required
                style={{ width: '160px' }}
            />

            <input 
                type="text" 
                placeholder="ID Flashscore (Opcional)" 
                value={newExternalId}
                onChange={e => setNewExternalId(e.target.value)}
                style={{ width: '160px' }}
            />
          </div>

          <div style={{ display: 'flex', gap: '15px', alignItems: 'center', backgroundColor: '#e9ecef', padding: '10px', borderRadius: '5px' }}>
            <strong>Cargar Corners:</strong>
            
            <label>Total: <input type="number" placeholder="L" value={cHome} onChange={e => setCHome(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /> - <input type="number" placeholder="V" value={cAway} onChange={e => setCAway(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /></label>
            
            <label>1T: <input type="number" placeholder="L" value={c1Home} onChange={e => setC1Home(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /> - <input type="number" placeholder="V" value={c1Away} onChange={e => setC1Away(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /></label>
            
            <label>2T: <input type="number" placeholder="L" value={c2Home} onChange={e => setC2Home(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /> - <input type="number" placeholder="V" value={c2Away} onChange={e => setC2Away(e.target.value ? +e.target.value : '')} style={{ width: '50px' }} /></label>

            <label style={{ marginLeft: 'auto' }}>
                <input type="checkbox" checked={newFinished} onChange={e => setNewFinished(e.target.checked)} /> Finalizado
            </label>
          </div>

          <div style={{ marginTop: '10px', textAlign: 'right' }}>
            <button type="submit" style={{ backgroundColor: '#007bff', color: 'white', padding: '8px 16px' }}>Guardar Partido</button>
          </div>
        </form>
      )}

      <div className="row" style={{ marginTop: '1rem' }}>
        {loading && <span>⏳ Cargando...</span>}
        {progress && <span className="warn">🔄 {progress}</span>}
        {league && <span>{games.length} partidos encontrados</span>}
      </div>

      <div className="scroll">
        <table>
          <thead>
            <tr>
              <th>Fecha</th>
              <th>Liga</th>
              <th>Local</th>
              <th>Visita</th>
              <th>Corners (L-V)</th>
              <th>Estado</th>
            </tr>
          </thead>
          <tbody>
            {games.map(g => (
              <tr key={g.id}>
                <td>{new Date(g.date).toLocaleString('es-CO')}</td>
                <td>{g.league}</td>
                <td>{g.home}</td>
                <td>{g.away}</td>
                <td>
                  {g.cornersLoaded ? `${g.homeCorners} - ${g.awayCorners}` : '—'}
                </td>
                <td>
                  {g.finished ? <span className="ok">Finalizado</span> : <span className="warn">Programado</span>}
                  
                  {g.cornersLoaded ? (
                    <span className="ok" style={{ marginLeft: '0.5rem' }}>✅ {g.homeCorners} - {g.awayCorners}</span>
                  ) : editingGame === g.id ? (
                    <span style={{ marginLeft: '0.5rem' }}>
                        <input type="number" placeholder="L" value={manualHome} onChange={e => setManualHome(e.target.value ? +e.target.value : '')} style={{ width: '40px', marginRight: '5px' }} />
                        -
                        <input type="number" placeholder="V" value={manualAway} onChange={e => setManualAway(e.target.value ? +e.target.value : '')} style={{ width: '40px', marginLeft: '5px', marginRight: '5px' }} />
                        <button type="button" onClick={() => handleSaveManualCorners(g.id)} style={{ padding: '0.2rem 0.5rem', fontSize: '0.8rem', backgroundColor: '#28a745', color: 'white', border: 'none', borderRadius: '3px' }}>OK</button>
                        <button type="button" onClick={() => setEditingGame(null)} style={{ padding: '0.2rem 0.5rem', fontSize: '0.8rem', marginLeft: '2px' }}>X</button>
                    </span>
                  ) : (
                    <span style={{ marginLeft: '0.5rem' }}>
                      <button 
                        style={{ padding: '0.2rem 0.5rem', fontSize: '0.8rem' }}
                        onClick={async () => {
                          try {
                            const res = await api.post(`/games/${g.id}/sync-corners`, {});
                            if (res.loaded === 0) {
                                setEditingGame(g.id);
                                alert(`⚠️ ${res.message || 'No se pudieron cargar los corners.'} Ingresa los datos manualmente.`);
                            } else {
                                alert("✅ ¡Corners cargados!");
                                loadGames();
                            }
                          } catch (e: any) {
                            alert(`❌ Error: ${e.message}`);
                            setEditingGame(g.id);
                          }
                        }}
                      >
                        Cargar Corners
                      </button>
                      <button 
                        style={{ padding: '0.2rem 0.5rem', fontSize: '0.8rem', marginLeft: '0.2rem', backgroundColor: '#ffc107' }}
                        onClick={() => setEditingGame(g.id)}
                        title="Ingresar manualmente"
                      >
                        ✍️
                      </button>
                    </span>
                  )}
                </td>
              </tr>
            ))}
            {games.length === 0 && !loading && (
              <tr>
                <td colSpan={6} style={{ textAlign: 'center', padding: '2rem', color: '#666' }}>
                  {league ? 'No hay partidos cargados para esta liga.' : 'Selecciona una liga para ver los partidos.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}