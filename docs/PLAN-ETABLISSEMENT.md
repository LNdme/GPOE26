# Plan — comptes d'établissement et d'enseignant indépendant

Rédigé le 11 novembre 2026, sur la branche `feat/site-admin-navbar`. Questions Q1 à Q9 validées le même jour.

> **Avancement :** lots 1 à 4 commités le 11 novembre 2026 (`e57fbc9`, `1d68d64`, `398154a`, non poussés) ; lot 5 fait le même jour (non commité). Voir « Lot 1 … 5 : ce qui a été fait » en fin de document. Lots 6 et 7 à faire.

## Le but en une phrase

Un établissement (ou un enseignant indépendant) **gère lui-même** ses enseignants, ses classes et ses élèves ; chaque élève entre avec **son propre code**, qui le relie à son matricule et à sa classe. Le suivi part de là.

## Les choix déjà validés

1. Un **responsable** par établissement, désigné par l'admin de la plateforme. Il peut ajouter d'autres **gestionnaires**.
2. Le responsable **importe la liste des élèves** (nom, matricule, classe) depuis Excel/CSV, ou les saisit à la main.
3. Chaque élève reçoit un **code personnel à usage unique**. L'activer crée le compte, place l'élève dans sa classe et consomme une place de licence. Le responsable voit les codes, les réimprime, les révoque (la place est libérée).
4. Une classe a **plusieurs enseignants, un par matière**. Les classes sont rattachées à une **année scolaire**.
5. L'**enseignant indépendant** suit le même schéma en petit ; le matricule est généré automatiquement ; sa formule compte un nombre de places (environ 30).
6. Un élève peut encore s'inscrire seul, **pour un usage individuel uniquement**. Un élève d'établissement n'entre que par son code.
7. Le **matricule est unique dans l'établissement**.
8. Les classes existantes sont **migrées** vers leur établissement ou vers l'espace de leur enseignant.
9. Les **parents ne changent pas** (`ParentChildLinks`).

---

## 1. Ce qui ne va pas aujourd'hui (base `userdb`)

| Problème | Où |
|---|---|
| `Organizations` n'est qu'un nom ; personne ne la gère, à part l'admin de la plateforme. | `User/Model/Organization.cs`, `User/Endpoints/AdminEndpoints.cs` |
| La licence donne **une clé partagée** (`ETB-…`, `MaxUses = places`) : premier arrivé, premier servi ; une place prise n'est jamais rendue. | `SubscriptionService.RedeemAsync`, `SubscriptionKeys`, `KeyRedemptions` |
| Aucune liste d'élèves, aucun matricule : l'établissement ne sait pas qui sont ses élèves. | — |
| Une classe = **un seul** `TeacherId`, avec un code d'invitation commun à toute la classe. | `Classrooms`, `ClassroomService.JoinAsync` |
| `Classrooms.TeacherId` est en `ON DELETE CASCADE` : supprimer le compte d'un enseignant **supprime la classe** et ses inscriptions. | `User/Data/UserSchema.cs:63` |
| Connexion par e-mail seulement, e-mail obligatoire : bloquant pour des élèves sans adresse. | `User/Program.cs:259`, `AppUsers.Email NOT NULL` |
| Pas de limite de tentatives sur les codes : un code court peut être deviné. | `User/Program.cs` (aucun `AddRateLimiter`) |

## 2. Le modèle cible

### Idée centrale : tout le monde a un « espace »

Un **enseignant indépendant devient un petit établissement** (`Organizations.Type = Independent`) dont il est à la fois responsable et unique enseignant. Ainsi il n'y a **qu'un seul modèle** à coder, tester et afficher — c'est le choix 5 pris au pied de la lettre.

```
Organizations (School | Institution | Independent)
 ├─ OrganizationMembers      responsable, gestionnaires, enseignants  (comptes existants)
 ├─ Subscriptions            la licence : places élèves (+ places enseignants)
 ├─ SchoolYears              « 2026-2027 », une seule en cours
 │    └─ Classrooms          rattachées à une année
 │         ├─ ClassroomTeachers   un enseignant par matière, un professeur principal
 │         └─ ClassroomMembers    (inchangé) élèves ayant un compte
 └─ OrganizationStudents     la liste officielle : matricule, nom, classe — AVANT tout compte
      └─ AccessCodes         code personnel à usage unique → crée le compte
```

### Nouvelles tables et changements

