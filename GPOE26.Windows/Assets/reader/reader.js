// Pont entre l'application (C#) et la page de lecture. Le HTML arrive déjà rendu et assaini
// par MarkdownRenderer (Markdig, HTML brut désactivé) ; ici on ne fait que l'afficher et rendre les formules.
(() => {
  const root = () => document.getElementById("root");
  const post = (message) => window.chrome?.webview?.postMessage(message);

  // Les images du cours pointent vers le service Cours (« /uploads/… ») : on les fait pointer vers son adresse.
  function absolutize(container, baseUrl) {
    if (!baseUrl) return;
    container.querySelectorAll("img[src^='/']").forEach((img) => {
      img.src = baseUrl.replace(/\/$/, "") + img.getAttribute("src");
    });
  }

  // Même logique que markdown-interop.js du site : <span class="math">\(…\)</span>, <div class="math">\[…\]</div>
  function renderMath(container) {
    if (!window.katex) return;
    container.querySelectorAll(".math").forEach((el) => {
      let text = el.textContent.trim();
      const display = el.tagName.toLowerCase() === "div";
      if ((text.startsWith("\\(") && text.endsWith("\\)")) || (text.startsWith("\\[") && text.endsWith("\\]")))
        text = text.substring(2, text.length - 2);
      try {
        window.katex.render(text, el, { displayMode: display, throwOnError: false });
      } catch { /* formule invalide : le texte brut reste lisible */ }
    });
  }

  // Outils (quiz, exercices…) : un bouton data-action renvoie l'action à l'application, avec les champs de son formulaire.
  // Pour un quiz, chaque groupe de boutons radio « q:<id> » donne la réponse choisie (-1 : pas de réponse).
  function collect(scope) {
    const values = {};
    scope.querySelectorAll("select[name], input[name]:not([type=radio])").forEach((el) => { values[el.name] = el.value; });
    const groups = new Set([...scope.querySelectorAll("input[type=radio][name]")].map((el) => el.name));
    groups.forEach((name) => {
      const checked = scope.querySelector(`input[type=radio][name="${CSS.escape(name)}"]:checked`);
      values[name] = checked ? checked.value : "-1";
    });
    return values;
  }

  document.addEventListener("click", (e) => {
    const target = e.target.closest("[data-action]");
    if (!target) return;
    e.preventDefault();
    e.stopPropagation();
    const scope = target.closest("[data-form]") || document;
    post({ type: "action", action: target.dataset.action, value: target.dataset.value ?? null, values: collect(scope) });
  }, true);

  // Liens : jamais de navigation dans la page de lecture ; les adresses web s'ouvrent dans le navigateur.
  document.addEventListener("click", (e) => {
    const a = e.target.closest("a[href]");
    if (!a) return;
    const href = a.getAttribute("href");
    if (href.startsWith("#")) {
      e.preventDefault();
      document.getElementById(decodeURIComponent(href.substring(1)))?.scrollIntoView({ behavior: "smooth" });
      return;
    }
    e.preventDefault();
    post({ type: "open", url: a.href });
  });

  const escape = (s) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

  function messageHtml(m) {
    const body = m.role === "user" ? escape(m.text ?? "") : (m.html ?? "");
    return `<div class="msg ${m.role}"><div><div class="body${m.typing ? " typing" : ""}">${body}</div>` +
      (m.meta ? `<div class="meta">${escape(m.meta)}</div>` : "") + `</div></div>`;
  }

  const nearBottom = () => window.innerHeight + window.scrollY >= document.body.scrollHeight - 80;

  window.gpoe = {
    setTheme(theme) { document.documentElement.dataset.theme = theme; },

    // Réglage « Taille du texte » de l'application : un ou deux points de plus, cours et discussion.
    setTextStep(step) { document.documentElement.style.setProperty("--step", String(step | 0)); },

    showCourse(title, subject, html, baseUrl) {
      const r = root();
      r.className = "course";
      r.innerHTML = `<h1>${escape(title)}</h1><p class="subject">${escape(subject ?? "")}</p>${html}`;
      absolutize(r, baseUrl);
      renderMath(r);
      window.scrollTo(0, 0);
    },

    showEmpty(text) {
      const r = root();
      r.className = "";
      r.innerHTML = `<div class="empty">${escape(text)}</div>`;
    },

    // Page d'un outil de la barre de droite (HTML préparé et assaini par ToolPages côté application)
    showTool(html) {
      const r = root();
      r.className = "tool";
      r.innerHTML = html;
      renderMath(r);
      window.scrollTo(0, 0);
    },

    scrollToHeading(id) { document.getElementById(id)?.scrollIntoView({ behavior: "smooth" }); },

    // Discussion : la liste complète…
    setMessages(messages) {
      const r = root();
      r.className = "chat";
      r.innerHTML = messages.map(messageHtml).join("");
      renderMath(r);
      window.scrollTo(0, document.body.scrollHeight);
    },

    // …puis la réponse en cours d'écriture, remplacée à chaque morceau reçu.
    updateLast(message) {
      const r = root();
      const follow = nearBottom();
      const last = r.lastElementChild;
      const wrapper = document.createElement("div");
      wrapper.innerHTML = messageHtml(message);
      const fresh = wrapper.firstElementChild;
      if (last && last.classList.contains(message.role)) r.replaceChild(fresh, last); else r.appendChild(fresh);
      renderMath(fresh);
      if (follow) window.scrollTo(0, document.body.scrollHeight);
    }
  };

  post({ type: "ready" });
})();
