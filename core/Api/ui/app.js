'use strict';
// MoSaProxy browser UI. Everything captured is untrusted text: it is only ever put on the page with textContent (never as markup).

const MAX_SHOWN_BYTES = 1024 * 1024;
const state = { token: null, rows: new Map(), filter: '', selected: null, capture: true, tab: 'overview', loadId: 0 };

// ---- token ---------------------------------------------------------------------------------------------------------------

function findToken() {
  const given = new URLSearchParams(location.search).get('token');
  try {
    if (given) sessionStorage.setItem('mosaproxy-token', given);
    return given || sessionStorage.getItem('mosaproxy-token') || '';
  } catch { return given || ''; }
}
state.token = findToken();
// The token has been taken; keep it out of the address bar, history and any screen share.
if (location.search.includes('token=')) history.replaceState(null, '', location.pathname);

// ---- helpers -------------------------------------------------------------------------------------------------------------

const $ = id => document.getElementById(id);

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key === 'class') node.className = value;
    else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
    else node.setAttribute(key, value);
  }
  for (const child of children.flat()) if (child != null) node.append(child);
  return node;
}

async function api(path, options = {}) {
  const response = await fetch('api/' + path, { ...options, headers: { 'X-MoSaProxy-Token': state.token, ...(options.headers || {}) } });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response;
}
const json = async (path, options) => (await api(path, options)).json();

function bytes(n) {
  if (n < 1024) return n + ' B';
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
  return (n / 1024 / 1024).toFixed(1) + ' MB';
}
const statusClass = s => (s ? 's' + String(s)[0] : 'err');
const clock = iso => new Date(iso).toLocaleTimeString();

// ---- the list ------------------------------------------------------------------------------------------------------------

function query(extra = {}) {
  const params = new URLSearchParams(extra);
  if (state.filter) params.set('q', state.filter);
  return params.toString();
}

function addRow(e) {
  if (state.rows.has(e.id)) return;
  state.rows.set(e.id, e);
  const status = e.status ?? (e.error ? 'ERR' : '');
  const tr = el('tr', { 'data-id': e.id, onclick: () => select(e.id) },
    el('td', { class: 'num' }, String(e.id)),
    el('td', {}, clock(e.timestamp)),
    el('td', {}, e.method),
    el('td', { title: e.host }, e.host),
    el('td', { title: e.url }, e.path),
    el('td', { class: statusClass(e.status) }, String(status)),
    el('td', { class: 'num' }, bytes(e.responseBytes)),
    el('td', { class: 'num' }, Math.round(e.durationMs).toString()));
  const body = $('rows');
  const stick = $('list').scrollTop + $('list').clientHeight >= $('list').scrollHeight - 30;
  body.append(tr);
  if (stick) $('list').scrollTop = $('list').scrollHeight;
  showCount();
}

function showCount() {
  $('count').textContent = state.rows.size + (state.rows.size === 1 ? ' exchange' : ' exchanges');
  $('empty').hidden = state.rows.size > 0;
}

async function loadAll() {
  const mine = ++state.loadId;
  state.rows.clear();
  $('rows').replaceChildren();
  showCount();
  let after = null;
  do {
    const page = await json('exchanges?' + query({ limit: 500, ...(after ? { after } : {}) }));
    if (mine !== state.loadId) return;
    page.items.forEach(addRow);
    after = page.next;
  } while (after);
  if (state.selected && !state.rows.has(state.selected)) deselect();
  markSelected();
}

/** A new exchange finished: fetch it (through the same filter) and add it. */
async function fetchOne(id) {
  const page = await json('exchanges?' + query({ after: id - 1, limit: 1 }));
  page.items.filter(e => e.id === id).forEach(addRow);
}

// ---- the detail pane -----------------------------------------------------------------------------------------------------

function markSelected() {
  for (const tr of $('rows').children) tr.classList.toggle('selected', Number(tr.dataset.id) === state.selected);
}

function deselect() {
  state.selected = null;
  $('detail').hidden = true;
}