| Table | Colonnes principales | Règles |
|---|---|---|
| `OrganizationMembers` | `OrganizationId`, `UserId`, `Role` (`Owner` / `Manager` / `Teacher`), `Status` (`Active` / `Removed`), `JoinedAt` | Clé (`OrganizationId`, `UserId`, `Role`) : une ligne par rôle. Au moins un `Owner` actif par espace. Un enseignant peut appartenir à plusieurs établissements (cas des vacataires). |
| `SchoolYears` | `Id`, `OrganizationId`, `Label`, `StartsOn`, `EndsOn`, `IsCurrent` | Une seule année `IsCurrent` par espace (index unique partiel). |
| `OrganizationStudents` | `Id`, `OrganizationId`, `Matricule`, `LastName`, `FirstName`, `BirthDate?`, `ClassroomId?`, `UserId?`, `Status` (`Pending` / `Active` / `Revoked` / `Left`), `CreatedAt`, `ActivatedAt?` | **Unique (`OrganizationId`, `Matricule`)** (choix 7). Unique (`OrganizationId`, `UserId`) quand renseigné : un même compte peut figurer dans deux espaces (son lycée et son répétiteur). La ligne survit d'une année à l'autre : passer en classe supérieure = changer `ClassroomId`. `ClassroomId` est la classe principale ; les groupes multiples d'un indépendant restent dans `ClassroomMembers`. |
| `AccessCodes` | `Id`, `Code`, `OrganizationId`, `Kind` (`Student` / `Teacher` / `Owner`), `OrganizationStudentId?`, `Status` (`Issued` / `Used` / `Revoked` / `Expired`), `ExpiresAt`, `UsedAt?`, `UsedByUserId?`, `CreatedByUserId` | Code unique, à usage unique. Un seul code `Issued` à la fois par élève : réimprimer en génère un nouveau et révoque l'ancien. |
| `Classrooms` | **+** `SchoolYearId`, `OrganizationId` devient obligatoire ; `TeacherId` devient **nullable** et en `ON DELETE SET NULL` | `TeacherId` est conservé pendant la transition comme « professeur principal », puis supprimé (lot 7). `InviteCode` devient facultatif puis disparaît. |
| `ClassroomTeachers` | `ClassroomId`, `TeacherId`, `Subject`, `IsMain` | Clé (`ClassroomId`, `TeacherId`, `Subject`). Un enseignant d'une classe doit être `OrganizationMembers` actif de l'espace. |
| `Subscriptions` | **+** `SeatsUsed`, **+** `TeacherSeats?` | `SeatsUsed` est réservé par un `UPDATE … WHERE SeatsUsed < Seats` (même technique que `RedeemAsync` aujourd'hui), pour tenir face aux activations simultanées. |
| `AppUsers` | `Email` devient **nullable** | Index unique conservé (PostgreSQL accepte plusieurs `NULL`). `OrganizationId` / `OrganizationName` deviennent de simples affichages, la vérité est dans `OrganizationMembers` / `OrganizationStudents`. |

### Les codes

- Format : `GP-XXXX-XXXX` (8 caractères de l'alphabet actuel sans 0/O/1/I/L : 31⁸ ≈ 8,5 × 10¹¹), généré par `CodeGenerator`.
- Stockés en clair pour pouvoir être **revus et réimprimés** (choix 3). Le risque est faible : le code ne sert qu'une fois et expire.
- Validité par défaut : jusqu'à la fin de l'année scolaire (à confirmer, question Q7).
- **Limite de tentatives** sur l'activation (limiteur ASP.NET Core par adresse IP) : sans elle, un code peut être deviné.

### Les droits — qui peut faire quoi

| Action | Admin plateforme | Responsable (`Owner`) | Gestionnaire (`Manager`) | Enseignant | Élève |
|---|---|---|---|---|---|
| Créer l'établissement, la licence, désigner le responsable | ✅ | | | | |
| Ajouter / retirer un gestionnaire | ✅ | ✅ | | | |
| Années scolaires, classes, affectation des enseignants | | ✅ | ✅ | | |
| Inviter / retirer un enseignant | | ✅ | ✅ | | |
| Importer les élèves, générer / réimprimer / révoquer les codes | | ✅ | ✅ | | |
| Voir les élèves et le suivi d'une classe | | ✅ | ✅ | ses classes | |
| Créer des évaluations et cours pour une classe | | | | ses classes | |
| Activer son code | | | | ✅ (code enseignant) | ✅ |

Les droits d'espace sont vérifiés **en base** à chaque appel, comme `RequireAdmin` (`User/Endpoints/Policies.cs`) : un nouveau filtre `RequireOrganizationRole(orgId, Owner|Manager)`. Rien n'est ajouté au jeton.

### Le droit Premium

`SubscriptionService.GetEntitlementAsync` ajoute deux sources :
- élève : une ligne `OrganizationStudents` `Active` dont l'espace a une licence en cours ;
- enseignant / gestionnaire : une ligne `OrganizationMembers` `Active` dans un tel espace.

La révocation d'un élève passe `Status` à `Revoked`, décrémente `SeatsUsed` et retire l'élève de `ClassroomMembers`. Son compte reste (il peut continuer en usage individuel gratuit, choix 6).

---

## 3. Les parcours

### Admin de la plateforme
1. Crée l'établissement et sa licence (`/admin/licences`, existant).
2. **Nouveau :** désigne le responsable — par l'e-mail d'un compte existant, ou en générant un **code responsable** (`Kind = Owner`) à lui transmettre.

### Responsable / gestionnaire — nouvel espace « Mon établissement »
1. Tableau de bord : places utilisées / total, année en cours, nombre de classes, d'enseignants, d'élèves activés / en attente.
2. **Années et classes** : créer l'année 2026-2027, créer les classes, affecter un enseignant par matière, choisir le professeur principal.
3. **Enseignants** : inviter (code enseignant personnel, ou e-mail d'un compte existant), retirer.
4. **Élèves** :
   - importer un fichier (colonnes : matricule, nom, prénom, classe, date de naissance facultative) avec un **aperçu avant validation** (lignes en erreur, matricules en double, classes inconnues) ;
   - ajouter à la main, changer de classe, marquer « a quitté l'établissement » ;
   - générer les codes d'une classe, **imprimer une fiche par élève** (nom, classe, code, adresse du site, mode d'emploi) ;
   - révoquer un code ou un élève (la place revient).
5. **Gestionnaires** (responsable seulement) : ajouter / retirer.

### Élève d'établissement
1. Page publique **« J'ai un code »** : il saisit son code.
2. L'écran affiche « *Nom Prénom — Terminale C — Lycée X* : c'est bien toi ? ».
3. Il choisit un **nom d'utilisateur** (proposé à partir de son nom) et un mot de passe ; l'e-mail est facultatif.
4. Le compte est créé, l'élève est placé dans sa classe, la place est consommée, il est connecté.
5. Ensuite il se connecte avec **son nom d'utilisateur ou son e-mail**.

Un élève qui a **déjà un compte** individuel peut saisir le code depuis son profil : le compte est relié à sa ligne `OrganizationStudents` au lieu d'en créer un autre.

### Enseignant d'établissement
- Il active son code enseignant (nouveau compte ou compte existant) et devient `OrganizationMembers.Teacher`.
- Il ne crée plus ses classes : il voit celles où le responsable l'a affecté, avec sa matière.

### Enseignant indépendant
- À l'inscription (`TeachingContext = Independent`) ou à la souscription de la formule Enseignant : création automatique de son espace (`Type = Independent`), dont il est `Owner` et `Teacher`, avec une année en cours.
- Il crée ses classes lui-même et ajoute ses élèves (nom, classe) ; **le matricule est généré** (format en question Q2).
- Places : celles de sa formule (environ 30, question Q1).
- Mêmes fiches de codes, même page d'activation pour ses élèves.

### Élève individuel
Inchangé : inscription libre, usage individuel. Il ne peut plus rejoindre une classe par un code de classe commun.

---

## 4. Les autres services

Les bases Cours, Quiz et Chat ne gardent que des identifiants ; elles demandent les droits au service User. Le changement est petit mais **obligatoire dès qu'une classe a deux enseignants** :

| Où | Aujourd'hui | Demain |
|---|---|---|
| `GET /classrooms/mine` (User) | Enseignant : `TeacherId == moi` | Enseignant : présent dans `ClassroomTeachers`. Le DTO gagne `MyRole` (`Teacher` / `Student` / `Manager`), `MySubjects` et `Teachers[]`. `TeacherId` reste rempli (professeur principal) pour ne rien casser. |
| `GET /classrooms/{id}/members` (User) | Propriétaire seulement | Enseignant de la classe **ou** gestionnaire de l'espace. |
| Quiz `AssessmentEndpoints.cs:186` (`OwnedClassroomIdsAsync`), `UserDirectoryClient.cs` | `c.TeacherId == userId` | `c.MyRole == Teacher` |
| Quiz `ProgressEndpoints.cs:27` | Évaluations de **l'enseignant connecté** | Inchangé pour un enseignant (il voit sa matière). Un gestionnaire voit **toutes** les évaluations de la classe (nouveau cas). |
| Cours `UserDirectoryClient.cs:27` | `c.TeacherId == userId` | `c.MyRole == Teacher` |
| Chat `AgentContextService.cs:105` | Noms des classes | Inchangé ; peut ajouter la matière de l'enseignant. |
| `Assessments.TeacherId` (Quiz) | Auteur | **Inchangé** : reste l'auteur. |

Interface (`GPOE26.UI`) : `AccountModels.cs:98`, `Apis.cs:209-215`, `ClassDetail.razor`, `TeacherHome.razor` (le code d'invitation disparaît), `Onboarding.razor` (étapes `invite` et `cle`), `Login.razor` (identifiant au lieu d'e-mail), `SubscriptionPage.razor`.

---

## 5. La migration des données (choix 8)

Le schéma est créé par du SQL idempotent rejoué à chaque démarrage (`UserSchema.cs`). La reprise des données ne doit tourner **qu'une fois** : ajout d'une petite table `SchemaVersions` et d'une étape « migration v2 » exécutée dans une transaction si elle n'est pas encore notée.

1. **Sauvegarde** de `userdb` avant tout (`pg_dump`).
2. Pour chaque établissement existant : une année « 2026-2027 » en cours.
3. Pour chaque enseignant qui a des classes **sans** établissement : un espace `Independent` à son nom, avec une année en cours.
4. Chaque classe : `SchoolYearId` renseigné, `OrganizationId` renseigné, une ligne `ClassroomTeachers` (`TeacherId`, `Subject`, `IsMain = true`).
5. Clés `ETB-` / `ORG-` déjà activées : enseignants → `OrganizationMembers.Teacher` ; élèves → `OrganizationStudents` `Active` avec matricule généré ; `SeatsUsed` recalculé.
6. Élèves des `ClassroomMembers` sans ligne `OrganizationStudents` : création avec matricule généré, `Active`.
7. Les clés `ETB-` / `ORG-` sont ensuite révoquées : on n'en émet plus.
8. Comptes de démonstration : selon la réponse à Q3.

---

## 6. Les lots

Chaque lot se compile, passe les tests et laisse le site utilisable.

| Lot | Contenu | Vérification |
|---|---|---|
| **1. Schéma et migration** | Nouvelles tables, `TeacherId` nullable et `SET NULL`, `Email` nullable, `SchemaVersions`, migration v2. Aucune page ne change. | Migration jouée sur une copie de la base réelle ; tests existants verts ; contrôle SQL des comptes (classes, élèves, places). |
| **2. Espace et enseignants** | Filtre `RequireOrganizationRole` ; API `/organizations/{id}/…` : membres, années, classes, affectations. `GET /classrooms/mine` étendu ; Quiz, Cours, Chat passent à `MyRole`. Admin : désigner le responsable. | Tests des règles de droits ; scénario API : deux enseignants sur une classe, chacun ne voit que ses évaluations. |
| **3. Élèves et codes** | `OrganizationStudents`, import CSV/Excel avec aperçu, codes, activation, places, révocation, droit Premium ; connexion par nom d'utilisateur ; limiteur de tentatives. | Tests : unicité du matricule, places pleines, activation simultanée, code révoqué / expiré / déjà utilisé, révocation qui libère la place. |
| **4. Pages « Mon établissement »** | Tableau de bord, classes, enseignants, élèves, import, fiches de codes à imprimer, gestionnaires ; page « J'ai un code » ; `Login.razor`. | Essai dans le navigateur, du responsable à l'élève connecté dans sa classe. |
| **5. Enseignant indépendant** | Création automatique de l'espace, matricules générés, places de la formule. | Scénario : inscription indépendant → 3 élèves → codes → activation. |
| **6. Suivi par le gestionnaire** | Le gestionnaire voit le suivi de toutes les classes (Quiz `ProgressEndpoints`). | Test d'accès : gestionnaire oui, enseignant d'une autre classe non. |
| **7. Nettoyage** | Retrait de `POST /classrooms/join`, de `InviteCode`, de `Classrooms.TeacherId`, des clés `ETB-`/`ORG-` ; README, `scripts/test-user-api.sh`, `scripts/seed-tutor-demo.py`. | Recherche de toute référence restante ; tests verts. |

Les lots 1 à 3 sont la base ; 4 rend la chose visible ; 5 à 7 peuvent suivre.

---

## Lot 1 : ce qui a été fait (11 novembre 2026)

**Code :**
- `User/Data/UserSchema.cs` : nouvelles tables (`OrganizationMembers`, `SchoolYears`, `ClassroomTeachers`, `OrganizationStudents`, `AccessCodes`, `SchemaVersions`), `AppUsers.Email` facultatif, `Classrooms.TeacherId` facultatif et en `ON DELETE SET NULL`, `Classrooms.SchoolYearId`, `Organizations.MatriculeSeq`, `Subscriptions.SeatsUsed` / `TeacherSeats`.
- `User/Model/OrganizationSpace.cs` (nouveaux modèles), `UserContext` (relations déclarées pour l'ordre des insertions).
- `User/Data/UserDataMigrations.cs` : reprise v2, jouée une fois au démarrage, dans une transaction, notée dans `SchemaVersions`.
- `User/Service/SpaceService.cs` + `SpaceRules.cs` : les anciens parcours tiennent les nouvelles tables à jour (création de classe → espace, année, professeur principal ; code de classe ou clé `ETB-` → liste des élèves avec matricule, ou membre enseignant).
- Inscription : avec une clé d'établissement, les classes créées à l'inscription vont dans cet établissement (elles restaient sans établissement).
- Démonstration (développement seulement, Q3) : la classe de ProfMaths va au « Lycée de démonstration », AdminGPOE en devient responsable.

**Écarts avec la section 5 :**
- Les clés `ETB-` / `ORG-` ne sont **pas** révoquées au lot 1 : elles donnent encore l'accès Premium. Le lot 3 les retire en même temps qu'il bascule le droit Premium.
- `SeatsUsed` est créé à 0 ; le lot 3 le calcule au moment de la bascule.
- `Classrooms.OrganizationId` reste facultatif en base ; toute classe créée en a désormais un.
- Le nom d'un élève repris est son nom d'utilisateur : la base ne connaît pas d'autre nom.

**Vérifications :**
- 293 tests unitaires réussis (dont 12 nouveaux, `GPOE26.Logic.Tests/SpaceTests.cs`).
- Reprise jouée sur une copie jetable de la sauvegarde du 10 novembre : 4 espaces indépendants créés, la classe de démo rangée au lycée, 7 classes avec année et professeur principal, l'élève de démo sur la liste (`2026-0001`). Pas rejouée au redémarrage.
- Parcours par l'API sur cette copie : indépendant avec 2 classes puis une 3e (un seul espace), élève par code de classe dans 2 classes (une seule ligne), enseignant et élève avec la clé `ETB-DEMO-PROF`.
- Suppression d'un enseignant sur la copie : sa classe reste, sans enseignant.
- Non vérifié : les tests après les deux dernières retouches (relations EF, jeu de démonstration), faute de place sur le disque C ; la compilation et les parcours API, eux, ont été rejoués après ces retouches.

## Lot 2 : ce qui a été fait (11 novembre 2026)

**Droits :** `RequireSpaceRole(…)` (`User/Endpoints/Policies.cs`) vérifie en base, à chaque appel, le rôle actif dans l'espace de la route `/organizations/{orgId}/…` ; refus en 403 avec un message.

**API de l'espace** (`User/Endpoints/OrganizationEndpoints.cs`) :

| Route | Qui | Rôle |
|---|---|---|
| `GET /organizations/mine` | tout compte | mes espaces et mes rôles |
| `GET /organizations/{id}` | responsable, gestionnaire | tableau de bord (année, classes, enseignants, élèves, places) |
| `GET /organizations/{id}/members` | responsable, gestionnaire | membres et leurs rôles |
| `POST`/`DELETE /organizations/{id}/managers` | responsable | nommer / retirer un gestionnaire |
| `POST`/`DELETE /organizations/{id}/teachers` | responsable, gestionnaire | ajouter / retirer un enseignant ; le retrait le retire aussi de ses classes, le rôle de professeur principal passe au suivant ; plafond `TeacherSeats` respecté |
| `GET`/`POST /organizations/{id}/years`, `POST …/years/{yid}/current` | responsable, gestionnaire | années ; une seule en cours |
| `GET`/`POST`/`PUT`/`DELETE /organizations/{id}/classes` | responsable, gestionnaire | classes de l'année (créées sans enseignant), archivage |
| `PUT`/`DELETE /organizations/{id}/classes/{cid}/teachers` | responsable, gestionnaire | affectation par matière, professeur principal |
| `PUT`/`DELETE /admin/organizations/{id}/owners` | admin plateforme | désigner / retirer le responsable ; `GET /admin/organizations` montre les responsables et n'affiche plus les espaces indépendants |

Ajouts par l'e-mail d'un compte existant : les codes enseignant et responsable arrivent au lot 3.

**Classes** (`ClassroomEndpoints.cs`) : un enseignant voit les classes où il est **affecté** (`ClassroomTeachers`), plus seulement celles dont il est `TeacherId`. Le DTO gagne `MyRole` (`Teacher` / `Manager` / `Student`), `MySubjects`, `Teachers`, `OrganizationId`, `SchoolYearId` ; `TeacherId`/`TeacherName` = professeur principal. Liste des élèves, retrait d'un élève, nouveau code : enseignants de la classe et gestionnaires. Archivage : professeur principal ou gestionnaire.

**Autres services :** Quiz (`ClassroomRef.TaughtBy`) et Cours (`UserDirectoryClient.OwnedBy`) décident d'après `MyRole == "Teacher"` : un co-enseignant peut viser la classe d'une évaluation ou y partager un cours ; un gestionnaire non. Sans `MyRole` (ancien service User), ils retombent sur le professeur principal. Chat : rien à changer (il ne lit que les noms).

**Pas d'interface au lot 2** : les écrans (responsable, désignation par l'admin dans `/admin/licences`) sont au lot 4.

**Limites connues, pour les lots suivants :**
- Un enseignant ajouté à un établissement par e-mail qui crée lui-même une classe la range dans son espace indépendant (son compte n'a pas l'établissement « vérifié » par la clé). À régler au lot 4, quand les enseignants d'établissement ne créeront plus leurs classes.
- Le suivi d'une classe par le gestionnaire (Quiz `ProgressEndpoints`) reste au lot 6.

**Vérifications :**
- Tests : 330 / 330 (`GPOE26.Logic.Tests`, dont `SpaceTests.cs` : années saisies, partage par un co-enseignant) et 54 / 54 (`Quiz.Tests`, dont `ClassroomAccessTests.cs`).
- Scénario complet par l'API sur une copie jetable de la sauvegarde (39 contrôles, tous bons) : l'admin désigne le responsable (pas un élève) ; un inconnu et un enseignant pas encore ajouté sont refusés ; année, deux enseignants sur une classe (maths principal, physique) ; chacun voit la classe avec sa matière ; le co-enseignant et le responsable voient la liste des élèves, pas un inconnu ; retirer le professeur principal fait passer le rôle à l'autre ; un gestionnaire crée une classe mais ne nomme personne ; changement d'année ; retrait du responsable par l'admin ; l'ancien parcours « l'enseignant crée sa classe » marche toujours.
- Un enseignant de démo dont la classe vient de la reprise du lot 1 la voit toujours, avec sa matière et le code.

## Lot 3 : ce qui a été fait (11 novembre 2026)

**Liste des élèves** (`User/Endpoints/StudentEndpoints.cs`, responsable et gestionnaires) :
- `GET /organizations/{id}/students` (filtres classe, statut ; chaque élève avec son code à imprimer) ; `POST` (à la main, matricule vide = généré) ; `PUT` (corriger, changer de classe : un élève actif suit) ; `POST …/{sid}/status` (`Revoked` / `Left` : place libérée, l'élève quitte les classes de l'espace, son code est annulé ; `Pending` : réintégré) ; `DELETE` (seulement sans compte).
- `POST /organizations/{id}/students/import` : CSV d'Excel ou LibreOffice (`;` `,` tabulation, guillemets, BOM), titres reconnus sans accents (« N° Matricule », « Prénom(s) », « Né(e) le », « Nom et prénoms »…). Toujours un aperçu ligne par ligne ; rien n'est enregistré tant qu'une ligne est en erreur (nom manquant, date illisible, matricule en double ou déjà inscrit, classe inconnue cette année). Lecture dans `User/Service/StudentImport.cs`. **Excel `.xlsx` direct : pas encore** (Q9 : CSV d'abord).

**Codes personnels** (`AccessCodeService`) : format `GP-XXXX-XXXX`, valables jusqu'à la fin de l'année scolaire en cours (Q7), un seul code valide par élève (en redonner un annule l'ancien).
- Responsable / gestionnaires : code d'un élève, codes de toute une classe (seulement pour ceux qui n'en ont pas), codes « en blanc » d'une classe (Q8), code enseignant, liste à imprimer (`GET …/codes`), annulation.
- Admin : `POST /admin/organizations/{id}/owner-code` (code responsable).
- Activation : `POST /auth/codes/check` (sans compte : « Nom — classe — établissement », ou le nom à saisir pour un code en blanc), `POST /auth/codes/activate` (crée le compte élève ou enseignant ; **e-mail facultatif** ; rien n'est créé si le code ne passe pas), `POST /access-codes/redeem` (compte existant).

**Places :** comptées sous verrou de l'espace (`SELECT … FOR UPDATE`) au moment de l'activation : élèves actifs ≤ places des licences en cours (établissement) ou 30 (`Subscriptions:IndependentSeats`, enseignant indépendant, Q1). Établissement sans licence : activation refusée. Enseignants : plafond `TeacherSeats` s'il est fixé. La colonne `Subscriptions.SeatsUsed` du lot 1 **ne sert pas** : le comptage sous verrou ne peut pas dériver.

**Droit Premium** (`SubscriptionService.GetEntitlementAsync`, source `space`) : élève actif, enseignant ou gestionnaire d'un établissement → sa licence en cours ; élève d'un enseignant indépendant → l'équivalent Premium élève tant que la formule Enseignant (ou l'essai) de cet enseignant est en cours.

**Fin de l'ancien système pour les établissements :**
- Reprise v3 : les clés `ETB-`/`ORG-` des licences d'établissement sont annulées ; ceux qui les avaient activées gardent leur accès par l'espace (repris au lot 1). Saisir une de ces clés répond : « demandez votre code personnel à votre établissement ».
- Une nouvelle licence d'établissement n'a plus de clé. Une licence sans établissement (paiement simulé, développement) garde la sienne.
- Le code de classe commun est refusé pour une classe d'établissement (choix 6) ; chez un indépendant il reste accepté jusqu'au lot 7, dans la limite de ses places.
- Un enseignant entré par code enseignant range ses nouvelles classes dans l'établissement (règle la limite notée au lot 2 pour ce cas).

**Connexion :** par e-mail ou nom d'utilisateur (sans tenir compte des majuscules s'il n'y a qu'un compte possible) ; `Login.razor` demande « Adresse e-mail ou nom d'utilisateur ».

