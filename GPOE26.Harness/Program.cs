using System.ClientModel;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GPOE26.Ai;
using GPOE26.Harness;
using GPOE26.Harness.Contract;
using GPOE26.Harness.Data;
using GPOE26.Harness.Model;
using GPOE26.Harness.Service;
using GPOE26.Harness.Tools;
using GPOE26.Markdown;
using GPOE26.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using OpenAI;

// ══════════════════════════════════════════════════════════════════════════════
//  Harness A — .NET
// ══════════════════════════════════════════════════════════════════════════════
//
// La boucle à outils ne s'écrit pas ici : FunctionInvokingChatClient l'apporte. Ce
// service assemble les pièces — un IChatClient sur OpenRouter, les outils que la table
// autorise, des sessions qui survivent à la fermeture d'un onglet — et les expose sous
// le contrat que la suite de conformité arbitre.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// ─── Sessions ─────────────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<HarnessContext>("harnessdb");

// ─── Authentification ─────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "GPOE2026",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "GPOE2026Users",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    });

builder.Services.AddAuthorization();

// ─── Accès au service Cours ───────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(CoursClient.HttpClientName, client =>
    client.BaseAddress = new Uri("https+http://cours"));

builder.Services.AddScoped<CoursClient>();

// ─── Passerelle IA ────────────────────────────────────────────────────────────
//
// OpenRouter parle l'API d'OpenAI : on pointe le SDK OpenAI sur son URL, et
// AsIChatClient donne l'abstraction de Microsoft.Extensions.AI par-dessus.
builder.Services.AddGpoeAi(builder.Configuration);
builder.Services.AddMemoryCache(options => options.SizeLimit = 64L * 1024 * 1024);

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ApiKey))
        throw new InvalidOperationException(
            "OpenRouter:ApiKey n'est pas configurée. Renseignez OpenRouter__ApiKey — " +
            "la clé arrive par l'AppHost, jamais par un appsettings.json versionné.");

    var openAi = new OpenAIClient(
        new ApiKeyCredential(options.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

    return new ChatClientBuilder(openAi.GetChatClient(options.ModelFor(AgentKind.Tuteur)).AsIChatClient())
        // Toute la boucle est là : exécution des appels d'outils et relance du tour
        // jusqu'à la réponse finale.
        .UseFunctionInvocation()
        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
        .Build();
});

builder.Services.AddScoped<SpeechCache>();
builder.Services.AddScoped<StudyTools>();
builder.Services.AddScoped<TurnRunner>();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<HarnessContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// ── POST /sessions ────────────────────────────────────────────────────────────
app.MapPost("/sessions", async (
    OpenSessionRequest req, ClaimsPrincipal principal, HarnessContext db) =>
{
    var caller = principal.GetUserId();
    if (caller is null) return Results.Unauthorized();

    // Un parent consulte, il n'étudie pas : lui laisser ouvrir une session de lecture au
    // nom de son enfant reviendrait à lui donner le répétiteur de l'enfant.
    if (!ToolPolicy.CanOpen(principal.GetRole(), req.Kind)) return Results.Forbid();

    var studentId = req.StudentId ?? caller.Value;
    if (!principal.CanViewStudent(studentId)) return Results.Forbid();

    var session = new HarnessSession
    {
        StudentId = studentId,
        CourseId = req.CourseId,
        Kind = req.Kind,
    };

    db.Sessions.Add(session);
    await db.SaveChangesAsync();

    return Results.Ok(ToDto(session));
})
.RequireAuthorization()
.WithSummary("Ouvrir une session d'étude");

// ── GET /sessions/{id} ────────────────────────────────────────────────────────
app.MapGet("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal principal, HarnessContext db) =>
{
    var session = await LoadAsync(db, id);
    if (session is null) return Results.NotFound();
    if (!principal.CanViewStudent(session.StudentId)) return Results.Forbid();

    var turns = session.Turns
        .OrderBy(t => t.Index)
        .Select(t => new TurnDto(
            t.Index, t.Role, t.Content, t.At,
            t.Citations?.Split(" | ", StringSplitOptions.RemoveEmptyEntries)))
        .ToList();

    return Results.Ok(new SessionHistoryDto(ToDto(session), turns));
})
.RequireAuthorization()
.WithSummary("État et historique d'une session");

// ── GET /outils ───────────────────────────────────────────────────────────────
app.MapGet("/outils", (SessionKind kind, StudyTools tools) =>
{
    // On décrit ce qui sera réellement exposé au modèle, pas une liste tenue à part :
    // une table et une implémentation qui divergent, c'est une autorisation qui ment.
    var descriptors = tools.FunctionsFor(kind)
        .OfType<AIFunction>()
        .Select(f => new ToolDescriptor(
            f.Name,
            f.Description,
            f.JsonSchema.ToString(),
            f.Name is ToolNames.EcrireFichier or ToolNames.ExecuterTests))
        .ToList();

    return Results.Ok(descriptors);
})
.RequireAuthorization()
.WithSummary("Outils exposés à une nature de session");

