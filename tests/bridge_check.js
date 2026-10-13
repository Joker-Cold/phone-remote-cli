/* Regression checks for the terminal's WebSocket: reconnection, pipelining, immutable retries and why a socket was refused. */
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../web/terminal/bridge.js'), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));
const terminalId = 'a'.repeat(32);
const output = { t: 'out', terminal: { id: terminalId, state: 'running' }, chunks: [], after: 7 };

function page(host = 'relay.example') {
  const requests = [], replaced = [], sockets = [], timers = new Map(), received = [], errors = [], connections = [], listeners = {};
  let counter = 0, now = 0;
  class Socket {
    static OPEN = 1;
    constructor(url) { this.url = url; this.readyState = 0; this.sent = []; sockets.push(this); }
    open() { this.readyState = 1; if (this.onopen) this.onopen(); }
    send(text) { this.sent.push(JSON.parse(text)); }
    message(item) { this.onmessage({ data: JSON.stringify(item) }); }
    close(code = 1006) { this.readyState = 3; if (this.onclose) this.onclose({ code }); }
  }
  const document = { hidden: false, addEventListener: (name, handler) => { listeners[name] = handler; } };
  const context = {
    URL, URLSearchParams, TextDecoder, Uint8Array, AbortController, WebSocket: Socket, document,
    location: { hostname: host, host, protocol: 'https:', search: '?id=' + terminalId, href: 'https://' + host + '/terminal/?id=' + terminalId, replace(url) { replaced.push(url); } },
    localStorage: { getItem() { return null; } },
    crypto: { getRandomValues(bytes) { bytes.fill(++counter); return bytes; } },
    Date: { now: () => now },
    setTimeout(fn, delay) { const id = ++counter; timers.set(id, { fn, at: now + delay }); return id; },
    clearTimeout(id) { timers.delete(id); },
    fetch(url, options = {}) {
      return new Promise((resolve, reject) => {
        const request = { url, options, resolve, reject };
        requests.push(request);
        if (options.signal) options.signal.addEventListener('abort', () => reject(new Error('aborted')));
      });
    }
  };
  context.window = context;
  context.TerminalUI = { receive(item) { received.push(item); },
    connection: info => connections.push({ ...info }),
    error: text => errors.push(text), inputError: text => errors.push(text) };
  vm.runInNewContext(source, context);
  return { context, requests, replaced, sockets, received, errors, connections, listeners,
    advance(ms) {
      now += ms;
      for (const [id, timer] of [...timers]) if (timer.at <= now && timers.has(id)) { timers.delete(id); timer.fn(); }
    } };
}

