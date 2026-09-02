function agent(DATA) {
  if (window.__intelbyteAgent) {
    window.__intelbyteAgent.update(DATA);
    return;
  }

  var lookup = {};
  var emailRe = null;
  var phoneMap = {};
  var tailMap = {};
  var hasPhones = false;
  var customLookup = {};
  var customRe = null;
  var phoneRe = /[+(]?\d(?:[\s().\-]{0,2}\d){6,}/g;
  var maskRe = /([*•·●∗]{3,})(\d{2,4})/g;
  var docs = [];

  var SPLIT_SEL = 'b,i,em,strong,span,mark,u,s,small,font,bdi,bdo,sub,sup,tt,ins,del,cite,abbr,q';
  var INLINE_OK = { B:1, I:1, EM:1, STRONG:1, SPAN:1, MARK:1, U:1, S:1, SMALL:1, FONT:1, BDI:1, BDO:1, SUB:1, SUP:1, TT:1, INS:1, DEL:1, CITE:1, ABBR:1, Q:1, WBR:1 };

  function esc(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  function canon(digits) {
    return digits.length >= 10 ? digits.slice(-10) : digits;
  }

  function rebuild(data) {
    lookup = {};
    var reals = [];
    var emails = (data && data.emails) || [];
    for (var i = 0; i < emails.length; i++) {
      lookup[String(emails[i][0]).toLowerCase()] = emails[i][1];
      reals.push(esc(emails[i][0]));
    }
    emailRe = reals.length ? new RegExp(reals.join('|'), 'gi') : null;

    phoneMap = {};
    tailMap = {};
    var phones = (data && data.phones) || [];
    hasPhones = phones.length > 0;
    for (var j = 0; j < phones.length; j++) {
      var realCanon = phones[j][0];
      var fake = phones[j][1];
      phoneMap[realCanon] = fake;
      var fakeDigits = String(fake).replace(/\D/g, '');
      for (var k = 2; k <= 4; k++) {
        if (realCanon.length >= k && fakeDigits.length >= k) {
          tailMap[realCanon.slice(-k)] = fakeDigits.slice(-k);
        }
      }
    }

    customLookup = {};
    customRe = null;
    var customs = (data && data.customs) || [];
    var creals = [];

    customs = customs.slice().sort(function (a, b) {
      return String(b[0]).length - String(a[0]).length;
    });
    for (var m = 0; m < customs.length; m++) {
      customLookup[String(customs[m][0]).toLowerCase()] = customs[m][1];
      creals.push(esc(String(customs[m][0])));
    }
    customRe = creals.length ? new RegExp(creals.join('|'), 'gi') : null;
  }

  function rewrite(text) {
    var out = text;
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
    if (customRe) {
      out = out.replace(customRe, function (m) {
        var r = customLookup[m.toLowerCase()];
        return r != null ? r : m;
      });
    }
    return out;
  }

  function inEditable(node) {
    var p = node.nodeType === 3 ? node.parentNode : node;
    while (p) {
      if (p.nodeType === 1) {
        var tag = p.tagName;
        if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'TEXTAREA' || tag === 'INPUT') return true;
        if (p.isContentEditable) return true;
      }
      p = p.parentNode;
    }
    return false;
  }

  function fix(node) {
    if (!emailRe && !hasPhones && !customRe) return;
    if (inEditable(node)) return;
    var v = node.nodeValue;
    if (!v) return;
    var nv = rewrite(v);
    if (nv !== v) node.nodeValue = nv;
  }

  function inlineOnly(el) {
    var kids;
    try {
      kids = el.getElementsByTagName('*');
    } catch (e) {
      return false;
    }
    for (var i = 0; i < kids.length; i++) {
      if (!INLINE_OK[kids[i].tagName]) return false;
    }
    return true;
  }

  function fixSplit(root) {
    if ((!emailRe && !hasPhones && !customRe) || !root || !root.querySelectorAll) return;
    var marks;
    try {
      marks = root.querySelectorAll(SPLIT_SEL);
    } catch (e) {
      return;
    }
    if (!marks || !marks.length) return;
    var seen = typeof Set === 'function' ? new Set() : null;
    for (var i = 0; i < marks.length; i++) {
      var p = marks[i].parentNode;
      if (!p || p.nodeType !== 1) continue;
      if (seen) {
        if (seen.has(p)) continue;
        seen.add(p);
      }
      var tc = p.textContent;
      if (!tc || tc.length > 300) continue;
      var nv = rewrite(tc);
      if (nv === tc) continue;
      if (inEditable(p)) continue;
      if (!inlineOnly(p)) continue;
      p.textContent = nv;
    }
  }

  function walk(root) {
    if ((!emailRe && !hasPhones && !customRe) || !root) return;
    if (root.nodeType === 3) {
      fix(root);
      return;
    }
    if (root.nodeType !== 1 && root.nodeType !== 9 && root.nodeType !== 11) return;
    var doc = root.ownerDocument || document;

    var tw = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, null);
    var texts = [];
    var shadows = [];
    if (root.nodeType === 1 && root.shadowRoot) shadows.push(root.shadowRoot);
    var n;
    while ((n = tw.nextNode())) {
      if (n.nodeType === 3) texts.push(n);
      else if (n.shadowRoot) shadows.push(n.shadowRoot);
    }
    for (var i = 0; i < texts.length; i++) fix(texts[i]);
    fixSplit(root);
    for (var s = 0; s < shadows.length; s++) processShadow(shadows[s]);
  }

  function hookFrame(frame) {
    try {
      if (frame.contentDocument) processDoc(frame.contentDocument);
    } catch (e) {}
    if (!frame.__ibLoad) {
      frame.__ibLoad = 1;
      try {
        frame.addEventListener('load', function () {
          try {
            processDoc(frame.contentDocument);
          } catch (e) {}
        });
      } catch (e) {}
    }
  }

  function scanFrames(root) {
    var list;
    try {
      list = root.querySelectorAll ? root.querySelectorAll('iframe, frame') : [];
    } catch (e) {
      return;
    }
    for (var i = 0; i < list.length; i++) hookFrame(list[i]);
  }

  function onMutations(muts) {
    for (var i = 0; i < muts.length; i++) {
      var m = muts[i];
      if (m.type === 'characterData') {
        fix(m.target);

        var pe = m.target.parentNode;
        if (pe && pe.nodeType === 1) {
          fixSplit(pe);
          if (pe.parentNode && pe.parentNode.nodeType === 1) fixSplit(pe.parentNode);
        }
      } else {
        for (var j = 0; j < m.addedNodes.length; j++) {
          var an = m.addedNodes[j];
          if (an.nodeType === 3) {
            fix(an);
          } else if (an.nodeType === 1) {
            if (an.tagName === 'IFRAME' || an.tagName === 'FRAME') hookFrame(an);
            walk(an);
            scanFrames(an);
          }
        }
      }
    }
  }

  function processDoc(doc) {
    if (!doc) return;
    if (doc.defaultView) hookAttachShadow(doc.defaultView);

    var target = doc.documentElement || doc;
    for (var i = 0; i < docs.length; i++) {
      if (docs[i] === doc) {
        walk(doc.body || doc.documentElement || doc);
        scanFrames(doc);
        return;
      }
    }
    docs.push(doc);
    walk(doc.body || doc.documentElement || doc);
    try {
      new MutationObserver(onMutations).observe(target, {
        childList: true,
        subtree: true,
        characterData: true,
      });
    } catch (e) {}
    scanFrames(doc);
  }

  function hookAttachShadow(win) {
    try {
      var proto = win && win.Element && win.Element.prototype;
      if (!proto || proto.__ibShadowHooked) return;
      var orig = proto.attachShadow;
      if (typeof orig !== 'function') return;
      proto.__ibShadowHooked = 1;
      proto.attachShadow = function () {
        var root = orig.apply(this, arguments);
        try {
          processShadow(root);
        } catch (e) {}
        return root;
      };
    } catch (e) {}
  }

  function processShadow(sr) {
    if (!sr) return;
    for (var i = 0; i < docs.length; i++) {
      if (docs[i] === sr) {
        walk(sr);
        return;
      }
    }
    docs.push(sr);
    walk(sr);
    try {
      new MutationObserver(onMutations).observe(sr, {
        childList: true,
        subtree: true,
        characterData: true,
      });
    } catch (e) {}
  }

  function boot() {

    hookAttachShadow(window);
    processDoc(document);
  }

  window.__intelbyteAgent = {
    data: null,
    frames: function () {
      return docs.length;
    },
    update: function (data) {
      var s = JSON.stringify(data);
      if (s === this.data) return;
      this.data = s;
      rebuild(data);
      for (var i = 0; i < docs.length; i++) {
        try {

          walk(docs[i].body || docs[i].documentElement || docs[i]);
        } catch (e) {}
      }
    },
  };

  window.__intelbyteAgent.update(DATA);
  boot();
}

export function buildPayload(data) {
  return '(' + agent.toString() + ')(' + JSON.stringify(data) + ');';
}