async function select(id) {
  state.selected = id;
  markSelected();
  $('detail').hidden = false;
  await renderDetail();
}

function tabsFor(detail) {
  const tabs = ['overview', 'request', 'response'];
  if (detail.hasWebSocketLog) tabs.push('websocket');
  return tabs;
}

async function renderDetail() {
  const id = state.selected;
  let detail;
  try { detail = await json('exchanges/' + id); }
  catch (error) { $('pane').replaceChildren(el('p', { class: 'err' }, 'Could not load exchange ' + id + ': ' + error.message)); return; }
  if (id !== state.selected) return;
  const tabs = tabsFor(detail);
  if (!tabs.includes(state.tab)) state.tab = 'overview';
  $('tabs').replaceChildren(...tabs.map(name => el('button', {
    role: 'tab', 'aria-selected': String(name === state.tab), onclick: () => { state.tab = name; renderDetail(); },
  }, name[0].toUpperCase() + name.slice(1))));
  const pane = $('pane');
  pane.replaceChildren();
  if (state.tab === 'overview') pane.append(...overview(detail));
  else if (state.tab === 'websocket') pane.append(await textPart('exchanges/' + id + '/websocket'));
  else pane.append(...await message(detail, state.tab));
}

function overview(d) {
  const s = d.summary;
  const rows = [
    ['URL', s.url], ['Method', s.method], ['Status', s.status ?? '(none)'], ['Mode', s.mode],
    ['Client', `${d.client}:${d.clientPort}` + (d.clientTlsVersion ? ' (' + d.clientTlsVersion + ')' : '')],
    ['Protocols', `client ${s.clientHttpVersion}` + (s.originHttpVersion ? `, origin ${s.originHttpVersion}` : '')],
    ['Started', new Date(s.timestamp).toLocaleString()], ['Duration', Math.round(s.durationMs) + ' ms'],
    ['Request bytes', s.requestBytes], ['Response bytes', s.responseBytes],
    ['Captured from', `request ${d.requestSource}, response ${d.responseSource}`],
    ['Connections reused', `client ${d.clientConnectionReused}, origin ${d.originConnectionReused}`],
  ];
  if (d.requestBodyTruncated || d.responseBodyTruncated) rows.push(['Truncated', 'A body was cut at the capture size limit.']);
  if (s.error) rows.push(['Error', s.error]);
  const out = [el('dl', { class: 'kv' }, rows.flatMap(([k, v]) => [el('dt', {}, k), el('dd', { class: k === 'Error' ? 'err' : '' }, String(v))]))];
  const times = Object.entries(d.times || {});
  if (times.length) out.push(el('h3', {}, 'Timings'), el('dl', { class: 'kv' }, times.flatMap(([k, v]) => [el('dt', {}, k), el('dd', {}, new Date(v).toISOString())])));
  return out;
}

async function message(detail, which) {
  const head = which === 'request' ? detail.request : detail.response;
  const id = detail.summary.id;
  const out = [];
  if (!head) return [el('p', { class: 'muted' }, 'No ' + which + ' was captured.')];
  out.push(el('h3', {}, head.firstLine));
  out.push(el('table', { class: 'headers' }, el('tbody', {}, head.headers.map(h => el('tr', {}, el('td', {}, h.key), el('td', {}, h.value))))));
  out.push(el('h3', {}, 'Body'));
  out.push(await bodyPart(id, which, head));
  return out;
}

function headerValue(head, name) {
  return head.headers.find(h => h.key.toLowerCase() === name)?.value ?? '';
}

