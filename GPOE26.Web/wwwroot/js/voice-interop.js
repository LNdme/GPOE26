// ══════════════════════════════════════════════════════════════════════════════
//  Lecture à voix haute des explications
//  ────────────────────────────────────────────────────────────────────────────
//  L'audio arrive de .NET en base64 : le circuit Blazor l'a déjà récupéré, avec le
//  JWT de l'élève. Passer par une URL directe obligerait à exposer une seconde route
//  authentifiée, alors que le circuit fait déjà le travail.
//
//  Un seul son à la fois : deux explications qui se parlent par-dessus sont
//  inécoutables.
// ══════════════════════════════════════════════════════════════════════════════

window.voiceInterop = (() => {
    let current = null;      // { audio, url }
    let dotNetRef = null;    // pour signaler la fin de lecture

    function release() {
        if (!current) return;

        current.audio.pause();
        current.audio.src = '';

        // Sans révocation, chaque écoute laisse fuir son blob jusqu'au rechargement.
        URL.revokeObjectURL(current.url);
        current = null;
    }

    function notifyEnded() {
        if (!dotNetRef) return;
        dotNetRef.invokeMethodAsync('OnSpeechEnded').catch(() => { });
    }

    return {
        /**
         * Joue un MP3 encodé en base64.
         * @param {string} base64 contenu audio
         * @param {object} owner  référence .NET prévenue à la fin de la lecture
         */
        play(base64, owner) {
            release();
            dotNetRef = owner ?? null;

            const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
            const url = URL.createObjectURL(new Blob([bytes], { type: 'audio/mpeg' }));
            const audio = new Audio(url);

            current = { audio, url };

            audio.addEventListener('ended', () => { release(); notifyEnded(); });
            audio.addEventListener('error', () => { release(); notifyEnded(); });

            // play() est refusé si l'utilisateur n'a pas encore interagi avec la page.
            // Ici la lecture part d'un clic, mais on reste défensif.
            return audio.play().catch(() => { release(); notifyEnded(); });
        },

        stop() {
            release();
        },

        isPlaying() {
            return current !== null;
        },
    };
})();
