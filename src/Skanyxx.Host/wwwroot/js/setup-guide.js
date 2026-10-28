// The client half of the one setup block (Pages/Shared/Components/SetupGuide renders the server half). Pages whose
// guides come from their own API ({ id, title, cause, steps }: Agents, Tool Servers, Chat, Dashboard) draw them with
// skxSetupGuide.render, so every guide looks the same and copies the same kind of prompt. Copying itself is ui.js's
// data-copy. The texts are the API's fixed strings: setting names and steps, never a configured value.
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

    /** One guide as a <details class="card setup-guide">, open: it is shown because something is missing. */
    function render(g, { tone = 'warning', badge = 'needs setup' } = {}) {
        const box = el('details', undefined, 'card setup-guide');
        box.id = `setup-${g.id}`;
        box.dataset.setup = 'missing';
        box.dataset.guideId = g.id;
        box.open = true;
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
        const steps = el('ol', undefined, 'setup-steps');
        g.steps.forEach(s => steps.appendChild(el('li', s)));
        const agent = el('div', undefined, 'setup-agent');
        agent.append(copyButton(prompt(g), 'Copy instructions for agent', 'btn btn-primary'),
            el('span', 'A prompt for an AI coding agent: the cause and the steps. No secret values.', 'text-muted'));
        body.append(steps, agent);
        box.append(summary, body);
        return box;
    }

    return { render, prompt, copyButton };
})();
