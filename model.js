(() => {
  const rarity = { '红': 0, '金': 1, '紫': 2, '蓝': 3 };
  function candidates(data, id) {
    const group = data.classes.find(group => group.id === id);
    if (!group) throw new Error('Unknown sound class.');
    const ids = new Set(group.itemIds);
    return data.items.filter(item => ids.has(item.id)).sort((a, b) => a.cells - b.cells || rarity[a.rarity] - rarity[b.rarity] || a.name.localeCompare(b.name, 'zh'));
  }
  function sections(items) {
    const groups = new Map();
    for (const item of items) { if (!groups.has(item.cells)) groups.set(item.cells, []); groups.get(item.cells).push(item); }
    return [...groups].map(([cells, items]) => ({ cells, items, red: items.filter(item => item.red).length, probability: Math.round(100 * items.filter(item => item.red).length / items.length) }));
  }
  function create(data) {
    const state = { groupId: data.initial, mode: 'listening', phase: 'candidates', playing: null, font: 'medium', pinned: true, visible: true, auto: false, history: [], difference: '1.8', selectedItem: null };
    function dispatch(action) {
      switch (action.type) {
        case 'recognize':
          candidates(data, action.id); state.groupId = action.id; state.phase = 'analyzing'; state.mode = 'listening'; state.playing = null; state.visible = true; state.selectedItem = null; break;
        case 'result':
          state.phase = 'candidates';
          state.history = [{ id: state.groupId, at: action.at || '刚刚', difference: state.difference }, ...state.history.filter(entry => entry.id !== state.groupId)].slice(0, 6); break;
        case 'play':
          state.mode = 'paused'; state.phase = 'replay'; state.playing = action.item || '本次录音'; state.selectedItem = action.item || null; break;
        case 'stop': state.playing = null; state.phase = 'candidates'; break;
        case 'listen': state.mode = 'listening'; state.phase = 'candidates'; state.playing = null; state.selectedItem = null; break;
        case 'pause': state.mode = 'paused'; state.playing = null; state.phase = 'candidates'; break;
        case 'font': if (!['small', 'medium', 'large'].includes(action.value)) throw new Error('Invalid font size.'); state.font = action.value; break;
        case 'pin': state.pinned = !state.pinned; break;
        case 'visibility': state.visible = action.value; break;
        case 'auto': state.auto = action.value; break;
        default: throw new Error('Unknown demonstration action.');
      }
      return state;
    }
    return { state, dispatch, items: () => candidates(data, state.groupId), sections: () => sections(candidates(data, state.groupId)) };
  }
  globalThis.DemoModel = { create, candidates, sections };
})();
