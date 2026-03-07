using GPOE26.ApiService.Data;
using GPOE26.ApiService.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);



builder.AddNpgsqlDbContext<ApiServiceContext>("LyceeDB");


/*
builder.Services.AddDbContext<ApiServiceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("LyceeDB")));
*/


// âœ… AJOUT: Configuration CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});












// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();








// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Portfolio Projet API", Description = "API de gestion des projets", Version = "v1" });
});












var app = builder.Build();

// Création automatique des tables au démarrage (pas de migrations EF pour ce projet)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiServiceContext>();
    await db.Database.EnsureCreatedAsync();

    // ⚠️ AJOUT : injection des données test (Contact, Events, News...)
    await ApiSeeder.SeedAsync(db);
}

app.UseCors("AllowAll");
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GPOE26 School API V1"));
}

app.UseHttpsRedirection();

// ── Routes ────────────────────────────────────────────────────────────────────

var news = app.MapGroup("/api/news");
var events = app.MapGroup("/api/events");
var speeches = app.MapGroup("/api/speeches");
var activities = app.MapGroup("/api/activities");
var hierarchy = app.MapGroup("/api/hierarchy");
var contacts = app.MapGroup("/api/contact");

// NEWS
news.MapGet("", NewsGetAll);
news.MapGet("/{id:guid}", NewsGetById);
news.MapGet("/slug/{slug}", NewsGetBySlug);
news.MapPost("", NewsCreate);
news.MapPut("/{id:guid}", NewsUpdate);
news.MapDelete("/{id:guid}", NewsDelete);
news.MapPatch("/{id:guid}/publish", NewsPublish);

// EVENTS
events.MapGet("", EventsGetAll);
events.MapGet("/upcoming", EventsGetUpcoming);
events.MapGet("/{id:guid}", EventsGetById);
events.MapPost("", EventsCreate);
events.MapPut("/{id:guid}", EventsUpdate);
events.MapDelete("/{id:guid}", EventsDelete);

// SPEECHES
speeches.MapGet("", SpeechesGetAll);
speeches.MapGet("/latest", SpeechesGetLatest);
speeches.MapGet("/{id:guid}", SpeechesGetById);
speeches.MapPost("", SpeechesCreate);
speeches.MapPut("/{id:guid}", SpeechesUpdate);
speeches.MapDelete("/{id:guid}", SpeechesDelete);

// ACTIVITIES
activities.MapGet("", ActivitiesGetAll);
activities.MapGet("/{id:guid}", ActivitiesGetById);
activities.MapPost("", ActivitiesCreate);
activities.MapPut("/{id:guid}", ActivitiesUpdate);
activities.MapDelete("/{id:guid}", ActivitiesDelete);

// HIERARCHY
hierarchy.MapGet("", HierarchyGetAll);
hierarchy.MapGet("/{id:int}", HierarchyGetById);
hierarchy.MapGet("/role/{role}", HierarchyGetByRole);
hierarchy.MapPost("", HierarchyCreate);
hierarchy.MapPut("/{id:int}", HierarchyUpdate);
hierarchy.MapDelete("/{id:int}", HierarchyDelete);

// CONTACTS
contacts.MapGet("", async (ApiServiceContext db) =>
    TypedResults.Ok(await db.Contacts.FirstOrDefaultAsync()));
contacts.MapPost("", async (Contact contact, ApiServiceContext db) =>
{
    var existing = await db.Contacts.FirstOrDefaultAsync();
    if (existing is not null)
    {
        existing.Name = contact.Name;
        existing.Address = contact.Address;
        existing.Email = contact.Email;
        existing.Phone = contact.Phone;
        existing.City = contact.City;
    }
    else
    {
        db.Contacts.Add(contact);
    }
    await db.SaveChangesAsync();
    return TypedResults.Ok(contact);
});
contacts.MapPut("/{id:int}", UpdateContact);

contacts.MapDelete("/{id:int}", DeleteContact);

