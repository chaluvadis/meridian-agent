// Voice in and out of the browser, using what the platform already has: the microphone is
// transcribed on the device rather than over the network, and the speaker comes from the browser's
// own synthesis. Both halves are best-effort — a browser without them, or a page that rendered
// before the circuit existed, must degrade quietly back to typing.

let recognition = null;
let listening = false;
let utteranceCallbacks = null;

const recognitionFor = () => {
    const Constructor = window.SpeechRecognition ?? window.webkitSpeechRecognition;

    if (!Constructor) {
        return null;
    }

    const instance = new Constructor();
    instance.continuous = false;
    instance.interimResults = false;
    instance.lang = document.documentElement.lang || 'en-US';

    return instance;
};

window.isSpeechSupported = () =>
    (window.SpeechRecognition ?? window.webkitSpeechRecognition) !== undefined &&
    'speechSynthesis' in window;

window.startListening = (callbacks) => {
    // Speaking to the traveler while listening is how you get a transcript of yourself.
    window.stopSpeaking();

    if (listening) {
        return true;
    }

    const instance = recognitionFor();

    if (!instance) {
        return false;
    }

    utteranceCallbacks = callbacks;

    instance.onresult = (event) => {
        let transcript = '';

        for (const result of event.results) {
            transcript += result[0].transcript;
        }

        transcript = transcript.trim();

        if (transcript) {
            tell(callbacks, 'OnUtteranceFinalAsync', transcript);
        }
    };

    instance.onerror = (event) => tell(callbacks, 'OnListeningErrorAsync', event.error ?? 'unknown');

    instance.onend = () => {
        listening = false;
        recognition = null;
        utteranceCallbacks = null;
        tell(callbacks, 'OnListeningStoppedAsync');
    };

    try {
        instance.start();
    }
    catch {
        // Already listening, or a page that went away: nothing to start.
        return false;
    }

    recognition = instance;
    listening = true;

    return true;
};

window.stopListening = () => {
    if (!recognition) {
        return;
    }

    const instance = recognition;
    recognition = null;
    listening = false;

    try {
        instance.stop();
    }
    catch {
        // Already stopped.
    }
};

window.speakAloud = (text, callbacks) => {
    if (!('speechSynthesis' in window) || !text) {
        return false;
    }

    // One voice at a time: a new message replaces whatever is being read.
    window.stopSpeaking();

    utteranceCallbacks = callbacks;

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.rate = 1;
    utterance.pitch = 1;

    utterance.onend = () => {
        utteranceCallbacks = null;
        tell(callbacks, 'OnSpeakingFinishedAsync');
    };

    utterance.onerror = () => {
        utteranceCallbacks = null;
        tell(callbacks, 'OnSpeakingFinishedAsync');
    };

    window.speechSynthesis.speak(utterance);

    return true;
};

window.stopSpeaking = () => {
    if (!('speechSynthesis' in window)) {
        return;
    }

    const callbacks = utteranceCallbacks;
    utteranceCallbacks = null;

    // Cancelling fires no end event, so the caller is told here instead.
    try {
        if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
            window.speechSynthesis.cancel();

            if (callbacks) {
                tell(callbacks, 'OnSpeakingFinishedAsync');
            }
        }
    }
    catch {
        // Nothing was speaking.
    }
};

// Sends a callback across without letting a JS problem break the page. The circuit can be gone by
// the time speech finishes, and that is not worth an error the traveler never sees.
function tell(callbacks, method, argument) {
    if (!callbacks || typeof callbacks[method] !== 'function') {
        return;
    }

    try {
        const result = argument === undefined
            ? callbacks[method]()
            : callbacks[method](argument);

        if (result && typeof result.catch === 'function') {
            result.catch(() => { });
        }
    }
    catch {
        // The circuit went away mid-utterance.
    }
}
