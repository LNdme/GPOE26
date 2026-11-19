using System.Globalization;
using System.Net;
using System.Text;
using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Pages des outils de la barre de droite (quiz, exercices, devoirs, fiches, plan, progrès, mémoire),
/// affichées dans la même surface de lecture que la discussion (WebView2, KaTeX, hors connexion).
/// Tout texte venant du serveur ou de l'IA est soit échappé, soit rendu par <see cref="MarkdownRenderer"/>
/// (HTML brut désactivé). Les boutons portent <c>data-action</c> : reader.js renvoie l'action et les champs à l'application.
/// </summary>
public static class ToolPages
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string Md(string? markdown) => MarkdownRenderer.Render(markdown ?? "").Html;
    private static string Date(DateTime? utc, string format = "ddd d MMM 'à' HH:mm") =>
        utc is null ? "" : utc.Value.ToLocalTime().ToString(format, Fr);
    private static string Time(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    /// <summary>Copie locale lue hors connexion : la date est rappelée en tête de page.</summary>
    private static string Stale(DateTime? savedAt) => savedAt is null ? "" :
        $"""<p class="stale">Hors connexion · copie du {E(Date(savedAt))}</p>""";

    private static string Head(string title, string? sub) =>
        $"""<header class="tool-head"><h2>{E(title)}</h2>{(string.IsNullOrWhiteSpace(sub) ? "" : $"<p>{E(sub)}</p>")}</header>""";

    public static string Message(string title, string text, string? action = null, string? actionLabel = null) =>
        $"""{Head(title, null)}<div class="hint">{E(text)}</div>""" +
        (action is null ? "" : $"""<p><button class="btn" data-action="{E(action)}">{E(actionLabel)}</button></p>""");

    public static string Loading(string text) => $"""<div class="loading"><span class="spinner"></span>{E(text)}</div>""";

    // ─── Quiz et exercices : le choix du niveau et du nombre ──────────────────────────

    public static readonly (string Value, string Label)[] Difficulties =
    [
        ("standard", "Standard : applications directes"),
        ("approfondi", "Approfondi : raisonnement en plusieurs étapes"),
        ("exigeant", "Exigeant : cas limites et démonstrations")
    ];

    public static string GenerateForm(bool problems, string courseTitle, string difficulty, bool online, bool hasPrevious)
    {
        var counts = problems ? new[] { 1, 2, 3, 5 } : new[] { 3, 5, 10 };
        var preferred = problems ? 3 : 5;
        var sb = new StringBuilder();
        sb.Append(Head(problems ? "Exercices corrigés" : "Quiz", courseTitle));
        sb.Append($"""<p class="muted">{(problems
            ? "Des exercices tirés du cours, avec des indices à révéler un par un et la solution détaillée."
            : "Des questions à choix multiples sur le cours, corrigées avec une explication pour chaque réponse.")}</p>""");
        sb.Append("""<div class="form" data-form>""");
        sb.Append("""<label>Niveau<select name="difficulty">""");
        foreach (var (value, label) in Difficulties)
            sb.Append($"""<option value="{value}"{(value == difficulty ? " selected" : "")}>{E(label)}</option>""");
        sb.Append("</select></label>");
        sb.Append($"""<label>{(problems ? "Nombre d'exercices" : "Nombre de questions")}<select name="count">""");
        foreach (var n in counts)
            sb.Append($"""<option value="{n}"{(n == preferred ? " selected" : "")}>{n}</option>""");
        sb.Append("</select></label>");
        sb.Append(online
            ? $"""<button class="btn primary" data-action="{(problems ? "problems-generate" : "quiz-generate")}">Générer</button>"""
            : """<p class="hint">Hors connexion : la génération reprendra dès le retour de la connexion.</p>""");
        if (hasPrevious)
            sb.Append($"""<button class="btn" data-action="{(problems ? "problems-last" : "quiz-last")}">Revoir la dernière série</button>""");
        sb.Append("</div>");
        return sb.ToString();
    }

    // ─── Quiz ──────────────────────────────────────────────────────────────────────────

    /// <param name="answers">Réponses déjà cochées (le quiz est réaffiché avec un avertissement : elles ne sont pas perdues).</param>
    public static string Quiz(PracticeQuiz quiz, string courseTitle, IReadOnlyDictionary<string, string>? answers = null, string? warning = null)
    {
        var sb = new StringBuilder();
        sb.Append(Head(quiz.Title, $"{courseTitle} · {quiz.Questions.Count} questions"));
        if (warning is not null) sb.Append($"""<p class="warn">{E(warning)}</p>""");
        sb.Append("""<div data-form>""");
        var n = 0;
        foreach (var q in quiz.Questions)
        {
            n++;
            sb.Append($"""<section class="card q"><div class="q-num">Question {n}</div><div class="q-text">{Md(q.Text)}</div>""");
            for (var i = 0; i < q.Options.Count; i++)
            {
                var chosen = answers?.GetValueOrDefault($"q:{q.Id}") == i.ToString(CultureInfo.InvariantCulture) ? " checked" : "";
                sb.Append($"""<label class="opt"><input type="radio" name="q:{q.Id}" value="{i}"{chosen} /><span>{Md(q.Options[i])}</span></label>""");
            }
            sb.Append("</section>");
        }
        sb.Append("""<div class="actions"><button class="btn primary" data-action="quiz-submit">Valider mes réponses</button>""");
        sb.Append("""<button class="btn" data-action="quiz-new">Autre quiz</button></div></div>""");
        return sb.ToString();
    }

    public static string QuizResult(PracticeResult result, PracticeQuiz? quiz, string courseTitle, DateTime? savedAt = null)
    {
        var sb = new StringBuilder();
        sb.Append(Stale(savedAt));
        sb.Append(Head(quiz?.Title ?? "Résultat du quiz", courseTitle));
        var tone = result.Percentage >= 70 ? "good" : result.Percentage >= 50 ? "mid" : "low";
        sb.Append($"""<div class="score {tone}"><strong>{result.Score} / {result.Total}</strong><span>{result.Percentage:0} % de bonnes réponses</span></div>""");
        var n = 0;
        foreach (var f in result.Feedbacks)
        {
            n++;
            var options = quiz?.Questions.FirstOrDefault(q => q.Id == f.QuestionId)?.Options;
            string Option(int i) => options is not null && i >= 0 && i < options.Count ? Md(options[i]) : E($"Réponse {i + 1}");
            sb.Append($"""<section class="card q {(f.IsCorrect ? "ok" : "ko")}"><div class="q-num">Question {n} · {(f.IsCorrect ? "juste" : "à revoir")}</div>""");
            sb.Append($"""<div class="q-text">{Md(f.QuestionText)}</div>""");
            if (!f.IsCorrect)
                sb.Append($"""<div class="answer mine"><span class="tag">Ta réponse</span>{(f.SelectedOptionIndex < 0 ? "<p>Sans réponse</p>" : Option(f.SelectedOptionIndex))}</div>""");
            sb.Append($"""<div class="answer right"><span class="tag">Bonne réponse</span>{Option(f.CorrectOptionIndex)}</div>""");
            if (!string.IsNullOrWhiteSpace(f.Explanation))
                sb.Append($"""<details open><summary>Explication</summary>{Md(f.Explanation)}</details>""");
            sb.Append("</section>");
        }
        sb.Append("""<div class="actions"><button class="btn primary" data-action="quiz-new">Nouveau quiz</button>""");
        sb.Append("""<button class="btn" data-action="ask-tutor" data-value="Peux-tu m'expliquer les questions que j'ai ratées au quiz ?">En parler au tuteur</button></div>""");
        return sb.ToString();
    }

    // ─── Exercices corrigés ────────────────────────────────────────────────────────────

    public static string Problems(PracticeProblemSet set, string courseTitle, DateTime? savedAt = null)
    {
        var sb = new StringBuilder();
        sb.Append(Stale(savedAt));
        sb.Append(Head(set.Title, $"{courseTitle} · {set.Problems.Count} exercice{(set.Problems.Count > 1 ? "s" : "")}"));
        sb.Append("""<p class="muted">Cherche d'abord seul, puis ouvre les indices un par un ; la solution en dernier.</p>""");
        var n = 0;
        foreach (var p in set.Problems)
        {
            n++;
            sb.Append($"""<section class="card"><div class="q-num">Exercice {n}</div><div class="q-text">{Md(p.Statement)}</div>""");
            var h = 0;
            foreach (var hint in p.Hints)
                sb.Append($"""<details><summary>Indice {++h}</summary>{Md(hint)}</details>""");
            sb.Append($"""<details class="solution"><summary>Solution</summary>{Md(p.Solution)}</details>""");
            sb.Append($"""<button class="btn small" data-action="ask-tutor" data-value="{E($"Je bloque sur l'exercice {n} : {Plain(p.Statement, 300)}")}">Demander de l'aide au tuteur</button>""");
            sb.Append("</section>");
        }
        sb.Append("""<div class="actions"><button class="btn primary" data-action="problems-new">Nouvelle série</button></div>""");
        return sb.ToString();
    }

    private static string Plain(string text, int max)
    {
        var flat = string.Join(' ', (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    // ─── Devoirs ───────────────────────────────────────────────────────────────────────

    public static string StudentAssessments(List<StudentAssessment> list, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Devoirs et évaluations", "Donnés par tes professeurs"));
        if (list.Count == 0) return sb.Append("""<div class="hint">Aucun devoir pour l'instant.</div>""").ToString();

        var now = DateTime.UtcNow;
        var todo = list.Where(a => a.CanStart(now)).OrderBy(a => a.DueAt ?? DateTime.MaxValue).ToList();
        var others = list.Except(todo).OrderByDescending(a => a.DueAt ?? DateTime.MinValue).ToList();
        void Group(string label, List<StudentAssessment> items)
        {
            if (items.Count == 0) return;
            sb.Append($"""<div class="group">{E(label)}</div>""");
            foreach (var a in items)
            {
                var state = a.HasAttemptInProgress ? "En cours"
                    : a.BestScoreScaled is { } s ? $"{s:0.#} / {a.ScaleTo}"
                    : a.AttemptsUsed > 0 ? "Rendu"
                    : a.CanStart(now) ? "À faire" : Labels.Of(a.Status);
                sb.Append($"""<a class="row" data-action="open-site" data-value="/app/evaluations/{a.Id}/composer">""");
                sb.Append($"""<div class="row-main"><strong>{E(a.Title)}</strong><span>{E(string.Join(" · ", new[] { Labels.Of(a.Kind), a.Subject, a.DueAt is null ? null : "pour le " + Date(a.DueAt) }.Where(x => !string.IsNullOrWhiteSpace(x))))}</span></div>""");
                sb.Append($"""<span class="pill">{E(state)}</span></a>""");
            }
        }
        Group("À FAIRE", todo);
        Group("PASSÉS", others);
        sb.Append("""<p class="muted small">Les devoirs se font sur le site (photos de copie, minuteur) : chaque ligne l'ouvre dans ton navigateur.</p>""");
        return sb.ToString();
    }

    public static string TeacherAssessments(List<Assessment> list, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Mes évaluations", "Créées pour vos classes"));
        if (list.Count == 0) return sb.Append("""<div class="hint">Aucune évaluation pour l'instant.</div>""").ToString();
        foreach (var a in list.OrderByDescending(a => a.UpdatedAt))
        {
            sb.Append($"""<a class="row" data-action="open-site" data-value="/app/evaluations">""");
            sb.Append($"""<div class="row-main"><strong>{E(a.Title)}</strong><span>{E(string.Join(" · ", new[] { Labels.Of(a.Kind), a.Subject, $"{a.QuestionCount} questions", a.DueAt is null ? null : "pour le " + Date(a.DueAt) }.Where(x => !string.IsNullOrWhiteSpace(x))))}</span></div>""");
            sb.Append($"""<span class="pill">{E(Labels.Of(a.Status))}</span></a>""");
        }
        return sb.ToString();
    }

    // ─── Fiches épinglées ──────────────────────────────────────────────────────────────

    public static string Pinned(List<PinnedExplanation> list, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Fiches épinglées", "Les explications du tuteur que tu as gardées"));
        if (list.Count == 0)
            return sb.Append("""<div class="hint">Aucune fiche. Sur le site, épingle une réponse du tuteur pour la retrouver ici, même hors connexion.</div>""").ToString();
        foreach (var p in list.OrderByDescending(p => p.CreatedAt))
        {
            sb.Append($"""<section class="card"><div class="q-num">{E(p.Subject is null ? p.CourseTitle : $"{p.Subject} › {p.CourseTitle}")} · {E(Date(p.CreatedAt, "d MMM"))}</div>""");
            if (!string.IsNullOrWhiteSpace(p.Question)) sb.Append($"""<p class="question">{E(p.Question)}</p>""");
            sb.Append($"""<div class="q-text">{Md(p.Content)}</div>""");
            sb.Append($"""<button class="btn small" data-action="open-course" data-value="{p.CourseId}">Ouvrir le cours</button></section>""");
        }
        return sb.ToString();
    }

    // ─── Plan du jour ──────────────────────────────────────────────────────────────────

    public static string Plan(DayPlan plan, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Plan du jour", DateTime.Now.ToString("dddd d MMMM", Fr)));
        if (!plan.HasSchedule)
        {
            sb.Append("""<div class="hint">Remplis ton emploi du temps sur le site : le plan du jour place tes révisions dans tes heures libres.</div>""");
            return sb.Append("""<p><button class="btn" data-action="open-site" data-value="/app/emploi-du-temps">Ouvrir l'emploi du temps</button></p>""").ToString();
        }
        sb.Append($"""<p class="muted">{plan.AvailableMinutes} min libres aujourd'hui.</p>""");
        if (plan.Blocks.Count == 0) sb.Append("""<div class="hint">Rien de prévu : profites-en pour relire un cours ou faire un quiz.</div>""");
        foreach (var b in plan.Blocks.OrderBy(b => b.StartMinutes))
        {
            var action = b.TargetId is not { } id ? ""
                : b.Kind == "assessment" ? $""" data-action="open-site" data-value="/app/evaluations/{id}/composer" """
                : $""" data-action="open-course" data-value="{id}" """;
            sb.Append($"""<a class="row plan {E(b.Kind)}"{action}><span class="time">{Time(b.StartMinutes)}<br/>{Time(b.EndMinutes)}</span>""");
            sb.Append($"""<div class="row-main"><strong>{E(b.Title)}</strong><span>{E(PlanKind(b.Kind))}</span></div></a>""");
        }
        return sb.ToString();
    }

    private static string PlanKind(string kind) => kind switch
    {
        "assessment" => "Devoir à rendre bientôt",
        "course" => "Révision du cours",
        _ => kind
    };

    // ─── Progrès ───────────────────────────────────────────────────────────────────────

    public static string Progress(StudentProgress progress, IReadOnlyDictionary<Guid, string> courseTitles, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Mes progrès", null));
        sb.Append(progress.AverageOn20 is { } avg
            ? $"""<div class="score {(avg >= 12 ? "good" : avg >= 10 ? "mid" : "low")}"><strong>{avg:0.#} / 20</strong><span>moyenne des évaluations notées</span></div>"""
            : """<div class="hint">Pas encore de note : ta moyenne apparaîtra après ta première évaluation corrigée.</div>""");

        if (progress.Mastery.Count > 0)
        {
            sb.Append("""<div class="group">MAÎTRISE PAR COURS</div>""");
            foreach (var m in progress.Mastery.OrderBy(m => m.Percent))
            {
                var title = courseTitles.TryGetValue(m.CourseId, out var t) ? t : "Cours";
                sb.Append($"""<a class="row" data-action="open-course" data-value="{m.CourseId}"><div class="row-main"><strong>{E(title)}</strong>""");
                sb.Append($"""<span class="bar"><i style="width:{Math.Clamp(m.Percent, 0, 100).ToString("0", CultureInfo.InvariantCulture)}%"></i></span></div><span class="pill">{m.Percent:0} %</span></a>""");
            }
        }
        if (progress.Results.Count > 0)
        {
            sb.Append("""<div class="group">DERNIERS RÉSULTATS</div>""");
            foreach (var r in progress.Results.OrderByDescending(r => r.SubmittedAt).Take(12))
            {
                var score = r.ScoreScaled is { } s ? $"{s:0.#} / {r.ScaleTo}" : Labels.Of(r.Status);
                sb.Append($"""<div class="row"><div class="row-main"><strong>{E(r.Title)}</strong><span>{E(string.Join(" · ", new[] { Labels.Of(r.Kind), Date(r.SubmittedAt, "d MMM"), r.IsLate ? "en retard" : null }.Where(x => !string.IsNullOrWhiteSpace(x))))}</span></div><span class="pill">{E(score)}</span></div>""");
            }
        }
        return sb.ToString();
    }

    // ─── Mémoire du tuteur ─────────────────────────────────────────────────────────────

    public static string Memory(List<AgentMemoryItem> items, IReadOnlyDictionary<Guid, string> courseTitles, DateTime? savedAt)
    {
        var sb = new StringBuilder(Stale(savedAt));
        sb.Append(Head("Mémoire du tuteur", "Ce que le tuteur a retenu pour t'aider"));
        if (items.Count == 0)
            sb.Append("""<div class="hint">Le tuteur n'a encore rien retenu. Il note au fil des discussions ce qui t'aide (tes difficultés, ta façon d'apprendre).</div>""");
        foreach (var m in items.OrderByDescending(m => m.Importance).ThenByDescending(m => m.CreatedAt))
        {
            var course = m.CourseId is { } id && courseTitles.TryGetValue(id, out var t) ? t : null;
            sb.Append($"""<div class="row"><div class="row-main"><strong>{E(m.Content)}</strong><span>{E(string.Join(" · ", new[] { MemoryType(m.Type), course, Date(m.CreatedAt, "d MMM") }.Where(x => !string.IsNullOrWhiteSpace(x))))}</span></div></div>""");
        }
        sb.Append("""<p><button class="btn" data-action="open-site" data-value="/app/tuteur/memoire">Gérer sur le site</button></p>""");
        return sb.ToString();
    }

    private static string MemoryType(string type) => type switch
    {
        "difficulty" => "Difficulté",
        "preference" => "Préférence",
        "goal" => "Objectif",
        "fact" => "À savoir",
        _ => type
    };
}
