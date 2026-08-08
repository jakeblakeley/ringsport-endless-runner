mergeInto(LibraryManager.library, {
    // Suspends / resumes Unity's WebAudio context. Dropping AudioListener
    // volume alone leaves the AudioContext "running", and on iOS Safari a
    // running context owns the audio session - which is what kills the
    // player's Music/Spotify the moment the game makes its first sound.
    // Suspending the context hands the session back, so a muted game and
    // the player's own media can play at the same time.
    RingSportSetWebAudioMuted: function(muted) {
        window.__ringsportAudioMuted = !!muted;

        var apply = function() {
            if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) {
                return false;
            }
            var ctx = WEBAudio.audioContext;
            if (!ctx.__ringsportRealResume) {
                ctx.__ringsportRealResume = ctx.resume.bind(ctx);
            }
            if (window.__ringsportAudioMuted) {
                // Unity re-resumes the context on every user gesture to get
                // past the browser autoplay gate, which would silently re-grab
                // the iOS audio session on the next tap - stub resume out
                // while muted so the suspend sticks.
                ctx.resume = function() { return Promise.resolve(); };
                if (ctx.state === 'running') {
                    ctx.suspend();
                }
            } else {
                ctx.resume = ctx.__ringsportRealResume;
                ctx.resume();
            }
            return true;
        };

        if (apply()) {
            return;
        }

        // A muted save applies at boot, before the audio engine has created
        // its context - keep trying until it exists.
        if (!window.__ringsportAudioMutePoll) {
            window.__ringsportAudioMutePoll = setInterval(function() {
                if (apply()) {
                    clearInterval(window.__ringsportAudioMutePoll);
                    window.__ringsportAudioMutePoll = null;
                }
            }, 250);
        }
    }
});
