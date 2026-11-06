// Parcours du tuteur IA et des copies photographiées dans un vrai navigateur (Playwright), avec un vrai LLM.
// Prérequis : pile démarrée (start.sh) avec LLM__ApiKey, données de démonstration (Development), puis
//   python3 scripts/seed-tutor-demo.py > ids.json      (cours de physique, exercice, élève Premium)
//   node scripts/ui-tutor-e2e.js <dossier-captures> <ids.json> <photo-exercice.jpg> <photo-chute.jpg>
// Les modèles gratuits sont lents : chaque réponse peut prendre jusqu'à quelques minutes.
const { chromium } = require('playwright');
const fs = require('fs');

const WEB = process.env.WEB ?? 'http://localhost:5000';
const [SHOTS = '.', IDS = 'ids.json', COPY_EXERCISE, COPY_TUTOR] = process.argv.slice(2);
const { course, assessment } = JSON.parse(fs.readFileSync(IDS, 'utf8'));
const LLM_WAIT = 300_000;
let passed = 0, failed = 0;
const errors = [];

const ok = (label) => { passed++; console.log('  ✓', label); };
const ko = (label, e) => { failed++; console.log('  ✗', label, '→', (e?.message ?? e).toString().split('\n')[0]); };
async function step(label, fn) { try { await fn(); ok(label); } catch (e) { ko(label, e); } }
async function shot(page, name, full = true) { await page.waitForTimeout(600); await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: full }); }
function watch(page, who) {
  page.on('console', m => { if (m.type() === 'error') errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who}: ${e.message}`));
}
async function login(page, email) {
  await page.goto(`${WEB}/login`);
  await page.fill('#login-email', email);
  await page.fill('#login-password', 'Test1234!');
  await page.click('button[type=submit]');
  await page.waitForURL(/\/app/, { timeout: 20000 });
}
async function botCount(page) { return page.locator('.tu-msg.bot:not(.typing)').count(); }
async function waitReply(page, before) {
  await page.waitForFunction(n => document.querySelectorAll('.tu-msg.bot:not(.typing)').length > n && !document.querySelector('.tu-msg.typing'),
    before, { timeout: LLM_WAIT });
  const err = page.locator('.tu .ga-alert.error');
  if (await err.count()) throw new Error(await err.innerText());
}

(async () => {
  const browser = await chromium.launch();
  const desktop = { viewport: { width: 1366, height: 900 }, locale: 'fr-FR', deviceScaleFactor: 2 };
  const mobile = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'fr-FR' };

  // ═══ Élève : lecture du cours et tuteur ═══════════════════════════════════
  console.log('── Élève : tuteur IA dans le lecteur de cours');
  const sctx = await browser.newContext(desktop);
  const student = await sctx.newPage(); watch(student, 'élève');
  await step('connexion élève Premium', () => login(student, 'eleve@gpoe26.cm'));

  await step('lecture du cours avec formules KaTeX', async () => {
    await student.goto(`${WEB}/app/cours/${course}`);
    await student.waitForSelector('.rd-content .katex', { timeout: 15000 });
  });
  await shot(student, '01-cours-formules');

  await step('ouverture du tuteur (accueil ou discussion enregistrée)', async () => {
    await student.click('button:has-text("Demander au tuteur IA")');
    await student.waitForSelector('.tu-drawer .tu-scroll');
    await student.waitForSelector('.tu-intro, .tu-saved', { timeout: 15000 });
  });
  await shot(student, '02-tuteur-historique', false);

  await step('question au tuteur (réponse du LLM, formules rendues)', async () => {
    const before = await botCount(student);
    await student.fill('.tu-input textarea', "Donne-moi un exercice d'application sur la relation v² − v0² = 2a(x − x0), sans la solution.");
    await student.click('.tu-send');
    await waitReply(student, before);
  });
  await shot(student, '03-tuteur-reponse', false);

  await step('photo de copie envoyée au tuteur : démarche vérifiée', async () => {
    const before = await botCount(student);
    await student.setInputFiles('.tu-input input[type=file]', COPY_TUTOR);
    await student.waitForSelector('.tu-attach img');
    await shot(student, '04-tuteur-photo-prete', false);
    await student.fill('.tu-input textarea', "J'ai fait l'exercice de chute libre sur papier, peux-tu vérifier ma démarche ?");
    await student.click('.tu-send');
    await waitReply(student, before);
    await student.waitForSelector('.tu-msg.me .ga-photo img');
  });
  await shot(student, '05-tuteur-analyse-photo', false);
  const lastReply = (await student.locator('.tu-msg.bot .tu-md').last().innerText()).slice(0, 400);
  console.log('    réponse du tuteur sur la photo :', lastReply.replace(/\s+/g, ' ').slice(0, 300), '…');

  await step('épingler l\'explication', async () => {
    await student.locator('.tu-msg.bot .tu-tools button').last().click();
    await student.waitForSelector('.tu-msg.bot .tu-tools button.on');
  });

  let count = 0;
  await step('fermeture du cours puis réouverture : la discussion est conservée', async () => {
    count = await student.locator('.tu-msg').count();
    await student.click('.tu-head button[aria-label="Fermer le tuteur"]');
    await student.goto(`${WEB}/app`);
    await student.goto(`${WEB}/app/cours/${course}`);
    await student.click('button:has-text("Demander au tuteur IA")');
    await student.waitForSelector('.tu-saved');
    const again = await student.locator('.tu-msg').count();
    if (again !== count) throw new Error(`${again} messages au lieu de ${count}`);
  });

  await step('« Tuteur IA » : liste des discussions', async () => {
    await student.goto(`${WEB}/app/tuteur`);
    await student.waitForSelector('.tu-list .ga-card');
  });
  await shot(student, '06-mes-discussions');

  await step('explications épinglées', async () => {
    await student.click('button[role=tab]:has-text("épinglées")');
    await student.waitForSelector('.tu-pinned .ga-card');
  });
  await shot(student, '07-explications-epinglees');

  await step('relecture d\'une discussion en pleine page', async () => {
    await student.click('.tu-pinned a:has-text("Voir dans la discussion")');
    await student.waitForSelector('.tu-msg.flash', { timeout: 15000 });
  });
  await shot(student, '08-discussion-pleine-page', false);

  // ═══ Élève : exercice traité sur papier ════════════════════════════════════
  console.log('── Élève : exercice avec copie photographiée');
  await step('composition : QCM puis photo de la copie lue par l\'IA', async () => {
    await student.goto(`${WEB}/app/evaluations/${assessment}/composer`);
    await student.waitForSelector('.ex-q');
    await student.locator('.ex-opt', { hasText: 'constante' }).click();
    await student.click('button:has-text("Suivante")');
    await student.setInputFiles('.ex-q input[type=file]', COPY_EXERCISE);
    await student.waitForSelector('.ga-photo-card img', { timeout: LLM_WAIT });
    await student.waitForSelector('.ga-photo-card .ga-transcript, .ga-photo-card .ga-small', { timeout: 5000 });
  });
  await shot(student, '09-composition-photo-transcrite');

  await step('copie rendue (réponse photographiée comptée)', async () => {
    await student.click('.ex-head button:has-text("Rendre ma copie")');
    const warn = await student.locator('.ga-alert.warn strong').innerText();
    if (/sans réponse/.test(warn)) throw new Error(warn);
    await student.click('button:has-text("Oui, rendre ma copie")');
    await student.click('a:has-text("Voir ma copie")');
    await student.waitForSelector('.ga-photo-card img');
  });

  await step('analyse de la démarche par l\'IA (après publication)', async () => {
    await student.click('button:has-text("Analyser ma démarche avec l\'IA")');
    await student.waitForSelector('.ga-ai-box', { timeout: LLM_WAIT });
  });
  await shot(student, '10-copie-analyse-ia');
  const analysis = await student.locator('.ga-ai-box').innerText().catch(() => '');
  console.log('    analyse :', analysis.replace(/\s+/g, ' ').slice(0, 300), '…');

  // ═══ Enseignant : correction assistée sur la photo ════════════════════════
  console.log('── Enseignant : correction assistée d\'une copie photographiée');
  const tctx = await browser.newContext(desktop);
  const teacher = await tctx.newPage(); watch(teacher, 'enseignant');
  await step('connexion enseignant et copie photographiée', async () => {
    await login(teacher, 'prof@gpoe26.cm');
    await teacher.goto(`${WEB}/app/evaluations/${assessment}/copies`);
    await teacher.locator('a:has-text("Corriger (")').first().click();
    await teacher.waitForSelector('.ga-photo-card img');
  });
  await step('proposition de note de l\'IA à partir de la photo', async () => {
    await teacher.click('button:has-text("Proposition de l\'IA")');
    await teacher.waitForSelector('text=Proposition de l\'IA :', { timeout: LLM_WAIT });
  });
  await shot(teacher, '11-enseignant-proposition-ia');

  // ═══ Mobile ════════════════════════════════════════════════════════════════
  console.log('── Élève sur mobile');
  const mctx = await browser.newContext(mobile);
  const phone = await mctx.newPage(); watch(phone, 'mobile');
  await step('tuteur sur mobile (plein écran)', async () => {
    await login(phone, 'eleve@gpoe26.cm');
    await phone.goto(`${WEB}/app/cours/${course}`);
    await phone.click('button.tu-fab');
    await phone.waitForSelector('.tu-drawer .tu-saved');
  });
  await shot(phone, '12-mobile-tuteur', false);
  await step('composition sur mobile : bouton photo', async () => {
    await phone.goto(`${WEB}/app/evaluations/${assessment}/composer`);
    await phone.waitForSelector('.ex-q');
    await phone.click('nav.ex-dots button:nth-child(2)');
    await phone.waitForSelector('label:has-text("Photographier ma copie")');
  });
  await shot(phone, '13-mobile-composition-photo');

  await browser.close();
  console.log(`\n${passed} étape(s) réussie(s), ${failed} échec(s)`);
  if (errors.length) { console.log('Erreurs console :'); errors.slice(0, 10).forEach(e => console.log('  -', e)); }
  process.exit(failed ? 1 : 0);
})();
