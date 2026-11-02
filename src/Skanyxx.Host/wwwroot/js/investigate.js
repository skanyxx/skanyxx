/*
 * Investigate (D340): workflows as a step tree anyone can edit with buttons (add, move up/down, remove, pick an agent),
 * runs with live per-step progress, history, and the report downloads. Every value from the API is set with
 * textContent; the server decides who may see or change what (D341), the page only hides buttons that would be refused.
 */
(() => {
    'use strict';

    const api = '/api/investigate';
    const root = document.getElementById('investigate');
    if (!root) return;
    const isSupervisor = root.dataset.supervisor === 'true';

    let options = { agents: [], agentsAvailable: true, teams: [], limits: { maxDepth: 4, maxSteps: 40, maxPromptChars: 8000, maxInputChars: 20000, stepTimeoutSeconds: 300 } };
    let workflows = null;
    let editing = null;      // { id, steps } while the editor is open
    let starting = null;     // the workflow being started
    let watching = null;     // the run open in the run panel
    let pollTimer = null;

    const $ = id => document.getElementById(id);
    const el = (tag, cls, text) => {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined && text !== null) e.textContent = text;
        return e;
    };
    const button = (label, onClick, cls = 'btn btn-secondary btn-sm', iconId) => {
        const b = el('button', cls);
        b.type = 'button';
        if (iconId) b.append(skx.icon(iconId));
        b.append(document.createTextNode(label));
        b.addEventListener('click', onClick);
        return b;
    };
    const showErrors = (box, list) => {
        box.replaceChildren(...list.map(m => el('div', null, m)));
        box.hidden = list.length === 0;
    };
    const errorList = err => (err.data && err.data.errors ? Object.values(err.data.errors).flat() : [err.message]);
    const stateLabel = s => ({ queued: 'Queued', running: 'Running', succeeded: 'Succeeded', failed: 'Failed', cancelled: 'Cancelled', pending: 'Pending', skipped: 'Skipped' })[s] || s;
    const duration = (start, end) => {
        if (!start) return '';
        const ms = (end ? new Date(end) : new Date()) - new Date(start);
        const s = Math.max(0, Math.round(ms / 100) / 10);
        return s < 60 ? `${s.toFixed(1)} s` : `${Math.floor(s / 60)} min ${Math.round(s % 60)} s`;
    };
    const agentLabel = ref => {
        const a = options.agents.find(x => x.ref === ref);
        return a ? a.name + (a.namespace !== 'kagent' ? ` (${a.namespace})` : '') : ref;
    };

    // ---------- Tabs ----------
    function selectTab(which) {
        const runs = which === 'runs';
        $('invTabWorkflows').setAttribute('aria-selected', String(!runs));
        $('invTabRuns').setAttribute('aria-selected', String(runs));
        $('invTabWorkflows').tabIndex = runs ? -1 : 0;
        $('invTabRuns').tabIndex = runs ? 0 : -1;
        $('invPanelWorkflows').hidden = runs;
        $('invPanelRuns').hidden = !runs;
        if (runs) loadRuns();
    }

    // ---------- Workflows ----------
    async function loadOptions() {
        try {
            options = await skx.api('GET', `${api}/options`);
        } catch (err) {
            skx.toast('The agent list could not be loaded: ' + err.message, { tone: 'error' });
        }
        const box = $('invAgentsDown');
        box.replaceChildren();
        if (!options.agentsAvailable) {
            const alert = el('div', 'alert alert-warning');
            alert.append(skx.icon('i-alert-triangle'));
            const body = el('div', 'alert-body');
            body.append(el('div', 'alert-title', 'kagent did not answer, so no agent can be picked or run now.'), el('p', null, 'The Chat guide says what to check.'));
            alert.append(body);
            box.append(alert);
        }
        const share = $('invShare');
        share.replaceChildren(share.options[0] || el('option', null, 'Not shared'));
        share.options[0].value = '';
        options.teams.forEach(t => { const o = el('option', null, t.name); o.value = t.slug; share.append(o); });
    }

    async function loadWorkflows() {
        const box = $('invWorkflows');
        if (!workflows) skx.state.loading(box, { rows: 3 });
        try {
            workflows = await skx.api('GET', `${api}/workflows`);
            renderWorkflows();
            fillWorkflowFilter();
        } catch (err) {
            if (workflows) { skx.toast('The workflows could not be refreshed: ' + err.message, { tone: 'error' }); return; }
            skx.state.error(box, { title: 'The workflows could not be loaded', detail: err.message, retry: loadWorkflows });
        }
    }

    function renderWorkflows() {
        const q = $('invSearch').value.trim().toLowerCase();
        const shown = workflows.filter(w => !q || w.name.toLowerCase().includes(q) || w.agents.some(a => a.toLowerCase().includes(q)));
        $('invCount').textContent = `${shown.length} workflow${shown.length === 1 ? '' : 's'}${q ? ' (filtered)' : ''}`;
        const box = $('invWorkflows');
        if (shown.length === 0) {
            if (q) skx.state.empty(box, { icon: 'i-search', title: 'No workflow matches', body: 'Try another word.' });
            else skx.state.empty(box, {
                icon: 'i-search', title: 'No workflows yet', compact: false,
                body: 'A workflow is a list of agent steps that look into a question together. Start with one step and add more later.',
                action: { label: 'New workflow', onClick: () => openEditor(null) }
            });
            return;
        }
        box.replaceChildren(...shown.map(workflowCard));
    }

    function workflowCard(w) {
        const card = el('article', 'card');
        card.setAttribute('aria-label', w.name);
        const head = el('div', 'tile-head');
        const info = el('div');
        info.append(el('h2', 'tile-title', w.name));
        const badges = el('div', 'badges mt-1');
        badges.append(el('span', 'badge badge-neutral', `${w.stepCount} step${w.stepCount === 1 ? '' : 's'}`));
        if (w.sharedTeam) badges.append(el('span', 'badge badge-neutral', `Shared: ${(options.teams.find(t => t.slug === w.sharedTeam) || {}).name || w.sharedTeam}`));
        if (!w.mine) badges.append(el('span', 'badge badge-neutral', `By ${w.ownerId}`));
        info.append(badges);
        head.append(info);
        card.append(head);
        if (w.description) card.append(el('p', 'tile-sub mt-1 clamp-2', w.description));
        if (w.agents.length) card.append(el('p', 'tile-meta mt-1', 'Agents: ' + w.agents.map(agentLabel).join(', ')));
        const actions = el('div', 'tile-actions mt-2');
        actions.append(button('Run', () => openStart(w), 'btn btn-primary btn-sm'));
        actions.append(button(w.canEdit ? 'Edit' : 'View steps', () => openEditor(w)));
        actions.append(button('Duplicate', ev => duplicate(w, ev.currentTarget)));
        actions.append(button('History', () => { $('invFilterWorkflow').value = w.id; selectTab('runs'); }, 'btn btn-ghost btn-sm'));
        if (w.canEdit) actions.append(button('Delete', ev => remove(w, ev.currentTarget), 'btn btn-danger-ghost btn-sm'));
        card.append(actions);
        return card;
    }

    function fillWorkflowFilter() {
        const select = $('invFilterWorkflow');
        const current = select.value;
        select.replaceChildren(select.options[0]);
        workflows.forEach(w => { const o = el('option', null, w.name); o.value = w.id; select.append(o); });
        select.value = current;
    }

    async function duplicate(w, btn) {
        try {
            const copy = await skx.busy(btn, skx.api('POST', `${api}/workflows/${w.id}/duplicate`));
            skx.toast(`Copied as "${copy.name}".`);
            await loadWorkflows();
        } catch (err) {
            skx.toast('It could not be copied: ' + err.message, { tone: 'error' });
        }
    }

    async function remove(w, btn) {
        const ok = await skx.confirm({
            title: `Delete "${w.name}"?`, body: 'The workflow goes; its past runs and reports stay in the history.',
            confirm: 'Delete', danger: true
        });
        if (!ok) return;
        try {
            await skx.busy(btn, skx.api('DELETE', `${api}/workflows/${w.id}`));
            skx.toast(`Deleted "${w.name}".`);
            await loadWorkflows();
        } catch (err) {
            skx.toast('It could not be deleted: ' + err.message, { tone: 'error' });
        }
    }

    // ---------- Editor: a tree of steps ----------
    function allSteps(steps, out = []) {
        steps.forEach(s => { out.push(s); allSteps(s.steps || [], out); });
        return out;
    }

    function nextId() {
        const n = allSteps(editing.steps).map(s => /^s(\d+)$/.exec(s.id)).filter(Boolean).map(m => Number(m[1]));
        return 's' + ((n.length ? Math.max(...n) : 0) + 1);
    }

    function newStep(kind) {
        return kind === 'group'
            ? { id: nextId(), title: 'Group', kind: 'group', mode: 'parallel', agent: null, prompt: null, steps: [] }
            : { id: nextId(), title: 'Step', kind: 'agent', mode: 'sequential', agent: options.agents.length ? options.agents[0].ref : null, prompt: '{{input}}', steps: [] };
    }

    async function openEditor(summary) {
        showErrors($('invEditorErrors'), []);
        $('invEditorForm').reset();
        let workflow = { name: '', description: '', sharedTeam: null, steps: [], canEdit: true, allowChainedWrites: false };
        if (summary) {
            try {
                workflow = await skx.api('GET', `${api}/workflows/${summary.id}`);
            } catch (err) {
                skx.toast('The workflow could not be opened: ' + err.message, { tone: 'error' });
                return;
            }
        }
        editing = { id: summary ? summary.id : null, steps: workflow.steps, readOnly: !workflow.canEdit };
        if (!summary) editing.steps.push(newStep('agent'));
        $('invEditorTitle').textContent = summary ? (workflow.canEdit ? `Edit ${workflow.name}` : workflow.name) : 'New workflow';
        $('invName').value = workflow.name;
        $('invDescription').value = workflow.description;
        $('invShare').value = workflow.sharedTeam || '';
        if (workflow.sharedTeam && $('invShare').value !== workflow.sharedTeam) {
            const o = el('option', null, workflow.sharedTeam);
            o.value = workflow.sharedTeam;
            $('invShare').append(o);
            $('invShare').value = workflow.sharedTeam;
        }
        if ($('invChainedWrites')) $('invChainedWrites').checked = !!workflow.allowChainedWrites;
        ['invName', 'invDescription', 'invShare', 'invAddStep', 'invAddGroup', 'invChainedWrites'].forEach(id => { if ($(id)) $(id).disabled = editing.readOnly; });
        $('invEditorSave').hidden = editing.readOnly;
        $('invEditorCancel').textContent = editing.readOnly ? 'Close' : 'Cancel';
        renderTree();
        $('invEditor').showModal();
        if (!editing.readOnly) $('invName').focus();
    }

    function renderTree() {
        $('invTree').replaceChildren(...editing.steps.map((s, i) => node(s, editing.steps, i, 1)));
        const count = allSteps(editing.steps).length;
        const full = count >= options.limits.maxSteps;
        $('invStepCount').textContent = `${count} of at most ${options.limits.maxSteps} steps`;
        $('invAddStep').disabled = editing.readOnly || full;
        $('invAddGroup').disabled = editing.readOnly || full || options.limits.maxDepth < 2;
    }

    function node(step, siblings, index, depth) {
        const li = el('li', 'inv-node subcard');
        const ro = editing.readOnly;
        const head = el('div', 'inv-node-head');
        head.append(skx.badge(step.kind === 'group' ? 'group' : 'agent', step.kind === 'group' ? 'Group' : 'Agent step'));
        const title = el('input', 'inv-title');
        title.type = 'text';
        title.maxLength = 120;
        title.value = step.title;
        title.disabled = ro;
        title.setAttribute('aria-label', `Title of step ${step.id}`);
        title.addEventListener('input', () => { step.title = title.value; });
        head.append(title, el('code', 'inv-ref', `{{steps.${step.id}}}`));
        li.append(head);

        if (step.kind === 'group') {
            const mode = el('select');
            mode.setAttribute('aria-label', `How group ${step.id} runs its steps`);
            [['sequential', 'One after another'], ['parallel', 'At the same time']].forEach(([v, t]) => { const o = el('option', null, t); o.value = v; mode.append(o); });
            mode.value = step.mode;
            mode.disabled = ro;
            mode.addEventListener('change', () => { step.mode = mode.value; });
            li.append(mode);
        } else {
            const agent = el('select');
            agent.setAttribute('aria-label', `Agent for step ${step.id}`);
            const known = options.agents.some(a => a.ref === step.agent);
            if (!known) {
                const o = el('option', null, step.agent ? `${step.agent} (not available now)` : 'Pick an agent');
                o.value = step.agent || '';
                agent.append(o);
            }
            options.agents.forEach(a => {
                const o = el('option', null, a.description ? `${agentLabel(a.ref)}: ${a.description}` : agentLabel(a.ref));
                o.value = a.ref;
                agent.append(o);
            });
            agent.value = step.agent || '';
            agent.disabled = ro;
            agent.addEventListener('change', () => { step.agent = agent.value || null; });
            const prompt = el('textarea');
            prompt.value = step.prompt || '';
            prompt.maxLength = options.limits.maxPromptChars;
            prompt.disabled = ro;
            prompt.setAttribute('aria-label', `What the agent of step ${step.id} should do`);
            prompt.placeholder = 'What the agent should do. {{input}} is the question; {{steps.s1}} is step s1\'s answer.';
            prompt.addEventListener('input', () => { step.prompt = prompt.value; });
            li.append(agent, prompt);
        }

        if (!ro) {
            const actions = el('div', 'inv-node-actions');
            const move = (delta) => () => {
                const [s] = siblings.splice(index, 1);
                siblings.splice(index + delta, 0, s);
                renderTree();
            };
            const up = button('Up', move(-1), 'btn btn-ghost btn-sm', 'i-chevron-up');
            up.disabled = index === 0;
            up.setAttribute('aria-label', `Move ${step.title || step.id} up`);
            const down = button('Down', move(1), 'btn btn-ghost btn-sm', 'i-chevron-down');
            down.disabled = index === siblings.length - 1;
            down.setAttribute('aria-label', `Move ${step.title || step.id} down`);
            actions.append(up, down);
            const full = allSteps(editing.steps).length >= options.limits.maxSteps;
            if (step.kind === 'group') {
                const add = button('Add agent step', () => { step.steps.push(newStep('agent')); renderTree(); }, 'btn btn-secondary btn-sm', 'i-plus');
                add.disabled = full || depth + 1 > options.limits.maxDepth;
                const addGroup = button('Add group', () => { step.steps.push(newStep('group')); renderTree(); }, 'btn btn-secondary btn-sm', 'i-plus');
                addGroup.disabled = full || depth + 2 > options.limits.maxDepth;
                actions.append(add, addGroup);
            }
            actions.append(button('Remove', () => { siblings.splice(index, 1); renderTree(); }, 'btn btn-danger-ghost btn-sm'));
            li.append(actions);
        }

        if (step.kind === 'group') {
            const children = el('ol', 'inv-tree');
            children.setAttribute('aria-label', `Steps of ${step.title || step.id}`);
            children.append(...step.steps.map((c, i) => node(c, step.steps, i, depth + 1)));
            li.append(children);
        }
        return li;
    }

    async function saveEditor(e) {
        e.preventDefault();
        if (editing.readOnly) return;
        const body = {
            name: $('invName').value.trim(),
            description: $('invDescription').value.trim(),
            sharedTeam: $('invShare').value || null,
            steps: editing.steps,
            // Only supervisors have the box; a builder's save never carries an allowance forward.
            allowChainedWrites: $('invChainedWrites') ? $('invChainedWrites').checked : false
        };
        try {
            await skx.busy($('invEditorSave'), editing.id
                ? skx.api('PUT', `${api}/workflows/${editing.id}`, body)
                : skx.api('POST', `${api}/workflows`, body));
            $('invEditor').close();
            skx.toast(`Saved "${body.name}".`);
            await loadWorkflows();
        } catch (err) {
            showErrors($('invEditorErrors'), errorList(err));
            $('invEditorErrors').scrollIntoView({ block: 'nearest' });
        }
    }

    // ---------- Start ----------
    function openStart(w) {
        starting = w;
        showErrors($('invStartErrors'), []);
        $('invStartTitle').textContent = `Start "${w.name}"`;
        $('invInput').value = '';
        $('invInput').maxLength = options.limits.maxInputChars;
        $('invStart').showModal();
        $('invInput').focus();
    }

    async function start(e) {
        e.preventDefault();
        try {
            const run = await skx.busy($('invStartGo'), skx.api('POST', `${api}/runs`, { workflowId: starting.id, input: $('invInput').value }));
            $('invStart').close();
            openRun(run.id);
        } catch (err) {
            showErrors($('invStartErrors'), errorList(err));
        }
    }

    // ---------- Run panel: live progress ----------
    async function openRun(id) {
        watching = id;
        $('invRunSteps').replaceChildren();
        skx.state.loading($('invRunSteps'), { rows: 4 });
        if (!$('invRun').open) $('invRun').showModal();
        await refreshRun();
    }

    async function refreshRun() {
        clearTimeout(pollTimer);
        if (!watching || !$('invRun').open) return;
        const id = watching;
        try {
            const run = await skx.api('GET', `${api}/runs/${id}`);
            if (watching !== id) return;
            renderRun(run);
            if (run.state === 'queued' || run.state === 'running') pollTimer = setTimeout(refreshRun, 1500);
            else if ($('invPanelRuns').hidden === false) loadRuns();
        } catch (err) {
            skx.state.error($('invRunSteps'), { title: 'The run could not be loaded', detail: err.message, retry: refreshRun });
        }
    }

    function renderRun(run) {
        $('invRunTitle').textContent = run.workflowName;
        $('invRunBadges').replaceChildren(skx.badge(run.state, stateLabel(run.state) + (run.cancelRequested && run.state === 'running' ? ' (stopping)' : '')));
        const failure = $('invRunFailure');
        failure.replaceChildren();
        if (run.state === 'failed' && run.failure) {
            const alert = el('div', 'alert alert-error');
            alert.append(skx.icon('i-alert-circle'));
            const body = el('div', 'alert-body');
            body.append(el('div', 'alert-title', 'Why it failed'), el('p', null, run.failure));
            alert.append(body);
            failure.append(alert);
        }
        const meta = $('invRunMeta');
        meta.replaceChildren();
        const row = (k, v) => { if (v) meta.append(el('dt', null, k), el('dd', null, v)); };
        row('Started by', run.mine ? 'You' : run.createdBy);
        row('Queued', new Date(run.createdAt).toLocaleString());
        row('Duration', duration(run.startedAt, run.endedAt));
        const actions = $('invRunActions');
        actions.replaceChildren();
        if (run.state === 'queued' || run.state === 'running') {
            const cancel = button('Cancel', () => cancelRun(run, cancel), 'btn btn-danger-ghost btn-sm', 'i-x');
            cancel.disabled = run.cancelRequested;
            actions.append(cancel);
        } else {
            actions.append(button('Run again', ev => rerun(run, ev.currentTarget), 'btn btn-secondary btn-sm', 'i-refresh'));
        }
        const link = (label, href) => {
            const a = el('a', 'btn btn-secondary btn-sm');
            a.href = href;
            a.setAttribute('download', '');
            a.append(skx.icon('i-download'), document.createTextNode(label));
            return a;
        };
        actions.append(link('Report (PDF)', `${api}/runs/${run.id}/report.pdf`), link('Report (JSON)', `${api}/runs/${run.id}/report.json`));
        if (run.cardScope && run.cardKey) {
            const card = el('a', 'btn btn-ghost btn-sm', 'Memory card');
            card.href = `/Library?scope=${encodeURIComponent(run.cardScope)}&key=${encodeURIComponent(run.cardKey)}`;
            actions.append(card);
        }
        $('invRunInput').textContent = run.input;
        const results = Object.fromEntries(run.stepRuns.map(s => [s.stepId, s]));
        $('invRunSteps').replaceChildren(...run.steps.map(s => progressNode(s, results)));
    }

    function progressNode(step, results) {
        const r = results[step.id] || { state: 'pending' };
        const li = el('li', 'inv-node subcard');
        const head = el('div', 'subcard-head');
        const title = el('div');
        title.append(el('strong', null, step.title), document.createTextNode(' '),
            el('span', 'inv-step-meta', step.kind === 'group' ? (step.mode === 'parallel' ? 'at the same time' : 'one after another') : agentLabel(step.agent)));
        head.append(title, skx.badge(r.state, stateLabel(r.state)));
        li.append(head);
        const time = duration(r.startedAt, r.endedAt);
        if (time) li.append(el('div', 'inv-step-meta', r.state === 'running' ? `running for ${time}` : time));
        if (r.error) li.append(el('p', 'field-error', r.error));
        // kagent's own words: sent to builders and up only (D556), text only, cut visibly.
        const why = skx.kagentReason(r.kagentReason);
        if (why) li.append(why);
        if (r.output) {
            const out = el('details', 'inv-output');
            out.open = step.kind === 'agent' && r.output.length < 1200;
            out.append(el('summary', null, 'Answer'), el('div', 'text-block', r.output));
            li.append(out);
        }
        if (step.kind === 'group') {
            const children = el('ol', 'inv-tree');
            children.append(...step.steps.map(c => progressNode(c, results)));
            li.append(children);
        }
        return li;
    }

    async function cancelRun(run, btn) {
        const ok = await skx.confirm({ title: 'Cancel this investigation?', body: 'Steps still running stop; finished answers stay in the report.', confirm: 'Cancel it', cancel: 'Keep running', danger: true });
        if (!ok) return;
        try {
            await skx.busy(btn, skx.api('POST', `${api}/runs/${run.id}/cancel`));
            skx.announce('Cancelling.');
        } catch (err) {
            skx.toast('It could not be cancelled: ' + err.message, { tone: 'error' });
        }
        refreshRun();
    }

    async function rerun(run, btn) {
        try {
            const next = await skx.busy(btn, skx.api('POST', `${api}/runs/${run.id}/rerun`));
            openRun(next.id);
        } catch (err) {
            skx.toast('It could not be started again: ' + err.message, { tone: 'error' });
        }
    }

    // ---------- History ----------
    let runsRequest = 0;

    async function loadRuns() {
        const ticket = ++runsRequest; // latest request wins: an older answer landing late is dropped
        const box = $('invRuns');
        skx.state.loading(box, { rows: 4 });
        const params = new URLSearchParams();
        if ($('invFilterWorkflow').value) params.set('workflowId', $('invFilterWorkflow').value);
        if ($('invFilterState').value) params.set('state', $('invFilterState').value);
        if ($('invFilterText').value.trim()) params.set('q', $('invFilterText').value.trim());
        if (isSupervisor && $('invFilterMine').checked) params.set('mine', 'true');
        try {
            const runs = await skx.api('GET', `${api}/runs?${params}`);
            if (ticket !== runsRequest) return;
            if (runs.length === 0) {
                skx.state.empty(box, { icon: 'i-inbox', title: 'No investigations', body: params.toString() ? 'Nothing matches these filters.' : 'Run a workflow and it shows up here.' });
                return;
            }
            const table = el('table', 'table table-stack');
            const head = el('thead');
            const hr = el('tr');
            ['Workflow', 'Status', 'Started by', 'Queued', 'Duration', ''].forEach(h => hr.append(el('th', null, h)));
            head.append(hr);
            const body = el('tbody');
            runs.forEach(r => {
                const tr = el('tr');
                const cell = (label, content) => { const td = el('td'); td.dataset.label = label; td.append(content); tr.append(td); };
                cell('Workflow', el('span', null, r.workflowName));
                cell('Status', skx.badge(r.state, stateLabel(r.state)));
                cell('Started by', el('span', null, r.mine ? 'You' : r.createdBy));
                const when = el('time', null, skx.time(r.createdAt));
                when.dateTime = r.createdAt;
                when.title = r.createdAt;
                cell('Queued', when);
                cell('Duration', el('span', null, duration(r.startedAt, r.endedAt)));
                const actions = el('div', 'row-actions');
                actions.append(button('Open', () => openRun(r.id)));
                if (r.state !== 'queued' && r.state !== 'running') actions.append(button('Run again', ev => rerun(r, ev.currentTarget), 'btn btn-ghost btn-sm'));
                cell('', actions);
                body.append(tr);
            });
            table.append(head, body);
            skx.table.label(table);
            box.replaceChildren(table);
        } catch (err) {
            if (ticket !== runsRequest) return;
            skx.state.error(box, { title: 'The history could not be loaded', detail: err.message, retry: loadRuns });
        }
    }

    function init() {
        $('invTabWorkflows').addEventListener('click', () => selectTab('workflows'));
        $('invTabRuns').addEventListener('click', () => selectTab('runs'));
        $('invNew').addEventListener('click', () => openEditor(null));
        $('invSearch').addEventListener('input', () => { if (workflows) renderWorkflows(); });
        $('invAddStep').addEventListener('click', () => { editing.steps.push(newStep('agent')); renderTree(); });
        $('invAddGroup').addEventListener('click', () => { editing.steps.push(newStep('group')); renderTree(); });
        $('invEditorForm').addEventListener('submit', saveEditor);
        $('invEditorCancel').addEventListener('click', () => $('invEditor').close());
        $('invStartForm').addEventListener('submit', start);
        $('invStartCancel').addEventListener('click', () => $('invStart').close());
        $('invRunClose').addEventListener('click', () => $('invRun').close());
        $('invRun').addEventListener('close', () => { watching = null; clearTimeout(pollTimer); });
        ['invFilterWorkflow', 'invFilterState'].forEach(id => $(id).addEventListener('change', loadRuns));
        if (isSupervisor) $('invFilterMine').addEventListener('change', loadRuns);
        let typing;
        $('invFilterText').addEventListener('input', () => { clearTimeout(typing); typing = setTimeout(loadRuns, 300); });
        $('invRunsRefresh').addEventListener('click', loadRuns);
        loadOptions().then(loadWorkflows);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
