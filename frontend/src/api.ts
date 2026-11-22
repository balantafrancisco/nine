const json = async (r: Response) => {
  if (!r.ok) throw new Error(await r.text());
  return r.status === 204 ? null : r.json();
};
const headers = { 'Content-Type': 'application/json' };

export const api = {
  get: (u: string) => fetch('/api' + u).then(json),
  post: (u: string, b: unknown) => fetch('/api' + u, { method: 'POST', headers, body: JSON.stringify(b) }).then(json),
  del: (u: string) => fetch('/api' + u, { method: 'DELETE' }).then(json),
};
