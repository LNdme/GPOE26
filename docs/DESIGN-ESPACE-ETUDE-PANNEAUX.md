# Conception — Espace d'étude à panneaux (« workspace »)

Date : 2026-11-14 · Branche : `feat/site-admin-navbar` · Périmètre : espace **élève** d'abord, **web d'abord** (l'app Windows MAUI hérite ensuite de la même bibliothèque `GPOE26.UI`).

## 1. Le problème

L'interface actuelle (phase 3) est en pages : on navigue de `/app/cours` vers `/app/cours/{id}`, puis un tiroir plein écran (`.tu-drawer`, 460 px fixes) s'ouvre par-dessus pour le tuteur IA. Deux frottements :

1. L'élève **ne peut pas** lire le cours et poser sa question au tuteur avec son cours sous les yeux : le tiroir recouvre le texte (sur grand écran il réserve la place, mais sans pouvoir l'ajuster ni fermer le sommaire pour gagner de la largeur).
2. Les agents tournent déjà en arrière-plan (mise en forme du PDF, suggestions de labos, horloge d'étude, corrections), mais leur état est noyé dans la page. Rien ne montre « l'app derrière ».

Une partie de tes amis a raison : la plupart des écrans d'une plateforme d'étude se ramènent à **lire** quelque chose **pendant qu'un agent travaille** (tuteur, correction, plan du jour). L'UI devrait rendre ça explicite : pas des pages, des **espaces de travail à panneaux**, chacun agrandi, réduit ou fermé par l'utilisateur.

## 2. Le concept

Un **espace d'étude** = un écran plein, sans pages internes, composé de panneaux :

```
┌──────┬─────────────────────────────────┬──────────────────┐
│ rail │  Panneau de lecture             │  Panneau agent   │
│ (icô-│  Cours « Mruv », Markdown       │  Tuteur IA       │
│  nes)│  (ou photo de l'exo)            │  conversation +  │
│      │  sommaire « Sur cette page »    │  « L'agent lit… »│
│      ├────╌╌╌╌╌╌ poignée ╌╌╌╌╌╌───────┤  (état des tâches)│
│      │  Panneau d'exercice             │                  │
│      │  Évaluation / labo en cours     │                  │
└──────┴─────────────────────────────────┴──────────────────┘
```

Chaque panneau a un en-tête avec trois actions : **réduire** (le panneau se replie en onglet latéral étroit), **agrandir** (il prend toute la largeur), **fermer** (il disparaît de l'espace). Une **poignée de redimensionnement** entre panneaux adjacents ajuste leur largeur à la souris, au doigt ou au clavier.

Les premiers panneaux livrés (lot 1) :

| Panneau | Contenu | Source actuelle |
|---|---|---|
| Lecture | Cours Markdown/PDF + sommaire | `CourseReader.razor` (réutilisé tel quel) |
| Tuteur IA | Discussion du cours, photo d'exo, épinglages | `TutorPanel.razor` (réutilisé tel quel) |

Lot 2 (ensuite) : Évaluation/laboratoire en cours d'épreuve, Plan d'études du jour, Mémoire du tuteur (« ce que les agents retiennent »).

## 3. Ce qui change, ce qui ne change pas

**Ne bouge pas** : les services, les API, le modèle d'abonnement, le site public, les espaces enseignant/parent, les tests. La bibliothèque `GPOE26.UI` reste sans dépendance ASP.NET (contrainte MAUI). La connexion persistante, le jeton renouvelé et `ISessionStore` non plus.

**Change** :

- Nouveau composant `WorkspaceLayout` (bibliothèque `GPOE26.UI`, partage Web/MAUI) qui remplace `AppLayout` **uniquement sur la route `/app/cours/{id}`** dans le lot 1. Les autres écrans gardent `AppLayout`.
- `.tu-drawer` (position `fixed`, 460 px figés) est retiré au profit du panneau tuteur redimensionnable, **tout comme le bouton flottant « Tuteur IA »** : un panneau fermé rouvre depuis la barre de pastilles en haut de l'espace (« Tuteur IA ») ou depuis le bouton « Demander au tuteur IA » du sommaire.
- Le sommaire « Sur cette page » devient un panneau réduit/agrandi au lieu du mode `no-toc` actuel.

## 4. Comportement des panneaux

### Actions

| Action | Effet | Clavier |
|---|---|---|
| Réduire | Le panneau se replie en un rail étroit (icône + libellé vertical) en bord de l'espace | `Entrée` sur le rail |
| Agrandir | Le panneau prend toute la largeur disponible, les autres passent derrière (onglets) | `Échap` restaure |
| Fermer | Le panneau quitte l'espace ; rouvrable depuis la barre d'onglets (`+`) | Bouton dans l'en-tête |
| Redimensionner | Tire la poignée ; 160 px minimum par panneau | Poignée `role="separator"`, flèches `←`/`→`, `Maj` pour un pas de 10 px |
| Réinitialiser | Double-clic sur la poignée → 50/50 | Bouton « Réinitialiser » (menu ⋯ de l'en-tête) |

### Persistance

La disposition (panneaux ouverts, largeurs, quel panneau est agrandi) est enregistrée **globale à l'espace élève** : l'élève règle une fois « Cours à gauche 60 % / tuteur à droite 40 % », ça vaut pour tous les cours. Clé `gpoe26.workspace`. Pas de données sensibles : pas besoin de chiffrement, et ça évite d'alourdir `SessionState`. La taille minimale et la valeur par défaut (55/45) vivent en CSS côté client ; l'état n'est commité côté serveur **qu'à la fin du glissement** pour ne pas déclencher un rendu Blazor par `mousemove`.

### État des agents visibles

L'en-tête du panneau de lecture affiche une pastille d'état quand un agent travaille : « Mise en forme du PDF… », « L'agent cherche des labos… ». Ces états existent déjà (`StructuringStatus == "Pending"`, polling `StartPolling`) : on les surface au lieu de les cacher dans la page. Le panneau tuteur affiche « Le tuteur écrit… » pendant `_busy`. C'est la couche « l'app tourne derrière » rendue visible.

## 5. Technique

### Côté Razor

```
GPOE26.UI/Components/Workspace/
  Workspace.razor            ← grille de panneaux, état (ouverts, parts, agrandi)
  Panel.razor                ← en-tête (titre, réduire/agrandir/fermer/menu) + corps
  Splitter.razor             ← poignée role="separator", @ref vers le JS
  WorkspaceState.cs          ← modèle + (dé)sérialisation JSON
```

`Workspace` rend une `display: grid` dont `grid-template-columns` est calculé depuis les parts persistées (ex. `1fr 6px 0.8fr`). Chaque `Panel` connaît ses voisins pour poser la poignée entre eux.

### Côté JS (nouveau module `workspace.js`, sur le modèle de `gpoe-app.js`)

```js
export function attachSplitter(el, index, dotNetRef)   // pointer events
export function setSizes(container, parts)              // CSS variables → grid
export function loadLayout() / saveLayout(json)         // localStorage
```

- Glissement géré **entièrement côté client** (CSS variable `--ga-pane-parts` → `grid-template-columns`) ; Blazor n'est rappelé qu'au `pointerup` (tolérant à l'échec : si le circuit coupe, la disposition locale reste correcte).
- Ajustements min/max en JS (160 px) et en CSS (`minmax()`), les deux pour ne pas mentir au clavier.
- `renderMath` (KaTeX) est re-déclenché à la fin du glissement pour la zone du cours, comme aujourd'hui après `OnAfterRenderAsync`.

### Responsive et MAUI

- **< 900 px** : les panneaux ne s'affichent plus côte à côte. Une barre d'onglets basse (« Cours », « Tuteur », « Épreuve ») montre un seul panneau à la fois ; les actions d'en-tête restent (fermer surtout). Mêmes composants, même état.
- **MAUI (phase 4)** : `workspace.js` fonctionne dans WebView ; la persistance passera par `ISessionStore` (SQLite) puisqu'il n'y aura pas forcément de `localStorage` partagé — prévoir la clé d'état derrière une interface `IWorkspaceStore` dès le lot 1 (implémentation web : `localStorage`).

### Points d'attention Blazor Server

- **Aucun `StateHasChanged` pendant le glissement** : c'est le piège n°1. Le JS mute le DOM, le serveur lit l'état final.
- Le panneau tuteur fermé **ne démonte pas** la discussion : l'état vit côté serveur (base `chatdb`) ; on ferme l'affichage, pas les données. Rouvrir = réafficher, `TutorPanel` se recharge.
- Prérendu : l'espace est de toute façon en circuit interactif unique sans prérendu (`App.razor`) — pas de flash de layout.

## 6. Maquette des écrans

`/app/cours/{id}` après le lot 1 (desktop) :

```
┌─rail─┬────────────────────────────────────────────┬────────────────────┐
│ 📚   │ Mruv — Cours de physique          ◐ ◦ — ✕  │ Tuteur IA   ◦ — ✕  │
├──────┼────────────────────────────────────────────┼────────────────────┤
│ 🏠   │ « Un mobile part de l'arrêt… »             │ Toi : est-ce que…  │
│ 📖●  │                                            │                    │
│ ✅   │ ## Vitesse moyenne                         │ Tuteur : oui, la   │
│ 🗓   │ ...                                        │ formule…           │
│ ✨   │ ─────────────────────                      │                    │
│      │ Sur cette page │ Vitesse moy. │ Accél…    │ [ Photo ] [ Envoyer]│
│      │        ◐ "L'agent met le cours en forme…"  │ ─── L'agent lit… ──│
│      ├────────────────────────────────────────────┤                    │
│      │  (lot 2 : panneau Épreuve d'entraînement)  │                    │
└──────┴────────────────────────────────────────────┴────────────────────┘
```

Mobile (< 900 px) : contenu plein écran + onglets en bas, pilule « Tuteur » avec badge quand il répond.

## 7. Découpage

Cohérent avec les lots établissement (chaque lot = compilable + testable) :

| Lot | Contenu | Vérification |
|---|---|---|
| 1 | `Workspace`/`Panel`/`Splitter` + persistance + route cours : lecture et tuteur en panneaux redimensionnables (fini le tiroir) | Build + 450 tests existants + essai navigateur : glisser, réduire, agrandir, fermer, recharger la page (disposition retrouvée) |
| 2 | Sommaire « Sur cette page » en panneau réduit ; état des agents dans les en-têtes ; raccourcis clavier | Essai navigateur (Playwright `scripts/ui-tutor-e2e.js` adapté) |
| 3 | Mobile : onglets bas, un panneau à la fois ; badge tuteur | Essai mobile (même script) |
| 4 | Panneau Épreuve (Composer) et Plan du jour en panneau ; élève assemblant son espace | Après retour d'usage des lots 1–3 |

Estimation indicative : lot 1 seul tient dans une session ou deux ; lots 2–3 similaires.

## 8. Risques

1. **Glissement saccadé côté Blazor Server** si on rebase l'état à chaque pixel → mitigé : CSS variables, commit au `pointerup`.
2. **Accessibilité** des poignées → `role="separator"`, `aria-valuenow/min/max`, clavier ; sans ça l'interface est inutilisable au clavier.
3. **KaTeX/fin de cours** : recalcul après resize manquant → formules déformées ; couvert par le re-render de fin de glissement.
4. **Régression sur `ui-tutor-e2e.js`** (sélecteurs `.tu-drawer`, bouton « Tuteur IA ») → mettre à jour les sélecteurs dans le même lot, pas après.
5. **Scope creep** : ne pas convertir enseignant/parent dans ce chantier ; la valeur est côté élève.

## 9. Décisions à confirmer avant de coder

1. Nom : « Espace d'étude » ? « Mon plan de travail » ? (Le code dira `Workspace`.)
2. La poignée remplace physiquement `.tu-drawer` (approche présentée) — OK, ou garder le tiroir comme solution de repli ?
3. La disposition est globale à l'espace élève (recommandé) — pas par cours. OK ?
4. On commence par le lot 1 tout de suite ou après merge de `feat/site-admin-navbar` ?
