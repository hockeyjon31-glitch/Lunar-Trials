mergeInto(LibraryManager.library, {
  // ------------------------------------------------------ host introspection
  // Tab-visibility and touch detection must work on plain web builds too (no SDK).
  LunarTabHidden: function () {
    try { return (typeof document !== 'undefined' && document.hidden) ? 1 : 0; } catch (e) { return 0; }
  },

  LunarIsMobile: function () {
    try {
      var ua = navigator.userAgent || '';
      return /Android|iPhone|iPad|iPod|Mobile|Silk|Kindle/i.test(ua) ? 1 : 0;
    } catch (e) { return 0; }
  },

  // ------------------------------------------------------------- fullscreen
  // Unity's own fullscreen button is stripped from index.html (the portal/itch owns
  // fullscreen framing), so the in-game control in FullscreenButton.cs drives the browser
  // Fullscreen API directly. All the vendor-prefixed spellings are kept for older Safari.
  //
  // Returns 1 when a request was actually issued, 0 when the host refused or has no API.
  // The rejection is swallowed on purpose: requestFullscreen returns a promise that REJECTS
  // whenever the document is not user-activated or is inside an iframe without
  // allowfullscreen - the normal case on an embed host - and an unhandled rejection would
  // just log noise. The caller re-checks LunarIsFullscreen instead of trusting the return.
  LunarRequestFullscreen: function () {
    try {
      var el = document.documentElement;
      if (!el) return 0;
      var fn = el.requestFullscreen || el.webkitRequestFullscreen ||
               el.mozRequestFullScreen || el.msRequestFullscreen;
      if (!fn) return 0;
      var p = fn.call(el);
      if (p && typeof p.catch === 'function') p.catch(function () {});
      return 1;
    } catch (e) { return 0; }
  },

  LunarExitFullscreen: function () {
    try {
      var fn = document.exitFullscreen || document.webkitExitFullscreen ||
               document.mozCancelFullScreen || document.msExitFullscreen;
      if (!fn) return 0;
      var p = fn.call(document);
      if (p && typeof p.catch === 'function') p.catch(function () {});
      return 1;
    } catch (e) { return 0; }
  },

  // Polled from C# rather than pushed through SendMessage, so pressing Esc or F11 (both of
  // which the browser handles itself, with no event reaching Unity) still updates the button.
  LunarIsFullscreen: function () {
    try {
      var el = document.fullscreenElement || document.webkitFullscreenElement ||
               document.mozFullScreenElement || document.msFullscreenElement;
      return el ? 1 : 0;
    } catch (e) { return 0; }
  },

  // The in-game fullscreen control is for embed hosts that have no chrome of their own
  // (itch.io and friends). The CrazyGames portal frames the game fullscreen itself and the
  // platform owns that decision, so the button is suppressed there.
  LunarOnCrazyGamesHost: function () {
    try {
      var host = (location.hostname || '').toLowerCase();
      if (!host) return 0;
      var parts = host.split('.');
      for (var i = 0; i < parts.length; i++) {
        if (parts[i] === 'crazygames' || parts[i] === 'dev-crazygames') return 1;
      }
      return 0;
    } catch (e) { return 0; }
  },

  // True when the game is running inside an iframe (itch.io's embed, or the CrazyGames portal).
  // A framed document cannot be promoted to real fullscreen unless the host's iframe policy
  // allows it, and the host already ships its own working fullscreen control in that corner -
  // so the in-game button stands down rather than duplicating (and colliding with) it.
  LunarIsFramed: function () {
    try {
      if (typeof window === 'undefined') return 0;
      try { return (window.self !== window.top) ? 1 : 0; }
      catch (e) { return 1; }   // cross-origin top access throws => definitely framed
    } catch (e) { return 0; }
  },
});