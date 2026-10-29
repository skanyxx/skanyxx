// Day 2 (requirements §5): people use what Day 1 connected. A supervisor adds the knowledge base (company cards), a
// builder makes an agent from a template (instructions, a skill, a company grant) as a pull request, CI checks it,
// a supervisor merges it, Argo CD (or the reconciler) deploys it to kagent, Studio shows it deployed, an employee
// chats with it and finds the chat in their history; RBAC refusals on the way. Run by day2.sh (and e2e/studio-cicd.sh).
//
// Env: AGENT (e2e-test-support), STAGES (kb,studio,chat,ui; studio-cicd.sh runs "studio"), CI (runner|emulate|off),
// ARGO (on|off), SKILL_REF (a digest-pinned skill from the allow-listed registry, empty = none), GITEA_LOCAL,
// GITEA_TOKEN_FILE, SHOTS (screenshot folder), PLAYWRIGHT_MODULE (path of a playwright package; empty = no UI step).
import fs from 'node:fs';
import { createRequire } from 'node:module';
import { BASE, NAMESPACE, call, check, finish, kubectlJson, note, ownerToken, ensurePerson, stateFile, until, sleep } from '../e2e/skx.mjs';

const AGENT = process.env.AGENT || 'e2e-test-support';
const STAGES = (process.env.STAGES || 'kb,studio,chat,ui').split(',');
const { CI = 'runner', ARGO = 'on', SKILL_REF = '', GITEA_LOCAL, GITEA_TOKEN_FILE, SHOTS, PLAYWRIGHT_MODULE } = process.env;
const STEP = process.env.STEP_NAME || 'day2';
const gitToken = fs.readFileSync(GITEA_TOKEN_FILE, 'utf8');
const gitea = (method, path, body) => call(method, `${GITEA_LOCAL}/api/v1${path}`, { body, headers: { Authorization: `token ${gitToken}` } });
const REPO = '/repos/skanyxx/skanyxx-agents';
// Facts only the knowledge base knows: the agent can answer them only by searching company memory.
const REFUND_DAYS = '37';
const CODE_WORD = 'PELICAN-42';

note(`Skanyxx at ${BASE}; agent ${AGENT}; stages ${STAGES.join(',')}; CI ${CI}; Argo ${ARGO}`);
const owner = await ownerToken();
const supervisor = await ensurePerson(owner, 'supervisor');
const builder = await ensurePerson(owner, 'builder');
const employee = await ensurePerson(owner, 'employee');
check('people: supervisor, builder and employee invited and signed in', supervisor && builder && employee);

// ---------------------------------------------------------------- knowledge base
if (STAGES.includes('kb')) {
  const cards = [
    { key: 'e2e-test-refund-policy', type: 'fact', what: 'Refund window of E2E Corp',
      why: 'Support answers refund questions from this card.',
      body: `E2E Corp accepts refunds within ${REFUND_DAYS} days of purchase. After ${REFUND_DAYS} days only a store credit is offered.`,
      keywords: ['refund', 'e2e-test'] },
    { key: 'e2e-test-escalation', type: 'procedure', what: 'How support escalates a refund exception',
      why: 'The escalation path for refunds outside the window.',
      body: `# Escalation (document)\n\n1. Open a ticket in the Refunds queue.\n2. Write the code word ${CODE_WORD} in the first line.\n3. The support supervisor approves or declines within one business day.`,
      keywords: ['refund', 'escalation', 'e2e-test'] },
  ];
  const denied = await call('PUT', `/api/memory/cards/company/e2e-test-denied`, { token: employee, body: { version: 0, type: 'fact', what: 'x', why: 'x' } });
  check('kb: an employee may not write company cards (403)', denied.status === 403, `HTTP ${denied.status}`);
  for (const card of cards) {
    const current = await call('GET', `/api/memory/cards/company/${card.key}`, { token: supervisor });
    const version = current.status === 200 ? current.json.version : 0;
    const r = await call('PUT', `/api/memory/cards/company/${card.key}`, { token: supervisor, body: { ...card, version, key: undefined } });
    check(`kb: supervisor writes company/${card.key} (v${version} → v${r.json?.version})`, [200, 201].includes(r.status), `HTTP ${r.status}`);
  }
  const lib = await call('GET', '/api/memory/library?q=refund', { token: employee });
  const keys = JSON.stringify(lib.json ?? {});
  check('kb: an employee finds both cards in the Library', lib.status === 200 && cards.every(c => keys.includes(c.key)), `HTTP ${lib.status}, total ${lib.json?.total}`);
  const history = await call('GET', '/api/memory/cards/company/e2e-test-refund-policy/history', { token: employee });
  check('kb: the card keeps its versions (history)', history.status === 200, `HTTP ${history.status}, ${Array.isArray(history.json) ? history.json.length : history.json?.versions?.length ?? '?'} versions`);
}

