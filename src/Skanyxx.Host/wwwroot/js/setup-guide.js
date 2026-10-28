// The client half of the one setup block (Pages/Shared/Components/SetupGuide renders the server half). Pages whose
// guides come from their own API draw them with skxSetupGuide.render, so every guide looks the same and copies the same
// kind of prompt. A guide is { id | code, title, cause } plus either steps (strings: Agents, Chat, Dashboard) or
// local / helm / verify ({ text, command? }: Tool Servers), and optionally the server's own agentPrompt. Copying is
// ui.js's data-copy. Text only (textContent), never markup: the texts are the API's fixed strings and names.
window.skxSetupGuide = (() => {
    const svg = (id, cls) => {
        const ns = 'http://www.w3.org/2000/svg';
        const s = document.createElementNS(ns, 'svg');
        s.setAttribute('class', cls);
        s.setAttribute('aria-hidden', 'true');
        s.setAttribute('focusable', 'false');
        const use = document.createElementNS(ns, 'use');
        use.setAttribute('href', `#${id}`);
        s.appendChild(use);
        return s;
    };

    const el = (tag, text, cls) => {
        const e = document.createElement(tag);
        if (text !== undefined) e.textContent = text;
        if (cls) e.className = cls;
        return e;
    };

    /** The "Copy instructions for agent" text: a prompt an AI coding agent can act on, from the guide's own text. */
    function prompt(g) {
        return [
            `Fix "${g.title}" in this Skanyxx install. Work in the Skanyxx repository.`,
            '',
            `Cause: ${g.cause}`,
            '',
            'Rules:',
            '- Never print, log, commit or paste a secret value (token, password, key, connection string).',
            '- Ask before changing a shared cluster.',
            '',
            'Steps:',
            ...g.steps.map((s, i) => `${i + 1}. ${s}`),
            ''
        ].join('\n');
    }

    /** A button that copies <paramref name="text"/> (ui.js data-copy). */
    function copyButton(text, label = 'Copy instructions for agent', cls = 'btn btn-secondary') {
        const b = el('button', undefined, cls);
        b.type = 'button';
        b.dataset.copy = text;
        b.append(svg('i-copy', 'icon'), el('span', label));
        b.lastChild.setAttribute('data-copy-label', '');
        return b;
    }

    function stepList(steps) {
        const list = el('ol', undefined, 'setup-steps');
        steps.forEach(step => {
            const item = el('li', typeof step === 'string' ? step : step.text);
            if (step.command) {
                const row = el('div', undefined, 'setup-command');
                row.append(el('code', step.command), copyButton(step.command, 'Copy', 'btn btn-secondary copy-btn'));
                item.append(row);
            }
            list.appendChild(item);
        });
        return list;
    }

    /** One guide as a <details class="card setup-guide">, open: it is shown because something is missing. */
    function render(g, { tone = 'warning', badge = 'needs setup', open = true } = {}) {
        const id = g.id ?? g.code;
        const box = el('details', undefined, 'card setup-guide');
        box.id = `setup-${id}`;
        box.dataset.setup = 'missing';
        box.dataset.guideId = id;
        box.open = open;
        const summary = el('summary');
        const heading = el('span', undefined, 'setup-guide-heading');
        heading.append(el('span', g.title, 'setup-guide-title'));
        const status = el('span', undefined, `badge badge-${tone}`);
        const dot = el('span', undefined, 'badge-dot');
        dot.setAttribute('aria-hidden', 'true');
        status.append(dot, badge);
        summary.append(svg('i-compass', 'setup-guide-icon'), heading, status, svg('i-chevron-down', 'icon setup-guide-chevron'));
        const body = el('div', undefined, 'setup-guide-body');
        body.append(el('p', g.cause));
        const sections = g.steps ? [[null, g.steps]]
            : [['Fix it: local development', g.local], ['Fix it: Helm install', g.helm], ['Verify', g.verify]];
        for (const [title, steps] of sections) {
            if (!steps || !steps.length) continue;
            if (title) body.append(el('h4', title));
            body.append(stepList(steps));
        }
        const agent = el('div', undefined, 'setup-agent');
        agent.append(copyButton(g.agentPrompt ?? prompt(g), 'Copy instructions for agent', 'btn btn-primary'),
            el('span', 'A prompt for an AI coding agent: the cause and the steps. No secret values.', 'text-muted'));
        body.append(agent);
        box.append(summary, body);
        return box;
    }

    return { render, prompt, copyButton };
})();
