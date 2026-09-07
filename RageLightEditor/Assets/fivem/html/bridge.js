(function () {
    var resource = (typeof GetParentResourceName === 'function') ? GetParentResourceName() : 'ragetools_linking';
    var ws = null;
    var want = null;
    var timer = null;
    var open = false;

    function post(name, data) {
        try {
            fetch('https://' + resource + '/' + name, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json; charset=UTF-8' },
                body: JSON.stringify(data || {})
            }).catch(function () {});
        } catch (e) {}
    }

    function retry() {
        clearTimeout(timer);
        timer = setTimeout(connect, 2000);
    }

    function connect() {
        if (!want) return;
        if (ws) { try { ws.onclose = null; ws.close(); } catch (e) {} ws = null; }
        var url = 'ws://' + want.host + ':' + want.port + '/';
        try { ws = new WebSocket(url); } catch (e) { retry(); return; }
        ws.onopen = function () { open = true; post('ws', { connected: true }); };
        ws.onclose = function () { if (open) post('ws', { connected: false }); open = false; retry(); };
        ws.onerror = function () {};
        ws.onmessage = function (ev) {
            var data = null;
            try { data = JSON.parse(ev.data); } catch (e) { return; }
            if (data && typeof data === 'object') post('msg', data);
        };
    }

    window.addEventListener('message', function (ev) {
        var d = ev.data || {};
        if (d.connect) { want = d.connect; connect(); }
        if (d.reconnect && ws) { try { ws.close(); } catch (e) {} }
        if (d.send && ws && ws.readyState === 1) { try { ws.send(d.send); } catch (e) {} }
    });

    post('ready', {});
})();
