var builder = DistributedApplication.CreateBuilder(args);

// ── JWT partagé : même clé / issuer / audience pour User (émetteur) et Cours (validateur)
var jwtKeyValue = "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";
var jwtIssuer   = "GPOE2026";
var jwtAudience = "GPOE2026Users";




// ── 1 seul serveur PostgreSQL, toutes les DBs dessus ─────────────────────────
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(); // interface admin dispo sur http://localhost:5050

var userDb = postgres.AddDatabase("userdb");
var coursDb = postgres.AddDatabase("coursdb");
var schoolDb = postgres.AddDatabase("LyceeDB");  // ApiService
// Quiz et Chat sont en InMemory pour l'instant, pas de DB à déclarer






var apiService = builder.AddProject<Projects.GPOE26_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(schoolDb)
    .WaitFor(schoolDb);

var userService = builder.AddProject<Projects.User>("user")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithReference(userDb)   // ← ajoute
    .WaitFor(userDb);        // ← ajoute

var coursService = builder.AddProject<Projects.Cours>("cours")
    .WithEnvironment("Jwt__Key", jwtKeyValue)
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithReference(coursDb)  // ← ajoute
    .WaitFor(coursDb);       // ← ajoute

var chatService = builder.AddProject<Projects.Chat>("chat");

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
    .WithReference(quizService)
    .WaitFor(quizService);

builder.Build().Run();