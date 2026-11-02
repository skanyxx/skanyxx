// The Chat page (kagent-style): agent picker and the person's chats on the left, the streamed conversation in the
// middle, the agent's details on the right. The server is the source of truth: a conversation is reopened from
// GET /api/chat/conversations/{id} (kagent's session, read as its owner), and only sending a first message creates one.
// What is open lives in the URL (/Chat/{namespace}/{agent}/{session}) and in this tab's sessionStorage, so a reload or
// a trip to another page comes back to the same conversation. Values from the API are rendered as text, except agent
// answers, which go through ChatMarkdown (escape first, then a fixed set of tags).
(() => {
    const STORE_KEY = 'skanyxx.chat';
    const app = document.getElementById('chatApp');
    const $ = id => document.getElementById(id);
    const md = window.ChatMarkdown;
    const agentKey = a => `${a.namespace}/${a.name}`;
    // ?ticket=SDB-58 (comma-separated for several) links a new conversation to those Jira tickets in the chat index.
    const ticketKeys = (new URLSearchParams(location.search).get('ticket') || '').split(',')
        .map(t => t.trim()).filter(t => /^[A-Z][A-Z0-9_]{0,29}-[1-9][0-9]{0,9}$/.test(t));

    const state = {
        agents: [],
        agent: null,
        conversationId: null,
        title: 'New chat',
        items: [],
        usage: { prompt: 0, completion: 0, total: 0 },
        clientTokens: 0,
        price: null,
        sessions: [],
        limits: null,
        attachments: [],
        streaming: null,
        notes: new Map(),
        askFresh: false,
        maskSensitive: true,
        scan: null
    };

    // ---------- small helpers ----------

    function h(tag, attrs = {}, ...children) {
        const el = document.createElement(tag);
        for (const [k, v] of Object.entries(attrs)) {
            if (v === undefined || v === null || v === false) continue;
            if (k === 'class') el.className = v;
            else if (k === 'text') el.textContent = v;
            else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
            else el.setAttribute(k, v === true ? '' : v);
        }
        for (const c of children.flat()) if (c !== null && c !== undefined && c !== false) el.append(c);
        return el;
    }

    // The shared client (js/ui.js): problem+json becomes the error's message, with its status.
    const call = (method, url, body) => skx.api(method, url, body);

    // An empty conversation, a failure, a load: the design system's states, built with text only.
    function emptyState(title, body, extra) {
        return h('div', { class: 'empty-state' }, skx.icon('i-chat', 'empty-state-icon'),
            h('h3', { class: 'empty-state-title', text: title }), body ? h('div', { class: 'empty-state-body', text: body }) : null, extra ?? null);
    }

    function errorAlert(title, message, retry, reason) {
        return h('div', { class: 'alert alert-error', role: 'alert' }, skx.icon('i-alert-circle'),
            h('div', { class: 'alert-body' }, h('div', { class: 'alert-title', text: title }), h('div', { class: 'message-content plain', text: message }),
                skx.kagentReason(reason),
                retry ? h('div', { class: 'mt-1' }, h('button', { type: 'button', class: 'btn btn-secondary btn-sm', onclick: retry }, 'Try again')) : null));
    }

    const formatTokens = n => n >= 10000 ? `${(n / 1000).toFixed(1)}k` : Number(n || 0).toLocaleString();
    const tokensLabel = u => u && u.total ? `${formatTokens(u.total)} tokens (in ${formatTokens(u.prompt)} · out ${formatTokens(u.completion)})` : '';
    const sizeLabel = bytes => bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
    const timeLabel = at => at ? new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';

    function notice(text) {
        $('chatNotice').textContent = text || '';
        $('chatNotice').hidden = !text;
    }

    // ---------- where we are: URL + this tab's storage ----------

    function remember() {
        try {
            if (state.agent)
                sessionStorage.setItem(STORE_KEY, JSON.stringify({ namespace: state.agent.namespace, agent: state.agent.name, session: state.conversationId }));
        } catch { /* storage off: the URL still carries it */ }
    }

    function pathFor(agent, conversationId) {
        const base = `/Chat/${encodeURIComponent(agent.namespace)}/${encodeURIComponent(agent.name)}`;
        return (conversationId ? `${base}/${encodeURIComponent(conversationId)}` : base) + location.search.replace(/[?&]agent=[^&]*/, '').replace(/^&/, '?');
    }

    function setUrl(push) {
        if (!state.agent) return;
        const path = pathFor(state.agent, state.conversationId);
        if (path !== location.pathname + location.search)
            history[push ? 'pushState' : 'replaceState']({ chat: true }, '', path);
        remember();
    }

    function routeFromPath(path) {
        const parts = path.split('/').filter(Boolean).map(decodeURIComponent);
        return parts[0]?.toLowerCase() === 'chat' && parts.length >= 3 ? { namespace: parts[1], agent: parts[2], session: parts[3] || null } : null;
    }

    function initialRoute() {
        if (app.dataset.namespace && app.dataset.agent)
            return { namespace: app.dataset.namespace, agent: app.dataset.agent, session: app.dataset.session || null };
        // The Agents page's older link: ?agent=<namespace/name or name>.
        const legacy = new URLSearchParams(location.search).get('agent');
        if (legacy)
            return legacy.includes('/') ? { namespace: legacy.split('/')[0], agent: legacy.split('/')[1], session: null } : { namespace: null, agent: legacy, session: null };
        try {
            const saved = JSON.parse(sessionStorage.getItem(STORE_KEY) || 'null');
            if (saved && saved.agent) return saved;
        } catch { /* nothing saved */ }
        return {};
    }

    // ---------- agents ----------

    async function loadAgents(route) {
        let agents;
        try {
            agents = await call('GET', '/api/chat/agents');
        } catch (e) {
            showProblem(e.message, e.status);
            return;
        }
        hideProblem();
        state.agents = agents;
        if (agents.length === 0) {
            showEmptyAgents();
            return;
        }
        const wanted = agents.find(a => a.namespace === route.namespace && a.name === route.agent)
            || (!route.namespace && agents.find(a => a.name === route.agent));
        if (route.agent && !wanted)
            notice(`The agent ${route.namespace ? route.namespace + '/' : ''}${route.agent} is not available to you. Pick another one.`);
        const agent = wanted || agents.find(a => a.name === 'seed') || agents[0];
        await selectAgent(agent, wanted ? route.session : null, false);
    }

    function showEmptyAgents() {
        $('chatMessages').replaceChildren(emptyState('No agents to chat with yet',
            'An agent appears here once a supervisor adds it to Chat on the Agents page.'));
    }

    async function selectAgent(agent, conversationId, push) {
        state.agent = agent;
        $('agentName').textContent = agent.name;
        $('agentModel').textContent = [agent.namespace, agent.model].filter(Boolean).join(' · ');
        $('chatSubtitle').textContent = agentKey(agent);
        loadDetails(agent);
        renderSessions();
        if (conversationId) await openConversation(conversationId, push);
        else newChat(push);
    }

    function openPicker() {
        if (!state.agents.length) return;
        $('pickerSearch').value = '';
        renderPicker();
        $('agentPicker').showModal();
        $('pickerSearch').focus();
    }

    // Ready but a tool server it names gives no tools (D554): the card says so, by name for builders and up only.
    function statusDot(a) {
        const [cls, label] = !a.ready ? ['starting', 'Starting'] : a.toolProblem ? ['unhealthy', 'A tool is unavailable'] : ['ready', 'Ready'];
        return h('span', { class: 'status-dot ' + cls, title: label, 'aria-label': label });
    }

    function renderPicker() {
        const q = $('pickerSearch').value.trim().toLowerCase();
        const shown = state.agents.filter(a => !q || [a.name, a.namespace, a.model, a.description].some(v => (v || '').toLowerCase().includes(q)));
        $('pickerGrid').replaceChildren(...shown.map(a => h('button', {
            type: 'button', role: 'option', class: 'agent-card' + (state.agent && agentKey(a) === agentKey(state.agent) ? ' selected' : ''),
            'aria-selected': state.agent && agentKey(a) === agentKey(state.agent) ? 'true' : 'false',
            onclick: () => {
                $('agentPicker').close();
                if (state.streaming || (state.agent && agentKey(a) === agentKey(state.agent))) return;
                notice('');
                selectAgent(a, null, true);
            }
        },
            h('span', { class: 'agent-card-top' },
                h('span', { class: 'agent-card-name', text: a.name }),
                statusDot(a)),
            h('span', { class: 'agent-card-meta', text: [a.namespace, a.model].filter(Boolean).join(' · ') }),
            h('span', { class: 'agent-card-desc', text: a.description || 'No description.' }),
            a.toolProblem ? h('span', { class: 'agent-card-problem', text: a.toolProblem }) : null)));
        if (!shown.length) $('pickerGrid').append(h('p', { class: 'muted', text: 'No agent matches.' }));
    }

    // ---------- agent details (right panel) ----------

    async function loadDetails(agent) {
        state.price = null;
        renderCost();
        const body = $('detailsBody');
        body.replaceChildren(h('p', { class: 'muted', text: 'Loading…' }));
        let d;
        try {
            d = await call('GET', `/api/chat/agents/${encodeURIComponent(agent.namespace)}/${encodeURIComponent(agent.name)}`);
        } catch (e) {
            body.replaceChildren(h('p', { class: 'muted', text: e.message }));
            return;
        }
        if (!state.agent || agentKey(state.agent) !== agentKey(agent)) return;
        state.price = d.price || null;
        renderCost();
        $('detailsLink').href = d.agentsPageUrl;
        $('detailsLink').hidden = !d.agentsPageUrl;
        const section = (title, ...content) => h('section', { class: 'details-section' }, h('h4', { text: title }), ...content);
        const servers = d.toolServers.map(s => h('details', { class: 'tool-server', open: d.toolServers.length === 1 },
            h('summary', {}, h('span', { text: s.ref }), h('span', { class: 'count', text: s.allTools ? `all ${s.tools.length}` : String(s.tools.length) })),
            s.problem ? h('p', { class: 'tool-server-problem', text: s.problem }) : null,
            s.allTools ? h('p', { class: 'muted small', text: 'Every tool this server offers.' }) : null,
            h('ul', { class: 'tool-list' }, ...s.tools.map(t => h('li', {}, h('code', { text: t.name }), t.description ? h('span', { class: 'muted small', text: t.description }) : null)))));
        const memory = d.memory === null
            ? h('p', { class: 'muted', text: 'Company memory is not installed.' })
            : d.memory.length === 0
                ? h('p', { text: 'Default: searches company cards and your own; writes only your own.' })
                : h('ul', { class: 'plain-list' }, ...d.memory.map(g => h('li', {},
                    h('code', { text: g.scope === 'personal' ? 'your own' : g.scope }),
                    h('span', { class: 'muted small', text: ' ' + [g.search ? 'search' : null, g.upsert ? 'write' : null].filter(Boolean).join(' + ') || ' no access' }))));
        body.replaceChildren(...[
            h('p', { class: 'details-desc', text: d.description || 'No description.' }),
            section('Model', h('p', { text: d.model || d.modelConfig || 'Not reported' }),
                d.modelProvider || d.modelConfig ? h('p', { class: 'muted small', text: [d.modelProvider, d.modelConfig].filter(Boolean).join(' · ') }) : null,
                h('p', { class: 'muted small', text: d.acceptsImages ? 'Reads images.' : 'Does not read images.' })),
            section('Tools', d.toolServers.length ? servers : h('p', { class: 'muted', text: 'No tools.' })),
            d.agentTools.length ? section('Agents it can ask', h('ul', { class: 'plain-list' }, ...d.agentTools.map(a => h('li', {}, h('code', { text: a }))))) : null,
            d.skills.length || d.a2aSkills.length
                ? section('Skills', h('ul', { class: 'plain-list' },
                    ...d.skills.map(s => h('li', {}, h('code', { text: s }))),
                    ...d.a2aSkills.map(s => h('li', {}, h('strong', { text: s.name || s.id }), s.description ? h('span', { class: 'muted small', text: ' ' + s.description }) : null))))
                : null,
            section('Memory access', memory),
            d.ready && d.toolProblem
                ? h('p', { class: 'tool-server-problem', text: d.toolProblem })
                : h('p', { class: 'muted small', text: d.ready ? 'Ready.' : 'Starting: the first answer may take a minute.' })
        ].filter(Boolean));
    }

    // ---------- conversations (left panel) ----------

    async function loadSessions() {
        try {
            state.sessions = await call('GET', '/api/chat/conversations');
            renderSessions();
        } catch (e) {
            $('sessionList').replaceChildren(h('p', { class: 'muted small', text: e.message }));
        }
    }

    function dayBucket(at) {
        const day = new Date(at); day.setHours(0, 0, 0, 0);
        const today = new Date(); today.setHours(0, 0, 0, 0);
        const days = Math.round((today - day) / 86400000);
        return days <= 0 ? 'Today' : days === 1 ? 'Yesterday' : days < 7 ? 'Previous 7 days' : 'Older';
    }

    function renderSessions() {
        const q = $('sessionSearch').value.trim().toLowerCase();
        const shown = state.sessions.filter(s => !q || s.title.toLowerCase().includes(q) || s.agentName.includes(q));
        const groups = new Map();
        const current = state.agent ? agentKey(state.agent) : null;
        if (current) groups.set(current, []);
        for (const s of shown) {
            const key = `${s.agentNamespace}/${s.agentName}`;
            if (!groups.has(key)) groups.set(key, []);
            groups.get(key).push(s);
        }
        const list = $('sessionList');
        list.replaceChildren();
        for (const [key, sessions] of groups) {
            if (!sessions.length && key !== current) continue;
            const buckets = new Map();
            for (const s of sessions) {
                const b = dayBucket(s.updatedAt || s.createdAt);
                if (!buckets.has(b)) buckets.set(b, []);
                buckets.get(b).push(s);
            }
            const available = state.agents.some(a => agentKey(a) === key);
            list.append(h('details', { class: 'session-group', open: key === current || !!q },
                h('summary', {}, h('span', { text: key }), h('span', { class: 'count', text: String(sessions.length) })),
                sessions.length ? null : h('p', { class: 'muted small', text: q ? 'No chat matches.' : 'No chats yet.' }),
                ...[...buckets].map(([label, rows]) => h('div', { class: 'session-bucket' },
                    h('div', { class: 'bucket-label', text: label }),
                    ...rows.map(s => sessionRow(s, available))))));
        }
    }

    function sessionRow(s, available) {
        const active = s.id === state.conversationId;
        const link = h('a', {
            class: 'session-title', href: available ? `/Chat/${encodeURIComponent(s.agentNamespace)}/${encodeURIComponent(s.agentName)}/${encodeURIComponent(s.id)}` : undefined,
            'aria-current': active ? 'page' : undefined, title: available ? s.title : `${s.title} (this agent is no longer offered in Chat)`,
            onclick: e => {
                e.preventDefault();
                if (!available || state.streaming || active) return;
                const agent = state.agents.find(a => a.namespace === s.agentNamespace && a.name === s.agentName);
                notice('');
                if (agentKey(agent) !== agentKey(state.agent)) selectAgent(agent, s.id, true);
                else openConversation(s.id, true);
            }
        }, s.title);
        const row = h('div', { class: 'session-row' + (active ? ' active' : '') + (available ? '' : ' unavailable'), 'data-id': s.id }, link,
            h('span', { class: 'session-actions' },
                h('button', { type: 'button', class: 'icon-btn small', title: 'Rename', 'aria-label': `Rename ${s.title}`, onclick: () => renameInline(row, s) }, '✎'),
                h('button', { type: 'button', class: 'icon-btn small', title: 'Delete', 'aria-label': `Delete ${s.title}`, onclick: () => removeSession(s) }, '×')));
        return row;
    }

    function renameInline(row, s) {
        const input = h('input', { class: 'rename-input', value: s.title, maxlength: '120', 'aria-label': 'New title' });
        row.replaceChildren(input);
        input.focus();
        input.select();
        let done = false;
        const finish = async save => {
            if (done) return;
            done = true;
            const title = input.value.trim();
            if (save && title && title !== s.title) {
                try {
                    const renamed = await call('PATCH', `/api/chat/conversations/${encodeURIComponent(s.id)}`, { title });
                    s.title = renamed.title;
                    if (s.id === state.conversationId) setTitle(renamed.title);
                } catch (e) {
                    notice(e.message);
                }
            }
            renderSessions();
        };
        input.addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); finish(true); }
            if (e.key === 'Escape') finish(false);
        });
        input.addEventListener('blur', () => finish(true));
    }

    async function removeSession(s) {
        if (!await skx.confirm({ title: 'Delete this chat?', body: `"${s.title}": its messages and files are deleted for good.`, confirm: 'Delete', danger: true })) return;
        try {
            await call('DELETE', `/api/chat/conversations/${encodeURIComponent(s.id)}`);
        } catch (e) {
            notice(e.message);
            return;
        }
        state.sessions = state.sessions.filter(x => x.id !== s.id);
        if (s.id === state.conversationId && !state.streaming) newChat(false);
        renderSessions();
    }

    // ---------- the conversation ----------

    function setTitle(title) {
        state.title = title || 'New chat';
        $('chatTitle').textContent = state.title;
        document.title = `${state.title} - Chat - Skanyxx`;
    }

    // The model's tokens and what Skanyxx sent are two counters, never added together (D548).
    function setUsage(usage, clientTokens) {
        state.usage = usage || { prompt: 0, completion: 0, total: 0 };
        state.clientTokens = clientTokens || 0;
        $('usageChipText').textContent = `${formatTokens(state.usage.total)} tokens`;
        $('clientTokens').textContent = formatTokens(state.clientTokens);
        $('totalTokens').textContent = formatTokens(state.usage.total);
        $('promptTokens').textContent = formatTokens(state.usage.prompt);
        $('completionTokens').textContent = formatTokens(state.usage.completion);
        renderCost();
    }

    // The configured price of the agent's model (Analytics:Prices, D549), input and output apart; n/a without one.
    function renderCost() {
        const price = state.price;
        const cost = $('estCost');
        if (!price) {
            cost.textContent = 'n/a';
            cost.title = 'No price is configured for this agent\'s model';
            $('costBasis').textContent = 'No price is configured for this agent\'s model, so it shows n/a.';
            return;
        }
        const amount = state.usage.prompt / 1e6 * price.inputPerMillion + state.usage.completion / 1e6 * price.outputPerMillion;
        cost.textContent = `≈ ${amount.toLocaleString(undefined, { style: 'currency', currency: price.currency, maximumFractionDigits: 4 })}`;
        cost.title = `${price.model}: ${price.inputPerMillion} in, ${price.outputPerMillion} out per million tokens (${price.currency})`;
        $('costBasis').textContent = `Estimated from the model's tokens and the configured price for ${price.model}; what Skanyxx sent is not priced.`;
    }

    function newChat(push) {
        state.conversationId = null;
        state.items = [];
        state.notes.clear();
        setTitle('New chat');
        setUsage(null);
        setUrl(push);
        renderItems();
        renderSessions();
        $('chatInput').focus();
    }

    // Reopens from the server (never creates one). Not the caller's, or gone: a new chat with this agent instead.
    async function openConversation(id, push) {
        state.conversationId = id;
        state.items = [];
        state.notes.clear();
        setUrl(push);
        skx.state.loading($('chatMessages'), { rows: 3 });
        try {
            const view = await call('GET', `/api/chat/conversations/${encodeURIComponent(id)}`);
            if (state.conversationId !== id) return;
            const agent = state.agents.find(a => a.namespace === view.agentNamespace && a.name === view.agentName);
            if (agent && agentKey(agent) !== agentKey(state.agent)) {
                state.agent = agent;
                $('agentName').textContent = agent.name;
                $('agentModel').textContent = [agent.namespace, agent.model].filter(Boolean).join(' · ');
                $('chatSubtitle').textContent = agentKey(agent);
                loadDetails(agent);
                setUrl(false);
            }
            state.items = view.items;
            setTitle(view.title);
            setUsage(view.usage, view.clientTokens);
            renderItems();
        } catch (e) {
            if (state.conversationId !== id) return;
            if (e.status === 404 || e.status === 403) {
                notice('That chat is not one of yours, or it no longer exists. This is a new chat.');
                newChat(false);
                return;
            }
            // kagent down: keep the conversation, so what is sent next still continues it.
            setTitle(state.sessions.find(s => s.id === id)?.title || 'Chat');
            $('chatMessages').replaceChildren(errorAlert('Earlier messages could not be shown', e.message, () => openConversation(id, false)));
        }
        renderSessions();
    }

    // Questions anyone can start with; a click puts one in the box, it is never sent by itself.
    const examples = ['How does our refund process work?', 'What did we decide about remote work?', 'Who do I ask about invoices?'];

    function welcome() {
        const name = state.agent ? state.agent.name : 'an agent';
        const chips = h('div', { class: 'chips example-questions', role: 'group', 'aria-label': 'Example questions' },
            examples.map(q => h('button', { type: 'button', class: 'chip', onclick: () => { const input = $('chatInput'); input.value = q; autosize(); input.focus(); } }, q)));
        return emptyState(`New chat with ${name}`,
            state.agent?.description || 'Ask how the company works: the agent searches company memory and cites the cards it used.',
            h('div', {}, chips, ticketKeys.length ? h('p', { class: 'muted small mt-1', text: `Linked to ${ticketKeys.join(', ')}.` }) : null));
    }

    function renderItems() {
        const box = $('chatMessages');
        box.replaceChildren();
        if (!state.items.length) {
            box.append(welcome());
            return;
        }
        // Only questions and answers: tool steps are background work, and the server sends none (D555).
        state.items.forEach((item, index) => box.append(item.kind === 'user' ? userBubble(item) : answerBubble(item, index)));
        box.scrollTop = box.scrollHeight;
    }

    function userBubble(item) {
        return h('div', { class: 'chat-message user' },
            h('div', { class: 'message-bubble' },
                h('div', { class: 'message-content plain', text: item.text }),
                item.attachments?.length ? h('div', { class: 'message-files' }, ...item.attachments.map(f =>
                    h('a', { class: 'file-chip', href: `/api/chat/files/${encodeURIComponent(f.id)}`, download: f.name, title: 'Download (only you can)' }, f.name))) : null,
                h('div', { class: 'message-footer' }, h('span', { text: timeLabel(item.at) }), item.masked ? h('span', { class: 'masked-badge', title: 'Sensitive data was masked before sending', text: 'Masked' }) : null)));
    }

    function answerBubble(item, index) {
        const content = h('div', { class: 'message-content markdown' });
        content.innerHTML = md.render(item.text);
        const note = state.notes.get(index);
        return h('div', { class: 'chat-message assistant' },
            h('div', { class: 'message-bubble' }, content,
                h('div', { class: 'message-footer' },
                    h('span', { text: timeLabel(item.at) }),
                    item.usage?.total ? h('span', { class: 'message-tokens', text: tokensLabel(item.usage) }) : null,
                    h('button', { type: 'button', class: 'link-btn copy-answer', 'data-index': String(index) }, 'Copy'))),
            note || null);
    }

    // Which prior chats and cards a first turn was given, or that the answer came from the cache.
    function relatedNote(done, question) {
        const used = [
            ...(done.relatedSessions || []).map(s => `chat "${s.title}" (${new Date(s.updatedAt).toLocaleDateString()})`),
            ...(done.memoryCards || []).map(c => `card "${c.what}"`)
        ];
        const notes = [];
        if (done.cached) notes.push('Answered from the cache: you asked this before, so no tokens were spent.');
        if (used.length) notes.push(`Used prior work: ${used.join('; ')}.`);
        if (!notes.length) return null;
        return h('div', { class: 'related-note' }, notes.join(' '),
            done.cached ? h('button', { type: 'button', class: 'link-btn', onclick: () => askAgainFresh(question) }, 'Ask again without the cache') : null);
    }

    function askAgainFresh(question) {
        if (state.streaming) return;
        newChat(true);
        state.askFresh = true;
        send(question, []);
    }

    // ---------- sending (streamed) ----------

    async function readEvents(body, onEvent) {
        const reader = body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        for (;;) {
            const { value, done } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            let at;
            while ((at = buffer.indexOf('\n\n')) >= 0) {
                const raw = buffer.slice(0, at);
                buffer = buffer.slice(at + 2);
                let type = 'message', data = '';
                for (const line of raw.split('\n')) {
                    if (line.startsWith('event:')) type = line.slice(6).trim();
                    else if (line.startsWith('data:')) data += line.slice(5).trim();
                }
                if (data) onEvent(type, JSON.parse(data));
            }
        }
    }

    function liveTurn() {
        const box = $('chatMessages');
        const turn = { draft: '', answer: '', usage: null, bubble: null };
        const working = () => h('span', { class: 'typing-dots', 'aria-label': 'The agent is working' }, h('i'), h('i'), h('i'));
        const bubble = h('div', { class: 'chat-message assistant live' },
            h('div', { class: 'message-bubble' }, h('div', { class: 'message-content markdown' }, working())));
        box.append(bubble);
        turn.bubble = bubble;
        turn.render = () => {
            const text = turn.answer || turn.draft;
            const content = bubble.querySelector('.message-content');
            // An empty draft (a tool call took it away) shows the agent working again.
            if (text) content.innerHTML = md.render(text) + '<span class="cursor" aria-hidden="true"></span>';
            else content.replaceChildren(working());
            box.scrollTop = box.scrollHeight;
        };
        turn.remove = () => bubble.remove();
        return turn;
    }

    function failedBubble(message, retry, reason) {
        return errorAlert('The agent did not answer', message, retry, reason);
    }

    function setStreaming(on) {
        $('sendBtn').hidden = on;
        $('stopBtn').hidden = !on;
        $('attachBtn').disabled = on;
    }

    async function send(text, fileIds, sentFiles) {
        if (state.streaming || !state.agent) return;
        let message = text;
        let masked = false;
        if (state.maskSensitive && Scanner.scan(text).length) {
            message = Scanner.mask(text);
            masked = message !== text;
        }
        const wasNew = !state.conversationId;
        const attachments = sentFiles || state.attachments.filter(a => fileIds.includes(a.id)).map(a => ({ id: a.id, name: a.name }));
        if (!state.items.length) $('chatMessages').replaceChildren();
        const userItem = { kind: 'user', text, attachments, at: new Date().toISOString(), masked };
        state.items.push(userItem);
        $('chatMessages').append(userBubble(userItem));
        const turn = liveTurn();
        const controller = new AbortController();
        state.streaming = { controller, turn };
        setStreaming(true);
        const fresh = state.askFresh;
        state.askFresh = false;
        const retry = () => {
            turn.remove();
            failed?.remove();
            state.items.splice(state.items.indexOf(userItem), 1);
            renderItems();
            send(text, fileIds, attachments);
        };
        let failed = null;
        const fail = (msg, reason) => {
            turn.remove();
            failed = failedBubble(msg, retry, reason);
            $('chatMessages').append(failed);
            $('chatMessages').scrollTop = $('chatMessages').scrollHeight;
        };
        let done = null;
        try {
            const response = await fetch('/api/chat/stream', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal,
                body: JSON.stringify({
                    message,
                    agentNamespace: state.agent.namespace,
                    agentName: state.agent.name,
                    conversationId: state.conversationId,
                    // Links a new conversation only: a later turn must not re-add a link the person removed.
                    ticketKeys: state.conversationId ? [] : ticketKeys,
                    noCache: fresh,
                    fileIds
                })
            });
            if (!response.ok) {
                // A 429 is the quota hook's refusal (IChatQuota): its words are shown as they are, and the quota meter
                // (quota-mine.js) reloads to offer "Request more".
                const problem = await response.json().catch(() => ({}));
                fail(problemText(problem, response.status), problem.kagentReason);
                if (response.status === 429) document.dispatchEvent(new CustomEvent('skx:quota-changed'));
                return;
            }
            clearAttachments(fileIds);
            await readEvents(response.body, (type, data) => {
                if (type === 'session') {
                    if (data.isNew) {
                        state.conversationId = data.conversationId;
                        setTitle(data.title);
                        setUrl(true);
                        state.sessions.unshift({ id: data.conversationId, title: data.title, agentNamespace: state.agent.namespace, agentName: state.agent.name, updatedAt: new Date().toISOString() });
                        renderSessions();
                    }
                } else if (type === 'delta') { turn.draft += data.text; turn.render(); }
                else if (type === 'text') { turn.draft = data.text; turn.render(); }
                else if (type === 'answer') { turn.answer = data.text; turn.render(); }
                else if (type === 'usage') turn.usage = data;
                else if (type === 'done') done = data;
                else if (type === 'error') {
                    if (data.conversationGone && wasNew) forgetNewConversation();
                    fail(data.message, data.kagentReason);
                }
            });
            if (done) await finish(turn, done, text);
            document.dispatchEvent(new CustomEvent('skx:quota-changed'));
        } catch (e) {
            if (e.name === 'AbortError') {
                stopped(turn, wasNew, text);
                // A stopped turn counts too (its prompt at least): the meter reloads.
                document.dispatchEvent(new CustomEvent('skx:quota-changed'));
            }
            else fail('The connection to Skanyxx was lost before the answer finished. Try again.');
        } finally {
            state.streaming = null;
            setStreaming(false);
        }
    }

    async function finish(turn, done, question) {
        turn.remove();
        if (done.cached) {
            // The answer belongs to an earlier conversation, which the next message continues: show it.
            await openConversation(done.conversationId, true);
            state.notes.set(state.items.length - 1, relatedNote(done, question));
            renderItems();
            return;
        }
        state.items.push({ kind: 'assistant', text: turn.answer || turn.draft, usage: done.usage, at: new Date().toISOString() });
        const note = relatedNote(done, question);
        if (note) state.notes.set(state.items.length - 1, note);
        setUsage({
            prompt: state.usage.prompt + (done.usage?.prompt || 0),
            completion: state.usage.completion + (done.usage?.completion || 0),
            total: state.usage.total + (done.usage?.total || 0)
        }, state.clientTokens + (done.clientTokens || 0));
        renderItems();
        const row = state.sessions.find(s => s.id === state.conversationId);
        if (row) {
            row.updatedAt = new Date().toISOString();
            state.sessions = [row, ...state.sessions.filter(s => s !== row)];
            renderSessions();
        }
    }

    // A stopped first turn leaves no conversation (the server deletes it): the message goes back into the box.
    function stopped(turn, wasNew, text) {
        if (wasNew) {
            turn.remove();
            state.items.pop();
            forgetNewConversation();
            renderItems();
            $('chatInput').value = text;
            notice('Stopped. Nothing was saved, since the agent had not answered yet.');
            return;
        }
        if (turn.answer || turn.draft) state.items.push({ kind: 'assistant', text: (turn.answer || turn.draft) + '\n\n_(stopped)_', at: new Date().toISOString() });
        turn.remove();
        renderItems();
        notice('Stopped.');
    }

    function forgetNewConversation() {
        state.sessions = state.sessions.filter(s => s.id !== state.conversationId);
        state.conversationId = null;
        setTitle('New chat');
        setUrl(false);
        renderSessions();
    }

    function submit() {
        const input = $('chatInput');
        const text = input.value.trim();
        const pending = state.attachments.filter(a => a.status === 'uploading');
        if (pending.length) { notice('Wait until your files are uploaded.'); return; }
        if (state.attachments.some(a => a.status === 'ready' && a.kind === 'image') && !state.agent?.acceptsImages) {
            notice(`${state.agent.name} does not read images. Remove the image, or pick an agent that reads images.`);
            return;
        }
        const fileIds = state.attachments.filter(a => a.status === 'ready').map(a => a.id);
        if (!text && !fileIds.length) return;
        notice('');
        input.value = '';
        autosize();
        $('securityWarning').hidden = true;
        send(text, fileIds);
    }

    // ---------- files ----------

    function extensionOf(name) { const at = name.lastIndexOf('.'); return at >= 0 ? name.slice(at).toLowerCase() : ''; }

    function addFiles(list) {
        const limits = state.limits;
        for (const file of list) {
            const chip = { localId: crypto.randomUUID(), name: file.name, size: file.size, status: 'uploading' };
            const ext = extensionOf(file.name);
            chip.kind = limits && limits.imageExtensions.includes(ext) ? 'image' : 'text';
            if (limits && !limits.textExtensions.includes(ext) && !limits.imageExtensions.includes(ext))
                Object.assign(chip, { status: 'error', error: `Not a supported type. Chat takes ${limits.textExtensions.join(' ')} and images (${limits.imageExtensions.join(' ')}).` });
            else if (limits && file.size > (chip.kind === 'image' ? limits.maxImageBytes : limits.maxTextBytes))
                Object.assign(chip, { status: 'error', error: `Larger than the ${sizeLabel(chip.kind === 'image' ? limits.maxImageBytes : limits.maxTextBytes)} limit.` });
            else if (limits && state.attachments.filter(a => a.status !== 'error').length >= limits.maxFilesPerMessage)
                Object.assign(chip, { status: 'error', error: `At most ${limits.maxFilesPerMessage} files per message.` });
            else if (chip.kind === 'image' && !state.agent?.acceptsImages)
                Object.assign(chip, { status: 'error', error: `${state.agent?.name || 'This agent'} does not read images. Describe it in words instead.` });
            state.attachments.push(chip);
            if (chip.status === 'uploading') upload(chip, file);
        }
        renderAttachments();
    }

    async function upload(chip, file) {
        const form = new FormData();
        form.append('file', file, file.name);
        try {
            const response = await fetch('/api/chat/files', { method: 'POST', body: form });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(problemText(data, response.status));
            Object.assign(chip, { status: 'ready', id: data.id, kind: data.kind, warning: data.warning });
        } catch (e) {
            Object.assign(chip, { status: 'error', error: e.message });
        }
        renderAttachments();
    }

    function removeAttachment(chip) {
        state.attachments = state.attachments.filter(a => a !== chip);
        if (chip.id) fetch(`/api/chat/files/${encodeURIComponent(chip.id)}`, { method: 'DELETE' });
        renderAttachments();
    }

    function clearAttachments(sentIds) {
        state.attachments = state.attachments.filter(a => !sentIds.includes(a.id) && a.status !== 'error');
        renderAttachments();
    }

    function renderAttachments() {
        $('attachments').replaceChildren(...state.attachments.map(a => h('div', { class: `attachment ${a.status}` + (a.warning ? ' warned' : '') },
            h('span', { class: 'attachment-name', text: a.name }),
            h('span', { class: 'muted small', text: a.status === 'uploading' ? 'uploading…' : sizeLabel(a.size) }),
            a.warning ? h('span', { class: 'attachment-warning', role: 'alert', text: a.warning }) : null,
            a.error ? h('span', { class: 'attachment-error', role: 'alert', text: a.error }) : null,
            h('button', { type: 'button', class: 'icon-btn small', 'aria-label': `Remove ${a.name}`, onclick: () => removeAttachment(a) }, '×'))));
    }

    async function loadLimits() {
        try {
            state.limits = await call('GET', '/api/chat/limits');
            $('fileInput').accept = [...state.limits.textExtensions, ...state.limits.imageExtensions].join(',');
        } catch { /* the server checks every upload anyway */ }
    }

    // ---------- voice (Web Speech API, on-device only) ----------
    // Only the recognised text reaches the input; recognition runs on the device (processLocally), so no audio leaves
    // the browser. Browsers that can only recognise speech on a server get no microphone button.

    async function setupVoice() {
        const mic = $('micBtn');
        const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        const probe = Recognition ? new Recognition() : null;
        const local = probe && ('processLocally' in probe || probe.mode !== undefined);
        if (!local) {
            mic.disabled = true;
            mic.classList.add('unsupported');
            mic.title = 'Voice input needs a browser that recognises speech on this device (a recent Chrome or Edge). Skanyxx never sends your audio anywhere.';
            return;
        }
        mic.title = 'Voice input (recognised on this device; only the text is sent)';
        let recognition = null;
        let base = '';
        mic.addEventListener('click', async () => {
            if (recognition) { recognition.stop(); return; }
            const lang = document.documentElement.lang || navigator.language || 'en-US';
            try {
                if (Recognition.available) {
                    const options = { langs: [lang], processLocally: true };
                    const availability = await Recognition.available(options);
                    if (availability === 'unavailable') throw new Error(`Speech in ${lang} cannot be recognised on this device.`);
                    if (availability !== 'available') {
                        $('voiceStatus').textContent = 'Downloading the speech model to this device…';
                        if (!await Recognition.install(options)) throw new Error('The speech model could not be installed.');
                    }
                }
            } catch (e) {
                $('voiceStatus').textContent = e.message;
                return;
            }
            recognition = new Recognition();
            if ('processLocally' in recognition) recognition.processLocally = true;
            else recognition.mode = 'ondevice-only';
            recognition.lang = lang;
            recognition.interimResults = true;
            recognition.continuous = true;
            base = $('chatInput').value ? $('chatInput').value.replace(/\s*$/, ' ') : '';
            recognition.onresult = event => {
                let finalText = '', interim = '';
                for (let i = 0; i < event.results.length; i++)
                    (event.results[i].isFinal ? (finalText += event.results[i][0].transcript) : (interim += event.results[i][0].transcript));
                $('chatInput').value = base + finalText + interim;
                autosize();
            };
            recognition.onerror = event => { $('voiceStatus').textContent = event.error === 'not-allowed' ? 'Microphone access was refused.' : `Voice input stopped (${event.error}).`; };
            recognition.onend = () => {
                recognition = null;
                mic.setAttribute('aria-pressed', 'false');
                mic.classList.remove('listening');
                if (!/stopped|refused/.test($('voiceStatus').textContent)) $('voiceStatus').textContent = '';
            };
            recognition.start();
            mic.setAttribute('aria-pressed', 'true');
            mic.classList.add('listening');
            $('voiceStatus').textContent = 'Listening… click the microphone to stop.';
        });
    }

    // ---------- kagent unreachable / not configured ----------

    const fixSteps = [
        'Check that kagent is running: kubectl -n kagent get pods (the kagent-controller pod should be Running).',
        'Check the address Skanyxx uses for kagent: the KAgent:BaseUrl and KAgent:Port settings, or KAgent:IngressUrl (environment KAgent__BaseUrl, KAgent__Port, KAgent__IngressUrl).',
        'From the machine or pod Skanyxx runs on, the kagent controller should answer on its /health route.',
        'After changing a setting, restart Skanyxx, then reload this page.'
    ];

    // Drawn by the shared setup block (js/setup-guide.js), which also copies it as instructions for an agent.
    // No settings values in it, so no secrets.
    function showProblem(cause, status) {
        const title = status === 502 || status === undefined ? 'Chat cannot reach the agents' : 'Chat cannot load the agents';
        $('problemGuide').replaceChildren(skxSetupGuide.render({
            id: 'chat-kagent',
            title,
            cause: `${cause} If you do not run Skanyxx yourself, copy the instructions below and send them to whoever does.`,
            steps: fixSteps
        }, { tone: 'danger', badge: 'not working' }));
        $('chatProblem').hidden = false;
        $('chatMessages').replaceChildren();
    }

    function hideProblem() { $('chatProblem').hidden = true; }

    // ---------- sensitive data typed in the message (masked before sending) ----------

    const Scanner = {
        patterns: [
            { name: 'API key', pattern: /\b(api[_-]?key|apikey)\s*[:=]\s*['"]?([a-zA-Z0-9_\-]{20,})['"]?/gi, replacement: 'API_KEY=[REDACTED]' },
            { name: 'Token', pattern: /\b(bearer|token)\s*[:=]?\s*['"]?([a-zA-Z0-9_\-\.]{20,})['"]?/gi, replacement: 'TOKEN=[REDACTED]' },
            { name: 'AWS access key', pattern: /\b(AKIA[0-9A-Z]{16})\b/g, replacement: 'AWS_KEY=[REDACTED]' },
            { name: 'Password', pattern: /\b(password|passwd|pwd)\s*[:=]\s*['"]?([^\s'"]{8,})['"]?/gi, replacement: 'PASSWORD=[REDACTED]' },
            { name: 'Password', pattern: /\b(?:my |the )?password\s+(?:is|are)\s*['"]?([^\s'"]{4,})['"]?/gi, replacement: 'password is [REDACTED]' },
            { name: 'Private key', pattern: /-----BEGIN\s+[A-Z ]*PRIVATE KEY-----[\s\S]*?-----END\s+[A-Z ]*PRIVATE KEY-----/g, replacement: '-----BEGIN PRIVATE KEY-----[REDACTED]-----END PRIVATE KEY-----' },
            { name: 'Connection string', pattern: /\b(mongodb|postgres|postgresql|mysql|redis|amqp):\/\/[^\s]+/gi, replacement: 'CONNECTION_STRING=[REDACTED]' },
            { name: 'JWT', pattern: /\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, replacement: 'JWT=[REDACTED]' },
            { name: 'Card number', pattern: /\b(?:\d{4}[-\s]?){3}\d{4}\b/g, replacement: 'CREDIT_CARD=[REDACTED]' },
            { name: 'GitHub token', pattern: /\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36}\b/g, replacement: 'GITHUB_TOKEN=[REDACTED]' },
            { name: 'Provider key', pattern: /\b(sk|pk|rk)-[A-Za-z0-9_-]{16,}/g, replacement: 'KEY=[REDACTED]' },
            { name: 'Slack token', pattern: /xox[baprs]-[0-9A-Za-z-]{10,}/g, replacement: 'SLACK_TOKEN=[REDACTED]' }
        ],
        scan(text) { return text ? this.patterns.filter(p => new RegExp(p.pattern).test(text)).map(p => p.name) : []; },
        mask(text) { return this.patterns.reduce((t, p) => t.replace(new RegExp(p.pattern), p.replacement), text); }
    };

    function scanInput() {
        const found = [...new Set(Scanner.scan($('chatInput').value))];
        $('warningFindings').replaceChildren(...found.map(f => h('span', { class: 'badge badge-warning', text: f })));
        $('securityWarning').hidden = found.length === 0;
    }

    // ---------- wiring ----------

    function autosize() {
        const input = $('chatInput');
        input.style.height = 'auto';
        input.style.height = Math.min(input.scrollHeight, 200) + 'px';
        $('inputTokenCounter').textContent = `~${Math.ceil(input.value.length / 4)} tokens`;
    }

    function wire() {
        // The chat-layout preference (Account, "Chat panel") and narrow screens start with the agent details closed.
        if (document.documentElement.dataset.chatDetails === 'hidden' || matchMedia('(max-width: 1200px)').matches) {
            app.classList.add('details-hidden');
            $('detailsToggle').setAttribute('aria-expanded', 'false');
        }
        $('agentSwitch').addEventListener('click', openPicker);
        $('pickerClose').addEventListener('click', () => $('agentPicker').close());
        $('pickerSearch').addEventListener('input', renderPicker);
        $('newChat').addEventListener('click', () => { if (!state.streaming && state.agent) { notice(''); newChat(true); } });
        $('sessionSearch').addEventListener('input', renderSessions);
        $('sideToggle').addEventListener('click', () => app.classList.toggle('side-open'));
        $('detailsToggle').addEventListener('click', () => {
            const hidden = app.classList.toggle('details-hidden');
            $('detailsToggle').setAttribute('aria-expanded', String(!hidden));
        });
        $('usageChip').addEventListener('click', () => {
            const panel = $('usagePanel');
            panel.hidden = !panel.hidden;
            $('usageChip').setAttribute('aria-expanded', String(!panel.hidden));
        });
        $('composer').addEventListener('submit', e => { e.preventDefault(); submit(); });
        $('stopBtn').addEventListener('click', () => state.streaming?.controller.abort());
        $('chatInput').addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); submit(); }
        });
        $('chatInput').addEventListener('input', () => { autosize(); scanInput(); });
        $('maskCheckbox').addEventListener('change', e => { state.maskSensitive = e.target.checked; });
        $('dismissWarning').addEventListener('click', () => { $('securityWarning').hidden = true; });
        $('attachBtn').addEventListener('click', () => $('fileInput').click());
        $('fileInput').addEventListener('change', e => { addFiles([...e.target.files]); e.target.value = ''; });
        const composer = $('composer');
        composer.addEventListener('dragover', e => { e.preventDefault(); $('dropHint').hidden = false; });
        composer.addEventListener('dragleave', () => { $('dropHint').hidden = true; });
        composer.addEventListener('drop', e => {
            e.preventDefault();
            $('dropHint').hidden = true;
            if (!state.streaming) addFiles([...e.dataTransfer.files]);
        });
        $('retryAgents').addEventListener('click', () => loadAgents(initialRoute()).then(loadSessions));
        $('chatMessages').addEventListener('click', async e => {
            const code = e.target.closest('.copy-code');
            const answer = e.target.closest('.copy-answer');
            const text = code ? code.closest('.code-block').querySelector('code').textContent
                : answer ? state.items[Number(answer.dataset.index)]?.text : null;
            if (text === null || text === undefined) return;
            const button = code || answer;
            try {
                await navigator.clipboard.writeText(text);
                button.textContent = 'Copied';
            } catch {
                button.textContent = 'Copy failed';
            }
            setTimeout(() => { button.textContent = 'Copy'; }, 1500);
        });
        window.addEventListener('popstate', () => {
            const route = routeFromPath(location.pathname);
            if (!route || state.streaming) return;
            const agent = state.agents.find(a => a.namespace === route.namespace && a.name === route.agent);
            if (!agent) return;
            if (agentKey(agent) !== agentKey(state.agent)) selectAgent(agent, route.session, false);
            else if (route.session) openConversation(route.session, false);
            else newChat(false);
        });
    }

    wire();
    setupVoice();
    loadLimits();
    loadAgents(initialRoute()).then(loadSessions);
})();
