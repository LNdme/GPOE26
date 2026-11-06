// Petits services navigateur de l'espace d'apprentissage (module ES chargé à la demande).
// KaTeX et Prism sont fournis par l'hôte (GPOE26.Web) : s'ils manquent, le texte reste lisible tel quel.

/** Rend les formules produites par Markdig (.math : \( … \) ou \[ … \]) et colore le code. */
export function renderMath(element) {
    if (!element) return;
    const run = (tries) => {
        if (!window.katex) { if (tries > 0) setTimeout(() => run(tries - 1), 150); return; }
        element.querySelectorAll('.math:not([data-rendered])').forEach(el => {
            let tex = el.textContent.trim();
            if ((tex.startsWith('\\(') && tex.endsWith('\\)')) || (tex.startsWith('\\[') && tex.endsWith('\\]')))
                tex = tex.substring(2, tex.length - 2);
            try {
                window.katex.render(tex, el, { displayMode: el.tagName === 'DIV', throwOnError: false });
                el.setAttribute('data-rendered', '1');
            } catch { /* formule laissée en texte */ }
        });
    };
    run(20);
    if (window.Prism) window.Prism.highlightAllUnder(element);
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
