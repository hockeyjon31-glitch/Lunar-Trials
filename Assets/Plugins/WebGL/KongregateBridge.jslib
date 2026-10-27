// ---------------------------------------------------------------------------
// Kongregate client API bridge.
//
// Same flat style as LunarExtras.jslib: every export is LunarKong + PascalCase, booleans come
// back as int 1/0 wrapped in try/catch, strings cross the boundary through a caller-provided
// buffer. Everything is DETECTED, never pushed - this project polls platform state instead of
// registering callbacks (see FullscreenButton's "state is POLLED" note), so there is no login
// handler here: C# reads the username/guest flag on a timer and reacts to changes.
//
// TWO RULES THAT ARE NOT OPTIONAL, both learned the hard way:
//
//   1. NO FUNCTION MAY CALL ANOTHER ONE IN THIS FILE. Unity emits each library entry as a
//      `_LunarKongX()` declaration registered in a name table, so the bare `LunarKongX()`
//      spelling a cross-call compiles to is never actually declared - it is a ReferenceError
//      at runtime. LunarExtras.jslib got away with never cross-calling; the first draft of this
//      file did cross-call and would have thrown out of _LunarKongInit's unguarded prologue,
//      straight into MenuController.Awake, on the one host this code exists for. The ready/host
//      predicates are therefore inlined wherever they are needed. They are two lines each.
//
//   2. SAFETY: nothing is attempted unless the host really is kongregate.com. On itch.io,
//      localhost or CrazyGames this file evaluates to a no-op and never loads a third-party
//      script - which matters because Kongregate forbids outside links, and the other hosts
//      must not see Kongregate code at all.
// ---------------------------------------------------------------------------

mergeInto(LibraryManager.library, {
  // Host test. Splitting on '.' and comparing whole labels keeps "notkongregate.com" from
  // matching, which a bare indexOf would do.
  LunarKongOnHost: function () {
    try {
      var host = (location.hostname || '').toLowerCase();
      if (!host) return 0;
      var parts = host.split('.');
      for (var i = 0; i < parts.length; i++) {
        if (parts[i] === 'kongregate') return 1;
      }
      return 0;
    } catch (e) {
      return 0;
    }
  },

  // 1 only once the API object and BOTH namespaces C# touches are present. Checked as one
  // condition everywhere else too, so no caller can see a half-built object.
  LunarKongReady: function () {
    try {
      return (window.kongregate && window.kongregate.services && window.kongregate.stats) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // Starts the API handshake. Idempotent - safe to call every poll.
  //
  // Kongregate exposes either an already-constructed `kongregate` object or the `kongregateAPI`
  // loader shim. We take whichever is there, and only inject their CDN script as a last resort,
  // and only on their own host so no other portal ever pulls a Kongregate script over the net.
  //
  // Inlined ready/host checks (see rule 1) - this function is also the only one whose prologue
  // sits OUTSIDE its try, because a throw here would cross the P/Invoke boundary uncaught.
  LunarKongInit: function () {
    try {
      if (window.kongregate && window.kongregate.services && window.kongregate.stats) return 1;
    } catch (e) { }
    try {
      var host = (location.hostname || '').toLowerCase();
      var parts = host.split('.');
      var onHost = false;
      for (var i = 0; i < parts.length; i++) { if (parts[i] === 'kongregate') { onHost = true; break; } }
      if (!onHost) return 0;
    } catch (e) { return 0; }

    var st = window.__lunarKong;
    if (!st) st = window.__lunarKong = { injected: false, started: false };

    function attach() {
      try {
        if (typeof kongregateAPI !== 'undefined' && kongregateAPI && !st.started) {
          st.started = true;
          kongregateAPI.loadAPI(function () {
            try { window.kongregate = kongregateAPI.getAPI(); } catch (e) { }
          });
        }
      } catch (e) { }
    }

    try {
      if (typeof kongregateAPI !== 'undefined' && kongregateAPI) {
        attach();
      } else if (!st.injected) {
        st.injected = true;
        var s = document.createElement('script');
        s.src = 'https://cdn.kongregate.com/javascripts/kongregate_api.js';
        s.onload = attach;
        s.onerror = function () { st.injected = false; };   // retry on a later poll
        document.head.appendChild(s);
      }
    } catch (e) { }

    try {
      return (window.kongregate && window.kongregate.services && window.kongregate.stats) ? 1 : 0;
    } catch (e) { return 0; }
  },

  // kongregate.stats.submit(name, value) - generates the site's daily / weekly / lifetime /
  // friend leaderboards and is what Badge definitions are built from.
  LunarKongSubmitStat: function (namePtr, value) {
    try {
      if (!(window.kongregate && window.kongregate.services && window.kongregate.stats)) return;
      var name = UTF8ToString(namePtr);
      if (!name) return;
      window.kongregate.stats.submit(name, value | 0);
    } catch (e) { }
  },

  // Kongregate bans account systems, so the platform's own identity is the only one we expose.
  // Buffer is caller-allocated; returns the UTF-8 byte length.
  LunarKongGetUsername: function (buffer, size) {
    var name = '';
    try {
      if (window.kongregate && window.kongregate.services && window.kongregate.stats) {
        name = String(window.kongregate.services.getUsername() || '');
      }
    } catch (e) { name = ''; }
    if (buffer && size > 0) {
      try { stringToUTF8(name, buffer, size); } catch (e) { }
    }
    try { return lengthBytesUTF8(name); } catch (e) { return name.length; }
  },

  // 1 = signed-out guest. Returns 0 while the API is not up yet, so the C# side keeps polling
  // instead of drawing a Register button it cannot justify.
  LunarKongIsGuest: function () {
    try {
      if (!(window.kongregate && window.kongregate.services && window.kongregate.stats)) return 0;
      return window.kongregate.services.isGuest() ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // Raises Kongregate's own overlay sign-up form in place - no page reload.
  LunarKongShowRegistration: function () {
    try {
      if (window.kongregate && window.kongregate.services && window.kongregate.stats) {
        window.kongregate.services.showRegistrationBox();
      }
    } catch (e) { }
  },
});
