/*
 * Skanyxx UI helpers, loaded once by the layout. No dependencies, no inline handlers needed.
 *
 *   skx.api(method, url, body)        fetch JSON (or send FormData); throws Error(problem text) with .status and .data
 *   skx.toast(text, { tone, timeout }) short confirmation (tone: success | error | info)
 *   skx.confirm({ title, body, confirm, danger }) -> Promise<boolean>, a native <dialog>
 *   skx.state.loading(el, { rows })   skeleton rows in el
 *   skx.state.empty(el, { icon, title, body, action: { label, href | onClick } })
 *   skx.state.error(el, { title, detail, retry })   retry: a function, shown as "Try again"
 *   skx.badge(status, label)          a status badge element (same colours as <status-badge>)
 *   skx.fieldError(input, text)       inline error under a <field>; '' clears it
 *   skx.busy(button, promise)         spinner + disabled until the promise settles
 *   skx.prefs.update(partial)         merge into the signed-in person's saved preferences
 *   skx.prefs.pageSize                the person's table page size
 *   skx.time(iso)                     "5 minutes ago" (the UTC time belongs in a title)
 *   skx.ask(dialog)                   showModal a <dialog> holding a method="dialog" form -> Promise of the submit
 *                                     button's value, or '' for Cancel, Escape or the close button
 *   skx.announce(text)                say it to screen readers (a polite live region), without a toast
 *   skx.kagentReason(text)            kagent's own words about a failure (builders and up only, D553): its start, and
 *                                     when longer, "Shortened: show all N characters" with the whole text; null for none
 *
 * Declarative: <form data-confirm="…" data-confirm-title="…" data-confirm-action="Disable" data-danger>,
 * <form data-once> (one submit), <button data-copy="…"> / data-copy-target="#id", <table class="table-stack">,
 * <button data-dialog-open="id" [data-dialog-set="field id" data-dialog-value="…"]> / <button data-dialog-close>, <dialog data-autoshow> (opened on load),
 * <time data-relative datetime="…"> (relative text, UTC in its title).
 * Every value from an API is set with textContent, never innerHTML.
 *
 * Loaded in <head> without defer, so pages can call skx from their inline scripts; the shell wiring waits for the DOM.
 */
