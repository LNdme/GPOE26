// ══════════════════════════════════════════════════════════════════════════════
//  Confort de lecture du canvas d'étude
//  ────────────────────────────────────────────────────────────────────────────
//  Volontairement autonome côté navigateur : ce composant tourne en Blazor Server,
//  où chaque aller-retour d'interop passe par le circuit SignalR. Le défilement et
//  le sommaire actif seraient inutilisables s'ils déclenchaient un rendu serveur à
//  chaque pixel — tout ce qui suit le scroll est donc traité entièrement en JS.
// ══════════════════════════════════════════════════════════════════════════════

window.readingInterop = (() => {
    const STORAGE_PREFS = 'gpoe.reading.prefs';
    const STORAGE_POSITION = 'gpoe.reading.position';

    const DEFAULTS = { theme: 'light', size: 'md', width: 'default', font: 'sans' };

    let state = null;

    // ── Préférences ───────────────────────────────────────────────────────────

    function loadPreferences() {
        try {
            const stored = JSON.parse(localStorage.getItem(STORAGE_PREFS) || '{}');
            return { ...DEFAULTS, ...stored };
        } catch {
            return { ...DEFAULTS };
        }
    }

    function applyPreferences(root, prefs) {
        if (!root) return;
        root.setAttribute('data-reading-theme', prefs.theme);
        root.setAttribute('data-reading-size', prefs.size);
        root.setAttribute('data-reading-width', prefs.width);
        root.setAttribute('data-reading-font', prefs.font);
    }

    // ── Position de lecture ───────────────────────────────────────────────────

    function readPositions() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_POSITION) || '{}');
        } catch {
            return {};
        }
    }

    function savePosition(courseId, headingId) {
        if (!courseId || !headingId) return;
        const positions = readPositions();
        positions[courseId] = headingId;

        // Ne pas laisser le stockage grossir indéfiniment : on garde les 30 derniers cours.
        const keys = Object.keys(positions);
        if (keys.length > 30) delete positions[keys[0]];

        localStorage.setItem(STORAGE_POSITION, JSON.stringify(positions));
    }

    // ── Sommaire actif + barre de progression ─────────────────────────────────

    /** Part du texte à partir de laquelle on considère le cours lu. */
    const READ_THRESHOLD = 0.9;

    /**
     * Rythme du signal de présence, en millisecondes.
     *
     * Le serveur crédite l'écart réel entre deux signaux, plafonné à 90 secondes :
     * une minute laisse donc de la marge sans jamais gonfler la durée mesurée.
     */
    const HEARTBEAT_MS = 60_000;

    function refresh() {
        if (!state) return;

        const { scroller, progressBar, headings } = state;

        // Progression : part du texte réellement parcourue.
        const scrollable = scroller.scrollHeight - scroller.clientHeight;
        const ratio = scrollable > 0 ? Math.min(scroller.scrollTop / scrollable, 1) : 0;
        if (progressBar) progressBar.style.width = `${(ratio * 100).toFixed(1)}%`;

        // Franchissement du seuil de lecture : UN SEUL aller-retour vers .NET, jamais
        // un par pixel — en Blazor Server chaque appel traverse le circuit SignalR.
        // Un cours plus court que la fenêtre n'est jamais défilable : il compte comme lu.
        if (!state.readNotified && state.onRead && (ratio >= READ_THRESHOLD || scrollable <= 0)) {
            state.readNotified = true;
            state.onRead.invokeMethodAsync('OnReadingThresholdReached').catch(() => {
                // Circuit fermé entre-temps : sans importance, la page n'existe plus.
            });
        }

        // Titre courant : le dernier passé sous la ligne de lecture, à un tiers de l'écran.
        const line = scroller.getBoundingClientRect().top + scroller.clientHeight / 3;
        let current = headings.length > 0 ? headings[0] : null;

        for (const heading of headings) {
            if (heading.getBoundingClientRect().top <= line) current = heading;
            else break;
        }

        if (current && current.id !== state.activeId) {
            state.activeId = current.id;
            highlight(current.id);
            savePosition(state.courseId, current.id);
        }
    }

    function highlight(id) {
        if (!state) return;
        for (const link of state.tocLinks) {
            link.classList.toggle('is-active', link.dataset.tocTarget === id);
        }
    }

    // ── API exposée à Blazor ──────────────────────────────────────────────────

    return {
        /**
         * Prépare le canvas : applique les préférences, branche le suivi du scroll
         * et restaure la position de lecture précédente.
         */
        init(courseId, root, scroller, progressBar, onRead) {
            this.dispose();
            if (!root || !scroller) return loadPreferences();

            const prefs = loadPreferences();
            applyPreferences(root, prefs);

            state = {
                courseId,
                root,
                scroller,
                progressBar,
                onRead,
                readNotified: false,
                headings: Array.from(scroller.querySelectorAll('.course-canvas h1[id], .course-canvas h2[id], .course-canvas h3[id]')),
                tocLinks: Array.from(root.querySelectorAll('[data-toc-target]')),
                activeId: null,
                onScroll: null,
            };

            // rAF plutôt qu'un handler direct : le scroll émet bien plus souvent que
            // le taux de rafraîchissement, et refresh() lit la géométrie du document.
            let ticking = false;
            state.onScroll = () => {
                if (ticking) return;
                ticking = true;
                requestAnimationFrame(() => {
                    refresh();
                    ticking = false;
                });
            };

            scroller.addEventListener('scroll', state.onScroll, { passive: true });
            window.addEventListener('resize', state.onScroll, { passive: true });

            // Signal de présence : uniquement quand l'onglet est visible. Un onglet
            // laissé ouvert en arrière-plan ne doit pas compter comme du temps d'étude.
            if (onRead) {
                state.heartbeat = setInterval(() => {
                    if (document.visibilityState !== 'visible') return;

                    onRead.invokeMethodAsync('OnStudyHeartbeat').catch(() => { });
                }, HEARTBEAT_MS);
            }

            // Reprise : on attend un frame que la mise en page soit stabilisée
            // (KaTeX et les images changent la hauteur du document).
            requestAnimationFrame(() => {
                const saved = readPositions()[courseId];
                if (saved) {
                    const target = scroller.querySelector(`#${CSS.escape(saved)}`);
                    if (target) target.scrollIntoView({ block: 'start', behavior: 'auto' });
                }
                refresh();
            });

            return prefs;
        },

        /** Change une préférence, l'applique et la mémorise. */
        setPreference(root, key, value) {
            const prefs = loadPreferences();
            prefs[key] = value;
            localStorage.setItem(STORAGE_PREFS, JSON.stringify(prefs));
            applyPreferences(root, prefs);
            return prefs;
        },

        getPreferences() {
            return loadPreferences();
        },

        /** Saut vers une section depuis le sommaire. */
        scrollToHeading(scroller, id) {
            if (!scroller || !id) return;
            const target = scroller.querySelector(`#${CSS.escape(id)}`);
            if (!target) return;

            target.scrollIntoView({ block: 'start', behavior: 'smooth' });
            highlight(id);
        },

        /**
         * À rappeler quand le contenu du cours change (fin de la mise en forme, ou
         * passage à une autre partie) : les titres et les liens du sommaire ne sont
         * plus les mêmes objets DOM, et un nouveau texte est à relire depuis le début.
         */
        rescan(resetReadState) {
            if (!state) return;
            state.headings = Array.from(state.scroller.querySelectorAll('.course-canvas h1[id], .course-canvas h2[id], .course-canvas h3[id]'));
            state.tocLinks = Array.from(state.root.querySelectorAll('[data-toc-target]'));
            state.activeId = null;

            if (resetReadState) {
                state.readNotified = false;
                state.scroller.scrollTop = 0;
            }

            refresh();
        },

        dispose() {
            if (!state) return;
            state.scroller.removeEventListener('scroll', state.onScroll);
            window.removeEventListener('resize', state.onScroll);

            // Sans cela, le minuteur survivrait à la navigation et continuerait à
            // signaler de l'activité sur un cours que l'élève a quitté.
            if (state.heartbeat) clearInterval(state.heartbeat);

            state = null;
        },
    };
})();
