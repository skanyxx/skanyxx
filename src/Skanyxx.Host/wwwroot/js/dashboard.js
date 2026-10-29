// The admin Dashboard: health tiles (api/dashboard/health) and usage (api/dashboard/summary), loaded side by side so a
// slow dependency never holds the numbers back.
(() => {
    'use strict';
    const { el, json, chart, stacked, sparkline, table, empty, hasData, dayLabels, number, cost } = Insights;
    const root = document.getElementById('dashboard');
    const healthAlerts = [];
    let usageAlerts = null;

    // The shared setup block's prompt and copy button (js/setup-guide.js): the tile's fixed texts, no values.
    function guide(tile) {
        return {
            id: `dashboard-${tile.id}`,
            title: `Skanyxx Dashboard: "${tile.title}" is ${tile.status === 'off' ? 'not configured' : 'not healthy'} (${tile.summary})`,
            cause: tile.reason || tile.summary,
            steps: [...tile.fixSteps, ...(tile.fixUrl ? [`The page for this in Skanyxx: ${tile.fixUrl}`] : [])]
        };
    }

    function tileNode(tile) {
        const needsFix = tile.fixSteps.length > 0;
        return el('article', { class: 'health-tile', 'data-status': tile.status, 'data-tile': tile.id, 'aria-label': `${tile.title}: ${tile.summary}` },
            el('div', { class: 'tile-head' }, el('span', { class: 'status-dot', 'aria-hidden': 'true' }), tile.title),
            el('div', { class: 'tile-summary' }, tile.summary),
            tile.reason ? el('div', { class: 'tile-reason' }, tile.reason) : null,
            needsFix ? el('ol', null, tile.fixSteps.map(step => el('li', null, step))) : null,
            needsFix ? el('div', { class: 'tile-actions' },
                tile.fixUrl ? el('a', { class: 'btn btn-secondary', href: tile.fixUrl }, tile.status === 'down' ? 'Fix it' : 'Open') : null,
                skxSetupGuide.copyButton(skxSetupGuide.prompt(guide(tile)), 'Copy instructions for agent', 'btn btn-ghost')) : null);
    }

    function renderAlerts() {
        const list = document.getElementById('alerts');
        const items = [...healthAlerts, ...(usageAlerts ?? [])];
        if (items.length === 0) {
            list.replaceChildren(el('li', { class: 'is-empty text-muted' }, usageAlerts === null ? 'Checking…' : 'Nothing needs attention.'));
            return;
        }
        list.replaceChildren(...items.map(a => el('li', { 'data-severity': a.severity },
            a.text, a.url ? ' ' : null, a.url ? el('a', { href: a.url }, 'Open') : null)));
    }

    async function loadHealth() {
        const grid = document.getElementById('health');
        skx.state.loading(grid, { rows: 3 });
        try {
            const tiles = await json('/api/dashboard/health');
            grid.replaceChildren(...tiles.map(tileNode));
            healthAlerts.splice(0, healthAlerts.length, ...tiles.filter(t => t.status === 'down')
                .map(t => ({ severity: 'down', text: `${t.title}: ${t.reason ?? t.summary}`, url: t.fixUrl })));
        } catch (e) {
            skx.state.error(grid, { title: 'Health could not be read', detail: e.message, retry: loadHealth });
        }
        renderAlerts();
    }

    function stat(label, value, extra) {
        return el('div', { class: 'stat-tile' }, el('div', { class: 'stat-value' }, value), el('div', { class: 'stat-label' }, label), extra ?? null);
    }

    function renderGlance(s) {
        const spark = el('div', { class: 'sparkline' }, el('canvas', { 'aria-label': 'Tokens per day, last 7 days', role: 'img' }));
        const hitRate = s.requests7d === 0 ? '–' : `${Math.round(s.cacheHitRate7d * 100)}%`;
        document.getElementById('glance').replaceChildren(
            stat('People (enabled)', number(s.people)),
            stat('Active users today / 7 days', `${number(s.activeToday)} / ${number(s.active7d)}`),
            stat('New chats today / 7 days', `${number(s.chatsToday)} / ${number(s.chats7d)}`),
            stat('Tokens today (in / out)', `${number(s.tokensInToday)} / ${number(s.tokensOutToday)}`, spark),
            stat('Answer cache hit rate (7 days)', hitRate, el('div', { class: 'stat-delta' }, `≈ ${number(s.tokensSaved7d)} tokens saved (estimate)`)),
            stat('Estimated cost, 7 days', cost(s.cost7d)),
            stat('Estimated cost, 30 days', cost(s.cost30d)));
        chart(spark.querySelector('canvas'), t => sparkline(s.tokens7d, t));
    }

    function renderCharts(s) {
        const labels = dayLabels(s.days);
        const tokens = [{ name: 'Tokens in', values: s.tokensInPerDay }, { name: 'Tokens out', values: s.tokensOutPerDay }];
        const tokensCanvas = document.getElementById('chart-tokens');
        hasData(tokens) ? chart(tokensCanvas, t => stacked(labels, tokens, t)) : empty(tokensCanvas, 'No tokens reported in the last 30 days.');

        const chatsCanvas = document.getElementById('chart-chats');
        hasData(s.chatsByAgent) ? chart(chatsCanvas, t => stacked(labels, s.chatsByAgent, t)) : empty(chatsCanvas, 'No new chats in the last 30 days.');

        const errors = [{ name: 'Failed turns', values: s.errorsPerDay }];
        const errorsCanvas = document.getElementById('chart-errors');
        hasData(errors) ? chart(errorsCanvas, t => stacked(labels, errors, t, [t.down], false)) : empty(errorsCanvas, 'No failed chat turns in the last 30 days.');

        const cardsCanvas = document.getElementById('chart-cards');
        if (s.cardsPerDay === null) empty(cardsCanvas, 'Memory could not be read just now.');
        else {
            const cards = [{ name: 'New cards', values: s.cardsPerDay }];
            hasData(cards) ? chart(cardsCanvas, t => stacked(labels, cards, t, [t.ok], false)) : empty(cardsCanvas, 'No new memory cards in the last 30 days.');
        }
    }

    function renderTops(s) {
        const columns = name => [
            { title: name, value: r => r.name },
            { title: 'Turns', value: r => number(r.requests), num: true },
            { title: 'Tokens', value: r => number(r.tokens), num: true },
            { title: 'New chats', value: r => number(r.chats), num: true }
        ];
        document.getElementById('top-agents').replaceChildren(table(columns('Agent'), s.topAgents, 'No chat turns in the last 7 days.'));
        document.getElementById('top-users').replaceChildren(s.topUsers === null
            ? el('p', { class: 'text-muted' }, 'Per-person usage is the owner\'s. Your teams are on Analytics.')
            : table(columns('Person'), s.topUsers, 'No chat turns in the last 7 days.'));
        const events = document.getElementById('events');
        if (events && s.recentEvents) {
            events.replaceChildren(table([
                { title: 'When (UTC)', value: e => new Date(e.at).toISOString().slice(0, 16).replace('T', ' ') },
                { title: 'Action', value: e => el('code', null, e.action) },
                { title: 'Actor', value: e => e.actor ?? '' },
                { title: 'Target', value: e => e.target ?? '' }
            ], s.recentEvents, 'No admin events yet.'));
        }
    }

    async function loadSummary() {
        skx.state.loading(document.getElementById('glance'), { rows: 2 });
        try {
            const s = await json('/api/dashboard/summary');
            renderGlance(s);
            renderCharts(s);
            renderTops(s);
            usageAlerts = s.alerts;
        } catch (e) {
            skx.state.error(document.getElementById('glance'), { title: 'Usage could not be read', detail: e.message, retry: loadSummary });
            usageAlerts = [];
        }
        renderAlerts();
    }

    if (root) {
        renderAlerts();
        loadHealth();
        loadSummary();
    }
})();
