var builder = DistributedApplication.CreateBuilder(args);

var jwtKey = builder.AddParameter("jwt-key", secret: true);

var apiService = builder.AddProject<Projects.GPOE26_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

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