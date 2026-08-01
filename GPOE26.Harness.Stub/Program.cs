using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GPOE26.Harness.Contract;
using GPOE26.Harness.Stub;
using GPOE26.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// ══════════════════════════════════════════════════════════════════════════════
//  Harness — implémentation de référence
// ══════════════════════════════════════════════════════════════════════════════
//
// Ce service ne parle à aucun modèle. Il implémente le contrat avec des réponses en
// dur, dans un seul but : exercer la suite de conformité le jour où elle est écrite.
//
// Sans lui, l'étape 1 livrerait une suite de tests que rien n'a jamais fait tourner —
// c'est-à-dire exactement le travers dont l'étape 0 vient de sortir ce projet, où trois
// phases de code avaient été écrites sans une seule compilation.
//
// Il sert ensuite de deux façons : de référence exécutable pour qui implémente un vrai
// harness, et de harnais de non-régression pour la suite elle-même — via ses sabotages,
// qui vérifient que le juge sait tomber.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<SessionStore>();

// Le stub valide les mêmes jetons que les autres services : c'est ce qui permet à la
// suite de conformité de tester les refus d'autorisation pour de vrai, avec des jetons
// d'élève, de parent et d'enseignant.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "GPOE2026",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "GPOE2026Users",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// Trace au démarrage : un sabotage actif doit être visible, sinon on passe une heure à
// chercher pourquoi la suite tombe.
var sabotage = SabotageSettings.Current;
if (sabotage != Sabotage.None)
    app.Logger.LogWarning("⚠️  Stub saboté volontairement : {Sabotage}", sabotage);

// ── POST /sessions ────────────────────────────────────────────────────────────
app.MapPost("/sessions", (OpenSessionRequest req, ClaimsPrincipal principal, SessionStore store) =>
{
    var caller = principal.GetUserId();
    if (caller is null) return Results.Unauthorized();

    // Qui a le droit d'ouvrir quoi : un parent consulte, il n'étudie pas.
    if (!ToolPolicy.CanOpen(principal.GetRole(), req.Kind))
        return Results.Forbid();

    var studentId = req.StudentId ?? caller.Value;

    // SkipAuthorization : on accepte n'importe quel élève. Le test d'étanchéité doit
    // s'en apercevoir.
    if (!SabotageSettings.Is(Sabotage.SkipAuthorization) && !principal.CanViewStudent(studentId))
        return Results.Forbid();

    return Results.Ok(store.Open(studentId, req.CourseId, req.Kind).ToDto());
})
.RequireAuthorization()
.WithSummary("Ouvrir une session d'étude");

// ── GET /sessions/{id} ────────────────────────────────────────────────────────
app.MapGet("/sessions/{id:guid}", (Guid id, ClaimsPrincipal principal, SessionStore store) =>
{
    var session = store.Find(id);
    if (session is null) return Results.NotFound();

    return principal.CanViewStudent(session.StudentId)
        ? Results.Ok(new SessionHistoryDto(session.ToDto(), session.Turns))
        : Results.Forbid();
})
.RequireAuthorization()
.WithSummary("État et historique d'une session");

// ── GET /outils ───────────────────────────────────────────────────────────────
app.MapGet("/outils", (SessionKind kind) => Results.Ok(StubTools.DescriptorsFor(kind)))
    .RequireAuthorization()
    .WithSummary("Outils exposés à une nature de session");

