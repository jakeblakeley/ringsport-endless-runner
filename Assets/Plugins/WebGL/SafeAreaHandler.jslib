mergeInto(LibraryManager.library, {
    // Reports how much of the top of the viewport the OS is covering, so the
    // HUD can step out from under a notch / status bar.
    //
    // Only fullscreen (or an installed standalone window) is reported: in a
    // normal browser tab the status bar sits above the page, not over it, and
    // the HUD is already clear of it - which is exactly what the player sees.
    //
    // Returns -1 when there is nothing to dodge, otherwise the inset as a
    // fraction of the viewport height in ten-thousandths (an int, so no float
    // marshalling across the jslib boundary).
    RingSportSafeAreaTopBasisPoints: function () {
        var doc = document;
        var fullscreen = !!(doc.fullscreenElement || doc.webkitFullscreenElement ||
                            doc.mozFullScreenElement || doc.msFullscreenElement);
        // iPhone Safari has no Fullscreen API - the equivalent there is the
        // Add-to-Home-Screen window, which draws under the status bar.
        var standalone = (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches) ||
                         window.navigator.standalone === true;
        // itch.io fullscreens the embed <iframe> from ITS document, so our
        // document.fullscreenElement stays null even though we own the screen.
        // Owning the full screen height is the observable part of that.
        var screenH = (window.screen && window.screen.height) || 0;
        var fillsScreen = screenH > 0 && window.innerHeight >= screenH * 0.95;
        if (!fullscreen && !standalone && !fillsScreen) {
            return -1;
        }

        // env(safe-area-inset-top) is only readable off a real element, and the
        // probe is kept around because creating one per poll would churn layout.
        var probe = window.__ringsportSafeAreaProbe;
        if (!probe || !probe.isConnected) {
            probe = doc.createElement('div');
            probe.style.cssText =
                'position:fixed;top:0;left:0;width:0;height:0;visibility:hidden;pointer-events:none;' +
                'padding-top:constant(safe-area-inset-top,0px);padding-top:env(safe-area-inset-top,0px);';
            doc.body.appendChild(probe);
            window.__ringsportSafeAreaProbe = probe;
        }

        var height = window.innerHeight || doc.documentElement.clientHeight || 0;
        if (height <= 0) {
            return -1;
        }

        var inset = parseFloat(window.getComputedStyle(probe).paddingTop) || 0;

        // env() reports 0 in a few places that still overlap us - most notably
        // inside an iframe (itch.io's embed), where safe-area insets are only
        // handed to the top-level document. Fall back to a plain status-bar
        // height on touch devices rather than leaving the HUD underneath it.
        // Tweak live from the console via window.__ringsportMinFullscreenInset.
        if (inset <= 0) {
            var coarse = (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) ||
                         /iPhone|iPad|iPod|Android/i.test(navigator.userAgent);
            if (coarse) {
                var fallback = window.__ringsportMinFullscreenInset;
                inset = (typeof fallback === 'number') ? fallback : 24;
            }
        }

        return Math.round(Math.max(0, inset) / height * 10000);
    }
});
