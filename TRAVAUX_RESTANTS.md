# Travaux restants

État de référence : 19 novembre 2026. Ce document décrit les suites à donner à l’architecture et
aux fonctionnalités du dépôt ; il ne signifie pas que les éléments déjà présents dans le code
doivent être réécrits.

## Priorité 1 — Fiabiliser et exploiter le socle actuel

- [ ] Stabiliser la suite complète `dotnet test GPOE26.slnx`, y compris les tests d’intégration qui
  nécessitent Docker et Aspire. La CI GitHub exécute restore, build, tests unitaires et le test
  PostgreSQL workspace ; les tests Aspire marqués `Category=Integration` restent séparés jusqu’à
  disposer d’un runner Docker/Aspire stable.
- [ ] Vérifier les scénarios d’évolution des schémas User, Cours, Chat et Quiz sur une base
  existante, et adopter une stratégie explicite de migrations avant les déploiements en production.
- [ ] Valider en environnement de déploiement les secrets et la configuration : clé JWT, clé HMAC
  Chat–User, clé LLM, sauvegarde et partage des clés ASP.NET Data Protection entre instances.
- [ ] Déployer le sandbox MCP sur un hôte Docker dédié, de préférence rootless ou dans une VM isolée.
  L’accès à un daemon Docker rootful donne pratiquement des privilèges administrateur.
- [ ] Définir la supervision et l’exploitation : alertes de santé des services, disponibilité de User
  pour la validation inter-services, sauvegardes/restaurations PostgreSQL et procédures de rotation
  des secrets.
- [ ] Définir un nettoyage sûr des réseaux Docker MCP inactifs. La révocation n’efface pas encore
  automatiquement les réseaux ; avant toute suppression, vérifier qu’aucun serveur MCP ne les
  utilise.

## Priorité 1 — Terminer la validation du parcours MCP

- [ ] Ne pas activer l’ancienne image `mcp/fetch` auditée : elle est ancienne et reste absente du
  catalogue et de l’allowlist.
- [ ] Choisir une révision maintenue du serveur Fetch, contrôler la provenance, les changements de
  sécurité et la cohérence de la licence, puis produire une image reproductible non-root, épinglée
  par digest.
- [ ] Tester cette image dans le sandbox réel : accès autorisé réussi, domaine non autorisé refusé,
  accès Internet direct impossible, et vérification des limites réseau et de ressources.
- [ ] N’ajouter Fetch à `AllowedImages` et au catalogue opérateur qu’après ces contrôles ; commencer
  avec une liste d’hôtes minimale et documentée.
- [ ] Distinguer explicitement les états « configuration vérifiée », « autorisée » et « processus
  démarré/testé ». Une approbation administrative seule ne prouve pas qu’une image a été lancée.
- [ ] Compléter les tests d’intégration egress et de révocation, notamment la fermeture d’une
  session MCP après retrait d’un grant et le nettoyage des ressources associées.

## Priorité 2 — Personnaliser le tuteur et développer le harness

- [ ] Évaluer Pi comme runtime alternatif derrière `IAgentRuntime` dans un processus Node séparé :
  adapter les événements, les outils autorisés, le streaming et les erreurs de repli sans exposer
  les secrets GPOE26 ou contourner les grants/workspaces.
- [ ] Lancer un test A/B limité sur un jeu de tutorat représentatif, puis comparer la qualité,
  la latence, le coût, les appels d’outils et la robustesse aux contenus non fiables.
- [ ] Ne pas intégrer les extensions, compétences ou répertoires Pi comme source d’autorisation ;
  conserver les profils, grants, MCP et données de GPOE26 comme frontières officielles.

- [ ] Définir le modèle de profil pédagogique persistant par utilisateur : langue, niveau, objectifs,
  préférences d’explication, progression et consentements. Les profils et le routeur actuels
  constituent le socle ; décider quelles préférences sont modifiables par l’élève ou son responsable.
- [ ] Ajouter les API et l’interface de gestion de ces préférences, avec validation côté serveur et
  application du profil seulement dans le contexte de l’espace et de l’utilisateur authentifiés.
- [ ] Construire des évaluations pédagogiques reproductibles : jeux de cas, critères de qualité,
  tests de non-régression des réponses et des appels d’outils, suivi des coûts et des erreurs par
  modèle sans journaliser les contenus privés.
- [ ] Décider comment versionner, publier et retirer les profils et extensions. Les manifestes et le
  registre valident actuellement des déclarations ; ils ne constituent pas à eux seuls un magasin
  administrable ni un processus de publication.
- [ ] Étendre les runtimes ou fournisseurs LLM uniquement selon les besoins confirmés, en gardant les
  contrats agent indépendants du runtime et en testant le routage, les pièces jointes et le repli de
  modèles.

## Priorité 2 — Compléter les fonctionnalités de la plateforme

- [ ] Construire l’application Windows hybride prévue, avec lecture hors connexion, stockage local
  SQLite et synchronisation contrôlée au retour du réseau.
- [ ] Ajouter l’import de PDF, DOCX et images vers des cours éditables, avec prévisualisation,
  validation par l’enseignant et conservation de la provenance du contenu.
- [ ] Réaliser l’éditeur de cours à trois modes et la persistance des quiz générés, conformément à la
  feuille de route du README.
- [ ] Brancher le paiement réel (Mobile Money et/ou carte), gérer les confirmations asynchrones et
  les erreurs de paiement, et conserver l’activation manuelle comme parcours explicite si nécessaire.
- [ ] Compléter les tests de bout en bout pour les parcours enseignant, élève, parent et tuteur, sur
  mobile et avec les principaux cas d’autorisation par espace.

## Avant chaque mise en production

- [ ] Vérifier les migrations et restaurations de sauvegarde sur une copie de données.
- [ ] Vérifier les URLs internes, TLS, secrets, limites de ressources et health checks propres à
  l’environnement.
- [ ] Confirmer que le catalogue MCP et `AllowedImages` ne contiennent que des extensions approuvées,
  que le proxy ne permet que les domaines requis et que les images sont préchargées par digest.
- [ ] Revoir les règles de conservation et de suppression des conversations, pièces jointes,
  journaux et données de progression.
- [ ] Documenter le déploiement, la restauration après incident, la rotation des clés et la révocation
  d’une extension ou d’un grant.
