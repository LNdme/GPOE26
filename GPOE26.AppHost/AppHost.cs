var builder = DistributedApplication.CreateBuilder(args);

// ── JWT partagé : même clé / issuer / audience pour User (émetteur) et les services qui le valident
var jwtKeyValue = "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";
var jwtIssuer   = "GPOE2026";
var jwtAudience = "GPOE2026Users";

// La clé LLM est facultative pour le démarrage local ; les fonctions IA l'exigent.
// Pour l'activer : dotnet user-secrets set "Parameters:llm-apikey" "<clé>"
var llmApiKey = builder.AddParameter(
    "llm-apikey",
    builder.Configuration["Parameters:llm-apikey"] ?? string.Empty,
    secret: true);




// ── 1 seul serveur PostgreSQL, toutes les DBs dessus ─────────────────────────
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg18")   // PostgreSQL 18 avec la recherche vectorielle des agents
    // Le tag pg18 n'est pas détecté par WithDataVolume ; remonter l'ancien volume au chemin attendu par PostgreSQL 18.
    .WithVolume("gpoe26.apphost-d7acfcd7e9-postgres-data", "/var/lib/postgresql")
    .WithPgAdmin(); // interface admin dispo sur http://localhost:5050

var userDb = postgres.AddDatabase("userdb");
var coursDb = postgres.AddDatabase("coursdb");
var schoolDb = postgres.AddDatabase("LyceeDB");  // ApiService
var quizDb = postgres.AddDatabase("quizdb");     // Quiz : évaluations des enseignants
var chatDb = postgres.AddDatabase("chatdb");     // Chat : discussions avec le tuteur IA






var apiService = builder.AddProject<Projects.GPOE26_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Jwt__Key", jwtKeyValue)          // écriture du contenu réservée aux administrateurs
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithReference(schoolDb)
    .WaitFor(schoolDb);

var userService = builder.AddProject<Projects.User>("user")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Services__ApiService", apiService.GetEndpoint("http"))   // catalogue des formules
    // Premiers administrateurs du site (comptes déjà inscrits) :
    //   dotnet user-secrets set "Parameters:admin-emails" "a@exemple.cm;b@exemple.cm" --project GPOE26.AppHost
    .WithEnvironment("Admin__Emails", builder.Configuration["Parameters:admin-emails"] ?? "")
    .WithReference(userDb)   // ← ajoute
    .WaitFor(userDb)         // ← ajoute
    .WaitFor(apiService);

var coursService = builder.AddProject<Projects.Cours>("cours")
    .WithEnvironment("LLM__ApiKey", llmApiKey)   // mise en forme des PDF
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Services__User", userService.GetEndpoint("http"))   // partage des cours avec les classes
    .WithReference(coursDb)  // ← ajoute
    .WaitFor(coursDb);       // ← ajoute

var chatService = builder.AddProject<Projects.Chat>("chat")
    .WithEnvironment("LLM__ApiKey", llmApiKey)
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Services__User", userService.GetEndpoint("http"))    // abonnement Premium
    .WithEnvironment("Services__Cours", coursService.GetEndpoint("http"))  // contenu du cours étudié
    .WithReference(chatDb)
    .WaitFor(chatDb);

var quizService = builder.AddProject<Projects.Quiz>("quiz")
    .WithEnvironment("LLM__ApiKey", llmApiKey)
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Services__User", userService.GetEndpoint("http"))   // classes, élèves, parents
    .WithReference(quizDb)
    .WaitFor(quizDb)
    .WaitFor(userService);


chatService.WithEnvironment("Services__Quiz", quizService.GetEndpoint("http"));   // progression de l'élève

// Fournisseur d'IA : OpenRouter par défaut, ou tout service compatible (Ollama local…) via Parameters:llm-baseurl et llm-models
foreach (var service in new[] { coursService, chatService, quizService })
{
    if (builder.Configuration["Parameters:llm-baseurl"] is { Length: > 0 } baseUrl)
        service.WithEnvironment("LLM__BaseUrl", baseUrl);
    if (builder.Configuration["Parameters:llm-models"] is { Length: > 0 } models)
        service.WithEnvironment("LLM__Models", models);
    if (builder.Configuration["Parameters:llm-vision-models"] is { Length: > 0 } visionModels)
        service.WithEnvironment("LLM__VisionModels", visionModels);
}

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
    .WithReference(quizService)
    .WaitFor(quizService);

builder.Build().Run();