app.Run();


static async Task<IResult> UpdateContact(int id, Contact updated, ApiServiceContext db)
{
    var existing = await db.Contacts.FindAsync(id);
    if (existing is null) return TypedResults.NotFound();
    existing.Name = updated.Name;
    existing.Address = updated.Address;
    existing.Email = updated.Email;
    existing.Phone = updated.Phone;
    existing.City = updated.City;
    await db.SaveChangesAsync();
    return TypedResults.Ok(existing);
}

static async Task<IResult> DeleteContact(int id, ApiServiceContext db)
{
    var existing = await db.Contacts.FindAsync(id);
    if (existing is null) return TypedResults.NotFound();
    db.Contacts.Remove(existing);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}
// ══════════════════════════════════════════════════════════════════════════════
// NEWS HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

static async Task<IResult> NewsGetAll(
    ApiServiceContext db,
    string? category = null,
    bool? published = null,
    int page = 1,
    int pageSize = 10)
{
    var query = db.NewsArticles.AsQueryable();

    if (!string.IsNullOrEmpty(category))
        query = query.Where(n => n.Category == category);

    if (published.HasValue)
        query = query.Where(n => n.IsPublished == published.Value);

    var total = await query.CountAsync();
    var items = await query
        .OrderByDescending(n => n.PublishedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return TypedResults.Ok(new { total, page, pageSize, items });
}

static async Task<IResult> NewsGetById(Guid id, ApiServiceContext db)
{
    var article = await db.NewsArticles.FindAsync(id);
    return article is null ? TypedResults.NotFound() : TypedResults.Ok(article);
}

static async Task<IResult> NewsGetBySlug(string slug, ApiServiceContext db)
{
    var article = await db.NewsArticles.FirstOrDefaultAsync(n => n.Slug == slug);
    return article is null ? TypedResults.NotFound() : TypedResults.Ok(article);
}

static async Task<IResult> NewsCreate(NewArticle article, ApiServiceContext db)
{
    article.Id = Guid.NewGuid();
    article.CreatedAt = DateTime.UtcNow;
    article.UpdatedAt = DateTime.UtcNow;

    if (string.IsNullOrEmpty(article.Slug))
        article.Slug = GenerateSlug(article.Title);

    db.NewsArticles.Add(article);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/news/{article.Id}", article);
}

static async Task<IResult> NewsUpdate(Guid id, NewArticle updated, ApiServiceContext db)
{
    var article = await db.NewsArticles.FindAsync(id);
    if (article is null) return TypedResults.NotFound();

    article.Title = updated.Title;
    article.Content = updated.Content;
    article.Excerpt = updated.Excerpt;
    article.Category = updated.Category;
    article.IsPublished = updated.IsPublished;
    article.PublishedAt = updated.PublishedAt;
    article.ImageUrl = updated.ImageUrl;
    article.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return TypedResults.Ok(article);
}

static async Task<IResult> NewsDelete(Guid id, ApiServiceContext db)
{
    var article = await db.NewsArticles.FindAsync(id);
    if (article is null) return TypedResults.NotFound();

    db.NewsArticles.Remove(article);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}

static async Task<IResult> NewsPublish(Guid id, ApiServiceContext db)
{
    var article = await db.NewsArticles.FindAsync(id);
    if (article is null) return TypedResults.NotFound();

    article.IsPublished = true;
    article.PublishedAt = DateTime.UtcNow;
    article.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return TypedResults.Ok(article);
}

// ══════════════════════════════════════════════════════════════════════════════
// EVENTS HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

static async Task<IResult> EventsGetAll(ApiServiceContext db, string? type = null)
{
    var query = db.Events.AsQueryable();
    if (!string.IsNullOrEmpty(type))
        query = query.Where(e => e.Type == type);

    return TypedResults.Ok(await query.OrderBy(e => e.StartDate).ToListAsync());
}

static async Task<IResult> EventsGetUpcoming(ApiServiceContext db, int limit = 5)
{
    var now = DateTime.UtcNow;
    var events = await db.Events
        .Where(e => e.StartDate >= now)
        .OrderBy(e => e.StartDate)
        .Take(limit)
        .ToListAsync();

    return TypedResults.Ok(events);
}

static async Task<IResult> EventsGetById(Guid id, ApiServiceContext db)
{
    var ev = await db.Events.FindAsync(id);
    return ev is null ? TypedResults.NotFound() : TypedResults.Ok(ev);
}

static async Task<IResult> EventsCreate(SchoolEvent ev, ApiServiceContext db)
{
    ev.Id = Guid.NewGuid();
    ev.CreatedAt = DateTime.UtcNow;
    db.Events.Add(ev);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/events/{ev.Id}", ev);
}

static async Task<IResult> EventsUpdate(Guid id, SchoolEvent updated, ApiServiceContext db)
{
    var ev = await db.Events.FindAsync(id);
    if (ev is null) return TypedResults.NotFound();

    ev.Title = updated.Title;
    ev.Description = updated.Description;
    ev.Location = updated.Location;
    ev.StartDate = updated.StartDate;
    ev.EndDate = updated.EndDate;
    ev.Type = updated.Type;
    ev.IsPublic = updated.IsPublic;
    ev.ImageUrl = updated.ImageUrl;

    await db.SaveChangesAsync();
    return TypedResults.Ok(ev);
}

static async Task<IResult> EventsDelete(Guid id, ApiServiceContext db)
{
    var ev = await db.Events.FindAsync(id);
    if (ev is null) return TypedResults.NotFound();

    db.Events.Remove(ev);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}

// ══════════════════════════════════════════════════════════════════════════════
// SPEECHES HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

static async Task<IResult> SpeechesGetAll(ApiServiceContext db) =>
    TypedResults.Ok(await db.Speeches.OrderByDescending(s => s.DeliveredAt).ToListAsync());

static async Task<IResult> SpeechesGetLatest(ApiServiceContext db)
{
    var speech = await db.Speeches.OrderByDescending(s => s.DeliveredAt).FirstOrDefaultAsync();
    return speech is null ? TypedResults.NotFound() : TypedResults.Ok(speech);
}

static async Task<IResult> SpeechesGetById(Guid id, ApiServiceContext db)
{
    var speech = await db.Speeches.FindAsync(id);
    return speech is null ? TypedResults.NotFound() : TypedResults.Ok(speech);
}

static async Task<IResult> SpeechesCreate(Speech speech, ApiServiceContext db)
{
    speech.Id = Guid.NewGuid();
    speech.CreatedAt = DateTime.UtcNow;
    db.Speeches.Add(speech);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/speeches/{speech.Id}", speech);
}

static async Task<IResult> SpeechesUpdate(Guid id, Speech updated, ApiServiceContext db)
{
    var speech = await db.Speeches.FindAsync(id);
    if (speech is null) return TypedResults.NotFound();

    speech.Title = updated.Title;
    speech.Content = updated.Content;
    speech.Excerpt = updated.Excerpt;
    speech.Occasion = updated.Occasion;
    speech.DeliveredAt = updated.DeliveredAt;
    speech.AuthorName = updated.AuthorName;
    speech.AuthorRole = updated.AuthorRole;
    speech.AvatarUrl = updated.AvatarUrl;

    await db.SaveChangesAsync();
    return TypedResults.Ok(speech);
}

static async Task<IResult> SpeechesDelete(Guid id, ApiServiceContext db)
{
    var speech = await db.Speeches.FindAsync(id);
    if (speech is null) return TypedResults.NotFound();

    db.Speeches.Remove(speech);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}

// ══════════════════════════════════════════════════════════════════════════════
// ACTIVITIES HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

static async Task<IResult> ActivitiesGetAll(ApiServiceContext db, string? category = null)
{
    var query = db.Activities.AsQueryable();
    if (!string.IsNullOrEmpty(category))
        query = query.Where(a => a.Category == category);

    return TypedResults.Ok(await query.Where(a => a.IsActive).ToListAsync());
}

static async Task<IResult> ActivitiesGetById(Guid id, ApiServiceContext db)
{
    var activity = await db.Activities.FindAsync(id);
    return activity is null ? TypedResults.NotFound() : TypedResults.Ok(activity);
}

static async Task<IResult> ActivitiesCreate(SchoolActivity activity, ApiServiceContext db)
{
    activity.Id = Guid.NewGuid();
    activity.CreatedAt = DateTime.UtcNow;
    db.Activities.Add(activity);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/activities/{activity.Id}", activity);
}

static async Task<IResult> ActivitiesUpdate(Guid id, SchoolActivity updated, ApiServiceContext db)
{
    var activity = await db.Activities.FindAsync(id);
    if (activity is null) return TypedResults.NotFound();

    activity.Name = updated.Name;
    activity.Description = updated.Description;
    activity.Category = updated.Category;
    activity.Schedule = updated.Schedule;
    activity.ResponsibleTeacher = updated.ResponsibleTeacher;
    activity.ImageUrl = updated.ImageUrl;
    activity.IsActive = updated.IsActive;

    await db.SaveChangesAsync();
    return TypedResults.Ok(activity);
}

static async Task<IResult> ActivitiesDelete(Guid id, ApiServiceContext db)
{
    var activity = await db.Activities.FindAsync(id);
    if (activity is null) return TypedResults.NotFound();

    db.Activities.Remove(activity);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}

// ══════════════════════════════════════════════════════════════════════════════
// HIERARCHY HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

static async Task<IResult> HierarchyGetAll(ApiServiceContext db) =>
    TypedResults.Ok(await db.Hierarchies.OrderBy(h => h.Role).ToListAsync());

static async Task<IResult> HierarchyGetById(int id, ApiServiceContext db)
{
    var member = await db.Hierarchies.FindAsync(id);
    return member is null ? TypedResults.NotFound() : TypedResults.Ok(member);
}

static async Task<IResult> HierarchyGetByRole(string role, ApiServiceContext db)
{
    var members = await db.Hierarchies
        .Where(h => h.Role.ToLower() == role.ToLower())
        .ToListAsync();
    return TypedResults.Ok(members);
}

static async Task<IResult> HierarchyCreate(Hierarchy member, ApiServiceContext db)
{
    db.Hierarchies.Add(member);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/hierarchy/{member.Id}", member);
}

static async Task<IResult> HierarchyUpdate(int id, Hierarchy updated, ApiServiceContext db)
{
    var member = await db.Hierarchies.FindAsync(id);
    if (member is null) return TypedResults.NotFound();

    member.Role = updated.Role;
    member.Description = updated.Description;
    member.Department = updated.Department;
    member.Specialization = updated.Specialization;
    member.Name = updated.Name;
    member.PreName = updated.PreName;
    member.Email = updated.Email;
    member.Citation = updated.Citation;
    member.ImageUrl = updated.ImageUrl;

    await db.SaveChangesAsync();
    return TypedResults.Ok(member);
}

static async Task<IResult> HierarchyDelete(int id, ApiServiceContext db)
{
    var member = await db.Hierarchies.FindAsync(id);
    if (member is null) return TypedResults.NotFound();

    db.Hierarchies.Remove(member);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}

// ══════════════════════════════════════════════════════════════════════════════
// UTILS
// ══════════════════════════════════════════════════════════════════════════════

static string GenerateSlug(string title) =>
    title.ToLower()
         .Replace(" ", "-")
         .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
         .Replace("à", "a").Replace("â", "a")
         .Replace("ù", "u").Replace("û", "u")
         .Replace("ô", "o").Replace("î", "i")
         .Replace("ç", "c");