async function bodyPart(id, which, head) {
  let response;
  try { response = await api(`exchanges/${id}/${which}/body?decode=true`); }
  catch (error) { return el('p', { class: 'muted' }, 'No body: ' + error.message); }
  const blob = await response.blob();
  if (blob.size === 0) return el('p', { class: 'muted' }, '(empty)');
  const type = (headerValue(head, 'content-type') || blob.type || '').toLowerCase();
  const decoded = response.headers.get('X-MoSaProxy-Decoded') === 'true';
  const note = decoded || !headerValue(head, 'content-encoding') ? '' : ` (still ${headerValue(head, 'content-encoding')}-encoded: it could not be decoded)`;
  if (type.startsWith('image/') && !type.includes('svg'))
    return el('div', {}, el('img', { class: 'preview', src: URL.createObjectURL(new Blob([blob], { type: type.split(';')[0] })) }), el('p', { class: 'muted' }, bytes(blob.size)));
  if (isText(type) && !note) {
    let text = await blob.slice(0, MAX_SHOWN_BYTES).text();
    if (type.includes('json')) { try { text = JSON.stringify(JSON.parse(text), null, 2); } catch { /* shown as it is */ } }
    return el('div', {}, el('pre', {}, text), blob.size > MAX_SHOWN_BYTES ? el('p', { class: 'muted' }, `Showing the first ${bytes(MAX_SHOWN_BYTES)} of ${bytes(blob.size)}.`) : null);
  }
  const link = el('a', { href: URL.createObjectURL(blob), download: `exchange-${id}-${which}.bin` }, 'Download');
  return el('p', { class: 'muted' }, `${type || 'Binary data'}, ${bytes(blob.size)}${note}. `, link);
}

function isText(type) {
  return !type || type.startsWith('text/') || /json|xml|javascript|ecmascript|x-www-form-urlencoded|html|svg|yaml|graphql/.test(type);
}

async function textPart(path) {
  try { return el('pre', {}, await (await api(path)).text()); }
  catch (error) { return el('p', { class: 'muted' }, 'Not available: ' + error.message); }
}

// ---- capture switch, save, stop ------------------------------------------------------------------------------------------

function showCapture(enabled) {
  state.capture = enabled;
  const button = $('capture');
  button.textContent = enabled ? 'Capture: on' : 'Capture: off';
  button.className = enabled ? 'on' : 'off';
}

$('capture').addEventListener('click', async () => {
  try { showCapture((await json('capture', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled: !state.capture }) })).enabled); }
  catch (error) { alert('Could not change capture: ' + error.message); }
});

$('stop').addEventListener('click', async () => {
  if (!confirm('Stop the proxy? It finishes running exchanges, then exits and deletes the captured files.')) return;
  try { await api('stop', { method: 'POST' }); $('live').textContent = 'stopping…'; }
  catch (error) { alert('Could not stop: ' + error.message); }
});

$('export').addEventListener('click', event => {
  event.preventDefault();
  location.href = 'api/export/saz?token=' + encodeURIComponent(state.token);
});

let debounce;
$('filter').addEventListener('input', event => {
  clearTimeout(debounce);
  debounce = setTimeout(() => { state.filter = event.target.value.trim(); loadAll().catch(report); }, 250);
});

// ---- live events ---------------------------------------------------------------------------------------------------------

function listen() {
  // (an EventSource cannot set headers, so the token goes in the address; it reconnects by itself and sends Last-Event-ID)
  const source = new EventSource('api/stream?token=' + encodeURIComponent(state.token));
  source.onopen = () => { $('live').textContent = 'live'; };
  source.onerror = () => { $('live').textContent = source.readyState === EventSource.CLOSED ? 'disconnected' : 'reconnecting…'; };
  source.addEventListener('exchange-finished', e => fetchOne(JSON.parse(e.data).id).catch(report));
  source.addEventListener('capture', e => showCapture(JSON.parse(e.data).enabled));
  source.addEventListener('reset', () => loadAll().catch(report));
}

function report(error) { $('live').textContent = 'error: ' + error.message; }

// ---- start ---------------------------------------------------------------------------------------------------------------

(async () => {
  if (!state.token) { $('live').textContent = 'no token: open the address the console printed'; return; }
  try {
    showCapture((await json('capture')).enabled);
    listen();          // (first, so an exchange that finishes while the list loads is not missed)
    await loadAll();
  } catch (error) { report(error); }
})();
