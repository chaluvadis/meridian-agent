namespace MeridianAgent.Web.Components;

using Microsoft.JSInterop;

/// <summary>
/// Talks to the browser's own speech engine: the Web Speech API for listening and SpeechSynthesis
/// for reading aloud. Nothing is sent over the network and nothing is stored — which is why a
/// transcript only ever exists in the page that captured it.
///
/// A browser without either half is not an error, so every call reports what it did and the chat
/// simply falls back to typing. A page that renders before the circuit exists (prerendering) gets
/// the same silence: the JS may not be reachable yet, and a microphone prompt during prerender
/// would be a permission request with no one to grant it.
/// </summary>
public sealed class SpeechInterop : IAsyncDisposable
{
    private readonly IJSRuntime _runtime;
    private DotNetObjectReference<SpeechInterop>? _self;

    public SpeechInterop(IJSRuntime runtime) => _runtime = runtime;

    /// <summary>True while the microphone is open.</summary>
    public bool IsListening { get; private set; }

    /// <summary>True while the agent is reading a message aloud.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>Raised with the transcribed words when the traveler stops speaking.</summary>
    public event Func<string, Task>? UtteranceFinal;

    /// <summary>Raised when the microphone closes on its own: silence, an error, or the end of speech.</summary>
    public event Func<Task>? ListeningStopped;

    /// <summary>Raised with the browser's own reason when listening fails, for the log rather than the UI.</summary>
    public event Func<string, Task>? ListeningFailed;

    /// <summary>Raised when spoken output finishes or is cancelled, so the speaker icon can settle.</summary>
    public event Func<Task>? SpeakingFinished;

    private bool? _isSupported;

    /// <summary>Whether this browser can do either half. Cached because the answer never changes.</summary>
    public async Task<bool> IsSupportedAsync()
    {
        _isSupported ??= await InvokeAsync<bool>("isSpeechSupported");

        return _isSupported.Value;
    }

    /// <summary>Opens the microphone. Returns false when the browser has no speech recognition.</summary>
    public async Task<bool> StartListeningAsync()
    {
        _self ??= DotNetObjectReference.Create(this);

        if (IsListening)
        {
            return true;
        }

        var started = await InvokeAsync<bool>("startListening", _self);

        IsListening = started;

        return started;
    }

    /// <summary>Closes the microphone, whether it was opened by the traveler or by an error.</summary>
    public async Task StopListeningAsync()
    {
        if (!IsListening)
        {
            return;
        }

        IsListening = false;
        await InvokeVoidAsync("stopListening");
    }

    /// <summary>Reads the given text aloud. Returns false when the browser cannot synthesize speech.</summary>
    public async Task<bool> SpeakAsync(string text)
    {
        _self ??= DotNetObjectReference.Create(this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var speaking = await InvokeAsync<bool>("speakAloud", text, _self);

        IsSpeaking = speaking;

        return speaking;
    }

    /// <summary>Stops whatever is being read, which is also what a new message does first.</summary>
    public async Task StopSpeakingAsync()
    {
        if (!IsSpeaking)
        {
            return;
        }

        IsSpeaking = false;
        await InvokeVoidAsync("stopSpeaking");
    }

    [JSInvokable]
    public Task OnUtteranceFinalAsync(string transcript) =>
        UtteranceFinal?.Invoke(transcript) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnListeningStoppedAsync()
    {
        IsListening = false;
        return ListeningStopped?.Invoke() ?? Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnListeningErrorAsync(string reason) =>
        ListeningFailed?.Invoke(reason) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnSpeakingFinishedAsync()
    {
        IsSpeaking = false;
        return SpeakingFinished?.Invoke() ?? Task.CompletedTask;
    }

    private async Task<T> InvokeAsync<T>(string identifier, params object?[]? args)
    {
        try
        {
            return await _runtime.InvokeAsync<T>(identifier, args ?? Array.Empty<object?>());
        }
        catch (JSException)
        {
            return default!;
        }
        catch (JSDisconnectedException)
        {
            return default!;
        }
        catch (InvalidOperationException)
        {
            return default!;
        }
    }

    private async Task InvokeVoidAsync(string identifier)
    {
        try
        {
            await _runtime.InvokeVoidAsync(identifier);
        }
        catch (JSException)
        {
            // The script is not there; nothing to stop.
        }
        catch (JSDisconnectedException)
        {
            // The page is gone: there is nobody left to speak to.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: nothing was speaking to stop.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await InvokeVoidAsync("stopSpeaking");
        await InvokeVoidAsync("stopListening");

        _self?.Dispose();
        _self = null;
    }
}
