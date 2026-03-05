var builder = DistributedApplication.CreateBuilder(args);

var jwtKey = builder.AddParameter("jwt-key", secret: true);




// ── 1 seul serveur PostgreSQL, toutes les DBs dessus ─────────────────────────
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(); // interface admin dispo sur http://localhost:5050

var userDb = postgres.AddDatabase("userdb");
var coursDb = postgres.AddDatabase("coursdb");
var schoolDb = postgres.AddDatabase("schooldb");  // ApiService
// Quiz et Chat sont en InMemory pour l'instant, pas de DB à déclarer






var apiService = builder.AddProject<Projects.GPOE26_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(schoolDb)
    .WaitFor(schoolDb);

var userService = builder.AddProject<Projects.User>("user")
    .WithEnvironment("Jwt__Key", jwtKey)
    .WithEnvironment("Jwt__Issuer", "jpoe2026-user")
    .WithEnvironment("Jwt__Audience", "jpoe2026-clients");

var coursService = builder.AddProject<Projects.Cours>("cours");

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