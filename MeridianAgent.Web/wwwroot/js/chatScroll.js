// Small DOM helpers shared by the interactive pages.
window.scrollChatToBottom = (element) => {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

// Interactive pages set the tab title themselves: a <PageTitle> section cannot cross
// the static-SSR/WebAssembly/InteractiveServer render-mode boundary without a warning.
window.setDocumentTitle = (title) => {
    document.title = title;
};

// Copying needs a genuine user gesture and a secure context, so it is asked for at the moment of
// the tap. When the browser refuses, the text comes back as if it had pasted so the caller can
// show it instead of losing it.
window.copyTextToClipboard = async (text) => {
    if (!text) {
        return false;
    }

    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    }
    catch {
        // Permission or focus refused: fall through to the manual path.
    }

    const holder = document.createElement('textarea');
    holder.value = text;
    holder.setAttribute('readonly', '');
    holder.style.position = 'fixed';
    holder.style.opacity = '0';
    document.body.appendChild(holder);

    try {
        holder.select();
        return document.execCommand('copy');
    }
    catch {
        return false;
    }
    finally {
        document.body.removeChild(holder);
    }
};