// ---------------------------------------------------------------- studio: template → PR → CI → merge → deploy
if (STAGES.includes('studio')) {
  for (const [who, token, method, path, body] of [
    ['employee', employee, 'GET', '/api/studio/templates'],
    ['employee', employee, 'POST', '/api/studio/proposals', {}],
    ['builder', builder, 'POST', '/api/studio/apply', {}],
    ['builder', builder, 'GET', '/api/models'],
  ]) {
    const r = await call(method, path, { token, body });
    check(`rbac: ${who} ${method} ${path} → 403`, r.status === 403, `HTTP ${r.status}`);
  }

  const templates = await call('GET', '/api/studio/templates', { token: builder });
  const support = templates.json?.find?.(t => t.id === 'support');
  check('studio: a builder lists the templates and finds "support"', templates.status === 200 && !!support, `HTTP ${templates.status}, ${templates.json?.map?.(t => t.id).join(', ')}`);
  const draft = {
    ...support.draft,
    name: AGENT,
    description: 'E2E support agent: answers refund questions from company memory.',
    instructions: `${support.draft.instructions}\n- You do not know E2E Corp's policies. Before answering any refund or escalation question, call memory_search and answer only from the cards it returns, quoting numbers and code words exactly.\n- Answer in at most three sentences.`,
    skills: SKILL_REF ? [SKILL_REF] : [],
    grants: [{ scope: 'company', search: true, upsert: false }],
  };
  const preview = await call('POST', '/api/studio/manifest', { token: builder, body: draft });
  const agentYaml = preview.json?.agentYaml ?? '';
  check('studio: the YAML preview has no problems and carries instructions, skill and memory tool',
    preview.status === 200 && (preview.json?.problems ?? []).length === 0 && agentYaml.includes('memory_search') && (!SKILL_REF || agentYaml.includes(SKILL_REF)),
    `HTTP ${preview.status}, problems ${JSON.stringify(preview.json?.problems ?? [])}`);

  // Propose, or pick up the open proposal of an earlier run.
  let number = (await call('GET', '/api/studio', { token: builder })).json?.open?.find(p => p.agent === AGENT)?.number;
  if (!number) {
    const proposed = await call('POST', '/api/studio/proposals', { token: builder, body: draft });
    check('studio: the builder saves the form as a pull request (201)', proposed.status === 201, `HTTP ${proposed.status} ${proposed.json?.message ?? proposed.json?.detail ?? ''}`);
    number = proposed.json?.number;
  } else note(`open proposal #${number} from an earlier run`);
  const files = await gitea('GET', `${REPO}/pulls/${number}/files`);
  check(`studio: PR #${number} in the agent repo changes exactly agents/${AGENT}/{agent,grants}.yaml`,
    files.status === 200 && JSON.stringify(files.json?.map(f => f.filename).sort()) === JSON.stringify([`agents/${AGENT}/agent.yaml`, `agents/${AGENT}/grants.yaml`]),
    files.json?.map?.(f => f.filename).join(', '));
  const builderMerge = await call('POST', `/api/studio/proposals/${number}/merge`, { token: builder });
  check('rbac: the builder may not merge (403)', builderMerge.status === 403, `HTTP ${builderMerge.status}`);

  // CI: the shipped workflow on a runner, or (emulate) the same API call plus the status it would post.
  let proposal = (await call('GET', `/api/studio/proposals/${number}`, { token: builder })).json;
  if (CI === 'emulate' && proposal?.checks?.state !== 'Passed') {
    const head = (await gitea('GET', `${REPO}/pulls/${number}`)).json?.head?.sha;
    const body = { name: AGENT, agentYaml, grantsYaml: preview.json.grantsYaml };
    const v = await call('POST', '/api/studio/ci/validate', { body, headers: { Authorization: `Bearer ${fs.readFileSync(stateFile('ci-token'), 'utf8')}` } });
    const st = await gitea('POST', `${REPO}/statuses/${head}`, { state: v.json?.valid ? 'success' : 'failure', context: 'validate-agents / validate (pull_request)', description: 'emulated by scripts/day2 (no runner)' });
    check('CI (emulated): the workflow\'s check through /api/studio/ci/validate, posted as a commit status', v.json?.valid === true && st.status === 201, `valid ${v.json?.valid}, status HTTP ${st.status}`);
  }
  if (CI !== 'off') {
    if (proposal?.checks?.state !== 'Passed') {
      const early = await call('POST', `/api/studio/proposals/${number}/merge`, { token: supervisor });
      if (early.status === 409) check('studio: a merge before CI passed is refused (409 checks, Studio:RequireChecks)', true, early.json?.detail ?? early.json?.message);
      else note(`early merge answered ${early.status} (checks already passed)`);
    }
    const t0 = Date.now();
    proposal = await until(async () => {
      const p = (await call('GET', `/api/studio/proposals/${number}`, { token: builder })).json;
      return ['Passed', 'Failed'].includes(p?.checks?.state) ? p : null;
    }, 900000, 10000) ?? (await call('GET', `/api/studio/proposals/${number}`, { token: builder })).json;
    check(`CI: checks passed on the pull request's head (${CI})`, proposal?.checks?.state === 'Passed',
      `${proposal?.checks?.state} after ${Math.round((Date.now() - t0) / 1000)} s: ${proposal?.checks?.runs?.map(r => `${r.name}=${r.state}`).join(', ')}`);
  }

  const merged = await call('POST', `/api/studio/proposals/${number}/merge`, { token: supervisor, timeout: 120000 });
  check('studio: the supervisor merges (200 live / 202 merged)', [200, 202].includes(merged.status), `HTTP ${merged.status} ${merged.json?.message ?? merged.json?.detail ?? ''}`);

  // Deployed: kagent has the agent (labelled merged), Argo has it Synced+Healthy, Studio's timeline says Deployed.
  const agent = await until(() => {
    const a = kubectlJson('-n', NAMESPACE, 'get', 'agent', AGENT);
    return a?.status?.conditions?.some(c => c.type === 'Ready' && c.status === 'True') ? a : null;
  }, 600000, 5000) ?? kubectlJson('-n', NAMESPACE, 'get', 'agent', AGENT);
  check(`deploy: kagent Agent ${AGENT} is Ready (its skill pulled by skills-init)`, agent?.status?.conditions?.some(c => c.type === 'Ready' && c.status === 'True'),
    agent?.status?.conditions?.map(c => `${c.type}=${c.status}`).join(', ') ?? 'missing');
  check('deploy: labelled merged and managed by the studio', agent?.metadata?.labels?.['skanyxx.dev/merged'] === 'true' && agent?.metadata?.labels?.['skanyxx.dev/managed-by'] === 'skanyxx-studio',
    JSON.stringify(agent?.metadata?.labels ?? {}));
  if (SKILL_REF) check('deploy: the skill is on the Agent (spec.skills.refs)', agent?.spec?.skills?.refs?.includes(SKILL_REF), JSON.stringify(agent?.spec?.skills?.refs ?? []));
  if (ARGO === 'on') {
    // Through Argo's API with Skanyxx's read-only token: Argo CD 3.x keeps resource health out of the CR's status.
    const argoApp = async () => (await call('GET', `${process.env.ARGO_URL}/api/v1/applications/skanyxx-agents`, { token: fs.readFileSync(stateFile('argo-token'), 'utf8') })).json;
    const res = await until(async () => {
      const app = await argoApp();
      const r = app?.status?.resources?.find(x => x.kind === 'Agent' && x.name === AGENT);
      return r?.status === 'Synced' && r?.health?.status === 'Healthy' ? { app, r } : null;
    }, 300000, 5000);
    const app = res?.app ?? await argoApp();
    const r = res?.r ?? app?.status?.resources?.find(x => x.kind === 'Agent' && x.name === AGENT);
    check(`deploy: Argo CD Application tracks Agent ${AGENT} Synced + Healthy`, !!res, `resource ${r?.status}/${r?.health?.status}, app ${app?.status?.sync?.status}/${app?.status?.health?.status}`);
  }
  const change = await until(async () => {
    const c = (await call('GET', '/api/studio/changes', { token: builder })).json;
    const list = Array.isArray(c) ? c : c?.changes ?? [];
    const mine = list.find(x => x.number === number);
    return mine?.deploy?.state === 'Healthy' ? mine : null;
  }, 300000, 5000);
  check(`studio: the change timeline says Deployed (${ARGO === 'on' ? 'from Argo CD' : 'from the reconciler'})`, !!change && (ARGO !== 'on' || /argo/i.test(change.deploy.source)),
    change ? `${change.steps.map(s => `${s.name}:${s.state}`).join(' → ')}; deploy ${change.deploy.state} (${change.deploy.source}: ${change.deploy.detail})` : 'not deployed in time');
}

