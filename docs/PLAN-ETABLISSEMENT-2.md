# Plan — établissements, phase 2 : bibliothèque, connexion par matricule, données partagées

Rédigé le 11 novembre 2026, sur la branche `feat/site-admin-navbar`, après la phase 1 (`docs/PLAN-ETABLISSEMENT.md`, lots 1 à 7, poussée dans la PR #6). Décisions P1 à P6 et questions Q10 à Q15 validées le même jour.

> **Avancement :** lot 9 commité (`56015f4`), lot 10 commité (`2617802`) ; lot 8 commité (`192d1f6`) ; lot 11 fait le 11 novembre 2026 (non commité) — voir « Lot 8 à 11 : ce qui a été fait ». Lots 12 et 13 à faire.

## Ce que l'utilisateur a demandé

1. Un enseignant peut être **indépendant et membre d'un établissement** en même temps, et partager ses contenus (cours, devoirs, épreuves) avec son établissement.
2. L'élève se connecte **avec son matricule**, directement dans sa classe, après que l'établissement a **importé un fichier .docx, .csv ou Excel** (nom, e-mail facultatif, matricule, classe — ou « parcours » pour une entreprise —, autres informations).
3. Un élève qui a **déjà un abonnement** (personnel ou familial) reçoit un accès de l'établissement ; un enseignant a un compte indépendant et un compte d'établissement : comment les relier, et **partager les données** au mieux entre l'agent, l'établissement, l'enseignant, le parent et l'élève.

## Ce que la phase 1 règle déjà

- **Un compte, plusieurs espaces** : un enseignant est `Owner`+`Teacher` de son espace personnel et `Teacher` d'un ou plusieurs établissements ; les classes qu'il crée lui-même vont dans son espace personnel.
- **Partage vers des classes** : un cours (Cours) ou une épreuve (Quiz) peut déjà viser n'importe quelle classe où l'enseignant enseigne, de ses deux espaces.
- **Droit Premium** : `GetEntitlementAsync` réunit abonnement personnel, clé familiale et accès par l'espace, et garde le meilleur.
- **Rattacher un compte existant** : `POST /access-codes/redeem` (profil, accueil élève) — pas de second compte.

## Décisions validées (11 novembre 2026)

| # | Décision |
|---|---|
| P1 | Chaque établissement a une **bibliothèque partagée** : un enseignant y dépose un cours, un devoir ou une épreuve ; les autres enseignants (et les gestionnaires) de l'établissement le consultent et en font une **copie** pour leurs classes. |
| P2 | **L'original reste à son auteur.** S'il quitte l'établissement, il garde ses contenus ; l'établissement garde les copies déjà faites. |
| P3 | **Connexion par matricule** : identifiant = **matricule + code de l'établissement** ; à la première connexion, le mot de passe est le **code personnel** imprimé sur la fiche ; l'élève choisit ensuite son mot de passe. |
| P4 | **Import** : Excel `.xlsx`, CSV et Word `.docx` (tableau du document) ; « parcours » pour une entreprise ; les **colonnes en plus** sont gardées telles quelles dans la fiche de l'élève (OpenXML et ClosedXML, licence MIT). |
| P5 | **Une personne, un compte.** L'abonnement personnel continue ; l'élève et le parent payeur sont **prévenus** que l'établissement couvre déjà l'élève, jusqu'à telle date. |
| P6 | **Qui voit quoi** : tableau ci-dessous. |

### P6 — partage des données

| | Élève | Parent (lien accepté) | Enseignant | Établissement | Tuteur (agent) |
|---|---|---|---|---|---|
| Résultats aux épreuves de l'établissement | oui | oui | sa matière | oui | oui |
| Travail personnel (tuteur, quiz perso, cours privés) | oui | oui | non | non | oui |
| Notions fragiles relevées par le tuteur | oui | oui | ses élèves, en résumé | chiffres d'ensemble | oui |
| Discussions avec le tuteur | oui | non | non | non | oui |

- Le tuteur connaît en plus l'établissement, la classe, **le programme et les épreuves à venir** fixées par les enseignants, et les résultats scolaires.
- Si l'élève quitte l'établissement : son travail personnel reste à lui ; l'établissement garde ses notes en archive.
- Enseignant aux deux espaces : une seule bibliothèque personnelle et un seul assistant, qui sait dans quel espace on travaille ; l'établissement ne voit que ce qui touche ses classes ou sa bibliothèque.

---

## 1. Changements de données

### Service User (`userdb`)

| Changement | Pourquoi |
|---|---|
| `Organizations.ShortCode` (texte court, unique, ex. `LYCDEMO`) | Code de l'établissement pour la connexion par matricule (P3). Proposé à partir du nom, modifiable par l'administration. Vide pour un espace d'enseignant indépendant au départ ; généré à la première fiche imprimée. |
| `OrganizationStudents.Email` (facultatif) | E-mail importé ; recopié sur le compte à l'activation s'il n'est pas déjà pris. |
| `OrganizationStudents.Extra` (`jsonb`) | Colonnes en plus du fichier, avec leur titre (P4) : téléphone, parent, filière, adresse… |
| `AppUsers.MustChangePassword` (booléen) | Première connexion avec le code : changement de mot de passe obligatoire (P3). |
| `Classrooms.Kind` déjà `Class`/`Group` | « Parcours » d'une entreprise = groupe de l'institution ; l'import reconnaît la colonne « parcours ». |

### Service Cours (`coursdb`) et service Quiz (`quizdb`)

| Changement | Pourquoi |
|---|---|
| `Courses.LibraryOrganizationIds` (`uuid[]`) ; `Assessments.LibraryOrganizationIds` (`uuid[]`) | Bibliothèques d'établissement où le contenu est déposé (P1). |
| `Courses.SourceCourseId`, `Assessments.SourceAssessmentId` (facultatifs) | Une copie se souvient de son original (« d'après le cours de M. X »), sans en dépendre (P2). |

### Service Chat (`chatdb`)

Rien de nouveau à stocker : `CourseMastery.WeakPoints` (par élève et par cours) existe déjà ; il faut seulement de **nouvelles lectures** contrôlées par les droits (lot 12).

---

## 2. Les lots

| Lot | Contenu | Vérification |
|---|---|---|
| **8. Bibliothèque d'établissement** (P1, P2) | Cours et Quiz : déposer / retirer un contenu dans la bibliothèque d'un établissement dont on est enseignant ; lister la bibliothèque (enseignants et gestionnaires de l'établissement) ; **copier** dans ses contenus (sections, questions, pièces jointes), copie liée à son original. Droits demandés au service User (`GET /organizations/mine`). Écran « Bibliothèque » dans « Mon établissement » et dans « Mes cours » / « Épreuves ». | Un enseignant dépose, un autre copie et modifie sa copie sans toucher l'original ; un enseignant d'un autre établissement ne voit rien ; l'auteur retiré de l'établissement garde ses contenus, les copies restent. |
| **9. Import Excel et Word** (P4) | `.xlsx` (ClosedXML), `.docx` (premier tableau du document, OpenXML), CSV existant ; colonnes « parcours », « e-mail » ; colonnes inconnues gardées dans `Extra` et montrées dans la fiche de l'élève ; aperçu ligne par ligne comme aujourd'hui. | Fichiers d'essai Excel, Word et CSV (accents, cellules fusionnées, lignes vides) ; tests de lecture ; essai dans le navigateur. |
| **10. Connexion par matricule** (P3) | Code d'établissement ; page de connexion « Élève d'un établissement » (code de l'établissement, matricule, mot de passe) ; première connexion avec le code personnel → compte créé, élève placé dans sa classe, changement de mot de passe obligatoire ; ensuite matricule + mot de passe. Les fiches imprimées donnent le code de l'établissement et le matricule. La page « J'ai un code » reste pour qui préfère un nom d'utilisateur. Limite de tentatives par matricule. | Première connexion, changement de mot de passe, reconnexion ; mauvais code ou matricule d'un autre établissement refusés ; élève retiré refusé ; limite de tentatives. |
| **11. Abonnements qui se chevauchent** (P5) | Le droit Premium détaille ses sources (personnel, familial, établissement, avec dates) ; à l'activation d'un code ou dans « Abonnement », l'élève est prévenu que l'établissement le couvre ; le parent payeur voit, pour chaque enfant, « couvert par l'établissement jusqu'au … » et peut ne pas renouveler. Même chose pour un enseignant (formule Enseignant + licence de l'établissement). | Élève Premium personnel puis rattaché : avertissement ; parent Famille : enfant marqué « couvert » ; aucune perte d'accès. |
| **12. Données partagées et agent** (P6) | Tuteur : établissement, classe, **épreuves à venir** (Quiz) et résultats dans son contexte. Enseignant : résumé des notions fragiles de ses élèves sur les cours de sa classe (Chat, droits demandés à User). Établissement : chiffres d'ensemble (notions les plus fragiles par classe, sans nom d'élève). Parent : notions fragiles de son enfant. Discussions du tuteur jamais montrées hors de l'élève. Élève parti : notes gardées en archive par l'établissement, plus d'accès à son travail personnel. | Tests de droits pour chaque case du tableau P6 ; scénario : un élève en difficulté sur un cours, ce que voient l'enseignant, le parent, le gestionnaire. |
| **13. Enseignant aux deux espaces** | Accueil enseignant groupé par espace (« Lycée X », « Mon espace ») ; l'assistant enseignant sait de quel espace relève la classe dont on parle ; « Mes cours » montre ce qui est déposé dans quelle bibliothèque. | Un enseignant du lycée qui a aussi ses groupes : classes groupées, assistant correct, dépôt en bibliothèque visible. |

Ordre conseillé : 9 et 10 d'abord (les élèves entrent plus simplement), puis 8, 11, 12, 13.

---

## Lot 11 : ce qui a été fait (11 novembre 2026)

- **Le droit Premium détaille ses sources** (`Entitlement.Sources`) : essai, formule personnelle (Premium élève ou Enseignant, payée à la caisse ou activée par sa clé `ELE-`/`ENS-`), formule Famille (avec le nom du parent payeur), licence d'un établissement (avec son nom), formule de l'enseignant indépendant qui suit l'élève (avec le nom de son espace). Plan, date et `Source` restent ceux de la source la plus longue ; une formule personnelle activée par sa clé donne maintenant `Source = subscription` (avant : `key`).
- **Chevauchement** (`Entitlement.Overlap`, règle `EntitlementRules.Overlap`) : un établissement ou un enseignant couvre la personne **et** elle paie (formule personnelle ou place Famille). Un essai ne compte pas. **Rien n'est coupé** (P5).
- **Élève et enseignant prévenus** : `POST /access-codes/redeem` renvoie l'avertissement ; l'accueil élève l'ajoute au message « Tu as rejoint… », les pages « J'ai un code » et « Profil » ouvrent « Abonnement », qui montre l'avertissement et la liste des sources avec leurs dates. Un enseignant ajouté par la direction le voit dans « Abonnement ».
- **Parent payeur** : `GET /parents/children` donne pour chaque enfant `CoveredBy`, `CoveredUntil` et `UsesFamilySeat` ; l'accueil parent écrit « Couvert par … jusqu'au … » sous l'enfant, et « Abonnement » liste les enfants couverts, en disant qu'il peut ne pas renouveler. Q15 : pas encore d'e-mail (aucun fournisseur branché).
- Écart assumé : pas de remboursement ni de mise en pause automatique de l'abonnement payé ; c'est au payeur de ne pas renouveler.

**Vérifications :** tests Logic 434 / 434 (règle de chevauchement, ancienne valeur de `Source`) ; scénario sur une base neuve, 20 contrôles (élève qui paie puis rejoint le lycée, enfant d'une formule Famille et son parent, enseignant avec sa formule ajouté au lycée, élève d'un enseignant indépendant, élève sans abonnement, enseignant en essai, élève parti) ; dans le navigateur : « Abonnement » de l'enfant (sources et avertissement), accueil et « Abonnement » du parent. Trouvé pendant le travail : une formule personnelle payée s'active par sa clé, sans être rattachée au payeur ; elle est bien comptée comme payée. Corrigé pendant l'essai : la phrase de l'avertissement était coupée par un retour à la ligne.

## Lot 8 : ce qui a été fait (11 novembre 2026)

- **Données** : `Courses.LibraryOrganizationIds` et `Assessments.LibraryOrganizationIds` (`uuid[]`, index GIN), `Courses.SourceCourseId` et `Assessments.SourceAssessmentId`, ajoutés au démarrage de Cours et de Quiz (SQL idempotent).
- **Droits** demandés au service User par une nouvelle route, `GET /organizations/{id}/library` (enseignants, responsable et gestionnaires de l'établissement ; pas pour un espace d'enseignant indépendant) : nom de l'établissement, enseignants actifs, et `CanManage`. Écart assumé : le plan citait `GET /organizations/mine`, qui ne dit pas qui sont les collègues.
- **Un dépôt n'est visible que si son auteur enseigne encore dans l'établissement** : l'auteur qui part garde ses contenus, ils quittent la bibliothèque avec lui ; ils y reviennent s'il revient (P2).
- **Cours** : `PUT /cours/{id}/library` (l'auteur, seulement dans un établissement où il enseigne), `GET /cours/library/{orgId}` (auteur, nombre de copies), `DELETE /cours/library/{orgId}/{id}` (l'auteur, ou le responsable et les gestionnaires, Q13), `POST /cours/{id}/copy`. Un collègue peut **lire** un cours déposé. La copie est un brouillon au copieur, sans classe ni dépôt, avec ses propres sections ; **le PDF et les images sont recopiés** et les adresses des images réécrites, pour que la copie survive à l'original. Pas de copie pendant la mise en forme d'un PDF (409).
- **Quiz** : les mêmes routes sous `/assessments` ; un collègue voit toutes les questions (et les réponses) d'une évaluation déposée. La copie est un brouillon sans classe, sans dates et **sans lien vers le cours de l'auteur** (ce cours ne lui appartient pas).
- **Corrigé en passant** : supprimer un cours n'effaçait son dossier d'images que s'il avait un PDF (cours importé en photos, copie) ; le dossier est maintenant toujours effacé.
- **Écrans** : page « Bibliothèque » (`/app/bibliotheque`, lien dans le menu pour qui est dans un établissement, et bouton dans « Mon établissement ») : onglets « Cours » et « Devoirs et épreuves », déposer un de ses contenus, lire, voir les questions, copier (lien « Modifier ma copie »), retirer.
- Reste pour le lot 13 : montrer dans « Mes cours » et « Épreuves » où chaque contenu est déposé.

**Vérifications :** tests Logic 422 / 422 et Quiz 66 / 66 (visibilité, copie indépendante de l'original, images réécrites, fichiers recopiés) ; scénario sur une base neuve avec les trois services, 55 contrôles (dépôt, lecture et copie par un collègue, refus pour un enseignant indépendant et un élève, retrait par le responsable mais pas par un collègue, auteur retiré puis revenu, original supprimé : la copie et ses images restent) ; parcours dans le navigateur, aussi à 375 px de large. Corrigé pendant l'essai : une copie n'apparaissait dans « Déposer » qu'après rechargement de la page.

## Lot 9 : ce qui a été fait (11 novembre 2026)

- **Lecture des fichiers** (`User/Service/ImportFileReader.cs`) : Excel `.xlsx` (première feuille non vide, cellules lues comme Excel les affiche — un matricule `00123` garde ses zéros —, dates en jj/mm/aaaa, cellules fusionnées reprises), Word `.docx` (premier tableau qui a une colonne « nom » ; cellules fusionnées sur plusieurs colonnes ou vers le bas reprises), CSV (UTF-8 ou Windows-1252). Un ancien `.xls` ou `.doc`, ou un fichier abîmé, est refusé avec une explication. 5 Mo au plus. Paquet ajouté : **ClosedXML 0.105.1** (qui apporte DocumentFormat.OpenXml 3.1.1), sans faille connue.
- **Lecture des colonnes** (`StudentImport.ParseTable`) : la ligne des titres est cherchée dans les 10 premières lignes (un titre « Liste de la Tle C » au-dessus est ignoré) ; nouvelles colonnes reconnues : **parcours / groupe / promotion / cohorte** (tiennent lieu de classe), **e-mail / courriel**, « étudiant », « employé », « apprenant »… ; **les autres colonnes sont gardées** (titre → valeur).
- **Fiche de l'élève** : `OrganizationStudents.Email` et `ExtraJson` ; l'e-mail est aussi saisissable à la main ; il est **recopié sur le compte à l'activation** s'il est libre (Q12).
- **API** : `POST /organizations/{id}/students/import-file` (fichier, aperçu seulement ; renvoie le fichier remis en CSV), puis `POST …/students/import` avec ce CSV et `commit: true` pour enregistrer.
- **Écran** « Élèves et codes » : choix d'un fichier Excel, Word ou CSV ; l'aperçu montre l'e-mail et les autres informations, et les colonnes gardées ; la liste montre l'e-mail et les autres informations sous le nom.

**Vérifications :** tests 396 / 396, dont 13 nouveaux (vrais classeurs Excel et documents Word fabriqués en mémoire : titre fusionné, matricule à zéros, date Excel, fusion verticale dans Word, document sans tableau, ancien format, fichier abîmé) ; scénario par l'API sur une base neuve avec un classeur et un document d'essai, 15 contrôles ; liste vérifiée dans le navigateur (et matricule long gardé sur une ligne).

---

## Lot 10 : ce qui a été fait (11 novembre 2026)

- **Code de l'établissement** (`Organizations.ShortCode`, unique) : proposé à partir du nom (« Lycée de démonstration » → `LYCDEMO`, un chiffre ajouté s'il est pris), donné à la création d'un établissement ou d'un espace d'enseignant ; **reprise v4** pour les espaces existants ; modifiable par l'administration (`PUT /admin/organizations/{id}/short-code`, 4 à 10 lettres ou chiffres) ; montré dans « Mon établissement », l'administration et sur les fiches.
- **Connexion par matricule** (`POST /auth/login-matricule` : code de l'établissement, matricule, mot de passe ; majuscules indifférentes) :
  - première fois, avec le code de la fiche : le compte est créé (nom d'utilisateur `code.matricule`, ex. `lycdemo.tc-101`, e-mail importé recopié), l'élève est placé dans sa classe et prend une place ;
  - ensuite, avec son mot de passe (son nom d'utilisateur et son e-mail marchent aussi sur la connexion habituelle) ;
  - refus sans dire ce qui est faux ; élève parti ou retiré refusé ; **5 échecs par élève en 15 minutes** puis blocage (règle « connexion-matricule » de la limite de tentatives, clé = établissement + matricule).
- **Premier mot de passe** : après une connexion avec un code, `MustChangePassword` ; la page « Choisis ton mot de passe » (`/app/mot-de-passe`) est la seule ouverte tant qu'il n'est pas choisi (`POST /auth/me/first-password`).
- **Mot de passe oublié (Q11)** : « Code de reconnexion » dans la liste des élèves, pour un élève qui a son compte ; il ne sert qu'à ce compte (connexion par matricule, puis nouveau mot de passe). **Protection ajoutée** : un tel code ne peut ni créer un autre compte (`/code`), ni être pris par un autre compte — sinon qui trouve la fiche prendrait la place de l'élève.
- **Écrans** : onglet « Avec mon matricule » sur la page de connexion (`/login?matricule`) ; fiches avec « Établissement » et « Matricule » et la marche à suivre ; code de l'établissement dans l'aperçu et l'administration.

**Vérifications :** tests 416 / 416 (code proposé, code valable, nom d'utilisateur tiré du matricule, règle de limite) ; scénario sur une base neuve, 28 contrôles (reprise v4, première connexion, mot de passe choisi, reconnexion par matricule, nom d'utilisateur et e-mail, code de reconnexion et sa protection, élève parti, limite de tentatives, code changé par l'administration) ; parcours dans le navigateur : connexion par matricule → page du mot de passe imposée (aussi en allant ailleurs) → retour à la page demandée ; fiche imprimable vérifiée. Corrigé pendant l'essai : la touche Entrée juste après la frappe n'envoyait pas le mot de passe (le formulaire vérifie maintenant à l'envoi) ; matricule écrit deux fois sur la fiche.

---

## 3. Petites questions tranchées

> **Toutes les propositions ci-dessous ont été validées le 11 novembre 2026.**

| # | Question | Décision |
|---|---|---|
| Q10 | Format du code d'établissement ? | 4 à 10 lettres et chiffres, proposé à partir du nom (ex. « Lycée de démonstration » → `LYCDEMO`), modifiable par l'administration. |
| Q11 | Que fait un élève qui oublie son mot de passe (sans e-mail) ? | Le responsable lui redonne un code personnel : la connexion avec ce code remet un mot de passe à choisir. |
| Q12 | L'e-mail importé devient-il l'identifiant ? | Non : il est recopié sur le compte s'il est libre, et sert à se connecter en plus du matricule. |
| Q13 | Qui peut déposer dans la bibliothèque ? | Tout enseignant de l'établissement ; le responsable et les gestionnaires peuvent retirer un dépôt. |
| Q14 | Combien de temps l'établissement garde-t-il les notes d'un élève parti ? | Tant que l'établissement existe sur la plateforme ; effaçables à sa demande. |
| Q15 | Comment prévenir le parent (P5) ? | Dans son espace (bandeau « Abonnement ») ; par e-mail quand un fournisseur d'e-mail sera branché (audit B12). |
