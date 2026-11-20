// Laboratoires interactifs des cours (TP virtuels).
// Un cours ne contient qu'une description JSON : tout le code vient d'ici. Aucun script écrit
// par un enseignant ne s'exécute chez un élève, et tous les textes passent par textContent.

const COLORS = { navy: '#0E2350', blue: '#2456C9', green: '#1E6B4A', amber: '#B7791F', red: '#B4411E', grey: '#8C93A6', line: '#E4E2DA', bg: '#FBFAF7' };
const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

/** Remplace chaque bloc ```simulation d'un cours par son laboratoire. */
export function mountLabs(root) {
    if (!root) return;
    root.querySelectorAll('pre > code.language-simulation:not([data-mounted])').forEach(code => {
        const pre = code.parentElement;
        code.dataset.mounted = '1';
        let lab;
        try {
            const config = JSON.parse(code.textContent);
            const build = LABS[config.type];
            if (!build) throw new Error(`Type inconnu : ${config.type}`);
            lab = createFrame(config);
            build(config, lab);
            lab.finish();
        } catch (error) {
            lab = { root: errorCard(error.message) };
        }
        // Le bloc d'origine reste en place : Blazor le connaît et doit pouvoir le retirer quand le cours change
        pre.classList.add('lab-host');
        pre.append(lab.root);
    });
}

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function errorCard(message) {
    const card = el('div', 'lab lab-error');
    card.append(el('strong', '', 'Simulation illisible'), el('span', '', message));
    return card;
}

function createFrame(config) {
    const root = el('figure', 'lab');
    const head = el('div', 'lab-head');
    head.append(el('span', 'lab-kicker', 'LABO'), el('strong', 'lab-title', config.title || 'Simulation'));
    const body = el('div', 'lab-body');
    const stage = el('div', 'lab-stage');
    const panel = el('div', 'lab-panel');
    const controls = el('div', 'lab-controls');
    const readouts = el('dl', 'lab-readouts');
    readouts.setAttribute('aria-live', 'polite');
    panel.append(controls, readouts);
    body.append(stage, panel);
    root.append(head, body);

    const predicted = config.predict?.question ? predictionCard(config.predict, body) : null;
    if (predicted) root.insertBefore(predicted, body);
    if (config.caption) root.append(el('figcaption', 'lab-caption', config.caption));
    if (config.check?.question) root.append(checkCard(config.check));

    const values = new Map();
    return {
        root,
        finish() { /* point d'extension : le labo est prêt */ },
        canvas(height, label) {
            const canvas = el('canvas', 'lab-canvas');
            canvas.setAttribute('role', 'img');
            canvas.setAttribute('aria-label', label || config.title || 'Simulation');
            stage.append(canvas);
            return surface(canvas, height);
        },
        slider(label, min, max, step, value, unit, onInput, format) {
            const row = el('label', 'lab-slider');
            const name = el('span', '', label);
            const input = el('input');
            Object.assign(input, { type: 'range', min, max, step, value });
            const out = el('output');
            const show = () => { out.textContent = format ? format(+input.value) : `${trim(+input.value)}${unit ? ' ' + unit : ''}`; };
            input.addEventListener('input', () => { show(); onInput(+input.value); });
            show();
            row.append(name, input, out);
            controls.append(row);
            return input;
        },
        toggle(label, checked, onChange) {
            const row = el('label', 'lab-toggle');
            const input = el('input');
            input.type = 'checkbox';
            input.checked = checked;
            input.addEventListener('change', () => onChange(input.checked));
            row.append(input, el('span', '', label));
            controls.append(row);
            return input;
        },
        button(label, onClick, secondary) {
            const button = el('button', `ga-btn small ${secondary ? 'secondary' : ''}`, label);
            button.type = 'button';
            button.addEventListener('click', onClick);
            let bar = controls.querySelector('.lab-buttons');
            if (!bar) { bar = el('div', 'lab-buttons'); controls.append(bar); }
            bar.append(button);
            return button;
        },
        readout(name, value) {
            let item = values.get(name);
            if (!item) {
                const dt = el('dt', '', name);
                const dd = el('dd');
                readouts.append(dt, dd);
                item = dd;
                values.set(name, dd);
            }
            if (item.textContent !== value) item.textContent = value;
        },
        clearReadouts() { readouts.replaceChildren(); values.clear(); },
    };
}