// ── POST /sessions/{id}/tour ──────────────────────────────────────────────────
app.MapPost("/sessions/{id:guid}/tour", async (
    Guid id, TurnRequest req, ClaimsPrincipal principal,
    HarnessContext db, TurnRunner runner, HttpContext http) =>
{
    var session = await LoadAsync(db, id);
    if (session is null) { http.Response.StatusCode = StatusCodes.Status404NotFound; return; }

    if (!principal.CanViewStudent(session.StudentId))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    // Sans cet en-tête, un proxy inverse met le flux en tampon et anéantit l'intérêt du
    // streaming — l'élève retrouve son écran figé.
    http.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = http.RequestAborted;

    async Task Send(HarnessEvent evt)
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(evt, json)}\n\n", Encoding.UTF8, ct);
        await http.Response.Body.FlushAsync(ct);
    }

    if (string.IsNullOrWhiteSpace(req.Message))
    {
        await Send(HarnessEvent.OfError("La question est vide."));
    }
    else
    {
        var terminated = false;
        try
        {
            await foreach (var evt in runner.RunAsync(session, req, ct))
            {
                await Send(evt);
                if (evt.IsTerminal) terminated = true;
            }
        }
        catch (OperationCanceledException)
        {
            return; // l'élève a quitté la page
        }
        catch (OpenRouterException ex)
        {
            await Send(HarnessEvent.OfError(ex.Message));
            terminated = true;
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Tour en échec sur la session {SessionId}", id);
            await Send(HarnessEvent.OfError("Une erreur interne est survenue."));
            terminated = true;
        }

        // Le contrat exige qu'un tour se termine. Un flux qui se referme sans évènement
        // terminal laisse un client à attendre indéfiniment — c'est le défaut que le
        // sabotage NoTerminalEvent reproduit, et il n'a pas à survenir pour de vrai.
        if (!terminated)
            await Send(HarnessEvent.OfError("Le tour s'est achevé sans réponse."));
    }

    await http.Response.WriteAsync("data: [DONE]\n\n", ct);
    await http.Response.Body.FlushAsync(ct);
})
.RequireAuthorization()
.WithSummary("Jouer un tour, en flux");

// ── POST /sessions/{id}/compacter ─────────────────────────────────────────────
app.MapPost("/sessions/{id:guid}/compacter", async (
    Guid id, ClaimsPrincipal principal, HarnessContext db, TurnRunner runner) =>
{
    var session = await LoadAsync(db, id);
    if (session is null) return Results.NotFound();
    if (!principal.CanViewStudent(session.StudentId)) return Results.Forbid();

    await runner.CompactAsync(session);
    return Results.Ok(ToDto(session));
})
.RequireAuthorization()
.WithSummary("Compacter le contexte d'une session");

// ── GET /cours/{id}/rendu ─────────────────────────────────────────────────────
//
// Le cours rendu une seule fois, pour deux surfaces. Le Web l'affiche dans un composant
// Blazor, l'application bureau dans un conteneur ordinaire : c'est le même HTML, les
// mêmes ancres, la même feuille de style. Rendre deux fois, c'est se garantir que les
// deux finiront par différer — et la divergence porterait sur les ancres du sommaire,
// donc sur des liens qui cessent de fonctionner d'un seul côté.
app.MapGet("/cours/{id:guid}/rendu", async (Guid id, CoursClient cours, CancellationToken ct) =>
{
    var course = await cours.GetCourseAsync(id, ct);
    if (course is null) return Results.NotFound(new { message = "Cours introuvable." });

    var markdown = course.Content;
    if (string.IsNullOrWhiteSpace(markdown))
        return Results.Ok(new RenderedCourseDto(
            id, course.Title, course.Subject, string.Empty, [], DateTime.UtcNow));

    var outline = CourseRenderer.BuildOutline(markdown)
        .Select(e => new OutlineEntryDto(e.Level, e.Text, e.Id))
        .ToList();

    return Results.Ok(new RenderedCourseDto(
        id, course.Title, course.Subject, CourseRenderer.ToHtml(markdown), outline, DateTime.UtcNow));
})
.RequireAuthorization()
.WithSummary("Cours rendu en HTML, avec son sommaire");

// ── GET /voix/{key} ───────────────────────────────────────────────────────────
//
// Un outil ne peut pas renvoyer d'audio : il renvoie une référence, et l'interface vient
// chercher les octets ici.
app.MapGet("/voix/{key}", (string key, SpeechCache cache) =>
{
    var audio = cache.Get(key);
    return audio is null ? Results.NotFound() : Results.File(audio, "audio/mpeg");
})
.RequireAuthorization()
.WithSummary("Récupérer un audio préparé par l'outil de lecture");

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static Task<HarnessSession?> LoadAsync(HarnessContext db, Guid id) =>
    db.Sessions.Include(s => s.Turns).FirstOrDefaultAsync(s => s.Id == id);

static SessionDto ToDto(HarnessSession s) => new(
    s.Id, s.StudentId, s.CourseId, s.Kind, s.StartedAt, s.LastActivityAt,
    s.Turns.Count(t => t.Role == "user"),
    s.CompactedAt,
    ToolPolicy.For(s.Kind));
