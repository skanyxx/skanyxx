// The admin Dashboard: health tiles (api/dashboard/health) and usage (api/dashboard/summary), loaded side by side so a
// slow dependency never holds the numbers back. What is wrong comes first (D558): a status line that says "all healthy"
// or names what is not, then the alerts, the services with problems first, usage, charts and lists, and last the parts
// that are not set up. A state is always an icon and words, never a colour alone. Text only (textContent), never markup.
(() => {
    'use strict';
    const { el, json, chart, forget, stacked, sparkline, table, empty, hasData, dayLabels, number, cost } = Insights;
    const root = document.getElementById('dashboard');
    const owner = root?.dataset.owner === 'true';
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
    // Latest request wins: a reply that lands after a newer request for the same source is dropped.
    const seq = { health: 0, usage: 0 };
    // The server's own links (HealthBoard, DashboardSummaryHandler) are paths in this app; anything else is not a link.
    const local = url => typeof url === 'string' && url.startsWith('/') && !url.startsWith('//') ? url : null;

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
            local(tile.fixUrl) ? el('a', { class: 'btn btn-secondary btn-sm', href: local(tile.fixUrl) }, tile.status === 'down' && owner ? 'Fix it' : 'Open') : null,
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
                el('button', { type: 'button', class: 'btn btn-secondary btn-sm', onclick: () => retry(failed.usage !== null ? loadAll : loadHealth, 'status') },
                    icon('i-refresh'), 'Try again'));
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
        } else if (usageAlerts === null) {
            // Health is fine, but the all-clear waits for usage, so it never flips to amber a moment later.
            tone = 'ok';
            title = 'All services healthy';
            detail = 'Checking usage…';
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
            ...['down', 'warn'].flatMap(status => (tiles ?? []).filter(t => t.status === status)
                .map(t => ({ severity: status, text: `${t.title}: ${t.reason ?? t.summary}`, url: t.fixUrl }))),
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
                local(a.url) ? el('a', { href: local(a.url), class: 'dash-alert-link' }, 'Open') : null);
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

    /** A reply, or the error it failed with, for the latest request of a source only; null for a superseded one. */
    async function latest(source, url) {
        const n = ++seq[source];
        let reply;
        try { reply = { data: await json(url) }; }
        catch (e) { reply = { error: e.message }; }
        return n === seq[source] ? reply : null;
    }

    // "Try again" sits in the content it reloads, so focus moves to that region once it is back (not to the page).
    async function retry(load, regionId) {
        await load();
        $(regionId).focus();
    }

    async function loadHealth() {
        tiles = null;
        failed.health = null;
        renderStatus();
        renderCounts();
        renderAlerts();
        skeletons($('health'), 4, 'dash-tile-skeleton');
        const reply = await latest('health', '/api/dashboard/health');
        if (!reply) return;
        if ('data' in reply) {
            tiles = reply.data;
            renderHealth();
        } else {
            failed.health = reply.error;
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
            change = Math.sign(current - previous);
            iconId = change > 0 ? 'i-trending-up' : change < 0 ? 'i-trending-down' : 'i-minus';
            text = change === 0 ? 'same as the 7 days before'
                : `${change > 0 ? '+' : '−'}${pct === 0 ? '<1' : Math.abs(pct)}% vs the 7 days before`;
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

    /** A cost and, under it, the note that some models have no price (so the value stays one short line). */
    function costStat(label, estimate) {
        const partial = estimate.priced && estimate.unpricedModels.length > 0;
        return stat(label, cost(partial ? { ...estimate, unpricedModels: [] } : estimate),
            partial ? el('p', { class: 'dash-kpi-note' }, 'Some models unpriced') : null);
    }

    function renderGlance(s) {
        const spark = s.tokens7d.some(v => v > 0)
            ? el('div', { class: 'dash-spark' }, el('canvas', { 'aria-label': 'Tokens per day, last 7 days', role: 'img' }))
            : null;
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
            costStat('Estimated cost, 7 days', s.cost7d),
            costStat('Estimated cost, 30 days', s.cost30d),
            stat('Answer cache hit rate (7 days)', hitRate, el('p', { class: 'dash-kpi-note' }, `≈ ${number(s.tokensSaved7d)} tokens saved (estimate)`)),
            stat('Sent by us today / 7 days (estimate)', `${number(s.clientTokensToday)} / ${number(s.clientTokens7d)}`),
            stat('People (enabled)', number(s.people)));
        if (spark) chart(spark.querySelector('canvas'), t => sparkline(s.tokens7d, t));
    }

    /** The numbers behind a chart, for anyone who cannot read the bars: days with nothing are left out. */
    function dataTable(title, labels, series) {
        const rows = labels.map((day, i) => ({ day, values: series.map(s => s.values[i]) })).filter(r => r.values.some(v => v > 0));
        const t = table([{ title: 'Day', value: r => r.day }, ...series.map((s, i) => ({ title: s.name, value: r => number(r.values[i]), num: true }))],
            rows, 'No data.');
        return el('details', { class: 'chart-data' }, el('summary', null, 'Show the numbers'),
            el('div', { class: 'table-container', tabindex: '0', role: 'region', 'aria-label': `${title}: the numbers` }, stackable(t)),
            el('p', { class: 'field-hint' }, 'Days with nothing are left out.'));
    }

    function plot(id, summary, labels, series, build, emptyText) {
        const canvas = $(`chart-${id}`);
        const figure = canvas.closest('figure');
        canvas.parentElement.querySelector('.dash-chart-skeleton')?.remove();
        figure.querySelector('.chart-data')?.remove();
        $(`sum-${id}`).textContent = summary ?? '';
        const label = canvas.dataset.label ??= canvas.getAttribute('aria-label');
        if (series && hasData(series)) {
            canvas.setAttribute('aria-label', `${label}. ${summary}`);
            chart(canvas, build);
            figure.append(dataTable(figure.querySelector('.chart-title')?.textContent ?? label, labels, series));
        } else {
            canvas.setAttribute('aria-label', label);
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
        // The tiles are replaced: the old sparkline's chart is let go, or it would be redrawn on every theme change.
        $('glance').querySelectorAll('canvas').forEach(forget);
        skeletons($('glance'), 9, 'dash-kpi-skeleton');
        for (const canvas of document.querySelectorAll('.dash-charts canvas')) {
            const box = canvas.parentElement;
            if (!box.querySelector('.dash-chart-skeleton')) box.append(el('span', { class: 'skeleton dash-chart-skeleton', 'aria-hidden': 'true' }));
        }
        for (const id of ['top-agents', 'top-users', 'events'])
            if ($(id)) skx.state.loading($(id), { rows: 3 });
    }

    function usageFailed(message) {
        skx.state.error($('glance'), { title: 'Usage could not be read', detail: message, retry: () => retry(loadSummary, 'glance') });
        for (const id of ['tokens', 'chats', 'errors', 'cards']) plot(id, null, null, null, null, 'Not shown: usage could not be read.');
        for (const id of ['top-agents', 'top-users', 'events'])
            if ($(id)) $(id).replaceChildren(el('p', { class: 'chart-empty' }, 'Not shown: usage could not be read.'));
    }

    async function loadSummary() {
        usageAlerts = null;
        failed.usage = null;
        usageLoading();
        renderStatus();
        renderAlerts();
        const reply = await latest('usage', '/api/dashboard/summary');
        if (!reply) return;
        if ('data' in reply) {
            renderGlance(reply.data);
            renderCharts(reply.data);
            renderTops(reply.data);
            usageAlerts = reply.data.alerts;
        } else {
            usageFailed(reply.error);
            usageAlerts = [];
            failed.usage = reply.error;
        }
        renderStatus();
        renderAlerts();
    }

    async function loadAll() {
        await Promise.all([loadHealth(), loadSummary()]);
        const at = new Date();
        const word = failed.health === null && failed.usage === null ? 'Updated' : 'Last attempt';
        $('checked-at').replaceChildren(el('time', { datetime: at.toISOString(), title: skx.utc(at) },
            `${word} ${at.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`));
    }

    if (root) {
        renderStatus();
        renderAlerts();
        const refresh = $('dash-refresh');
        refresh.addEventListener('click', async () => {
            await skx.busy(refresh, loadAll());
            skx.announce(failed.health === null && failed.usage === null ? 'Dashboard updated.' : 'Dashboard updated; some parts could not be read.');
        });
        skx.busy(refresh, loadAll());
    }
})();
