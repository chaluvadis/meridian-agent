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
