var builder = DistributedApplication.CreateBuilder(args);

// ── JWT partagé : même clé / issuer / audience pour User (émetteur) et les services
//    qui le valident (Cours, Chat).
var jwtKeyValue = "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";
var jwtIssuer   = "GPOE2026";
var jwtAudience = "GPOE2026Users";

// ── Clé OpenRouter ────────────────────────────────────────────────────────────
// Passerelle unique pour le chat, la vision et les embeddings.
// Déclarée en paramètre secret : sa valeur vit dans les user-secrets de l'AppHost
// (`dotnet user-secrets set Parameters:openrouter-api-key sk-or-...`), jamais dans
// un appsettings.json versionné.
var openRouterApiKey = builder.AddParameter("openrouter-api-key", secret: true);

// ── 1 seul serveur PostgreSQL, toutes les DBs dessus ─────────────────────────
//
// Image pgvector/pgvector plutôt que postgres : elle EST postgres, augmentée de
// l'extension `vector` dont dépend la recherche sémantique des cours.
//
// ⚠️ Le tag doit conserver la MÊME version majeure de PostgreSQL que le volume de
// données existant (WithDataVolume ci-dessous) : PostgreSQL refuse de démarrer sur
// un répertoire de données créé par une majeure différente. En cas d'erreur
// « database files are incompatible with server » au démarrage, alignez ce tag sur
// la majeure du volume, ou supprimez le volume pour repartir de zéro.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17")
    .WithDataVolume()
    .WithPgAdmin(); // interface admin dispo sur http://localhost:5050

var userDb = postgres.AddDatabase("userdb");
var coursDb = postgres.AddDatabase("coursdb");
var chatDb = postgres.AddDatabase("chatdb");
var harnessDb = postgres.AddDatabase("harnessdb");
var schoolDb = postgres.AddDatabase("LyceeDB");  // ApiService
// Quiz reste InMemory pour l'instant.

var apiService = builder.AddProject<Projects.GPOE26_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(schoolDb)
    .WaitFor(schoolDb);

var userService = builder.AddProject<Projects.User>("user")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithReference(userDb)
    .WaitFor(userDb);

// Cours porte le contenu ET son index vectoriel : il appelle OpenRouter pour
// transcrire les photos, mettre en forme le cours et vectoriser les fragments.
//
// Il référence aussi User, pour le suivi enseignant seul : aucun jeton ne peut porter
// les cent cinquante élèves d'un professeur, donc `StudentDirectory` va demander son
// effectif à `GET /auth/classes/mes-eleves`. Référence à sens unique — User n'appelle
// pas Cours — donc pas de cycle.
//
// Pas de `WaitFor` : la dépendance est par requête et se referme d'elle-même. Si User
// n'est pas là, un enseignant est refusé et tout le reste de Cours fonctionne ; le faire
// attendre au démarrage coûterait de la disponibilité sans rien protéger.
var coursService = builder.AddProject<Projects.Cours>("cours")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("OpenRouter__ApiKey", openRouterApiKey)
    .WithReference(coursDb)
    .WaitFor(coursDb)
    .WithReference(userService);

// Chat orchestre les agents répétiteurs. Il lit les cours via le service Cours
// (référence à sens unique : c'est la bibliothèque partagée GPOE26.Ai qui évite
// d'avoir aussi besoin de Chat depuis Cours, et donc un cycle).
var chatService = builder.AddProject<Projects.Chat>("chat")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("OpenRouter__ApiKey", openRouterApiKey)
    .WithReference(chatDb)
    .WaitFor(chatDb)
    .WithReference(coursService)
    .WaitFor(coursService);

// Le harness : la boucle à outils qui remplace peu à peu le pipeline figé de Chat.
// Comme Chat, il lit les cours via le service Cours en relayant le JWT de l'élève —
// référence à sens unique, donc pas de cycle.
var harnessService = builder.AddProject<Projects.GPOE26_Harness>("harness")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("OpenRouter__ApiKey", openRouterApiKey)
    .WithReference(harnessDb)
    .WaitFor(harnessDb)
    .WithReference(coursService)
    .WaitFor(coursService);

var quizService = builder.AddProject<Projects.Quiz>("quiz");

builder.AddProject<Projects.GPOE26_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(userService)
    .WaitFor(userService)
    .WithReference(coursService)
    .WaitFor(coursService)
    .WithReference(chatService)
    .WaitFor(chatService)
    .WithReference(harnessService)
    .WaitFor(harnessService)
    .WithReference(quizService)
    .WaitFor(quizService);

builder.Build().Run();
