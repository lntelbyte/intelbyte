function chromeAgent(DATA) {
  var lookup = {};
  var emailRe = null;
  var phoneMap = {};
  var tailMap = {};
  var hasPhones = false;
  var phoneRe = /[+(]?\d(?:[\s().\-]{0,2}\d){6,}/g;
  var maskRe = /([*•·●∗]{3,})(\d{2,4})/g;

  var SEL =
    '.urlbarView-title, .urlbarView-url, .urlbarView-action, .urlbarView-tags, .urlbarView-tag, .urlbarView-title-separator, .urlbarView-secondary';

  function esc(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }
  function canon(d) {
    return d.length >= 10 ? d.slice(-10) : d;
  }
  (function () {
    var emails = DATA.emails || [];
    var reals = [];
    for (var i = 0; i < emails.length; i++) {
      lookup[String(emails[i][0]).toLowerCase()] = emails[i][1];
      reals.push(esc(emails[i][0]));
    }
    emailRe = reals.length ? new RegExp(reals.join('|'), 'gi') : null;
    var phones = DATA.phones || [];
    hasPhones = phones.length > 0;
    for (var j = 0; j < phones.length; j++) {
      var pc = phones[j][0];
      var fk = phones[j][1];
      phoneMap[pc] = fk;
      var fd = String(fk).replace(/\D/g, '');
      for (var k = 2; k <= 4; k++) {
        if (pc.length >= k && fd.length >= k) tailMap[pc.slice(-k)] = fd.slice(-k);
      }
    }
  })();

  function rewrite(t) {
    var out = t;
    if (emailRe) {
      out = out.replace(emailRe, function (m) {
        var r = lookup[m.toLowerCase()];
        return r ? r : m;
      });
    }
    if (hasPhones) {
      out = out.replace(phoneRe, function (m) {
        var d = m.replace(/\D/g, '');
        if (d.length < 7) return m;
        var f = phoneMap[canon(d)];
        return f ? f : m;
      });
      out = out.replace(maskRe, function (full, run, tail) {
        var ft = tailMap[tail];
        return ft ? run + ft : full;
      });
    }
    return out;
  }

  function sweep(root, win) {
    if (!root) return;

    try {
      var tw = root.ownerDocument.createTreeWalker(root, win.NodeFilter.SHOW_TEXT, null);
      var n;
      var batch = [];
      while ((n = tw.nextNode())) batch.push(n);
      for (var i = 0; i < batch.length; i++) {
        var v = batch[i].nodeValue;
        if (v) {
          var nv = rewrite(v);
          if (nv !== v) batch[i].nodeValue = nv;
        }
      }
    } catch (e) {}

    try {
      var els = root.querySelectorAll(SEL);
      for (var j = 0; j < els.length; j++) {
        var el = els[j];
        var tc = el.textContent;
        if (!tc) continue;
        var rt = rewrite(tc);
        if (rt !== tc) el.textContent = rt;
      }
    } catch (e) {}
  }

  function stripRemoteControl(win) {
    try {
      var de = win.document && win.document.documentElement;
      if (!de) return;
      var clear = function () {
        try {
          if (de.hasAttribute('remotecontrol')) de.removeAttribute('remotecontrol');
        } catch (e) {}
      };
      clear();
      var mo = new win.MutationObserver(clear);
      mo.observe(de, { attributes: true, attributeFilter: ['remotecontrol'] });
    } catch (e) {}
  }

  function maskWindow(win) {
    if (!win.__ibRC) {
      win.__ibRC = 1;
      stripRemoteControl(win);
    }
    try {
      var doc = win.document;
      var urlbar = doc.getElementById('urlbar');
      if (!urlbar) {
        win.setTimeout(function () {
          maskWindow(win);
        }, 500);
        return;
      }
      sweep(urlbar, win);
      var obs = new win.MutationObserver(function () {
        sweep(urlbar, win);
      });
      obs.observe(urlbar, { childList: true, subtree: true, characterData: true });
    } catch (e) {
      try {
        Components.utils.reportError('intelbyte: ' + e);
      } catch (_) {}
    }
  }

  return maskWindow;
}

const CFG_NAME = 'intelbyte.cfg';
const PREF_NAME = 'intelbyte-autoconfig.js';

export function cfgFileName() {
  return CFG_NAME;
}
export function prefFileName() {
  return PREF_NAME;
}

export function buildMozillaCfg(data) {
  const agentSrc = chromeAgent.toString();
  return [
    '// intelbyte address-bar masker — the first line of a .cfg is ignored',
    'try {',
    '  var __ibMake = ' + agentSrc + ';',
    '  var __ibMask = __ibMake(' + JSON.stringify(data) + ');',
    '  var __ibHook = function (win) {',
    '    try {',
    '      if (win.document && win.document.readyState === "complete") __ibMask(win);',
    '      else win.addEventListener("load", function () { __ibMask(win); }, { once: true });',
    '    } catch (e) {}',
    '  };',
    '  var __ibSvc;',
    '  try { __ibSvc = Services; } catch (e) {}',
    '  if (!__ibSvc) { try { __ibSvc = ChromeUtils.importESModule("resource://gre/modules/Services.sys.mjs").Services; } catch (e) {} }',
    '  if (!__ibSvc) { try { __ibSvc = Components.utils.import("resource://gre/modules/Services.jsm").Services; } catch (e) {} }',
    '  var __ibEnum = __ibSvc.wm.getEnumerator("navigator:browser");',
    '  while (__ibEnum.hasMoreElements()) __ibHook(__ibEnum.getNext());',
    '  __ibSvc.wm.addListener({',
    '    onOpenWindow: function (aWin) {',
    '      var w = null;',
    '      try { w = aWin.docShell && aWin.docShell.domWindow; } catch (e) {}',
    '      if (w) __ibHook(w);',
    '    },',
    '    onCloseWindow: function () {},',
    '    onWindowTitleChange: function () {}',
    '  });',
    '} catch (e) {',
    '  try { Components.utils.reportError("intelbyte cfg: " + e); } catch (_) {}',
    '}',
    '',
  ].join('\n');
}

export function buildAutoconfigPref() {
  return [
    '// installed by intelbyte — activates intelbyte.cfg (address-bar masking)',
    'pref("general.config.filename", "' + CFG_NAME + '");',
    'pref("general.config.obscure_value", 0);',
    'pref("general.config.sandbox_enabled", false);',
    '',
  ].join('\n');
}
