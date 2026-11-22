using System.Globalization;
using System.Text;
using GPOE26.Desktop.Controls;
using GPOE26.Desktop.Services;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GPOE26.Desktop;

/// <summary>Dernier quiz généré sur un cours, et sa correction une fois rendu : gardés sur le poste (relecture hors connexion).</summary>
public sealed record QuizRecord(PracticeQuiz Quiz, PracticeResult? Result);

/// <summary>
/// Outils de travail, ouverts depuis le menu de gauche (avec le profil) :
/// - quiz et exercices corrigés : en plein écran, pour s'exercer sans rien autour ;
/// - devoirs, fiches, plan du jour, progrès, mémoire : à la place du cours, au milieu (le tuteur reste à droite) ;
/// - créer un cours : un ou deux espaces côte à côte, chacun avec son agent, pour écrire deux cours en même temps.
/// « Retour au cours » (ou Échap) referme l'outil. La barre de droite reste celle du tuteur.
/// </summary>
public sealed partial class MainWindow
{
    private const string TwoSpacesKey = "create-two-spaces";

    private readonly IPracticeQuizApi _quizzes = App.Services.GetRequiredService<IPracticeQuizApi>();

    /// <summary>Outil ouvert (null : la lecture du cours) : quiz, problems, create, assessments, pinned, plan, progress, memory.</summary>
    private string? _tool;
    private PracticeQuiz? _quiz;
    private string _difficulty = "standard";
    private CourseDraftPane? _firstSpace, _secondSpace;

    private bool IsStudent => _app.Profile?.Role == UserRole.Student;
    private bool IsTeacher => _app.Profile?.Role == UserRole.Teacher;
    private bool CanGenerate => !_app.IsReadOnly && _net.IsOnline;

    private static string QuizKey(Guid courseId) => $"quiz:{courseId}";
    private static string ProblemsKey(Guid courseId) => $"problems:{courseId}";

    /// <summary>Outils proposés selon le rôle (le plan et les progrès sont ceux d'un élève).</summary>
    private void SetupTools()
    {
        AssessmentsRow.Visibility = IsStudent || IsTeacher ? Visibility.Visible : Visibility.Collapsed;
        AssessmentsLabel.Text = IsTeacher ? "Mes évaluations" : "Devoirs";
        PlanRow.Visibility = ProgressRow.Visibility = IsStudent ? Visibility.Visible : Visibility.Collapsed;
        CreateCourseRow.Visibility = IsStudent || IsTeacher ? Visibility.Visible : Visibility.Collapsed;
        if (IsTeacher) _difficulty = "approfondi";

        SetKeyTip(ModelButton, "Modèle de réponse", "Ctrl", "Maj", "M");
        SetKeyTip(AttachButton, "Joindre la photo de ta copie");
        SetKeyTip(StageBack, "Retour au cours", "Échap");
        SetBusy(false, null);
        UpdateUsage();
    }

    private bool IsAllowed(string tool) => tool switch
    {
        "quiz" or "problems" or "pinned" or "memory" => true,
        "assessments" or "create" => IsStudent || IsTeacher,
        "plan" or "progress" => IsStudent,
        _ => false
    };

    // ══════════════════════════ Ouvrir et fermer un outil ══════════════════════════

    private async void OnToolClick(object sender, RoutedEventArgs e) => await ShowToolAsync((string)((FrameworkElement)sender).Tag);

    private async void OnStageClose(object sender, RoutedEventArgs e) => await CloseStageAsync();

