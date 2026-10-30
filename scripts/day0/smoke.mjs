// Day 0 smoke: the UI through the Ingress (or Service), /health, owner bootstrap, kagent through Skanyxx, and an
// employee refused on admin APIs. Run by day0.sh smoke.
import { execFileSync } from 'node:child_process';
import { BASE, CONTEXT, NAMESPACE, call, check, finish, note, readEnvFile, writeEnvFile, stateFile, randomSecret, signIn, ensurePerson, until } from '../e2e/skx.mjs';

const RELEASE = process.env.SKX_RELEASE || 'skanyxx';
note(`Skanyxx at ${BASE}`);

// 1. Reachable: the UI and /health through the published URL.
const health = await until(async () => { const r = await call('GET', '/health'); return r.status === 200 ? r : null; }, 120000);
check('GET /health is 200 Healthy', health?.json?.status === 'Healthy', health ? JSON.stringify(health.json.entries) : 'no answer');
const home = await call('GET', '/');
check('GET / answers (UI reachable)', [200, 302].includes(home.status), `HTTP ${home.status} ${home.headers.get('location') ?? ''}`);
const mcp = await call('POST', '/mcp/memory', { body: {} });
check('/mcp/memory is not published on the UI URL', mcp.status === 404, `HTTP ${mcp.status}`);
const unauth = await call('GET', '/api/models');
check('admin API without a session is refused', unauth.status === 401, `HTTP ${unauth.status}`);

// 2. Owner bootstrap with the release's bootstrap token (read from its Secret, kept in memory only).
const ownerFile = stateFile('owner.env');
let owner = readEnvFile(ownerFile);
if (!owner) {
  const b64 = execFileSync('kubectl', ['--context', CONTEXT, '-n', NAMESPACE, 'get', 'secret', RELEASE, '-o', 'jsonpath={.data.bootstrap-token}']).toString();
  const bootstrapToken = Buffer.from(b64, 'base64').toString();
  owner = { OWNER_EMAIL: 'e2e-owner@e2e.test', OWNER_PASSWORD: randomSecret(28) };
  const r = await call('POST', '/api/identity/bootstrap', {
    headers: { 'X-Bootstrap-Token': bootstrapToken },
    body: { email: owner.OWNER_EMAIL, password: owner.OWNER_PASSWORD, displayName: 'E2E Owner' },
  });
  check('owner bootstrap (POST /api/identity/bootstrap with the bootstrap token)', r.status === 200 || r.status === 201, `HTTP ${r.status}`);
  if (r.status === 200 || r.status === 201) writeEnvFile(ownerFile, owner);
  else finish('day0');
  const again = await call('POST', '/api/identity/bootstrap', {
    headers: { 'X-Bootstrap-Token': bootstrapToken }, body: { email: 'e2e-second@e2e.test', password: randomSecret(28) },
  });
  check('a second bootstrap is refused (409)', again.status === 409, `HTTP ${again.status}`);
} else {
  note(`owner from an earlier run (${ownerFile})`);
}
const login = await call('GET', '/Login');
check('GET /Login serves the sign-in page once the owner exists', login.status === 200 && /<form/i.test(login.text), `HTTP ${login.status}`);
const ownerToken = await signIn(owner.OWNER_EMAIL, owner.OWNER_PASSWORD);
check('owner signs in', !!ownerToken);
const me = await call('GET', '/api/identity/me', { token: ownerToken });
check('owner has the owner role', me.json?.roles?.includes('owner'), `roles ${JSON.stringify(me.json?.roles)}`);

// 3. kagent through Skanyxx: the models list and the chat agent list are kagent's API answers.
const models = await call('GET', '/api/models', { token: ownerToken });
check('kagent reachable from Skanyxx (GET /api/models → kagent ModelConfigs)', models.status === 200, `HTTP ${models.status}, ${models.json?.models?.length ?? '?'} ModelConfigs (none before Day 1)`);
const agents = await call('GET', '/api/chat/agents', { token: ownerToken });
check('kagent agents list through Skanyxx (GET /api/chat/agents)', agents.status === 200, `HTTP ${agents.status}, ${Array.isArray(agents.json) ? agents.json.length : '?'} agents`);

// 4. RBAC: an invited employee is refused on admin APIs.
const employee = await ensurePerson(ownerToken, 'employee');
check('employee invited, accepted and signed in', !!employee);
for (const [method, path, body] of [
  ['GET', '/api/models'],
  ['POST', '/api/models', { name: 'e2e-test-denied', provider: 'OpenAI', model: 'gpt-4o-mini', noApiKey: true }],
  ['GET', '/api/identity/people'],
  ['POST', '/api/identity/invites', { email: 'e2e-test-x@e2e.test', roles: ['owner'] }],
  ['GET', '/api/identity/settings'],
]) {
  const r = await call(method, path, { token: employee, body });
  check(`employee ${method} ${path} → 403`, r.status === 403, `HTTP ${r.status}`);
}
const empChat = await call('GET', '/api/chat/agents', { token: employee });
check('employee may list chat agents (200)', empChat.status === 200, `HTTP ${empChat.status}`);

finish('day0');
