# À faire plus tard

Reporté par la revue /autoplan du 22 novembre 2026 de `docs/PLAN-APPRENANT-AU-CENTRE.md`.

## P2 — Piste B : contenu vivant
- **Quoi :** écrire `docs/PLAN-CONTENU-VIVANT.md` : cours versionnés, propositions de correction et de traduction attribuées et validées par l'auteur, version fixée par une assignation, annotations personnelles, OCR relié à la page, bibliothèque `GPOE26.Ingestion`, OCR local Windows (`Windows.Media.Ocr`).
- **Pourquoi :** règle 5 du cap (« un cours partagé ne change jamais en silence ») ; demande de l'utilisateur (retoucher un cours, changer de langue, corriger l'OCR, travailler hors connexion).
- **Contexte :** décision D4 (deux pistes). Touche Cours, `GPOE26.Windows`, les pages de cours de `GPOE26.UI`. Peut démarrer en parallèle du jalon 1.
- **Effort :** L (humain) → M (Claude).

## P2 — Proposé ≠ assigné
- **Quoi :** table `Proposals` (Cours et Quiz) à la place des listes `ClassroomIds` ; flux « Proposé pour moi ».
- **Pourquoi :** règle 3 du cap ; une suggestion n'est pas une obligation.
- **Dépend de :** jalon 1.
- **Effort :** M → S.

## P3 — Modèle `Accompaniments` unifié, famille = organisation
- **Quoi :** une seule notion d'accompagnement (famille, établissement, entreprise), autorisations de partage en table.
- **Pourquoi :** reporté par D1 ; à reconsidérer quand `VisibilityRules` aura servi.
- **Effort :** L → M.

## P3 — Dossier officiel figé (`OfficialRecords`)
- **Quoi :** copie figée des résultats assignés.
- **Pourquoi :** seulement si une note déjà publiée devient modifiable.

## P3 — Export vers les logiciels de vie scolaire
- **Quoi :** exporter les résultats officiels vers Pronote ou EcoleDirecte plutôt que de les concurrencer.

## P3 — Périodes datées pour les enseignants et les fiches d'espace
- **Quoi :** `ClassroomTeachers` et `OrganizationStudents` avec plusieurs périodes.
- **Pourquoi :** le jalon 1 se contente d'`AssignedAt` et d'une fiche par personne.

## P3 — Portabilité
- **Quoi :** export par la personne de toutes ses données (historique, résultats, mémoire du tuteur).
