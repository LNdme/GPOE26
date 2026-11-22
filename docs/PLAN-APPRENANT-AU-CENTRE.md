<!-- /autoplan restore point: C:/Users/Libert/.gstack/projects/LNdme-GPOE26/feat-site-admin-navbar-autoplan-restore-20261122-174740.md -->
# Plan : l'apprenant au centre (début du build)

> Écrit le 22 novembre 2026, revu par /autoplan le même jour (stratégie, design, ingénierie, DX). **Rien n'est codé.**
> Prémisses D1 à D4 et choix T1 à T5 validés par l'utilisateur le 22 novembre 2026 (§9). **Plan approuvé.**
> Ce plan prolonge `docs/PLAN-PUBLICS.md` (il termine P3c côté apprentissage) et `docs/PLAN-ETABLISSEMENT*.md`.

---

## 1. Le cap

**La personne possède son espace d'apprentissage. Les organisations et les familles l'accompagnent et gardent
leurs dossiers officiels. Les cours partagés évoluent de façon traçable.**

1. **Un compte = une personne.** Aucun rôle ne limite ce qu'elle peut apprendre.
2. **Accompagner n'est pas posséder.** Une famille ou une organisation paie, propose ou assigne, et voit
   l'évolution selon une règle écrite. Elle ne modifie pas le compte et ne lit jamais les discussions avec le tuteur.
3. **Proposé ≠ assigné.** (piste A, jalon 2)
4. **Le départ ne détruit rien.** La personne garde tout son historique. L'organisation garde les résultats de la
   période où la personne était là, jusqu'à une date limite, et ne voit rien de nouveau.
