// Espace de travail : disposition des panneaux (largeurs fr) et persistance.
// Le glissement des poignées est entièrement côté client pendant le mouvement ;
// le serveur ne relit l'état qu'au relâchement (CommitSplitAsync).
// Chaque espace (lecture d'un cours, éditeur…) a sa clé ; sans clé, c'est celle de la lecture (historique).
const KEY = 'gpoe26.workspace.v1';
const keyOf = layoutKey => layoutKey ? KEY + '.' + layoutKey : KEY;

export function loadJson(layoutKey) {
  try { return localStorage.getItem(keyOf(layoutKey)); } catch { return null; }
}

export function saveJson(json, layoutKey) {
  try { localStorage.setItem(keyOf(layoutKey), json); } catch { /* quota privé : la disposition ne sera pas gardée */ }
}

export function clear(layoutKey) {
  try { localStorage.removeItem(keyOf(layoutKey)); } catch { /* idem */ }
}

// Raccourcis Alt+lettre des panneaux de l'espace affiché ; Échap quitte l'agrandissement.
// Un seul écouteur global : il vise toujours le dernier espace monté (navigation entre lecture et éditeur).
// Ignorés quand le focus est dans un champ éditable ; seules les lettres déclarées sont interceptées.
export function attachShortcuts(dotNetRef, keys) {
  window.__wsShortcutRef = dotNetRef;
  window.__wsShortcutKeys = (keys || []).map(k => k.toLowerCase());
  if (window.__wsShortcuts) return;
  window.__wsShortcuts = true;
  document.addEventListener('keydown', e => {
    const ref = window.__wsShortcutRef;
    if (!ref) return;
    const t = e.target;
    if (t && t.closest && t.closest('input, textarea, select, [contenteditable="true"]')) return;
    const k = (e.key || '').toLowerCase();
    if (e.altKey && !e.ctrlKey && !e.metaKey && window.__wsShortcutKeys.includes(k)) {
      e.preventDefault();
      ref.invokeMethodAsync('ShortcutKeyAsync', k).catch(() => {});
    } else if (k === 'escape') {
      ref.invokeMethodAsync('ShortcutEscAsync').catch(() => {});
    }
  });
}

const MIN_PX = 160;
const SPLITTER_PX = 6;
const COLLAPSED_PX = 52;

// Pose (ou repose) les gestionnaires de glissement sur les poignées d'un conteneur. Idempotent.
export function attachSplitters(container, dotNetRef) {
  if (!container) return;
  container.querySelectorAll('.ws-splitter').forEach(el => {
    if (el.__wsAttached) return;
    el.__wsAttached = true;

    el.addEventListener('pointerdown', event => startDrag(container, el, event));
    el.addEventListener('dblclick', () => {
      const gap = Number(el.dataset.gap);
      dotNetRef.invokeMethodAsync('ResetSplitAsync', gap);
    });
  });
  container.__wsRef = dotNetRef;
}

function visiblePanels(container) {
  // Les panneaux masqués (mode agrandissement) ne participent ni à la grille ni au glissement
  return [...container.querySelectorAll(':scope > .ws-panel')]
    .filter(p => !p.classList.contains('ws-hidden'));
}

function templateFromWidths(container) {
  const parts = [];
  const panels = visiblePanels(container);
  panels.forEach((p, i) => {
    // Poignée seulement quand les deux voisins sont dépliés : le nombre de pistes égale le nombre d'enfants
    if (i > 0 && !panels[i - 1].classList.contains('collapsed') && !p.classList.contains('collapsed'))
      parts.push(`${SPLITTER_PX}px`);
    parts.push(p.classList.contains('collapsed') ? `${COLLAPSED_PX}px` : `${p.__px}px`);
  });
  return parts.join(' ');
}

function commit(container) {
  const expanded = visiblePanels(container)
    .filter(p => !p.classList.contains('collapsed'))
    .map(p => p.__px);
  const total = container.getBoundingClientRect().width;
  const ref = container.__wsRef;
  if (ref && expanded.length > 0) {
    ref.invokeMethodAsync('CommitSplitAsync', expanded, total);
  } else {
    container.style.gridTemplateColumns = templateFromWidths(container);
  }
}

