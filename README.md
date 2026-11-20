# GPOE26

Plateforme scolaire et d'apprentissage en ligne : site vitrine de l'établissement (actualités, événements, hiérarchie…), cours numériques avec tuteur IA, quiz générés automatiquement, et suivi des élèves par leurs enseignants et leurs parents.

Construite en **.NET 10** : frontend **Blazor Server**, microservices **ASP.NET Core**, base **PostgreSQL**, orchestration locale avec **.NET Aspire**.

---

## Architecture

```mermaid
flowchart TB
    Browser["Navigateur"]
    UI["GPOE26.Web + GPOE26.UI<br/>Blazor Server"]
    Api["GPOE26.ApiService<br/>API du site"]
    User["User API<br/>comptes · JWT · espaces · autorisations"]
    Courses["Cours API<br/>cours · PDF · partage"]
    Chat["Chat API<br/>tuteur · conversations"]
    Quiz["Quiz API<br/>évaluations · corrections · progression"]
    Agent["Harness d'agent<br/>profils · routage · outils"]
    Maf["Runtime MAF<br/>Microsoft Agent Framework"]
    Mcp["Runtime MCP<br/>adaptateur lecture seule"]
    Docker["Sandbox Docker<br/>serveur MCP"]
    Proxy["Proxy egress Squid<br/>allowlist réseau"]
    LLM["API de modèle compatible OpenAI<br/>OpenRouter / fournisseur configuré"]
    DB[("PostgreSQL<br/>bases isolées par service")]
    Aspire["GPOE26.AppHost<br/>orchestration .NET Aspire"]

    Browser --> UI
    UI -->|"HTTP + JWT"| Api
    UI -->|"HTTP + JWT"| User
    UI -->|"HTTP + JWT"| Courses
    UI -->|"HTTP + JWT"| Chat
    UI -->|"HTTP + JWT"| Quiz
    Chat --> Agent
    Agent --> Maf
    Maf -->|"API modèle"| LLM
    Maf -->|"outils métier autorisés"| Chat
    Chat -->|"GET /cours/{id}<br/>JWT de l'élève"| Courses
    Chat -->|"GET /workspaces/context<br/>JWT de l'élève"| User
    Mcp -->|"grant + options<br/>JWT + HMAC Chat–User"| User
    Chat --> Mcp
    Mcp -->|"MCP sur stdio"| Docker
    Docker -->|"HTTP(S) via réseau interne"| Proxy
    Proxy -->|"hôtes explicitement autorisés"| Internet["Ressources pédagogiques externes"]
    Api --> DB
    User --> DB
    Courses --> DB
    Chat --> DB
    Quiz --> DB
    Aspire -.-> UI
    Aspire -.-> Api
    Aspire -.-> User
    Aspire -.-> Courses
    Aspire -.-> Chat
    Aspire -.-> Quiz
    Aspire -.-> DB
```