// ---------------------------------------------------------------- chat + history as the employee
if (STAGES.includes('chat')) {
  const listed = await until(async () => {
    const r = await call('GET', '/api/chat/agents', { token: employee });
    return (r.json ?? []).some(a => a.name === AGENT) ? r : null;
  }, 120000, 5000);
  check(`chat: the employee sees ${AGENT} in Chat (merged agents only)`, !!listed, listed ? listed.json.map(a => a.name).join(', ') : 'not listed');
  const ask = (message, conversationId) => call('POST', '/api/chat', {
    token: employee, timeout: 330000,
    body: { message, agentNamespace: NAMESPACE, agentName: AGENT, conversationId, noCache: true },
  });
  const t1 = await ask('Search company memory, then tell me: what is our refund window in days, and which code word goes into an escalation ticket?');
  const a1 = t1.json?.message?.content ?? '';
  check('chat: the agent answers from the knowledge base (refund window and code word)', t1.status === 200 && a1.includes(REFUND_DAYS) && a1.includes(CODE_WORD),
    `HTTP ${t1.status}: ${a1.replace(/\s+/g, ' ').slice(0, 220)}`);
  const conversation = t1.json?.conversationId;
  const t2 = await ask('Who approves the exception, and how fast?', conversation);
  check('chat: a second turn continues the same conversation', t2.status === 200 && t2.json?.conversationId === conversation,
    `HTTP ${t2.status}: ${(t2.json?.message?.content ?? '').replace(/\s+/g, ' ').slice(0, 160)}`);
  const history = await call('GET', '/api/chat/conversations', { token: employee });
  const entries = Array.isArray(history.json) ? history.json : history.json?.conversations ?? history.json?.items ?? [];
  check('history: the conversation is in the employee\'s history', history.status === 200 && JSON.stringify(entries).includes(conversation), `HTTP ${history.status}, ${entries.length} conversations`);
  const one = await call('GET', `/api/chat/conversations/${conversation}`, { token: employee });
  const items = one.json?.items ?? [];
  check('history: reopening it returns both turns', one.status === 200 && items.length >= 4, `HTTP ${one.status}, ${items.length} items`);
  const other = await call('GET', `/api/chat/conversations/${conversation}`, { token: builder });
  check('rbac: another person cannot read it (404/403)', [403, 404].includes(other.status), `HTTP ${other.status}`);
  fs.writeFileSync(stateFile(`${STEP}-conversation`), conversation ?? '', { mode: 0o600 });
}

