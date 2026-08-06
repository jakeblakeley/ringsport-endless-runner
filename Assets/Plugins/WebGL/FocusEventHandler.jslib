mergeInto(LibraryManager.library, {
    // Latches every "the player stopped looking at us" event so the C# side can
    // read it on its first frame back. Polling document.hidden from Unity would
    // always miss a tab switch: the browser stops driving the game loop while
    // the tab is hidden, so by the time Unity runs again the page is visible
    // once more - the latch is what survives that gap.
    RingSportWatchFocusLoss: function() {
        if (window.__ringsportFocusWatch) {
            return;
        }
        window.__ringsportFocusWatch = true;
        window.__ringsportFocusLost = false;

        var lost = function() {
            window.__ringsportFocusLost = true;
        };

        // Tab switch / minimise (fires inside the itch.io iframe too)
        document.addEventListener('visibilitychange', function() {
            if (document.hidden) {
                lost();
            }
        });

        // Another window, another app, or the browser chrome took focus
        window.addEventListener('blur', lost);
        window.addEventListener('pagehide', lost);

        console.log('RingSport focus watch enabled');
    },

    // Reads and clears the latch. 1 = focus was lost since the last call.
    RingSportConsumeFocusLoss: function() {
        var lost = window.__ringsportFocusLost ? 1 : 0;
        window.__ringsportFocusLost = false;
        return lost;
    }
});
