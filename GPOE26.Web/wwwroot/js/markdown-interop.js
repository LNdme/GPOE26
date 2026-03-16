window.markdownInterop = {
    renderEffects: function (element) {
        if (!element) return;

        const renderMath = () => {
            if (window.katex) {
                const mathElements = element.querySelectorAll('.math');
                mathElements.forEach(el => {
                    let text = el.textContent.trim();
                    let isDisplay = el.tagName.toLowerCase() === 'div';

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
                setTimeout(renderMath, 100);
            }
        };
        renderMath();

        if (window.Prism) {
            window.Prism.highlightAllUnder(element);
        }

        element.querySelectorAll('pre[class*="language-"]').forEach(pre => {
            const cls = Array.from(pre.classList).find(c => c.startsWith('language-'));
            if (cls) {
                pre.setAttribute('data-language', cls.replace('language-', ''));
            }
        });
    },

    initReadingProgress: function () {
        const bar = document.getElementById('reading-progress-bar');
        if (!bar) return;

        const update = () => {
            const scrollTop = window.scrollY || document.documentElement.scrollTop;
            const docHeight = document.documentElement.scrollHeight - document.documentElement.clientHeight;
            const pct = docHeight > 0 ? Math.min((scrollTop / docHeight) * 100, 100) : 0;
            bar.style.width = pct + '%';
        };

        window.removeEventListener('scroll', window._progressHandler);
        window._progressHandler = update;
        window.addEventListener('scroll', update, { passive: true });
        update();
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

        if (strPos !== endPos) {
            let selectedText = currentText.substring(strPos, endPos);
            let insertion = startTag + selectedText + endTag;
            field.value = currentText.substring(0, strPos) + insertion + currentText.substring(endPos, currentText.length);
            strPos = strPos + insertion.length;
        } else {
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

        let event = new Event('input', { bubbles: true });
        field.dispatchEvent(event);

        return field.value;
    }
};
