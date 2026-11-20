using System.Diagnostics;
using System.Globalization;
using GPOE26.Evals;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

// ══════════════════════════════════════════════════════════════════════════════
// Évaluations des agents (lot A0). Voir evals/README.md.
//   run       [--agent tutor|compose|site] [--filter texte] [--repeat N] [--judge] [--label nom]
//   compare   <résultat.json> [référence.json] [--margin 0.05]
//   baseline  <résultat.json>          fait de ce résultat la référence (evals/baseline.json)
//   check                              vérifie les cas sans rien appeler
// ══════════════════════════════════════════════════════════════════════════════

Console.OutputEncoding = System.Text.Encoding.UTF8;
var root = FindEvalsDir();
if (root is null)
{
    Console.Error.WriteLine("Dossier evals/ introuvable : lancez la commande depuis le dépôt.");
    return 2;
}
var casesDir = Path.Combine(root, "cases");
var fixturesDir = Path.Combine(root, "fixtures");
var resultsDir = Path.Combine(root, "results");
var baselinePath = Path.Combine(root, "baseline.json");

var command = args.FirstOrDefault() ?? "help";
var options = Options(args.Skip(1).ToArray());

var config = new ConfigurationBuilder()
    // La clé d'IA de l'AppHost (dotnet user-secrets set "Parameters:llm-apikey" …) sert au juge
    .AddUserSecrets("4c64a2bb-b808-44fa-9cf6-76771ec404b7")
    .AddEnvironmentVariables()
    .Build();

switch (command)
{
    case "check":
    {
        var cases = CaseLoader.LoadAll(casesDir);
        var errors = CaseLoader.Validate(cases, fixturesDir);
        foreach (var e in errors) Console.Error.WriteLine("  " + e);
        Console.WriteLine($"{cases.Count} cas ({string.Join(", ", cases.GroupBy(c => c.Agent).Select(g => $"{g.Key} {g.Count()}"))}) ; {errors.Count} erreur(s).");
        return errors.Count == 0 ? 0 : 1;
    }

    case "run":
        return await RunAsync();

    case "compare":
    {
        var current = options.Positional.ElementAtOrDefault(0) ?? LatestResult();
        var reference = options.Positional.ElementAtOrDefault(1) ?? baselinePath;
        if (current is null || !File.Exists(current) || !File.Exists(reference))
        {
            Console.Error.WriteLine("Résultat ou référence introuvable. Usage : compare <résultat.json> [référence.json]");
            return 2;
        }
        var margin = double.Parse(options.Get("margin") ?? "0.05", CultureInfo.InvariantCulture);
        var (text, regression) = Report.Compare(RunResult.Load(reference), RunResult.Load(current), margin);
        Console.WriteLine(text);
        return regression ? 1 : 0;
    }

    case "baseline":
    {
        var source = options.Positional.ElementAtOrDefault(0) ?? LatestResult();
        if (source is null || !File.Exists(source)) { Console.Error.WriteLine("Résultat introuvable."); return 2; }
        File.Copy(source, baselinePath, overwrite: true);
        Console.WriteLine($"Référence mise à jour : {Path.GetFileName(source)} → evals/baseline.json");
        return 0;
    }

    default:
        Console.WriteLine("""
            Évaluations des agents GPOE26 (voir evals/README.md)
              run       [--agent tutor|compose|site] [--filter texte] [--repeat N] [--judge] [--label nom]
              compare   [résultat.json] [référence.json] [--margin 0.05]
              baseline  [résultat.json]
              check
            """);
        return command == "help" ? 0 : 2;
}

