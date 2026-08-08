mergeInto(LibraryManager.library, {
    // Releases every iOS audio-session claim while muted, so the player's own
    // media (music, audiobook) keeps playing under a muted game:
    //
    //  1. The template's layer (window.__ringsportApplyMuteToSession, see
    //     WebGLTemplates/RingSportWeb/index.html): the silent <audio>
    //     keepalive and navigator.audioSession.type. A PLAYING keepalive
    //     element re-claims the session every time the tab foregrounds -
    //     suspending WebAudio alone does nothing about it, which is exactly
    //     the "audiobook dies when I switch back to the muted game" bug.
    //
    //  2. Unity's WebAudio context: suspended while muted. Unity re-resumes
    //     it on every user gesture (autoplay gate) and via a 400ms boot
    //     interval, all through ctx.resume() - so resume is stubbed to a
    //     no-op while muted. A statechange watchdog re-suspends in case
    //     WebKit itself revives the context on returning to the tab.
    RingSportSetWebAudioMuted: function(muted) {
        window.__ringsportAudioMuted = !!muted;

        if (window.__ringsportApplyMuteToSession) {
            try { window.__ringsportApplyMuteToSession(!!muted); } catch (e) {}
        }

        var apply = function() {
            if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) {
                return false;
            }
            var ctx = WEBAudio.audioContext;
            if (!ctx.__ringsportRealResume) {
                ctx.__ringsportRealResume = ctx.resume.bind(ctx);
                // addEventListener so Unity's own onstatechange= stays intact
                ctx.addEventListener('statechange', function() {
                    if (window.__ringsportAudioMuted && ctx.state === 'running') {
                        ctx.suspend();
                    }
                });
            }
            if (window.__ringsportAudioMuted) {
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
