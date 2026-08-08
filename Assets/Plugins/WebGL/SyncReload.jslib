// Full page reload after a cloud-save restore, so every manager re-reads its
// PlayerPrefs from IndexedDB. Application.OpenURL is unsuitable: on WebGL it
// window.open()s a NEW tab, which in the home-screen PWA context strands the
// player in an in-app browser instead of reloading the game.
mergeInto(LibraryManager.library, {
  SyncReloadPage: function () {
    location.reload();
  }
});