async Task<int> RunAsync()
{
    var all = CaseLoader.LoadAll(casesDir);
    var errors = CaseLoader.Validate(all, fixturesDir);
    if (errors.Count > 0)
    {
        foreach (var e in errors) Console.Error.WriteLine("  " + e);
        return 1;
    }

    var agent = options.Get("agent");
    var filter = options.Get("filter");
    var cases = all.Where(c => (agent is null || c.Agent == agent)
            && (filter is null || c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) || c.Tags.Contains(filter)))
        .ToList();
    var repeat = int.Parse(options.Get("repeat") ?? "1", CultureInfo.InvariantCulture);
    var settings = EvalSettings.From(config);

    Judge? judge = null;
    var judgeModel = config["Evals:JudgeModel"];
    if (options.Has("judge"))
    {
        if (judgeModel is null)
        {
            Console.Error.WriteLine("--judge demande Evals__JudgeModel (un seul modèle, toujours le même d'une passe à l'autre).");
            return 2;
        }
        var judgeConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LLM:BaseUrl"] = config["Evals:JudgeBaseUrl"] ?? config["Parameters:llm-baseurl"] ?? config["LLM:BaseUrl"],
            ["LLM:ApiKey"] = config["Evals:JudgeApiKey"] ?? config["LLM:ApiKey"] ?? config["Parameters:llm-apikey"],
            ["LLM:Models"] = judgeModel
        }).Build();
        judge = new Judge(new LlmClient(new HttpClient { Timeout = TimeSpan.FromMinutes(3) }, judgeConfig, NullLogger<LlmClient>.Instance));
    }
    else judgeModel = null;

    Console.WriteLine($"{cases.Count} cas × {repeat} ; juge : {judgeModel ?? "aucun"}. Les cas sont joués l'un après l'autre.");
    using var platform = new Platform(settings);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var started = DateTime.UtcNow;
    var results = new List<CaseResult>();
    var consecutiveOutages = 0;
    foreach (var c in cases)
    {
        var trials = new List<Trial>();
        for (var i = 0; i < repeat && !cts.IsCancellationRequested; i++)
        {
            var watch = Stopwatch.StartNew();
            Trial trial;
            try
            {
                var output = c.Agent switch
                {
                    Agents.Tutor => await platform.AskTutorAsync(c, fixturesDir, cts.Token),
                    Agents.Compose => await platform.ComposeAsync(c, cts.Token),
                    _ => await platform.AskSiteAsync(c, cts.Token)
                };
                var checks = Rules.Evaluate(c.Checks, output);
                var course = c.Course is null ? null : CourseFixture.Load(Path.Combine(fixturesDir, "courses", c.Course + ".md")).Markdown;
                var verdict = judge is null ? null : await judge.RateAsync(c, course, output, cts.Token);
                trial = new Trial(checks.All(k => k.Passed) ? TrialStatus.Passed : TrialStatus.Failed, checks, verdict, output.Model,
                    watch.ElapsedMilliseconds, Truncate(output.Text, 6000) + ActionsText(output), null);
                consecutiveOutages = 0;
            }
            catch (NotPlayedException ex)
            {
                trial = new Trial(ex.Infrastructure ? TrialStatus.Error : TrialStatus.Skipped, [], null, null, watch.ElapsedMilliseconds, null, ex.Message);
                consecutiveOutages = ex.Infrastructure ? consecutiveOutages + 1 : 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            trials.Add(trial);
            var mark = trial.Status switch { TrialStatus.Passed => "ok  ", TrialStatus.Failed => "ÉCHEC", TrialStatus.Skipped => "non joué", _ => "panne" };
            var why = trial.Status == TrialStatus.Failed
                ? string.Join(" ; ", trial.Checks.Where(k => !k.Passed).Select(k => k.Detail))
                : trial.Reason ?? "";
            Console.WriteLine($"  {mark,-8} {c.Id}{(repeat > 1 ? $" #{i + 1}" : "")} ({trial.DurationMs / 1000.0:0.0} s){(trial.Judge is { } j ? $" juge {j.Score}/5" : "")} {why}");
        }
        results.Add(new CaseResult(c.Id, c.Agent, c.Tags, trials));
        if (cts.IsCancellationRequested) break;
        if (consecutiveOutages >= 5)
        {
            Console.Error.WriteLine("Cinq pannes de suite : la passe s'arrête (service ou modèle indisponible).");
            break;
        }
    }

    var label = options.Get("label") ?? started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var run = new RunResult(label, started, GitCommit(), repeat, judgeModel, settings.TutorModel, results);
    Directory.CreateDirectory(resultsDir);
    var path = Path.Combine(resultsDir, $"{started:yyyyMMdd-HHmmss}-{Safe(label)}.json");
    run.Save(path);
    var summary = Report.Summary(run);
    File.WriteAllText(Path.ChangeExtension(path, ".md"), summary);
    Console.WriteLine();
    Console.WriteLine(summary);
    Console.WriteLine($"Résultat : {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");

    if (File.Exists(baselinePath))
    {
        var (text, regression) = Report.Compare(RunResult.Load(baselinePath), run, 0.05);
        Console.WriteLine();
        Console.WriteLine(text);
        return regression ? 1 : 0;
    }
    Console.WriteLine("Pas encore de référence : `baseline` fait de ce résultat la référence.");
    return 0;
}

static string? FindEvalsDir()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "evals", "cases"))) return Path.Combine(dir.FullName, "evals");
    return null;
}

string? LatestResult() => Directory.Exists(resultsDir)
    ? Directory.GetFiles(resultsDir, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
    : null;

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\n[…]";

static string ActionsText(AgentOutput o) => o.Actions.Count == 0 && o.Rejected.Count == 0 ? ""
    : "\n\n[actions] " + string.Join(" | ", o.Actions.Select(a => $"{a.Type}{(a.Destructive ? " (à confirmer)" : "")} : {a.Summary}"))
      + (o.Rejected.Count == 0 ? "" : "\n[refusées] " + string.Join(" | ", o.Rejected));

static string Safe(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());

static string? GitCommit()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false });
        var sha = p?.StandardOutput.ReadToEnd().Trim();
        p?.WaitForExit();
        return string.IsNullOrEmpty(sha) ? null : sha + (DirtyTree() ? "+modifs" : "");
    }
    catch (System.ComponentModel.Win32Exception) { return null; }
}

static bool DirtyTree()
{
    using var p = Process.Start(new ProcessStartInfo("git", "status --porcelain --untracked-files=no") { RedirectStandardOutput = true, UseShellExecute = false });
    var dirty = (p?.StandardOutput.ReadToEnd().Length ?? 0) > 0;
    p?.WaitForExit();
    return dirty;
}

static CliOptions Options(string[] args)
{
    var named = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var positional = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) { positional.Add(args[i]); continue; }
        var name = args[i][2..];
        var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") && name != "judge" ? args[++i] : null;
        named[name] = value;
    }
    return new CliOptions(named, positional);
}

record CliOptions(Dictionary<string, string?> Named, List<string> Positional)
{
    public string? Get(string name) => Named.GetValueOrDefault(name);
    public bool Has(string name) => Named.ContainsKey(name);
}
