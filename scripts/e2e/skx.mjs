// Shared by the Day 0/1/2 node steps: Skanyxx API calls with a bearer token, PASS/FAIL checks, the state dir.
// Env (set by lib.sh run_node): SKX_BASE, SKX_STATE, SKX_CONTEXT, SKX_NAMESPACE, SKX_RELEASE.
// Nothing secret is ever printed: details are statuses, names and counts.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

export const BASE = process.env.SKX_BASE;
export const STATE = process.env.SKX_STATE;
export const CONTEXT = process.env.SKX_CONTEXT;
export const NAMESPACE = process.env.SKX_NAMESPACE || 'skanyxx';
if (!BASE || !STATE) throw new Error('run through lib.sh (SKX_BASE and SKX_STATE unset)');

const results = [];
export function check(name, ok, detail = '') {
  results.push({ name, ok: !!ok, detail: String(detail ?? '') });
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' — ' + detail : ''}`);
  return !!ok;
}
export function note(text) { console.log(`---- ${text}`); }

// Writes the results to $STATE/<step>-result.json and exits 1 when anything failed.
export function finish(step) {
  const failed = results.filter(r => !r.ok);
  fs.writeFileSync(path.join(STATE, `${step}-result.json`), JSON.stringify({ step, at: new Date().toISOString(), results }, null, 2));
  console.log(`${step}: ${results.length - failed.length}/${results.length} passed`);
  process.exit(failed.length ? 1 : 0);
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

// Waits until fn() returns a truthy value (checked every `every` ms), or returns the last value after `ms`.
export async function until(fn, ms = 120000, every = 3000) {
  const end = Date.now() + ms;
  let last;
  while (Date.now() < end) {
    try { last = await fn(); } catch { last = undefined; }
    if (last) return last;
    await sleep(every);
  }
  return last;
}

export async function call(method, url, { token, body, headers = {}, timeout = 60000 } = {}) {
  const h = { ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  if (body !== undefined) h['Content-Type'] = 'application/json';
  const r = await fetch(url.startsWith('http') ? url : BASE + url, {
    method, headers: h, body: body === undefined ? undefined : JSON.stringify(body), redirect: 'manual',
    signal: AbortSignal.timeout(timeout),
  });
  const text = await r.text();
  let json = null;
  try { json = JSON.parse(text); } catch { }
  return { status: r.status, json, text, headers: r.headers };
}

// Retried for a minute: right after a rollout the old pod can still answer for a moment.
export async function signIn(email, password) {
  for (let i = 0; i < 12; i++) {
    const r = await call('POST', '/api/identity/sign-in', { body: { email, password } }).catch(() => ({ status: 0 }));
    if (r.json?.tokens?.accessToken) return r.json.tokens.accessToken;
    await sleep(5000);
  }
  return null;
}

// KEY=value lines (no quoting); the file stays 0600.
export function readEnvFile(file) {
  if (!fs.existsSync(file)) return null;
  return Object.fromEntries(fs.readFileSync(file, 'utf8').split('\n').filter(l => l.includes('='))
    .map(l => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]));
}
export function writeEnvFile(file, values) {
  fs.writeFileSync(file, Object.entries(values).map(([k, v]) => `${k}=${v}`).join('\n') + '\n', { mode: 0o600 });
  fs.chmodSync(file, 0o600);
}
export const stateFile = name => path.join(STATE, name);

export function randomSecret(n = 24) {
  const abc = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';
  return Array.from(crypto.getRandomValues(new Uint8Array(n)), b => abc[b % abc.length]).join('') + '-9x';
}

// kubectl on the target context, JSON out (read-only uses: get).
export function kubectlJson(...args) {
  try {
    return JSON.parse(execFileSync('kubectl', ['--context', CONTEXT, ...args, '-o', 'json'], { stdio: ['ignore', 'pipe', 'ignore'] }).toString());
  } catch { return null; }
}

// The people every day uses: invited once by the owner, passwords in $STATE/people.env (0600).
export const PEOPLE = {
  supervisor: 'e2e-test-supervisor@e2e.test',
  builder: 'e2e-test-builder@e2e.test',
  employee: 'e2e-test-employee@e2e.test',
};

export async function ensurePerson(ownerToken, role) {
  const file = stateFile('people.env');
  const saved = readEnvFile(file) ?? {};
  const email = PEOPLE[role];
  if (saved[role]) {
    // A clean (day2.sh clean) disables the e2e people: the owner enables them again for the next run.
    const everyone = (await call('GET', '/api/identity/people', { token: ownerToken })).json;
    const person = (Array.isArray(everyone) ? everyone : everyone?.people ?? everyone?.items ?? []).find(p => p.email === email);
    if (person?.disabled) await call('POST', `/api/identity/people/${person.id}/enable`, { token: ownerToken });
    const t = await signIn(email, saved[role]);
    if (t) return t;
  }
  const r = await call('POST', '/api/identity/invites', { token: ownerToken, body: { email, roles: [role] } });
  if (!r.json?.link) throw new Error(`invite ${role}: HTTP ${r.status} (an account from an earlier run with a lost password? delete $STATE and the cluster)`);
  const inviteToken = new URL(r.json.link).searchParams.get('token');
  const password = randomSecret();
  const a = await call('POST', '/api/identity/invites/accept', { body: { token: inviteToken, password, displayName: `E2E ${role}` } });
  if (a.status !== 201 && a.status !== 200) throw new Error(`accept ${role}: HTTP ${a.status}`);
  writeEnvFile(file, { ...saved, [role]: password });
  return signIn(email, password);
}

export async function ownerToken() {
  const o = readEnvFile(stateFile('owner.env'));
  if (!o) throw new Error('no owner yet: run scripts/day0/day0.sh first');
  const t = await signIn(o.OWNER_EMAIL, o.OWNER_PASSWORD);
  if (!t) throw new Error('the owner cannot sign in');
  return t;
}
