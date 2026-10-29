// The Quotas panel (api/quotas/overview): people and their usage, requests to decide, team and department limits, and
// model prices. The server decides who may do what; this page offers only what the overview says the viewer may do,
// and shows the server's words when it refuses.
(() => {
    'use strict';
    const root = document.getElementById('quotas');
    if (!root) return;
    const isOwner = root.dataset.owner === 'true';
    const $ = id => document.getElementById(id);
    const number = n => Number(n).toLocaleString();
    const kindLabel = { user: 'personal limit', team: 'team', department: 'department' };
    const statusTone = s => ({ approved: 'accepted', denied: 'refused' })[s] ?? s;
    let data = null;

    const h = (tag, attrs = {}, ...children) => {
        const e = document.createElement(tag);
        for (const [k, v] of Object.entries(attrs)) {
            if (v === null || v === undefined || v === false) continue;
            if (k === 'text') e.textContent = v;
            else e.setAttribute(k, v === true ? '' : v);
        }
        e.append(...children.filter(c => c !== null && c !== undefined && c !== false));
        return e;
    };

    // One full-width cell for a table's loading, empty or error state (skx.state).
    const tableState = (id, show) => {
        const cell = h('td', { 'data-label': '' });
        $(id).replaceChildren(h('tbody', {}, h('tr', {}, cell)));
        show(cell);
    };
    const tables = ['peopleTable', 'requestsTable', 'scopesTable', 'pricesTable'];

    const table = (id, heads, rows, emptyText) => {
        const t = $(id);
        if (rows.length === 0) {
            tableState(id, cell => skx.state.empty(cell, { title: emptyText }));
            return;
        }
        t.replaceChildren(
            h('thead', {}, h('tr', {}, ...heads.map(([label, cls]) => h('th', { scope: 'col', class: cls, text: label })))),
            h('tbody', {}, ...rows));
        skx.table.label(t);
    };

    function bar(p) {
        const fill = h('span');
        fill.style.width = `${Math.min(100, p.percent ?? 0)}%`;
        return h('div', {
            class: 'quota-bar', 'data-tone': p.over ? 'danger' : p.percent >= 80 ? 'warning' : null, role: 'progressbar',
            'aria-label': `${p.name}: monthly tokens used`, 'aria-valuemin': '0', 'aria-valuemax': '100',
            'aria-valuenow': String(Math.min(100, p.percent ?? 0))
        }, fill);
    }

    const button = (label, onClick, cls = 'btn btn-secondary btn-sm') => {
        const b = h('button', { type: 'button', class: cls, text: label });
        b.addEventListener('click', () => onClick(b));
        return b;
    };

    // The overview names whom the viewer may act for (never themselves, the owner or a fellow supervisor); the server refuses the rest.
    const mayActFor = userId => data.actFor.includes(userId);

    // Someone a supervisor may not act for (a fellow supervisor, the owner): the server sends their name and percentage only.
    const maskedRow = p => h('tr', {},
        h('td', { text: p.name }),
        h('td', { class: 'text-muted', text: 'Not shown' }),
        h('td', {}, h('div', { class: 'quota-cell' },
            h('span', { text: p.percent === null ? 'no limit' : `${p.percent}% of their limit` }),
            p.percent === null ? null : bar(p))),
        h('td', { class: 'text-muted', text: 'Not yours to manage' }),
        h('td', { class: 'num text-muted', text: 'Not shown' }),
        h('td', {}));

    function renderPeople() {
        $('monthLine').textContent = `Month ${data.month} (UTC). Usage resets on ${data.resetsOn} (UTC).`;
        table('peopleTable', [['Person'], ['Teams'], ['Used this month'], ['Limit set by'], ['Est. cost', 'num'], ['', '']],
            data.people.map(p => p.masked ? maskedRow(p) : h('tr', {},
                h('td', {}, h('span', { text: p.name }), h('span', { class: 'table-cell-sub', text: p.email })),
                h('td', { text: p.teams.join(', ') || 'No team' }),
                h('td', {}, h('div', { class: 'quota-cell' },
                    h('span', { text: p.limit === null ? `${number(p.used)} · no limit` : `${number(p.used)} of ${number(p.limit)} (${p.percent}%)` }),
                    p.limit === null ? null : bar(p),
                    p.estimatedTurns > 0 ? h('span', { class: 'table-cell-sub', text: `incl. ~${number(p.estimatedTokens)} estimated (kagent reported nothing for ${number(p.estimatedTurns)} turns)` }) : null)),
                h('td', {}, h('span', { text: p.bindingKind ? `${kindLabel[p.bindingKind]}${p.bindingKind === 'user' ? '' : ' ' + p.bindingName}` : 'none' }),
                    p.extra > 0 ? h('span', { class: 'table-cell-sub', text: `+${number(p.extra)} approved` }) : null,
                    p.personalLimit !== null && p.bindingKind !== 'user' ? h('span', { class: 'table-cell-sub', text: `personal ${number(p.personalLimit)}` }) : null),
                h('td', { class: 'num', text: p.cost === null ? 'no price' : `~${p.cost.toLocaleString(undefined, { style: 'currency', currency: data.currency, maximumFractionDigits: 4 })}` }),
                h('td', {}, h('div', { class: 'row-actions' },
                    mayActFor(p.userId) ? button('Personal limit', b => setLimit(b, 'user', p.userId, p.name, p.personalLimit)) : null)))),
            'Nobody to show yet.');
    }

    function renderRequests() {
        table('requestsTable', [['Person'], ['Tokens', 'num'], ['Reason'], ['Status'], ['Asked'], ['', '']],
            data.requests.map(r => h('tr', {},
                h('td', { text: r.name }),
                h('td', { class: 'num', text: number(r.amount) }),
                h('td', { text: r.reason }),
                h('td', {}, skx.badge(statusTone(r.status), r.status), r.decidedBy ? h('span', { class: 'table-cell-sub', text: `by ${r.decidedBy}` }) : null),
                h('td', { title: r.createdAt, text: skx.time(r.createdAt) }),
                h('td', {}, h('div', { class: 'row-actions' },
                    r.status === 'pending' && mayActFor(r.userId) ? button('Approve', b => decide(b, r, 'approve'), 'btn btn-primary btn-sm') : null,
                    r.status === 'pending' && mayActFor(r.userId) ? button('Deny', b => decide(b, r, 'deny')) : null)))),
            'No requests.');
    }

    function renderScopes() {
        const limitOf = (kind, id) => data.limits.find(l => l.kind === kind && l.id === id)?.monthlyTokens ?? null;
        table('scopesTable', [['Scope'], ['Name'], ['Tokens per member per month', 'num'], ['', '']],
            data.scopes.map(s => {
                const limit = limitOf(s.kind, s.id);
                return h('tr', {},
                    h('td', { text: s.kind }),
                    h('td', { text: s.name }),
                    h('td', { class: 'num', text: limit === null ? 'no limit' : number(limit) }),
                    h('td', {}, h('div', { class: 'row-actions' }, isOwner ? button('Set limit', b => setLimit(b, s.kind, s.id, s.name, limit)) : null)));
            }),
            'No teams or departments yet. Create them on the Org page.');
    }

    function renderPrices() {
        const price = model => data.prices.find(p => p.model === model)?.pricePer1K ?? null;
        const rows = data.models.map(m => h('tr', {},
            h('td', { text: m }),
            h('td', { class: 'num', text: price(m) === null ? 'no price' : `$${price(m)}` }),
            h('td', {}, h('div', { class: 'row-actions' }, isOwner ? button('Set price', b => setPrice(b, m, price(m))) : null))));
        table('pricesTable', [['Model'], ['USD per 1,000 tokens', 'num'], ['', '']], rows, 'No model has been used or priced yet.');
        if (isOwner) {
            const add = button('Add a model price', b => setPrice(b, '', null), 'btn btn-secondary');
            $('pricesTable').append(h('caption', {}, add));
        }
    }

    async function load() {
        $('quotasError').replaceChildren();
        if (!data) for (const id of tables) tableState(id, cell => skx.state.loading(cell, { rows: 3 }));
        try {
            data = await skx.api('GET', '/api/quotas/overview');
        } catch (e) {
            // Never an empty table that reads as "nobody": the tables say the data is missing, the box says why.
            data = null;
            for (const id of tables) tableState(id, cell => skx.state.empty(cell, { icon: 'i-alert-circle', title: 'Not loaded' }));
            skx.state.error($('quotasError'), { title: 'Quotas could not be loaded', detail: e.message, retry: load });
            return;
        }
        renderPeople();
        renderRequests();
        renderScopes();
        renderPrices();
    }

    async function save(trigger, method, url, body, done) {
        try {
            await skx.busy(trigger, skx.api(method, url, body));
            skx.toast(done, { tone: 'success' });
            skx.announce(done);
            await load();
        } catch (e) {
            skx.toast(e.message, { tone: 'error', timeout: 8000 });
        }
    }

    async function setLimit(trigger, kind, id, name, current) {
        const dialog = $('limitDialog');
        $('limitTitle').textContent = kind === 'user' ? `Personal limit for ${name}` : `Limit for ${kind} ${name}`;
        $('limitHint').textContent = kind === 'user'
            ? 'The tightest of this, their teams\' and their departments\' limits binds.' + (isOwner ? '' : ' You cannot go above their team or department limit.')
            : `Every member may use this many tokens a month, unless a tighter limit applies.`;
        const input = $('limitTokens');
        input.value = current ?? '';
        const answer = await skx.ask(dialog, input);
        if (answer === 'save' && input.value === '') { skx.toast('Type a number of tokens, or use Remove limit.', { tone: 'error' }); return; }
        if (answer !== 'save' && answer !== 'clear') return;
        const monthlyTokens = answer === 'clear' ? null : Number(input.value);
        await save(trigger, 'PUT', `/api/quotas/limits/${kind}/${encodeURIComponent(id)}`, { monthlyTokens },
            monthlyTokens === null ? `Limit removed for ${name}.` : `Limit for ${name} set to ${number(monthlyTokens)} tokens.`);
    }

    async function setPrice(trigger, model, current) {
        const modelInput = $('priceModel');
        const value = $('priceValue');
        modelInput.value = model;
        modelInput.readOnly = model !== '';
        value.value = current ?? '';
        const answer = await skx.ask($('priceDialog'), model ? value : modelInput);
        if (answer !== 'save' && answer !== 'clear') return;
        const slash = modelInput.value.indexOf('/');
        const [ns, name] = slash < 0 ? ['', ''] : [modelInput.value.slice(0, slash), modelInput.value.slice(slash + 1)];
        if (!ns || !name || name.includes('/')) { skx.toast('A model is its ModelConfig as namespace/name.', { tone: 'error' }); return; }
        if (answer === 'save' && value.value === '') { skx.toast('Type a price, or use Remove price.', { tone: 'error' }); return; }
        const pricePer1K = answer === 'clear' ? null : Number(value.value);
        await save(trigger, 'PUT', `/api/quotas/prices/${encodeURIComponent(ns)}/${encodeURIComponent(name)}`, { pricePer1K },
            pricePer1K === null ? `Price removed for ${modelInput.value}.` : `Price for ${modelInput.value} saved.`);
    }

    async function decide(trigger, request, decision) {
        const approve = decision === 'approve';
        const ok = await skx.confirm({
            title: approve ? `Approve ${number(request.amount)} tokens for ${request.name}?` : `Deny ${request.name}'s request?`,
            body: approve ? 'They are added to this person\'s limit until the month ends.' : 'They will see that it was denied.',
            confirm: approve ? 'Approve' : 'Deny',
            danger: !approve
        });
        if (!ok) return;
        await save(trigger, 'POST', `/api/quotas/requests/${request.id}/${decision}`, undefined,
            approve ? `Approved ${number(request.amount)} tokens for ${request.name}.` : `Denied ${request.name}'s request.`);
    }

    load();
})();