function startDrag(container, splitter, event) {
  event.preventDefault();
  const panels = visiblePanels(container);
  const gap = Number(splitter.dataset.gap);
  const left = panels[gap];
  const right = panels[gap + 1];
  if (!left || !right) return;
  if (left.classList.contains('collapsed') || right.classList.contains('collapsed')) return;

  const startX = event.clientX;
  const startLeft = left.getBoundingClientRect().width;
  const startRight = right.getBoundingClientRect().width;

  panels.forEach(p => { p.__px = p.getBoundingClientRect().width; });
  container.style.gridTemplateColumns = templateFromWidths(container);

  splitter.setPointerCapture(event.pointerId);

  const onMove = e => {
    const dx = e.clientX - startX;
    const pair = Math.max(MIN_PX * 2, startLeft + startRight);
    const newLeft = Math.min(Math.max(MIN_PX, startLeft + dx), pair - MIN_PX);
    left.__px = newLeft;
    right.__px = pair - newLeft;
    container.style.gridTemplateColumns = templateFromWidths(container);
  };
  const onUp = () => {
    splitter.removeEventListener('pointermove', onMove);
    splitter.removeEventListener('pointerup', onUp);
    splitter.removeEventListener('pointercancel', onUp);
    commit(container);
  };
  splitter.addEventListener('pointermove', onMove);
  splitter.addEventListener('pointerup', onUp);
  splitter.addEventListener('pointercancel', onUp);
}

// ── Barre latérale de l'application : largeur réglable, repli en rail ─────────────────────────────
const SIDE_KEY = 'gpoe26.sidebar.v1';

export function loadSidebar() {
  try {
    const raw = localStorage.getItem(SIDE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

export function saveSidebar(pref) {
  try { localStorage.setItem(SIDE_KEY, JSON.stringify(pref)); } catch { /* navigation privée */ }
}

// Le glissement ne passe pas par le circuit Blazor : la largeur suit la souris en CSS,
// et le serveur ne reçoit que la valeur finale au relâchement.
export function attachSideResizer(handle, dotNetRef, compactPx, minPx, maxPx) {
  if (!handle || handle.__gaAttached) return;
  handle.__gaAttached = true;
  const shell = handle.closest('.ga-shell');
  const sidebar = shell && shell.querySelector('.ga-sidebar');
  if (!sidebar) return;

  handle.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    event.preventDefault();
    const startX = event.clientX;
    const startW = sidebar.getBoundingClientRect().width;
    let width = startW;
    const startVar = shell.style.getPropertyValue('--ga-side-w');
    handle.setPointerCapture(event.pointerId);
    handle.classList.add('dragging');
    document.body.classList.add('ga-resizing');

    const onMove = e => {
      width = Math.min(maxPx, Math.max(compactPx, startW + e.clientX - startX));
      // Sous le minimum, aperçu du rail : on déplie la barre pendant le glissement pour voir la largeur réelle
      shell.classList.toggle('side-compact', width < 140);
      shell.style.setProperty('--ga-side-w', Math.max(minPx, width) + 'px');
    };
    const onUp = () => {
      handle.removeEventListener('pointermove', onMove);
      handle.removeEventListener('pointerup', onUp);
      handle.removeEventListener('pointercancel', onUp);
      handle.classList.remove('dragging');
      document.body.classList.remove('ga-resizing');
      // Repli en rail : la largeur dépliée reste celle d'avant le glissement
      if (width < 140) shell.style.setProperty('--ga-side-w', startVar);
      else shell.style.setProperty('--ga-side-w', Math.min(maxPx, Math.max(minPx, Math.round(width))) + 'px');
      dotNetRef.invokeMethodAsync('CommitSidebarAsync', width).catch(() => {});
    };
    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onUp);
    handle.addEventListener('pointercancel', onUp);
  });
}

// ── Petites préférences d'affichage (ex. barre d'outils de l'administration réduite) ─────────────
export function loadPref(name) {
  try { return localStorage.getItem('gpoe26.pref.' + name); } catch { return null; }
}

export function savePref(name, value) {
  try { localStorage.setItem('gpoe26.pref.' + name, value); } catch { /* navigation privée */ }
}