| Projet | Port | Rôle |
|---|---|---|
| `GPOE26.Web` | 5000 | Interface Blazor Server (site public, espace d'apprentissage, gestion) |
| `GPOE26.ApiService` | 5455 | Actualités, événements, discours, activités, hiérarchie, contacts, upload d'images |
| `User` | 5197 | Inscription, connexion, profil — émet les jetons JWT |
| `Cours` | 5188 | CRUD des cours (sections Markdown ou PDF, extraction du texte via PdfPig), partage avec les classes |
| `Chat` | 5212 | Tuteur IA : discussions conservées par élève et par cours (base `chatdb`), photos d'exercices, résumés, brouillons de cours |
| `Quiz` | 5163 | Évaluations des enseignants (quiz, devoirs, épreuves : base `quizdb`), correction automatique et assistée par IA, suivi de la maîtrise ; génération de quiz par IA |
| `GPOE26.AppHost` | — | Orchestrateur Aspire (PostgreSQL + pgAdmin + tous les services) |
| `GPOE26.ServiceDefaults` | — | Télémétrie, health checks, résilience HTTP partagés |
| `GPOE26.Tests` | — | Tests d'intégration Aspire |
| `GPOE26.Agent.Abstractions` | — | Contrats indépendants du runtime : profils, exécutions, outils, extensions |
| `GPOE26.Agent.Runtime.Maf` | — | Implémentation de `IAgentRuntime` avec Microsoft Agent Framework |
| `GPOE26.Agent.Runtime.Mcp` | — | Adaptateur MCP en lecture seule et lancement par sandbox Docker |
| `GPOE26.Workspaces.Abstractions` | — | Contexte et validation partagés des espaces actifs |

### Flux du tuteur et frontières d'API

```mermaid
sequenceDiagram
    actor Élève
    participant UI as GPOE26.UI
    participant Chat as API Chat
    participant User as API User
    participant Cours as API Cours
    participant Runtime as Harness / runtime MAF
    participant LLM as API du fournisseur LLM
    participant MCP as Adaptateur MCP
    participant Docker as Sandbox Docker
    participant Proxy as Proxy egress

    Élève->>UI: Question dans le cours
    UI->>Chat: POST /tutor/courses/{courseId}/messages (JWT)
    Chat->>User: GET /workspaces/context (JWT utilisateur)
    User-->>Chat: espace actif, adhésion et rôles validés
    Chat->>Cours: GET /cours/{courseId} (JWT utilisateur)
    Cours->>User: Validation du contexte d'espace
    Cours-->>Chat: cours accessible
    Chat->>Runtime: profil, niveau, langue, contexte et outils permis
    Runtime->>LLM: requête de génération (API HTTP)
    opt Un outil MCP de lecture est autorisé par le profil
        LLM-->>Runtime: demande d'outil
        Runtime->>MCP: mcp.read (identité et espace du JWT)
        MCP->>User: GET /internal/workspaces/{id}/mcp/grant et /options<br/>(JWT + signature HMAC)
        User-->>MCP: autorisation et configuration vérifiées
        MCP->>Docker: protocole MCP sur stdio, dans le conteneur isolé
        Docker->>Proxy: HTTP(S) via réseau Docker interne dédié
        Proxy-->>Docker: réponse d'un hôte autorisé
        Docker-->>MCP: résultat de lecture
        MCP-->>Runtime: résultat traité comme donnée non fiable
        Runtime->>LLM: résultat et demande de réponse finale
    end
    LLM-->>Runtime: explication pédagogique
    Runtime-->>Chat: événements d'exécution et réponse
    Chat-->>UI: réponse enregistrée
    UI-->>Élève: affichage de la réponse
```

Les appels **du navigateur vers les services** sont des API HTTP authentifiées par JWT. Les appels
inter-services vers User transmettent le JWT de l'utilisateur pour revérifier l'identité, l'espace et
l'adhésion ; les services Cours, Chat et Quiz échouent fermés si cette validation est indisponible.
Le pont interne d'autorisation MCP ajoute une signature HMAC courte durée Chat–User. Le runtime appelle
le fournisseur LLM par HTTP ; les serveurs MCP, eux, parlent MCP sur stdio et ne reçoivent pas le JWT.
La base PostgreSQL est partagée comme infrastructure par Aspire, mais chaque service conserve ses
propres données et son contexte. Docker et le proxy egress sont des dépendances opérées séparément,
pas des endpoints publics de l'application.

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
dotnet user-secrets set "Parameters:llm-apikey" "<votre-clé-openrouter>"
dotnet user-secrets set "Parameters:mcp-chat-service-key" "<secret-aléatoire-de-32-octets-minimum>"
dotnet run
```

Le tableau de bord Aspire s'ouvre et démarre PostgreSQL (volume persistant), pgAdmin et tous les services. Dans Visual Studio, définissez simplement `GPOE26.AppHost` comme projet de démarrage.

### Option 2 — Script (Replit ou serveur sans Docker)

```bash
export PGHOST=... PGPORT=5432 PGDATABASE=... PGUSER=... PGPASSWORD=...
export LLM_API_KEY=...   # clé OpenRouter
bash start.sh
```

Le script lance les 5 services backend, attend 30 s, puis démarre le frontend sur le port 5000. Voir `replit.md` pour les détails propres à Replit.

---

## Configuration

Aucun secret ne doit être commité. Copiez `.env.example` en `.env` (ignoré par git) ou utilisez les *user-secrets* .NET.

| Variable | Service(s) | Description |
|---|---|---|
| `LLM_API_KEY` | Chat, Quiz | Clé OpenRouter (transmise en `LLM__ApiKey`) ; `DEEPSEEK_API_KEY` reste accepté par `start.sh` |
| `LLM__BaseUrl`, `LLM__Models`, `LLM__VisionModels` | Chat, Quiz | Fournisseur compatible OpenAI (OpenRouter par défaut) et listes de modèles **essayés dans l'ordre** : si un modèle gratuit est saturé (429) ou muet, le suivant prend le relais. Les modèles de vision servent dès qu'une photo est jointe. `LLM__AttemptTimeoutSeconds` (90 par défaut) borne chaque essai |
| `LlmProvider` | Chat | Anciens écrans `/learning` : `OpenRouter` (défaut, même client que le tuteur), `DeepSeek`, `Claude`, `Gpt`, `Foundry` |
| `ConnectionStrings__ChatDB` | Chat | Base des discussions du tuteur (fournie par Aspire) |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` | User, Cours | Clé de signature JWT (≥ 256 bits). **La valeur par défaut est un exemple : à changer en production.** |
| `ConnectionStrings__UserDB` / `__CoursDB` / `__LyceeDB` | User, Cours, ApiService | Chaînes de connexion PostgreSQL (fournies automatiquement par Aspire) |
| `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` | `start.sh` | Utilisées pour construire les chaînes de connexion |

---

## Structure du dépôt

```
GPOE26.slnx                 Solution
GPOE26.AppHost/             Orchestrateur Aspire
GPOE26.ServiceDefaults/     Configuration commune des services
GPOE26.Agent.Abstractions/  Contrats neutres pour les profils, exécutions et outils des agents
GPOE26.Agent.Runtime.Maf/   Adaptateur du runtime Microsoft Agent Framework
GPOE26.UI/                  Espace d'apprentissage (bibliothèque Razor partagée Web / Windows)
GPOE26.Web/                 Frontend Blazor (site + hôte de GPOE26.UI)
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

### Extensions du harness d'agents

`GPOE26.Agent.Abstractions` définit un manifeste versionné pour les extensions de type serveur MCP,
skill ou profil, ainsi qu'un registre en mémoire qui valide leur identité, version, provenance,
capacités, paramètres et permissions déclarées. Ce registre décrit des métadonnées uniquement :
un manifeste valide n'installe pas l'extension et n'accorde aucun droit. L'hôte devra appliquer sa
propre politique d'approbation et d'autorisation avant toute connexion ou exécution. Les valeurs
secrètes de configuration ne font pas partie des manifestes.

`GPOE26.Agent.Runtime.Mcp` utilise le SDK C# officiel MCP pour négocier le protocole et découvrir les
outils. L'adaptateur n'accepte que le principal authentifié de la requête, compare son utilisateur et
son espace actif au principal HTTP, revalide l'adhésion auprès de User et interroge un registre de
permissions approuvées par utilisateur et espace à chaque appel. Les outils doivent être déclarés
dans le manifeste, explicitement autorisés, annoncer `readOnlyHint` et avoir un schéma identique à
celui approuvé. Le lancement passe maintenant par `IMcpSandboxClientFactory` ; l'adaptateur ne lance
plus directement une commande sur l'hôte. Sans fournisseur de sandbox configuré, l'exécution échoue
fermée. Les arguments, résultats, timeouts et sessions ont des limites configurées ; les journaux ne
contiennent ni contenu ni secrets.

User persiste les grants par utilisateur, espace et extension. Les endpoints
`POST /workspaces/{workspaceId}/mcp/extensions/{extensionId}/approve` et
`.../revoke` revalident les adhésions et rôles actifs : le propriétaire approuve dans Personal,
un Guardian dans Family, et seul un rôle `Admin` de l'espace Organization (s'il a été attribué)
peut approuver pour cet espace. Le rôle global n'accorde aucun droit workspace. Les options de
processus internes sont protégées par Data Protection en base ; la clé doit rester disponible
après redémarrage et être partagée entre instances. Aucun endpoint public ne permet de configurer
ou lancer un exécutable. Les lectures de grants et d'options utilisées par Chat sont exposées
uniquement sur `/internal/workspaces/{workspaceId}/mcp/{grant|options}` ; elles exigent le JWT
utilisateur avec son espace actif et une signature HMAC Chat–User limitée à la requête et valable
30 secondes. La clé partagée se configure comme paramètre secret Aspire `mcp-chat-service-key`
(configuration injectée `Mcp:ChatServiceKey`), doit contenir au moins 32 octets aléatoires et
n'est jamais transmise dans la requête.

L'ancienne frontière de processus seule **n'était pas un sandbox système** : le serveur MCP conservait
les droits OS du processus hôte. Le fournisseur Docker est maintenant enregistré dans Chat. Il lance
uniquement des images OCI autorisées par configuration et épinglées par digest, sans montage de
fichiers hôte, avec utilisateur non-root, système de fichiers en lecture seule, capacités supprimées,
limites CPU/mémoire/processus et réseau désactivé par défaut. Le runtime refuse le mode `bridge`.
En mode egress, il crée un réseau Docker interne propre au triplet utilisateur/espace/extension,
relie le proxy à ce seul réseau puis y lance le conteneur MCP.
Le proxy Squid limite les méthodes aux lectures Web, les ports à HTTP/HTTPS, les destinations aux
domaines autorisés et bloque les plages IP non publiques. Le jeu d'images autorisées est
vide par défaut : aucune image communautaire n'est exécutée tant que l'opérateur ne l'a pas autorisée.
Docker doit être accessible à l'hôte Chat. Les options de processus persistées utilisent maintenant
une référence d'image, pas une commande ni un chemin d'exécutable hôte.

Pour démarrer le proxy local : `docker compose -f tools/mcp-egress/compose.yaml up -d`.
Il n'expose aucun port sur l'hôte. Le conteneur MCP rejoint seulement le réseau interne ; le proxy
est le seul service également relié au réseau sortant. La configuration initiale autorise
`wikipedia.org`, `openstax.org`, `khanacademy.org`, `research.google`, `www.researchgate.net` et
`www.sciencedirect.com` (avec les sous-domaines de Wikipédia, OpenStax et Khan Academy). Les règles
Squid autorisent des hôtes, pas des chemins : l'URL ResearchGate ne peut donc pas être limitée au seul
chemin `/publication/`. Réviser cette liste avant tout déploiement. Pour une image MCP qui accepte
`HTTP_PROXY`/`HTTPS_PROXY`, configurer `NetworkMode: "egress"` ; le runtime injecte le proxy et
neutralise `NO_PROXY`. Les conteneurs en réseau `none` restent inchangés.
Le proxy garde un réseau sortant séparé, sans port publié. Chaque réseau interne ne contient que le
conteneur MCP concerné et le proxy ; il est étiqueté `gpoe.mcp.sandbox=true`. La révocation ne supprime
pas encore les réseaux inactifs ; l'opérateur peut les lister avec
`docker network ls --filter label=gpoe.mcp.sandbox=true`, puis retirer un réseau précis avec
`docker network rm <nom>` après avoir vérifié qu'aucun MCP ne tourne dessus.

Le tuteur expose un outil générique `mcp.read` uniquement aux profils qui l'autorisent et seulement
avec les cibles retournées par l'adaptateur après vérification des grants et de la configuration.
Chaque lecture revalide l'identité, l'espace et le consentement ; les résultats MCP sont traités comme
des données non fiables. Les manifestes opérateur sont chargés depuis `Mcp:Extensions` dans Chat ;
la liste est vide par défaut. L'interface `/app/mcp` permet de consulter les extensions déclarées et
de gérer les autorisations selon le rôle actif :
soi-même en Personnel, Guardian en Famille, Admin en Organisation. Les listes de membres et de grants
sont limitées à ces règles côté API. Le catalogue et l'allowlist étant vides par défaut, MCP reste
désactivé jusqu'à sa configuration explicite par un administrateur. Les annotations de lecture seule
demeurent une déclaration du serveur, pas une preuve de comportement.
Les images doivent être préchargées par l'opérateur (le runtime utilise `--pull=never`). Le daemon Docker
constitue une frontière de confiance : en production, utiliser un daemon rootless dédié ou une VM isolée.
L'accès au groupe Docker rootful équivaut pratiquement à des privilèges administrateur sur l'hôte.

Les administrateurs disposent des endpoints `/admin/mcp/process-configurations` pour enregistrer, lister,
approuver pour exécution sandboxée ou retirer une configuration MCP. Une configuration est d'abord chiffrée en base avec les états
`IsVerified=false` et `IsSandboxed=false`; elle devient disponible au runtime uniquement après vérification
explicite. Cette approbation ne lance pas l'image : l'image doit aussi figurer exactement dans
`McpSandbox:AllowedImages` et être préchargée. Les références mutables, les variables réservées, les arguments excessifs et les secrets hors
limites sont refusés. La réponse de liste ne contient ni payload chiffré ni secret. Les endpoints restent
réservés au rôle global `Admin`.

Pour le test local, `Chat/appsettings.Development.json` déclare uniquement
`gpoe/mcp-test@sha256:a54841ec3108c3fec04d59c986ffffaf077497f94c56d84679acef3ddf99370d`
comme image sans réseau. L'image est construite depuis `tools/GpoeMcpTestServer/`. Elle expose une
capacité `test_read` en lecture seule, sans réseau ni fichier hôte. La commande Docker reste vide :
l'image utilise son `ENTRYPOINT` .NET pour lancer le serveur MCP stdio.

L'AppHost active le bundle Aspire CLI (`AspireUseCliBundle=true`) pour éviter l'avertissement
`ASPIRE010`. Les tests `WebTests` démarrent l'application distribuée et vérifient les pages `/mcp`,
`/admin/mcp` ainsi que le refus sans JWT des endpoints MCP.

Les fichiers envoyés par les utilisateurs sont enregistrés dans `*/wwwroot/uploads/` et ne sont pas versionnés.

---

## Espace d'apprentissage (`GPOE26.UI`)

L'interface des élèves, enseignants et parents est une **bibliothèque Razor** sans dépendance au serveur ASP.NET : elle est hébergée par `GPOE26.Web` aujourd'hui et le sera par l'application Windows (MAUI Blazor Hybrid) en phase 4.

| Adresse | Écran |
|---|---|
| `/register`, `/login` | Inscription guidée en questions-réponses (élève, enseignant, parent), connexion |
| `/app` | Tableau de bord selon le rôle : plan d'études (élève), mes classes (enseignant), suivi des enfants (parent) |
| `/app/cours`, `/app/cours/{id}` | Cours partagés / mes cours et partage avec les classes ; lecture avec sommaire « Sur cette page » |
| `/app/evaluations` | Évaluations de l'élève, ou épreuves et devoirs de l'enseignant |
| `/app/evaluations/nouvelle`, `…/{id}/modifier` | Éditeur d'épreuve (6 types de questions, paramètres, publication) |
| `/app/evaluations/{id}/composer` | Composition (une question par écran, minuteur, envoi automatique à l'échéance) |
| `/app/evaluations/{id}/copies[/{copie}]` | Copies et correction (proposition de l'IA), ou copie corrigée de l'élève |
| `/app/classes/{id}` | Suivi d'une classe : résultats, maîtrise par cours, élèves à accompagner |
| `/app/abonnement` | Accès Premium, activation d'une clé, souscription, essai |
| `/app/tuteur` | Tuteur IA : toutes les discussions (une par cours) et les explications épinglées |
| `/app/tuteur/{coursId}` | Relecture d'une discussion en pleine page (lisible même sans abonnement) |

- **Architecture** : les pages consomment des interfaces (`IAccountApi`, `IClassroomApi`, `IFamilyApi`, `ICourseApi`, `IAssessmentApi`, `IProgressApi`) implémentées en HTTP ; l'application Windows pourra les décorer d'un cache hors ligne.
- **Session** : `SessionState` (une par circuit), persistée par l'hôte via `ISessionStore` — côté Web, dans le `localStorage` du navigateur, chiffrée par la protection des données ASP.NET. Le site fonctionne en un seul circuit interactif sans prérendu (`App.razor`), si bien que la connexion survit à la navigation et au rechargement. Le sélecteur d'espace de la navigation charge les adhésions actives et remplace le JWT après sélection.
- **Fuseau horaire** : les dates sont affichées et saisies dans le fuseau `App:TimeZone` (`Africa/Douala` par défaut), pas celui du serveur.
- **Sécurité** : le Markdown des cours est rendu sans HTML brut (un cours ne peut pas injecter de script chez les élèves).
- **Tuteur IA** (bouton « Demander au tuteur IA » dans le lecteur de cours, tiroir plein écran sur mobile) : chaque question et chaque réponse sont enregistrées (service Chat, base `chatdb`). En rouvrant le cours, l'élève retrouve la discussion ; il peut épingler les explications utiles, joindre la **photo d'un exercice traité sur papier** (le tuteur retranscrit, vérifie la démarche ligne par ligne et donne un indice) ou effacer la discussion. Formules rendues avec KaTeX. Poser une question demande Premium ; relire reste gratuit.
- **Copies sur papier** : pour une réponse ouverte, l'élève photographie sa copie (jusqu'à 4 pages, image réduite dans le navigateur avant l'envoi). L'IA en affiche la lecture pour qu'il la vérifie avant de rendre. L'enseignant voit les photos et peut demander une proposition de note à l'IA, qui lit les images. Une fois les résultats publiés, l'élève peut demander « Analyser ma démarche avec l'IA » (si l'enseignant a activé la correction assistée).
- Les pages historiques du site (`/cours`, `/learning`, blog…) restent disponibles ; la connexion est partagée.

---

## API du service User (comptes)

| Domaine | Endpoints |
|---|---|
| Authentification | `POST /auth/register` (inscription guidée : profil, classes initiales, clé d'abonnement, code de classe, essai), `POST /auth/login`, `GET/PUT/PATCH /auth/me` |
| Espaces | `GET /workspaces/mine`, `GET /workspaces/{id}/membership`, `GET /workspaces/context` (vérification de l'adhésion, des rôles et du rôle global actuels), `POST /workspaces/{id}/select` (nouveau JWT avec `workspace_id`, `workspace_kind` et claims répétés `workspace_role` ; connexion et inscription sélectionnent l'espace personnel par défaut). Cours, Quiz et Chat valident le contexte auprès de User sur chaque route protégée et répondent 503 si User est indisponible. |
| Classes et groupes | `POST /classrooms`, `GET /classrooms/mine`, `GET /classrooms/{id}`, `GET /classrooms/{id}/members`, `POST /classrooms/join`, `POST /classrooms/{id}/invite-code`, `DELETE /classrooms/{id}`, `DELETE /classrooms/{id}/members/{studentId}` |
| Parents | `POST /parents/children/link` (code de l'élève), `POST /parents/children` (créer le compte de l'enfant), `GET /parents/children`, `DELETE /parents/children/{linkId}` ; côté élève `GET /me/parents`, `POST /me/parents/{linkId}/approve` ou `/reject` |
| Abonnements | `GET /subscriptions/me`, `POST /subscriptions/redeem`, `POST /subscriptions/checkout`, `POST /subscriptions/trial` |
| Administration | `POST/GET /admin/organizations`, `POST /admin/organizations/{id}/licenses`, `POST /admin/subscriptions/{id}/activate` |

Les autorisations et les actions visibles dans l'application dépendent des rôles de l'espace actif ; le rôle global du compte reste utilisé pour le profil et la personnalisation.

### Tuteur IA (service Chat)

| Endpoint | Rôle |
|---|---|
| `GET /tutor/conversations` | Mes discussions (une par cours), avec aperçu et nombre d'explications épinglées |
| `GET /tutor/courses/{courseId}` | Discussion complète sur un cours |
| `POST /tutor/courses/{courseId}/messages` | Poser une question (Premium), éventuellement avec `imageId` ; le contenu du cours est lu dans le service Cours avec le jeton de l'élève (il n'accède qu'à ses cours) |
| `POST /tutor/images`, `GET /tutor/images/{id}` | Photo d'un exercice (JPEG/PNG/WebP, 6 Mo max), visible de son seul auteur |
| `PUT /tutor/messages/{id}/pin`, `GET /tutor/pinned` | Épingler une explication, liste des explications épinglées |
| `DELETE /tutor/courses/{courseId}` | Effacer la discussion d'un cours |

Premium est lu dans le JWT, puis vérifié auprès du service User s'il dit non (clé activée après la connexion).

### Évaluations (service Quiz) et partage des cours (service Cours)

| Qui | Endpoints |
|---|---|
| Enseignant | `POST /assessments`, `PUT /assessments/{id}` (tant qu'aucun élève n'a composé), `POST /assessments/{id}/publish`, `POST /assessments/{id}/close`, `DELETE /assessments/{id}`, `GET /assessments/{id}/submissions`, `PUT /assessments/{id}/submissions/{sid}/answers/{qid}/grade`, `POST …/ai-suggestion`, `GET /progress/classrooms/{classroomId}` ; Cours : `PUT /cours/{id}/sharing` |
| Élève | `GET /assessments/mine`, `POST /assessments/{id}/attempts` (démarrer ou reprendre), `POST /assessments/{id}/attempts/{sid}/questions/{qid}/photos` (photo de copie, transcrite par l'IA), `DELETE …/attempts/{sid}/photos/{pid}`, `POST /assessments/{id}/submissions/{sid}/answers/{qid}/ai-feedback` (analyse après publication), `POST /assessments/{id}/attempts/{sid}/submit`, `GET /assessments/{id}/submissions/{sid}`, `GET /progress/me` ; Cours : `GET /cours/shared`, `GET /cours/{id}` |
| Parent | `GET /progress/students/{studentId}` (enfant rattaché) |

- **Types de questions** : QCM, choix multiples (tout ou rien), vrai/faux, réponse courte (casse, accents et ponctuation ignorés), numérique (virgule, espaces, unité et tolérance acceptés), réponse ouverte (texte ou photo de copie, corrigée par l'enseignant).
- **Correction assistée** : sur demande, l'IA propose une note (arrondie au quart de point) et un commentaire selon les critères de l'enseignant ; rien n'est appliqué sans sa validation.
- **Publication des résultats** : immédiate pour un entraînement ; sinon après la date limite ou la clôture. La vue élève ne contient jamais les bonnes réponses avant.
- **Suivi** : note de la meilleure tentative ramenée sur 20, moyenne, et maîtrise par cours (points obtenus sur les questions liées au cours).
- Le service Quiz interroge le service User avec le jeton de l'utilisateur (classes, élèves, rattachements) : il a besoin de `Services__User` et de la même configuration `Jwt`.

Le JWT contient désormais `premium` (`true`/`false`) et `plan` : les services Chat et Quiz pourront réserver les fonctions Premium aux abonnés sans interroger la base User.

Tant que le paiement n'est pas branché (phase 5), un abonnement souscrit reste `PendingPayment` jusqu'à ce qu'un administrateur l'active.

**Données de démonstration (environnement Development uniquement)** — mot de passe `Test1234!` :
`admin@gpoe26.cm` (Admin), `prof@gpoe26.cm` (Enseignant), `eleve@gpoe26.cm` (Élève), `parent@gpoe26.cm` (Parent, rattaché à l'élève), classe « Terminale C » (code `DEM-TLC`), licence du « Lycée de démonstration » (clé `ETB-DEMO-PROF`, 50 places).

En production, aucun compte Admin n'est créé automatiquement : promouvoir un compte existant avec
`UPDATE "AppUsers" SET "Role" = 'Admin' WHERE "Email" = '...';`

---

## Tests

```bash
dotnet test GPOE26.slnx
```

Le test existant démarre l'AppHost complet (Docker requis) et vérifie que la page d'accueil répond.

`Quiz.Tests` contient les tests unitaires de la correction automatique (`dotnet test Quiz.Tests`). Le script `scripts/test-evaluations.sh` rejoue les parcours de la phase 1 bis (63 vérifications : partage des cours, création et publication, droits d'accès, tentatives, correction manuelle et assistée, publication des résultats, suivi enseignant/élève/parent).

Le script `scripts/ui-e2e.js` (Playwright) rejoue dans un navigateur le parcours complet : inscription d'un enseignant, partage d'un cours, création et publication d'un devoir, inscription d'un élève sur mobile, lecture, composition, correction assistée, clôture, rattachement d'un parent et suivi — `node scripts/ui-e2e.js <dossier-captures>` sur une pile démarrée avec des bases neuves.

Le script `scripts/ui-tutor-e2e.js` rejoue avec un vrai LLM (17 étapes) le tuteur et les copies photographiées : question au tuteur, photo d'exercice analysée, épinglage, discussion retrouvée après fermeture du cours, liste et explications épinglées, composition avec photo transcrite, analyse de la démarche, proposition de note de l'enseignant, version mobile. Préparer les données avec `python3 scripts/seed-tutor-demo.py > ids.json` (cours de physique, exercice, élève Premium), puis `node scripts/ui-tutor-e2e.js <captures> ids.json scripts/fixtures/copie-mruv.jpg scripts/fixtures/copie-chute.jpg`.

Le script `scripts/test-user-api.sh` rejoue les parcours de la phase 1 sur le service User (57 vérifications : inscriptions, clés, places, rôles, rattachements) — voir l'en-tête du script pour le lancer.

---

## Feuille de route

La liste détaillée des validations, décisions et fonctionnalités restantes est tenue dans
[`TRAVAUX_RESTANTS.md`](TRAVAUX_RESTANTS.md).

La comparaison entre l’architecture GPOE26 actuelle et le harness open source Pi est disponible dans
[`COMPARAISON_HARNESS_PI.md`](COMPARAISON_HARNESS_PI.md).

| Phase | Contenu | État |
|---|---|---|
| 0 | Nettoyage du dépôt, secrets hors du code, README | ✅ |
| 1 | ✅ (API) **Comptes et rôles** : Élève, Enseignant, Parent, Admin ; structures (établissement scolaire, autre institution : entreprise, startup, centre de formation…) ; classes et groupes avec code d'invitation ; lien parent ↔ enfant ; abonnements et **clés d'abonnement** (familiale, établissement/institution) ; plan d'études et progression : à faire | Backend fait, interface en phase 3 |
| 1 bis | ✅ (API) **Évaluations enseignant** : cours, devoirs et épreuves (quiz d'entraînement, devoir, épreuve notée, épreuve blanche) avec questions QCM, choix multiples, vrai/faux, réponse courte, numérique et réponse ouverte (correction manuelle assistée par IA) ; suivi de la maîtrise par élève et par chapitre ; partage des cours avec les classes | Backend fait, interface en phase 3 |
| 2 | **Maquettes** : inscription guidée en questions-réponses (système scolaire camerounais bilingue : classes, séries, BEPC/Probatoire/BAC, GCE O/A Level) ; tableaux de bord par rôle ; lecture de cours à 3 colonnes | À faire |
| 3 | ✅ Intégration Blazor : bibliothèque partagée `GPOE26.UI` (pages, API derrière des interfaces, session persistante), inscription guidée, tableaux de bord élève / enseignant / parent, lecture des cours, composition, éditeur d'épreuve, correction assistée, suivi de classe, abonnement, version mobile ; **tuteur IA avec discussions conservées et explications épinglées, copies photographiées lues par l'IA** (OpenRouter, modèles gratuits avec repli) | Fait (web) |
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
