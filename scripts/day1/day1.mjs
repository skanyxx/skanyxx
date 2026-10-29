// Day 1: the owner's integrations, verified through Skanyxx: git (the agent repo), CI (the workflow in the repo and
// its settings), Argo CD (the Application syncing the repo) and model providers (/api/models). Run by day1.sh.
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { BASE, NAMESPACE, call, check, finish, kubectlJson, note, ownerToken, stateFile, until, ensurePerson } from '../e2e/skx.mjs';

const { HOST_IP, OLLAMA_MODEL, OLLAMA_PORT, MODEL_KEY_FILE, CI, ARGO, GITEA_LOCAL, GITEA_TOKEN_FILE, UI_PORT, VERIFY_ONLY } = process.env;
const owner = await ownerToken();
const gitToken = fs.readFileSync(GITEA_TOKEN_FILE, 'utf8');
const gitea = (method, path, body) => call(method, `${GITEA_LOCAL}/api/v1${path}`, { body, headers: { Authorization: `token ${gitToken}` } });
const REPO = '/repos/skanyxx/skanyxx-agents';

// --- Git: the reconciler creates the private repo with main protected on its first pass.
note(`Skanyxx at ${BASE}`);
const studio = await until(async () => {
  const r = await call('GET', '/api/studio', { token: owner });
  return r.status === 200 && r.json?.repo && !r.json?.repoProblem ? r : null;
}, 180000, 5000);
check('git: Studio has its agent repo, no repo problem (GET /api/studio)', !!studio, studio ? `repo ${studio.json.repo}` : 'repo not ready');
const repo = await gitea('GET', REPO);
check('git: skanyxx/skanyxx-agents exists in the Gitea container and is private', repo.status === 200 && repo.json?.private === true, `HTTP ${repo.status}`);
const protection = await gitea('GET', `${REPO}/branch_protections`);
check('git: main is protected', protection.status === 200 && protection.json?.some(p => (p.rule_name || p.branch_name) === 'main'), `HTTP ${protection.status}`);
const health = await call('GET', '/health');
check('/health is Healthy with the studio on', health.json?.status === 'Healthy', JSON.stringify(health.json?.entries));

// --- CI: the owner's "Add CI to the agent repo", then the repo's variable and secret for the workflow.
if (CI !== 'off') {
  if (!VERIFY_ONLY) {
    const install = await call('POST', '/api/studio/ci/install', { token: owner });
    check('CI: owner adds the workflow (POST /api/studio/ci/install → 200/201, or 409 already there)', [200, 201, 409].includes(install.status), `HTTP ${install.status}`);
    const actions = await gitea('PATCH', REPO, { has_actions: true });
    check('CI: Actions enabled on the repo', actions.status === 200, `HTTP ${actions.status}`);
    const url = `http://host.docker.internal:${UI_PORT}`;
    let v = await gitea('POST', `${REPO}/actions/variables/SKANYXX_URL`, { value: url });
    if (v.status === 409 || v.status === 400) v = await gitea('PUT', `${REPO}/actions/variables/SKANYXX_URL`, { value: url });
    check('CI: repo variable SKANYXX_URL', [200, 201, 204].includes(v.status), `HTTP ${v.status} ${url}`);
    const s = await gitea('PUT', `${REPO}/actions/secrets/SKANYXX_CI_TOKEN`, { data: fs.readFileSync(stateFile('ci-token'), 'utf8') });
    check('CI: repo secret SKANYXX_CI_TOKEN (value from $STATE/ci-token)', [201, 204].includes(s.status), `HTTP ${s.status}`);
  }
  const wf = await gitea('GET', `${REPO}/contents/.gitea/workflows/validate-agents.yaml?ref=main`);
  check('CI: .gitea/workflows/validate-agents.yaml is in main', wf.status === 200, `HTTP ${wf.status}`);
  const anon = await call('POST', '/api/studio/ci/validate', { body: { name: 'x', agentYaml: '', grantsYaml: '' }, headers: { Authorization: 'Bearer wrong' } });
  check('CI: /api/studio/ci/validate refuses a wrong CI token (401)', anon.status === 401, `HTTP ${anon.status}`);
  const ok = await call('POST', '/api/studio/ci/validate', {
    body: { name: 'e2e-test-ci', agentYaml: 'x: 1', grantsYaml: 'agent: e2e-test-ci' },
    headers: { Authorization: `Bearer ${fs.readFileSync(stateFile('ci-token'), 'utf8')}` },
  });
  check('CI: the CI token is accepted and a bad folder is invalid', ok.status === 200 && ok.json?.valid === false, `HTTP ${ok.status}, ${ok.json?.problems?.length ?? 0} problems`);
  const builder = await ensurePerson(owner, 'builder');
  const tmpl = await call('GET', '/api/studio/ci-template', { token: builder });
  check('CI: a builder reads the workflow template (GET /api/studio/ci-template)', tmpl.status === 200, `HTTP ${tmpl.status}`);
  const refused = await call('POST', '/api/studio/ci/install', { token: builder });
  check('CI: a builder may not install CI (403)', refused.status === 403, `HTTP ${refused.status}`);
}

