# GPOE26

Plateforme scolaire et d'apprentissage en ligne : site vitrine de l'établissement (actualités, événements, hiérarchie…), cours numériques avec tuteur IA, quiz générés automatiquement, et suivi des élèves par leurs enseignants et leurs parents.

Construite en **.NET 10** : frontend **Blazor Server**, microservices **ASP.NET Core**, base **PostgreSQL**, orchestration locale avec **.NET Aspire**.

---

## Architecture

```
                        ┌────────────────────────┐
                        │   GPOE26.Web (Blazor)  │  :5000
                        └───────────┬────────────┘
        ┌──────────────┬────────────┼─────────────┬──────────────┐
        ▼              ▼            ▼             ▼              ▼
  ApiService        User          Cours          Chat           Quiz
   :5455           :5197         :5188          :5212          :5163
 (site école)   (comptes, JWT) (cours, PDF)  (tuteur IA)   (quiz IA)
        │              │            │             │              │
        └──────────────┴─────┬──────┘          LLM (DeepSeek, Claude, GPT…)
                             ▼
                        PostgreSQL
```

| Projet | Port | Rôle |
|---|---|---|
| `GPOE26.Web` | 5000 | Interface Blazor Server (site public, espace d'apprentissage, gestion) |
| `GPOE26.ApiService` | 5455 | Actualités, événements, discours, activités, hiérarchie, contacts, upload d'images |
| `User` | 5197 | Inscription, connexion, profil — émet les jetons JWT |
| `Cours` | 5188 | CRUD des cours (sections Markdown ou PDF, extraction du texte via PdfPig) |
| `Chat` | 5212 | Tuteur IA : réponses aux questions, résumés, brouillons de cours |
| `Quiz` | 5163 | Génération, correction et statistiques de quiz (stockage en mémoire pour l'instant) |
| `GPOE26.AppHost` | — | Orchestrateur Aspire (PostgreSQL + pgAdmin + tous les services) |
| `GPOE26.ServiceDefaults` | — | Télémétrie, health checks, résilience HTTP partagés |
| `GPOE26.Tests` | — | Tests d'intégration Aspire |

**Authentification** : JWT (issuer `GPOE2026`, audience `GPOE2026Users`) émis par `User` et validé par `Cours`. Le jeton contient le rôle et le niveau de l'utilisateur, que les services IA utilisent pour adapter leurs réponses.

**Rendu des cours** : Markdown via Markdig, coloration du code avec Prism.js, formules avec KaTeX, table des matières générée automatiquement.

---

## Démarrage

### Prérequis
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker (pour le PostgreSQL lancé par Aspire) **ou** un PostgreSQL existant (Replit)
- Une clé d'API pour le LLM (DeepSeek par défaut)

### Option 1 — Aspire (développement local, Visual Studio)

```bash
cd GPOE26.AppHost
dotnet user-secrets set "Parameters:deepseek-apikey" "<votre-clé>"
dotnet run
```

Le tableau de bord Aspire s'ouvre et démarre PostgreSQL (volume persistant), pgAdmin et tous les services. Dans Visual Studio, définissez simplement `GPOE26.AppHost` comme projet de démarrage.

### Option 2 — Script (Replit ou serveur sans Docker)

```bash
export PGHOST=... PGPORT=5432 PGDATABASE=... PGUSER=... PGPASSWORD=...
export DEEPSEEK_API_KEY=...
bash start.sh
```

Le script lance les 5 services backend, attend 30 s, puis démarre le frontend sur le port 5000. Voir `replit.md` pour les détails propres à Replit.

---

## Configuration

Aucun secret ne doit être commité. Copiez `.env.example` en `.env` (ignoré par git) ou utilisez les *user-secrets* .NET.

| Variable | Service(s) | Description |
|---|---|---|
| `DEEPSEEK_API_KEY` | Chat, Quiz | Clé du LLM (transmise en `DeepSeek__ApiKey` / `LLM__ApiKey`) |
| `LlmProvider` | Chat | `DeepSeek` (défaut dans `appsettings.json`), `Claude`, `Gpt`, `Foundry` |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` | User, Cours | Clé de signature JWT (≥ 256 bits). **La valeur par défaut est un exemple : à changer en production.** |
| `ConnectionStrings__UserDB` / `__CoursDB` / `__LyceeDB` | User, Cours, ApiService | Chaînes de connexion PostgreSQL (fournies automatiquement par Aspire) |
| `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` | `start.sh` | Utilisées pour construire les chaînes de connexion |

---

## Structure du dépôt

```
GPOE26.slnx                 Solution
GPOE26.AppHost/             Orchestrateur Aspire
GPOE26.ServiceDefaults/     Configuration commune des services
GPOE26.Web/                 Frontend Blazor
  Components/Pages/         Pages (Learning, Cours, Blog, Event, Activity, Hierarchy…)
  Components/UI/            MarkdownViewer, TableOfContents
  Services/                 ApiClient, AuthService
GPOE26.ApiService/          API du site de l'établissement
User/                       Comptes et authentification
Cours/                      Gestion des cours
Chat/                       Tuteur IA (services LLM interchangeables)
Quiz/                       Génération et correction de quiz
GPOE26.Tests/               Tests
scripts/post-merge.sh       Restore + build après un merge (Replit)
start.sh                    Lancement sans Aspire
```

Les fichiers envoyés par les utilisateurs sont enregistrés dans `*/wwwroot/uploads/` et ne sont pas versionnés.

---

## Tests

```bash
dotnet test GPOE26.slnx
```

Le test existant démarre l'AppHost complet (Docker requis) et vérifie que la page d'accueil répond.

---

## Feuille de route

| Phase | Contenu | État |
|---|---|---|
| 0 | Nettoyage du dépôt, secrets hors du code, README | ✅ |
| 1 | **Comptes et rôles** : Élève, Enseignant, Parent, Admin ; structures (établissement scolaire, autre institution : entreprise, startup, centre de formation…) ; classes et groupes avec code d'invitation ; lien parent ↔ enfant ; abonnements et **clés d'abonnement** (familiale, établissement/institution) ; plan d'études ; progression | À faire |
| 1 bis | **Évaluations enseignant** : cours, devoirs et épreuves (quiz d'entraînement, devoir, épreuve notée, épreuve blanche) avec questions QCM, choix multiples, vrai/faux, réponse courte, numérique et réponse ouverte (correction manuelle assistée par IA) ; suivi de la maîtrise par élève et par chapitre | À faire |
| 2 | **Maquettes** : inscription guidée en questions-réponses (système scolaire camerounais bilingue : classes, séries, BEPC/Probatoire/BAC, GCE O/A Level) ; tableaux de bord par rôle ; lecture de cours à 3 colonnes | À faire |
| 3 | Intégration Blazor : bibliothèque de composants partagée `GPOE26.UI`, services derrière des interfaces | À faire |
| 4 | **Application Windows hybride** (.NET MAUI Blazor Hybrid) : lecture des cours et exercices hors connexion (SQLite), synchronisation au retour du réseau | À faire |
| 5 | Paiement des abonnements (Mobile Money / carte) | Plus tard |
| 6 | Import PDF/DOCX/image → Markdown par IA, éditeur de cours à 3 modes, quiz persistés en base, tests | À faire |

**Modèle d'abonnement prévu (freemium)** : les cours partagés par les enseignants restent gratuits ; l'abonnement Premium débloque le tuteur IA, les quiz illimités, les épreuves blanches, le plan d'études et le suivi détaillé.

| Qui | Comment il accède |
|---|---|
| Élève | Gratuit, **ou** clé d'abonnement fournie par un parent ou l'établissement, **ou** abonnement payé lui-même |
| Parent | Paie l'abonnement et reçoit une **clé familiale** à transmettre à ses enfants |
| Enseignant | **Clé de licence** de son établissement ou de son institution (entreprise, startup…), **ou** abonnement enseignant individuel, **ou** essai gratuit |

L'interface est responsive : sur mobile, le menu latéral devient un tiroir (☰) et le sommaire « Sur cette page » se replie en haut de l'article.
