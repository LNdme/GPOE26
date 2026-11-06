// Parcours hors connexion (phase 4) dans un vrai navigateur : le site web en mode démonstration hors ligne
// (Offline__Enabled=true) se comporte comme l'application Windows. Les services Cours, Quiz et Chat sont
// réellement arrêtés puis relancés pendant le parcours.
//   python3 scripts/seed-offline-demo.py > ids.json
//   SVC=/chemin/svc.sh node scripts/ui-offline-e2e.js <dossier-captures> ids.json scripts/fixtures/copie-derivee.jpg
// SVC : commande « $SVC stop <service> » / « $SVC start <service> » (services : cours, quiz, chat).
const { chromium } = require('playwright');
const { execSync } = require('child_process');
const fs = require('fs');

const WEB = process.env.WEB ?? 'http://localhost:5000';
const SVC = process.env.SVC;
const [SHOTS = '.', IDS = 'ids.json', COPY] = process.argv.slice(2);
const { course, assessment } = JSON.parse(fs.readFileSync(IDS, 'utf8'));
let passed = 0, failed = 0;
const errors = [];

const ok = (label) => { passed++; console.log('  ✓', label); };
const ko = (label, e) => { failed++; console.log('  ✗', label, '→', (e?.message ?? e).toString().split('\n')[0]); };
async function step(label, fn) { try { await fn(); ok(label); } catch (e) { ko(label, e); } }
async function shot(page, name, full = true) { await page.waitForTimeout(500); await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: full }); }
const services = (action) => ['cours', 'quiz', 'chat'].forEach(s => execSync(`${SVC} ${action} ${s}`, { stdio: 'inherit' }));
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

(async () => {
  const browser = await chromium.launch();
  const desktop = { viewport: { width: 1366, height: 900 }, locale: 'fr-FR', deviceScaleFactor: 2 };
  const ctx = await browser.newContext(desktop);
  const student = await ctx.newPage(); watch(student, 'élève');

  console.log('── En ligne : téléchargement pour le hors connexion');
  await step('connexion élève', () => login(student, 'eleve@gpoe26.cm'));
  await step('cours : « Tout rendre disponible hors ligne »', async () => {
    await student.goto(`${WEB}/app/cours`);
    await student.click('button:has-text("Tout rendre disponible hors ligne")');
    await student.waitForSelector('.ga-tag:has-text("HORS LIGNE")', { timeout: 30000 });
  });
  await shot(student, '01-cours-telecharges');

  await step('devoir : « Composer hors ligne »', async () => {
    await student.goto(`${WEB}/app/evaluations`);
    await student.click('button:has-text("Composer hors ligne")');
    await student.waitForSelector('.ga-card .ga-tag:has-text("HORS LIGNE")', { timeout: 30000 });
  });
  await shot(student, '02-devoir-telecharge');
  await student.goto(`${WEB}/app`);          // tableau de bord mis en cache
  await student.waitForTimeout(1500);

  console.log('── Coupure : arrêt des services Cours, Quiz et Chat');
  services('stop');

  await step('bandeau « Hors connexion » et cours lisibles', async () => {
    await student.goto(`${WEB}/app/cours`);
    await student.waitForSelector('.ga-offline:has-text("Hors connexion")', { timeout: 90000 });
    await student.waitForSelector('text=Dérivation');
  });
  await shot(student, '03-hors-connexion-cours');

  await step('lecture du cours hors connexion (formules rendues)', async () => {
    await student.goto(`${WEB}/app/cours/${course}`);
    await student.waitForSelector('.rd-content .katex', { timeout: 90000 });
  });
  await shot(student, '04-hors-connexion-lecture');

  await step('composition hors connexion avec photo de la copie', async () => {
    await student.goto(`${WEB}/app/evaluations`);
    await student.waitForSelector('text=Devoir — Dérivation', { timeout: 90000 });
    await student.locator('.ga-card', { hasText: 'Devoir — Dérivation' }).locator('a:has-text("Commencer")').click();
    await student.waitForSelector('.ex-q', { timeout: 90000 });
    await student.locator('.ex-opt', { hasText: '3x²' }).click();
    await student.click('button:has-text("Suivante")');
    await student.fill('#ans-text', '7');
    await student.click('button:has-text("Suivante")');
    await student.setInputFiles('.ex-q input[type=file]', COPY);
    await student.waitForSelector('.ga-photo-card img', { timeout: 30000 });
  });
  await shot(student, '05-composition-hors-ligne');

  await step('copie rendue : enregistrée sur l\'appareil', async () => {
    await student.click('.ex-head button:has-text("Rendre ma copie")');
    await student.click('button:has-text("Oui, rendre ma copie")');
    await student.waitForSelector('text=Copie enregistrée');
    await student.waitForSelector('.ga-offline:has-text("1 envoi(s) en attente")', { timeout: 10000 });
  });
  await shot(student, '06-copie-en-attente');

  let pendingUrl = '';
  await step('la copie en attente est consultable', async () => {
    await student.click('a:has-text("Voir ma copie")');
    await student.waitForSelector('text=enregistrée sur cet appareil');
    pendingUrl = student.url();
  });

  console.log('── Retour du réseau : relance des services');
  services('start');
  await step('synchronisation automatique (bandeau disparu)', async () => {
    await student.waitForFunction(() => !document.querySelector('.ga-offline'), null, { timeout: 120000 });
  });
  await step('la copie est désormais celle du serveur', async () => {
    await student.goto(pendingUrl);
    await student.waitForSelector('text=Ta copie est bien rendue', { timeout: 30000 });
    await student.waitForSelector('.ga-photo-card img');
  });
  await shot(student, '07-copie-synchronisee');

  console.log('── Enseignant');
  const tctx = await browser.newContext(desktop);
  const teacher = await tctx.newPage(); watch(teacher, 'enseignant');
  await step('copie marquée « HORS LIGNE » dans la liste', async () => {
    await login(teacher, 'prof@gpoe26.cm');
    await teacher.goto(`${WEB}/app/evaluations/${assessment}/copies`);
    await teacher.waitForSelector('.ga-tag:has-text("HORS LIGNE")', { timeout: 30000 });
  });
  await shot(teacher, '08-enseignant-copies');
  await step('copie composée hors ligne, QCM et numérique déjà corrigés, photo jointe', async () => {
    await teacher.locator('a:has-text("Corriger (")').first().click();
    await teacher.waitForSelector('text=Copie composée hors connexion');
    await teacher.waitForSelector('.ga-photo-card img');
    const tags = await teacher.locator('.ga-card .ga-tag').allInnerTexts();
    if (!tags.some(t => t.includes('1 / 1')) || !tags.some(t => t.includes('2 / 2'))) throw new Error(`notes : ${tags.join(', ')}`);
  });
  await shot(teacher, '09-enseignant-copie-hors-ligne');

  await browser.close();
  console.log(`\n${passed} étape(s) réussie(s), ${failed} échec(s)`);
  if (errors.length) { console.log('Erreurs console :'); [...new Set(errors)].slice(0, 10).forEach(e => console.log('  -', e)); }
  process.exit(failed ? 1 : 0);
})();