function predictionCard(predict, body) {
    body.classList.add('lab-locked');
    const card = el('div', 'lab-predict');
    card.append(el('span', 'lab-kicker', 'VOTRE PRÉDICTION'), el('p', '', predict.question));
    const options = el('div', 'lab-options');
    const result = el('p', 'lab-result');
    (predict.options || []).forEach((label, index) => {
        const button = el('button', 'lab-option', label);
        button.type = 'button';
        button.addEventListener('click', () => {
            body.classList.remove('lab-locked');
            options.querySelectorAll('button').forEach((b, i) => {
                b.disabled = true;
                b.classList.toggle('good', i === predict.answer);
                b.classList.toggle('bad', i === index && i !== predict.answer);
            });
            result.textContent = (index === predict.answer ? 'Bien vu. ' : 'Observez ce qui se passe, puis comparez. ') + (predict.explain || '');
        });
        options.append(button);
    });
    card.append(options, result);
    return card;
}

function checkCard(check) {
    const card = el('div', 'lab-check');
    card.append(el('span', 'lab-kicker', 'CONTRÔLE'), el('p', '', check.question));
    const row = el('div', 'lab-check-row');
    const input = el('input', 'ga-input');
    input.inputMode = 'decimal';
    input.setAttribute('aria-label', 'Votre réponse');
    const button = el('button', 'ga-btn small', 'Vérifier');
    button.type = 'button';
    const result = el('p', 'lab-result');
    result.setAttribute('role', 'status');
    row.append(input);
    if (check.unit) row.append(el('span', 'lab-unit', check.unit));
    row.append(button);
    card.append(row, result);
    const verify = () => {
        const value = parseFloat(input.value.replace(',', '.').replace(/\s/g, ''));
        if (Number.isNaN(value)) { result.textContent = 'Entrez une valeur numérique.'; return; }
        const expected = +check.answer;
        const tolerance = Math.abs(expected) * (check.tolerance ?? 0.02) || 1e-9;
        const good = Math.abs(value - expected) <= tolerance;
        result.textContent = good ? 'Correct.' : `Pas encore. ${check.hint || 'Reprenez vos mesures dans le labo.'}`;
        result.className = `lab-result ${good ? 'good' : 'bad'}`;
    };
    button.addEventListener('click', verify);
    input.addEventListener('keydown', event => {
        if (event.key !== 'Enter') return;
        event.preventDefault();
        verify();
    });
    return card;
}

/** Zone de dessin nette à toute largeur et sur écrans haute densité. */
function surface(canvas, height) {
    const ctx = canvas.getContext('2d');
    const state = { ctx, width: 0, height, redraw: null };
    const resize = () => {
        const width = canvas.clientWidth || 600;
        const ratio = window.devicePixelRatio || 1;
        state.width = width;
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        canvas.style.height = `${height}px`;
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        state.redraw?.();
    };
    new ResizeObserver(resize).observe(canvas);
    canvas.style.height = `${height}px`;
    state.resize = resize;
    state.element = canvas;
    return state;
}

