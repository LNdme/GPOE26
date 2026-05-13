window.markdownInterop = {

    // ── Scroll chat to bottom ──────────────────────────────────
    scrollToBottom: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    },

    // ── Inject id attributes into headings for TOC anchors ────
    injectHeadingIds: function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const headings = container.querySelectorAll('h1, h2, h3, h4');
        const seen = {};
        headings.forEach(h => {
            const raw = h.textContent.trim().toLowerCase()
                .replace(/[àâä]/g, 'a').replace(/[éèêë]/g, 'e')
                .replace(/[ïî]/g, 'i').replace(/[ôö]/g, 'o')
                .replace(/[ùûü]/g, 'u').replace(/ç/g, 'c')
                .replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
            let slug = 'h-' + raw;
            // deduplicate
            if (seen[slug]) { seen[slug]++; slug += '-' + seen[slug]; }
            else seen[slug] = 1;
            h.id = slug;
            h.style.scrollMarginTop = '130px';
        });
    },

    // ── Reading progress bar (window scroll) ──────────────────
    setupReadingProgress: function (progressBarId) {
        const bar = document.getElementById(progressBarId);
        if (!bar) return;
        const onScroll = () => {
            const scrollTop = window.scrollY || document.documentElement.scrollTop;
            const docHeight = document.documentElement.scrollHeight - window.innerHeight;
            const pct = docHeight > 0 ? Math.min(100, (scrollTop / docHeight) * 100) : 0;
            bar.style.width = pct + '%';
        };
        window.removeEventListener('scroll', window._progressHandler);
        window._progressHandler = onScroll;
        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
    },

    renderEffects: function (element) {
        if (!element) return;

        // Render math with KaTeX explicitly over Markdig's `.math` elements
        const renderMath = () => {
            if (window.katex) {
                const mathElements = element.querySelectorAll('.math');
                mathElements.forEach(el => {
                    let text = el.textContent.trim();
                    let isDisplay = el.tagName.toLowerCase() === 'div';

                    // Remove \( \) and \[ \] that Markdig adds if present
                    if (text.startsWith('\\(') && text.endsWith('\\)')) {
                        text = text.substring(2, text.length - 2);
                    } else if (text.startsWith('\\[') && text.endsWith('\\]')) {
                        text = text.substring(2, text.length - 2);
                    }

                    try {
                        window.katex.render(text, el, {
                            displayMode: isDisplay,
                            throwOnError: false
                        });
                    } catch (e) {
                        console.error('KaTeX error:', e);
                    }
                });
            } else {
                // Retry after 100ms if KaTeX script is not fully loaded yet
                setTimeout(renderMath, 100);
            }
        };
        renderMath();

        // Render code blocks with Prism
        if (window.Prism) {
            window.Prism.highlightAllUnder(element);
        }
    },

    insertAtCursor: function (elementId, startTag, endTag) {
        let field = document.getElementById(elementId);
        if (!field) return "";

        let scrollPos = field.scrollTop;
        let strPos = 0;
        let br = ((field.selectionStart || field.selectionStart === 0) ?
            "ff" : (document.selection ? "ie" : false));

        if (br === "ie") {
            field.focus();
            let range = document.selection.createRange();
            range.moveStart('character', -field.value.length);
            strPos = range.text.length;
        } else if (br === "ff") strPos = field.selectionStart;

        let endPos = field.selectionEnd;
        let currentText = field.value;

        // Si on a sélectionné du texte, on l'entoure (ex: gras)
        if (strPos !== endPos) {
            let selectedText = currentText.substring(strPos, endPos);
            let insertion = startTag + selectedText + endTag;
            field.value = currentText.substring(0, strPos) + insertion + currentText.substring(endPos, currentText.length);
            strPos = strPos + insertion.length;
        } else {
            // Sinon on insère vide et on place le curseur entre les balises
            let insertion = startTag + endTag;
            field.value = currentText.substring(0, strPos) + insertion + currentText.substring(strPos, currentText.length);
            strPos = strPos + startTag.length;
        }

        field.focus();

        if (br === "ie") {
            let range = document.selection.createRange();
            range.moveStart('character', -field.value.length);
            range.moveStart('character', strPos);
            range.moveEnd('character', 0);
            range.select();
        } else if (br === "ff") {
            field.selectionStart = strPos;
            field.selectionEnd = strPos;
            field.scrollTop = scrollPos;
        }

        // Déclencher un événement 'input' pour que Blazor mette à jour son bind
        let event = new Event('input', { bubbles: true });
        field.dispatchEvent(event);

        return field.value;
    }
};
