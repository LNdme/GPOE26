window.markdownInterop = {
    renderEffects: function (element) {
        if (!element) return;

        // Render math with KaTeX explicitly over Markdig's `.math` elements.
        //
        // KaTeX replaces the *contents* of the `.math` node but leaves the class in
        // place, so a second pass would feed already-rendered KaTeX markup back into
        // katex.render and corrupt the formula. Every processed node is therefore
        // stamped with data-katex-done and skipped on later passes.
        const renderMath = () => {
            if (window.katex) {
                const mathElements = element.querySelectorAll('.math:not([data-katex-done])');
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
                        el.setAttribute('data-katex-done', '');
                    } catch (e) {
                        // Stamp failures too, otherwise a malformed formula is retried on
                        // every single re-render of the component.
                        el.setAttribute('data-katex-done', '');
                        console.error('KaTeX error:', e);
                    }
                });
            } else {
                // Retry after 100ms if KaTeX script is not fully loaded yet
                setTimeout(renderMath, 100);
            }
        };
        renderMath();

        // Render code blocks with Prism — same idempotence concern: highlightAllUnder
        // would re-tokenize markup Prism itself produced.
        if (window.Prism) {
            element.querySelectorAll('pre > code:not([data-prism-done])').forEach(el => {
                el.setAttribute('data-prism-done', '');
                window.Prism.highlightElement(el);
            });
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