**Limite de tentatives (audit A9) :** `/access-codes/redeem` est ajouté à la règle « codes » (10 codes refusés en 15 minutes par compte). Les routes sans compte (`/auth/codes/*`) ne peuvent pas être limitées par compte : les codes ont 31⁸ combinaisons, et une limite par adresse IP du visiteur reste à placer côté site (le site appelle les services pour tout le monde).

**À faire ensuite :** lot 4, les écrans (liste, import, fiches de codes à imprimer, page « J'ai un code »).

**Vérifications :**
- Tests : 367 / 367 (`GPOE26.Logic.Tests`, dont import CSV et règles des codes) et 61 / 61 (`Quiz.Tests`). Le test d'import a trouvé un vrai défaut, corrigé : le titre « Prénom(s) » n'était pas reconnu.
- Scénario par l'API sur une copie jetable de la sauvegarde, 43 contrôles : licence sans clé, code responsable (refusé à un élève, à usage unique, saisi en minuscules), import avec aperçu puis enregistrement, codes d'une classe, activation sans e-mail puis connexion par nom d'utilisateur, places pleines (aucun compte créé), départ d'un élève (place libérée, plus de classe ni de Premium), code de classe et clé `ETB-` refusés, code enseignant, codes en blanc et Premium par l'essai de l'enseignant indépendant. Le seul écart vient d'une faute de frappe dans le script ; la réponse du service était la bonne.
- Comptes de démo repris : l'élève et l'enseignant de démo gardent l'accès Premium (licence du lycée de démonstration) et leur classe.

## Lot 4 : ce qui a été fait (11 novembre 2026)

**« Mon établissement »** (`/app/etablissement`, lien dans le menu pour le responsable, les gestionnaires et l'enseignant indépendant — « Mes élèves et codes » pour ce dernier) : `GPOE26.UI/Pages/SpacePage.razor` et un composant par onglet (`Components/Space*Tab.razor`).
- **Vue d'ensemble** : places occupées sur la licence, élèves en attente, enseignants, classes ; alerte si l'établissement n'a pas de licence ; années scolaires (préparer, passer à une autre).
- **Classes** : création dans l'année en cours, affectation d'un enseignant par matière (professeur principal), retrait, archivage avec confirmation ; chez un indépendant, « 5 codes en blanc » ; lien vers les fiches.
- **Enseignants** : équipe et rôles, ajout par e-mail, **code enseignant**, retrait ; gestionnaires (responsable seulement).
- **Élèves et codes** : filtres par classe et statut, changement de classe sur la ligne, code à donner ou à redonner, départ / retrait / réintégration, effacement (sans compte, avec confirmation), inscription à la main, **import** (fichier ou copier-coller) avec aperçu ligne par ligne. Un CSV d'Excel enregistré en Windows-1252 est lu correctement (accents).
- **Fiches à imprimer** (`/app/etablissement/{id}/fiches?classe=…`) : une fiche à découper par code (établissement, classe, nom, matricule, code, adresse de la page « J'ai un code », date de fin) ; l'impression masque le reste de la page.

**« J'ai un code »** (`/code`, lien depuis la page de connexion) : le code est vérifié, la page montre « Nom — classe — établissement, c'est bien toi ? », propose un nom d'utilisateur (« jean.mballa »), demande un mot de passe et un e-mail facultatif ; pour un code en blanc, le nom et le prénom. Une personne déjà connectée rattache son compte à la place. Le profil a aussi un champ « Code personnel d'établissement ».

**Administration** (`/admin/licences`) : responsables de chaque établissement, désignation par e-mail ou **code responsable**, retrait ; plus de clé pour une nouvelle licence ; une ancienne clé annulée est affichée comme telle.

**Ailleurs :** connexion par « adresse e-mail ou nom d'utilisateur » ; le code de classe commun n'est plus montré pour une classe d'établissement (« les élèves entrent avec leur code personnel »), le détail d'une classe liste ses enseignants ; le profil indique « aucune adresse » pour un compte sans e-mail. Côté service : la classe porte le type de son espace (`SpaceType`), et un enseignant ajouté à un seul établissement par e-mail y range les classes qu'il crée (limite du lot 2 réglée).

**Vérifications (navigateur, site et service User sur une copie jetable de la base) :** tableau de bord du Lycée de démonstration ; import de 3 élèves avec une classe inconnue signalée puis corrigée ; codes de la classe ; fiches ; création du compte d'un élève par son code (« c'est bien toi ? », classe et licence retrouvées) ; connexion par nom d'utilisateur, y compris en minuscules ; désignation par code responsable dans l'administration. Défauts trouvés pendant l'essai et corrigés : boutons qui restaient grisés tant qu'on n'avait pas quitté le champ (les champs réagissent maintenant à la frappe), nom proposé avec le dernier prénom au lieu du premier, date de fin affichée « 1er août » au lieu du 31 juillet (heure de Douala), ancienne clé annulée montrée comme active. Les écrans du suivi (Quiz, Chat) n'ont pas été lancés dans cette pile.

## Lot 5 : ce qui a été fait (11 novembre 2026)

Une bonne partie de l'enseignant indépendant était déjà en place (espace `Independent`, forfait de 30 places, codes en blanc, Premium par sa formule). Le lot 5 complète :

- **L'espace existe dès l'inscription** d'un enseignant indépendant (`TeachingContext = Independent`), même sans classe : « Mes élèves et codes » est là tout de suite.
- **Une classe créée par un enseignant va toujours dans son espace personnel** (`SpaceService.TeacherSpaceAsync`). Les classes d'un établissement sont créées par sa direction, qui y affecte ses enseignants (section 3 du plan). Cela remplace la règle des lots 1 à 4 (classes rangées dans l'établissement de la clé, ou de l'unique établissement de l'enseignant) : un répétiteur qui enseigne aussi dans un lycée ne mélange plus ses groupes privés et les classes du lycée. Les classes déjà rangées ne bougent pas.
- **L'aperçu dit si les élèves ont l'accès Premium** (`StudentsPremium`) : licence en cours pour un établissement ; formule Enseignant ou essai en cours pour un indépendant, sinon un encadré propose la formule.
- **Accueil enseignant** : s'il enseigne dans un établissement, le formulaire devient « Créer un groupe personnel » et rappelle que les classes de l'établissement viennent de sa direction. L'inscription d'un enseignant mentionne les codes personnels.
- Message « places pleines » neutre (établissement ou enseignant).

**Vérifications :** tests 384 / 384 (`GPOE26.Logic.Tests`) et 61 / 61 (`Quiz.Tests`) ; scénario par l'API sur une copie jetable de la base, avec un forfait réglé à 2 places (`Subscriptions__IndependentSeats=2`), 17 contrôles : espace créé à l'inscription, classe et élèves avec matricules générés à la suite, 3e élève refusé quand les 2 places sont prises, Premium des élèves dès l'essai de l'enseignant, groupe personnel d'un enseignant de lycée rangé chez lui et pas au lycée, indépendant inscrit avec deux classes dans un seul espace.

---

## 7. Questions tranchées

> **Toutes les propositions ci-dessous ont été validées le 11 novembre 2026.** Elles font désormais partie du plan.

| # | Question | Décision |
|---|---|---|
| Q1 | Nombre de places d'un enseignant indépendant ? | 30 dans la formule Enseignant. |
| Q2 | Format du matricule généré (indépendant, migration) ? | `AAAA-NNNN` (année d'entrée + numéro), ex. `2026-0007`. Les établissements importent leurs propres matricules. |
| Q3 | Comptes et classes de démonstration (`@gpoe26.cm`) ? | Les migrer comme les autres (espace « Lycée de démonstration »), seulement en développement. |
| Q4 | E-mail facultatif pour les élèves activés par code, connexion par nom d'utilisateur ? | Oui : beaucoup d'élèves n'ont pas d'adresse. Sans e-mail, le mot de passe oublié est réinitialisé par le responsable (nouveau code). |
| Q5 | Les enseignants consomment-ils des places de licence ? | Non : places pour les élèves ; nombre d'enseignants plafonné à part (`TeacherSeats`). |
| Q6 | Un enseignant peut-il appartenir à plusieurs établissements ? | Oui (vacataires) ; il choisit l'espace affiché. |
| Q7 | Durée de validité d'un code non utilisé ? | Jusqu'à la fin de l'année scolaire. |
| Q8 | Pour l'indépendant, des codes « en blanc » (l'élève saisit lui-même son nom à l'activation) ? | Oui, en option : moins de saisie pour l'enseignant. |
| Q9 | Format d'import ? | CSV dès le lot 3 ; Excel `.xlsx` en plus (paquet ClosedXML, licence MIT). |
