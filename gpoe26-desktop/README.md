# GPOE26 Étude — l'application bureau

L'environnement d'étude complet : lire son cours, avancer son parcours, faire ses
exercices — **y compris sans réseau**. Le Web reste la surface légère pour qui n'a pas
installé l'application ; ici, tout est disponible hors ligne.

Bâti sur [Eclipse Theia](https://theia-ide.org), conçu pour être adapté sans être forké.
On en hérite Monaco, le *plugin host* en processus séparé et l'empaquetage Electron ; on
n'écrit que ce qui relève de GPOE26.

## Ce qu'il y a dedans

```
extensions/gpoe26-core      Client du harness, identité, magasin local, synchronisation
extensions/gpoe26-lecture   Le canvas de lecture
plugins/gpoe26-python       Premier type d'exercice — isolé, voir plus bas
exemples/exercice-python    Un exercice à ouvrir pour essayer
browser-app                 Cible navigateur — pour développer et vérifier
electron-app                Cible installable — Windows et Mac
```

**Extensions Theia** (dans le processus) pour notre interface ; les **types d'exercice**
sont des *plugins* (API VS Code, processus séparé). La frontière passe entre ce que nous
écrivons et ce qui peut planter sans emporter l'atelier.

## Construire et lancer

```bash
npm install
npx tsc -b extensions/gpoe26-core extensions/gpoe26-lecture
npm run build  --workspace browser-app
npm start      --workspace browser-app     # http://localhost:3000
```

Pour l'application installable, sur une machine Windows ou Mac :

```bash
npm run build   --workspace electron-app
npm run package --workspace electron-app   # produit dist/
```

⚠️ **L'empaquetage ne se fait pas sur Linux**, et pas seulement pour des raisons
techniques : les binaires doivent être **signés** — certificat Authenticode côté Windows,
notarisation côté Apple — faute de quoi le système affiche un avertissement que les
familles interprètent, à juste titre, comme un danger. Voir `electron-builder.yml`.

## Configuration

Les adresses des services se règlent sans recompiler, dans la section `theia.frontend.config`
du `package.json` de l'application :

| Clé | Défaut |
|---|---|
| `gpoe26.harnessUrl` | `http://localhost:5200` |
| `gpoe26.userUrl` | `http://localhost:5100` |

Les données de l'élève vont dans `~/.gpoe26` (ou `GPOE26_DATA_DIR`) : un fichier par cours,
une file d'attente, un fichier d'identité en `0600`.

## Le hors ligne, en trois décisions

**Le cours vient du réseau si possible, du disque sinon.** L'ordre compte : l'élève voit
une version à jour quand il en a une, et retombe sur le cache sans qu'on le prévienne
d'un échec — c'est le fonctionnement attendu, pas une dégradation. Un bandeau discret dit
d'où vient le cours, sans quoi l'élève ne comprendrait pas pourquoi la correction publiée
la veille n'y figure pas.

**Chaque signal porte sa date.** Une séance d'hier soir remontée ce matin serait sinon
datée de la synchronisation, et ferait apparaître une nuit entière de révision. Le serveur
applique ses règles — trente minutes d'inactivité, quatre-vingt-dix secondes de crédit
maximal entre deux signaux — à l'heure déclarée.

**La file n'est purgée que de ce que le serveur confirme.** Perdre du travail est pire que
le remonter deux fois ; ces signaux sont idempotents côté serveur.

## Les exercices de code, et pourquoi deux enveloppes

Un type d'exercice est un **plugin** (API VS Code), pas une extension : il tourne dans
l'hôte des plugins, un processus séparé de l'atelier. Le cœur ne le cite nulle part — on
en ajoute un en le déposant dans `plugins/`.

Le code de l'élève, lui, tourne un cran plus loin encore : dans un **fil d'exécution** du
plugin, avec un délai de dix secondes. Ce n'est pas une précaution de confort. On ne peut
pas interrompre du JavaScript ni du WASM synchrone : aucun minuteur ne reprend la main
tant que le code tourne. Seul `worker.terminate()` y met fin, et il faut donc que ce code
tourne dans un fil qu'on puisse tuer. Un élève qui écrit `while True: pass` fige son
exercice — mesuré, interrompu à 8 secondes — pas son application.

Python vient de **Pyodide**, en local : les tests tournent sur la machine de l'élève,
sans réseau, et le système de fichiers que voit son code est virtuel.

⚠️ **Ce qui tourne chez l'élève est falsifiable.** Un verdict produit sur son appareil ne
vaut pas une note vérifiée : il remonte marqué `selfAssessed`, et le bilan parent le dit
en toutes lettres. La réexécution serveur reste à faire.

Pour essayer : ouvrir `exemples/exercice-python` comme dossier de travail, puis Ctrl+Entrée.

## Ce qui reste à faire

- La **réexécution serveur** des tests, pour qu'un résultat compte comme une note vérifiée.
- D'autres types d'exercice : SQL, C, mathématiques symboliques.
- Le **rail du parcours** et le **panneau du répétiteur** — le client sait déjà jouer un tour en flux, il manque l'interface.
- Le **trousseau du système** pour le jeton de rafraîchissement : aujourd'hui un fichier en `0600`, ce qui protège des autres comptes de la machine mais pas d'un logiciel malveillant qui tournerait sous celui de l'élève. `safeStorage` d'Electron est le bon outil, et il n'existe que sur la cible Electron.
