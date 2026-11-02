// The admin Dashboard: health tiles (api/dashboard/health) and usage (api/dashboard/summary), loaded side by side so a
// slow dependency never holds the numbers back. What is wrong comes first (D558): a status line that says "all healthy"
// or names what is not, then the alerts, the services with problems first, usage, charts and lists, and last the parts
// that are not set up. A state is always an icon and words, never a colour alone. Text only (textContent), never markup.
(() => {
    'use strict';
    const { el, json, chart, stacked, sparkline, table, empty, hasData, dayLabels, number, cost } = Insights;
    const root = document.getElementById('dashboard');
    const $ = id => document.getElementById(id);
    const icon = (id, cls = 'icon') => skx.icon(id, cls);

    // How each status reads: an icon and a word, so colour is never the only signal.
    const STATES = {
        down: { icon: 'i-x-circle', label: 'Down', rank: 0 },
        warn: { icon: 'i-alert-triangle', label: 'Degraded', rank: 1 },
        ok: { icon: 'i-check-circle', label: 'Healthy', rank: 2 },
        off: { icon: 'i-minus-circle', label: 'Off', rank: 3 }
    };
    const SEVERITY = {
        down: { icon: 'i-x-circle', label: 'Problem' },
        warn: { icon: 'i-alert-triangle', label: 'Warning' },
        info: { icon: 'i-info', label: 'Note' }
    };
    const stateOf = status => STATES[status] ?? STATES.off;
    const plural = (n, one, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;

    let tiles = null;
    let usageAlerts = null;
    // A source that could not be read is never "nothing needs attention": the status and the list say so.
    const failed = { health: null, usage: null };

    // The shared setup block's prompt and copy button (js/setup-guide.js): the tile's fixed texts, no values.
    function guide(tile) {
        return {
            id: `dashboard-${tile.id}`,
            title: `Skanyxx Dashboard: "${tile.title}" is ${tile.status === 'off' ? 'not configured' : 'not healthy'} (${tile.summary})`,
            cause: tile.reason || tile.summary,
            steps: [...tile.fixSteps, ...(tile.fixUrl ? [`The page for this in Skanyxx: ${tile.fixUrl}`] : [])]
        };
    }

    function stateBadge(status) {
        const s = stateOf(status);
        return el('span', { class: 'dash-state', 'data-status': status }, icon(s.icon), s.label);
    }

    function fixActions(tile) {
        return el('div', { class: 'dash-tile-actions' },
            tile.fixUrl ? el('a', { class: 'btn btn-secondary btn-sm', href: tile.fixUrl }, tile.status === 'down' ? 'Fix it' : 'Open') : null,
            skxSetupGuide.copyButton(skxSetupGuide.prompt(guide(tile)), 'Copy instructions for agent', 'btn btn-ghost btn-sm'));
    }

    function tileNode(tile) {
        const needsFix = tile.fixSteps.length > 0;
        return el('article', { class: 'dash-tile', 'data-status': tile.status, 'data-tile': tile.id, 'aria-label': `${tile.title}: ${stateOf(tile.status).label}, ${tile.summary}` },
            el('div', { class: 'dash-tile-head' }, el('h3', { class: 'dash-tile-title' }, tile.title), stateBadge(tile.status)),
            el('p', { class: 'dash-tile-summary' }, tile.summary),
            tile.reason ? el('p', { class: 'dash-tile-reason' }, tile.reason) : null,
            needsFix ? el('ol', { class: 'dash-steps' }, tile.fixSteps.map(step => el('li', null, step))) : null,
            needsFix ? fixActions(tile) : null);
    }

    // A part that is off: one line, opened for its steps. Nothing is broken, so it waits at the bottom of the page.
    function offNode(tile) {
        return el('details', { class: 'dash-off-item', 'data-status': 'off', 'data-tile': tile.id },
            el('summary', null,
                el('span', { class: 'dash-off-title' }, tile.title), stateBadge('off'),
                el('span', { class: 'dash-off-summary' }, tile.summary),
                icon('i-chevron-down', 'icon dash-chevron')),
            el('div', { class: 'dash-off-body' },
                tile.reason ? el('p', { class: 'dash-tile-reason' }, tile.reason) : null,
                tile.fixSteps.length ? el('ol', { class: 'dash-steps' }, tile.fixSteps.map(step => el('li', null, step))) : null,
                tile.fixSteps.length ? fixActions(tile) : null));
    }

    // ---------- Status: the one line that says whether anything is wrong ----------
    function renderStatus() {
        const box = $('status');
        if (tiles === null && failed.health === null) {
            box.dataset.tone = 'loading';
            box.setAttribute('aria-busy', 'true');
            box.replaceChildren(el('span', { class: 'skeleton dash-status-icon-skeleton' }),
                el('div', { class: 'dash-status-text' }, el('span', { class: 'skeleton skeleton-line short' }), el('span', { class: 'skeleton skeleton-line medium' }),
                    el('span', { class: 'visually-hidden' }, 'Checking the install…')));
            return;
        }
        box.setAttribute('aria-busy', 'false');
        const usageNote = failed.usage !== null ? ' Usage could not be read either.' : '';
        if (failed.health !== null) {
            box.dataset.tone = 'down';
            box.replaceChildren(icon('i-x-circle', 'icon dash-status-icon'),
                el('div', { class: 'dash-status-text' },
                    el('p', { class: 'dash-status-title' }, 'Health could not be read'),
                    el('p', { class: 'dash-status-detail' }, `The server said: ${failed.health.replace(/\.$/, '')}.${usageNote}`)),
                el('button', { type: 'button', class: 'btn btn-secondary btn-sm', onclick: () => loadHealth() }, icon('i-refresh'), 'Try again'));
            return;
        }
        const down = tiles.filter(t => t.status === 'down');
        const warn = tiles.filter(t => t.status === 'warn');
        const problems = down.length + warn.length;
        const usageWarnings = (usageAlerts ?? []).filter(a => a.severity === 'warn').length;
        let tone, title, detail;
        if (problems > 0) {
            tone = down.length ? 'down' : 'warn';
            title = `${plural(problems, 'problem')} ${problems === 1 ? 'needs' : 'need'} attention`;
            detail = [down.length ? `Down: ${down.map(t => t.title).join(', ')}.` : null,
                warn.length ? `Degraded: ${warn.map(t => t.title).join(', ')}.` : null].filter(Boolean).join(' ');
        } else if (usageWarnings > 0 || failed.usage !== null) {
            tone = 'warn';
            title = 'All services healthy, usage needs a look';
            detail = failed.usage !== null ? 'Usage could not be read.' : `${plural(usageWarnings, 'warning')} below.`;
        } else {
            tone = 'ok';
            title = 'All systems healthy';
            const ok = tiles.filter(t => t.status === 'ok').length;
            const off = tiles.length - ok;
            detail = `${ok} of ${tiles.length} checks healthy${off ? `; ${off} not set up` : ''}.`;
        }
        if (problems > 0 && usageNote) detail += usageNote;
        box.dataset.tone = tone;
        box.replaceChildren(icon(stateOf(tone).icon, 'icon dash-status-icon'),
            el('div', { class: 'dash-status-text' }, el('p', { class: 'dash-status-title' }, title), el('p', { class: 'dash-status-detail' }, detail)));
    }

    function renderAlerts() {
        const list = $('alerts');
        const items = [
            ...(failed.health !== null ? [{ severity: 'down', text: 'Health could not be read, so this list may be incomplete.' }] : []),
            ...(failed.usage !== null ? [{ severity: 'down', text: 'Usage could not be read, so this list may be incomplete.' }] : []),
            ...(tiles ?? []).filter(t => t.status === 'down').map(t => ({ severity: 'down', text: `${t.title}: ${t.reason ?? t.summary}`, url: t.fixUrl })),
            ...(usageAlerts ?? [])];
        if (items.length === 0) {
            const checking = usageAlerts === null || tiles === null;
            list.replaceChildren(el('li', { class: 'is-empty' }, icon(checking ? 'i-refresh' : 'i-check-circle'), checking ? 'Checking…' : 'Nothing needs attention.'));
            return;
        }
        list.replaceChildren(...items.map(a => {
            const s = SEVERITY[a.severity] ?? SEVERITY.info;
            return el('li', { 'data-severity': a.severity }, icon(s.icon),
                el('span', { class: 'dash-alert-text' }, el('span', { class: 'visually-hidden' }, `${s.label}: `), a.text),
                a.url ? el('a', { href: a.url, class: 'dash-alert-link' }, 'Open') : null);
        }));
    }

    function renderCounts() {
        const box = $('health-counts');
        if (!tiles) { box.replaceChildren(); return; }
        box.replaceChildren(...['down', 'warn', 'ok', 'off']
            .map(status => [status, tiles.filter(t => t.status === status).length])
            .filter(([, n]) => n > 0)
            .map(([status, n]) => el('span', { class: 'dash-count', 'data-status': status }, icon(stateOf(status).icon),
                `${n} ${status === 'off' ? 'not set up' : stateOf(status).label.toLowerCase()}`)));
    }

    function renderHealth() {
        const grid = $('health');
        const live = tiles.filter(t => t.status !== 'off').sort((a, b) => stateOf(a.status).rank - stateOf(b.status).rank);
        const off = tiles.filter(t => t.status === 'off');
        grid.replaceChildren(...live.map(tileNode));
        $('off').replaceChildren(...off.map(offNode));
        $('off-section').hidden = off.length === 0;
    }

    function skeletons(target, count, cls) {
        const box = el('div', { class: 'dash-skeletons', role: 'status' }, el('span', { class: 'visually-hidden' }, 'Loading…'));
        for (let i = 0; i < count; i++) box.append(el('span', { class: `skeleton ${cls}` }));
        target.replaceChildren(box);
    }

    async function loadHealth() {
        tiles = null;
        failed.health = null;
        renderStatus();
        renderCounts();
        skeletons($('health'), 4, 'dash-tile-skeleton');
        try {
            tiles = await json('/api/dashboard/health');
            renderHealth();
        } catch (e) {
            failed.health = e.message;
            $('health').replaceChildren(el('p', { class: 'dash-muted' }, 'Not shown: health could not be read (see above).'));
            $('off-section').hidden = true;
        }
        renderStatus();
        renderCounts();
        renderAlerts();
    }

    // ---------- Usage ----------
    const sum = values => values.reduce((a, b) => a + b, 0);
    const addSeries = list => list[0].values.map((_, i) => sum(list.map(s => s.values[i])));

    /** The last 7 days against the 7 before them, from a per-day series (today last); null when both are empty. */
    function trend(values, worseWhenUp = false) {
        const current = sum(values.slice(-7));
        const previous = sum(values.slice(-14, -7));
        if (current === 0 && previous === 0) return null;
        let iconId, text, change;
        if (previous === 0) {
            iconId = 'i-trending-up'; change = 1; text = 'none in the 7 days before';
        } else {
            const pct = Math.round((current - previous) / previous * 100);
            change = Math.sign(pct);
            iconId = pct > 0 ? 'i-trending-up' : pct < 0 ? 'i-trending-down' : 'i-minus';
            text = pct === 0 ? 'same as the 7 days before' : `${pct > 0 ? '+' : '−'}${Math.abs(pct)}% vs the 7 days before`;
        }
        const tone = !worseWhenUp || change === 0 ? 'neutral' : change > 0 ? 'bad' : 'good';
        return el('p', { class: 'dash-trend', 'data-tone': tone }, icon(iconId), `${number(current)} in 7 days, ${text}`);
    }

    function stat(label, value, ...extra) {
        return el('div', { class: 'dash-kpi' },
            el('p', { class: 'dash-kpi-label' }, label),
            el('p', { class: 'dash-kpi-value' }, value),
            ...extra);
    }

    function renderGlance(s) {
        const spark = el('div', { class: 'dash-spark' }, el('canvas', { 'aria-label': 'Tokens per day, last 7 days', role: 'img' }));
        const hitRate = s.requests7d === 0 ? '–' : `${Math.round(s.cacheHitRate7d * 100)}%`;
        const tokens = s.tokensInPerDay.map((v, i) => v + s.tokensOutPerDay[i]);
        const chats = s.chatsByAgent.length ? addSeries(s.chatsByAgent) : [];
        const failedTurns = sum(s.errorsPerDay.slice(-7));
        $('glance').replaceChildren(
            stat('Active users today / 7 days', `${number(s.activeToday)} / ${number(s.active7d)}`),
            stat('New chats today / 7 days', `${number(s.chatsToday)} / ${number(s.chats7d)}`, trend(chats)),
            // Two counters, never one number (D548): the model's tokens and what Skanyxx sent, by its own estimate.
            stat('Spent by model today (input / output)', `${number(s.tokensInToday)} / ${number(s.tokensOutToday)}`, trend(tokens), spark),
            stat('Failed turns, 7 days', number(failedTurns), trend(s.errorsPerDay, true)),
            stat('Estimated cost, 7 days', cost(s.cost7d)),
            stat('Estimated cost, 30 days', cost(s.cost30d)),
            stat('Answer cache hit rate (7 days)', hitRate, el('p', { class: 'dash-kpi-note' }, `≈ ${number(s.tokensSaved7d)} tokens saved (estimate)`)),
            stat('Sent by us today / 7 days (estimate)', `${number(s.clientTokensToday)} / ${number(s.clientTokens7d)}`),
            stat('People (enabled)', number(s.people)));
        chart(spark.querySelector('canvas'), t => sparkline(s.tokens7d, t));
    }

    /** The numbers behind a chart, for anyone who cannot read the bars: days with nothing are left out. */
    function dataTable(labels, series) {
        const rows = labels.map((day, i) => ({ day, values: series.map(s => s.values[i]) })).filter(r => r.values.some(v => v > 0));
        const t = table([{ title: 'Day', value: r => r.day }, ...series.map((s, i) => ({ title: s.name, value: r => number(r.values[i]), num: true }))],
            rows, 'No data.');
        return el('details', { class: 'chart-data' }, el('summary', null, 'Show the numbers'),
            el('div', { class: 'table-container' }, stackable(t)), el('p', { class: 'field-hint' }, 'Days with nothing are left out.'));
    }

    function plot(id, summary, labels, series, build, emptyText) {
        const canvas = $(`chart-${id}`);
        const figure = canvas.closest('figure');
        canvas.parentElement.querySelector('.dash-chart-skeleton')?.remove();
        figure.querySelector('.chart-data')?.remove();
        $(`sum-${id}`).textContent = summary ?? '';
        if (series && hasData(series)) {
            canvas.setAttribute('aria-label', `${canvas.dataset.label ??= canvas.getAttribute('aria-label')}. ${summary}`);
            chart(canvas, build);
            figure.append(dataTable(labels, series));
        } else {
            empty(canvas, emptyText);
        }
    }

    function busiest(labels, values) {
        const max = Math.max(...values);
        return max > 0 ? `; busiest ${labels[values.indexOf(max)]} (${number(max)})` : '';
    }

    function renderCharts(s) {
        const labels = dayLabels(s.days);
        const tokens = [{ name: 'Tokens in', values: s.tokensInPerDay }, { name: 'Tokens out', values: s.tokensOutPerDay }];
        plot('tokens', `${number(sum(s.tokensInPerDay))} in, ${number(sum(s.tokensOutPerDay))} out`, labels, tokens,
            t => stacked(labels, tokens, t), 'No tokens reported in the last 30 days.');

        const chatTotals = s.chatsByAgent.length ? addSeries(s.chatsByAgent) : [];
        plot('chats', `${number(sum(chatTotals))} chats${busiest(labels, chatTotals)}`, labels, s.chatsByAgent,
            t => stacked(labels, s.chatsByAgent, t), 'No new chats in the last 30 days.');

        const errors = [{ name: 'Failed turns', values: s.errorsPerDay }];
        plot('errors', `${number(sum(s.errorsPerDay))} failed${busiest(labels, s.errorsPerDay)}`, labels, errors,
            t => stacked(labels, errors, t, [t.down], false), 'No failed chat turns in the last 30 days.');

        if (s.cardsPerDay === null) plot('cards', null, labels, null, null, 'Memory could not be read just now.');
        else {
            const cards = [{ name: 'New cards', values: s.cardsPerDay }];
            plot('cards', `${number(sum(s.cardsPerDay))} new cards`, labels, cards,
                t => stacked(labels, cards, t, [t.ok], false), 'No new memory cards in the last 30 days.');
        }
    }

    function stackable(node) {
        if (node.tagName === 'TABLE') {
            node.classList.add('table-stack');
            skx.table.label(node);
        }
        return node;
    }

    function renderTops(s) {
        const columns = name => [
            { title: name, value: r => r.name },
            { title: 'Turns', value: r => number(r.requests), num: true },
            { title: 'Tokens', value: r => number(r.tokens), num: true },
            { title: 'New chats', value: r => number(r.chats), num: true }
        ];
        $('top-agents').replaceChildren(stackable(table(columns('Agent'), s.topAgents, 'No chat turns in the last 7 days.')));
        $('top-users').replaceChildren(s.topUsers === null
            ? el('p', { class: 'chart-empty' }, 'Per-person usage is the owner\'s. Your teams are on Analytics.')
            : stackable(table(columns('Person'), s.topUsers, 'No chat turns in the last 7 days.')));
        const events = $('events');
        if (events && s.recentEvents) {
            events.replaceChildren(stackable(table([
                { title: 'When (UTC)', value: e => new Date(e.at).toISOString().slice(0, 16).replace('T', ' ') },
                { title: 'Action', value: e => el('code', null, e.action) },
                { title: 'Actor', value: e => e.actor ?? '' },
                { title: 'Target', value: e => e.target ?? '' }
            ], s.recentEvents, 'No admin events yet.')));
        }
    }

    function usageLoading() {
        skeletons($('glance'), 9, 'dash-kpi-skeleton');
        for (const canvas of document.querySelectorAll('.dash-charts canvas')) {
            const box = canvas.parentElement;
            if (!box.querySelector('.dash-chart-skeleton')) box.append(el('span', { class: 'skeleton dash-chart-skeleton', 'aria-hidden': 'true' }));
        }
        for (const id of ['top-agents', 'top-users', 'events'])
            if ($(id)) skx.state.loading($(id), { rows: 3 });
    }

    function usageFailed(message) {
        skx.state.error($('glance'), { title: 'Usage could not be read', detail: message, retry: () => loadSummary() });
        for (const id of ['tokens', 'chats', 'errors', 'cards']) plot(id, null, null, null, null, 'Not shown: usage could not be read.');
        for (const id of ['top-agents', 'top-users', 'events'])
            if ($(id)) $(id).replaceChildren(el('p', { class: 'chart-empty' }, 'Not shown: usage could not be read.'));
    }

    async function loadSummary() {
        usageAlerts = null;
        failed.usage = null;
        usageLoading();
        renderAlerts();
        try {
            const s = await json('/api/dashboard/summary');
            renderGlance(s);
            renderCharts(s);
            renderTops(s);
            usageAlerts = s.alerts;
        } catch (e) {
            usageFailed(e.message);
            usageAlerts = [];
            failed.usage = e.message;
        }
        renderStatus();
        renderAlerts();
    }

    async function loadAll() {
        await Promise.all([loadHealth(), loadSummary()]);
        const at = new Date();
        $('checked-at').replaceChildren(el('time', { datetime: at.toISOString(), title: skx.utc(at) },
            `Updated ${at.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`));
    }

    if (root) {
        renderStatus();
        renderAlerts();
        const refresh = $('dash-refresh');
        refresh.addEventListener('click', () => skx.busy(refresh, loadAll()));
        skx.busy(refresh, loadAll());
    }
})();
