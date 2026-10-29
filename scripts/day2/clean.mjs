// Day 2 cleanup: every e2e-test-* object the days created goes, through the same paths a person would use.
//   agents: their folders leave main by one pull request (Skanyxx's git account; the studio has no "remove" yet,
//           D115), then the owner's confirmed reconcile pass removes them from kagent (the removal brake, D119);
//   cards: marked stale (memory keeps versions, nothing is deleted); conversations: deleted by their owner;
//   models: e2e-test-* ModelConfigs deleted through /api/models; people: e2e-test-* accounts disabled.
// Env: AGENTS (comma list: only those agents; default every agents/e2e-test-* folder in main and everything else),
// GITEA_LOCAL, GITEA_TOKEN_FILE.
import fs from 'node:fs';
import { NAMESPACE, call, check, finish, kubectlJson, note, ownerToken, signIn, readEnvFile, stateFile, until, PEOPLE } from '../e2e/skx.mjs';

const { GITEA_LOCAL, GITEA_TOKEN_FILE } = process.env;
const gitToken = fs.readFileSync(GITEA_TOKEN_FILE, 'utf8');
const gitea = (method, path, body) => call(method, `${GITEA_LOCAL}/api/v1${path}`, { body, headers: { Authorization: `token ${gitToken}` } });
const REPO = '/repos/skanyxx/skanyxx-agents';
const owner = await ownerToken();

// Agents: one PR deleting every e2e-test-* folder, merged by the bot (only it may merge into main).
const tree = await gitea('GET', `${REPO}/git/trees/main?recursive=true`);
const files = (tree.json?.tree ?? []).filter(f => f.type === 'blob' && /^agents\/e2e-test-[a-z0-9-]+\//.test(f.path));
const agents = process.env.AGENTS ? process.env.AGENTS.split(',') : [...new Set(files.map(f => f.path.split('/')[1]))];
for (const open of (await call('GET', '/api/studio', { token: owner })).json?.open ?? []) {
  if (open.agent?.startsWith('e2e-test-') && (!process.env.AGENTS || agents.includes(open.agent))) {
    const r = await call('POST', `/api/studio/proposals/${open.number}/close`, { token: owner });
    check(`agents: open proposal #${open.number} (${open.agent}) closed`, [200, 204].includes(r.status), `HTTP ${r.status}`);
  }
}
const gone = files.filter(f => agents.includes(f.path.split('/')[1]));
if (gone.length) {
  const branch = `e2e-cleanup-${Date.now()}`;
  const change = await gitea('POST', `${REPO}/contents`, {
    branch: 'main', new_branch: branch, message: `Remove ${agents.join(', ')} (e2e cleanup)`,
    files: gone.map(f => ({ operation: 'delete', path: f.path, sha: f.sha })),
  });
  const pr = await gitea('POST', `${REPO}/pulls`, { base: 'main', head: branch, title: `Remove ${agents.join(', ')} (e2e cleanup)` });
  let merged;
  for (let i = 0; i < 6 && pr.json?.number; i++) {
    merged = await gitea('POST', `${REPO}/pulls/${pr.json?.number}/merge`, { Do: 'merge', delete_branch_after_merge: true });
    if (merged.status !== 405) break;
    await new Promise(r => setTimeout(r, 1000 * (i + 1)));
  }
  check(`agents: ${agents.join(', ')} removed from main (PR #${pr.json?.number})`, change.status === 201 && [200, 204].includes(merged.status), `change ${change.status}, merge ${merged.status}`);
  const confirm = await call('POST', '/api/studio/confirm', { token: owner, timeout: 120000 });
  check('agents: the owner confirms the removal pass (POST /api/studio/confirm)', [200, 202, 204].includes(confirm.status), `HTTP ${confirm.status}`);
}
const left = await until(() => {
  const list = kubectlJson('-n', NAMESPACE, 'get', 'agents')?.items ?? [];
  return list.some(a => agents.includes(a.metadata.name)) ? null : list;
}, 180000, 5000);
check(`agents: ${agents.join(', ') || 'no e2e-test-* agent'} gone from kagent`, !!left, (kubectlJson('-n', NAMESPACE, 'get', 'agents')?.items ?? []).map(a => a.metadata.name).join(', '));

// The rest only on a full clean (AGENTS unset): studio-cicd.sh removes its own agent and keeps the people.
if (process.env.AGENTS) finish('day2-clean');

// Conversations, as their owner.
const people = readEnvFile(stateFile('people.env')) ?? {};
if (people.employee) {
  const employee = await signIn(PEOPLE.employee, people.employee);
  const list = (await call('GET', '/api/chat/conversations', { token: employee })).json;
  const conversations = Array.isArray(list) ? list : list?.conversations ?? list?.items ?? [];
  let deleted = 0;
  for (const c of conversations) {
    const r = await call('DELETE', `/api/chat/conversations/${c.id ?? c.conversationId}`, { token: employee });
    if ([200, 204].includes(r.status)) deleted++;
  }
  check('chat: the employee\'s e2e conversations deleted', deleted === conversations.length, `${deleted}/${conversations.length}`);
}

// Cards: stale (company scope, as the owner).
for (const key of ['e2e-test-refund-policy', 'e2e-test-escalation']) {
  const card = await call('GET', `/api/memory/cards/company/${key}`, { token: owner });
  if (card.status !== 200) continue;
  if (card.json.status === 'stale') { note(`company/${key} already stale`); continue; }
  const r = await call('POST', `/api/memory/cards/company/${key}/status`, { token: owner, body: { version: card.json.version, status: 'stale' } });
  check(`cards: company/${key} marked stale`, r.status === 200, `HTTP ${r.status}`);
}

// Models.
const models = (await call('GET', '/api/models', { token: owner })).json?.models ?? [];
for (const m of models.filter(x => x.name.startsWith('e2e-test-'))) {
  const r = await call('DELETE', `/api/models/${m.namespace}/${m.name}`, { token: owner, body: { inUseConfirmed: true } });
  check(`models: ${m.name} deleted`, [200, 204].includes(r.status), `HTTP ${r.status}`);
}

// People.
const everyone = (await call('GET', '/api/identity/people', { token: owner })).json;
const list = Array.isArray(everyone) ? everyone : everyone?.people ?? everyone?.items ?? [];
for (const p of list.filter(x => /^e2e-test-/.test(x.email) && !x.disabled)) {
  const r = await call('POST', `/api/identity/people/${p.id}/disable`, { token: owner });
  check(`people: ${p.email} disabled`, [200, 204].includes(r.status), `HTTP ${r.status}`);
}
finish('day2-clean');
