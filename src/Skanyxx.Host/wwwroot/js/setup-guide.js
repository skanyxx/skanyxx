// Copy buttons of the setup guides (Pages/Shared/Components/SetupGuide). The clipboard API needs a secure context
// (https or localhost); an install opened over plain http falls back to selecting the text and execCommand.
document.addEventListener('click', async (event) => {
    const button = event.target.closest('.setup-copy');
    if (!button) return;
    const text = button.dataset.copyFrom
        ? document.getElementById(button.dataset.copyFrom).value
        : button.dataset.copy;
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
    } else {
        const area = document.createElement('textarea');
        area.value = text;
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy');
        area.remove();
    }
    const label = button.textContent;
    button.textContent = 'Copied';
    setTimeout(() => { button.textContent = label; }, 1500);
});