// ── POST /sessions/{id}/tour ──────────────────────────────────────────────────
//
// Le flux d'un tour. Le stub joue toujours la même partition — étape, appel d'outil,
// résultat, rédaction, fin — ce qui rend la forme du flux vérifiable indépendamment de
// ce qu'un modèle aurait décidé.
app.MapPost("/sessions/{id:guid}/tour", async (
    Guid id, TurnRequest req, ClaimsPrincipal principal, SessionStore store, HttpContext http) =>
{
    var session = store.Find(id);
    if (session is null) { http.Response.StatusCode = StatusCodes.Status404NotFound; return; }

    if (!principal.CanViewStudent(session.StudentId))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = http.RequestAborted;

    async Task Send(HarnessEvent evt)
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(evt, json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    session.Record("user", req.Message);

    await Send(HarnessEvent.OfStep("Analyse de votre question…"));

    // Un appel d'outil, choisi dans ce que la nature de session autorise. Une session de
    // suivi n'en a aucun : le flux passe alors directement à la rédaction.
    var tools = ToolPolicy.For(session.Kind);
    if (tools.Count > 0)
    {
        var tool = tools[0];
        await Send(HarnessEvent.OfToolCall(tool, """{"requete":"stub"}""", 1));

        // SwallowToolFailure : l'outil échoue et on continue comme si de rien n'était,
        // au lieu d'émettre `error`.
        if (req.Message.Contains("__fail_tool__", StringComparison.Ordinal))
        {
            if (!SabotageSettings.Is(Sabotage.SwallowToolFailure))
            {
                await Send(HarnessEvent.OfError($"L'outil {tool} a échoué."));
                return;
            }

            await Send(HarnessEvent.OfToolResult(tool, "(échec avalé)", 1));
        }
        else
        {
            await Send(HarnessEvent.OfToolResult(tool, "3 passages trouvés", 1));
        }
    }

    const string reply = "Réponse du stub. Ce service ne consulte aucun modèle.";
    foreach (var word in reply.Split(' '))
        await Send(HarnessEvent.OfToken(word + " "));

    session.Record("assistant", reply, ["Chapitre 1 › Introduction"]);

    // NoTerminalEvent : on ferme le flux sans `done`, ce qui laisserait un client à
    // attendre indéfiniment.
    if (!SabotageSettings.Is(Sabotage.NoTerminalEvent))
        await Send(HarnessEvent.OfDone(reply, ["Chapitre 1 › Introduction"], "Explication"));

    await http.Response.WriteAsync("data: [DONE]\n\n", ct);
    await http.Response.Body.FlushAsync(ct);
})
.RequireAuthorization()
.WithSummary("Jouer un tour, en flux");

// ── POST /sessions/{id}/compacter ─────────────────────────────────────────────
app.MapPost("/sessions/{id:guid}/compacter", (Guid id, ClaimsPrincipal principal, SessionStore store) =>
{
    var session = store.Find(id);
    if (session is null) return Results.NotFound();
    if (!principal.CanViewStudent(session.StudentId)) return Results.Forbid();

    session.Compact();
    return Results.Ok(session.ToDto());
})
.RequireAuthorization()
.WithSummary("Compacter le contexte d'une session");

// ── GET /cours/{id}/rendu ─────────────────────────────────────────────────────
app.MapGet("/cours/{id:guid}/rendu", (Guid id) => Results.Ok(new RenderedCourseDto(
        id,
        "Cours de démonstration",
        "Mathématiques",
        "<h2 id=\"introduction\">Introduction</h2><p>Contenu rendu par le stub.</p>",
        [new OutlineEntryDto(2, "Introduction", "introduction")],
        DateTime.UtcNow)))
    .RequireAuthorization()
    .WithSummary("Cours rendu en HTML");

// ── POST /sync ────────────────────────────────────────────────────────────────
app.MapPost("/sync", (SyncBatch batch) =>
        Results.Ok(new SyncResult(batch.Activities.Count, batch.Results.Count, [])))
    .RequireAuthorization()
    .WithSummary("Remonter un lot d'activité hors ligne");

// ── GET /suivi/{studentId}/resume ─────────────────────────────────────────────
//
// La surface parent et enseignant. Ce qu'elle ne contient PAS est sa raison d'être :
// aucun contenu d'échange avec le répétiteur. Le test de fuite de la suite lit cette
// réponse et vérifie qu'aucun message n'y figure.
app.MapGet("/suivi/{studentId:guid}/resume", (Guid studentId, ClaimsPrincipal principal) =>
{
    if (!SabotageSettings.Is(Sabotage.SkipAuthorization) && !principal.CanViewStudent(studentId))
        return Results.Forbid();

    var resume = new Dictionary<string, object?>
    {
        ["studentId"] = studentId,
        ["sessionsThisWeek"] = 3,
        ["activeSecondsThisWeek"] = 4_200,
        ["exercisesThisWeek"] = 5,
        ["questionsThisWeek"] = 12,
        ["coursesStarted"] = 2,
        ["coursesFinished"] = 1,
    };

    // LeakMessage : on glisse un échange dans la réponse. C'est précisément ce que la
    // phase 2b a promis de ne jamais faire, et ce que le test de fuite doit attraper.
    if (SabotageSettings.Is(Sabotage.LeakMessage))
        resume["dernierEchange"] = "Élève : je n'ai rien compris au chapitre 3.";

    return Results.Ok(resume);
})
.RequireAuthorization()
.WithSummary("Suivi d'un élève, pour son parent ou son enseignant");

app.Run();

/// <summary>Rend le point d'entrée visible aux tests d'intégration.</summary>
public partial class Program;
