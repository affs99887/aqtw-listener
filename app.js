(() => {
  'use strict';
  const data = globalThis.SHIDIAN_DATA;
  const model = globalThis.DemoModel.create(data);
  const $ = id => document.getElementById(id);
  const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
  const itemsById = new Map(data.items.map(item => [item.id, item]));
  const classesById = new Map(data.classes.map(group => [group.id, group]));
  const classItems = id => globalThis.DemoModel.candidates(data, id);
  const motion = matchMedia('(prefers-reduced-motion: reduce)');
  const timers = new Set();
  let replayTimer = null, recognizeTimer = null, toastTimer = null, historyTimer = null;
  let generation = 0, autoIndex = 0, historySelected = data.initial, probabilityCell = 4;
  const counts = new Map(data.featured.map(id => [id, classesById.get(id).count]));
  const showcase = data.featured.map(id => {
    const group = classesById.get(id);
    return { id, label: group.count === 1 ? '单件候选' : group.count === 6 ? '六件候选' : group.count > 30 ? '密集候选' : '多件候选' };
  });
  const largest = data.classes[0];
  model.state.history = data.featured.slice(0, 3).map((id, i) => ({ id, at: ['刚刚', '12 秒前', '28 秒前'][i], difference: ['1.8', '1.2', '2.1'][i] }));
  function schedule(delay, callback) {
    const id = setTimeout(() => { timers.delete(id); if (model.state.auto && !document.hidden) callback(); }, delay);
    timers.add(id);
  }
  function clearAuto() { for (const id of timers) clearTimeout(id); timers.clear(); }
  function clearReplay() { clearTimeout(replayTimer); replayTimer = null; }
  function manual() {
    clearAuto(); model.dispatch({ type: 'auto', value: false }); renderAuto();
  }
  function toast(message) {
    clearTimeout(toastTimer); $('demo-toast').textContent = message; $('demo-toast').hidden = false;
    toastTimer = setTimeout(() => { $('demo-toast').hidden = true; }, 2900);
  }
  function imageMarkup(item) {
    const picture = `<img src="${escape(item.image)}" alt="${escape(item.name)}" loading="lazy" decoding="async">`;
    if (!item.background) return `<div class="candidate-image">${picture}</div>`;
    const factor = 66 / Math.max(item.width, item.height);
    return `<div class="candidate-image has-footprint"><span class="footprint" style="--foot-w:${item.width * factor}px;--foot-h:${item.height * factor}px;--rarity-bg:${escape(item.background)}">${picture}</span></div>`;
  }
  function renderCandidates() {
    const list = model.items();
    const columns = list.length > 20 ? 4 : list.length > 6 ? 3 : 2;
    const window = $('float-window');
    window.style.setProperty('--columns', columns);
    window.style.setProperty('--window-width', `${columns === 4 ? 850 : columns === 3 ? 755 : 592}px`);
    window.classList.toggle('dense', list.length > 20);
    $('candidate-content').innerHTML = model.sections().map(group => `<section class="candidate-section" aria-label="${group.cells}格，${group.items.length}件候选，大红候选占比${group.probability}%">
      <div class="size-heading"><strong>${group.cells} 格</strong><span class="amount">${group.items.length} 件</span><div class="probability-description">大红概率 ${group.red}/${group.items.length}</div><div class="probability-value ${group.red ? 'has-red' : ''}">${group.probability}%</div></div>
      <div class="candidate-grid">${group.items.map(item => `<article class="candidate-card ${model.state.selectedItem === item.name && model.state.playing ? 'playing' : ''}" data-item-id="${escape(item.id)}">
        ${imageMarkup(item)}<div class="candidate-details"><strong class="candidate-name ${item.red ? 'red' : ''}">${escape(item.name)}</strong><div class="candidate-meta"><span>差异率 ${model.state.difference}%</span><span>${item.grid}</span></div></div><button class="candidate-play" data-action="preview" data-item="${escape(item.id)}" aria-label="模拟试听${escape(item.name)}" title="模拟试听${escape(item.name)}">▷</button>
      </article>`).join('')}</div></section>`).join('');
    $('candidate-count').textContent = `${list.length} 件候选`;
    $('sound-class').value = model.state.groupId;
    for (const button of $('demo-scenes').querySelectorAll('button')) button.setAttribute('aria-pressed', String(button.dataset.class === model.state.groupId));
    for (const button of $('cargo-grid').querySelectorAll('button')) button.classList.toggle('selected', button.dataset.class === model.state.groupId);
  }
  function setTimeline(phase) {
    for (const step of document.querySelectorAll('[data-step]')) step.classList.toggle('active', step.dataset.step === phase);
  }
  function renderAuto() {
    $('auto-button').setAttribute('aria-pressed', String(model.state.auto));
    $('auto-icon').textContent = model.state.auto ? 'Ⅱ' : '▷';
    $('auto-label').textContent = model.state.auto ? '暂停自动演示' : '继续自动演示';
  }
  function renderHistory() {
    $('history-list').innerHTML = model.state.history.slice(0, 3).map(entry => {
      const group = classesById.get(entry.id);
      const cells = [...new Set(classItems(entry.id).map(item => item.cells))].sort((a, b) => a - b).join(' / ');
      return `<button class="history-row ${historySelected === entry.id ? 'selected' : ''}" data-history="${escape(entry.id)}" aria-label="模拟回放${escape(group.name)}"><span class="history-time">${escape(entry.at)}</span><span class="history-items">${escape(group.name)}<small>${cells} 格 · 差异率 ${entry.difference}%</small></span><span aria-hidden="true">▷</span></button>`;
    }).join('');
    $('recordings-menu').innerHTML = model.state.history.map((entry, i) => `<button data-action="recording" data-class="${escape(entry.id)}">${i === 0 ? '最新录音' : '历史录音'} · ${escape(classesById.get(entry.id).name)}</button>`).join('');
  }
  function render() {
    const state = model.state;
    const window = $('float-window');
    window.hidden = !state.visible; $('restore-window').hidden = state.visible;
    window.dataset.font = state.font; window.dataset.phase = state.phase;
    window.classList.toggle('paused', state.mode === 'paused');
    $('mode-label').textContent = state.mode === 'listening' ? '监听中' : '已暂停';
    $('mode-button').textContent = state.mode === 'listening' ? '暂停监听' : '返回监听';
    $('stop-button').disabled = !state.playing;
    document.querySelector('[data-action=pin]').setAttribute('aria-pressed', String(state.pinned));
    document.querySelector('[data-action=pin]').setAttribute('aria-label', state.pinned ? '模拟解锁浮窗' : '模拟固定浮窗');
    for (const button of document.querySelectorAll('[data-action=font]')) button.setAttribute('aria-pressed', String(button.dataset.value === state.font));
    $('analyzing-band').hidden = state.phase !== 'analyzing';
    $('replay-band').hidden = !state.playing;
    $('replay-name').textContent = state.playing || '本次录音';
    $('freshness').textContent = state.phase === 'analyzing' ? '分析中' : '刚刚';
    $('game-caption').textContent = state.playing ? '试听时，行商助手先暂停监听。返回后继续等待下一件。' : state.mode === 'paused' ? '采音已暂停。点击「返回监听」，继续看下一件候选。' : '声音是线索。格数，让候选更清楚。';
    renderCandidates(); renderHistory(); renderAuto(); setTimeline(state.phase);
  }
  function pulseResult() {
    const row = document.querySelector('.summary-row');
    row.classList.remove('just-arrived'); void row.offsetWidth; row.classList.add('just-arrived');
  }
  function recognize(id, user = false) {
    if (user) manual();
    generation++; const active = generation;
    clearTimeout(recognizeTimer); clearReplay();
    model.dispatch({ type: 'recognize', id }); render();
    recognizeTimer = setTimeout(() => {
      if (active !== generation) return;
      model.dispatch({ type: 'result', at: '刚刚' }); render(); pulseResult();
    }, motion.matches ? 120 : 650);
  }
  function play(item = null, user = false) {
    if (user) manual();
    generation++; clearTimeout(recognizeTimer); clearReplay();
    model.dispatch({ type: 'play', item }); render();
    const progress = document.querySelector('.replay-track i'); progress.style.animation = 'none'; void progress.offsetWidth; progress.style.animation = '';
    if (user) toast('模拟试听中 · 监听已暂停');
    replayTimer = setTimeout(() => { model.dispatch({ type: 'stop' }); render(); }, 4000);
  }
  function listen(user = false) {
    if (user) manual();
    generation++; clearTimeout(recognizeTimer); clearReplay();
    model.dispatch({ type: 'listen' }); render(); setTimeline('listening');
  }
  function runCycle() {
    clearAuto();
    if (!model.state.auto || document.hidden) return;
    const id = data.featured[autoIndex % data.featured.length];
    listen();
    schedule(1100, () => recognize(id));
    schedule(4950, () => { const item = model.items().find(item => item.red) || model.items()[0]; play(item.name); });
    schedule(9500, () => listen());
    schedule(12200, () => { autoIndex++; runCycle(); });
  }
  function setAuto(value) {
    clearAuto(); model.dispatch({ type: 'auto', value }); renderAuto();
    if (value) { autoIndex = Math.max(0, data.featured.indexOf(model.state.groupId)); runCycle(); }
  }
  function renderProbability() {
    const groups = globalThis.DemoModel.sections(classItems(largest.id));
    const available = groups.filter(group => [2, 4, 6].includes(group.cells));
    if (!available.some(group => group.cells === probabilityCell)) probabilityCell = available[0].cells;
    $('grid-filter').innerHTML = available.map(group => `<button data-cell="${group.cells}" aria-pressed="${group.cells === probabilityCell}">${group.cells} 格</button>`).join('');
    const group = available.find(group => group.cells === probabilityCell);
    $('probability-content').innerHTML = `<div class="probability-body"><div class="probability-stat"><strong>${group.probability}%</strong><span>${group.red}/${group.items.length} 件候选为大红</span></div><div class="probability-items">${group.items.map(item => `<button class="probability-item" data-probability="${escape(item.id)}" aria-label="查看${escape(item.name)}的格数"><img src="${escape(item.image)}" alt="" loading="lazy"><span class="${item.red ? 'red' : ''}">${escape(item.name)}<small>${item.grid} · ${item.rarity}</small></span></button>`).join('')}</div></div>`;
  }
  function historyPlay(id) {
    manual(); historySelected = id; clearTimeout(historyTimer);
    $('history-title').textContent = classesById.get(id).name;
    $('history-description').textContent = '模拟回放中 · 录音与候选一起保留';
    $('history-play').textContent = 'Ⅱ'; $('history-player').classList.add('playing'); renderHistory();
    historyTimer = setTimeout(() => { $('history-player').classList.remove('playing'); $('history-play').textContent = '▷'; $('history-description').textContent = '回放结束 · 可以重新比较候选'; }, 3400);
  }
  $('sound-class').innerHTML = data.classes.map(group => `<option value="${escape(group.id)}">${escape(group.name)}</option>`).join('');
  $('demo-scenes').innerHTML = showcase.map(scene => `<button data-action="scene" data-class="${escape(scene.id)}" aria-pressed="false">${scene.label} · ${counts.get(scene.id)}</button>`).join('');
  const cargo = data.featured.map((id, index) => ({ groupId: id, item: classItems(id).find(item => item.red) || classItems(id)[0], index }));
  cargo.push({ groupId: data.featured[0], item: classItems(data.featured[0])[1] || classItems(data.featured[0])[0], index: 4 });
  cargo.push({ groupId: largest.id, item: classItems(largest.id).find(item => item.name !== cargo[3]?.item.name && item.red) || classItems(largest.id)[1], index: 5 });
  $('cargo-grid').innerHTML = cargo.map(entry => `<button class="cargo-tile" data-action="cargo" data-class="${escape(entry.groupId)}" aria-label="模拟拿起${escape(entry.item.name)}，查看${classesById.get(entry.groupId).count}件候选"><img src="${escape(entry.item.image)}" alt=""><span class="cargo-label"><strong>${escape(entry.item.name)}</strong>${classesById.get(entry.groupId).count} 件候选</span><span class="cargo-number">0${entry.index + 1}</span></button>`).join('');
  $('sound-class').addEventListener('change', event => recognize(event.target.value, true));
  $('auto-button').addEventListener('click', () => setAuto(!model.state.auto));
  $('reset-button').addEventListener('click', () => { manual(); autoIndex = 0; model.dispatch({ type: 'visibility', value: true }); recognize(data.initial); toast('从第一件货物重新开始'); });
  $('try-replay').addEventListener('click', () => { play(null, true); });
  $('history-play').addEventListener('click', () => {
    if ($('history-player').classList.contains('playing')) { clearTimeout(historyTimer); $('history-player').classList.remove('playing'); $('history-play').textContent = '▷'; $('history-description').textContent = '回放已暂停'; }
    else historyPlay(historySelected);
  });
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-action]');
    if (button) {
      switch (button.dataset.action) {
        case 'cargo': case 'scene': recognize(button.dataset.class, true); break;
        case 'preview': play(itemsById.get(button.dataset.item).name, true); break;
        case 'replay': play(null, true); break;
        case 'stop': manual(); clearReplay(); model.dispatch({ type: 'stop' }); render(); break;
        case 'mode': manual(); clearReplay(); model.dispatch({ type: model.state.mode === 'listening' ? 'pause' : 'listen' }); render(); break;
        case 'save': manual(); toast('保存流程演示 · 本次声音可以保存为 WAV'); break;
        case 'font': manual(); model.dispatch({ type: 'font', value: button.dataset.value }); render(); break;
        case 'pin': manual(); model.dispatch({ type: 'pin' }); render(); toast(model.state.pinned ? '模拟：浮窗已固定' : '模拟：浮窗已解锁'); break;
        case 'hide': manual(); model.dispatch({ type: 'visibility', value: false }); render(); break;
        case 'restore': manual(); model.dispatch({ type: 'visibility', value: true }); render(); break;
        case 'info': manual(); $('info-dialog').showModal(); break;
        case 'recordings': manual(); $('recordings-menu').hidden = !$('recordings-menu').hidden; button.setAttribute('aria-expanded', String(!$('recordings-menu').hidden)); break;
        case 'recording': $('recordings-menu').hidden = true; recognize(button.dataset.class, true); break;
      }
    }
    const filter = event.target.closest('[data-cell]');
    if (filter) { probabilityCell = Number(filter.dataset.cell); renderProbability(); }
    const probability = event.target.closest('[data-probability]');
    if (probability) {
      const item = itemsById.get(probability.dataset.probability);
      document.querySelector('.probability-demo .mini-caption').textContent = `${item.name} · ${item.grid} · 该格数的同音候选之一`;
      for (const tile of document.querySelectorAll('.probability-item')) tile.classList.toggle('selected', tile === probability);
    }
    const history = event.target.closest('[data-history]'); if (history) historyPlay(history.dataset.history);
    if (!event.target.closest('.recordings-menu') && !event.target.closest('[data-action=recordings]')) $('recordings-menu').hidden = true;
  });
  document.addEventListener('keydown', event => { if (event.key === 'Escape') { $('recordings-menu').hidden = true; } });
  document.addEventListener('visibilitychange', () => { if (document.hidden) clearAuto(); else if (model.state.auto) runCycle(); });
  motion.addEventListener('change', event => { if (event.matches) setAuto(false); });
  renderProbability(); render();
  if (!motion.matches) setAuto(true);
  globalThis.SHIDIAN_DEMO = { get state() { return { ...model.state }; }, select: id => recognize(id, true), auto: setAuto };
})();
