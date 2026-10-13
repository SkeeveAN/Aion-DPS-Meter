namespace AionDPS.Streaming;

/// <summary>The files of the stream overlay page: one HTML, one style, one script, no libraries and nothing loaded from elsewhere (see the server's Content-Security-Policy).</summary>
public static class StreamOverlayPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Aion DPS</title>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <link rel="stylesheet" href="overlay.css"></head>
        <body><div id="meter"><div id="head"><span id="mode"></span><span id="heads"></span></div><div id="boss" hidden><div id="bossname"></div><div id="bosshp"><div id="bosshpfill"></div></div><div id="bosstext"></div></div><div id="rows"></div></div>
        <script src="overlay.js"></script></body></html>
        """;

    public const string Style = """
        :root { color-scheme: dark; }
        html, body { margin: 0; background: transparent; font-family: "Segoe UI", system-ui, sans-serif; color: #f2f5f8; overflow: hidden; }
        #meter { width: 100%; box-sizing: border-box; padding: 4px; }
        #head { display: flex; justify-content: space-between; font-size: 12px; color: #9db3c2; padding: 2px 8px; text-transform: uppercase; letter-spacing: .04em; }
        #heads span { display: inline-block; min-width: 64px; text-align: right; }
        #boss { margin: 2px 4px 6px; }
        #bossname { font-weight: 600; font-size: 13px; }
        #bosshp { height: 8px; background: rgba(27,42,54,.85); border-radius: 4px; overflow: hidden; margin: 3px 0; }
        #bosshpfill { height: 100%; width: 0; background: #5bd07a; transition: width .25s linear; }
        #bosstext { font-size: 11px; color: #9db3c2; }
        .row { position: relative; display: flex; align-items: center; height: 26px; margin: 2px 0; padding: 0 8px; border-radius: 5px; background: rgba(14,24,32,.62); overflow: hidden; }
        .row .bar { position: absolute; left: 0; top: 0; bottom: 0; width: 0; opacity: .55; transition: width .25s linear; }
        .row > * { position: relative; }
        .row .rank { width: 18px; color: #9db3c2; font-size: 12px; }
        .row img { width: 20px; height: 20px; margin-right: 6px; }
        .row .name { flex: 1; font-weight: 600; font-size: 14px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; text-shadow: 0 1px 2px #000; }
        .row.self .name { color: #ffba42; }
        .row .cell { min-width: 64px; text-align: right; font-size: 13px; font-variant-numeric: tabular-nums; text-shadow: 0 1px 2px #000; }
        .row .sub { position: absolute; right: 8px; bottom: 0; font-size: 9px; color: #9db3c2; }
        """;

    public const string Script = """
        (function () {
          var params = new URLSearchParams(location.search);
          var scale = parseFloat(params.get('scale')) || 1;
          if (scale !== 1) { document.body.style.zoom = scale; }
          var maxRows = parseInt(params.get('rows'), 10) || 0;
          var rowsEl = document.getElementById('rows');
          var colors = { Cleric: '#ffe27a', Chanter: '#e0a020', Templar: '#2f5fd0', Gladiator: '#5aa9ff', Brawler: '#e07a5f',
                         Assassin: '#7ee08f', Ranger: '#2f9e57', Sorcerer: '#b98af5', Spiritmaster: '#8a4fd8' };
          function render(s) {
            document.getElementById('mode').textContent = s.modeLabel || '';
            var heads = document.getElementById('heads'); heads.textContent = '';
            (s.headers || []).forEach(function (h) { var e = document.createElement('span'); e.textContent = h; heads.appendChild(e); });
            var boss = document.getElementById('boss');
            if (s.boss) {
              boss.hidden = false;
              document.getElementById('bossname').textContent = s.boss.name;
              document.getElementById('bosshpfill').style.width = Math.max(0, Math.min(100, s.boss.percent)) + '%';
              document.getElementById('bosstext').textContent = s.boss.text;
            } else { boss.hidden = true; }
            var limit = maxRows || s.maxRows || 8;
            rowsEl.textContent = '';
            (s.rows || []).slice(0, limit).forEach(function (r) {
              var row = document.createElement('div'); row.className = 'row' + (r.self ? ' self' : '');
              var bar = document.createElement('div'); bar.className = 'bar';
              bar.style.width = Math.round((r.bar || 0) * 100) + '%'; bar.style.background = colors[r.cls] || '#9fb3c8';
              var rank = document.createElement('span'); rank.className = 'rank'; rank.textContent = r.rank || '';
              var img = document.createElement('img'); img.alt = ''; img.src = 'c/' + encodeURIComponent(r.cls || 'x') + '.png'; img.onerror = function () { img.style.visibility = 'hidden'; };
              var name = document.createElement('span'); name.className = 'name'; name.textContent = r.name;
              row.appendChild(bar); row.appendChild(rank); row.appendChild(img); row.appendChild(name);
              (r.cells || []).forEach(function (c) { var e = document.createElement('span'); e.className = 'cell'; e.textContent = c; row.appendChild(e); });
              if (r.sub) { var sub = document.createElement('span'); sub.className = 'sub'; sub.textContent = r.sub; row.appendChild(sub); }
              rowsEl.appendChild(row);
            });
          }
          function connect() {
            var source = new EventSource('events');
            source.onmessage = function (e) { try { render(JSON.parse(e.data)); } catch (x) { } };
            source.onerror = function () { source.close(); setTimeout(connect, 2000); };
          }
          connect();
        })();
        """;
}
