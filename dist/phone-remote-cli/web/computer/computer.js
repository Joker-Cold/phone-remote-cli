/* The program on the computer, asked from the phone for a few of its settings, which can be changed here. */
(() => {
  'use strict';
  const $ = id => document.getElementById(id), native = window.RemoteCliNative || null;
  const newId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('');
  const saved = (key, fallback) => {
    if (native && native.pref) { const chosen = native.pref(key); if (chosen) return chosen; }
    try { return localStorage.getItem('rcli-' + key) || fallback; } catch (error) { return fallback; }
  };
  const svg = path => `<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;
  function el(tag, props, ...children) {
    const node = document.createElement(tag);
    Object.keys(props || {}).forEach(key => { if (key in node) node[key] = props[key]; else node.setAttribute(key, props[key]); });
    node.append(...children.filter(Boolean));
    return node;
  }
  let toastTimer = 0;
  function toast(text) { const box = $('toast'); box.textContent = text; box.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { box.hidden = true; }, 3000); }

  // ---- look: the skin and the text size of the other pages
  const skin = window.RemoteCliPaint(saved('skin', 'paper'));
  document.documentElement.style.fontSize = { small: '14px', normal: '15.5px', large: '17px', larger: '19px' }[saved('text', 'normal')] || '15.5px';
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.content = skin.t.background;
  if (native && native.chrome) native.chrome(skin.t.background);
  $('back').innerHTML = svg('<path d="M15 5l-7 7 7 7"/>');
  $('back').addEventListener('click', () => { if (history.length > 1 && document.referrer.startsWith(location.origin)) history.back(); else location.replace('../'); });

  // ---- asking the program
  async function ask(action, fields) {
    const reply = await fetch('../api/computer', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(Object.assign({ id: newId(), action }, fields)) });
    let body = {};
    try { body = await reply.json(); } catch (error) { /* not JSON */ }
    if (reply.status === 401) { location.replace('../'); throw new Error('请重新登录'); }
    if (!reply.ok) throw new Error(body.error || '请求失败（' + reply.status + '）');
    return body;
  }

  // ---- what is shown
  const SETTINGS = [
    { name: 'rights', title: '新终端的权限', about: 'Claude Code 和 Codex 从手机启动时的权限模式，对之后新开的终端生效', choices: [['full', '默认'], ['edit', '自动改文件'], ['read', '只读']] },
    { name: 'autostart', title: '登录 Windows 后自动启动', about: '开机后在托盘运行，不弹出窗口' }];
  let told = null;
  function problem(title, text) {
    $('state').hidden = false;
    $('state-title').textContent = title; $('state-text').textContent = text;
  }
  function draw() {
    $('loading').hidden = true;
    if (!told) return;
    $('body').hidden = false;
    $('device-text').textContent = told.name + ' · v' + told.version;
    $('settings').replaceChildren(...SETTINGS.map(setting => {
      const value = told.settings[setting.name];
      const control = setting.choices
        ? el('span', { className: 'pick', role: 'group', 'aria-label': setting.title }, ...setting.choices.map(([key, label]) =>
            el('button', { type: 'button', textContent: label, 'aria-pressed': String(key === value), onclick: () => { if (key !== value) change(setting.name, key); } })))
        : el('button', { type: 'button', className: 'flip', role: 'switch', 'aria-checked': String(value === true), 'aria-label': setting.title, onclick: () => change(setting.name, value !== true) });
      return el('li', { className: 'card' }, el('span', { className: 'text' }, el('b', { textContent: setting.title }), el('span', { textContent: setting.about })), control);
    }));
  }
  async function change(setting, value) {
    try { told = await ask('settings_change', { name: setting, value }); toast('已保存到电脑'); }
    catch (error) { toast(error.message); }
    draw();
  }

  (async () => {
    try { told = await ask('settings_read'); }
    catch (error) { problem('没有读到电脑的设置', error.message); }
    draw();
  })();
})();