// --- Argo CD: the Application syncs the repo (empty agents/ is fine); Skanyxx reads it with its read-only token.
if (ARGO === 'on') {
  const app = await until(() => {
    const a = kubectlJson('-n', 'argocd', 'get', 'application', 'skanyxx-agents');
    return a?.status?.sync?.status === 'Synced' && a?.status?.health?.status === 'Healthy' ? a : null;
  }, 300000, 5000);
  const last = app ?? kubectlJson('-n', 'argocd', 'get', 'application', 'skanyxx-agents');
  check('Argo CD: Application skanyxx-agents is Synced and Healthy on the agent repo', !!app,
    `sync ${last?.status?.sync?.status}, health ${last?.status?.health?.status}, revision ${last?.status?.sync?.revision?.slice(0, 8) ?? '-'}${last?.status?.conditions ? ', ' + last.status.conditions.map(c => c.type).join(',') : ''}`);
  // D544: as Argo's controller, the studio's shape is admitted and a deployment override is refused (server dry runs).
  const asArgo = agent => {
    try {
      execFileSync('kubectl', ['--context', process.env.SKX_CONTEXT, 'create', '--dry-run=server', '-f', '-',
        '--as=system:serviceaccount:argocd:argocd-application-controller'], { input: JSON.stringify(agent), stdio: ['pipe', 'pipe', 'pipe'] });
      return 'admitted';
    } catch (e) { return /denied request/.test(e.stderr?.toString() ?? '') ? 'denied' : 'error'; }
  };
  const shape = {
    apiVersion: 'kagent.dev/v1alpha2', kind: 'Agent', metadata: { name: 'e2e-test-policy', namespace: NAMESPACE },
    spec: { type: 'Declarative', description: 'd', declarative: { modelConfig: 'default-model-config', systemMessage: 's',
      tools: [{ type: 'McpServer', mcpServer: { name: 'skanyxx-memory-e2e-test-policy', kind: 'RemoteMCPServer', apiGroup: 'kagent.dev', toolNames: ['memory_search'] } }] } },
  };
  const override = structuredClone(shape);
  override.spec.declarative.deployment = { replicas: 1, serviceAccountName: 'default' };
  const headers = structuredClone(shape);
  headers.spec.declarative.tools[0].headersFrom = [{ name: 'Authorization', valueFrom: { type: 'Secret', name: 'any', key: 'token' } }];
  const verdicts = [asArgo(shape), asArgo(override), asArgo(headers)];
  check('Argo CD: from git only the studio\'s Agent shape is admitted (deployment and secret headers refused)',
    verdicts.join(',') === 'admitted,denied,denied', verdicts.join(', '));
  const env = kubectlJson('-n', NAMESPACE, 'get', 'deploy', process.env.SKX_RELEASE || 'skanyxx')?.spec?.template?.spec?.containers?.[0]?.env ?? [];
  check('Argo CD: Skanyxx is configured with Studio:Argo:BaseUrl (status reads, D363)', env.some(e => e.name === 'Studio__Argo__BaseUrl'), env.find(e => e.name === 'Studio__Argo__BaseUrl')?.value ?? 'unset');
}