/** Boucle d'animation : s'arrête d'elle-même quand le labo quitte la page, et en pause hors de l'écran. */
function animate(element, step) {
    let visible = true;
    new IntersectionObserver(entries => { visible = entries[0].isIntersecting; }).observe(element);
    let previous = performance.now();
    const frame = now => {
        if (!element.isConnected && now - previous > 2000) return;
        const dt = Math.min((now - previous) / 1000, 0.05);
        previous = now;
        if (visible && element.isConnected) step(dt);
        requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
}

function trim(value) { return Number.isInteger(value) ? String(value) : String(+value.toFixed(3)); }

function si(value, unit) {
    const abs = Math.abs(value);
    if (abs === 0) return `0 ${unit}`;
    if (abs >= 1e6) return `${(value / 1e6).toFixed(2)} M${unit}`;
    if (abs >= 1e3) return `${(value / 1e3).toFixed(2)} k${unit}`;
    if (abs >= 1) return `${value.toFixed(2)} ${unit}`;
    if (abs >= 1e-3) return `${(value * 1e3).toFixed(1)} m${unit}`;
    return `${(value * 1e6).toFixed(1)} µ${unit}`;
}

function clear(s) { s.ctx.clearRect(0, 0, s.width, s.height); s.ctx.fillStyle = COLORS.bg; s.ctx.fillRect(0, 0, s.width, s.height); }

function text(ctx, value, x, y, color = COLORS.navy, align = 'center', size = 12) {
    ctx.fillStyle = color;
    ctx.font = `${size}px "DM Sans", system-ui, sans-serif`;
    ctx.textAlign = align;
    ctx.fillText(value, x, y);
}

// ── Circuit à courant continu : série ou parallèle ───────────────────────────
function circuitLab(config, lab) {
    const series = config.topology === 'series';
    const count = Math.min(Math.max(config.branches ?? 2, 1), 3);
    const state = { U: config.voltage ?? 12, R: [config.R1 ?? 100, config.R2 ?? 220, config.R3 ?? 470].slice(0, count), open: Array(count).fill(false), phase: Array(count).fill(0) };
    const palette = [COLORS.blue, COLORS.green, COLORS.amber];
    const view = lab.canvas(270, series ? 'Circuit en série' : 'Circuit à résistances en parallèle');

    lab.slider('Tension U', 1, 48, 0.5, state.U, 'V', v => { state.U = v; update(); });
    state.R.forEach((r, i) => lab.slider(`R${i + 1}`, 10, 1000, 10, r, 'Ω', v => { state.R[i] = v; update(); }));
    if (series) lab.toggle('Circuit ouvert', false, v => { state.open[0] = v; update(); });
    else state.R.forEach((_, i) => lab.toggle(`Ouvrir la branche ${i + 1}`, false, v => { state.open[i] = v; update(); }));

    let currents = [];
    function update() {
        lab.clearReadouts();
        if (series) {
            const total = state.R.reduce((a, b) => a + b, 0);
            const I = state.open[0] ? 0 : state.U / total;
            currents = [I];
            lab.readout('Résistance équivalente', si(total, 'Ω'));
            lab.readout('Courant I', si(I, 'A'));
            state.R.forEach((r, i) => lab.readout(`Tension sur R${i + 1}`, si(I * r, 'V')));
            lab.readout('Puissance totale', si(state.U * I, 'W'));
        } else {
            currents = state.R.map((r, i) => state.open[i] ? 0 : state.U / r);
            const I = currents.reduce((a, b) => a + b, 0);
            const conductance = state.R.reduce((a, r, i) => a + (state.open[i] ? 0 : 1 / r), 0);
            lab.readout('Courant total I', si(I, 'A'));
            lab.readout('Résistance équivalente', conductance ? si(1 / conductance, 'Ω') : '∞');
            currents.forEach((c, i) => lab.readout(`I${i + 1} = U / R${i + 1}`, si(c, 'A')));
            lab.readout('Puissance totale', si(state.U * I, 'W'));
        }
        draw();
    }

    function paths() {
        const w = view.width, h = view.height, left = 62, top = 40, bottom = h - 34, right = w - 34;
        if (series) return [[[left, (top + bottom) / 2], [left, top], [right, top], [right, bottom], [left, bottom], [left, (top + bottom) / 2]]];
        const gap = (right - left - 60) / Math.max(count, 1);
        return state.R.map((_, i) => {
            const x = left + 60 + gap * (i + 0.5);
            return [[left, (top + bottom) / 2], [left, top], [x, top], [x, bottom], [left, bottom], [left, (top + bottom) / 2]];
        });
    }

    function pointAt(path, distance) {
        for (let i = 0; i < path.length - 1; i++) {
            const [ax, ay] = path[i], [bx, by] = path[i + 1];
            const length = Math.hypot(bx - ax, by - ay);
            if (distance <= length) return [ax + (bx - ax) * distance / length, ay + (by - ay) * distance / length];
            distance -= length;
        }
        return path[path.length - 1];
    }

    const pathLength = path => path.slice(1).reduce((sum, p, i) => sum + Math.hypot(p[0] - path[i][0], p[1] - path[i][1]), 0);

    function resistor(ctx, x, y, label, vertical) {
        ctx.fillStyle = '#fff';
        ctx.strokeStyle = COLORS.navy;
        ctx.lineWidth = 1.6;
        const [w, h] = vertical ? [20, 54] : [54, 20];
        ctx.fillRect(x - w / 2, y - h / 2, w, h);
        ctx.strokeRect(x - w / 2, y - h / 2, w, h);
        if (vertical) text(ctx, label, x + 16, y + 4, COLORS.navy, 'left');
        else text(ctx, label, x, y - 18);
    }

    function draw() {
        const { ctx } = view;
        if (!view.width) return;
        clear(view);
        const all = paths();
        ctx.lineWidth = 2;
        all.forEach((path, i) => {
            ctx.strokeStyle = state.open[i] ? COLORS.grey : COLORS.navy;
            ctx.beginPath();
            path.forEach(([x, y], k) => (k ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
            ctx.stroke();
        });

        const middle = (40 + view.height - 34) / 2;
        ctx.fillStyle = COLORS.bg;
        ctx.strokeStyle = COLORS.navy;
        ctx.beginPath(); ctx.arc(62, middle, 20, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
        text(ctx, `${trim(state.U)} V`, 62, middle + 4, COLORS.navy, 'center', 11);

        if (series) {
            const span = (view.width - 34 - 62) / (count + 1);
            state.R.forEach((r, i) => resistor(ctx, 62 + span * (i + 1), 40, `R${i + 1} = ${trim(r)} Ω`, false));
        } else {
            all.forEach((path, i) => {
                const x = path[2][0];
                ctx.fillStyle = COLORS.bg;
                if (state.open[i]) { ctx.fillRect(x - 4, 62, 8, 18); text(ctx, 'ouvert', x + 8, 76, COLORS.red, 'left'); }
                resistor(ctx, x, middle, `R${i + 1}`, true);
                text(ctx, si(currents[i] ?? 0, 'A'), x + 16, middle + 22, palette[i], 'left', 11);
            });
        }

        // Le courant dessine des charges qui avancent : plus il est fort, plus elles sont rapides
        all.forEach((path, i) => {
            if (!(currents[i] > 0)) return;
            const length = pathLength(path);
            ctx.fillStyle = palette[series ? 0 : i];
            for (let d = state.phase[i] % 34; d < length; d += 34) {
                const [x, y] = pointAt(path, d);
                ctx.beginPath(); ctx.arc(x, y, 3.2, 0, Math.PI * 2); ctx.fill();
            }
        });
    }

    view.redraw = draw;
    update();
    const reference = 48 / 10;
    animate(view.element, dt => {
        if (reducedMotion()) return;
        currents.forEach((c, i) => { state.phase[i] += dt * (40 + 260 * Math.sqrt(c / reference)); });
        draw();
    });
}

// ── Régime transitoire RC, RL, RLC : un oscilloscope ─────────────────────────
function transientLab(config, lab) {
    const kind = config.kind || 'RC';
    const state = { U: config.voltage ?? 5, R: config.R ?? 1000, C: (config.C ?? 10) * 1e-6, L: (config.L ?? 100) * 1e-3, discharge: false, progress: 0, data: null };
    const view = lab.canvas(260, `Oscilloscope du circuit ${kind}`);

    lab.slider('Tension U', 1, 24, 0.5, state.U, 'V', v => { state.U = v; solve(); });
    lab.slider('R', 10, 10000, 10, state.R, 'Ω', v => { state.R = v; solve(); });
    if (kind !== 'RL') lab.slider('C', 1, 1000, 1, state.C * 1e6, 'µF', v => { state.C = v * 1e-6; solve(); });
    if (kind !== 'RC') lab.slider('L', 1, 2000, 1, state.L * 1e3, 'mH', v => { state.L = v * 1e-3; solve(); });
    if (kind !== 'RLC') lab.toggle('Décharge (U coupée)', false, v => { state.discharge = v; solve(); });
    lab.button('Relancer', () => { state.progress = 0; });

    function duration() {
        if (kind === 'RC') return 6 * state.R * state.C;
        if (kind === 'RL') return 6 * state.L / state.R;
        const omega0 = 1 / Math.sqrt(state.L * state.C);
        const alpha = state.R / (2 * state.L);
        const decay = alpha > omega0 ? alpha - Math.sqrt(alpha * alpha - omega0 * omega0) : alpha;
        return Math.max(5 / decay, 12 * Math.PI / omega0 > 5 / decay ? Math.min(12 * Math.PI / omega0, 5 / decay) : 0);
    }

    function derivative([q, i], source) {
        if (kind === 'RC') return [(source - q / state.C) / state.R, 0];
        if (kind === 'RL') return [0, (source - state.R * i) / state.L];
        return [i, (source - state.R * i - q / state.C) / state.L];
    }

    function solve() {
        const steps = 600;
        const T = duration();
        const h = T / steps;
        const source = state.discharge ? 0 : state.U;
        let y = state.discharge ? (kind === 'RC' ? [state.C * state.U, 0] : [0, state.U / state.R]) : [0, 0];
        const u = [], current = [];
        const add = (a, b, f) => [a[0] + b[0] * f, a[1] + b[1] * f];
        for (let n = 0; n <= steps; n++) {
            const i = kind === 'RC' ? (source - y[0] / state.C) / state.R : y[1];
            u.push(kind === 'RL' ? state.R * i : y[0] / state.C);
            current.push(i);
            const k1 = derivative(y, source), k2 = derivative(add(y, k1, h / 2), source), k3 = derivative(add(y, k2, h / 2), source), k4 = derivative(add(y, k3, h), source);
            y = [y[0] + h / 6 * (k1[0] + 2 * k2[0] + 2 * k3[0] + k4[0]), y[1] + h / 6 * (k1[1] + 2 * k2[1] + 2 * k3[1] + k4[1])];
        }
        state.data = { u, current, T };
        state.progress = 0;
        lab.clearReadouts();
        if (kind === 'RC') lab.readout('Constante de temps τ = RC', si(state.R * state.C, 's'));
        if (kind === 'RL') lab.readout('Constante de temps τ = L/R', si(state.L / state.R, 's'));
        if (kind === 'RLC') {
            const omega0 = 1 / Math.sqrt(state.L * state.C);
            const Q = Math.sqrt(state.L / state.C) / state.R;
            lab.readout('Fréquence propre f₀', si(omega0 / (2 * Math.PI), 'Hz'));
            lab.readout('Facteur de qualité Q', Q.toFixed(2));
            lab.readout('Régime', Q > 0.5 ? 'pseudo-périodique' : Q === 0.5 ? 'critique' : 'apériodique');
        }
        lab.readout('Courant initial', si(current[0], 'A'));
        lab.readout('Durée affichée', si(T, 's'));
        draw();
    }

    function curve(ctx, values, area, max, color, count) {
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.beginPath();
        for (let i = 0; i < count; i++) {
            const x = area.x + area.w * i / (values.length - 1);
            const y = area.y + area.h / 2 - (values[i] / max) * (area.h / 2) * 0.92;
            i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
        }
        ctx.stroke();
    }

    function draw() {
        const { ctx } = view;
        if (!view.width || !state.data) return;
        clear(view);
        const area = { x: 44, y: 16, w: view.width - 60, h: view.height - 44 };
        ctx.strokeStyle = COLORS.line;
        ctx.lineWidth = 1;
        for (let i = 0; i <= 10; i++) { const x = area.x + area.w * i / 10; ctx.beginPath(); ctx.moveTo(x, area.y); ctx.lineTo(x, area.y + area.h); ctx.stroke(); }
        for (let i = 0; i <= 8; i++) { const y = area.y + area.h * i / 8; ctx.beginPath(); ctx.moveTo(area.x, y); ctx.lineTo(area.x + area.w, y); ctx.stroke(); }
        const { u, current, T } = state.data;
        const uMax = Math.max(...u.map(Math.abs), 1e-9), iMax = Math.max(...current.map(Math.abs), 1e-12);
        const count = Math.max(2, Math.floor(state.progress * u.length));
        curve(ctx, u, area, uMax, COLORS.blue, count);
        curve(ctx, current, area, iMax, COLORS.amber, count);
        text(ctx, `${kind === 'RL' ? 'uR' : 'uC'} (max ${si(uMax, 'V')})`, area.x + 6, area.y + 12, COLORS.blue, 'left', 11);
        text(ctx, `i (max ${si(iMax, 'A')})`, area.x + 6, area.y + 26, COLORS.amber, 'left', 11);
        text(ctx, `temps : ${si(T, 's')} pour toute la largeur`, area.x + area.w, view.height - 8, COLORS.grey, 'right', 11);
    }

    view.redraw = draw;
    solve();
    animate(view.element, dt => {
        if (state.progress >= 1) return;
        state.progress = reducedMotion() ? 1 : Math.min(1, state.progress + dt / 3);
        draw();
    });
}

// ── Fentes d'Young, particule par particule ──────────────────────────────────
function youngLab(config, lab) {
    const state = { slit: config.slit ?? 4, V: config.visibility ?? 1, rate: 60, running: false, impacts: [], accumulator: 0, showCurve: true };
    const view = lab.canvas(270, "Figure d'interférence construite impact par impact");

    const density = x => Math.exp(-x * x * 2.2) * (1 + state.V * Math.cos(2 * Math.PI * state.slit * x));
    const sample = () => {
        for (let i = 0; i < 400; i++) {
            const x = -1.6 + Math.random() * 3.2;
            if (Math.random() * 2 < density(x)) return x;
        }
        return 0;
    };

    lab.slider('Écart entre les fentes', 1, 8, 0.5, state.slit, '', v => { state.slit = v; state.impacts = []; update(); });
    lab.slider('Visibilité V (information de chemin)', 0, 1, 0.05, state.V, '', v => { state.V = v; state.impacts = []; update(); });
    lab.slider('Débit', 5, 400, 5, state.rate, '/s', v => { state.rate = v; });
    lab.toggle('Afficher la loi de probabilité', true, v => { state.showCurve = v; draw(); });
    const run = lab.button('Envoyer des particules', () => { state.running = !state.running; run.textContent = state.running ? 'Pause' : 'Envoyer des particules'; });
    lab.button('Une seule', () => { state.impacts.push([sample(), Math.random()]); update(); }, true);
    lab.button('Effacer', () => { state.impacts = []; update(); }, true);

    function update() { lab.readout('Impacts comptés', String(state.impacts.length)); lab.readout('Visibilité V', state.V.toFixed(2)); draw(); }

    function draw() {
        const { ctx } = view;
        if (!view.width) return;
        clear(view);
        const left = 20, width = view.width - 40, screenBottom = 110, top = 130, height = view.height - 150;
        const toX = x => left + (x + 1.6) / 3.2 * width;
        ctx.fillStyle = COLORS.navy;
        ctx.fillRect(left, 10, width, screenBottom - 10);
        ctx.fillStyle = '#F5D76E';
        state.impacts.slice(-6000).forEach(([x, y]) => ctx.fillRect(toX(x), 12 + y * (screenBottom - 16), 1.6, 1.6));

        const bins = new Array(80).fill(0);
        state.impacts.forEach(([x]) => { const i = Math.floor((x + 1.6) / 3.2 * 80); if (i >= 0 && i < 80) bins[i]++; });
        const peak = Math.max(...bins, 1);
        ctx.fillStyle = COLORS.blue;
        bins.forEach((value, i) => { const h = value / peak * height; ctx.fillRect(left + i * width / 80, top + height - h, width / 80 - 1, h); });

        if (state.showCurve) {
            ctx.strokeStyle = COLORS.red;
            ctx.lineWidth = 2;
            ctx.beginPath();
            for (let i = 0; i <= 400; i++) {
                const x = -1.6 + 3.2 * i / 400;
                const y = top + height - density(x) / 2 * height;
                i ? ctx.lineTo(toX(x), y) : ctx.moveTo(toX(x), y);
            }
            ctx.stroke();
        }
    }

    view.redraw = draw;
    update();
    animate(view.element, dt => {
        if (!state.running) return;
        state.accumulator += state.rate * dt;
        while (state.accumulator >= 1) { state.impacts.push([sample(), Math.random()]); state.accumulator--; }
        update();
    });
}

// ── Qubit sur la sphère de Bloch ─────────────────────────────────────────────
function qubitLab(config, lab) {
    const state = { theta: config.theta ?? 60, phi: config.phi ?? 45, counts: [0, 0] };
    const view = lab.canvas(270, 'Sphère de Bloch');
    const rad = d => d * Math.PI / 180;

    lab.slider('θ', 0, 180, 1, state.theta, '°', v => { state.theta = v; state.counts = [0, 0]; update(); });
    lab.slider('φ', 0, 360, 1, state.phi, '°', v => { state.phi = v; state.counts = [0, 0]; update(); });
    const measure = n => { const p0 = Math.cos(rad(state.theta) / 2) ** 2; for (let i = 0; i < n; i++) state.counts[Math.random() < p0 ? 0 : 1]++; update(); };
    lab.button('Mesurer 1 fois', () => measure(1));
    lab.button('Mesurer 1000 fois', () => measure(1000), true);
    lab.button('Effacer', () => { state.counts = [0, 0]; update(); }, true);

    function update() {
        const p0 = Math.cos(rad(state.theta) / 2) ** 2;
        const total = state.counts[0] + state.counts[1];
        lab.readout('|α|² = P(0), théorique', p0.toFixed(3));
        lab.readout('|β|² = P(1), théorique', (1 - p0).toFixed(3));
        lab.readout('Mesures', String(total));
        lab.readout('Fréquence observée de 0', total ? (state.counts[0] / total).toFixed(3) : '—');
        draw();
    }

    function draw() {
        const { ctx } = view;
        if (!view.width) return;
        clear(view);
        const cx = Math.min(view.width * 0.32, 150), cy = view.height / 2, r = 100;
        ctx.strokeStyle = COLORS.navy; ctx.lineWidth = 1.5;
        ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.stroke();
        ctx.strokeStyle = COLORS.grey; ctx.lineWidth = 1;
        ctx.beginPath(); ctx.ellipse(cx, cy, r, r * 0.3, 0, 0, Math.PI * 2); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(cx, cy - r); ctx.lineTo(cx, cy + r); ctx.stroke();
        text(ctx, '|0⟩', cx, cy - r - 6); text(ctx, '|1⟩', cx, cy + r + 15);

        const t = rad(state.theta), p = rad(state.phi);
        const x = Math.sin(t) * Math.sin(p), y = Math.cos(t), z = Math.sin(t) * Math.cos(p);
        const px = cx + x * r, py = cy - y * r + z * r * 0.3;
        ctx.strokeStyle = COLORS.blue; ctx.lineWidth = 3;
        ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(px, py); ctx.stroke();
        ctx.fillStyle = COLORS.blue; ctx.beginPath(); ctx.arc(px, py, 5.5, 0, Math.PI * 2); ctx.fill();

        const total = state.counts[0] + state.counts[1];
        const barX = cx + r + 60, barWidth = 46, base = view.height - 40, maxHeight = 170;
        [0, 1].forEach(i => {
            const value = total ? state.counts[i] / total : 0;
            const h = value * maxHeight;
            ctx.fillStyle = i ? COLORS.amber : COLORS.blue;
            ctx.fillRect(barX + i * 80, base - h, barWidth, h);
            text(ctx, `|${i}⟩`, barX + i * 80 + barWidth / 2, base + 16);
            text(ctx, value.toFixed(2), barX + i * 80 + barWidth / 2, base - h - 6);
        });
    }

    view.redraw = draw;
    update();
}

// ── Tracé de fonction à curseurs : le labo générique de toutes les matières ──
const MATH_FUNCTIONS = new Map(Object.entries({
    sin: Math.sin, cos: Math.cos, tan: Math.tan, asin: Math.asin, acos: Math.acos, atan: Math.atan, sinh: Math.sinh, cosh: Math.cosh, tanh: Math.tanh,
    exp: Math.exp, ln: Math.log, log: Math.log10, sqrt: Math.sqrt, abs: Math.abs, floor: Math.floor, ceil: Math.ceil, sign: Math.sign,
}));

/** Analyseur d'expressions : jamais de eval, seuls les fonctions listées et les noms déclarés sont acceptés. */
export function compileExpression(source, names) {
    const tokens = String(source).match(/\d+\.?\d*(?:[eE][-+]?\d+)?|\.\d+|[a-zA-Zα-ωΑ-Ω_]\w*|[-+*/^(),]/g) || [];
    if (tokens.join('') !== String(source).replace(/\s+/g, '')) throw new Error(`Expression invalide : ${source}`);
    let position = 0;
    const peek = () => tokens[position];
    const take = () => tokens[position++];
    const expect = token => { if (take() !== token) throw new Error(`« ${token} » attendu dans : ${source}`); };

    function expression() {
        let left = term();
        while (peek() === '+' || peek() === '-') {
            const op = take(), a = left, b = term();
            left = op === '+' ? env => a(env) + b(env) : env => a(env) - b(env);
        }
        return left;
    }
    function term() {
        let left = unary();
        while (peek() === '*' || peek() === '/') {
            const op = take(), a = left, b = unary();
            left = op === '*' ? env => a(env) * b(env) : env => a(env) / b(env);
        }
        return left;
    }
    function unary() {
        if (peek() === '-') { take(); const a = unary(); return env => -a(env); }
        if (peek() === '+') { take(); return unary(); }
        return power();
    }
    function power() {
        const base = primary();
        if (peek() !== '^') return base;
        take();
        const exponent = unary();
        return env => Math.pow(base(env), exponent(env));
    }
    function primary() {
        const token = take();
        if (token === undefined) throw new Error(`Expression incomplète : ${source}`);
        if (token === '(') { const value = expression(); expect(')'); return value; }
        if (/^[\d.]/.test(token)) { const number = parseFloat(token); return () => number; }
        if (!/^[a-zA-Zα-ωΑ-Ω_]/.test(token)) throw new Error(`Symbole inattendu « ${token} » dans : ${source}`);
        if (peek() === '(') {
            const fn = MATH_FUNCTIONS.get(token);
            if (!fn) throw new Error(`Fonction inconnue : ${token}`);
            take();
            const argument = expression();
            expect(')');
            return env => fn(argument(env));
        }
        if (token === 'pi') return () => Math.PI;
        if (token === 'e' && !names.includes('e')) return () => Math.E;
        if (!names.includes(token)) throw new Error(`Nom inconnu : ${token}`);
        return env => env.get(token);
    }

    const compiled = expression();
    if (position !== tokens.length) throw new Error(`Expression invalide : ${source}`);
    return compiled;
}

/** « a:0:5:1, b:1:10:2 » : nom, minimum, maximum, valeur de départ. */
function parseParams(source) {
    return String(source || '').split(',').map(part => part.trim()).filter(Boolean).map(part => {
        const [name, min, max, value] = part.split(':').map(s => s.trim());
        if (!name || [min, max].some(v => Number.isNaN(+v))) throw new Error(`Curseur mal décrit : ${part} (attendu nom:min:max:valeur)`);
        return { name, min: +min, max: +max, value: value === undefined ? (+min + +max) / 2 : +value };
    });
}

function plotLab(config, lab) {
    const params = parseParams(config.params);
    const names = ['x', ...params.map(p => p.name)];
    const curves = [config.expr, config.expr2].filter(Boolean).map(e => ({ label: e, fn: compileExpression(e, names) }));
    if (curves.length === 0) throw new Error('Aucune expression à tracer.');
    const xMin = config.xmin ?? -10, xMax = config.xmax ?? 10;
    if (!(xMax > xMin)) throw new Error('xmax doit être supérieur à xmin.');

    const env = new Map(params.map(p => [p.name, p.value]));
    const view = lab.canvas(280, `Tracé de ${curves.map(c => c.label).join(' et ')}`);
    params.forEach(p => lab.slider(p.name, p.min, p.max, (p.max - p.min) / 200, p.value, '', v => { env.set(p.name, v); draw(); }));

    function draw() {
        const { ctx } = view;
        if (!view.width) return;
        clear(view);
        const area = { x: 44, y: 14, w: view.width - 58, h: view.height - 44 };
        const points = curves.map(curve => Array.from({ length: 501 }, (_, i) => {
            env.set('x', xMin + (xMax - xMin) * i / 500);
            const y = curve.fn(env);
            return Number.isFinite(y) ? y : null;
        }));
        const values = points.flat().filter(v => v !== null);
        let yMin = config.ymin ?? Math.min(...values, 0), yMax = config.ymax ?? Math.max(...values, 0);
        if (yMax - yMin < 1e-9) { yMin -= 1; yMax += 1; }
        const toX = x => area.x + (x - xMin) / (xMax - xMin) * area.w;
        const toY = y => area.y + area.h - (y - yMin) / (yMax - yMin) * area.h;

        ctx.strokeStyle = COLORS.line; ctx.lineWidth = 1;
        ctx.strokeRect(area.x, area.y, area.w, area.h);
        ctx.strokeStyle = COLORS.grey;
        if (yMin < 0 && yMax > 0) { ctx.beginPath(); ctx.moveTo(area.x, toY(0)); ctx.lineTo(area.x + area.w, toY(0)); ctx.stroke(); }
        if (xMin < 0 && xMax > 0) { ctx.beginPath(); ctx.moveTo(toX(0), area.y); ctx.lineTo(toX(0), area.y + area.h); ctx.stroke(); }
        text(ctx, trim(+yMax.toFixed(2)), area.x - 4, area.y + 10, COLORS.grey, 'right', 10);
        text(ctx, trim(+yMin.toFixed(2)), area.x - 4, area.y + area.h, COLORS.grey, 'right', 10);
        text(ctx, trim(xMin), area.x, area.y + area.h + 14, COLORS.grey, 'center', 10);
        text(ctx, trim(xMax), area.x + area.w, area.y + area.h + 14, COLORS.grey, 'center', 10);

        [COLORS.blue, COLORS.red].forEach((color, c) => {
            if (!points[c]) return;
            ctx.strokeStyle = color; ctx.lineWidth = 2.2;
            ctx.beginPath();
            let pen = false;
            points[c].forEach((y, i) => {
                if (y === null) { pen = false; return; }
                const x = xMin + (xMax - xMin) * i / 500;
                pen ? ctx.lineTo(toX(x), toY(y)) : ctx.moveTo(toX(x), toY(y));
                pen = true;
            });
            ctx.stroke();
            text(ctx, curves[c].label, area.x + 8, area.y + 14 + c * 15, color, 'left', 11);
        });
    }

    view.redraw = draw;
    draw();
}

const LABS = { 'dc-circuit': circuitLab, transient: transientLab, young: youngLab, qubit: qubitLab, plot: plotLab };