    private async Task ShowToolAsync(string tool)
    {
        if (!IsAllowed(tool)) return;
        _tool = tool;
        ApplyStage();
        MarkTool();

        var (title, withCourse) = tool switch
        {
            "quiz" => ("Quiz", true),
            "problems" => ("Exercices corrigés", true),
            "create" => ("Créer un cours", false),
            "assessments" => (IsTeacher ? "Mes évaluations" : "Devoirs", false),
            "pinned" => ("Fiches épinglées", false),
            "plan" => ("Plan du jour", false),
            "progress" => ("Mes progrès", false),
            _ => ("Mémoire du tuteur", false)
        };
        StageTitle.Text = title;
        StageSubtitle.Text = withCourse && _current is not null ? $"  ·  {_current.Title}" : "";

        var titles = _courses.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Title);
        switch (tool)
        {
            case "quiz": await ShowQuizFormAsync(); break;
            case "problems": await ShowProblemsAsync(); break;
            case "create": await ShowCreateAsync(); break;
            case "assessments" when IsTeacher:
                await ShowToolPageAsync("Mes évaluations", _data.TeacherAssessmentsAsync, ToolPages.TeacherAssessments); break;
            case "assessments":
                await ShowToolPageAsync("Devoirs", _data.StudentAssessmentsAsync, ToolPages.StudentAssessments); break;
            case "pinned": await ShowToolPageAsync("Fiches épinglées", _data.PinnedAsync, ToolPages.Pinned); break;
            case "plan": await ShowToolPageAsync("Plan du jour", _data.TodayPlanAsync, ToolPages.Plan); break;
            case "progress":
                await ShowToolPageAsync("Mes progrès", _data.ProgressAsync, (p, at) => ToolPages.Progress(p, titles, at)); break;
            case "memory":
                await ShowToolPageAsync("Mémoire du tuteur", _data.MemoriesAsync, (m, at) => ToolPages.Memory(m, titles, at)); break;
        }
    }

    private Task CloseStageAsync()
    {
        _tool = null;
        AgentNotice.IsOpen = false;
        ApplyLayout();   // remet les panneaux comme l'élève les avait laissés
        MarkTool();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Place de l'outil : plein écran pour s'exercer (quiz, exercices), au milieu et à droite pour créer un cours
    /// (le menu reste là), à la place du cours pour les autres pages (menu et tuteur restent là).
    /// </summary>
    private void ApplyStage()
    {
        if (_tool is null)
        {
            Stage.Visibility = Visibility.Collapsed;
            ReadingPane.Visibility = Visibility.Visible;
            return;
        }

        var full = _tool is "quiz" or "problems";
        var wide = _tool == "create";
        var (column, span) = full ? (0, 5) : wide ? (2, 3) : (2, 1);
        Grid.SetColumn(Stage, column);
        Grid.SetColumnSpan(Stage, span);
        Stage.Margin = new Thickness(full || !_layout.MenuOpen ? 6 : 0, 0, full || wide || !_layout.AgentOpen ? 6 : 0, 6);
        Stage.Visibility = Visibility.Visible;
        ReadingPane.Visibility = Visibility.Collapsed;
        if (full) MenuPane.Visibility = MenuSplitter.Visibility = Visibility.Collapsed;
        if (full || wide) AgentPane.Visibility = AgentSplitter.Visibility = Visibility.Collapsed;

        ToolView.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        CreateView.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        TwoSpacesToggle.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>L'outil ouvert est surligné dans le menu, comme le cours sélectionné.</summary>
    private void MarkTool()
    {
        var selected = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        foreach (var row in new[] { CreateCourseRow, QuizRow, ProblemsRow, AssessmentsRow, PinnedRow, PlanRow, ProgressRow, MemoryRow })
            if ((string)row.Tag == _tool) row.Background = selected; else row.ClearValue(Control.BackgroundProperty);
    }

    /// <summary>Le tuteur, la question éventuellement préremplie (« Je bloque sur l'exercice 2… »), le curseur dans la saisie.</summary>
    private async Task OpenTutorAsync(string? prefill)
    {
        if (_tool is "quiz" or "problems" or "create") await CloseStageAsync();   // le tuteur doit être visible
        if (!_layout.MenuOpen && !_layout.AgentOpen) ToggleFocus();
        else if (!_layout.AgentOpen) SetAgentOpen(true);
        if (!string.IsNullOrEmpty(prefill))
        {
            QuestionBox.Text = prefill;
            QuestionBox.SelectionStart = prefill.Length;
        }
        QuestionBox.Focus(FocusState.Programmatic);
    }

    private void OnSectionToggle(object sender, RoutedEventArgs e)
    {
        var (section, chevron) = (string)((FrameworkElement)sender).Tag switch
        {
            "tools" => ((FrameworkElement)ToolsList, ToolsChevron),
            "courses" => (CoursesSection, CoursesChevron),
            _ => (DiscussionsList, DiscussionsChevron)
        };
        var open = section.Visibility != Visibility.Visible;
        section.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        chevron.Glyph = open ? "" : "";
    }

    // ══════════════════════════ Menu : discussions et compteurs ══════════════════════════

    private async Task RefreshNavAsync()
    {
        if (!_app.IsSignedIn) return;
        var conversations = _data.ConversationsAsync();
        var pinned = _data.PinnedAsync();
        var todo = IsStudent ? _data.StudentAssessmentsAsync() : null;

        // La discussion libre du site (lot P1, rangée sous Guid.Empty) n'a pas encore d'écran ici : elle n'est pas listée
        var list = ((await conversations).Value ?? []).Where(c => c.CourseId != Guid.Empty).ToList();
        DiscussionsList.Children.Clear();
        if (list.Count == 0)
            DiscussionsList.Children.Add(new TextBlock
            {
                Text = "Aucune discussion pour l'instant.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 2, 10, 4),
                Style = (Style)Application.Current.Resources["RowHint"]
            });

        var today = DateTime.Today;
        string Group(DateTime utc)
        {
            var day = utc.ToLocalTime().Date;
            return day == today ? "Aujourd'hui" : day == today.AddDays(-1) ? "Hier" : day > today.AddDays(-7) ? "Cette semaine" : "Plus ancien";
        }
        foreach (var group in list.OrderByDescending(c => c.UpdatedAt).GroupBy(c => Group(c.UpdatedAt)))
        {
            DiscussionsList.Children.Add(new TextBlock
            {
                Text = group.Key,
                Margin = new Thickness(10, 6, 10, 2),
                Style = (Style)Application.Current.Resources["RowHint"]
            });
            foreach (var c in group) DiscussionsList.Children.Add(DiscussionRow(c));
        }
        MarkDiscussion();

        var pins = (await pinned).Value?.Count ?? 0;
        PinnedBadge.Text = pins > 0 ? pins.ToString(CultureInfo.InvariantCulture) : "";
        if (todo is not null)
        {
            var due = (await todo).Value?.Count(a => a.CanStart(DateTime.UtcNow)) ?? 0;
            AssessmentsBadge.Text = due > 0 ? $"{due} à faire" : "";
        }
    }

    private Button DiscussionRow(TutorConversationSummary c)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = c.CourseTitle, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        if (!string.IsNullOrWhiteSpace(c.LastMessage))
            text.Children.Add(new TextBlock
            {
                Text = string.Join(' ', c.LastMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["RowHint"]
            });

        var row = new Button { Style = (Style)Application.Current.Resources["ToolRow"], Content = text, Tag = c.CourseId };
        ToolTipService.SetToolTip(row, $"{c.Subject} › {c.CourseTitle} · {c.MessageCount} messages");
        row.Click += async (_, _) =>
        {
            if (!SelectCourse(c.CourseId))
                Notice(AgentNotice, InfoBarSeverity.Warning, "", "Ce cours n'est plus dans ta liste.");
            await CloseStageAsync();
            await OpenTutorAsync(null);
        };
        return row;
    }

    /// <summary>La discussion du cours ouvert est surlignée dans le menu.</summary>
    private void MarkDiscussion()
    {
        var selected = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        foreach (var row in DiscussionsList.Children.OfType<Button>())
            if (row.Tag is Guid id && id == _current?.Id) row.Background = selected; else row.ClearValue(Control.BackgroundProperty);
    }

    /// <summary>Ouvre un cours au milieu (par la liste du menu, qui charge le cours et sa discussion).</summary>
    private bool SelectCourse(Guid courseId)
    {
        if (_courses.FirstOrDefault(c => c.Id == courseId) is not { } course) return false;
        if (CourseSearch.Text.Length > 0) { CourseSearch.Text = ""; FilterCourses(); }
        CourseList.SelectedItem = course;
        return true;
    }

    // ══════════════════════════ Pages des outils ══════════════════════════

    /// <summary>Lecture réseau d'abord, copie locale ensuite (<see cref="OfflineData"/>), puis la page de l'outil.</summary>
    private async Task ShowToolPageAsync<T>(string title, Func<Task<Loaded<T>>> load, Func<T, DateTime?, string> render)
    {
        var tool = _tool;
        await ToolView.ShowToolAsync(ToolPages.Loading("Chargement…"));
        var loaded = await load();
        if (_tool != tool) return;
        await ToolView.ShowToolAsync(loaded.Value is { } value
            ? render(value, loaded.SavedAt)
            : ToolPages.Message(title, loaded.Error ?? "Indisponible pour le moment."));
    }

    private async Task OnToolActionAsync(ToolAction action)
    {
        switch (action.Name)
        {
            case "quiz-generate": await GenerateQuizAsync(action.Values); break;
            case "quiz-submit": await SubmitQuizAsync(action.Values); break;
            case "quiz-new": await ShowQuizFormAsync(); break;
            case "quiz-last": await ShowLastQuizAsync(); break;
            case "problems-generate": await GenerateProblemsAsync(action.Values); break;
            case "problems-new": await ShowProblemsFormAsync(); break;
            case "problems-last": await ShowProblemsAsync(); break;
            case "ask-tutor": await OpenTutorAsync(action.Value); break;
            case "open-course" when Guid.TryParse(action.Value, out var id):
                if (SelectCourse(id)) await CloseStageAsync();
                else Notice(AgentNotice, InfoBarSeverity.Warning, "", "Ce cours n'est pas dans ta liste.");
                break;
            case "open-site" when action.Value is { } path && path.StartsWith('/'):
                await global::Windows.System.Launcher.LaunchUriAsync(new Uri(new Uri(_addresses.Web), path));
                break;
        }
    }

    private void SetToolBusy(bool busy) =>
        AgentBusyDot.Visibility = busy || _asking is not null ? Visibility.Visible : Visibility.Collapsed;

    // ── Quiz ──

    private async Task ShowQuizFormAsync()
    {
        if (_current is not { } course)
        {
            await ToolView.ShowToolAsync(ToolPages.Message("Quiz", "Ouvre d'abord un cours dans le menu : le quiz porte sur le cours que tu lis."));
            return;
        }
        var kept = await _data.KeptAsync<QuizRecord>(QuizKey(course.Id));
        await ToolView.ShowToolAsync(ToolPages.GenerateForm(false, course.Title, _difficulty, CanGenerate, kept is not null));
    }

    private async Task GenerateQuizAsync(IReadOnlyDictionary<string, string> values)
    {
        if (_current is not { } course || !CanGenerate) return;
        _difficulty = values.GetValueOrDefault("difficulty") ?? _difficulty;
        var count = int.TryParse(values.GetValueOrDefault("count"), out var n) ? n : 5;

        await ToolView.ShowToolAsync(ToolPages.Loading("L'IA prépare ton quiz… (jusqu'à deux minutes)"));
        SetToolBusy(true);
        try
        {
            var result = await _quizzes.GenerateAsync(course.Title, course.Id, count, _difficulty);
            if (_tool != "quiz" || _current?.Id != course.Id) return;
            if (!result.Ok || result.Value is null)
            {
                await ToolView.ShowToolAsync(ToolPages.Message("Quiz", result.Error ?? "Le quiz n'a pas pu être préparé.", "quiz-new", "Réessayer"));
                return;
            }
            _quiz = result.Value;
            await _data.KeepAsync(QuizKey(course.Id), new QuizRecord(_quiz, null));
            await ToolView.ShowToolAsync(ToolPages.Quiz(_quiz, course.Title));
        }
        finally { SetToolBusy(false); }
    }

    private async Task SubmitQuizAsync(IReadOnlyDictionary<string, string> values)
    {
        if (_quiz is not { } quiz || _current is not { } course) return;
        var answers = quiz.Questions
            .Select(q => new PracticeAnswer(q.Id, int.TryParse(values.GetValueOrDefault($"q:{q.Id}"), out var i) ? i : -1))
            .ToList();
        var missing = answers.Count(a => a.SelectedOptionIndex < 0);
        if (missing > 0)
        {
            await ToolView.ShowToolAsync(ToolPages.Quiz(quiz, course.Title, values,
                missing == 1 ? "Il reste une question sans réponse." : $"Il reste {missing} questions sans réponse."));
            return;
        }
        if (!CanGenerate)
        {
            await ToolView.ShowToolAsync(ToolPages.Quiz(quiz, course.Title, values, "Hors connexion : valide dès le retour de la connexion."));
            return;
        }

        SetToolBusy(true);
        try
        {
            var result = await _quizzes.SubmitAsync(quiz.QuizId, answers);
            if (!result.Ok || result.Value is null)
            {
                await ToolView.ShowToolAsync(ToolPages.Quiz(quiz, course.Title, values, result.Error ?? "La correction n'a pas pu être faite. Réessaie."));
                return;
            }
            await _data.KeepAsync(QuizKey(course.Id), new QuizRecord(quiz, result.Value));
            if (_tool == "quiz" && _current?.Id == course.Id)
                await ToolView.ShowToolAsync(ToolPages.QuizResult(result.Value, quiz, course.Title));
        }
        finally { SetToolBusy(false); }
    }

    private async Task ShowLastQuizAsync()
    {
        if (_current is not { } course || await _data.KeptAsync<QuizRecord>(QuizKey(course.Id)) is not { } kept) return;
        if (kept.Value.Result is { } result)
        {
            await ToolView.ShowToolAsync(ToolPages.QuizResult(result, kept.Value.Quiz, course.Title, kept.SavedAt));
            return;
        }
        _quiz = kept.Value.Quiz;
        await ToolView.ShowToolAsync(ToolPages.Quiz(_quiz, course.Title));
    }

    // ── Exercices corrigés : la dernière série reste lisible hors connexion ──

    private async Task ShowProblemsAsync()
    {
        if (_current is not { } course)
        {
            await ToolView.ShowToolAsync(ToolPages.Message("Exercices corrigés", "Ouvre d'abord un cours dans le menu : les exercices portent sur le cours que tu lis."));
            return;
        }
        if (await _data.KeptAsync<PracticeProblemSet>(ProblemsKey(course.Id)) is { } kept)
            await ToolView.ShowToolAsync(ToolPages.Problems(kept.Value, course.Title, kept.SavedAt));
        else
            await ShowProblemsFormAsync();
    }

    private async Task ShowProblemsFormAsync()
    {
        if (_current is not { } course) { await ShowProblemsAsync(); return; }
        var kept = await _data.KeptAsync<PracticeProblemSet>(ProblemsKey(course.Id));
        await ToolView.ShowToolAsync(ToolPages.GenerateForm(true, course.Title, _difficulty, CanGenerate, kept is not null));
    }

    private async Task GenerateProblemsAsync(IReadOnlyDictionary<string, string> values)
    {
        if (_current is not { } course || !CanGenerate) return;
        _difficulty = values.GetValueOrDefault("difficulty") ?? _difficulty;
        var count = Math.Clamp(int.TryParse(values.GetValueOrDefault("count"), out var n) ? n : 3, 1, 5);

        await ToolView.ShowToolAsync(ToolPages.Loading("L'IA rédige les exercices et leurs corrigés… (jusqu'à deux minutes)"));
        SetToolBusy(true);
        try
        {
            var result = await _quizzes.GenerateProblemsAsync(course.Title, course.Id, count, _difficulty);
            if (_tool != "problems" || _current?.Id != course.Id) return;
            if (!result.Ok || result.Value is null)
            {
                await ToolView.ShowToolAsync(ToolPages.Message("Exercices corrigés", result.Error ?? "Les exercices n'ont pas pu être préparés.", "problems-new", "Réessayer"));
                return;
            }
            await _data.KeepAsync(ProblemsKey(course.Id), result.Value);
            await ToolView.ShowToolAsync(ToolPages.Problems(result.Value, course.Title));
        }
        finally { SetToolBusy(false); }
    }

    // ══════════════════════════ Créer un cours : un ou deux espaces ══════════════════════════

    private async Task ShowCreateAsync()
    {
        if (_firstSpace is null)
        {
            _firstSpace = NewSpace(1);
            FirstSpaceHost.Child = _firstSpace;
        }
        var two = await _store.GetSettingAsync(TwoSpacesKey) == "1";
        TwoSpacesToggle.IsChecked = two;
        SetSecondSpace(two);
        StageSubtitle.Text = _app.IsReadOnly || !_net.IsOnline ? "  ·  hors connexion : le brouillon est gardé, l'agent attendra la connexion" : "";
    }

    private CourseDraftPane NewSpace(int slot)
    {
        var space = new CourseDraftPane(slot, IsTeacher, App.Services.GetRequiredService<IAgentApi>(), _tutor,
            App.Services.GetRequiredService<ICourseApi>(), _data);
        space.Saved += async (_, course) => await OnCourseCreatedAsync(course);
        _ = space.SetTextStepAsync(Typography.Step(_textSize));
        return space;
    }

    private async void OnTwoSpacesToggle(object sender, RoutedEventArgs e)
    {
        var two = TwoSpacesToggle.IsChecked == true;
        await _store.SetSettingAsync(TwoSpacesKey, two ? "1" : "0");
        SetSecondSpace(two);
    }

    private void SetSecondSpace(bool open)
    {
        if (open && _secondSpace is null)
        {
            _secondSpace = NewSpace(2);
            SecondSpaceHost.Child = _secondSpace;
        }
        SecondSpaceColumn.Width = open ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SecondSpaceHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(TwoSpacesToggle, open ? "Revenir à un seul espace (le second brouillon est gardé)" : "Ouvrir un second espace pour écrire deux cours en même temps");
    }

    /// <summary>Un cours vient d'être enregistré depuis un espace : il rejoint la liste du menu.</summary>
    private async Task OnCourseCreatedAsync(CourseDetail course)
    {
        await RefreshCoursesAsync();
        if (_courses.FirstOrDefault(c => c.Id == course.Id) is { } summary)
            CourseList.SelectedItem = summary;   // il s'ouvrira au milieu dès le retour au cours
    }

    // ══════════════════════════ Tokens et contexte du tuteur ══════════════════════════

    /// <summary>
    /// Le rond à côté du modèle : la part de la fenêtre de contexte occupée par la discussion (dernier envoi au modèle
    /// et sa réponse). Le détail (dernière réponse, toute la discussion) s'ouvre au clic.
    /// </summary>
    private void UpdateUsage()
    {
        var (used, window, _) = ContextUse();
        ContextRing.Value = window is > 0 ? Math.Clamp(100.0 * used / window.Value, 2, 100) : used > 0 ? 2 : 0;
        ToolTipService.SetToolTip(UsageButton, used == 0
            ? "Tokens et contexte : pas encore de réponse comptée"
            : window is > 0 ? $"Contexte : {100.0 * used / window.Value:0} % de {Tokens(window.Value)} · détail au clic"
            : $"Contexte : {Tokens(used)} tokens · détail au clic");
    }

    /// <summary>Tokens occupés par la discussion (dernier envoi + réponse), taille du contexte du modèle, et la réponse en question.</summary>
    private (int Used, int? Window, TutorMessage? Last) ContextUse()
    {
        var last = _conversation?.Messages.LastOrDefault(m => !m.FromStudent && m.PromptTokens is not null);
        if (last is null) return (0, null, null);
        var used = (last.PromptTokens ?? 0) + (last.CompletionTokens ?? 0);
        int? window = last.Model is { } model && _models?.Context?.TryGetValue(model, out var size) == true ? size : null;
        return (used, window, last);
    }

    private void OnUsageOpening(object sender, object e)
    {
        UsagePanel.Children.Clear();
        var (used, window, last) = ContextUse();
        var answers = _conversation?.Messages.Where(m => !m.FromStudent).ToList() ?? [];
        var counted = answers.Where(m => m.PromptTokens is not null).ToList();

        // Contexte : barre et pourcentage
        UsageRow("Contexte", window is > 0 ? $"{Tokens(used)} / {Tokens(window.Value)}" : used > 0 ? $"{Tokens(used)} tokens" : "—");
        var bar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 2, 0, 2),
            Value = window is > 0 ? Math.Clamp(100.0 * used / window.Value, 0, 100) : 0
        };
        UsagePanel.Children.Add(bar);
        UsagePanel.Children.Add(Hint(window is > 0
            ? $"{100.0 * used / window.Value:0} % de la mémoire de travail de {ModelNames.Display(last?.Model)} : la discussion récente, l'extrait du cours et les consignes."
            : used > 0 ? "Taille du contexte inconnue pour ce modèle." : "Pas encore de réponse comptée sur ce cours."));

        UsagePanel.Children.Add(Separator());
        if (last is not null)
            UsageRow("Dernière réponse", $"↑ {Tokens(last.PromptTokens ?? 0)}   ↓ {Tokens(last.CompletionTokens ?? 0)}"
                + (last.ReasoningTokens is > 0 ? $"  ({Tokens(last.ReasoningTokens.Value)} réflexion)" : ""));
        UsageRow("Cette discussion", counted.Count == 0 ? "—"
            : $"↑ {Tokens(counted.Sum(m => m.PromptTokens ?? 0))}   ↓ {Tokens(counted.Sum(m => m.CompletionTokens ?? 0))}");
        UsageRow("Réponses", counted.Count == answers.Count ? $"{answers.Count}" : $"{answers.Count} ({answers.Count - counted.Count} sans compte)");
        UsagePanel.Children.Add(Separator());
        UsagePanel.Children.Add(Hint("↑ envoyés au modèle · ↓ écrits par le modèle. Effacer la discussion libère le contexte."));
    }

    private void UsageRow(string label, string value)
    {
        var row = new Grid { Padding = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        var v = new TextBlock { Text = value, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(v, 1);
        row.Children.Add(v);
        UsagePanel.Children.Add(row);
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4),
        Style = (Style)Application.Current.Resources["RowHint"]
    };

    private static Border Separator() => new()
    {
        Height = 1, Margin = new Thickness(0, 6, 0, 6),
        Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
    };

    /// <summary>1234 → « 1,2K », 4 900 000 → « 4,9M ».</summary>
    private static string Tokens(int n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000.0).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + "M",
        >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + "K",
        _ => n.ToString(CultureInfo.InvariantCulture)
    };

    // ══════════════════════════ Commandes « / » de la zone de saisie ══════════════════════════

    private sealed record Command(string Name, string Label, string Glyph, Func<Task> Run);

    private IEnumerable<Command> Commands()
    {
        yield return new("/quiz", "Quiz sur ce cours", "", () => ShowToolAsync("quiz"));
        yield return new("/exercices", "Exercices corrigés", "", () => ShowToolAsync("problems"));
        if (IsStudent || IsTeacher)
        {
            yield return new("/devoirs", IsTeacher ? "Mes évaluations" : "Devoirs", "", () => ShowToolAsync("assessments"));
            yield return new("/cours", "Créer un cours", "", () => ShowToolAsync("create"));
        }
        yield return new("/fiches", "Fiches épinglées", "", () => ShowToolAsync("pinned"));
        if (IsStudent)
        {
            yield return new("/plan", "Plan du jour", "", () => ShowToolAsync("plan"));
            yield return new("/progres", "Mes progrès", "", () => ShowToolAsync("progress"));
        }
        yield return new("/memoire", "Mémoire du tuteur", "", () => ShowToolAsync("memory"));
        yield return new("/effacer", "Effacer la discussion", "", () => { OnClearChat(this, new RoutedEventArgs()); return Task.CompletedTask; });
    }

    /// <summary>« /quiz », « /exo », « /progrès »… : un début de commande suffit s'il n'y en a qu'une qui commence ainsi.</summary>
    private bool TryRunCommand(string text)
    {
        if (!text.StartsWith('/')) return false;
        var typed = Plain(text.Split(' ', 2)[0]);
        var matches = Commands().Where(c => c.Name.StartsWith(typed, StringComparison.Ordinal)).ToList();
        var command = matches.FirstOrDefault(c => c.Name == typed) ?? (matches.Count == 1 ? matches[0] : null);
        if (command is null) return false;
        _ = command.Run();
        return true;
    }

    private static string Plain(string text) =>
        string.Concat(text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

    /// <summary>Taper « / » seul ouvre la liste des commandes, au-dessus de la zone de saisie.</summary>
    private void OnQuestionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuestionBox.Text != "/") return;
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft };
        foreach (var command in Commands())
        {
            var item = new MenuFlyoutItem
            {
                Text = command.Label,
                Icon = new FontIcon { Glyph = command.Glyph },
                KeyboardAcceleratorTextOverride = command.Name
            };
            item.Click += async (_, _) => { QuestionBox.Text = ""; await command.Run(); };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(QuestionBox);
    }

    // ── Info-bulles avec les touches (« Modèle de réponse  Ctrl  Maj  M ») ──

    private static void SetKeyTip(DependencyObject target, string label, params string[] keys)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, keys.Length > 0 ? 6 : 0, 0) });
        foreach (var key in keys)
            panel.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["KeyChip"],
                Child = new TextBlock { Text = key, FontSize = 10 }
            });
        ToolTipService.SetToolTip(target, new ToolTip { Content = panel });
    }
}
