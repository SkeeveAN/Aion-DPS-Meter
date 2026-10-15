// Top menu: one link per panel, built from the rendered panel headings
(function () {
  var tries = 0, t = setInterval(function () {
    var heads = document.querySelectorAll('h4.gheader');
    if (!heads.length && ++tries < 100) return;
    clearInterval(t);
    var bar = document.createElement('div');
    bar.id = 'topbar';
    var top = document.createElement('a');
    top.href = '#'; top.textContent = 'Overview';
    bar.appendChild(top);
    heads.forEach(function (h) {
      var a = document.createElement('a');
      a.href = '#' + h.id;
      a.textContent = h.childNodes[0].textContent;
      bar.appendChild(a);
    });
    document.body.appendChild(bar);
  }, 100);
})();

// German dates: 15/Oct/2026 -> 15.10.2026, 2026-10-15 09:15:53 -> 15.10.2026 09:15:53 (text nodes incl. chart axes)
(function () {
  var M = {jan: '01', feb: '02', mar: '03', apr: '04', may: '05', jun: '06', jul: '07', aug: '08', sep: '09', oct: '10', nov: '11', dec: '12'};
  var RX1 = /\b(\d{1,2})\/([A-Za-z]{3})\/(\d{4})\b/g, RX2 = /\b(\d{4})-(\d{2})-(\d{2})\b/g;
  function fixCells(root) {
    // table cells split their text with empty <b> tags ("15" + "/Oct/2026")
    root.querySelectorAll('span.value').forEach(function (el) {
      var m = /^(\d{1,2})\/([A-Za-z]{3})\/(\d{4})$/.exec(el.textContent);
      if (m && M[m[2].toLowerCase()]) el.textContent = (m[1].length < 2 ? '0' + m[1] : m[1]) + '.' + M[m[2].toLowerCase()] + '.' + m[3];
    });
  }
  function fix(root) {
    fixCells(root);
    var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), n;
    while ((n = w.nextNode())) {
      var t = n.nodeValue;
      if (!/\d{1,2}\/[A-Za-z]{3}\/\d{4}|\d{4}-\d{2}-\d{2}/.test(t)) continue;
      t = t.replace(RX1, function (m, d, mo, y) { var k = M[mo.toLowerCase()]; return k ? (d.length < 2 ? '0' + d : d) + '.' + k + '.' + y : m; })
           .replace(RX2, function (m, y, mo, d) { return d + '.' + mo + '.' + y; });
      if (t !== n.nodeValue) n.nodeValue = t;
    }
  }
  var busy = false;
  new MutationObserver(function () {
    if (busy) return;
    busy = true;
    requestAnimationFrame(function () { fix(document.body); busy = false; });
  }).observe(document.documentElement, {childList: true, subtree: true, characterData: true});
})();