async function check() {
  const live = page();
  live.context.ProjectTerminal.ready();
  const ws = live.sockets[0];
  assert.match(ws.url, /^wss:\/\/relay.example\/api\/terminal\/ws/);
  assert.deepEqual(live.connections.at(-1), { state: 'connecting', reconnects: 0, lastLatency: null });
  ws.open(); ws.message(output);
  assert.equal(live.connections.at(-1).state, 'connected');
  assert.equal(live.connections.at(-1).reconnects, 0);
  assert.equal(live.connections.at(-1).lastLatency, null, 'the socket handshake must not be presented as recent network latency');
  const measurement = live.requests.find(item => item.url === '../api/session');
  live.advance(37); measurement.resolve({ status: 200, ok: true, json: async () => ({ signed_in: true }) }); await settle();
  assert.equal(live.connections.at(-1).lastLatency, 37, 'latency must come from an immediate response, with a concrete round-trip time');
  live.context.ProjectTerminal.input('a');
  live.context.ProjectTerminal.input('b');
  assert.equal(ws.sent.length, 2, 'typing must not wait for the previous acknowledgment');
  const originals = ws.sent.map(item => JSON.stringify(item));
  ws.close();
  assert.equal(live.connections.at(-1).state, 'reconnecting');
  assert.equal(live.connections.at(-1).reconnects, 1);
  live.advance(300);
  const resumed = live.sockets[1];
  assert.match(resumed.url, /after=7$/);
  resumed.open(); resumed.message(output);
  assert.deepEqual(resumed.sent.map(item => JSON.stringify(item)), originals, 'lost acknowledgments retry identical IDs and payloads');
  for (const op of resumed.sent) resumed.message({ t: 'ack', id: op.id, status: 200, state: 'queued' });
  live.advance(21000);
  assert.equal(resumed.readyState, 1, 'acknowledged operations must cancel their timeout');

  const reconnecting = page();
  reconnecting.context.ProjectTerminal.ready();
  const previous = reconnecting.sockets[0];
  previous.open(); previous.message(output); previous.close(); reconnecting.advance(300);
  const reconnects = reconnecting.connections.at(-1).reconnects;
  previous.onclose({ code: 1006 });  // a late event from the old connection
  assert.equal(reconnecting.connections.at(-1).reconnects, reconnects, 'a stale close must not count as another reconnect');
  reconnecting.advance(4000); reconnecting.advance(0);
  assert.ok(reconnecting.requests.some(item => /^\.\.\/api\/terminal\?terminal=/.test(item.url)), 'an old close event must not cancel the new connection timeout');
  assert.equal(reconnecting.connections.at(-1).state, 'reconnecting', 'a failed retry must retain the outage state');
  assert.equal(reconnecting.connections.at(-1).reconnects, 1, 'retries during one outage must count only once');

  // A socket refused before its first message: one plain request tells why.
  const away = page();
  away.context.ProjectTerminal.ready(); away.sockets[0].open(); away.context.ProjectTerminal.input('kept');
  away.sockets[0].close(); await settle();
  let asked = away.requests.at(-1);
  assert.match(asked.url, /^\.\.\/api\/terminal\?terminal=a{32}&after=0$/);
  assert.equal(away.connections.at(-1).reconnects, 0, 'failing before the first connection is not a reconnect');
  asked.reject(new Error('network away')); await settle();
  assert.equal(away.errors.at(-1), '读取失败，正在重连；电脑上的终端会继续运行');
  away.advance(1500);
  assert.equal(away.sockets.length, 2, 'a network that is away is tried again');
  away.sockets[1].open(); away.sockets[1].message(output);
  assert.equal(away.connections.at(-1).state, 'connected');
  assert.equal(away.sockets[1].sent.at(-1).data, 'kept', 'input typed during the outage goes out on the next socket');
  away.sockets[1].message({ t: 'ack', id: away.sockets[1].sent.at(-1).id, status: 400, state: 'error', error: '终端已结束' });
  assert.equal(away.errors.at(-1), '终端已结束');

  const ended = page();
  ended.context.ProjectTerminal.ready(); ended.sockets[0].close(); await settle();
  ended.requests.at(-1).resolve({ status: 400, json: async () => ({ error: '终端参数无效' }) }); await settle();
  assert.equal(ended.errors.at(-1), '终端参数无效');
  assert.equal(ended.connections.at(-1).state, 'offline');
  ended.advance(5000);
  assert.equal(ended.sockets.length, 1, 'a terminal that is gone is not asked for again');

  const signedOut = page();
  signedOut.context.ProjectTerminal.ready(); signedOut.sockets[0].close(); await settle();
  signedOut.requests.at(-1).resolve({ status: 401, json: async () => ({ error: 'auth' }) }); await settle();
  assert.deepEqual(signedOut.replaced, ['../'], 'an expired sign-in leads back to the list');

  const timedOut = page();
  timedOut.context.ProjectTerminal.ready(); timedOut.advance(4000); await settle();
  assert.equal(timedOut.sockets[0].readyState, 3, 'a socket without its first message within four seconds is closed');
  timedOut.requests.at(-1).resolve({ status: 200, ok: true, json: async () => output }); await settle();
  timedOut.advance(1500);
  assert.equal(timedOut.sockets.length, 2);

  const hidden = page();
  hidden.context.ProjectTerminal.ready(); hidden.sockets[0].open(); hidden.sockets[0].message(output);
  hidden.context.document.hidden = true; hidden.listeners.visibilitychange(); await settle();
  assert.equal(hidden.sockets[0].readyState, 3);
  assert.equal(hidden.connections.at(-1).state, 'paused');
  assert.equal(hidden.connections.at(-1).reconnects, 0, 'pausing the page must not count as a reconnect');
  hidden.context.document.hidden = false; hidden.listeners.visibilitychange();
  assert.match(hidden.sockets[1].url, /after=7$/, 'returning resumes from the output already shown');
  const suspended = page();
  suspended.context.ProjectTerminal.ready(); suspended.sockets[0].open(); suspended.sockets[0].message(output);
  suspended.context.document.hidden = true; suspended.listeners.visibilitychange(); await settle();
  assert.equal(suspended.connections.at(-1).state, 'paused');
  assert.equal(suspended.connections.at(-1).reconnects, 0);
  assert.equal(suspended.requests[0].options.signal.aborted, true, 'backgrounding must cancel latency probes too');
  const fresh = page();
  fresh.context.ProjectTerminal.ready(); fresh.sockets[0].open();
  fresh.sockets[0].message({ ...output, terminal: { id: terminalId, state: 'closed', tool: 'claude', dir: 'new-project', session: '' } });
  fresh.context.ProjectTerminal.again();
  const freshRequest = fresh.requests.find(item => item.options.method === 'POST');
  const freshPayload = JSON.parse(freshRequest.options.body);
  assert.equal(freshPayload.history, false, 'a terminal without an ID must restart fresh instead of opening the resume picker');
  assert.equal(freshPayload.session, undefined);
  freshRequest.resolve({ status: 200, json: async () => ({ terminal: terminalId }) }); await settle();
  const resumedPage = page();
  resumedPage.context.ProjectTerminal.ready(); resumedPage.sockets[0].open();
  const savedSession = '12345678-1234-1234-1234-123456789abc';
  resumedPage.sockets[0].message({ ...output, terminal: { id: terminalId, state: 'closed', tool: 'claude', dir: 'demo', session: savedSession } });
  resumedPage.context.ProjectTerminal.again();
  assert.equal(JSON.parse(resumedPage.requests.find(item => item.options.method === 'POST').options.body).session, savedSession, 'continue must preserve the exact conversation');
  console.log('bridge transport checks passed');
}

check().catch(error => { console.error(error); process.exitCode = 1; });
