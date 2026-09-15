/*
  Flint — REX Technologies. The page works without this file; with it, two things improve.

  1. The download panel leads with the build for the device that is reading the page, and offers
     the others beneath. Detection uses the browser's own platform hints and nothing else, and a
     row of chips lets a person overrule it, because a laptop reading about a phone app is the
     ordinary case rather than the exception.

  2. The phone app's download link is resolved from the GitHub Releases API at page load, because
     the rolling build's file name carries the commit it was built from and changes on every merge.
     When the API cannot be reached the button falls back to the release page, which always exists.

  Nothing here is sent anywhere. The only request this page makes is to api.github.com for the
  release listing, and it is made without credentials.
*/
(function () {
  "use strict";

  var REPO = "tochi-mba/flint-app-firestick-cast";
  var API = "https://api.github.com/repos/" + REPO;
  var RELEASES = "https://github.com/" + REPO + "/releases";

  var root = document.documentElement;
  root.classList.remove("no-js");
  root.classList.add("js");

  /* ---------- Header menu ---------- */

  var toggle = document.querySelector(".menu-toggle");
  var nav = document.querySelector(".nav");
  if (toggle && nav) {
    toggle.addEventListener("click", function () {
      var open = nav.classList.toggle("open");
      toggle.setAttribute("aria-expanded", open ? "true" : "false");
      toggle.querySelector(".label").textContent = open ? "Close" : "Menu";
    });
    nav.addEventListener("click", function (event) {
      if (event.target.closest("a")) {
        nav.classList.remove("open");
        toggle.setAttribute("aria-expanded", "false");
        toggle.querySelector(".label").textContent = "Menu";
      }
    });
  }

  /* ---------- Platform detection ---------- */

  // The platforms the page knows how to talk about. `supported` is whether Flint runs on it;
  // `available` is whether there is a download today. Both are stated to the visitor as they are.
  var PLATFORMS = {
    android: {
      name: "Android",
      eyebrow: "Flint Mobile",
      title: "Flint for Android",
      requirement: "Android 8.0 or newer, and a phone that can run a hotspot. The phone becomes the host and the Fire TV becomes its screen.",
      primaryLabel: "Download the APK",
      primaryHref: RELEASES + "/tag/latest",
      fine: "A rolling build, replaced on every merge and signed by the release workflow. Android will warn about an unknown developer, because this is not a store listing.",
      install: "android",
    },
    windows: {
      name: "Windows",
      eyebrow: "Flint for Windows",
      title: "Flint for Windows",
      requirement: "Windows 10 or 11, 64-bit. The PC is the host; the Fire TV is the screen. Screen mirroring and media handoff are proven end to end on a Fire TV Stick.",
      primaryLabel: "Download for Windows",
      primaryHref: RELEASES + "/tag/latest-windows",
      fine: "A rolling build, replaced on every merge: a zip to extract, then run Flint.App.exe. Windows will warn that it does not recognise the publisher; that warning is true, and building from source avoids it.",
      install: "windows",
    },
    firetv: {
      name: "Fire TV",
      eyebrow: "The receiver",
      title: "Flint on this Fire TV",
      requirement: "The receiver is not downloaded on the television. The phone app installs it over ADB, and the Windows package carries it for sideloading.",
      primaryLabel: "Get the phone app",
      primaryHref: RELEASES + "/tag/latest",
      fine: "Open this page on an Android phone, install Flint there, and it will offer to put the receiver on this TV, showing exactly what it installs first.",
      install: "firetv",
    },
    other: {
      name: "Something else",
      eyebrow: "Not this device",
      title: "Flint runs on Android and Windows",
      requirement: "There is no Flint for iPhone, iPad, Mac, Linux or ChromeOS, and none is planned for now. The Android app or the Windows app is what casts to the television.",
      primaryLabel: "See every release",
      primaryHref: RELEASES,
      fine: "Pick a device above to read the download and install steps for it.",
      install: "android",
    },
  };

  // Amazon's television models all carry an AFT code; its tablets carry a KF code. Both ship the
  // Silk browser, so Silk alone says "an Amazon device" and never "a television".
  var FIRE_TV_MODEL = /\bAFT[A-Z0-9]*\b/;
  var FIRE_TABLET_MODEL = /\bKF[A-Z0-9]{2,}\b/;

  /*
    Most specific signal first, broad platform hint last.

    The order is the substance of this function. Every device below is Android or claims to be
    something else that overlaps: a Fire TV is Android, a Fire tablet is Android AND runs the same
    browser as the Fire TV, and an iPad has reported itself as a Macintosh since iPadOS 13. So each
    branch is reached only once everything that could be mistaken for it has been ruled out.
  */
  function detect() {
    var ua = navigator.userAgent || "";
    var data = navigator.userAgentData;
    var platform = data && data.platform ? String(data.platform) : "";

    // A television, by its model code or by saying so. Checked before Android, which it also is.
    // Silk is deliberately NOT enough on its own: a Fire tablet runs Silk too, and it is a host
    // that can run Flint rather than a screen to cast to, so calling it a television told its
    // owner the app was not for them.
    if (FIRE_TV_MODEL.test(ua) || /Fire ?TV/i.test(ua)) {
      return { key: "firetv", label: "a Fire TV" };
    }
    // An iPad reports itself as a Macintosh, so it has to be told from a real Mac by touch.
    if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) {
      return { key: "other", label: "an iPad" };
    }
    if (/iPhone|iPod/.test(ua)) return { key: "other", label: "an iPhone" };
    // ChromeOS before the broad hints: it is neither Android nor a PC, whatever else it reports.
    if (/CrOS/.test(ua) || /Chrome ?OS/i.test(platform)) return { key: "other", label: "a Chromebook" };

    if (/Android/i.test(platform) || /Android/i.test(ua)) {
      // A Fire tablet is an Android tablet whatever its model code says about Amazon.
      var tablet = FIRE_TABLET_MODEL.test(ua) || !((data && data.mobile === true) || /\bMobile\b/.test(ua));
      return { key: "android", label: tablet ? "an Android tablet" : "an Android phone" };
    }
    if (/Windows/i.test(platform) || /Windows NT/i.test(ua)) {
      return { key: "windows", label: "a Windows PC" };
    }
    if (/Mac OS X|macOS/i.test(ua) || /macOS/i.test(platform)) return { key: "other", label: "a Mac" };
    if (/Linux/i.test(platform) || /Linux/i.test(ua)) return { key: "other", label: "a Linux computer" };
    return { key: "other", label: "" };
  }

  /* ---------- Install tabs ---------- */

  var tabs = Array.prototype.slice.call(document.querySelectorAll(".tab"));
  var panels = Array.prototype.slice.call(document.querySelectorAll(".panel"));

  function selectTab(key, focus) {
    tabs.forEach(function (tab) {
      var selected = tab.getAttribute("data-panel") === key;
      tab.setAttribute("aria-selected", selected ? "true" : "false");
      tab.setAttribute("tabindex", selected ? "0" : "-1");
      if (selected && focus) tab.focus();
    });
    panels.forEach(function (p) {
      p.hidden = p.id !== "install-" + key;
    });
  }

  tabs.forEach(function (tab, index) {
    tab.addEventListener("click", function () { selectTab(tab.getAttribute("data-panel")); });
    tab.addEventListener("keydown", function (event) {
      var next = null;
      if (event.key === "ArrowRight") next = tabs[(index + 1) % tabs.length];
      if (event.key === "ArrowLeft") next = tabs[(index - 1 + tabs.length) % tabs.length];
      if (event.key === "Home") next = tabs[0];
      if (event.key === "End") next = tabs[tabs.length - 1];
      if (next) {
        event.preventDefault();
        selectTab(next.getAttribute("data-panel"), true);
      }
    });
  });

  /* ---------- The download panel ---------- */

  var panel = document.getElementById("download");
  if (!panel) return;

  var detectedLine = panel.querySelector(".detected");
  var chips = panel.querySelectorAll(".chip");
  var eyebrow = panel.querySelector("[data-slot=eyebrow]");
  var title = panel.querySelector("[data-slot=title]");
  var requirement = panel.querySelector("[data-slot=requirement]");
  var meta = panel.querySelector("[data-slot=meta]");
  var primary = panel.querySelector("[data-slot=primary]");
  var fine = panel.querySelector("[data-slot=fine]");
  var others = panel.querySelectorAll(".other[data-platform]");

  var release = { android: null, windows: null };
  var current = null;

  function metaLine(platformKey) {
    var r = release[platformKey];
    if (platformKey === "android") {
      if (!r) return "";
      return "<span>Version <b>" + escapeHtml(r.version) + "</b></span>" +
        "<span>Size <b>" + escapeHtml(r.size) + "</b></span>" +
        "<span>Built <b>" + escapeHtml(r.date) + "</b></span>";
    }
    if (platformKey === "windows") {
      if (!r) return "";
      return "<span>Version <b>" + escapeHtml(r.version) + "</b></span>" +
        "<span>Size <b>" + escapeHtml(r.size) + "</b></span>";
    }
    return "";
  }

  function render(key) {
    var p = PLATFORMS[key] || PLATFORMS.other;
    current = key;
    eyebrow.textContent = p.eyebrow;
    title.textContent = p.title;
    requirement.textContent = p.requirement;
    meta.innerHTML = metaLine(key);
    meta.hidden = meta.innerHTML === "";

    var href = p.primaryHref;
    var label = p.primaryLabel;
    var fineText = p.fine;
    if (key === "android" && release.android) {
      href = release.android.url;
      label = "Download for Android";
    }
    if (key === "firetv" && release.android) {
      href = release.android.url;
    }
    if (key === "windows" && release.windows) {
      href = release.windows.url;
      label = "Download for Windows";
      fineText = "A zip: extract it and run Flint.App.exe. Windows will warn that it does not recognise the publisher; that warning is true, and building from source avoids it.";
    }
    primary.setAttribute("href", href);
    primary.textContent = label;
    fine.textContent = fineText;

    // Everything that is not the lead is offered beneath it, so nothing is hidden by the guess.
    Array.prototype.forEach.call(others, function (card) {
      card.hidden = card.getAttribute("data-platform") === key;
    });
    Array.prototype.forEach.call(chips, function (chip) {
      chip.setAttribute("aria-pressed", chip.getAttribute("data-platform") === key ? "true" : "false");
    });

    selectTab(p.install);
  }

  function setDetected(text) {
    detectedLine.innerHTML = text;
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  Array.prototype.forEach.call(chips, function (chip) {
    chip.addEventListener("click", function () {
      var key = chip.getAttribute("data-platform");
      render(key);
      setDetected("Showing <strong>" + escapeHtml(PLATFORMS[key].name) + "</strong>. Chosen by you.");
    });
  });

  // Secondary cards link to the same resolved downloads.
  Array.prototype.forEach.call(others, function (card) {
    var key = card.getAttribute("data-platform");
    var button = card.querySelector("[data-slot=other-primary]");
    if (!button) return;
    button.addEventListener("click", function (event) {
      if (button.getAttribute("href") === "#download") {
        event.preventDefault();
        render(key);
        setDetected("Showing <strong>" + escapeHtml(PLATFORMS[key].name) + "</strong>. Chosen by you.");
        primary.focus();
      }
    });
  });

  var guess = detect();
  render(guess.key);
  if (guess.label) {
    setDetected("It looks like you are on <strong>" + escapeHtml(guess.label) + "</strong>. Not right? Pick a device.");
  } else {
    setDetected("Pick the device you are installing on.");
  }

  /* ---------- Release lookup ---------- */

  function formatSize(bytes) {
    if (!bytes) return "";
    var mb = bytes / (1024 * 1024);
    return (mb >= 10 ? Math.round(mb) : mb.toFixed(1)) + " MB";
  }

  function formatDate(iso) {
    if (!iso) return "";
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  }

  function apkOf(rel) {
    var assets = (rel && rel.assets) || [];
    for (var i = 0; i < assets.length; i++) {
      if (/\.apk$/i.test(assets[i].name)) return assets[i];
    }
    return null;
  }

  function zipOf(rel) {
    var assets = (rel && rel.assets) || [];
    for (var i = 0; i < assets.length; i++) {
      if (/win-x64\.zip$/i.test(assets[i].name) || /\.zip$/i.test(assets[i].name)) return assets[i];
    }
    return null;
  }

  function versionFrom(name, fallback) {
    // A rolling phone build is `-<hash>`; a rolling Windows build is `-g<hash>`, because dotnet reads
    // the version strictly and the `g` keeps a hash that happens to be all digits a valid label.
    var m = /(\d+\.\d+\.\d+(?:-g?[0-9a-f]{6,})?)/i.exec(name || "");
    return m ? m[1] : fallback;
  }

  function fetchJson(url) {
    return fetch(url, { headers: { Accept: "application/vnd.github+json" } }).then(function (response) {
      if (!response.ok) throw new Error(String(response.status));
      return response.json();
    });
  }

  function applyRelease() {
    if (current) render(current);
    Array.prototype.forEach.call(others, function (card) {
      var key = card.getAttribute("data-platform");
      var button = card.querySelector("[data-slot=other-primary]");
      var note = card.querySelector("[data-slot=other-note]");
      if (key === "android" && release.android && button) {
        button.setAttribute("href", release.android.url);
        button.textContent = "Download APK";
        if (note) note.textContent = "Version " + release.android.version + ", " + release.android.size + ".";
      }
      if (key === "windows" && release.windows && button) {
        button.setAttribute("href", release.windows.url);
        button.textContent = "Download zip";
        if (note) note.textContent = "Version " + release.windows.version + ", " + release.windows.size + ".";
      }
    });
  }

  if (typeof fetch === "function") {
    fetchJson(API + "/releases/tags/latest").then(function (rel) {
      var apk = apkOf(rel);
      if (!apk) return;
      release.android = {
        url: apk.browser_download_url,
        version: versionFrom(apk.name, rel.tag_name),
        size: formatSize(apk.size),
        date: formatDate(rel.published_at),
      };
      applyRelease();
    }).catch(function () { /* The static links stand. */ });

    // The rolling Windows build first, as the phone's is: every merge replaces it. A tagged stable
    // release is only the fallback, for when the rolling one is missing or has no zip on it.
    fetchJson(API + "/releases/tags/latest-windows").then(function (rel) {
      var zip = zipOf(rel);
      if (!zip) throw new Error("The rolling release has no zip.");
      return { rel: rel, zip: zip };
    }).catch(function () {
      return fetchJson(API + "/releases/latest").then(function (rel) {
        if (!rel || rel.prerelease) return null;
        var zip = zipOf(rel);
        return zip ? { rel: rel, zip: zip } : null;
      });
    }).then(function (found) {
      if (!found) return;
      release.windows = {
        url: found.zip.browser_download_url,
        version: versionFrom(found.zip.name, found.rel.tag_name),
        size: formatSize(found.zip.size),
      };
      applyRelease();
    }).catch(function () { /* Neither is reachable from here; the static link and build steps stand. */ });
  }

  // A hash such as #install-windows opens that tab directly.
  var hash = (location.hash || "").replace("#install-", "");
  if (hash && document.getElementById("install-" + hash)) selectTab(hash);
})();
