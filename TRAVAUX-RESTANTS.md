# Travaux restants — GPOE26

Ce document regroupe les prochaines étapes du produit, du harness d'agents et de son exploitation.
Il complète la feuille de route résumée dans le [README](README.md) et distingue les fondations déjà
présentes des livrables encore nécessaires. L'ordre proposé privilégie d'abord la sécurité et la
fiabilité avant l'activation d'extensions externes ou le déploiement en production.

## 1. Priorité immédiate — finaliser la sécurité MCP

- [ ] **Évaluer une version maintenue de `mcp/fetch`** : sélectionner une révision du dépôt amont,
  vérifier le commit, les protections SSRF/DNS, les dépendances, le Dockerfile et la licence. Résoudre
  l'écart déjà observé entre les métadonnées de licence Docker Hub et le fichier `LICENSE` amont.
- [ ] **Construire une image reproductible** depuis la révision retenue, l'épingler par digest et
  documenter la chaîne de provenance. Ne pas réutiliser l'ancienne image `mcp/fetch:latest` auditée.
- [ ] **Valider l'image dans le sandbox avant approbation** : source autorisée accessible, hôte absent
  de l'allowlist refusé, accès Internet direct impossible, destinations privées bloquées et limites
  Docker effectivement appliquées. L'image ne doit entrer dans `AllowedImages` et le catalogue qu'après
  réussite de ces contrôles.
- [ ] **Distinguer les états MCP** : séparer clairement le consentement, la vérification de la
  configuration, l'autorisation de lancement et l'état d'exécution/santé. `IsSandboxed` ne doit pas
  laisser entendre qu'un conteneur a été lancé ou testé.
- [ ] **Gérer le cycle de vie des réseaux egress** : nettoyer de façon sûre les réseaux dédiés devenus
  inactifs après révocation, tout en préservant les réseaux utilisés par un processus MCP actif.
- [ ] **Revoir l'allowlist de sortie** avant chaque déploiement : elle autorise des hôtes entiers, pas
  des chemins précis. Toute nouvelle source doit être justifiée et testée.

## 2. Fiabilité des espaces et des API

- [ ] **Formaliser les migrations de schéma** pour User et les autres services : stratégie de
  migration versionnée, exécution au déploiement, reprise après échec et compatibilité pendant les
  mises à jour. Le test de rétrocompatibilité/backfill des espaces, actuellement ignoré, doit être
  exécuté contre une base de test réelle et redevenir obligatoire.
- [ ] **Compléter la matrice d'accès des espaces** Personal, Family et Organization pour chaque
  endpoint et action d'interface. Tester les rôles Owner, Guardian, Admin d'espace, enseignant, élève
  et les refus attendus ; le rôle global ne doit pas se substituer à une autorisation d'espace.
- [ ] **Tester les changements de contexte et les erreurs interservices** : changement d'espace et
  renouvellement du JWT, adhésion révoquée, JWT périmé, User indisponible (503) et absence de fuite
  entre deux utilisateurs ou espaces.
- [ ] **Vérifier l'expérience de session expirée et invalide** dans l'interface : message explicite,
  réauthentification, perte de droits après révocation et absence de fallback permissif vers le rôle
  global.
- [ ] **Automatiser les scénarios bout en bout** principaux des services User, Cours, Chat et Quiz,
  incluant l'intégration Aspire, les parcours utilisateur et les permissions workspace.

## 3. Harness d'agents et expérience d'apprentissage

- [ ] **Faire évoluer les profils pédagogiques** au-delà du catalogue en mémoire : définir comment
  les profils prennent en compte le niveau, la langue, les objectifs, les préférences et le contexte
  de l'utilisateur, et comment l'utilisateur ou l'administrateur peut les personnaliser.
- [ ] **Définir le cycle de vie de la mémoire pédagogique** : quelles informations sont conservées,
  leur durée, leur visibilité et leur suppression. Distinguer cette mémoire du simple historique de
  conversation et obtenir un consentement adapté.
- [ ] **Élargir les outils éducatifs selon les besoins** : création et amélioration de cours,
  exercices, plans d'étude et recherche documentaire. Chaque outil doit avoir des contrats, des
  autorisations, des limites et des tests propres ; ne pas accorder à un outil d'écriture les droits
  d'un outil de lecture.
