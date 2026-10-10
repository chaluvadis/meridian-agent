// Chat helpers shared by the Chat page.
window.scrollChatToBottom = (element) => {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};
