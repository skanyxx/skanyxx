// The signed-in person's own token quota (api/quotas/me), wherever a [data-quota-meter] is: "compact" on Chat, "full" on
// the account page. Hidden when the quotas module is not loaded (404). "Request more" asks through #quotaRequestDialog.
// Chat fires 'skx:quota-changed' after a turn or a 429, and the meter reloads.
(() => {
    'use strict';
    const meters = [...document.querySelectorAll('[data-quota-meter]')];
    if (meters.length === 0) return;
    const number = n => Number(n).toLocaleString();

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

    // skx.badge picks its colour from a status word: approved reads as accepted, denied as refused.
    const statusTone = s => ({ approved: 'accepted', denied: 'refused' })[s] ?? s;

    function tone(q) {
        if (q.over) return 'danger';
        return q.percent >= 80 ? 'warning' : null;
    }

    function bar(q, label) {
        const fill = h('span');
        fill.style.width = `${Math.min(100, q.percent ?? 0)}%`;
        return h('div', {
            class: 'quota-bar', 'data-tone': tone(q), role: 'progressbar', 'aria-label': label,
            'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(Math.min(100, q.percent ?? 0)),
            'aria-valuetext': `${q.percent}% of your monthly tokens used`
        }, fill);
    }

    function binding(q) {
        const kind = { user: 'your personal limit', team: `team ${q.bindingName}`, department: `department ${q.bindingName}` }[q.bindingKind];
        return `Limit set by ${kind}` + (q.extra > 0 ? `, plus ${number(q.extra)} approved extra tokens` : '') + '.';
    }

    function requestButton(data) {
        if (data.quota.limit === null) return null;
        if (data.hasPending) return h('span', { class: 'text-muted', text: 'Your request for more is waiting for an answer.' });
        const button = h('button', { type: 'button', class: 'btn btn-secondary btn-sm', text: 'Request more' });
        button.addEventListener('click', () => requestMore(button));
        return button;
    }

    function compact(meter, data) {
        const q = data.quota;
        if (q.limit === null) {
            meter.replaceChildren(h('span', { text: `${number(q.used)} tokens this month · no limit` }));
            return;
        }
        meter.replaceChildren(
            bar(q, 'Your monthly tokens'),
            h('span', { title: `${number(q.used)} of ${number(q.limit)} tokens this month. ${binding(q)}`, text: `${q.percent}%` }),
            q.over ? requestButton(data) : null);
    }

    function full(meter, data) {
        const q = data.quota;
        const notes = [];
        if (q.limit === null) notes.push(h('span', { text: 'You have no token limit.' }));
        else notes.push(h('span', { text: `${binding(q)} Resets on ${new Date(data.resetsOn + 'T00:00:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'long', timeZone: 'UTC' })} (UTC).` }));
        if (q.over) notes.push(h('strong', { text: 'You have reached your limit: Chat will not send messages until you get more or the month ends.' }));
        if (q.estimatedTurns > 0)
            notes.push(h('span', { text: `About ${number(q.estimatedTokens)} of these tokens are estimated: kagent did not report usage for ${number(q.estimatedTurns)} of your ${number(q.turns)} turns, so Chat counted characters instead.` }));
        if (q.cost !== null)
            notes.push(h('span', { text: `Estimated cost: ${q.cost.toLocaleString(undefined, { style: 'currency', currency: data.currency, maximumFractionDigits: 4 })} (an estimate, not a bill${q.unpricedModels.length ? `; no price for ${q.unpricedModels.join(', ')}` : ''}).` }));

        meter.querySelector('[data-quota-body]').replaceChildren(
            h('div', { class: 'quota-line' },
                h('span', { text: q.limit === null ? 'Tokens used' : 'Tokens used of your monthly limit' }),
                h('strong', { text: q.limit === null ? number(q.used) : `${number(q.used)} of ${number(q.limit)} (${q.percent}%)` })),
            q.limit === null ? null : bar(q, 'Your monthly tokens'),
            h('div', { class: 'quota-notes' }, ...notes),
            h('div', { class: 'quota-actions' }, requestButton(data)));
        meter.querySelector('[data-quota-cost]').hidden = q.cost === null;

        const list = meter.querySelector('[data-quota-requests]');
        if (data.requests.length === 0) {
            skx.state.empty(list, { title: 'No requests yet', body: q.limit === null ? 'You have no limit to ask more of.' : 'Use "Request more" when you need more tokens this month.' });
            return;
        }
        list.replaceChildren(h('ul', { class: 'list-plain' }, ...data.requests.map(r => h('li', {},
            skx.badge(statusTone(r.status), r.status),
            h('span', { text: ` ${number(r.amount)} tokens, asked ${skx.time(r.createdAt)}` + (r.decidedBy ? `, ${r.status} by ${r.decidedBy}` : '') }),
            h('span', { class: 'table-cell-sub', text: r.reason })))));
    }

    const target = meter => meter.querySelector('[data-quota-body]') ?? meter;
    let loaded = false;

    async function load() {
        // The first load shows a placeholder; a reload after a turn keeps the last numbers until the new ones arrive.
        if (!loaded) for (const meter of meters) {
            meter.hidden = false;
            skx.state.loading(target(meter), { rows: meter.dataset.quotaMeter === 'compact' ? 1 : 3 });
        }
        let data;
        try {
            data = await skx.api('GET', '/api/quotas/me');
        } catch (e) {
            // No quotas module: nothing to show. Anything else is said, with a way to try again, never hidden.
            for (const meter of meters) {
                meter.hidden = e.status === 404;
                if (e.status !== 404)
                    skx.state.error(target(meter), { title: 'Your token usage could not be loaded', detail: e.message, retry: load });
            }
            loaded = false;
            return;
        }
        loaded = true;
        for (const meter of meters) {
            meter.hidden = false;
            if (meter.dataset.quotaMeter === 'compact') compact(meter, data);
            else full(meter, data);
        }
    }

    async function requestMore(button) {
        const dialog = document.getElementById('quotaRequestDialog');
        const amount = dialog.querySelector('[name=amount]');
        const reason = dialog.querySelector('[name=reason]');
        amount.value = '';
        reason.value = '';
        if (await skx.ask(dialog, amount) !== 'send') return;
        try {
            await skx.busy(button, skx.api('POST', '/api/quotas/requests', { amount: Number(amount.value), reason: reason.value }));
            skx.toast('Request sent. You will see the answer here.', { tone: 'success' });
            skx.announce('Request for more tokens sent.');
            await load();
        } catch (e) {
            skx.toast(e.message, { tone: 'error' });
        }
    }

    document.addEventListener('skx:quota-changed', load);
    load();
})();
