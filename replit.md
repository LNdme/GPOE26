# GPOE26 — Plateforme de gestion scolaire

Système de gestion scolaire basé sur .NET 10 / Blazor, déployé comme microservices sur Replit (sans Aspire orchestrator).

## Architecture

| Service | Port | Rôle |
|---------|------|------|
| **ApiService** | 5455 | API principale (contacts, actualités, événements, activités, hiérarchie) |
| **User** | 5197 | Authentification JWT, gestion des utilisateurs |
| **Cours** | 5188 | Gestion des cours (CRUD, sections Markdown, PDF) |
| **Chat** | 5212 | Tuteur IA (DeepSeek par défaut, configurable) |
| **Quiz** | 5163 | Génération et correction de quiz |
| **Web** | 5000 | Frontend Blazor Server (public face) |

## Base de données
- **PostgreSQL** unique partagé (schéma `public`)
- Tables : `AppUsers`, `Courses`, `CourseSections`, `Activities`, `Contacts`, `Events`, `Hierarchies`, `NewsArticles`, `Speeches`
- Chaque service crée ses tables via `CREATE TABLE IF NOT EXISTS` au démarrage (pas de migrations EF)
- `Courses.ContentType` stocké en `text` (enum avec `.HasConversion<string>()`)

## Authentification
- JWT avec clé 256 bits
- Issuer/Audience : `GPOE2026` / `GPOE2026Users`
- Token envoyé dans l'en-tête Authorization Bearer

## Système de cours (état actuel)
- Cours stockés avec `CourseSections` (Title, Paragraph, Image, Heading)
- Rendu Markdown riche via **Markdig** + **Prism.js** + **KaTeX**
- `MarkdownViewer.razor` : rendu HTML depuis Markdown avec AutoIdentifiers (IDs sur les titres)
- `TableOfContents.razor` : génère une nav cliquable depuis les titres
- `markdown-interop.js` : initialise Prism, KaTeX et la barre de progression de lecture
- `wwwroot/css/cours.css` : styles typographiques complets (titres, code, tableaux, blockquotes)

## Démarrage
```bash
bash start.sh
```
Le script compile et démarre les 6 services en parallèle. La Web attend 30s avant de démarrer pour laisser les backends s'initialiser.

## Variables d'environnement nécessaires
- `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` — PostgreSQL (auto-configuré sur Replit)
- `DEEPSEEK_API_KEY` — pour les services Chat (tuteur IA) et Quiz

## Prochaines étapes (session plan)
- T001 : Refonte modèle Course (MarkdownContent unifié, Status, SourceType)
- T002 : Service extraction IA (PDF/DOCX/images → Markdown via LLM)
- T003 : Endpoint upload multi-format
- T004 : Interface création cours (3 modes : éditeur MD, import fichier, dictée guidée)
