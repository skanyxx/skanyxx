// Markdown for agent answers, safe by construction: the text is HTML-escaped first, and only the tags below are
// produced from it. Links keep http(s) and mailto only. Code blocks get a copy button (handled in chat.js).
window.ChatMarkdown = (() => {
    const escapeHtml = text => String(text ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

    // Inline marks on already-escaped text; code spans are cut out first so nothing inside them is formatted.
    function inline(text) {
        const spans = [];
        const keep = html => { spans.push(html); return `\u0000${spans.length - 1}\u0000`; };
        // Code spans and link targets are cut out first, so no emphasis lands inside them.
        let out = text.replace(/`([^`\n]+)`/g, (_, code) => keep(`<code>${code}</code>`));
        out = out
            .replace(/\[([^\]\n]+)\]\(((?:https?:\/\/|mailto:)[^\s)]+)\)/g, (_, label, href) => keep(`<a href="${href}" target="_blank" rel="noopener noreferrer">`) + label + keep('</a>'))
            .replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>')
            .replace(/(^|[^*\w])\*([^*\n]+)\*(?!\w)/g, '$1<em>$2</em>')
            .replace(/(^|[^_\w])_([^_\n]+)_(?!\w)/g, '$1<em>$2</em>')
            .replace(/~~([^~\n]+)~~/g, '<del>$1</del>');
        return out.replace(/\u0000(\d+)\u0000/g, (_, i) => spans[Number(i)]);
    }

    function table(lines) {
        const cells = line => line.trim().replace(/^\||\|$/g, '').split('|').map(c => inline(c.trim()));
        const head = cells(lines[0]).map(c => `<th>${c}</th>`).join('');
        const body = lines.slice(2).map(l => `<tr>${cells(l).map(c => `<td>${c}</td>`).join('')}</tr>`).join('');
        return `<div class="md-table"><table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
    }

    function codeBlock(lang, code) {
        const label = lang ? escapeHtml(lang) : 'code';
        return `<div class="code-block"><div class="code-head"><span>${label}</span>` +
            `<button type="button" class="copy-code" aria-label="Copy code">Copy</button></div>` +
            `<pre><code>${escapeHtml(code)}</code></pre></div>`;
    }

    function render(markdown) {
        const lines = String(markdown ?? '').replace(/\r\n?/g, '\n').split('\n');
        const html = [];
        let paragraph = [];
        const flush = () => {
            if (paragraph.length) html.push(`<p>${paragraph.map(l => inline(escapeHtml(l))).join('<br>')}</p>`);
            paragraph = [];
        };
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const fence = line.match(/^\s*```\s*([\w+#.-]*)\s*$/);
            if (fence) {
                flush();
                const code = [];
                for (i++; i < lines.length && !/^\s*```\s*$/.test(lines[i]); i++) code.push(lines[i]);
                html.push(codeBlock(fence[1], code.join('\n')));
                continue;
            }
            if (!line.trim()) { flush(); continue; }
            const heading = line.match(/^(#{1,6})\s+(.*)$/);
            if (heading) {
                flush();
                const level = Math.min(heading[1].length + 2, 6);
                html.push(`<h${level}>${inline(escapeHtml(heading[2]))}</h${level}>`);
                continue;
            }
            if (/^\s*\|.*\|\s*$/.test(line) && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
                flush();
                const rows = [];
                for (; i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i]); i++) rows.push(escapeHtml(lines[i]));
                i--;
                html.push(table(rows));
                continue;
            }
            const list = line.match(/^\s*([-*+]|\d+[.)])\s+(.*)$/);
            if (list) {
                flush();
                const ordered = /\d/.test(list[1]);
                const items = [];
                for (; i < lines.length; i++) {
                    const item = lines[i].match(/^\s*([-*+]|\d+[.)])\s+(.*)$/);
                    if (!item || /\d/.test(item[1]) !== ordered) break;
                    items.push(`<li>${inline(escapeHtml(item[2]))}</li>`);
                }
                i--;
                html.push(ordered ? `<ol>${items.join('')}</ol>` : `<ul>${items.join('')}</ul>`);
                continue;
            }
            if (/^\s*>\s?/.test(line)) {
                flush();
                const quote = [];
                for (; i < lines.length && /^\s*>\s?/.test(lines[i]); i++) quote.push(inline(escapeHtml(lines[i].replace(/^\s*>\s?/, ''))));
                i--;
                html.push(`<blockquote>${quote.join('<br>')}</blockquote>`);
                continue;
            }
            if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) { flush(); html.push('<hr>'); continue; }
            paragraph.push(line);
        }
        flush();
        return html.join('');
    }

    return { render, escapeHtml };
})();
