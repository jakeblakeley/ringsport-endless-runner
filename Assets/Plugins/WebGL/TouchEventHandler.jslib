mergeInto(LibraryManager.library, {
    PreventDefaultTouchEvents: function() {
        // Prevent default touch behaviors (scrolling, zooming, etc.) on iOS Safari
        var canvas = document.getElementById('#canvas');
        if (!canvas) {
            canvas = document.querySelector('canvas');
        }

        if (canvas) {
            // Prevent touch event defaults
            canvas.addEventListener('touchstart', function(e) {
                e.preventDefault();
            }, { passive: false });

            canvas.addEventListener('touchmove', function(e) {
                e.preventDefault();
            }, { passive: false });

            canvas.addEventListener('touchend', function(e) {
                e.preventDefault();
            }, { passive: false });

            canvas.addEventListener('touchcancel', function(e) {
                e.preventDefault();
            }, { passive: false });

            console.log('Touch event prevention enabled for iOS Safari/WebGL');
        } else {
            console.warn('Canvas element not found for touch event prevention');
        }

        // Also prevent body scrolling on touch
        document.body.addEventListener('touchstart', function(e) {
            if (e.target == canvas) {
                e.preventDefault();
            }
        }, { passive: false });

        document.body.addEventListener('touchmove', function(e) {
            if (e.target == canvas) {
                e.preventDefault();
            }
        }, { passive: false });

        // Prevent double-tap zoom on iOS
        var lastTouchEnd = 0;
        document.addEventListener('touchend', function(event) {
            var now = (new Date()).getTime();
            if (now - lastTouchEnd <= 300) {
                event.preventDefault();
            }
            lastTouchEnd = now;
        }, false);

        // Prevent pinch zoom
        document.addEventListener('gesturestart', function(e) {
            e.preventDefault();
        });
    }
});
