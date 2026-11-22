const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5080/api';

const json = async (r: Response) => {
  if (!r.ok) throw new Error(await r.text());
  return r.status === 204 ? null : r.json();
};

const headers = { 'Content-Type': 'application/json' };

export const api = {
  get: (u: string) => fetch(`${API_BASE}${u}`, { headers }).then(json),
  post: (u: string, b: unknown) => fetch(`${API_BASE}${u}`, { method: 'POST', headers, body: JSON.stringify(b) }).then(json),
  del: (u: string) => fetch(`${API_BASE}${u}`, { method: 'DELETE', headers }).then(json),
};