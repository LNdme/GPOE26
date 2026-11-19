using GPOE26.Desktop.Services;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GPOE26.Desktop.Controls;

/// <summary>Brouillon d'un espace de création, gardé sur le poste (on le retrouve après avoir fermé l'application).</summary>
public sealed record CourseDraft(string Title, string Subject, string Instructions, string Markdown);

/// <summary>
/// Un espace de création de cours : titre et matière, l'aperçu du cours tel que les élèves le liront,
/// et une zone de saisie pour guider l'agent (« Proposer un plan », « Rédiger la suite »).
/// Deux espaces peuvent travailler en même temps : chacun a son agent, ses appels et son brouillon.
/// </summary>
public sealed class CourseDraftPane : UserControl
{
    private readonly int _slot;
    private readonly bool _teacher;
    private readonly IAgentApi _agent;
    private readonly ITutorApi _tutor;
    private readonly ICourseApi _courses;
    private readonly OfflineData _data;

    private readonly TextBox _title = new() { PlaceholderText = "Titre du cours" };
    private readonly TextBox _subject = new() { PlaceholderText = "Matière" };
    private readonly ToggleButton _source = new() { Padding = new Thickness(7, 5, 7, 5) };
    private readonly Button _clear = new();
    private readonly InfoBar _notice = new() { IsClosable = true, Margin = new Thickness(12, 0, 12, 6) };
    private readonly ReaderView _preview = new();
    private readonly TextBox _markdown = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
        Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Stretch
    };
    private readonly TextBox _instructions = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40, MaxHeight = 140,
        BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(2)
    };
    private readonly Button _plan = new();
    private readonly Button _write = new();
    private readonly Button _save = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressRing _ring = new() { Width = 16, Height = 16, IsActive = false };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _keep;
    private bool _busy;

    /// <summary>Le cours vient d'être enregistré : l'application l'ajoute à la liste et peut l'ouvrir.</summary>
    public event EventHandler<CourseDetail>? Saved;

    public CourseDraftPane(int slot, bool teacher, IAgentApi agent, ITutorApi tutor, ICourseApi courses, OfflineData data)
    {
        (_slot, _teacher, _agent, _tutor, _courses, _data) = (slot, teacher, agent, tutor, courses, data);
        var app = Application.Current.Resources;
        _markdown.FontFamily = (FontFamily)app["AppFont"];
        _instructions.PlaceholderText = teacher
            ? "Dis à l'agent ce que tu veux : niveau, chapitres, exemples, exercices…"
            : "Précise ce que tu veux : niveau, chapitres, exemples…";
        foreach (var (key, color) in new[] { ("TextControlBackgroundPointerOver", 0), ("TextControlBackgroundFocused", 0), ("TextControlBorderBrushFocused", 0), ("TextControlBorderBrushPointerOver", 0) })
            _instructions.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        _keep = DispatcherQueue.CreateTimer();
        _keep.Interval = TimeSpan.FromSeconds(1);
        _keep.IsRepeating = false;
        _keep.Tick += async (_, _) => await KeepAsync();

        Content = Build(app);
        Loaded += async (_, _) => await RestoreAsync();
    }

    private string Key => $"course-draft:{_slot}";
    private string Markdown => _markdown.Text.Trim();

    private Grid Build(ResourceDictionary app)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Titre · matière · texte source · vider
        var head = new Grid { Padding = new Thickness(12, 10, 12, 8), ColumnSpacing = 6 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _source.Content = new FontIcon { Glyph = "", FontSize = 12 };
        ToolTipService.SetToolTip(_source, "Modifier le texte du cours (Markdown)");
        _source.Click += async (_, _) => await ToggleSourceAsync();
        _clear.Style = (Style)app["IconButton"];
        _clear.Content = new FontIcon { Glyph = "", FontSize = 12 };
        ToolTipService.SetToolTip(_clear, "Vider cet espace");
        _clear.Click += async (_, _) => await ClearAsync();
        Grid.SetColumn(_subject, 1); Grid.SetColumn(_source, 2); Grid.SetColumn(_clear, 3);
        head.Children.Add(_title); head.Children.Add(_subject); head.Children.Add(_source); head.Children.Add(_clear);
        _title.TextChanged += (_, _) => _keep.Start();
        _subject.TextChanged += (_, _) => _keep.Start();
        _markdown.TextChanged += (_, _) => _keep.Start();
        _instructions.TextChanged += (_, _) => _keep.Start();

        Grid.SetRow(_notice, 1);
        Grid.SetRow(_preview, 2);
        Grid.SetRow(_markdown, 2);

        // Zone de saisie de l'agent, comme celle du tuteur
        var composer = new Border
        {
            Margin = new Thickness(10, 6, 10, 10), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 8, 8),
            BorderThickness = new Thickness(1),
            Background = (Brush)app["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)app["ControlStrokeColorDefaultBrush"]
        };
        var inner = new Grid { RowSpacing = 6 };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.Children.Add(_instructions);

        var actions = new Grid { ColumnSpacing = 4 };
        for (var i = 0; i < 5; i++)
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Chip(_plan, "", _teacher ? "Proposer un plan" : "Rédiger le cours");
        Chip(_write, "", "Rédiger la suite");
        _write.Visibility = _teacher ? Visibility.Visible : Visibility.Collapsed;   // la suite passe par l'assistant enseignant
        _plan.Click += async (_, _) => await ComposeAsync("plan");
        _write.Click += async (_, _) => await ComposeAsync("write");
        _status.Style = (Style)app["RowHint"];
        _save.Style = (Style)app["AccentButtonStyle"];
        _save.Padding = new Thickness(10, 4, 10, 5);
        _save.Content = "Enregistrer le cours";
        _save.Click += async (_, _) => await SaveAsync();
        Grid.SetColumn(_write, 1); Grid.SetColumn(_status, 2); Grid.SetColumn(_ring, 3); Grid.SetColumn(_save, 4);
        actions.Children.Add(_plan); actions.Children.Add(_write); actions.Children.Add(_status); actions.Children.Add(_ring); actions.Children.Add(_save);
        Grid.SetRow(actions, 1);
        inner.Children.Add(actions);
        composer.Child = inner;
        Grid.SetRow(composer, 3);

        root.Children.Add(head); root.Children.Add(_notice); root.Children.Add(_preview); root.Children.Add(_markdown); root.Children.Add(composer);
        return root;
    }

    private static void Chip(Button button, string glyph, string label)
    {
        button.Padding = new Thickness(8, 4, 10, 5);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = label });
        button.Content = panel;
    }

    // ─── Brouillon gardé sur le poste ───

    private async Task RestoreAsync()
    {
        if (await _data.KeptAsync<CourseDraft>(Key) is { } kept)
        {
            _title.Text = kept.Value.Title;
            _subject.Text = kept.Value.Subject;
            _instructions.Text = kept.Value.Instructions;
            _markdown.Text = kept.Value.Markdown;
        }
        await RefreshPreviewAsync();
    }

    private Task KeepAsync() =>
        _data.KeepAsync(Key, new CourseDraft(_title.Text, _subject.Text, _instructions.Text, _markdown.Text));

    private async Task RefreshPreviewAsync()
    {
        if (Markdown.Length == 0)
            await _preview.ShowEmptyAsync(_teacher
                ? "Le cours apparaîtra ici, tel que les élèves le liront. Donne un titre et une matière, puis demande un plan à l'agent."
                : "Le cours apparaîtra ici. Donne un titre et une matière, puis demande à l'agent de le rédiger.");
        else
            await _preview.ShowCourseAsync(_title.Text.Trim(), _subject.Text.Trim(), MarkdownRenderer.Render(Markdown).Html, null);
    }

    private async Task ToggleSourceAsync()
    {
        var source = _source.IsChecked == true;
        _markdown.Visibility = source ? Visibility.Visible : Visibility.Collapsed;
        _preview.Visibility = source ? Visibility.Collapsed : Visibility.Visible;
        if (!source) await RefreshPreviewAsync();
    }

    public Task SetTextStepAsync(int step) => _preview.SetTextStepAsync(step);

    // ─── Agent ───

    private async Task ComposeAsync(string mode)
    {
        if (_busy) return;
        var title = _title.Text.Trim();
        var subject = _subject.Text.Trim();
        if (title.Length == 0 || subject.Length == 0)
        {
            Show(InfoBarSeverity.Warning, "Indique d'abord le titre et la matière du cours.");
            return;
        }

        _notice.IsOpen = false;
        SetBusy(true, mode == "plan" ? (_teacher ? "L'agent prépare le plan…" : "L'agent rédige le cours…") : "L'agent rédige la suite…");
        string? markdown, error;
        var instructions = _instructions.Text.Trim();
        try
        {
            if (_teacher)
            {
                // Le cours déjà écrit accompagne la demande : « Rédiger la suite » continue au lieu de recommencer
                var result = await _agent.ComposeAsync(new ComposeRequest(mode, subject, title,
                    instructions.Length > 0 ? instructions : null, null, Markdown.Length > 0 ? Markdown : null));
                (markdown, error) = (result.Value?.Markdown, result.Error);
            }
            else
            {
                var result = await _tutor.DraftCourseAsync(subject,
                    $"Le nom du cours est précisément : {title}." + (instructions.Length > 0 ? $" {instructions}" : ""));
                (markdown, error) = (result.Value, result.Error);
            }
        }
        finally { SetBusy(false, null); }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            Show(InfoBarSeverity.Error, error ?? "L'agent n'a rien rédigé. Réessaie dans un instant.");
            return;
        }
        _markdown.Text = Markdown.Length == 0 ? markdown.Trim() : $"{Markdown}\n\n{markdown.Trim()}";
        _instructions.Text = "";
        await RefreshPreviewAsync();
        await KeepAsync();
        _status.Text = "Ajouté au cours. Relis, modifie le texte, ou demande la suite.";
    }

    private async Task SaveAsync()
    {
        if (_busy) return;
        var title = _title.Text.Trim();
        var subject = _subject.Text.Trim();
        if (title.Length == 0 || subject.Length == 0 || Markdown.Length == 0)
        {
            Show(InfoBarSeverity.Warning, "Il faut un titre, une matière et un contenu avant d'enregistrer le cours.");
            return;
        }

        SetBusy(true, "Enregistrement…");
        try
        {
            var created = await _courses.CreateAsync(new CourseInput(title, subject, null,
                [new CourseSectionInput(CourseSectionType.Paragraph, Markdown, 0)]));
            if (created.Value is null)
            {
                Show(InfoBarSeverity.Error, created.Error ?? "Le cours n'a pas pu être enregistré.");
                return;
            }
            Reset();
            await KeepAsync();
            await RefreshPreviewAsync();
            Show(InfoBarSeverity.Success, $"« {created.Value.Title} » est enregistré dans tes cours. Cet espace est prêt pour un autre cours.");
            Saved?.Invoke(this, created.Value);
        }
        finally { SetBusy(false, null); }
    }

    private async Task ClearAsync()
    {
        if (_busy) return;
        if (Markdown.Length > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Vider cet espace ?",
                Content = "Le brouillon de ce cours sera effacé de ce poste (rien n'a été enregistré dans tes cours).",
                PrimaryButtonText = "Vider",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        Reset();
        await KeepAsync();
        await RefreshPreviewAsync();
    }

    private void Reset()
    {
        _title.Text = _subject.Text = _instructions.Text = _markdown.Text = "";
        _status.Text = "";
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        _ring.IsActive = busy;
        _status.Text = status ?? "";
        _plan.IsEnabled = _write.IsEnabled = _save.IsEnabled = _clear.IsEnabled = !busy;
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        _notice.Severity = severity;
        _notice.Message = message;
        _notice.IsOpen = true;
    }
}
