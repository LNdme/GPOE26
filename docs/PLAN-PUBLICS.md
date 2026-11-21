# Plan : les publics de la plateforme (particuliers, familles, B2B)

> Écrit le 21 novembre 2026. **Rien n'est codé.** Questions Q1 à Q8 (section 6) **validées telles que proposées le 21 novembre 2026**.
> Ce plan complète `docs/PLAN-AGENT-NOYAU.md` (phases A à E) et `docs/PLAN-ETABLISSEMENT*.md`. Il ne les remplace pas :
> il dit qui sont les utilisateurs, et ce qui doit changer dans les comptes, les formules et l'interface pour les servir.

---

## 1. La cible, en une phrase

**L'utilisateur principal est toute personne qui veut apprendre.** Les autres (parent, établissement, entreprise, formateur) sont là pour lui **ouvrir l'accès**, **payer** ou **suivre**.

Trois publics en découlent :

| Public | Qui | Qui paie | Comment on entre |
|---|---|---|---|
| **Particulier** | élève, étudiant, chercheur, développeur, adulte en reconversion, curieux | la personne elle-même | inscription seule, objectif choisi (examen, compétence, curiosité) |
| **Famille** | enfants et adolescents, et le parent qui peut aussi apprendre | un parent | code famille ; profil géré pour un mineur (phase C du plan de l'agent noyau) |
| **B2B** | élèves d'un établissement, stagiaires ou collaborateurs d'une entreprise, élèves d'un formateur indépendant | l'organisation | code personnel ou matricule remis par l'organisation |

Une même personne peut cumuler : un étudiant couvert par son université qui paie aussi son Premium, un parent qui apprend l'anglais, un enseignant qui suit un cours d'une autre école.

---

## 2. Ce qui existe déjà et qu'on garde

- **Droits calculés à partir de sources** (`User/Service/SubscriptionService.cs`, `Entitlement.Sources`) : abonnement personnel, formule Famille, licence d'un établissement, enseignant indépendant ; avertissement en cas de chevauchement (`Overlap`). C'est déjà le modèle « l'accès vient de moi, de mes parents ou de mon organisation ».
- **Espaces** (`Organizations` : `School`, `Institution`, `Independent`) avec responsable, gestionnaires (`OrganizationRole`), liste d'élèves, matricules, import Excel/CSV/docx, « parcours » pour une entreprise, bibliothèque partagée.
- **Catalogue des formules en données** (`GPOE26.ApiService`, `/api/plans`) : chaque formule est une liste de codes de fonctionnalités (`tutor-ai`, `quiz-ai`, `teacher-agent`…). On crée une offre sans toucher au code.
- **Contexte d'apprentissage** (lot B1) : pays, système, niveau libre, `LevelBand.Adult`, objectif `Exam / Skill / Curiosity`, échelle de notes.
- **Une personne, un compte** (décision P5 du plan établissement 2).
- **Droits relus après la connexion** (`SessionState.RefreshProfileAsync`, commit `2b157db`) : un accès ouvert par un parent ou une organisation apparaît sans reconnexion.

---

## 3. Ce qui bloque

### 3.1 Un compte = un rôle fixe

`AppUser.Role` (`UserRole` : `Student`, `Teacher`, `Parent`) décide presque tout :

- le menu (`GPOE26.UI/Layout/AppLayout.razor`, `Items(role)`) ;
- l'accès aux pages : 16 pages déclarent `AllowedRoles` ;
- ce qu'on peut acheter et activer (`SubscriptionService.CanPurchase`, `CanRedeem`) ;
- les fonctionnalités accordées (`FeatureAllowedFor`) ;
- les règles d'accès des espaces (`User/Endpoints/OrganizationEndpoints.cs`, `AccessCodeEndpoints.cs`, `Policies.cs`).

On en compte environ 170 usages, dans 42 fichiers : User, l'interface, l'application Windows et `GPOE26.Web/NavMenu.razor`.

**Ce que ça empêche aujourd'hui :**

- un parent ne peut pas apprendre : `/app/tuteur` et `/app/cours` refusent `Parent` ;
- un gestionnaire RH d'entreprise doit créer un compte « Enseignant » ;
- un développeur ou un chercheur est un « élève », avec « Plan d'études » et « Emploi du temps » ;
- un enseignant qui veut suivre un cours comme apprenant n'a qu'une demi-place.

### 3.2 Des formules nommées par rôle

Les formules s'appellent `StudentPremium`, `Teacher`, `Family` et `OrganizationLicense` (enum `SubscriptionPlan`). Elles ne sont achetables que par un rôle donné. Une même clé (`ELE-`, `ENS-`, `FAM-`) mélange trois choses : qui paie, qui bénéficie et qui gère.

Deux conséquences constatées :

- **Le paiement simulé délivre une clé qu'il faut encore activer.** Les abonnements « en attente de paiement » restent orphelins : il y en a 3 sur un compte de test.
- **Le bug du 21 novembre.** Un abonnement activé par l'administration n'apparaissait pas dans l'interface. Il est corrigé, mais il venait de cette chaîne indirecte.

### 3.3 Un vocabulaire scolaire partout

Les écrans parlent de « élève », « classe », « matricule », « Plan d'études », « Emploi du temps » et « Évaluations » : 36 occurrences de « élève » dans les écrans de `GPOE26.UI`. Une entreprise attend plutôt :

- « stagiaire » ou « collaborateur » ;
- « parcours » ou « cohorte » ;
- un « identifiant » ;
- des « rapports ».

Un particulier qui apprend Python attend « mes projets », pas « mes classes ».

### 3.4 Le tuteur exige un cours

Une discussion avec le tuteur est rattachée à un cours (`/tutor/courses/{courseId}`). Sans cours, la page « Tuteur IA » dit seulement « ouvre un cours ». Un client Premium sans classe ni document ne peut donc rien demander : c'est vérifié dans le navigateur le 21 novembre.

---

## 4. Le modèle cible

### 4.1 Des casquettes au lieu d'un rôle

Toute personne est **apprenante**. Elle peut porter en plus d'autres casquettes, calculées côté serveur et renvoyées dans le profil (`/auth/me`) :

| Casquette | D'où elle vient | Ce qu'elle ouvre |
|---|---|---|
| `learner` | toujours | cours, tuteur, quiz, projets, abonnement personnel |
| `guardian` | un lien parent actif (`ParentChildLinks`) | suivi des enfants, formule Famille, profils gérés |
| `teacher` | membre `Teacher` d'un espace, ou espace `Independent` | classes, épreuves, assistant, corrections |
| `manager` | `Owner` ou `Manager` d'un espace | liste des membres, codes, licences, rapports |
| `payer` | une formule payée dont il n'est pas seul bénéficiaire | factures, places, renouvellement |

Le profil renvoie `Hats: [ "learner", "teacher", … ]`. L'interface et les services lisent les casquettes, plus `Role`. Pour la migration :

- `Role` reste en base et sert de **casquette par défaut** (élève → `learner`, enseignant → `learner` + `teacher`, parent → `learner` + `guardian`) ;
- les règles sont réécrites une par une ;
- `Role` disparaît des règles quand plus rien ne le lit.

Le choix du menu par défaut (« J'apprends », « J'enseigne », « Je suis mes enfants ») devient une **préférence d'affichage**, pas un droit.

### 4.2 Trois familles de formules

| Famille | Formules (exemples, prix à fixer) | Payeur | Bénéficiaires |
|---|---|---|---|
| **Particulier** | Gratuit, Premium (mensuel ou annuel), éventuellement un tarif étudiant ou pays | la personne | elle-même |
| **Famille** | Famille 3 places, Famille 5 places | un parent | le parent (s'il le veut) + les enfants désignés |
| **Organisation** | École, Entreprise, Formateur indépendant (places, durée, options : assistant enseignant, correction) | l'organisation | ses membres actifs |

On sépare trois notions qui sont aujourd'hui mêlées dans la clé :

- **le payeur** : `Subscription.PayerUserId` ou `OrganizationId` ;
- **le bénéficiaire** : une **place** (`Seat`) attribuée à une personne, reprise quand la personne part ;
- **le gestionnaire** : casquette `manager` de l'espace.

Les clés `ELE-` et `FAM-` restent lisibles pour l'existant. Un achat pour soi n'en produit plus : l'abonnement est rattaché directement à l'acheteur.

### 4.3 Le vocabulaire dépend de l'espace

Une table de libellés par type d'espace, côté interface (`Labels.cs`) :

| Notion | École | Entreprise | Formateur indépendant | Particulier |
|---|---|---|---|---|
| membre qui apprend | élève | stagiaire / collaborateur (réglable) | élève | — |
| groupe | classe | parcours | groupe | projet |
| identifiant | matricule | identifiant interne | numéro généré | — |
| suivi | résultats, notions fragiles | rapport d'ensemble | résultats | ma progression |

La règle « qui voit quoi » (P6 du plan établissement 2) est étendue à l'employeur : **chiffres d'ensemble seulement**, jamais les discussions avec le tuteur ni le détail des notes d'une personne, sauf évaluation officielle du parcours.

### 4.4 L'interface suit l'objectif, pas le rôle

- **Inscription** : pays (fait en B5) → « Pourquoi tu apprends ? » (examen, compétence, curiosité) → « Qui t'ouvre l'accès ? ». Trois réponses possibles :
  - « moi » : formule gratuite, Premium plus tard ;
  - « mes parents » : code famille ;
  - « mon école ou mon entreprise » : code personnel ou matricule.

  « Je veux enseigner » et « Je veux gérer une organisation » sont des entrées à part.
- **Menu de l'apprenant**, selon l'objectif :
  - examen : plan d'études, épreuves, emploi du temps ;
  - compétence : projets, parcours ;
  - curiosité : bibliothèque, tuteur libre.

  Les entrées des autres casquettes s'ajoutent dessous.
- **Tuteur libre** : « Pose ta question » sans cours, et « Nouveau projet » à partir de ses documents (lot A6 du plan de l'agent noyau, en version courte d'abord).

---

## 5. Lots

Ordre conseillé : **P1 et P2** (gains visibles pour les particuliers, peu risqués), puis **P3 et P4** (structure), puis **P5 et P6** (B2B). La phase C du plan de l'agent noyau (profils gérés pour les mineurs) reste la condition de l'offre Famille pour les moins de 15 ans.

| Lot | Contenu | Fichiers principaux | Risque |
|---|---|---|---|
| **P1 Tuteur libre** | Discussion sans cours : `POST /tutor/free/messages` (et `/stream`). Le contexte du modèle est le profil et la mémoire, sans extrait de cours. Le prompt système n'invente pas de cours et propose de déposer un document. Page `/app/tuteur` : zone de question en haut et liste des discussions dessous. Même quota `tutor-ai`. Plus tard, rattachement à un projet A6 via la table de correspondance prévue. | `Chat/Endpoints/TutorEndpoints.cs`, `Chat/Model` (conversation avec `CourseId` facultatif), `GPOE26.UI/Pages/TutorHome.razor`, `TutorPanel.razor` | Faible : nouvelles routes |
| **P2 Inscription par objectif** | Après le pays : objectif, puis « qui t'ouvre l'accès ». Les étapes scolaires (sous-système, examens, classe) ne s'affichent que pour l'objectif « examen » au secondaire. Niveau libre pour les adultes. | `GPOE26.UI/Pages/Onboarding.razor`, `User/Program.cs` (inscription), `LearningContext.cs` | Faible |
| **P3 Casquettes** | `Hats` calculées dans User (`HatRules`, fonction pure testée) et renvoyées par `/auth/me` et dans le jeton (claim `hats`). `AppPage.AllowedRoles` remplacé par `RequiredHats`. Menu construit à partir des casquettes. Accès des parents à l'espace d'apprentissage. `CanPurchase`, `CanRedeem` et `FeatureAllowedFor` réécrits sur les casquettes. `Role` gardé en base comme casquette par défaut. | `User/Service/HatRules.cs` (nouveau), `AuthDto.cs`, `Jwtservice.cs`, `SubscriptionService.cs`, `Policies.cs`, `GPOE26.UI/Components/AppPage.cs`, `AppLayout.razor`, les 16 pages à `AllowedRoles`, `GPOE26.Windows` | Moyen : touche tous les écrans ; à faire par écran, derrière les tests |
| **P4 Payeur, place, formules par public** | `Subscription.PayerUserId` (renommage d'`OwnerUserId` par reprise SQL idempotente dans `UserSchema.cs`) et table `Seats` (abonnement, personne, attribuée le, reprise le). Un achat pour soi crée l'abonnement et la place d'un coup, sans clé. Catalogue regroupé par famille (`Category` : Particulier, Famille, Organisation). Page Abonnement qui montre seulement ce que les casquettes permettent d'acheter. Nettoyage des « en attente de paiement » orphelins de plus de 30 jours. | `User/Model/Subscription.cs`, `UserSchema.cs`, `SubscriptionService.cs`, `SubscriptionEndpoints.cs`, `GPOE26.ApiService/Data/ApiSeeder.cs`, `SubscriptionPage.razor`, `PlanComparison.razor`, `PricingPreview.razor` | Moyen : reprise de données ; sauvegarde avant |
| **P5 Vocabulaire par espace** | `Labels.For(OrganizationType)` dans l'interface. Libellé du membre réglable pour une entreprise (stagiaire, collaborateur, apprenant). Écrans « Mon établissement », imports, fiches et codes relus avec ces libellés. Consignes des agents : le mot « élève » remplacé par le libellé de l'espace. | `GPOE26.UI/Services/Labels.cs`, pages de l'espace, `Chat/Agents/*` (consignes) | Faible, mais long : écran par écran |
| **P6 B2B entreprise** | Rapport d'ensemble par parcours (actifs, temps, progression, sans données individuelles sensibles). Facturation sur devis : la licence est créée par l'administration avec un numéro de commande, puis facture PDF. Réattribution des places quand un stagiaire part. Durée de conservation des données après le départ. | `User/Endpoints/OrganizationEndpoints.cs`, `Quiz` (agrégats), `GPOE26.Web` (admin licences) | Moyen : règles de confidentialité à écrire avant de coder |

Chaque lot :

- ajoute ses tests (`GPOE26.Logic.Tests`) ;
- n'est commité qu'avec ses propres fichiers, car deux sessions travaillent dans ce dossier ;
- est vérifié dans le navigateur avec des comptes de test, supprimés ensuite.

---

## 6. Questions tranchées

Toutes validées telles que proposées le 21 novembre 2026.

| # | Question | Décision |
|---|---|---|
| **Q1** | Remplacer le rôle unique par des casquettes ? | Oui, progressivement (P3). `Role` reste la casquette par défaut jusqu'à ce que plus rien ne le lise. |
| **Q2** | Un seul Premium pour tous les particuliers (élève, étudiant, chercheur, développeur) ? | Oui : mêmes fonctionnalités, et des **prix** différents possibles (étudiant, pays) gérés dans le catalogue. Les profils variés passent par l'objectif et le contexte, pas par la formule. |
| **Q3** | Le parent qui paie la formule Famille occupe-t-il une place ? | Non par défaut. Il peut prendre une place s'il veut apprendre aussi. |
| **Q4** | Entreprise : vocabulaire et facturation sur devis dès la première version B2B ? | Vocabulaire oui (P5, peu coûteux). Facturation sur devis, gérée à la main par l'administration (P6). Paiement en ligne des licences plus tard. |
| **Q5** | Que voit l'employeur ? | Chiffres d'ensemble par parcours, plus les résultats des **évaluations officielles** du parcours. Jamais les discussions avec le tuteur, ni les quiz d'entraînement personnels. |
| **Q6** | Tuteur libre (P1) avant le reste ? | Oui : c'est le plus grand manque pour les particuliers, et il ne casse rien. |
| **Q7** | Un mineur peut-il s'inscrire seul comme particulier ? | Oui au-delà de l'âge seuil du pays (P5 du plan de l'agent noyau) ; en dessous, seulement via un parent ou une organisation. |
| **Q8** | Garder les clés `ELE-` et `FAM-` ? | Les clés existantes restent valables. Un achat pour soi n'en produit plus. La clé `FAM-` est remplacée par l'invitation d'un enfant depuis le tableau du parent. |

---

## 6 bis. Avancement

- **P1 fait le 21 novembre 2026 (`825f3fc`).**
  - **Écart avec le tableau** : pas de nouvelle route `/tutor/free`. La discussion libre est une conversation par personne, rangée sous `CourseId = Guid.Empty` (`TutorConversation.FreeCourseId`). Elle réutilise les routes `/tutor/courses/{id}` sans changement de schéma.
  - **Ce qui change pour une question libre** : consigne `FreePrompt`, et `Learner(…, free: true)` ne suppose plus « élève du secondaire ». La mémoire est rangée sans cours, et rien n'est noté dans la maîtrise.
  - **Interface** : le panneau est en haut de `/app/tuteur`, et la discussion libre s'ouvre aussi sur `/app/tuteur/{Guid.Empty}`.
  - **Reste à faire** :
    - l'application Windows n'affiche pas encore la discussion libre (elle la masque) ;
    - plusieurs discussions libres viendront avec les projets (A6).
- **P2 fait le 21 novembre 2026.**
  - **Parcours** : un seul choix « Apprendre » remplace élève, étudiant et adulte. Viennent ensuite :
    1. qui ouvre l'accès : moi, une clé familiale, ou un code d'école ou d'entreprise (qui mène tout de suite à `/code`) ;
    2. le prénom, puis le pays ;
    3. où en est la personne : collège ou lycée, supérieur, vie active ou autre ;
    4. pourquoi elle apprend : examen, compétence ou curiosité ;
    5. les étapes propres au niveau ;
    6. gratuit ou Premium, puis la création du compte.
  - **Questions d'examen** : posées seulement pour l'objectif « examen ».
  - **Règles** : regroupées dans `GPOE26.UI/Services/OnboardingRules.cs` et testées.
  - **Serveur** : aucun changement, il enregistrait déjà l'objectif pour tous.
  - **Écart avec le plan** : « qui t'ouvre l'accès » est demandé avant le profil, et non après. Avec un code, on évite de remplir le profil pour rien.
- **P3 (casquettes), découpé le 21 novembre 2026 :**

  | Sous-lot | Contenu | Fichiers |
  |---|---|---|
  | **P3a** Calcul | `HatRules` (règles pures, testées) et `ProfileService` (lecture en base). Les règles : `learner` pour tous ; `guardian` pour un parent ou un lien parent actif ; `teacher` pour un enseignant ou un membre `Teacher` d'un espace ; `manager` pour un responsable ou gestionnaire actif ; `payer` pour qui a une formule Famille en cours ou en attente. `Hats` est ajouté au profil renvoyé par l'inscription, la connexion, le renouvellement et `/auth/me`, et un claim `hats` est ajouté au jeton. | `User/Service/HatRules.cs`, `ProfileService.cs`, `AuthDto.cs`, `Jwtservice.cs`, `Program.cs`, `AccessCodeEndpoints.cs`, `ProfileEndpoints.cs` |
  | **P3b** Interface | `UserProfile.Hats`. Pour une session enregistrée avant P3, les casquettes sont déduites du rôle. `SessionState.HasHat`. `AppPage.RequiredHats` remplace `AllowedRoles` dans les 16 pages. Le menu de `AppLayout` est fait par sections : « Apprendre » pour tous, puis « Enseigner » et « Mes enfants » selon les casquettes. **Un parent peut apprendre.** L'accueil `/app` garde la vue du rôle. | `GPOE26.UI` |
  | **P3c** Règles du serveur | `CanPurchase`, `CanRedeem`, `FeatureAllowedFor` et les politiques `Policies.*` lues sur le claim `hats`, avec repli sur `role` pour les anciens jetons. Les politiques de Quiz suivent le même principe. | `User/Service/SubscriptionService.cs`, `User/Endpoints/Policies.cs`, `Quiz/Endpoints/EndpointHelpers.cs` |
  | **P3a et P3b faits le 21 novembre 2026** | Vérifié dans le navigateur avec un compte parent. Le serveur renvoie `['learner', 'guardian']`. Le menu affiche une section « APPRENDRE » (Mes cours, Tuteur IA, Emploi du temps), et les trois pages s'ouvrent pour un parent. **Écarts :** `Assessments`, `SubmissionView` et `TakeAssessment` gardent le contrôle par rôle, car Quiz réserve ces routes au rôle élève (à revoir en P3c). Le vocabulaire « classe / plan d'études » reste scolaire pour un parent (P5). | — |
  | **P3d** Autres clients | Menu du site (`GPOE26.Web/NavMenu.razor`) et application Windows, sur les casquettes. Choix de la vue d'accueil comme préférence d'affichage. | `GPOE26.Web`, `GPOE26.Windows` |

## 7. Hors de ce plan

- Paiement réel (mobile money, carte) : à brancher sur P4 quand un prestataire sera choisi.
- Versements aux formateurs (phase D du plan de l'agent noyau).
- Authentification unique (SSO) des entreprises : à étudier après P6, si un partenaire la demande.
- Application mobile.
