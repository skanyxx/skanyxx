// Day 1: Skanyxx's read-only Argo CD token. Signs in as Argo's initial admin (password read from its Secret, kept in
// memory), mints a token for the apiKey account `skanyxx` into $STATE/argo-token (0600) and proves it reads the
// Application but cannot sync it. Run by day1.sh through a port-forward to argocd-server ($ARGO_PF_PORT).
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { CONTEXT, call, check, finish, stateFile, until } from '../e2e/skx.mjs';

const ARGO = `http://127.0.0.1:${process.env.ARGO_PF_PORT}`;
const file = stateFile('argo-token');
const app = '/api/v1/applications/skanyxx-agents';

const readsApp = async token => (await call('GET', ARGO + app, { token })).status === 200;

let token = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
if (!token || !(await readsApp(token))) {
  const b64 = execFileSync('kubectl', ['--context', CONTEXT, '-n', 'argocd', 'get', 'secret', 'argocd-initial-admin-secret', '-o', 'jsonpath={.data.password}']).toString();
  const session = await until(async () => (await call('POST', ARGO + '/api/v1/session', {
    body: { username: 'admin', password: Buffer.from(b64, 'base64').toString() },
  })).json?.token, 120000);
  check('Argo CD admin session', !!session);
  const minted = await call('POST', ARGO + '/api/v1/account/skanyxx/token', { token: session, body: { id: `skanyxx-${Date.now()}` } });
  check('read-only token for the Argo account skanyxx', minted.status === 200 && !!minted.json?.token, `HTTP ${minted.status}`);
  token = minted.json?.token ?? '';
  fs.writeFileSync(file, token, { mode: 0o600 });
  fs.chmodSync(file, 0o600);
}
check('the skanyxx token reads the Application', await until(() => readsApp(token), 60000));
const sync = await call('POST', ARGO + app + '/sync', { token, body: {} });
check('the skanyxx token cannot sync it (403)', sync.status === 403, `HTTP ${sync.status}`);
const del = await call('DELETE', ARGO + app, { token, body: {} });
check('the skanyxx token cannot delete it (403), and it is still there', del.status === 403 && await readsApp(token), `HTTP ${del.status}`);
finish('day1-argo');
