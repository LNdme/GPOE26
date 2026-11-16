// Petits services navigateur de l'espace d'apprentissage (module ES chargé à la demande).
// KaTeX et Prism sont fournis par l'hôte (GPOE26.Web) : s'ils manquent, le texte reste lisible tel quel.

// Dernière action de l'élève sur la page (défilement, clic, frappe, toucher) : le temps d'étude ne compte que s'il est là.
let lastActivity = Date.now();
for (const type of ['scroll', 'keydown', 'pointerdown', 'pointermove', 'touchstart', 'wheel'])
    window.addEventListener(type, () => { lastActivity = Date.now(); }, { passive: true, capture: true });

/** Page visible et action de l'élève depuis moins de `ms` millisecondes. */
export function activeWithin(ms) {
    return document.visibilityState === 'visible' && Date.now() - lastActivity <= ms;
}

/** Rend les formules produites par Markdig (.math : \( … \) ou \[ … \]) et colore le code. */
/**
 * Rend les formules KaTeX et le code. `element` : nœud DOM (ElementReference) ou sélecteur CSS.
 * Le fragment peut ne pas exister au premier appel (espace de travail) : on réessaie quelques fois, sans jamais lever d'erreur.
 */
export function renderMath(element) {
    let tries = 20;
    const resolve = () => (typeof element === 'string' ? document.querySelector(element) : element);
    const run = () => {
        const el = resolve();
        if (!el || typeof el.querySelectorAll !== 'function') {
            if (tries-- > 0) setTimeout(run, 100);
            return;
        }
        if (!window.katex) { if (tries > 0) setTimeout(run, 150); return; }
        el.querySelectorAll('.math:not([data-rendered])').forEach(m => {
            let tex = m.textContent.trim();
            if ((tex.startsWith('\\(') && tex.endsWith('\\)')) || (tex.startsWith('\\[') && tex.endsWith('\\]')))
                tex = tex.substring(2, tex.length - 2);
            try {
                window.katex.render(tex, m, { displayMode: m.tagName === 'DIV', throwOnError: false });
                m.setAttribute('data-rendered', '1');
            } catch { /* formule laissée en texte */ }
        });
    };
    run();
    const root = resolve();
    if (root && typeof root.querySelector === 'function' && root.querySelector('code.language-simulation'))
        import('./gpoe-labs.js').then(labs => labs.mountLabs(root));
    if (window.Prism && root && typeof root.querySelectorAll === 'function') window.Prism.highlightAllUnder(root);
}

/** Entoure la sélection d'un champ de texte (barre d'outils Markdown) et renvoie le nouveau contenu. */
export function insertAtCursor(id, before, after) {
    const el = document.getElementById(id);
    if (!el) return '';
    const { selectionStart: start, selectionEnd: end, value } = el;
    const selected = value.substring(start, end);
    el.value = value.substring(0, start) + before + selected + after + value.substring(end);
    el.focus();
    el.selectionStart = start + before.length;
    el.selectionEnd = start + before.length + selected.length;
    return el.value;
}

/** Texte sélectionné dans un champ. */
export function selectionText(id) {
    const el = document.getElementById(id);
    return el ? el.value.substring(el.selectionStart, el.selectionEnd) : '';
}

/** Remplace la sélection d'un champ et renvoie le nouveau contenu. */
export function replaceSelection(id, text) {
    const el = document.getElementById(id);
    if (!el) return '';
    el.value = el.value.substring(0, el.selectionStart) + text + el.value.substring(el.selectionEnd);
    return el.value;
}

/** Fait défiler une zone jusqu'en bas (dernier message). */
export function scrollToEnd(element) {
    if (element) element.scrollTop = element.scrollHeight;
}

/** Centre un élément (message épinglé ouvert depuis la liste des explications). */
export function reveal(id) {
    const el = document.getElementById(id);
    if (el) { el.scrollIntoView({ block: 'center' }); el.classList.add('flash'); }
}
