using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GPOE26.Evals;

public static class TrialStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    /// <summary>Refus attendu (droit, formule, quota, compte non configuré) : pas noté.</summary>
    public const string Skipped = "skipped";
    /// <summary>Panne (modèle, réseau, délai) : pas noté, mais compté pour juger si la passe est complète.</summary>
    public const string Error = "error";
}

public record Trial(string Status, List<CheckResult> Checks, JudgeVerdict? Judge, string? Model, long DurationMs, string? Output, string? Reason);

public record CaseResult(string Id, string Agent, List<string> Tags, List<Trial> Trials)
{
    public int Passed => Trials.Count(t => t.Status == TrialStatus.Passed);
    public int Scored => Trials.Count(t => t.Status is TrialStatus.Passed or TrialStatus.Failed);
}

public record RunResult(string Label, DateTime StartedAt, string? Commit, int Repeat, string? JudgeModel, string? TutorModel, List<CaseResult> Cases)
{
    /// <summary>Taux de réussite par agent, tous essais notés confondus (cas × répétitions).</summary>
    public Rate RateFor(string agent, IReadOnlySet<string>? onlyCases = null)
    {
        var trials = Cases.Where(c => c.Agent == agent && (onlyCases is null || onlyCases.Contains(c.Id))).SelectMany(c => c.Trials).ToList();
        return new Rate(trials.Count(t => t.Status == TrialStatus.Passed), trials.Count(t => t.Status is TrialStatus.Passed or TrialStatus.Failed));
    }

    public int NotPlayed(string agent) => Cases.Where(c => c.Agent == agent).SelectMany(c => c.Trials)
        .Count(t => t.Status is TrialStatus.Skipped or TrialStatus.Error);

    public Rate JudgeRateFor(string agent)
    {
        var judged = Cases.Where(c => c.Agent == agent).SelectMany(c => c.Trials).Where(t => t.Judge is not null).ToList();
        return new Rate(judged.Count(t => t.Judge!.Score >= Judge.PassScore), judged.Count);
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, CaseLoader.Json));

    public static RunResult Load(string path) =>
        JsonSerializer.Deserialize<RunResult>(File.ReadAllText(path), CaseLoader.Json) ?? throw new InvalidDataException(path);
}

public static class Report
{
    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    public static string Summary(RunResult run)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Évaluations des agents : {run.Label}");
        sb.AppendLine();
        sb.AppendLine($"- Date : {run.StartedAt:yyyy-MM-dd HH:mm} UTC ; commit : {run.Commit ?? "?"} ; répétitions : {run.Repeat}");
        sb.AppendLine($"- Modèle du tuteur demandé : {run.TutorModel ?? "celui du serveur"} ; juge : {run.JudgeModel ?? "aucun"}");
        var models = run.Cases.SelectMany(c => c.Trials).Select(t => t.Model).Where(m => m is not null).Distinct().ToList();
        if (models.Count > 0) sb.AppendLine($"- Modèles qui ont répondu (tuteur) : {string.Join(", ", models)}");
        sb.AppendLine();
        sb.AppendLine("| Agent | Réussite (règles) | Intervalle 95 % | Notés | Non joués | Juge ≥ 4 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var agent in Agents.All)
        {
            var rate = run.RateFor(agent);
            if (rate.Total == 0 && run.NotPlayed(agent) == 0) continue;
            var (low, high) = rate.Interval;
            var judge = run.JudgeRateFor(agent);
            sb.AppendLine($"| {agent} | {Pct(rate.Value)} ({rate.Passed}/{rate.Total}) | {Pct(low)} à {Pct(high)} | {rate.Total} | {run.NotPlayed(agent)} | {(judge.Total == 0 ? "—" : $"{Pct(judge.Value)} ({judge.Passed}/{judge.Total})")} |");
        }

        var failed = run.Cases.Where(c => c.Trials.Any(t => t.Status == TrialStatus.Failed)).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Cas en échec");
            foreach (var c in failed)
            {
                var trial = c.Trials.First(t => t.Status == TrialStatus.Failed);
                var reasons = trial.Checks.Where(k => !k.Passed).Select(k => $"{k.Kind} : {k.Detail}");
                sb.AppendLine($"- **{c.Id}** ({c.Passed}/{c.Scored}) : {string.Join(" ; ", reasons)}");
            }
        }

        var notPlayed = run.Cases.SelectMany(c => c.Trials.Where(t => t.Status is TrialStatus.Skipped or TrialStatus.Error).Select(t => (c.Id, t)))
            .GroupBy(x => x.t.Reason).ToList();
        if (notPlayed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Non joués");
            foreach (var g in notPlayed)
                sb.AppendLine($"- {g.Count()} essai(s) : {g.Key} ({string.Join(", ", g.Select(x => x.Id).Distinct().Take(6))}{(g.Select(x => x.Id).Distinct().Count() > 6 ? "…" : "")})");
        }
        return sb.ToString();
    }

    /// <summary>Comparaison à la référence, agent par agent, sur les seuls cas présents des deux côtés.</summary>
    public static (string Text, bool Regression) Compare(RunResult baseline, RunResult current, double margin)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Comparaison : {current.Label} contre la référence {baseline.Label}");
        sb.AppendLine();
        sb.AppendLine($"Marge tolérée : {Pct(margin)}. « Au moins aussi bon » = une baisse de plus de {Pct(margin)} est exclue à 95 %.");
        sb.AppendLine();
        sb.AppendLine("| Agent | Référence | Actuel | Écart (intervalle 95 %) | Verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        var regression = false;
        var common = baseline.Cases.Select(c => c.Id).Intersect(current.Cases.Select(c => c.Id)).ToHashSet();
        foreach (var agent in Agents.All)
        {
            var b = baseline.RateFor(agent, common);
            var c = current.RateFor(agent, common);
            if (b.Total == 0 && c.Total == 0) continue;
            var incomplete = current.NotPlayed(agent) > current.Cases.Count(x => x.Agent == agent) * current.Repeat / 10;
            var cmp = Stats.Compare(agent, b, c, margin, incomplete);
            regression |= cmp.Verdict == Verdict.Regression;
            sb.AppendLine($"| {agent} | {Pct(b.Value)} ({b.Passed}/{b.Total}) | {Pct(c.Value)} ({c.Passed}/{c.Total}) | {Pct(cmp.DiffLow)} à {Pct(cmp.DiffHigh)} | {Stats.Label(cmp.Verdict)} |");
        }

        // Cas qui réussissaient toujours et échouent maintenant : à regarder en premier
        var broken = current.Cases.Where(c => common.Contains(c.Id) && c.Scored > 0 && c.Passed < c.Scored)
            .Where(c => baseline.Cases.First(b => b.Id == c.Id) is { Scored: > 0 } b && b.Passed == b.Scored)
            .Select(c => c.Id).ToList();
        if (broken.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Cas qui réussissaient toujours et échouent maintenant : {string.Join(", ", broken)}");
        }
        var fixedCases = current.Cases.Where(c => common.Contains(c.Id) && c.Scored > 0 && c.Passed == c.Scored)
            .Where(c => baseline.Cases.First(b => b.Id == c.Id) is { Scored: > 0 } b && b.Passed == 0)
            .Select(c => c.Id).ToList();
        if (fixedCases.Count > 0) sb.AppendLine($"\nCas qui échouaient toujours et réussissent maintenant : {string.Join(", ", fixedCases)}");
        return (sb.ToString(), regression);
    }
}