- [ ] **Évaluer la qualité pédagogique** avec des jeux de scénarios représentatifs : exactitude,
  adaptation au niveau, qualité des indices, refus d'inventer des sources, robustesse aux contenus
  non fiables et cohérence entre fournisseurs de modèles.
- [ ] **Documenter et superviser le changement de fournisseur LLM** : configuration, gestion des
  secrets, modèles texte/vision, délais, quotas, erreurs, coût et stratégie de repli.

## 4. Fonctions produit planifiées

- [ ] **Finaliser l'inscription et l'onboarding** selon les parcours réellement attendus (élève,
  enseignant, parent et organisation), notamment les niveaux/classes et le système bilingue indiqué
  dans la feuille de route du README.
- [ ] **Compléter l'expérience de création de cours** : import PDF/DOCX/image vers un contenu éditable,
  étapes de validation humaine, éditeur à plusieurs modes et tests de qualité d'extraction.
- [ ] **Persister et administrer les quiz générés** : modèle de données, édition, publication,
  permissions et suivi des résultats.
- [ ] **Construire le plan d'études et la progression personnalisée**, puis relier les recommandations
  du tuteur aux cours, aux évaluations et aux objectifs de l'apprenant.
- [ ] **Développer l'application Windows hybride** (.NET MAUI Blazor Hybrid) : authentification,
  lecture hors ligne, stockage local SQLite, synchronisation et résolution des conflits au retour
  du réseau.
- [ ] **Brancher les paiements** (Mobile Money et carte) avec confirmation par webhook, idempotence,
  rapprochement des transactions et activation fiable des abonnements. Tant que ce travail n'est pas
  livré, l'activation administrative reste le mécanisme prévu.

## 5. Préparation de la production

- [ ] **Sécuriser les secrets et la protection des données** : fournir une clé HMAC forte, stocker et
  faire tourner les secrets dans un gestionnaire adapté, persister le trousseau ASP.NET Data
  Protection et le partager correctement entre instances.
- [ ] **Isoler l'exécution Docker en production** : préférer un daemon rootless ou une VM dédiée,
  limiter l'accès au socket Docker, appliquer des quotas et séparer les charges MCP du serveur Chat.
- [ ] **Définir l'exploitation des données** : sauvegardes PostgreSQL, restauration testée, rétention,
  chiffrement, supervision des bases et procédure de mise à jour des schémas.
- [ ] **Compléter l'observabilité et les alertes** : disponibilité des services, erreurs de validation
  d'espace, appels LLM, limites et timeouts MCP, refus du proxy egress, capacité des sessions et
  consommation de ressources, sans journaliser les messages, pièces jointes ou secrets.
- [ ] **Créer une procédure de mise en production** : configuration par environnement, migrations,
  déploiement progressif, retour arrière, vérifications de santé et réponse aux incidents.

## État déjà présent

Les éléments suivants sont des fondations livrées et ne sont donc pas comptés comme tâches à refaire :

- services User, Cours, Chat et Quiz, interface Blazor et orchestration Aspire ;
- espaces Personal, Family et Organization, claims JWT et validation live du contexte par les API ;
- contrats de runtime indépendants du fournisseur, runtime MAF, profils et instrumentation ;
- tuteur avec historique, outil de lecture du cours et sélection de modèles texte/vision ;
- autorisations MCP liées à l'utilisateur et à l'espace, parcours d'approbation/révocation et
  adaptateur MCP en lecture seule ;
- sandbox Docker fermé par défaut, images épinglées, réseau interne par extension et proxy Squid
  avec allowlist ;
- tests de solution : `dotnet test GPOE26.slnx` (75 réussis, 1 ignoré dans GPOE26.Tests ; 24 réussis
  dans Quiz.Tests lors de la dernière validation).

La liste est une feuille de route, pas une affirmation que chaque élément est bloquant pour tout
déploiement. Les travaux MCP et production sont toutefois des prérequis avant d'autoriser des serveurs
communautaires ou d'exposer le service à des utilisateurs réels.