// ---------------------------------------------------------------- UI evidence (Playwright, real Chrome)
if (STAGES.includes('ui') && PLAYWRIGHT_MODULE) {
  const { chromium } = createRequire(import.meta.url)(PLAYWRIGHT_MODULE);
  const people = Object.fromEntries(fs.readFileSync(stateFile('people.env'), 'utf8').trim().split('\n').map(l => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const page = async (email, password) => {
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const p = await ctx.newPage();
    await p.goto(BASE + '/Login', { waitUntil: 'networkidle' });
    await p.fill('input[type=email]', email);
    await p.fill('input[type=password]', password);
    await Promise.all([p.waitForLoadState('networkidle'), p.click('button[type=submit]')]);
    return p;
  };
  fs.mkdirSync(SHOTS, { recursive: true });
  const b = await page('e2e-test-builder@e2e.test', people.builder);
  await b.goto(BASE + '/Studio', { waitUntil: 'networkidle' });
  await sleep(2500);
  const studioText = (await b.locator('body').innerText()).replace(/\s+/g, ' ');
  await b.screenshot({ path: `${SHOTS}/${STEP}-studio-builder.png`, fullPage: true });
  check('ui: /Studio shows the change and its deployment', studioText.includes(AGENT) && /Deployed/i.test(studioText), studioText.match(/Changes and deployments.{0,200}/)?.[0] ?? '');
  const e = await page('e2e-test-employee@e2e.test', people.employee);
  await e.goto(BASE + '/Chat', { waitUntil: 'networkidle' });
  await sleep(2000);
  const chatText = (await e.locator('body').innerText()).replace(/\s+/g, ' ');
  await e.screenshot({ path: `${SHOTS}/${STEP}-chat-employee.png`, fullPage: true });
  check('ui: the employee\'s Chat offers the agent, with its skill and memory access in the details',
    chatText.includes(`${NAMESPACE}/${AGENT}`) && (!SKILL_REF || chatText.includes(SKILL_REF.split('@')[0])) && /Memory access company search/.test(chatText),
    chatText.match(/Agent details.{0,240}/)?.[0] ?? '');
  const nav = await e.locator('nav a.nav-item').evaluateAll(as => as.map(a => a.getAttribute('href')));
  check('ui: the employee\'s menu has no Studio, Models or People', !nav.some(h => /Studio|Model|People/.test(h ?? '')), nav.join(' '));
  await browser.close();
} else if (STAGES.includes('ui')) note('ui step skipped: PLAYWRIGHT_MODULE is not set');

finish(STEP);
