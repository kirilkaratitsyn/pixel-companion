'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const TOKEN_KEY = 'pixel-companion-token-v1';
  const IDLE_KEY = 'pixel-companion-idle-v2';
  const config = { brightness: Number(localStorage.getItem('brightness') || 25), idle: Number(localStorage.getItem(IDLE_KEY) ?? 0), usbLeft: localStorage.getItem('usb-side') !== 'right' };
  let token = localStorage.getItem(TOKEN_KEY), ws, online = false, state = null, received = 0, lastMessage = 0;
  let reconnectTimer, retry = 1000, generation = 0, panel = '', panelTimer, toastTimer, artworkHash = null;
  let lastTouch = performance.now(), idleSince = performance.now(), sleeping = false, wakeUntil = 0, lastLocked = false;
  let dragSeek = false, volumeDrag = null, mixerSignature = '', manualBlank = false;
  const pending = new Map();
  const fmt = seconds => { const s = Math.max(0, Math.floor(seconds)); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
  const native = (blank) => { try { if (window.PixelNative) window.PixelNative.display(blank, Math.max(.02, config.brightness / 100) * (performance.now() - lastTouch > 30000 ? .65 : 1)); else document.documentElement.style.setProperty('--brightness', .5 + .5 * Math.min(1, config.brightness / 100)); } catch (_) {} };
  const applyOrientation = () => { try { if (window.PixelNative?.orientation) window.PixelNative.orientation(config.usbLeft); } catch (_) {} };
  function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true, 4000); }
  function closePanel() { $('overlay').hidden = true; panel = ''; volumeDrag = null; clearTimeout(panelTimer); }
  function setSleeping(value) { if (sleeping === value) return; sleeping = value; $('wake').hidden = !value; if (value) closePanel(); native(value); }
  function showPair() { token = null; localStorage.removeItem(TOKEN_KEY); generation++; clearTimeout(reconnectTimer); if (ws) ws.close(); online = false; setSleeping(false); closePanel(); $('pairing').hidden = false; render(); }
  function offline() { online = false; for (const p of pending.values()) clearTimeout(p); pending.clear(); render(); }
  function connect() {
    if (!token) { showPair(); return; }
    clearTimeout(reconnectTimer); const mine = ++generation;
    ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
    ws.addEventListener('open', () => { if (mine === generation) ws.send(JSON.stringify({ type: 'auth', token })); });
    ws.addEventListener('message', event => {
      if (mine !== generation) return;
      let message; try { message = JSON.parse(event.data); } catch (_) { return; }
      lastMessage = performance.now();
      if (message.type === 'auth_error') { showPair(); return; }
      if (message.type === 'ready') { if (message.version !== 1) { toast('Версии приложений несовместимы'); ws.close(); return; } retry = 1000; $('pairing').hidden = true; return; }
      if (message.type === 'state') {
        if (message.version !== 1) return;
        const wasPlaying = state?.media?.status === 'playing';
        state = message; received = performance.now(); online = true;
        if (state.media.status === 'playing') idleSince = received;
        else if (wasPlaying) idleSince = received;
        if (state.locked && !lastLocked) { wakeUntil = 0; setSleeping(true); }
        if (!state.locked && lastLocked) { lastTouch = received; wakeUntil = received + 15000; setSleeping(false); }
        lastLocked = state.locked;
        if (state.media.artworkHash !== artworkHash) { $('art').hidden = true; $('art-placeholder').hidden = false; }
        if (!state.media.artworkHash) { artworkHash = null; $('art').removeAttribute('src'); }
        if (state.media.status === 'playing' && !state.locked && !manualBlank) setSleeping(false);
        render(); return;
      }
      if (message.type === 'artwork') {
        if (message.hash === state?.media?.artworkHash && typeof message.data === 'string' && message.data.startsWith('data:image/jpeg;base64,')) {
          artworkHash = message.hash; $('art').src = message.data; $('art').hidden = false; $('art-placeholder').hidden = true;
        }
        return;
      }
      if (message.type === 'result') { const timer = pending.get(message.id); if (timer) clearTimeout(timer); pending.delete(message.id); if (!message.ok) toast(message.error || 'Команда не выполнена'); }
    });
    ws.addEventListener('close', () => { if (mine !== generation) return; offline(); if (token) { reconnectTimer = setTimeout(connect, retry); retry = Math.min(15000, retry * 1.8); } });
    ws.addEventListener('error', () => { if (mine === generation) ws.close(); });
  }
  function command(name, extra = {}) {
    if (!online || ws?.readyState !== WebSocket.OPEN) { toast('Нет связи с компьютером'); return; }
    const bytes = new Uint8Array(12); crypto.getRandomValues(bytes); const id = Array.from(bytes, n => n.toString(16).padStart(2, '0')).join('');
    const m = state.media;
    ws.send(JSON.stringify({ id, name, sessionId: m.sessionId, revision: m.revision, ...extra }));
    // Never replay commands after reconnect: a delayed next/toggle could affect a different track.
    pending.set(id, setTimeout(() => { pending.delete(id); toast('Компьютер не подтвердил команду'); }, 4500));
  }
  function render() {
    const m = state?.media, hasMedia = online && !!m?.sessionId;
    $('music').hidden = !hasMedia; $('empty').hidden = hasMedia;
    $('source-name').textContent = hasMedia ? m.source : 'Pixel Companion';
    $('source').disabled = !online || !state.sources.length;
    $('status').textContent = !online ? 'Соединяемся…' : state.error ? 'Плеер недоступен' : !hasMedia ? 'Компьютер подключён' : m.status === 'playing' ? 'Сейчас играет' : 'На паузе';
    $('empty-message').textContent = !online ? 'Компьютер недоступен · подключимся автоматически' : state?.error || (state?.selectedSource ? 'Выбранный плеер закрыт · можно выбрать другой источник' : 'Начните воспроизведение на компьютере');
    $('connection-name').textContent = online ? state.computer : '';
    $('volume-open').disabled = !online || state.volume == null;
    $('volume-value').textContent = online && state.volume != null ? Math.round(state.volume * 100) + '%' : '—';
    if (panel === 'volume') syncVolumePanel();
    if (!hasMedia) return;
    $('title').textContent = m.title || 'Без названия'; $('artist').textContent = m.artist || m.source;
    $('previous').disabled = !m.controls.previous; $('next').disabled = !m.controls.next;
    $('play').disabled = !(m.controls.toggle || (m.status === 'playing' ? m.controls.pause : m.controls.play));
    $('play').setAttribute('aria-label', m.status === 'playing' ? 'Пауза' : 'Воспроизвести');
    $('play-icon').innerHTML = m.status === 'playing' ? '<path d="M8 5v14M16 5v14"/>' : '<path d="m8 5 11 7-11 7Z"/>';
    $('seek').disabled = !m.controls.seek; $('duration').textContent = m.duration > 0 ? fmt(m.duration) : '—';
    progress();
  }
  function progress() {
    if (!online || !state?.media.sessionId || dragSeek) return;
    const m = state.media; const elapsed = m.status === 'playing' ? (performance.now() - received) / 1000 * m.rate : 0;
    const position = m.duration > 0 ? Math.min(m.duration, m.position + elapsed) : m.position + elapsed;
    $('position').textContent = fmt(position);
    const fraction = m.duration > 0 ? position / m.duration : 0;
    $('seek').value = Math.round(fraction * 1000); $('seek').style.setProperty('--progress', fraction * 100 + '%');
  }
  function armVolumePanel() { clearTimeout(panelTimer); panelTimer = setTimeout(closePanel, 15000); }
  function mixerKey() { return (state?.mixer || []).map(app => app.id).join('\u0000'); }
  function syncVolumePanel() {
    if (mixerSignature !== mixerKey() && volumeDrag === null) { buildVolumePanel($('overlay-body')); return; }
    if ($('volume-slider') && volumeDrag !== 'master' && state?.volume != null) {
      const value = Math.round(state.volume * 100); $('volume-slider').value = value; $('volume-number').textContent = value + '%';
    }
    for (const row of document.querySelectorAll('.mixer-item')) {
      const app = (state?.mixer || []).find(item => item.id === row.dataset.mixerId); if (!app) continue;
      if (volumeDrag !== app.id) { row.querySelector('.mixer-slider').value = Math.round(app.volume * 100); row.querySelector('.mixer-value').textContent = Math.round(app.volume * 100) + '%'; }
      const mute = row.querySelector('.mixer-mute'); mute.textContent = app.muted ? 'Включить' : 'Без звука'; mute.setAttribute('aria-pressed', String(app.muted));
      row.classList.toggle('active', app.active);
    }
  }
  function buildVolumePanel(body) {
    body.replaceChildren(); body.className = 'mixer-panel'; mixerSignature = mixerKey();
    const master = document.createElement('section'); master.className = 'master-volume';
    master.innerHTML = '<div class="volume-number" id="volume-number"></div><input id="volume-slider" class="slider" type="range" min="0" max="100" aria-label="Общая громкость компьютера"><small>Общая громкость Windows</small>';
    body.append(master);
    const value = Math.round((state?.volume ?? 0) * 100); $('volume-slider').value = value; $('volume-number').textContent = value + '%';
    $('volume-slider').addEventListener('input', event => { volumeDrag = 'master'; $('volume-number').textContent = event.target.value + '%'; armVolumePanel(); });
    $('volume-slider').addEventListener('change', event => { command('volume', { value: Number(event.target.value) / 100 }); volumeDrag = null; armVolumePanel(); });
    const heading = document.createElement('div'); heading.className = 'mixer-heading'; heading.textContent = 'Приложения'; body.append(heading);
    const list = document.createElement('div'); list.className = 'mixer-list'; body.append(list);
    const apps = state?.mixer || [];
    if (!apps.length) { const empty = document.createElement('small'); empty.textContent = 'Запустите звук в приложении — оно появится здесь.'; list.append(empty); }
    for (const app of apps) {
      const row = document.createElement('section'); row.className = 'mixer-item' + (app.active ? ' active' : ''); row.dataset.mixerId = app.id;
      const top = document.createElement('div'); top.className = 'mixer-top';
      const name = document.createElement('span'); name.className = 'mixer-name'; name.textContent = app.name;
      const percent = document.createElement('span'); percent.className = 'mixer-value'; percent.textContent = Math.round(app.volume * 100) + '%';
      const mute = document.createElement('button'); mute.className = 'mixer-mute'; mute.textContent = app.muted ? 'Включить' : 'Без звука'; mute.setAttribute('aria-pressed', String(app.muted));
      mute.addEventListener('click', () => { const current = (state?.mixer || []).find(item => item.id === app.id); command('mixer-mute', { mixerId: app.id, muted: !(current?.muted ?? app.muted) }); armVolumePanel(); });
      top.append(name, percent, mute);
      const slider = document.createElement('input'); slider.className = 'slider mixer-slider'; slider.type = 'range'; slider.min = '0'; slider.max = '100'; slider.value = String(Math.round(app.volume * 100)); slider.setAttribute('aria-label', 'Громкость ' + app.name);
      slider.addEventListener('input', event => { volumeDrag = app.id; percent.textContent = event.target.value + '%'; armVolumePanel(); });
      slider.addEventListener('change', event => { command('mixer-volume', { mixerId: app.id, value: Number(event.target.value) / 100 }); volumeDrag = null; armVolumePanel(); });
      row.append(top, slider); list.append(row);
    }
    armVolumePanel();
  }
  function openPanel(kind) {
    closePanel(); panel = kind; $('overlay').hidden = false; const body = $('overlay-body'); body.className = '';
    const titles = { volume: 'Микшер громкости', sources: 'Источник музыки', settings: 'Настройки', title: 'Сейчас играет' }; $('overlay-title').textContent = titles[kind];
    if (kind === 'volume') {
      buildVolumePanel(body);
    } else if (kind === 'sources') {
      body.replaceChildren();
      for (const source of [{ id: null, name: 'Автоматически · текущая сессия Windows' }, ...state.sources]) {
        const button = document.createElement('button'); button.className = 'row'; button.textContent = source.name + (source.id === state.selectedSource ? ' ✓' : '');
        button.addEventListener('click', () => { command('source', { sourceId: source.id }); closePanel(); }); body.append(button);
      }
    } else if (kind === 'settings') {
      body.innerHTML = '<div class="settings-grid"><label>Яркость · <span id="brightness-value"></span><input id="brightness" class="slider" type="range" min="5" max="100" aria-label="Яркость экрана"></label><label>USB-C<select id="usb-side"><option value="left">Слева</option><option value="right">Справа</option></select></label><label>Гасить при простое<select id="idle-delay"><option value="0">Никогда</option><option value="60">Через 1 минуту</option><option value="180">Через 3 минуты</option><option value="300">Через 5 минут</option></select></label></div><button id="blank-now" class="row">Чёрный экран сейчас</button><button id="fullscreen" class="row">Полный экран</button><button id="forget" class="row">Отвязать этот пульт</button><small>Pixel Companion 0.2 · экран остаётся включённым, пока работает приложение. При блокировке ПК показывается чёрный экран.</small>';
      $('brightness').value = config.brightness; $('brightness-value').textContent = config.brightness + '%'; $('usb-side').value = config.usbLeft ? 'left' : 'right'; $('idle-delay').value = String(config.idle);
      $('brightness').addEventListener('input', e => { config.brightness = Number(e.target.value); localStorage.setItem('brightness', config.brightness); $('brightness-value').textContent = config.brightness + '%'; native(sleeping); });
      $('usb-side').addEventListener('change', e => { config.usbLeft = e.target.value === 'left'; localStorage.setItem('usb-side', e.target.value); applyOrientation(); });
      $('idle-delay').addEventListener('change', e => { config.idle = Number(e.target.value); localStorage.setItem(IDLE_KEY, String(config.idle)); idleSince = performance.now(); if (config.idle === 0 && !state?.locked && !manualBlank) setSleeping(false); });
      $('blank-now').addEventListener('click', () => { wakeUntil = 0; manualBlank = true; setSleeping(true); });
      $('fullscreen').addEventListener('click', async () => { try { await document.documentElement.requestFullscreen(); closePanel(); } catch (_) { toast('Откройте Android-приложение для полного экрана'); } });
      $('forget').addEventListener('click', showPair);
    } else if (kind === 'title') {
      body.replaceChildren(); const title = document.createElement('h1'), artist = document.createElement('p'); title.textContent = state?.media.title || 'Без названия'; artist.textContent = state?.media.artist || ''; body.append(title, artist); panelTimer = setTimeout(closePanel, 6000);
    }
  }
  $('pair-form').addEventListener('submit', async e => {
    e.preventDefault(); $('pair-error').textContent = ''; $('pair-button').disabled = true;
    try {
      const response = await fetch('/api/pair', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: $('pair-code').value }), signal: AbortSignal.timeout(6000) });
      const data = await response.json(); if (!response.ok || typeof data.token !== 'string') throw new Error(data.error || 'Не удалось подключиться');
      token = data.token; localStorage.setItem(TOKEN_KEY, token); $('pair-code').value = ''; $('pairing').hidden = true; connect();
    } catch (error) { $('pair-error').textContent = error.message || 'Компьютер недоступен'; }
    finally { $('pair-button').disabled = false; }
  });
  $('play').addEventListener('click', () => { const m = state.media; command(m.controls.toggle ? 'toggle' : m.status === 'playing' ? 'pause' : 'play'); });
  $('next').addEventListener('click', () => command('next')); $('previous').addEventListener('click', () => command('previous'));
  $('seek').addEventListener('input', e => { dragSeek = true; $('position').textContent = fmt(Number(e.target.value) / 1000 * state.media.duration); $('seek').style.setProperty('--progress', Number(e.target.value) / 10 + '%'); });
  $('seek').addEventListener('change', e => { command('seek', { value: Number(e.target.value) / 1000 * state.media.duration }); dragSeek = false; });
  $('source').addEventListener('click', () => openPanel('sources')); $('volume-open').addEventListener('click', () => openPanel('volume')); $('settings').addEventListener('click', () => openPanel('settings')); $('title').addEventListener('click', () => openPanel('title')); $('close').addEventListener('click', closePanel);
  $('wake').addEventListener('click', e => { e.stopPropagation(); manualBlank = false; lastTouch = performance.now(); idleSince = lastTouch; wakeUntil = lastTouch + 15000; setSleeping(false); });
  document.addEventListener('pointerdown', () => { lastTouch = performance.now(); idleSince = lastTouch; }, { passive: true });
  document.addEventListener('keydown', e => { lastTouch = performance.now(); if (e.key === 'Escape') closePanel(); });
  window.addEventListener('online', () => { if (token && !online) connect(); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden && token && (!ws || ws.readyState !== WebSocket.OPEN)) connect(); });
  setInterval(() => {
    const now = performance.now(); const time = new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }); $('clock').textContent = time; $('empty-clock').textContent = time;
    if (online && now - lastMessage > 6000) { offline(); ws.close(); }
    if (token && now > wakeUntil && ((state?.locked && online) || config.idle > 0 && (state?.media.status !== 'playing' || !online) && now - idleSince > config.idle * 1000)) setSleeping(true);
    $('app').classList.toggle('dim', now - lastTouch > 30000); native(sleeping); progress();
  }, 500);
  setInterval(() => { const offsets = [-3, -1, 1, 3], dpr = Math.max(1, window.devicePixelRatio || 1); const i = Math.floor(Date.now() / 60000); $('app').style.transform = `translate(${offsets[i % 4] / dpr}px,${offsets[Math.floor(i / 4) % 4] / dpr}px)`; }, 60000);
  applyOrientation(); connect();
})();