// --- Models through Skanyxx: the default (Ollama on this machine) and an e2e-test-* one with a key from a file.
// Ollama by name: it answers 403 to a Host header that is a public-range IP (OrbStack's 0.250.250.254), and pods resolve
// host.docker.internal (the NetworkPolicy admits its address).
const options = await call('GET', '/api/models/options', { token: owner });
const providers = (options.json?.providers ?? []).map(p => p.type);
check('models: provider options from kagent (GET /api/models/options)', options.status === 200 && providers.includes('Ollama') && providers.includes('OpenAI'), providers.join(', '));
async function saveModel(name, form) {
  let r = await call('POST', '/api/models', { token: owner, body: { namespace: NAMESPACE, name, ...form } });
  if (r.status === 409) r = await call('PUT', `/api/models/${NAMESPACE}/${name}`, { token: owner, body: { namespace: NAMESPACE, name, ...form } });
  return r;
}
const key = fs.readFileSync(MODEL_KEY_FILE, 'utf8').trim();
if (!VERIFY_ONLY) {
  const def = await saveModel('default-model-config', { provider: 'Ollama', model: OLLAMA_MODEL, params: { host: `http://host.docker.internal:${OLLAMA_PORT}` } });
  check(`models: default-model-config = Ollama ${OLLAMA_MODEL} (POST/PUT /api/models)`, [200, 201].includes(def.status), `HTTP ${def.status}`);
  const keyed = await saveModel('e2e-test-openai', { provider: 'OpenAI', model: 'gpt-4o-mini', apiKey: key });
  check('models: e2e-test-openai with an API key from a file (never echoed)', [200, 201].includes(keyed.status) && !keyed.text.includes(key), `HTTP ${keyed.status}`);
}
const list = await until(async () => {
  const r = await call('GET', '/api/models', { token: owner });
  const want = ['default-model-config', 'e2e-test-openai'].map(n => r.json?.models?.find(m => m.name === n));
  return want.every(m => m?.accepted) ? r : null;
}, 120000, 4000) ?? await call('GET', '/api/models', { token: owner });
for (const name of ['default-model-config', 'e2e-test-openai']) {
  const m = list.json?.models?.find(x => x.name === name);
  check(`models: ${name} accepted by kagent (healthy in /Model)`, m?.accepted === true, m ? `${m.provider} ${m.model}, accepted ${m.accepted}${m.statusMessage ? ', ' + m.statusMessage : ''}` : 'missing');
}
check('models: the default is default-model-config', list.json?.defaultRef === `${NAMESPACE}/default-model-config`, list.json?.defaultRef);
const one = await call('GET', `/api/models/${NAMESPACE}/e2e-test-openai`, { token: owner });
check('models: the API key is write-only (hasApiKey, never the value, in list and detail)',
  one.json?.hasApiKey === true && !one.text.includes(key) && !list.text.includes(key), `hasApiKey ${one.json?.hasApiKey}`);
const supervisor = await ensurePerson(owner, 'supervisor');
const supRead = await call('GET', '/api/models', { token: supervisor });
const supWrite = await call('POST', '/api/models', { token: supervisor, body: { namespace: NAMESPACE, name: 'e2e-test-denied', provider: 'Ollama', model: 'x', params: { host: 'http://x:1' } } });
check('models: a supervisor reads (200) but may not write (403)', supRead.status === 200 && supWrite.status === 403, `GET ${supRead.status}, POST ${supWrite.status}`);

finish('day1');