(() => {
    'use strict';
    const root = document.documentElement;

    const el = (tag, cls, text) => {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined && text !== null) e.textContent = text;
        return e;
    };

    const icon = (id, cls = 'icon') => {
        const ns = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('class', cls);
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        const use = document.createElementNS(ns, 'use');
        use.setAttribute('href', '#' + id);
        svg.appendChild(use);
        return svg;
    };

    // ---------- API ----------
    const problemText = (problem, status) =>
        (problem.errors ? Object.values(problem.errors).flat().join(' ') : '')
        || problem.detail || problem.message || problem.title || `The server answered ${status}.`;

    // Content-Type only with a body: the API reads a JSON body whenever one is announced.
    async function api(method, url, body) {
        const init = { method, headers: { Accept: 'application/json' } };
        if (body instanceof FormData) init.body = body;
        else if (body !== undefined) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }
        let response;
        try {
            response = await fetch(url, init);
        } catch {
            throw new Error('Skanyxx could not be reached. Check your connection and try again.');
        }
        const data = response.status === 204 ? {} : await response.json().catch(() => ({}));
        if (!response.ok) {
            const error = new Error(problemText(data, response.status));
            error.status = response.status;
            error.data = data;
            throw error;
        }
        return data;
    }

    // ---------- Toasts ----------
    const toastIcons = { success: 'i-check-circle', error: 'i-alert-circle', info: 'i-info' };
    function toast(text, { tone = 'success', timeout = tone === 'error' ? 8000 : 4000 } = {}) {
        const host = document.getElementById('skx-toasts');
        if (!host) return;
        const t = el('div', `toast toast-${tone}`);
        t.setAttribute('role', tone === 'error' ? 'alert' : 'status');
        t.append(icon(toastIcons[tone] || 'i-info'), el('div', 'toast-message', text));
        const close = el('button', 'btn btn-ghost btn-icon btn-sm');
        close.type = 'button';
        close.setAttribute('aria-label', 'Dismiss');
        close.append(icon('i-x'));
        t.append(close);
        host.append(t);
        const leave = () => { t.classList.add('leaving'); setTimeout(() => t.remove(), 250); };
        close.addEventListener('click', leave);
        if (timeout) setTimeout(leave, timeout);
    }

    // ---------- Confirm ----------
    function confirmDialog({ title = 'Are you sure?', body = '', confirm = 'Confirm', cancel = 'Cancel', danger = false } = {}) {
        const dialog = document.getElementById('skx-confirm');
        if (!dialog || typeof dialog.showModal !== 'function')
            return Promise.resolve(window.confirm([title, body].filter(Boolean).join('\n\n')));
        dialog.querySelector('[data-confirm-title]').textContent = title;
        dialog.querySelector('[data-confirm-body]').textContent = body;
        const ok = dialog.querySelector('[data-confirm-ok]');
        ok.textContent = confirm;
        ok.className = danger ? 'btn btn-danger' : 'btn btn-primary';
        dialog.querySelector('[data-confirm-cancel]').textContent = cancel;
        // A destructive confirm opens on Cancel, so a stray Enter does not delete.
        return ask(dialog, danger ? dialog.querySelector('[data-confirm-cancel]') : ok).then(value => value === 'ok');
    }

    // returnValue survives between openings and a close without a value (Escape, close()) keeps it, so it is cleared first.
    function ask(dialog, focus) {
        dialog.returnValue = '';
        dialog.showModal();
        focus?.focus();
        return new Promise(resolve => dialog.addEventListener('close', () => resolve(dialog.returnValue), { once: true }));
    }

    // ---------- Screen-reader announcements ----------
    // The layout renders the region up front (a region inserted with its first words is often not read).
    let announcing = 0;
    function announce(text) {
        let region = document.getElementById('skx-announce');
        if (!region) {
            region = el('div', 'visually-hidden');
            region.id = 'skx-announce';
            region.setAttribute('aria-live', 'polite');
            document.body.append(region);
        }
        region.textContent = '';
        clearTimeout(announcing);
        // The words after a tick, so the same words twice are still read.
        announcing = setTimeout(() => { region.textContent = text; }, 50);
    }

    // ---------- States ----------
    const state = {
        loading(target, { rows = 4 } = {}) {
            const box = el('div', 'skeleton-rows');
            box.setAttribute('role', 'status');
            box.append(el('span', 'visually-hidden', 'Loading…'));
            for (let i = 0; i < rows; i++)
                box.append(el('span', `skeleton skeleton-line${i % 3 === 2 ? ' medium' : ''}`));
            target.replaceChildren(box);
        },
        empty(target, { icon: iconId = 'i-inbox', title, body, action, compact = true } = {}) {
            const box = el('div', compact ? 'empty-state compact' : 'empty-state');
            box.append(icon(iconId, 'empty-state-icon'), el('h3', 'empty-state-title', title));
            if (body) box.append(el('div', 'empty-state-body', body));
            if (action) box.append(actions([action]));
            target.replaceChildren(box);
        },
        error(target, { title = 'Something went wrong', detail, retry, compact = true } = {}) {
            const box = el('div', compact ? 'error-state compact' : 'error-state');
            box.setAttribute('role', 'alert');
            box.append(icon('i-alert-circle', 'error-state-icon'), el('h3', 'error-state-title', title));
            if (detail) box.append(el('p', 'error-state-detail', detail));
            if (retry) box.append(actions([{ label: 'Try again', onClick: retry, secondary: true }]));
            target.replaceChildren(box);
        },
    };

    function actions(list) {
        const row = el('div', 'empty-state-actions');
        for (const a of list) {
            const b = a.href ? el('a', '', a.label) : el('button', '', a.label);
            b.className = a.secondary ? 'btn btn-secondary' : 'btn btn-primary';
            if (a.href) b.href = a.href; else { b.type = 'button'; b.addEventListener('click', a.onClick); }
            row.append(b);
        }
        return row;
    }

    // ---------- Badges (same words as TagHelpers/StatusTone.cs) ----------
    const tones = {
        success: ['ready', 'accepted', 'healthy', 'running', 'active', 'live', 'succeeded', 'published', 'merged', 'enabled', 'online', 'ok', 'set'],
        warning: ['pending', 'starting', 'creating', 'draft', 'degraded', 'suspended', 'paused', 'waiting', 'unknown', 'expiring', 'experimental'],
        danger: ['failed', 'error', 'unhealthy', 'rejected', 'refused', 'invalid', 'crashloopbackoff', 'offline'],
    };
    const toneOf = status => {
        const s = String(status ?? '').toLowerCase();
        return Object.keys(tones).find(t => tones[t].includes(s)) || 'neutral';
    };
    function badge(status, label) {
        const b = el('span', `badge badge-${toneOf(status)}`);
        const dot = el('span', 'badge-dot');
        dot.setAttribute('aria-hidden', 'true');
        b.append(dot, document.createTextNode(label ?? status ?? 'Unknown'));
        return b;
    }

    // ---------- Fields ----------
    function wireField(field) {
        const id = field.dataset.field;
        const control = id && document.getElementById(id);
        if (!control) return;
        const ids = [`${id}-hint`, `${id}-error`].filter(x => document.getElementById(x));
        if (ids.length) control.setAttribute('aria-describedby', ids.join(' '));
        const error = document.getElementById(`${id}-error`);
        if (error && error.textContent.trim()) control.setAttribute('aria-invalid', 'true');
    }

    function fieldError(input, text) {
        const box = document.getElementById(`${input.id}-error`);
        if (box) box.textContent = text || '';
        if (text) input.setAttribute('aria-invalid', 'true'); else input.removeAttribute('aria-invalid');
    }

    // ---------- Busy ----------
    async function busy(button, promise) {
        button.setAttribute('aria-busy', 'true');
        button.disabled = true;
        try { return await promise; }
        finally { button.removeAttribute('aria-busy'); button.disabled = false; }
    }

    // ---------- Tables that stack on phones ----------
    function labelTable(table) {
        const heads = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim() || th.getAttribute('aria-label') || '');
        for (const row of table.querySelectorAll('tbody tr'))
            [...row.children].forEach((cell, i) => { if (!cell.hasAttribute('data-label')) cell.setAttribute('data-label', heads[i] ?? ''); });
    }

    // ---------- Time ----------
    const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
    const steps = [['year', 31536e6], ['month', 2592e6], ['week', 6048e5], ['day', 864e5], ['hour', 36e5], ['minute', 6e4]];
    function time(value) {
        const at = new Date(value);
        if (Number.isNaN(at.getTime())) return '';
        const diff = at.getTime() - Date.now();
        for (const [unit, ms] of steps)
            if (Math.abs(diff) >= ms) return relative.format(Math.round(diff / ms), unit);
        return relative.format(0, 'minute');
    }
    const utc = value => {
        const at = new Date(value);
        return Number.isNaN(at.getTime()) ? '' : at.toISOString().replace('T', ' ').slice(0, 16) + ' UTC';
    };

    // ---------- Preferences ----------
    const prefs = {
        get pageSize() { return Number(root.dataset.pageSize) || 25; },
        async update(partial) {
            const view = await api('GET', '/api/me/preferences');
            return api('PUT', '/api/me/preferences', { ...view.saved, ...partial });
        },
    };

    // ---------- kagent's failure reason (D553, D556) ----------
    // The server sends it to builders and up only. Text only; a long one shows its start, says it was cut, and keeps the
    // whole of it one click away: nothing is cut without saying so.
    const REASON_SHOWN = 280;
    function kagentReason(reason) {
        if (!reason) return null;
        const chars = Array.from(reason);
        const box = el('div', 'failure-reason');
        box.append(el('div', 'failure-reason-label', 'kagent said'),
            el('div', 'failure-reason-text', chars.length > REASON_SHOWN ? chars.slice(0, REASON_SHOWN).join('') + '…' : reason));
        if (chars.length > REASON_SHOWN) {
            const more = el('details');
            more.append(el('summary', null, `Shortened: show all ${chars.length.toLocaleString()} characters`), el('pre', 'failure-reason-full', reason));
            box.append(more);
        }
        return box;
    }

    window.skx = { api, toast, confirm: confirmDialog, state, badge, fieldError, busy, prefs, icon, time, utc, ask, announce, kagentReason, table: { label: labelTable } };

    // ---------- Shell behaviour ----------
    function initNav() {
        const sidebar = document.getElementById('sidebar');
        const open = document.getElementById('navOpen');
        const backdrop = document.getElementById('navBackdrop');
        if (!sidebar || !open) return;
        const set = shown => {
            sidebar.classList.toggle('open', shown);
            open.setAttribute('aria-expanded', String(shown));
            if (backdrop) backdrop.hidden = !shown;
            if (shown) sidebar.querySelector('.nav-item')?.focus();
        };
        open.addEventListener('click', () => set(!sidebar.classList.contains('open')));
        backdrop?.addEventListener('click', () => set(false));
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && sidebar.classList.contains('open')) { set(false); open.focus(); }
        });

        const toggle = document.getElementById('sidebarToggle');
        toggle?.addEventListener('click', async () => {
            const collapsed = root.dataset.sidebar !== 'collapsed';
            root.dataset.sidebar = collapsed ? 'collapsed' : 'expanded';
            toggle.setAttribute('aria-pressed', String(collapsed));
            try { await prefs.update({ sidebar: collapsed ? 'collapsed' : 'expanded' }); }
            catch (e) { toast(`The sidebar choice was not saved: ${e.message}`, { tone: 'error' }); }
        });
    }

    function initMenus() {
        const menus = [...document.querySelectorAll('details.user-menu')];
        document.addEventListener('click', e => {
            for (const m of menus) if (m.open && !m.contains(e.target)) m.open = false;
        });
        document.addEventListener('keydown', e => {
            if (e.key !== 'Escape') return;
            for (const m of menus) if (m.open) { m.open = false; m.querySelector('summary')?.focus(); }
        });
    }

    function initForms() {
        document.addEventListener('submit', async e => {
            const form = e.target;
            if (!(form instanceof HTMLFormElement)) return;
            // A form that already went (data-once, or a confirmed one) does not go twice: a second click on
            // "Merge" while the first request is under way must not merge again.
            if (form.dataset.sent === 'true') { e.preventDefault(); return; }
            if (form.dataset.confirm && form.dataset.confirmed !== 'true') {
                e.preventDefault();
                const submitter = e.submitter;
                const ok = await confirmDialog({
                    title: form.dataset.confirmTitle || 'Are you sure?',
                    body: form.dataset.confirm,
                    confirm: form.dataset.confirmAction || 'Confirm',
                    danger: form.hasAttribute('data-danger'),
                });
                if (!ok) return;
                form.dataset.confirmed = 'true';
                form.requestSubmit(submitter);
                return;
            }
            // A page script that took the submit over (preventDefault, then fetch) decides itself what may go again.
            if (!e.defaultPrevented && (form.hasAttribute('data-once') || form.dataset.confirmed === 'true')) {
                delete form.dataset.confirmed;
                form.dataset.sent = 'true';
                (e.submitter || form.querySelector('[type=submit]'))?.setAttribute('aria-busy', 'true');
            }
        });
        // A page restored from the back/forward cache comes back with its forms as they were left: sent and busy.
        window.addEventListener('pageshow', e => {
            if (!e.persisted) return;
            for (const form of document.querySelectorAll('form[data-sent], form[data-confirmed]')) {
                delete form.dataset.sent;
                delete form.dataset.confirmed;
                form.querySelectorAll('[aria-busy="true"]').forEach(b => b.removeAttribute('aria-busy'));
            }
        });
    }

    function initCopy() {
        document.addEventListener('click', async e => {
            const button = e.target.closest('[data-copy], [data-copy-target]');
            if (!button) return;
            const target = button.dataset.copyTarget && document.querySelector(button.dataset.copyTarget);
            const value = button.dataset.copy ?? target?.value ?? target?.textContent ?? '';
            try {
                await navigator.clipboard.writeText(value);
            } catch {
                target?.select?.();
                toast('Copy is blocked here: the text is selected, press Ctrl+C (Cmd+C).', { tone: 'info' });
                return;
            }
            const label = button.querySelector('[data-copy-label]');
            if (label && !label.dataset.copyLabelDefault) label.dataset.copyLabelDefault = label.textContent;
            clearTimeout(Number(button.dataset.copyTimer));
            button.dataset.copied = 'true';
            if (label) label.textContent = 'Copied';
            announce('Copied');
            button.dataset.copyTimer = String(setTimeout(() => {
                button.dataset.copied = 'false';
                if (label) label.textContent = label.dataset.copyLabelDefault;
            }, 2000));
        });
    }

    function initGuides() {
        for (const guide of document.querySelectorAll('details[data-guide]')) {
            const key = `skx.guide.${guide.dataset.guide}`;
            try { if (localStorage.getItem(key) === 'closed') guide.open = false; } catch { /* storage blocked: keep the default */ }
            guide.addEventListener('toggle', () => {
                try { localStorage.setItem(key, guide.open ? 'open' : 'closed'); } catch { /* storage blocked */ }
            });
        }
    }

    function initTables() {
        const tables = document.querySelectorAll('table.table-stack');
        tables.forEach(labelTable);
        const observer = new MutationObserver(records => {
            for (const r of records) { const t = r.target.closest?.('table.table-stack'); if (t) labelTable(t); }
        });
        tables.forEach(t => observer.observe(t, { childList: true, subtree: true }));
    }

    function initInstall() {
        let deferred;
        const button = document.getElementById('installBtn');
        if (button) {
            window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); deferred = e; button.hidden = false; });
            button.addEventListener('click', async () => {
                if (!deferred) return;
                deferred.prompt();
                await deferred.userChoice;
                deferred = null;
                button.hidden = true;
            });
            window.addEventListener('appinstalled', () => { button.hidden = true; });
        }
        // An earlier build registered a service worker that cached stale pages; it stays unregistered.
        navigator.serviceWorker?.getRegistrations().then(rs => rs.forEach(r => r.unregister())).catch(() => { /* not a secure origin: nothing registered */ });
    }

    function initDialogs() {
        document.addEventListener('click', e => {
            const opener = e.target.closest('[data-dialog-open]');
            if (opener) {
                // data-dialog-set="id" data-dialog-value="v": open the dialog with that field preset (New team from a department).
                const field = opener.dataset.dialogSet && document.getElementById(opener.dataset.dialogSet);
                if (field) field.value = opener.dataset.dialogValue ?? '';
                document.getElementById(opener.dataset.dialogOpen)?.showModal();
                return;
            }
            const closer = e.target.closest('[data-dialog-close]');
            if (closer) closer.closest('dialog')?.close();
        });
        document.querySelectorAll('dialog[data-autoshow]').forEach(d => { if (!d.open) d.showModal(); });
    }

    function initTimes() {
        for (const t of document.querySelectorAll('time[data-relative]')) {
            const at = t.getAttribute('datetime');
            const text = time(at);
            if (!text) continue; // a date that does not parse keeps the server's text
            if (!t.title) t.title = utc(at);
            t.textContent = text;
        }
    }

    // ---------- Appearance settings (Account page) ----------
    function initAppearance() {
        const form = document.getElementById('appearance-form');
        if (!form) return;
        const original = { ...root.dataset };
        const attr = { Theme: 'theme', Accent: 'accent', Density: 'density', FontSize: 'fontSize', Sidebar: 'sidebar', Motion: 'motion', ChatDetails: 'chatDetails' };
        const value = name => form.querySelector(`[name="Appearance.${name}"]:checked`)?.value;

        const preview = () => {
            for (const [name, key] of Object.entries(attr)) {
                const v = value(name);
                if (v === undefined) continue;
                if ((key === 'accent' && v === 'auto') || (key === 'motion' && v === 'system')) delete root.dataset[key];
                else root.dataset[key] = v;
            }
            // Accent swatches paint in the theme being previewed.
            const theme = root.dataset.theme || 'system';
            form.querySelectorAll('[data-accent-preview]').forEach(s => s.setAttribute('data-theme', theme));
        };
        form.addEventListener('change', preview);
        form.addEventListener('reset', () => setTimeout(() => {
            for (const k of Object.keys(root.dataset)) if (!(k in original)) delete root.dataset[k];
            Object.assign(root.dataset, original);
            if (list) for (const key of initialOrder) list.append(list.querySelector(`[data-key="${key}"]`));
            preview();
            syncOrder();
        }));

        // Nav order: move buttons rewrite the hidden NavOrder field.
        const list = form.querySelector('[data-order-list]');
        const orderField = form.querySelector('input[name="Appearance.NavOrder"]');
        const initialOrder = list ? [...list.children].map(li => li.dataset.key) : [];
        const syncOrder = () => {
            if (!list || !orderField) return;
            orderField.value = [...list.children].map(li => li.dataset.key).join(',');
            [...list.children].forEach((li, i, all) => {
                li.querySelector('[data-move="up"]').disabled = i === 0;
                li.querySelector('[data-move="down"]').disabled = i === all.length - 1;
            });
        };
        list?.addEventListener('click', e => {
            const button = e.target.closest('[data-move]');
            if (!button) return;
            const li = button.closest('li');
            if (button.dataset.move === 'up' && li.previousElementSibling) li.previousElementSibling.before(li);
            if (button.dataset.move === 'down' && li.nextElementSibling) li.nextElementSibling.after(li);
            syncOrder();
            button.focus();
        });
        syncOrder();
    }

    function initShell() {
        document.querySelectorAll('[data-field]').forEach(wireField);
        initNav();
        initMenus();
        initForms();
        initCopy();
        initGuides();
        initTables();
        initDialogs();
        initTimes();
        initInstall();
        initAppearance();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initShell); else initShell();
})();
