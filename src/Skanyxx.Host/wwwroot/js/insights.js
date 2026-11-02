// Shared by the Dashboard and Analytics: charts that take their colours from CSS tokens at render time (and redraw when
// the theme changes), a DOM builder that only ever sets text, JSON fetches, and the "Copy instructions for agent" button.
window.Insights = (() => {
    'use strict';

    const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

    const theme = () => ({
        palette: [1, 2, 3, 4, 5, 6, 7, 8].map(i => css(`--chart-${i}`)).filter(Boolean),
        text: css('--text-2'),
        grid: css('--border'),
        ok: css('--status-ok'),
        warn: css('--status-warn'),
        down: css('--status-down')
    });

    const entries = new Set();

    function draw(entry) {
        entry.instance?.destroy();
        const t = theme();
        Chart.defaults.color = t.text;
        Chart.defaults.borderColor = t.grid;
        entry.instance = new Chart(entry.canvas, entry.build(t));
    }

    /** Lets a canvas's chart go: destroyed, and no longer redrawn on a theme change. */
    function forget(canvas) {
        const old = [...entries].find(e => e.canvas === canvas);
        if (old) {
            old.instance?.destroy();
            entries.delete(old);
        }
    }

    /** Draws a chart from build(theme) → Chart.js config, and again on every theme change. */
    function chart(canvas, build) {
        forget(canvas);
        canvas.hidden = false;
        const message = canvas.parentElement.querySelector('.chart-empty');
        if (message) message.hidden = true;
        const entry = { canvas, build, instance: null };
        entries.add(entry);
        draw(entry);
    }

    const redrawAll = () => entries.forEach(draw);
    new MutationObserver(redrawAll).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'class'] });
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', redrawAll);

    const compact = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });
    const number = n => (n ?? 0) >= 10000 ? compact.format(n) : (n ?? 0).toLocaleString();

    const axes = (stacked, t) => ({
        x: { stacked, grid: { display: false }, ticks: { maxTicksLimit: 8 } },
        y: { stacked, beginAtZero: true, grid: { color: t.grid }, ticks: { callback: v => number(v), precision: 0 } }
    });

    /** Stacked bars, one dataset per series ({name, values}). */
    function stacked(labels, series, t, colors, legend = true) {
        return {
            type: 'bar',
            data: {
                labels,
                datasets: series.map((s, i) => ({
                    label: s.name, data: s.values, backgroundColor: (colors ?? t.palette)[i % (colors ?? t.palette).length], borderWidth: 0
                }))
            },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                interaction: { mode: 'index', intersect: false },
                plugins: { legend: { display: legend, position: 'bottom', labels: { boxWidth: 10 } } },
                scales: axes(true, t)
            }
        };
    }

    function line(labels, series, t) {
        return {
            type: 'line',
            data: {
                labels,
                datasets: series.map((s, i) => ({
                    label: s.name, data: s.values, borderColor: t.palette[i % t.palette.length],
                    backgroundColor: t.palette[i % t.palette.length], pointRadius: 0, tension: 0.25, borderWidth: 2
                }))
            },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                interaction: { mode: 'index', intersect: false },
                plugins: { legend: { display: series.length > 1, position: 'bottom', labels: { boxWidth: 10 } } },
                scales: axes(false, t)
            }
        };
    }

    function horizontal(labels, values, label, t) {
        return {
            type: 'bar',
            data: { labels, datasets: [{ label, data: values, backgroundColor: t.palette[0], borderWidth: 0, maxBarThickness: 28 }] },
            options: {
                indexAxis: 'y', responsive: true, maintainAspectRatio: false, animation: false,
                plugins: { legend: { display: false } },
                scales: {
                    x: { beginAtZero: true, grid: { color: t.grid }, ticks: { callback: v => number(v) } },
                    y: { grid: { display: false } }
                }
            }
        };
    }

    function sparkline(values, t) {
        return {
            type: 'line',
            data: { labels: values.map((_, i) => i), datasets: [{ data: values, borderColor: t.palette[0], pointRadius: 0, borderWidth: 2, tension: 0.3 }] },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                plugins: { legend: { display: false }, tooltip: { enabled: false } },
                scales: { x: { display: false }, y: { display: false, beginAtZero: true } }
            }
        };
    }

    /** An element with attributes and children; strings become text nodes, never HTML. */
    function el(tag, attrs, ...children) {
        const node = document.createElement(tag);
        for (const [key, value] of Object.entries(attrs ?? {})) {
            if (value === null || value === undefined || value === false) continue;
            if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
            else node.setAttribute(key, value === true ? '' : value);
        }
        for (const child of children.flat()) {
            if (child === null || child === undefined || child === false) continue;
            node.append(child instanceof Node ? child : document.createTextNode(String(child)));
        }
        return node;
    }

    /** GET as the signed-in person; a non-2xx throws with the problem's detail (never a stack or a body we did not write). */
    async function json(url) {
        const response = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (!response.ok) {
            let detail = `${response.status}`;
            try {
                const problem = await response.json();
                detail = problem.detail || problem.title || detail;
            } catch { /* not a problem document */ }
            throw new Error(detail);
        }
        return response.json();
    }

    /** A table from rows: columns are {title, value(row) → text or Node, num?}. */
    function table(columns, rows, empty) {
        if (rows.length === 0) return el('p', { class: 'chart-empty' }, empty);
        return el('table', { class: 'table' },
            el('thead', null, el('tr', null, columns.map(c => el('th', { class: c.num ? 'num' : null, scope: 'col' }, c.title)))),
            el('tbody', null, rows.map(r => el('tr', null, columns.map(c => el('td', { class: c.num ? 'num' : null }, c.value(r)))))));
    }

    /** Instead of a chart: a sentence (no data, or a source that is not tracked). */
    function empty(canvas, text) {
        forget(canvas);
        canvas.hidden = true;
        let message = canvas.parentElement.querySelector('.chart-empty');
        if (!message) {
            message = el('p', { class: 'chart-empty' });
            canvas.parentElement.append(message);
        }
        message.textContent = text;
        message.hidden = false;
    }

    const hasData = series => series.some(s => s.values.some(v => v > 0));

    /** Short day labels for the x axis (yyyy-MM-dd → "Oct 28"). */
    const dayLabels = days => days.map(d => new Date(`${d}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' }));

    function cost(estimate) {
        if (!estimate.priced) return 'n/a (no model price set)';
        const amount = estimate.amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return `≈ ${amount} ${estimate.currency}${estimate.unpricedModels.length ? ' (some models unpriced)' : ''}`;
    }

    return { chart, forget, stacked, line, horizontal, sparkline, el, json, table, empty, hasData, dayLabels, number, cost };
})();
