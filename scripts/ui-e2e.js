// Parcours de bout en bout de l'espace d'apprentissage dans un vrai navigateur (Playwright).
// Prérequis : pile complète démarrée sur des bases neuves (User, Cours, Quiz, ApiService, Web sur :5000)
// et, pour la correction assistée, un LLM compatible OpenAI configuré dans Quiz.
//   node scripts/ui-e2e.js <dossier-captures>
const { chromium } = require('playwright');

const WEB = process.env.WEB ?? 'http://localhost:5000';
const COURS = process.env.COURS_API ?? 'http://localhost:5188';
const USER = process.env.USER_API ?? 'http://localhost:5197';
const SHOTS = process.argv[2] ?? '.';
const RUN = Date.now().toString(36);
let passed = 0, failed = 0;
const errors = [];

const ok = (label) => { passed++; console.log('  ✓', label); };
const ko = (label, e) => { failed++; console.log('  ✗', label, '→', (e?.message ?? e).toString().split('\n')[0]); };
async function step(label, fn) { try { await fn(); ok(label); } catch (e) { ko(label, e); } }
async function shot(page, name) { await page.waitForTimeout(300); await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: true }); }

function watch(page, who) {
  page.on('console', m => { if (m.type() === 'error') errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who}: ${e.message}`));
}

async function choose(page, label) { await page.locator('button.ob-option', { hasText: label }).first().click(); await next(page); }
async function next(page) { await page.locator('.ob-foot button.ga-btn').click(); }
async function type(page, value) { await page.fill('#ob-text', value); await next(page); }
async function account(page, user, email) {
  await page.fill('#ob-user', user);
  await page.fill('#ob-email', email);
  await page.fill('#ob-pwd', 'Test1234!');
  await page.locator('.ob-foot button.ga-btn').click();
  await page.waitForSelector('text=Accéder à mon espace', { timeout: 20000 });
}

(async () => {
  const browser = await chromium.launch();
  const desktop = { viewport: { width: 1366, height: 900 }, locale: 'fr-FR' };
  const mobile = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'fr-FR' };

  // ═══ Enseignant ═══════════════════════════════════════════════════════════
  console.log('── Enseignant : inscription guidée');
  const tctx = await browser.newContext(desktop);
  const teacher = await tctx.newPage(); watch(teacher, 'enseignant');
  let invite = '';
  await step('inscription enseignant (établissement, classe, essai)', async () => {
    await teacher.goto(`${WEB}/register`);
    await choose(teacher, 'Je suis enseignant');
    await type(teacher, 'M. Ndongo');
    await choose(teacher, 'Un établissement scolaire');
    await type(teacher, 'Lycée de Bonabéri');
    await teacher.locator('button.ga-chip', { hasText: 'Mathématiques' }).click(); await next(teacher);
    await teacher.locator('button.ga-chip', { hasText: /^Terminale$/ }).click(); await next(teacher);
    await choose(teacher, 'Essayer gratuitement');
    await account(teacher, `ndongo${RUN}`, `ndongo${RUN}@t.cm`);
    invite = (await teacher.locator('.ob-code strong').first().innerText()).trim();
    if (!/^[A-Z2-9]{3}-[A-Z2-9]{3}$/.test(invite)) throw new Error(`code d'invitation inattendu : ${invite}`);
  });
  await shot(teacher, '01-enseignant-inscription-terminee');

  await step('tableau de bord enseignant', async () => {
    await teacher.click('text=Accéder à mon espace');
    await teacher.waitForSelector('h1:has-text("Bonjour")');
    await teacher.waitForSelector(`text=${invite}`);
  });
  await shot(teacher, '02-enseignant-tableau-de-bord');

  // Cours avec du contenu (créé par l'API, comme le ferait l'éditeur de cours historique)
  let courseId = '';
  await step('cours créé puis partagé avec la classe', async () => {
    const login = await (await fetch(`${USER}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: `ndongo${RUN}@t.cm`, password: 'Test1234!' }) })).json();
    const sections = [
      [0, 'Définition', 1], [1, "Une suite (uₙ) est **géométrique** s'il existe un réel q, la *raison*, tel que uₙ₊₁ = q × uₙ pour tout n.", 0],
      [0, 'Terme général', 1], [1, 'En multipliant n fois par q : **uₙ = u₀ × qⁿ**.\n\n> Exemple : 1 000 bactéries qui doublent chaque heure donnent 1 000 × 2⁵ = 32 000 bactéries après 5 heures.', 0],
      [0, 'Sens de variation', 1], [1, '| Raison q (u₀ > 0) | La suite est… |\n|---|---|\n| q > 1 | croissante |\n| 0 < q < 1 | décroissante |\n| q < 0 | ni l\'un ni l\'autre |', 0],
    ].map(([type, content, level], order) => ({ type, content, order, level }));
    const res = await fetch(`${COURS}/cours`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${login.token}` },
      body: JSON.stringify({ title: 'Suites géométriques', subject: 'Mathématiques', description: 'Chapitre 2 — suites numériques', sections }) });
    courseId = (await res.json()).id;
    await teacher.goto(`${WEB}/app/cours`);
    await teacher.click('button:has-text("Partager")');
    await teacher.locator('label.ga-check').first().click();
    await teacher.click('button:has-text("Publier")');
    await teacher.waitForSelector('.ga-tag:has-text("PUBLIÉ")');
  });
  await shot(teacher, '03-enseignant-cours-partage');

  console.log('── Enseignant : éditeur d\'épreuve');
  await step('création d\'un devoir (QCM + réponse ouverte) et publication', async () => {
    await teacher.goto(`${WEB}/app/evaluations/nouvelle?type=Homework`);
    await teacher.fill('#ed-title', 'Devoir n°1 — Suites géométriques');
    await teacher.fill('#q-text', 'u₀ = 3 et q = 2 : que vaut u₄ ?');
    for (const [i, v] of ['24', '48', '96'].entries()) await teacher.fill(`#opt-${i}`, v);
    await teacher.click('button[aria-label="Marquer l\'option 2 comme correcte"]');
    await teacher.fill('#q-pts', '2'); await teacher.press('#q-pts', 'Tab');
    await teacher.click('button:has-text("Ajouter une question")');
    await teacher.click('button[role="menuitem"]:has-text("Réponse ouverte")');
    await teacher.waitForSelector('#h-q:has-text("Question 2")');
    await teacher.fill('#q-text', 'Montrer que vₙ = uₙ − 4 est géométrique (uₙ₊₁ = 0,5 uₙ + 2).');
    await teacher.fill('#q-crit', 'Expression de vₙ₊₁ (2 pts) ; factorisation (2 pts) ; conclusion (2 pts)'); await teacher.press('#q-crit', 'Tab');
    await teacher.fill('#q-pts', '6'); await teacher.press('#q-pts', 'Tab');
    await teacher.selectOption('#ed-course', courseId);
    await teacher.fill('#ed-due', '2026-12-20T18:00'); await teacher.press('#ed-due', 'Tab');
    await teacher.waitForSelector('text=8 points');
  });
  await shot(teacher, '04-enseignant-editeur');
  await step('publication', async () => {
    await teacher.click('.ga-head button:has-text("Publier")');
    await teacher.waitForURL(/\/app\/evaluations$/, { timeout: 15000 });
    await teacher.waitForSelector('.ga-tag:has-text("PUBLIÉE")');
  });
  await shot(teacher, '05-enseignant-liste-evaluations');

  // ═══ Élève (mobile) ═══════════════════════════════════════════════════════
  console.log('── Élève (mobile) : inscription guidée');
  const sctx = await browser.newContext(mobile);
  const student = await sctx.newPage(); watch(student, 'élève');
  let linkCode = '';
  await step('inscription élève (classe, série, examen, code de classe)', async () => {
    await student.goto(`${WEB}/register`);
    await choose(student, 'Je suis élève');
    await type(student, 'Aïcha');
    await shot(student, '06-eleve-inscription-sous-systeme');
    await choose(student, 'Francophone');
    await choose(student, 'Terminale');
    await choose(student, 'Série C');
    await student.waitForSelector('h1:has-text("Quel examen prépares-tu")');
    await student.waitForSelector('button.ob-option.on:has-text("Baccalauréat")', { timeout: 5000 });
    await shot(student, '07-eleve-inscription-examen-suggere');
    await next(student);
    await student.locator('button.ga-chip', { hasText: 'Mathématiques' }).click(); await next(student);
    await choose(student, '30 min');
    await type(student, invite.toLowerCase());
    await choose(student, 'Commencer gratuitement');
    await account(student, `aicha${RUN}`, `aicha${RUN}@t.cm`);
    linkCode = (await student.locator('.ob-code strong').first().innerText()).trim();
  });
  await shot(student, '08-eleve-inscription-terminee');

  await step('tableau de bord élève (mobile) avec le devoir', async () => {
    await student.click('text=Accéder à mon espace');
    await student.waitForSelector('text=Devoir n°1 — Suites géométriques');
  });
  await shot(student, '09-eleve-tableau-de-bord-mobile');
  await step('menu tiroir (mobile)', async () => {
    await student.click('button[aria-label="Ouvrir le menu"]');
    await student.waitForSelector('.ga-sidebar.open');
  });
  await student.screenshot({ path: `${SHOTS}/10-eleve-menu-mobile.png` });
  if (await student.locator('.ga-scrim.open').count()) await student.click('.ga-scrim.open', { position: { x: 370, y: 400 } });

  await step('lecture du cours partagé (mobile)', async () => {
    await student.goto(`${WEB}/app/cours/${courseId}`);
    await student.waitForSelector('.rd-content h2:has-text("Terme général")');
    await student.waitForSelector('summary:has-text("Sur cette page")');
  });
  await shot(student, '11-eleve-lecture-mobile');

  await step('la session survit au rechargement', async () => {
    await student.reload();
    await student.waitForSelector('h1:has-text("Suites géométriques")', { timeout: 15000 });
  });

  console.log('── Élève : composition');
  await step('composer et rendre la copie', async () => {
    await student.goto(`${WEB}/app/evaluations`);
    await student.click('a:has-text("Commencer")');
    await student.waitForSelector('.ex-q h2');
    await shot(student, '12-eleve-composition-question');
    // Les questions peuvent être mélangées : on répond selon le type affiché
    for (let i = 0; i < 2; i++) {
      await student.waitForSelector(`text=QUESTION ${i + 1} SUR 2`);
      if (await student.locator('#ans-open').count()) await student.fill('#ans-open', 'vₙ₊₁ = uₙ₊₁ − 4 = 0,5 uₙ − 2 = 0,5 (uₙ − 4) = 0,5 vₙ : géométrique de raison 0,5.');
      else await student.locator('button.ex-opt', { hasText: '48' }).click();
      if (i === 0) await student.click('.ex-q button:has-text("Suivante")');
    }
    await student.click('.ex-q button:has-text("Rendre ma copie")');
    await student.click('button:has-text("Oui, rendre ma copie")');
    await student.waitForSelector('text=Copie rendue');
  });
  await shot(student, '13-eleve-copie-rendue');

  // ═══ Correction ═══════════════════════════════════════════════════════════
  console.log('── Enseignant : correction assistée');
  await step('copie à corriger signalée sur le tableau de bord', async () => {
    await teacher.goto(`${WEB}/app`);
    await teacher.waitForSelector('text=1 copie(s) à corriger');
  });
  await step('proposition de l\'IA puis note validée', async () => {
    await teacher.click('text=1 copie(s) à corriger');
    await teacher.click('a:has-text("Corriger")');
    await teacher.click('button:has-text("Proposition de l\'IA")');
    await teacher.waitForSelector('text=Proposition de l\'IA : 4,5 / 6');
    await shot(teacher, '14-enseignant-correction-ia');
    await teacher.click('button:has-text("Reprendre cette proposition")');
    await teacher.click('button:has-text("Valider la note")');
    await teacher.waitForSelector('.ga-stat .val:has-text("16")');
  });
  await step('clôture : résultats publiés', async () => {
    await teacher.click('a:has-text("Toutes les copies")');
    await teacher.click('button:has-text("Clôturer")');
    await teacher.waitForSelector('text=Clôturée');
  });

  await step('l\'élève voit sa note et la correction', async () => {
    await student.goto(`${WEB}/app/evaluations`);
    await student.click('button[role="tab"]:has-text("Rendues")');
    await student.click('a:has-text("Voir ma copie")');
    await student.waitForSelector('.ex-opt.good:has-text("48")');   // bonne réponse du QCM en vert
    await student.waitForSelector('.ga-stat .val:has-text("16,25")');
  });
  await shot(student, '15-eleve-copie-corrigee-mobile');

  // ═══ Parent ═══════════════════════════════════════════════════════════════
  console.log('── Parent : rattachement');
  const pctx = await browser.newContext(desktop);
  const parent = await pctx.newPage(); watch(parent, 'parent');
  await step('inscription parent avec le code de l\'enfant', async () => {
    await parent.goto(`${WEB}/register`);
    await choose(parent, 'Je suis parent');
    await type(parent, 'Clarisse');
    await choose(parent, 'Oui, il ou elle me donnera son code');
    await type(parent, linkCode);
    await choose(parent, 'Gratuit');
    await account(parent, `clarisse${RUN}`, `clarisse${RUN}@t.cm`);
    await parent.waitForSelector('text=demande de rattachement');
  });
  await step('l\'élève accepte la demande', async () => {
    await student.goto(`${WEB}/app`);
    await student.click('.ga-alert button:has-text("Accepter")');
    await student.waitForSelector('.ga-alert.warn', { state: 'detached' });
  });
  await step('le parent voit la note de son enfant', async () => {
    await parent.goto(`${WEB}/app`);
    await parent.waitForSelector(`text=aicha${RUN}`);
    await parent.waitForSelector('.ga-tag:has-text("16,25/20")');
  });
  await shot(parent, '16-parent-tableau-de-bord');

  await step('suivi de classe de l\'enseignant', async () => {
    await teacher.goto(`${WEB}/app`);
    await teacher.click('a:has-text("Voir le suivi")');
    await teacher.waitForSelector(`th:has-text("aicha${RUN}")`);
  });
  await shot(teacher, '17-enseignant-suivi-classe');

  await step('lecture du cours (ordinateur)', async () => {
    await teacher.goto(`${WEB}/app/cours/${courseId}`);
    await teacher.waitForSelector('.rd-toc a:has-text("Terme général")');
  });
  await shot(teacher, '18-lecture-ordinateur');

  console.log(`\nRésultat : ${passed} réussis, ${failed} échoués`);
  console.log('Erreurs navigateur :', errors.length ? '\n  ' + [...new Set(errors)].slice(0, 10).join('\n  ') : 'aucune');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
