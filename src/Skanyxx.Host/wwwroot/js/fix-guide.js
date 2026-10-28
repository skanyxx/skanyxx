// Renders one "not working" fix ({code, title, cause, local, helm, verify, agentPrompt}, steps {text, command}) as a
// status line, a "How to fix" section with numbered steps for local dev and Helm, per-command copy buttons and a
// "Copy instructions for agent" button. Text only (textContent), never markup: fixes name servers and Secrets.
// Self-contained so the shared setup-guide component can replace it at merge (it has the same step shape).
(function () {
    'use strict';

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function copy(text, button) {
        function done(ok) {
            var label = button.textContent;
            button.textContent = ok ? 'Copied' : 'Copy failed';
            setTimeout(function () { button.textContent = label; }, 1500);
        }
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(function () { done(true); }, function () { done(false); });
            return;
        }
        var area = el('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(area);
        done(ok);
    }

    function copyButton(label, text, extraClass) {
        var button = el('button', 'btn btn-ghost fix-copy' + (extraClass ? ' ' + extraClass : ''), label);
        button.type = 'button';
        button.addEventListener('click', function () { copy(text, button); });
        return button;
    }

    function steps(title, list) {
        var section = el('div', 'fix-steps');
        section.appendChild(el('h5', null, title));
        var ol = el('ol');
        list.forEach(function (step) {
            var li = el('li');
            li.appendChild(el('span', null, step.text));
            if (step.command) {
                var row = el('div', 'fix-command');
                row.appendChild(el('code', null, step.command));
                row.appendChild(copyButton('Copy', step.command));
                li.appendChild(row);
            }
            ol.appendChild(li);
        });
        section.appendChild(ol);
        return section;
    }

    function render(fix) {
        var box = el('div', 'fix');
        box.setAttribute('data-fix', fix.code);
        var line = el('div', 'fix-status');
        line.appendChild(el('strong', null, fix.title));
        line.appendChild(el('p', null, fix.cause));
        box.appendChild(line);

        var details = el('details', 'fix-how');
        details.appendChild(el('summary', null, 'How to fix'));
        details.appendChild(steps('Local development', fix.local));
        details.appendChild(steps('Helm install', fix.helm));
        details.appendChild(steps('Verify', fix.verify));
        box.appendChild(details);

        var actions = el('div', 'fix-actions');
        actions.appendChild(copyButton('Copy instructions for agent', fix.agentPrompt, 'fix-agent'));
        box.appendChild(actions);
        return box;
    }

    window.SkanyxxFixGuide = { render: render, copy: copy };
})();