5. **Un cours partagé ne change jamais en silence.** (piste B, plan séparé `docs/PLAN-CONTENU-VIVANT.md`, à écrire,
   démarrable en parallèle : Cours, `GPOE26.Windows` et les pages de lecture et d'édition de cours)

## 2. Ce qui existe (vérifié dans le code)

| Sujet | Où | Constat |
|---|---|---|
| Appartenance à une classe | `ClassroomMember` (`ClassroomId`, `StudentId`, `JoinedAt`), clé primaire `(ClassroomId, StudentId)` (`UserSchema.cs:80`) | **supprimée** au départ (`AccessCodeService.cs:282`, `:295`, retrait par l'enseignant) ; 24 lectures dans 7 fichiers de User |
| Rôle dans une classe | `ClassroomService.RoleInAsync` (`:90-98`) | « élève » = une ligne existe ; lu par Cours (`UserDirectoryClient.cs:31`) et Chat (`InsightRules`) |
| Liste officielle d'un espace | `OrganizationStudent` (`Status`, `ActivatedAt`, `BirthDate` facultative), index unique `(OrganizationId, UserId)` (`UserSchema.cs:220`) | `Left` sans date ; places comptées sur `Status` (`SpaceService.cs:55`) |
| Réadmission | `StudentEndpoints.cs:91-93` (Left → Pending), puis code | **déjà cassée** : `ApplyAsync` (`AccessCodeService.cs:115-121`) voit `UserId` rempli et marque seulement le code comme utilisé |
| Code et rôle | `AccessCodeService.RoleFits` (`:83`) | code élève refusé à tout compte qui n'est pas `Student` |
| Mes classes | `GET /classrooms/mine` (`ClassroomEndpoints.cs:41`) | choisit enseignant ou élève selon `user.Role` ; lu par Quiz, Cours (`UserDirectoryClient.cs:83`) et Chat (`AgentContextService.cs:120`) |
| Mes évaluations | `AssignedToStudentAsync` (`AssessmentEndpoints.cs:204`) ; `/assessments/mine` (`:33`) | classes **actuelles** seulement ; `/mine` change de forme selon `IsInRole(Teacher)` |
| Composer | `SubmissionEndpoints.cs:40-45` | `Roles.Student` + évaluation assignée |
| Ma progression | `GET /progress/me` | `Roles.Student` ; 22 contrôles `Roles.*` dans Quiz, Chat, Cours |
| Progression d'une classe | `GET /progress/classrooms/{id}` | membres **actuels** (`MembersAsync`) |
| Notions fragiles | `InsightEndpoints.cs` ; `CourseMastery.WeakPoints` | état **courant**, réécrit en continu : impossible à dater |
| Enseignants d'une classe | `ClassroomTeacher.AssignedAt` | ligne effacée au retrait d'un enseignant |
| Copies et notes | `Quiz.Submissions` | jamais supprimées |
| Reprises | `User/Data/UserDataMigrations.cs` (`SchemaVersion`), colonnes dans `UserSchema.cs` | v2 à v4 jouées ; **v5 réservée ici** (P4 de PLAN-PUBLICS prendra v6) |
| Âge | — | aucune date de naissance sur `AppUser` |
| Tests | `GPOE26.Logic.Tests`, `Quiz.Tests`, `GPOE26.Tests` | code pur seulement ; aucun test de route ni de base ; scripts `scripts/test-user-api.sh`, `test-evaluations.sh` |
| Windows | `GPOE26.Windows` (WinUI, `net10.0-windows`) | hors de `GPOE26.slnx` ; `scripts/post-merge.sh` (Linux) construit la solution |
| Interface | `AppLayout.razor` (`Items(role)`, `LearnItems`), `Dashboard.razor` (switch sur le rôle), `ClassDetail.razor` (grille `ga-heat`, « — » = non évalué), composants `Loading`, `Alert`, `ga-empty`, `ga-card`, `ga-tag`, `ga-switch` (`AssessmentEditor.razor:229`), confirmation en ligne (`SpaceStudentsTab.razor`, `_confirmDelete`) | réutilisés tels quels |

## 3. Architecture du jalon 1

```
                      ┌──────────────────────────── User ─────────────────────────────┐
                      │ ClassroomMembers (Id, JoinedAt, LeftAt)  ← filtre EF LeftAt NULL │
  GPOE26.UI           │ OrganizationStudents (LeftAt, CHECK statut)                       │
  /app/historique ───▶│ SharingOptOuts (LearnerId, OrgId | GuardianId, Scope)             │
  ClassDetail         │ VisibilityRules (pur)  ◀── Visibility:RetentionMonths (12)        │
                      │ GET /classrooms/mine?as=learner|teacher[&include=former]           │
                      │ GET /classrooms/{id}/members?include=former  (+ portées visibles)  │
                      │ GET /me/history          PUT|DELETE /me/sharing/{kind}/{id}/{scope}│
                      └───────▲───────────────────────▲───────────────────────▲──────────┘
                              │ (1 appel, portées incluses)                    │
                    Quiz ─────┘ mes évaluations, composer,          Chat ─────┘ notions fragiles
                              progression classe / moi / enfant     (anciens membres exclus)
                    Cours ── /classrooms/mine?as=learner (cours des classes)
```

**Ordre de déploiement (obligatoire, M5)** : (1) Quiz, Cours et Chat demandent explicitement `as=learner` ou
`as=teacher` ; (2) User accepte les rôles mélangés ; (3) `LeftAt` et le filtre EF ; (4) visibilité et page.

### Cycle de vie d'une appartenance (T1)

```
  ClassroomMember (une ligne par période)
     [arrivée] ──▶ ACTIVE (LeftAt NULL) ──[départ, changement, retrait]──▶ TERMINÉE (LeftAt posé)
                       ▲                                                        │
                       └────────────── [retour] = NOUVELLE ligne ACTIVE ────────┘
  Impossible : deux lignes ACTIVES pour (classe, élève)  → index unique partiel WHERE "LeftAt" IS NULL

  OrganizationStudent (une ligne par personne et par espace, matricule unique)
     Pending ──[code]──▶ Active ──[départ / révocation]──▶ Left | Revoked (LeftAt posé, CHECK)
        ▲                                                        │
        └─────────────[réadmission par le gestionnaire]───────────┘   puis [code] ──▶ Active
  Impossible : LeftAt posé et statut Active (CHECK), place comptée deux fois (LockAsync)
```

### Règles de visibilité (`VisibilityRules`, fonction pure)

| Qui regarde | Portée `OfficialResults` (évaluations assignées) | `Progress` (maîtrise) | `WeakPoints` |
|---|---|---|---|
| la personne | tout, toujours | tout, toujours | tout, toujours |
| enseignant de la classe | membres actuels ; anciens si sa propre affectation (`AssignedAt`) chevauche la période, jusqu'à `LeftAt + RetentionMonths` | idem, sauf retrait | **membres actuels seulement** (H3) |
| gestionnaire de l'espace | idem, sans condition d'affectation | idem, sauf retrait | membres actuels seulement |
| parent (lien actif) | oui | oui, sauf retrait | oui, sauf retrait |
| après `LeftAt + RetentionMonths` | rien | rien | rien |

Une évaluation compte pour une personne si **sa fenêtre chevauche la période d'appartenance**
(`OpensAt ?? PublishedAt ≤ LeftAt ?? ∞` et `DueAt ?? ∞ ≥ JoinedAt`) **ou si la personne a déjà une copie** (H2 :
pas de régression pour un élève arrivé en cours d'année). Les anciens membres ne renvoient jamais `LastLoginAt` ni
`Level` (M1). Retrait possible seulement pour une personne **réputée majeure** (T3).

## 4. Lots du jalon 1

| Lot | Contenu | Fichiers |
|---|---|---|
| **J0 Socle** | `GPOE26.Windows.slnx` séparé (ne pas toucher `GPOE26.slnx` : Linux et `post-merge.sh`). Vérifier l'espace disque (12 Go libres le 22 nov.) avant `pg_dumpall`, sauvegarde dans `C:\Users\Libert\Documents\GPOE26-sauvegardes`, **essai de restauration** dans un conteneur temporaire. | `GPOE26.Windows.slnx` (nouveau), `README.md` |
| **J1 Apprendre sans rôle** | Quiz, Cours, Chat appellent `/classrooms/mine?as=learner` ou `?as=teacher` (C3). `/classrooms/mine` répond selon l'appartenance réelle, plus selon `Role`. `/assessments/mine` scindé : `/assessments/mine` (apprenant) et `/assessments/authored` (auteur) (H5). `/progress/me` et les tentatives ouverts à toute personne connectée, l'évaluation devant être assignée **comme apprenant** ; un auteur ne compose jamais sa propre évaluation. `RoleFits` : un code élève est accepté par tout compte (un enseignant peut rejoindre une classe pour apprendre). | `Quiz/Endpoints/AssessmentEndpoints.cs`, `SubmissionEndpoints.cs`, `ProgressEndpoints.cs`, `EndpointHelpers.cs`, `Quiz/Service/UserDirectoryClient.cs`, `Cours/Service/UserDirectoryClient.cs`, `Chat/Service/AgentContextService.cs`, `User/Endpoints/ClassroomEndpoints.cs`, `User/Service/AccessCodeService.cs`, `GPOE26.UI/Pages/Assessments.razor`, `TakeAssessment.razor`, `SubmissionView.razor`, `GPOE26.UI/Services/Apis.cs` |
| **J2 Appartenances datées** | `ClassroomMembers` : colonne `Id`, `LeftAt`, clé primaire sur `Id`, index unique partiel `(ClassroomId, StudentId) WHERE "LeftAt" IS NULL` (T1). Filtre EF `HasQueryFilter(m => m.LeftAt == null)` ; `IgnoreQueryFilters()` seulement dans l'historique et la visibilité (C1). `OrganizationStudents.LeftAt` + CHECK (`LeftAt` posé ⇔ statut `Left` ou `Revoked`), écrit au seul endroit `RemoveStudentAsync` (H4). Reprise **v5** dans `UserDataMigrations.cs` : `LeftAt` des fiches déjà `Left`/`Revoked` = date de la reprise (T5). Les trois suppressions deviennent `LeftAt = now`. `ApplyAsync` : une fiche non `Active` avec son compte est **réactivée** (statut, `LeftAt = null`, place recomptée sous `LockAsync`, retour en classe) (C2). | `User/Model/Classroom.cs`, `OrganizationSpace.cs`, `User/Data/UserContext.cs`, `UserSchema.cs`, `UserDataMigrations.cs`, `User/Service/AccessCodeService.cs`, `ClassroomService.cs`, `SpaceService.cs`, `User/Endpoints/ClassroomEndpoints.cs`, `StudentEndpoints.cs`, `OrganizationEndpoints.cs` |
| **J3 Visibilité** | `VisibilityRules` (pure). `SharingOptOuts` (CHECK : exactement un de `OrganizationId`, `GuardianId` ; index unique partiel). `Visibility:RetentionMonths` (12 par défaut) et `Visibility:Enabled` (interrupteur de retour à la vue « membres actuels »). `/classrooms/{id}/members?include=former` renvoie les portées visibles de chaque ancien membre (pas d'appel par élève, M2). `/parents/children` renvoie aussi les portées (H6). `GET /me/history`, `PUT`/`DELETE /me/sharing/{organization|guardian}/{id}/{progress|weak-points}`. Erreurs selon §6. Âge : T3. | `User/Service/VisibilityRules.cs` (nouveau), `User/Endpoints/SharingEndpoints.cs` (nouveau), `ClassroomEndpoints.cs`, `ParentEndpoints.cs`, `UserSchema.cs`, `appsettings.json` |
| **J4 Progression par période** | Quiz : mes évaluations selon la règle de chevauchement (§3). Progression d'une classe : membres actuels + section des anciens, limités par leurs portées ; moyennes et « à accompagner » calculés sur les membres **actuels** seulement. Parent : portées respectées. Chat : anciens membres exclus des notions fragiles. | `Quiz/Endpoints/AssessmentEndpoints.cs`, `ProgressEndpoints.cs`, `Quiz/Service/ProgressService.cs`, `UserDirectoryClient.cs`, `Chat/Endpoints/InsightEndpoints.cs` |
| **J5 Interface** | Voir §5. | `GPOE26.UI/Pages/HistoryPage.razor` (nouveau), `ClassDetail.razor`, `CodePage.razor`, `SpaceStudentsTab.razor`, `Layout/AppLayout.razor`, `Services/Apis.cs`, `Services/Labels.cs`, `wwwroot` (classe `.out`) |

### Avancement

- **J0 fait le 22 novembre 2026.**
  - `GPOE26.Windows.slnx` contient Windows et `GPOE26.UI` ; il se construit (0 erreur). `GPOE26.slnx` n'est pas touché. Le README est à jour.
  - Sauvegarde `toutes-les-bases-2026-11-22-avant-jalon1.sql` (949 Ko, 6 bases), faite depuis un conteneur temporaire sur le volume d'Aspire, avec le mot de passe lu dans les user-secrets.
  - Restauration essayée dans un conteneur jetable en mémoire : 0 erreur, mêmes chiffres (24 comptes, 2 appartenances, 2 fiches).
- **J1 fait le 22 novembre 2026, pas commité.**
  - **User.** `/classrooms/mine?as=learner|teacher|all` (`ClassroomViewRules`, 400 si la valeur est inconnue). `RoleFits` accepte un code élève pour tout compte. Un enseignant ou un parent qui apprend garde son établissement et son niveau.
  - **Quiz.**
    - `/assessments/authored` (auteur) est séparée de `/assessments/mine` (apprenant, toujours la même forme).
    - Les routes de l'apprenant (tentatives, brouillon, rendu, photos, analyse IA, `/progress/me`, détail) sont ouvertes à tout compte.
    - L'auteur ne compose jamais sa propre évaluation (`AssignmentRules`).
  - **Cours et Chat.** Ils demandent `as=all`, `as=teacher` ou `as=learner`.
  - **Interface.**
    - Évaluations, composition et copie passent à la casquette « learner ».
    - La copie s'affiche en mode correction quand elle est celle d'un autre, plus selon le rôle.
    - Un enseignant voit aussi la section « À composer dans vos classes ».
    - Un parent a l'entrée « Évaluations ».
  - **Tests.** `ClassroomViewTests` (15) et `AssignmentRulesTests` (3). La donnée de `RoleFits` est mise à jour. Résultats : 679 + 69 tests passent.
  - **Écarts.**
    - `/progress/students/{id}` (parent) garde `Roles.Parent`, et les routes d'auteur gardent `Roles.Teacher` : ce sont les politiques de P3c.
    - La section apprenant vue par un enseignant reste au tutoiement.
- **J2 à J5 faits le 22 novembre 2026** (même commit que J1).
  - **J2.**
    - `ClassroomMembers` : `Id`, `LeftAt`, index unique partiel, filtre EF.
    - Les trois suppressions deviennent `LeftAt`.
    - `OrganizationStudents.LeftAt`, posé seulement par `RemoveStudentAsync`.
    - Reprise v5, avec la contrainte `CK_OrganizationStudents_LeftAt`.
    - Réadmission réparée (C2) : le code réactive la fiche, dans `ApplyAsync` comme dans la connexion par matricule.
  - **J3.**
    - `VisibilityRules` (pure) et `VisibilityService` ; réglages `Visibility:RetentionMonths` et `Visibility:Enabled`.
    - `SharingOptOuts`, avec deux CHECK et deux index partiels ; `AppUser.BirthYear` dans le profil.
    - `/classrooms/mine?include=former` (`MyPeriods`) et `/classrooms/{id}/members?include=former`. Les anciens élèves y apparaissent sans niveau ni dernière connexion ; un enseignant ne voit que ceux qu'il a eus.
    - `/parents/children` renvoie aussi les portées.
    - `GET /me/history` et `PUT` / `DELETE /me/sharing/...`.
  - **J4.**
    - Quiz : `LearnerHistoryAsync` (classes quittées, `Former`) et `AssignmentRules.CountsFor` (chevauchement ou copie).
    - Grille de la classe : anciens élèves avec `OutOfPeriod` ; `ProgressHidden` quand la maîtrise n'est plus partagée.
    - Parent : sa vue respecte les retraits.
    - Copies : les anciens élèves gardent leur nom.
    - Chat : notions fragiles seulement pour les membres actuels qui les partagent, et pour l'enfant qui les partage.
  - **J5.**
    - Page `HistoryPage.razor` : cartes, interrupteurs avec confirmation, états, vouvoiement d'un adulte qui n'est pas élève. Icône `history`.
    - Grille : section « Anciens élèves », cellule « Hors période », maîtrise « non partagée », moyennes et « à accompagner » sur les membres actuels. Si la classe n'a plus que d'anciens élèves, la section est ouverte.
    - Confirmation du retrait d'un élève ; infobulles « Parti », « Retirer », « Réintégrer » ; année de naissance dans le profil.
  - **Tests.**
    - `VisibilityTests` et `BirthYearProfileTests`, `AssignmentRulesTests` (période).
    - Nouveau projet `User.Tests` (7 tests sur PostgreSQL) : reprise rejouée, départ puis réintégration, A → B → A, deux périodes en cours refusées, CHECK, majorité réputée.
    - Résultats : 702 tests de logique, 76 Quiz, 7 User.
  - **Essai sur la pile Aspire** (22 nov.).
    - La reprise v5 s'est appliquée à la vraie base.
    - 30 vérifications d'API passent avec des comptes de test, supprimés ensuite : `as` / `include` inconnus, départ, ancien élève, retraits, résultats officiels non retirables, réintégration (C2), grille.
    - Navigateur :
      - historique d'un élève mineur, sans interrupteur ;
      - grille avec ancien élève, après correction d'un bogue : une classe sans membre actuel cachait les anciens ;
      - retrait de la maîtrise avec confirmation et message ;
      - largeur 375 px sans défilement.
  - **Écarts.**
    - `User.Tests` lance PostgreSQL avec la commande `docker` plutôt qu'avec Testcontainers, dont le téléchargement depuis nuget.org échouait.
    - Les résultats ne sont pas affichés dans les cartes de « Mon historique » : un lien mène à « Mes évaluations », qui garde les classes quittées.
    - `/progress/students/{id}` garde `Roles.Parent` (politiques de P3c).
    - Quitter une classe sans quitter l'espace garde l'espace « actuel » dans l'historique.

## 5. Interface (J5, revue design)

**Page « Mon historique » (`/app/historique`)**, dans le menu par défaut de l'élève (`Items`) **et** dans
`LearnItems` (sinon un élève ne la voit jamais). Icône `history`.

```
  Mon historique
  ┌─ ga-card : Lycée X ─────────────────────────── [ga-tag vert « Actuel »] ┐
  │ Terminale C · depuis le 3 sept. 2026                                    │
  │ Ce que Lycée X voit                                                     │
  │   🔒 Résultats des évaluations assignées   partagés pendant l'inscription,│
  │                                            gardés jusqu'au …            │
  │   Maîtrise                                 [ga-switch role=switch]       │
  │   Notions fragiles                         [ga-switch role=switch]       │
  │ ▸ Mes résultats (replié)                                                │
  └─────────────────────────────────────────────────────────────────────────┘
  ┌─ ga-card : Collège Y ───────────── [ga-tag gris « Parti le 30 juin 2026 »] ┐
  │ Collège Y ne voit plus rien depuis le …  (si la conservation est passée) │
  │ ▸ Mes résultats                                                          │
  └──────────────────────────────────────────────────────────────────────────┘
```

| État | Affichage |
|---|---|
| chargement | `Loading` |
| vide | `ga-empty` : « Tu n'as pas encore rejoint de classe ou d'espace. Ton historique apparaîtra ici. » |
| erreur User | `Alert` error + « Réessayer » |
| partiel (Quiz ne répond pas) | cartes affichées, résultats remplacés par « Résultats indisponibles pour le moment » |
| mineur (T3) | switchs désactivés + « Demande à ton parent » |
| retrait | confirmation en ligne : « Lycée X ne verra plus ta maîtrise à partir de maintenant. Tu peux la partager à nouveau quand tu veux. » ; `Alert role=status` ; focus rendu au switch |
| retrait refusé | jamais de switch sur les résultats officiels : cadenas + texte |

**Grille de l'enseignant (`ClassDetail.razor`)** : les anciens élèves vont dans une section repliée « Anciens
élèves » sous la grille. Elle est exclue des filtres, des moyennes et de `NeedsHelp`. La date apparaît en `<small>`
sous le nom. Nouvelle cellule `.out`, hachurée avec un texte et un `aria-label` « Hors période ». Une portée
retirée s'affiche dans une cellule fusionnée « Non partagé par l'élève ». Une entrée est ajoutée à la légende.

**Départ** : la page du code (`CodePage.razor`) confirme « Tes résultats restent dans Mon historique » avec un
lien. Le bouton « Retirer » de l'enseignant prend une confirmation en ligne qui annonce : « Il quitte la classe ;
ses résultats restent visibles 12 mois ».

**Textes** : on tutoie l'élève et on vouvoie l'adulte (enseignant, parent, adulte qui retire un partage), avec
`Labels` à deux formes. On emploie le vocabulaire déjà présent dans l'interface (« Maîtrise », « Notions
fragiles »), jamais `Progress` ni `WeakPoints`. Les dates passent par `Labels.Date`, dans le fuseau de la personne
(lot B3).

**Mobile (360 px)** : les cartes s'empilent et les switchs restent alignés à droite. La grille garde son
défilement horizontal, sans colonne en plus.

## 6. Erreurs (format du dépôt : `{ "message": … }`)

| Cas | Code | Message | Que faire |
|---|---|---|---|
| retrait de `OfficialResults` | 400 | « Les résultats des évaluations assignées restent partagés tant que tu es inscrit. » | rien |
| retrait par une personne réputée mineure | 403 | « Ton parent doit faire ce changement. » | passer par le parent |
| portée inconnue / `include` inconnu / `as` inconnu | 400 | « Valeur inconnue : … (attendu : …) » | corriger l'appel |
| ancien membre hors conservation | 404 | « Cet élève n'est plus visible depuis le … » | rien |
| route d'auteur appelée par un apprenant | 403 | « Réservé à l'auteur de l'évaluation. » | — |
| un auteur tente de composer | 403 | « Tu ne peux pas composer ta propre évaluation. » | — |
| User injoignable depuis Quiz ou Chat | 503 | « Service des comptes indisponible, réessaie dans un instant. » | réessayer |
| réadmission sans place libre | 409 | « Plus de place libre dans la licence. » | libérer une place |

Les journaux structurés de chaque refus de visibilité portent : qui regarde, quelle personne, quelle portée et quelle règle (sans contenu).

## 7. Tests

**Outil (T2)** : chaque règle devient une fonction pure dans `GPOE26.Logic.Tests` (`VisibilityRules`,
chevauchement des fenêtres, `RoleFits`, réactivation). Les routes et la reprise sont testées dans un nouveau
`User.Tests`, avec PostgreSQL en conteneur (Testcontainers) et `WebApplicationFactory`. `scripts/test-user-api.sh`
et `test-evaluations.sh` sont mis à jour pour `/classrooms/mine?as=` et `/members`.

```
CODE                                                   PARCOURS
[+] VisibilityRules                                    [+] Élève qui part puis revient
  ├─ [GAP→unit] actuel / ancien / expiré                 ├─ [GAP→E2E] départ par code → historique visible
  ├─ [GAP→unit] enseignant affecté après le départ       ├─ [GAP→E2E] A → B → A : deux périodes, rien de l'absence
  ├─ [GAP→unit] opt-out maîtrise ; refus officiel        └─ [GAP→E2E] réadmission Left→Pending→code→Active, place
  └─ [GAP→unit] mineur réputé (T3)                     [+] Enseignant
[+] Chevauchement évaluation / période                   ├─ [GAP→E2E] ancien élève dans « Anciens élèves »
  ├─ [GAP→unit] arrivé en cours d'année, copie existante │   sans évaluations après son départ
  └─ [GAP→unit] évaluation sans OpensAt ni DueAt         └─ [GAP→E2E] moyennes sur les membres actuels
[+] J2 filtre EF / réactivation                         [+] Enseignant qui apprend
  ├─ [GAP→intég.] RoleInAsync ancien = null (CRITIQUE,   ├─ [GAP→intég.] ne compose pas sa propre évaluation
  │    régression C1)                                    └─ [GAP→intég.] rejoint une classe par code
  ├─ [GAP→intég.] comptages c.Members.Count actuels     [+] Adulte qui retire un partage
  ├─ [GAP→intég.] deux activations sur la dernière place  ├─ [GAP→E2E] switch, confirmation, rechargement
  └─ [GAP→intég.] reprise v5 jouée deux fois            └─ [GAP→intég.] parent : portées respectées (H6)
[+] Ancien élève : accès aux cours de la classe refusé (régression C1) [GAP→intég.]
COUVERTURE AUJOURD'HUI : 0/22 ; cible : 22/22
```

Vérification dans le navigateur avec des comptes de test supprimés ensuite.

## 8. Mise en œuvre sûre

- **Sessions parallèles** : chaque lot finit par `git add <chemins du lot>`, avec `git add -p` pour
  `ClassroomEndpoints.cs` (J1, J2, J3), puis `git diff --cached --stat` avant de commiter. Jamais `git add -A`.
  J1 **absorbe** la partie apprentissage de P3c. Les politiques d'enseignant de P3c restent à P3c.
- **Retour arrière** : `Visibility:Enabled=false` revient à la vue « membres actuels ». Le filtre EF reste actif,
  donc l'ancien code n'est jamais redéployé seul. En dernier recours, après export, le script
  `scripts/rollback-v5.sql` (`DELETE FROM "ClassroomMembers" WHERE "LeftAt" IS NOT NULL` …) est lancé, suivi
  de la restauration de la sauvegarde.
- **README** : ajouter une section « Historique et visibilité » (reprise v5, réglages, page, nouvelles routes).

### Parallélisation

| Étape | Modules | Dépend de |
|---|---|---|
| J0 | solution, scripts | — |
| J1 | Quiz, Cours, Chat, User/Endpoints, UI | J0 |
| J2 | User (modèle, données, services) | J1 (ordre de déploiement) |
| J3 | User (services, endpoints) | J2 |
| J4 | Quiz, Chat | J3 |
| J5 | UI | J3 (routes), J4 |

**Ordre : J0 → J1 → J2 → J3 → (J4 ∥ J5).** J4 et J5 peuvent avancer en parallèle (Quiz et Chat d'un côté, UI de
l'autre). La **piste B** (contenu vivant) est une voie parallèle indépendante : elle ne touche pas User, Quiz ni
Chat. Elle partage seulement `GPOE26.UI/Services/Apis.cs` avec J1 et J5, d'où un risque de conflit à coordonner.

### Pannes possibles

| Chemin | Panne | Rattrapée ? | Test ? | Ce que voit la personne | Journalisée ? |
|---|---|---|---|---|---|
| `RoleInAsync`, comptages de membres | une requête oublie `LeftAt` : un ancien élève garde l'accès aux cours de la classe | oui (filtre EF) | prévu | rien (accès refusé) | oui |
| activation d'un code | deux activations sur la dernière place | oui (`LockAsync`) | prévu | 409 « Plus de place libre » | oui |
| Quiz ou Chat → User | User injoignable ou en délai dépassé | à faire (aujourd'hui : 404 muet) | prévu | 503 + « réessaie » | oui |
| reprise v5 | échec à mi-parcours | oui (transaction de la reprise, rejouable) | prévu | rien (service ne démarre pas) | oui |
| retrait de partage | double clic, deux onglets | oui (index unique, `PUT` idempotent) | prévu | état final cohérent | — |
| sauvegarde J0 | disque plein (97 %), sauvegarde tronquée | oui (vérification + essai de restauration) | manuel | — | — |
| page historique | Quiz répond, User non (ou l'inverse) | oui (état partiel) | prévu | cartes sans résultats + message | oui |

Pas de lacune critique restante une fois les tests du §7 écrits.

## 9. Décisions

| # | Décision | Origine |
|---|---|---|
| D1 | Premier jalon léger : dates d'appartenance et règles pures sur les tables actuelles | utilisateur, 22 nov. |
| D2 | Données gardées, reprises idempotentes, sauvegarde avant | utilisateur, 22 nov. |
| D3 | Résultats officiels non retirables pendant l'accompagnement, conservation limitée ; maîtrise et notions fragiles retirables par une personne majeure ; discussions jamais partagées | utilisateur, 22 nov. |
| D4 | Deux pistes : droits et historique (ce plan), contenu vivant (plan séparé) | utilisateur, 22 nov. |
| T1 | Une ligne par période d'appartenance (`Id` + index unique partiel), plutôt qu'une ligne réactivée | utilisateur, 22 nov. (validation finale) |
| T2 | Nouveau projet `User.Tests` (Testcontainers), plutôt que seulement des scripts | utilisateur, 22 nov. (validation finale) |
| T3 | Majorité : `AppUser.BirthYear` facultatif demandé à l'inscription ; sans réponse, est réputée mineure toute personne qui a un lien parent actif ou une `BirthDate` d'établissement de moins de 18 ans ; sinon majeure | utilisateur, 22 nov. (validation finale) |
| T4 | Conservation : 12 mois après le départ, réglable (`Visibility:RetentionMonths`) | utilisateur, 22 nov. (validation finale) |
| T5 | Fiches déjà parties : `LeftAt` = date de la reprise v5 | utilisateur, 22 nov. (validation finale) |

## 10. Hors de ce plan

| Sujet | Pourquoi plus tard |
|---|---|
| Modèle `Accompaniments` unifié, famille = organisation | D1 : valider d'abord à l'usage ; `ParentChildLink` vient d'être branché aux casquettes (P3a) |
| `OfficialRecords` figés | seulement si une note publiée devient modifiable |
| Export vers Pronote / EcoleDirecte | marché déjà tenu par ces logiciels : exporter plutôt que concurrencer |
| Proposé ≠ assigné (table `Proposals`) | jalon 2 de la piste A |
| Contenu vivant (versions, corrections, langues, OCR) | piste B, plan séparé |
| Refonte visuelle complète (trois vues, sélecteur d'espace) | après le jalon 1 |
| Périodes datées pour `ClassroomTeachers` et `OrganizationStudents` (plusieurs passages) | jalon 1 : `AssignedAt` et une seule fiche suffisent |
| Portabilité (export des données de la personne) | après le jalon 1 |

<!-- AUTONOMOUS DECISION LOG -->
## Journal des décisions

| # | Phase | Décision | Classement | Principe | Raison | Écarté |
|---|---|---|---|---|---|---|
| 1 | CEO | Mode SELECTIVE EXPANSION | mécanique | règle autoplan | itération sur un système existant | — |
| 2 | CEO | Prémisses D1-D4 | porte utilisateur | — | validées par l'utilisateur | — |
| 3 | CEO | Reporter le modèle `Accompaniments` et la famille = organisation | goût | P3, P5 | relecteur : modèle non validé, P3a récent | faire tout de suite |
| 4 | CEO | Ajouter la page « Mon historique » au jalon | mécanique | P2 | dans le périmètre, rend le jalon visible | — |
| 5 | CEO | Reporter l'export Pronote / EcoleDirecte | mécanique | P3 | hors du périmètre | — |
| 6 | Design | Cartes par espace, contrôle de partage en haut | mécanique | P5 | hiérarchie, revue design | liste à plat |
| 7 | Design | Cellule `.out` + section « Anciens élèves » | mécanique | P1 | « — » ambigu (critique) | colonne en plus |
| 8 | Design | Tutoiement / vouvoiement selon la personne | mécanique | P1 | convention du dépôt | tout tutoyer |
| 9 | Design | Page aussi dans le menu par défaut de l'élève | mécanique | P1 | sinon invisible pour un élève | — |
| 10 | Eng | Filtre EF `LeftAt IS NULL` | mécanique | P1, P5 | 24 lectures, oubli = faille (C1) | filtrer chaque requête |
| 11 | Eng | Réparer la réadmission dans `ApplyAsync` | mécanique | P2 | bogue existant dans le périmètre (C2) | — |
| 12 | Eng | `?as=learner|teacher` et scission de `/assessments/mine` | mécanique | P5 | faille « l'auteur compose » (C3, H5) | déduire du rôle |
| 13 | Eng | Une ligne par période (T1) | goût | P1 | garde l'absence (H1) | ligne réactivée |
| 14 | Eng | Règle de chevauchement ou copie existante | mécanique | P1 | sinon régression (H2) | « publiée pendant » |
| 15 | Eng | Anciens membres exclus des notions fragiles | mécanique | P5 | état courant non datable (H3) | filtre par période |
| 16 | Eng | CHECK `LeftAt` ⇔ statut | mécanique | P5 | une seule source de vérité (H4) | — |
| 17 | Eng | Portées dans `/members` et `/parents/children` | mécanique | P3 | pas d'appel par élève (M2, H6) | appel séparé |
| 18 | Eng | Âge réputé (T3) | goût | P1 | aucune donnée d'âge (H7) | ignorer les mineurs |
| 19 | Eng | Ordre de déploiement en 4 temps | mécanique | P5 | sinon faille pendant le déploiement (M5) | — |
| 20 | DX | `GPOE26.Windows.slnx` séparé | mécanique | P5 | ne pas casser Linux (critique) | dans `GPOE26.slnx` |
| 21 | DX | `User.Tests` avec Testcontainers (T2) | goût | P1 | aucun outil de test de route | scripts seuls |
| 22 | DX | Interrupteur `Visibility:Enabled`, réglage de conservation, script de retour | mécanique | P1 | retour arrière sûr (critique) | sauvegarde seule |
| 23 | DX | Routes `/me/history`, `/me/sharing/...`, `include=former` | mécanique | P5 | nommage cohérent avec `/me/parents` | `/classrooms/mine/history` |
| 24 | DX | Tableau des erreurs `{message}` | mécanique | P1 | convention du dépôt | — |
| 25 | DX | Reprise v5 réservée, v6 pour P4 | mécanique | P5 | collision de numéros | — |

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|--------|---------|-----|------|--------|----------|
| CEO Review | `/plan-ceo-review` | Scope & strategy | 1 | CLEAR (PLAN via /autoplan) | 2 proposals, 1 accepted, 1 deferred ; prémisses D1-D4 validées |
| Codex Review | `/codex review` | Independent 2nd opinion | 0 | indisponible | CLI Codex trop ancienne pour son modèle (`npm i -g @openai/codex@latest`) |
| Eng Review | `/plan-eng-review` | Architecture & tests (required) | 1 | CLEAR (PLAN via /autoplan) | 17 issues (3 critiques, 7 élevées), 0 critical gaps une fois les tests écrits |
| Design Review | `/plan-design-review` | UI/UX gaps | 1 | CLEAR (PLAN via /autoplan) | score : 3/10 → 7/10, 4 décisions |
| DX Review | `/plan-devex-review` | Developer experience gaps | 1 | CLEAR (PLAN via /autoplan) | score : 3/10 → 7/10, TTHW : 10-15 min → 2 min (tests purs) |

- **VERDICT:** CEO + DESIGN + ENG + DX CLEARED (via /autoplan) — prêt à coder, en commençant par J0.

NO UNRESOLVED DECISIONS
