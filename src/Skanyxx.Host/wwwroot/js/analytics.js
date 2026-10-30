// Analytics: one scope at a time (api/analytics/usage), the drill-down the server allows (api/analytics/scopes), the
// state in the URL so a view can be linked, and the owner's CSV (api/analytics/export).
(() => {
    'use strict';
    const { el, json, chart, stacked, line, horizontal, empty, hasData, dayLabels, number, cost } = Insights;
    const root = document.getElementById('analytics');
    if (!root) return;

    const params = new URLSearchParams(location.search);
    const state = {
        scope: params.get('scope') ?? 'me',
        id: params.get('id') ?? '',
        range: params.get('range') ?? '30d',
        from: params.get('from') ?? '',
        to: params.get('to') ?? '',
        compare: params.get('compare') === 'true'
    };
    const trail = [];
    let sort = { key: 'tokens', dir: -1 };
    let lastReport = null;

    const query = () => {
        const q = new URLSearchParams({ scope: state.scope, range: state.range });
        if (state.id) q.set('id', state.id);
        if (state.range === 'custom') { q.set('from', state.from); q.set('to', state.to); }
        if (state.compare) q.set('compare', 'true');
        return q;
    };

    function showError(message) {
        const node = document.getElementById('error');
        node.textContent = message ?? '';
        node.hidden = !message;
    }

    function delta(now, before) {
        if (before === undefined || before === null) return null;
        if (before === 0) return now === 0 ? 'same as before' : 'new this period';
        const change = Math.round((now - before) / before * 100);
        return `${change >= 0 ? '+' : ''}${change}% vs previous period`;
    }

    function stat(label, value, now, before) {
        const d = before === undefined ? null : delta(now, before);
        return el('div', { class: 'stat-tile' },
            el('div', { class: 'stat-value' }, value), el('div', { class: 'stat-label' }, label),
            d ? el('div', { class: 'stat-delta' }, d) : null);
    }

    function renderTotals(r) {
        const t = r.totals, p = r.previous ?? undefined;
        const pick = key => p === undefined ? undefined : p[key];
        document.getElementById('totals').replaceChildren(
            // Two counters, never one number (D548): the model's tokens (priced) and what Skanyxx sent (its own estimate).
            stat('Spent by model', number(t.tokensIn + t.tokensOut), t.tokensIn + t.tokensOut, p === undefined ? undefined : p.tokensIn + p.tokensOut),
            stat('Model input', number(t.tokensIn), t.tokensIn, pick('tokensIn')),
            stat('Model output', number(t.tokensOut), t.tokensOut, pick('tokensOut')),
            stat('Sent by us (estimate)', number(t.clientTokens), t.clientTokens, pick('clientTokens')),
            stat('Chat turns', number(t.requests), t.requests, pick('requests')),
            stat('New chats', number(t.chats), t.chats, pick('chats')),
            stat('Active people', number(t.activeUsers), t.activeUsers, pick('activeUsers')),
            stat('Cache hits · tokens saved (est.)', `${number(t.cacheHits)} · ${number(t.tokensSaved)}`, t.tokensSaved, pick('tokensSaved')),
            stat('Failed turns', number(t.errors), t.errors, pick('errors')),
            stat('Estimated cost', cost(t.cost)));
        const notes = [];
        if (t.unreported > 0) notes.push(`kagent reported no token usage for ${number(t.unreported)} of ${number(t.requests)} turns; their tokens are not counted.`);
        notes.push('Usage is counted from chat turns since usage recording was installed; earlier chats are not included.');
        document.getElementById('notes').textContent = notes.join(' ');
    }

    function renderCharts(r) {
        const labels = dayLabels(r.days);
        const model = document.getElementById('chart-model');
        hasData(r.tokensByModel) ? chart(model, t => stacked(labels, r.tokensByModel, t)) : empty(model, 'No tokens reported in this period.');
        const agent = document.getElementById('chart-agent');
        hasData(r.tokensByAgent) ? chart(agent, t => stacked(labels, r.tokensByAgent, t)) : empty(agent, 'No tokens reported in this period.');

        const top = document.getElementById('chart-top');
        const teams = r.topTeams.length > 0;
        const ranked = teams ? r.topTeams : r.topUsers;
        document.getElementById('top-title').textContent = teams ? 'Top teams by tokens' : r.scopeKind === 'user' ? 'Agents used (tokens)' : 'Top people by tokens';
        if (r.scopeKind === 'user') {
            const rows = r.rows.filter(x => x.totals.tokensIn + x.totals.tokensOut > 0);
            rows.length ? chart(top, t => horizontal(rows.map(x => x.name), rows.map(x => x.totals.tokensIn + x.totals.tokensOut), 'Tokens', t))
                : empty(top, 'No agent used in this period.');
        } else {
            ranked.some(x => x.tokens > 0)
                ? chart(top, t => horizontal(ranked.map(x => x.name), ranked.map(x => x.tokens), 'Tokens', t))
                : empty(top, 'Nobody used tokens in this period.');
        }

        const saved = document.getElementById('chart-saved');
        const savedSeries = [{ name: 'Tokens saved (estimate)', values: r.savedPerDay }];
        hasData(savedSeries) ? chart(saved, t => line(labels, savedSeries, t))
            : empty(saved, 'No answer came from the cache in this period (or the cache is off).');
    }

    const columns = [
        { key: 'name', title: 'Name', value: row => row.name },
        { key: 'requests', title: 'Turns', num: true, value: row => row.totals.requests },
        { key: 'chats', title: 'New chats', num: true, value: row => row.totals.chats },
        { key: 'clientTokens', title: 'Sent by us', num: true, value: row => row.totals.clientTokens },
        { key: 'tokens', title: 'Spent by model', num: true, value: row => row.totals.tokensIn + row.totals.tokensOut },
        { key: 'tokensIn', title: 'Model input', num: true, value: row => row.totals.tokensIn },
        { key: 'tokensOut', title: 'Model output', num: true, value: row => row.totals.tokensOut },
        { key: 'cacheHits', title: 'Cache hits', num: true, value: row => row.totals.cacheHits },
        { key: 'errors', title: 'Failed', num: true, value: row => row.totals.errors },
        { key: 'activeUsers', title: 'Active people', num: true, value: row => row.totals.activeUsers },
        { key: 'cost', title: 'Est. cost', num: true, value: row => row.totals.cost.priced ? row.totals.cost.amount : -1, text: row => cost(row.totals.cost) }
    ];

    function renderTable(r) {
        document.getElementById('table-title').textContent = {
            org: 'By department', department: 'By team', team: 'By person', user: 'By agent'
        }[r.scopeKind] ?? 'Breakdown';
        const rows = [...r.rows].sort((a, b) => {
            const c = columns.find(x => x.key === sort.key);
            const va = c.value(a), vb = c.value(b);
            return (typeof va === 'string' ? va.localeCompare(vb) : va - vb) * sort.dir;
        });
        const head = el('tr', null, columns.map(c => el('th', {
            scope: 'col', class: c.num ? 'num' : null, tabindex: '0',
            'aria-sort': sort.key === c.key ? (sort.dir > 0 ? 'ascending' : 'descending') : 'none',
            onclick: () => { sort = { key: c.key, dir: sort.key === c.key ? -sort.dir : (c.num ? -1 : 1) }; renderTable(r); },
            onkeydown: e => { if (e.key === 'Enter') e.currentTarget.click(); }
        }, c.title)));
        const body = rows.length === 0
            ? [el('tr', null, el('td', { colspan: String(columns.length), class: 'text-muted' }, 'Nothing in this period.'))]
            : rows.map(row => el('tr', null, columns.map(c => el('td', { class: c.num ? 'num' : null },
                c.key === 'name' && row.kind
                    ? el('button', { type: 'button', onclick: () => open(row.kind, row.id, row.name) }, row.name)
                    : c.text ? c.text(row) : c.num ? number(c.value(row)) : c.value(row)))));
        document.getElementById('breakdown').replaceChildren(el('thead', null, head), el('tbody', null, body));
    }

    function renderCrumbs(r) {
        const nav = document.getElementById('crumbs');
        const items = trail.map((t, i) => [
            i > 0 ? ' › ' : null,
            i === trail.length - 1 ? el('span', { 'aria-current': 'page' }, t.name)
                : el('button', { type: 'button', onclick: () => { trail.splice(i + 1); go(t.scope, t.id); } }, t.name)
        ]);
        nav.replaceChildren(...items.flat().filter(Boolean));
        document.getElementById('scope-name').textContent = r.scopeName;
        document.getElementById('range-name').textContent = `${r.from} – ${r.to} (UTC)`;
    }

    function open(kind, id, name) {
        trail.push({ scope: kind, id, name });
        go(kind, id);
    }

    function go(scope, id) {
        state.scope = scope;
        state.id = id ?? '';
        load();
    }

    async function load() {
        showError(null);
        history.replaceState(null, '', `?${query()}`);
        for (const b of document.querySelectorAll('[data-range]')) b.setAttribute('aria-pressed', String(b.dataset.range === state.range));
        document.getElementById('custom-range').hidden = state.range !== 'custom';
        try {
            const r = await json(`/api/analytics/usage?${query()}`);
            lastReport = r;
            if (trail.length === 0) trail.push({ scope: state.scope, id: state.id, name: r.scopeName });
            renderCrumbs(r);
            renderTotals(r);
            renderCharts(r);
            renderTable(r);
        } catch (e) {
            showError(`Analytics could not be loaded: ${e.message}`);
        }
    }

    async function loadScopes() {
        const pick = document.getElementById('scope-pick');
        const tree = await json('/api/analytics/scopes');
        const options = [];
        const walk = (nodes, depth) => nodes.forEach(n => {
            options.push(el('option', { value: `${n.kind}:${n.id}` }, `${' '.repeat(depth)}${n.name}`));
            walk(n.children, depth + 1);
        });
        walk(tree, 0);
        pick.replaceChildren(...options);
        pick.value = state.scope === 'me' ? `user:${tree[0].id}` : `${state.scope}:${state.id}`;
        pick.disabled = options.length < 2;
        pick.addEventListener('change', () => {
            const [kind, ...rest] = pick.value.split(':');
            trail.length = 0;
            go(kind, rest.join(':'));
        });
    }

    for (const b of document.querySelectorAll('[data-range]')) {
        b.addEventListener('click', () => {
            state.range = b.dataset.range;
            if (state.range === 'custom') {
                document.getElementById('custom-range').hidden = false;
                for (const x of document.querySelectorAll('[data-range]')) x.setAttribute('aria-pressed', String(x === b));
                return;
            }
            load();
        });
    }
    document.getElementById('from').value = state.from;
    document.getElementById('to').value = state.to;
    document.getElementById('apply-range').addEventListener('click', () => {
        state.from = document.getElementById('from').value;
        state.to = document.getElementById('to').value;
        load();
    });
    const compare = document.getElementById('compare');
    compare.checked = state.compare;
    compare.addEventListener('change', () => { state.compare = compare.checked; load(); });
    document.getElementById('export')?.addEventListener('click', () => {
        const q = query();
        q.delete('compare');
        location.href = `/api/analytics/export?${q}`;
    });

    loadScopes().catch(e => showError(`The scope list could not be loaded: ${e.message}`));
    load();
})();
