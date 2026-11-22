using System.Text.Json;
using GPOE26.Desktop.Controls;
using GPOE26.Desktop.Services;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;

namespace GPOE26.Desktop;

/// <summary>
/// Fenêtre unique de l'application : connexion, puis l'espace d'étude en trois panneaux.
/// Menu à gauche, <b>lecture du cours toujours au milieu</b>, tuteur IA à droite (avec le choix du modèle).
/// Les panneaux latéraux se redimensionnent, se masquent, et la disposition est gardée d'une session à l'autre ;
/// la zone de lecture ne change jamais de place, elle prend toute la largeur libre.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string LayoutKey = "layout";
    private const string ModelKey = "tutor-model";
    private const string ThemeKey = "theme";
    private const string LastCourseKey = "last-course";

    private readonly AppSession _app = App.Services.GetRequiredService<AppSession>();
    private readonly OfflineData _data = App.Services.GetRequiredService<OfflineData>();
    private readonly Connectivity _net = App.Services.GetRequiredService<Connectivity>();
    private readonly LocalStore _store = App.Services.GetRequiredService<LocalStore>();
    private readonly IAccountApi _account = App.Services.GetRequiredService<IAccountApi>();
    private readonly ITutorApi _tutor = App.Services.GetRequiredService<ITutorApi>();
    private readonly DesktopTutorApi _desktopTutor = App.Services.GetRequiredService<DesktopTutorApi>();
    private readonly ServiceAddresses _addresses = App.Services.GetRequiredService<ServiceAddresses>();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _reconnect;

    private List<CourseSummary> _courses = [];
    private CourseSummary? _current;
    private TutorConversation? _conversation;
    private TutorModels? _models;
    private string? _model;               // null = Automatique
    private ImageUpload? _attachment;
    private CancellationTokenSource? _asking;
    private bool _syncing;
    private Layout _layout = Layout.Default;
    private string? _textSize;            // réglage « Taille du texte », repris par les espaces de création

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1440, 900));

        // Pas de barre de titre à part : menu, nom et connexion tiennent dans celle de Windows
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDrag);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        Root.SizeChanged += (_, _) => UpdateCaptionInset();

        MenuSplitter.Target = MenuColumn;
        MenuSplitter.MinPaneWidth = 200; MenuSplitter.MaxPaneWidth = 420; MenuSplitter.DefaultWidth = Layout.Default.MenuWidth;
        AgentSplitter.Target = AgentColumn;
        AgentSplitter.MinPaneWidth = 300; AgentSplitter.MaxPaneWidth = 720; AgentSplitter.DefaultWidth = Layout.Default.AgentWidth;
        MenuSplitter.Committed += async (_, _) => await SaveLayoutAsync();
        AgentSplitter.Committed += async (_, _) => await SaveLayoutAsync();

        _net.Changed += _ => DispatcherQueue.TryEnqueue(UpdateConnection);
        _app.Changed += () => DispatcherQueue.TryEnqueue(OnSessionChanged);

        // Hors connexion : on retente le serveur toutes les 30 secondes, et on resynchronise dès son retour
        _reconnect = DispatcherQueue.CreateTimer();
        _reconnect.Interval = TimeSpan.FromSeconds(30);
        _reconnect.Tick += async (_, _) => { if (!_net.IsOnline && !_app.IsReadOnly) await RefreshCoursesAsync(); };
        _net.Changed += online => DispatcherQueue.TryEnqueue(async () =>
        {
            if (online) { _reconnect.Stop(); await SyncAsync(); } else _reconnect.Start();
        });

        AddShortcut(VirtualKey.B, VirtualKeyModifiers.Control, () => SetMenuOpen(!_layout.MenuOpen));
        AddShortcut(VirtualKey.J, VirtualKeyModifiers.Control, () => SetAgentOpen(!_layout.AgentOpen));
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, ToggleFocus);
        AddShortcut(VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            if (_layout.AgentOpen && ModelButton.IsEnabled) ModelButton.Flyout.ShowAt(ModelButton);
        });
        ToolView.ActionRequested += async (_, action) => await OnToolActionAsync(action);
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () => { if (_tool is not null) _ = CloseStageAsync(); });

        Root.Loaded += async (_, _) => await StartAsync();
    }

    // ══════════════════════════ Démarrage et session ══════════════════════════

    private async Task StartAsync()
    {
        UpdateCaptionInset();
        await ApplyThemeAsync(await _store.GetSettingAsync(ThemeKey));
        await ApplyTextSizeAsync(await _store.GetSettingAsync(Typography.SettingKey), refresh: false);
        await _app.StartAsync();
        if (_app.IsSignedIn) await ShowWorkspaceAsync(); else await ShowLoginAsync();
    }

    private void OnSessionChanged()
    {
        // Jeton refusé par le serveur (401) pendant l'utilisation : retour à l'écran de connexion
        if (!_app.IsSignedIn && WorkspaceView.Visibility == Visibility.Visible)
            _ = ShowLoginAsync("Ta session a expiré : reconnecte-toi.");
    }

    private async Task ShowLoginAsync(string? message = null)
    {
        WorkspaceView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        MenuToggle.Visibility = ConnectionButton.Visibility = Visibility.Collapsed;
        SignInForm.Visibility = Visibility.Visible;
        FirstPasswordForm.Visibility = Visibility.Collapsed;
        ShowLoginError(message);

        var candidate = await _app.OfflineCandidateAsync();
        OfflineCard.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
        if (candidate is not null)
        {
            OfflineCardText.Text = "Relis tes cours et tes discussions déjà copiés sur ce poste. Rien n'est envoyé tant que tu n'es pas reconnecté.";
            OfflineButton.Content = $"Continuer hors connexion ({candidate.Username})";
        }
    }

    private void OnLoginModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var matricule = sender.SelectedItem == MatriculeMode;
        EmailFields.Visibility = matricule ? Visibility.Collapsed : Visibility.Visible;
        MatriculeFields.Visibility = matricule ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await LoginAsync(); }
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e) => await LoginAsync();

    private async Task LoginAsync()
    {
        ShowLoginError(null);
        LoginButton.IsEnabled = false;
        LoginProgress.IsActive = true;
        try
        {
            var result = LoginMode.SelectedItem == MatriculeMode
                ? await _account.LoginMatriculeAsync(SchoolBox.Text.Trim(), MatriculeBox.Text.Trim(), PasswordInput.Password)
                : await _account.LoginAsync(EmailBox.Text.Trim(), PasswordInput.Password);
            if (!result.Ok || result.Value is null)
            {
                ShowLoginError(Connectivity.IsNetworkFailure(result.Status)
                    ? "Le serveur est injoignable. Vérifie ta connexion, ou continue hors connexion ci-dessous si tu t'es déjà connecté sur ce poste."
                    : result.Error);
                return;
            }

            PasswordInput.Password = "";
            await _app.SignInAsync(result.Value);
            if (result.Value.Profile.MustChangePassword)
            {
                SignInForm.Visibility = Visibility.Collapsed;
                FirstPasswordForm.Visibility = Visibility.Visible;
                return;
            }
            await ShowWorkspaceAsync();
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginProgress.IsActive = false;
        }
    }

    private async void OnFirstPasswordClick(object sender, RoutedEventArgs e)
    {
        if (NewPassword.Password.Length < 8) { ShowLoginError("Le mot de passe doit faire 8 caractères au moins."); return; }
        if (NewPassword.Password != NewPasswordAgain.Password) { ShowLoginError("Les deux mots de passe ne sont pas identiques."); return; }

        var done = await _account.FirstPasswordAsync(NewPassword.Password);
        if (!done.Ok) { ShowLoginError(done.Error); return; }
        if ((await _account.MeAsync()).Value is { } profile) await _app.Online.UpdateProfileAsync(profile);
        NewPassword.Password = NewPasswordAgain.Password = "";
        await ShowWorkspaceAsync();
    }

    private async void OnOfflineClick(object sender, RoutedEventArgs e)
    {
        if (await _app.OpenOfflineAsync()) await ShowWorkspaceAsync();
    }

    private void ShowLoginError(string? message)
    {
        LoginError.Message = message ?? "";
        LoginError.IsOpen = message is not null;
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        _asking?.Cancel();
        await _app.SignOutAsync();
        _current = null;
        _conversation = null;
        await ShowLoginAsync();
    }

    // ══════════════════════════ Espace d'étude ══════════════════════════

    private async Task ShowWorkspaceAsync()
    {
        LoginView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Visible;
        MenuToggle.Visibility = ConnectionButton.Visibility = Visibility.Visible;

        var profile = _app.Profile!;
        Avatar.DisplayName = profile.Username;
        UserName.Text = profile.Username;
        UserDetail.Text = string.Join(" · ", new[] { RoleLabel(profile.Role), profile.Level, profile.OrganizationName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        await LoadLayoutAsync();
        _model = await _store.GetSettingAsync(ModelKey);
        UpdateConnection();
        BuildModelMenu();
        await CourseReader.ShowEmptyAsync("Choisis un cours dans le menu pour commencer à lire.");
        SetupTools();
        await ShowChatPlaceholderAsync();
        _ = RefreshNavAsync();

        await RefreshCoursesAsync();
        if (await _store.GetSettingAsync(LastCourseKey) is { } last && _courses.FirstOrDefault(c => c.Id.ToString() == last) is { } course)
            CourseList.SelectedItem = course;

        if (!_app.IsReadOnly)
        {
            _ = LoadModelsAsync();
            _ = SyncAsync();
        }
    }

    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Teacher => "Enseignant",
        UserRole.Parent => "Parent",
        _ => "Élève"
    };

    private async Task RefreshCoursesAsync()
    {
        var loaded = await _data.CoursesAsync();
        _courses = loaded.Value ?? [];
        FilterCourses();
        CourseListEmpty.Text = loaded.Error ?? "Aucun cours pour l'instant. Tes cours et ceux de tes classes apparaîtront ici.";
        CourseListEmpty.Visibility = _courses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateConnection();
    }

    private void OnCourseSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => FilterCourses();

    private void FilterCourses()
    {
        var q = CourseSearch.Text.Trim();
        var selected = _current?.Id;
        CourseList.ItemsSource = string.IsNullOrEmpty(q)
            ? _courses
            : _courses.Where(c => c.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                               || c.Subject.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (selected is not null)
            CourseList.SelectedItem = _courses.FirstOrDefault(c => c.Id == selected);
    }

    private async void OnCourseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CourseList.SelectedItem is CourseSummary course && course.Id != _current?.Id)
            await OpenCourseAsync(course);
    }

    private async Task OpenCourseAsync(CourseSummary course)
    {
        _asking?.Cancel();
        _current = course;
        _conversation = null;
        await _store.SetSettingAsync(LastCourseKey, course.Id.ToString());

        CourseMeta.Text = string.IsNullOrWhiteSpace(course.Subject) ? "" : $"{course.Subject} › ";
        CourseTitle.Text = course.Title;
        ReadingNotice.IsOpen = false;
        TocFlyout.Items.Clear();
        TocButton.IsEnabled = false;
        await CourseReader.ShowEmptyAsync("Chargement du cours…");

        var loadCourse = _data.CourseAsync(course.Id);
        var loadChat = LoadConversationAsync(course.Id);

        var loaded = await loadCourse;
        if (_current?.Id != course.Id) return;   // un autre cours a été choisi entre-temps
        if (loaded.Value is not { } detail)
        {
            await CourseReader.ShowEmptyAsync(loaded.Error ?? "Ce cours n'a pas pu être chargé.");
        }
        else
        {
            var (html, toc) = MarkdownRenderer.Render(detail.ToMarkdown());
            await CourseReader.ShowCourseAsync(detail.Title, detail.Subject, html, _addresses.Cours);
            foreach (var entry in toc)
            {
                var item = new MenuFlyoutItem { Text = entry.Title, Padding = new Thickness(entry.Level == 3 ? 28 : 12, 6, 12, 6) };
                item.Click += async (_, _) => await CourseReader.ScrollToAsync(entry.Id);
                TocFlyout.Items.Add(item);
            }
            TocButton.IsEnabled = toc.Count > 0;

            if (loaded.FromCache)
                Notice(ReadingNotice, InfoBarSeverity.Informational, "Copie locale", $"Hors connexion : version enregistrée le {Local(loaded.SavedAt)}.");
            else if (detail.StructuringStatus == "Pending")
                Notice(ReadingNotice, InfoBarSeverity.Informational, "L'agent met le cours en forme…", "Le PDF est en cours de mise en forme : rouvre le cours dans un moment pour la version structurée.");
        }
        await loadChat;
        AgentSubtitle.Text = $"  ·  {course.Title}";
        if (_tool is "quiz" or "problems") await ShowToolAsync(_tool);   // l'exercice suit le cours ouvert
        MarkDiscussion();
    }

    // ══════════════════════════ Tuteur IA ══════════════════════════

    private async Task LoadConversationAsync(Guid courseId)
    {
        AgentNotice.IsOpen = false;
        var loaded = await _data.ConversationAsync(courseId);
        if (_current?.Id != courseId) return;
        _conversation = loaded.Value ?? new TutorConversation(null, courseId, _current?.Title, []);
        await RenderConversationAsync();
        if (loaded.FromCache)
            Notice(AgentNotice, InfoBarSeverity.Informational, "Copie locale", $"Discussion enregistrée le {Local(loaded.SavedAt)}.");
        else if (loaded.Error is not null)
            Notice(AgentNotice, InfoBarSeverity.Warning, "", loaded.Error);
        UpdateComposer();
    }

    private async Task RenderConversationAsync(ChatLine? pending = null)
    {
        UpdateUsage();
        if (_conversation is null || (_conversation.Messages.Count == 0 && pending is null))
        {
            await ShowChatPlaceholderAsync();
            return;
        }
        var lines = _conversation.Messages.Select(ToLine).ToList();
        if (pending is not null) lines.Add(pending);
        await ChatView.SetMessagesAsync(lines);
    }

    private Task ShowChatPlaceholderAsync() => ChatView.ShowEmptyAsync(_current is null
        ? "Le tuteur t'aide sur le cours que tu lis. Choisis d'abord un cours."
        : "Pose ta question sur ce cours : le tuteur explique pas à pas, et peut vérifier la photo de ta copie.");

    private static ChatLine ToLine(TutorMessage m) => m.FromStudent
        ? new ChatLine("user", m.ImageId is null ? m.Content : $"📷 {m.Content}", null, m.CreatedAt.ToLocalTime().ToString("HH:mm"))
        : new ChatLine("assistant", null, MarkdownRenderer.Render(m.Content).Html,
            string.Join(" · ", new[] { m.Model is null ? null : $"Répondu par {ModelNames.Display(m.Model)}", m.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm") }.Where(s => s is not null)));

    private void OnQuestionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shift) return;   // Maj+Entrée : nouvelle ligne
        e.Handled = true;
        _ = SendAsync();
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (_asking is not null) { _asking.Cancel(); return; }   // le bouton sert aussi à arrêter
        await SendAsync();
    }

    private void OnNewQuestion(object sender, RoutedEventArgs e)
    {
        _ = OpenTutorAsync(null);
    }

    private async Task SendAsync()
    {
        if (_asking is not null || _current is not { } course || _app.IsReadOnly || !_net.IsOnline) return;
        var text = QuestionBox.Text.Trim();
        if (text.Length == 0 && _attachment is null) return;
        if (TryRunCommand(text)) { QuestionBox.Text = ""; return; }   // « /quiz », « /exercices »…

        _asking = new CancellationTokenSource();
        var attachment = _attachment;
        QuestionBox.Text = "";
        AgentNotice.IsOpen = false;
        SetBusy(true, attachment is null ? "Le tuteur réfléchit…" : "Envoi de la photo…");

        try
        {
            Guid? imageId = null;
            if (attachment is not null)
            {
                var upload = await _tutor.UploadImageAsync(attachment);
                if (!upload.Ok) { Fail(upload.Error, text); return; }
                imageId = upload.Value!.Id;
                SetAttachment(null);
                SetBusy(true, "Le tuteur lit ta copie…");
            }

            // Affichage immédiat de la question, puis de la réponse au fil de l'eau
            var question = new ChatLine("user", attachment is null ? text : $"📷 {(text.Length > 0 ? text : "Voici ma copie")}", null, null);
            if (_conversation is { Messages.Count: > 0 }) await RenderConversationAsync(question);
            else await ChatView.SetMessagesAsync(new[] { question });

            var answer = new System.Text.StringBuilder();
            var lastPaint = DateTime.MinValue;
            var result = await _desktopTutor.AskStreamAsync(course.Id, text.Length > 0 ? text : null, imageId, _model, async piece =>
            {
                answer.Append(piece);
                if (DateTime.UtcNow - lastPaint < TimeSpan.FromMilliseconds(120)) return;   // pas un rendu par mot
                lastPaint = DateTime.UtcNow;
                SetBusy(true, "Le tuteur écrit…");
                await ChatView.UpdateLastAsync(new ChatLine("assistant", null, MarkdownRenderer.Render(answer.ToString()).Html, null, Typing: true));
            }, _asking.Token);

            if (_current?.Id != course.Id) return;
            if (!result.Ok || result.Value is null)
            {
                await RenderConversationAsync();
                Fail(result.Error, text);
                if (Connectivity.IsNetworkFailure(result.Status)) _net.Report(false);
                return;
            }

            _net.Report(true);
            var conversation = _conversation ?? new TutorConversation(null, course.Id, course.Title, []);
            _conversation = conversation with { Messages = [.. conversation.Messages, result.Value.Question, result.Value.Answer] };
            await _data.SaveConversationAsync(_conversation);
            await RenderConversationAsync();
            _ = RefreshNavAsync();   // la discussion remonte dans le menu
        }
        finally
        {
            _asking?.Dispose();
            _asking = null;
            SetBusy(false, null);
        }
    }

    private void Fail(string? error, string question)
    {
        if (QuestionBox.Text.Length == 0) QuestionBox.Text = question;   // la question n'est pas perdue
        Notice(AgentNotice, InfoBarSeverity.Error, "", error ?? "Le tuteur n'a pas pu répondre. Réessaie dans un instant.");
    }

    private void SetBusy(bool busy, string? status)
    {
        AgentStatus.Text = status ?? "";
        AgentBusyDot.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ContextRing.IsIndeterminate = busy;   // le rond du contexte tourne pendant la réponse
        SendIcon.Glyph = busy ? "\uE71A" : "\uE74A";   // carré « arrêter » pendant la réponse, flèche sinon
        AutomationProperties.SetName(SendButton, busy ? "Arrêter" : "Envoyer");
        if (busy) SetKeyTip(SendButton, "Arrêter la réponse");
        else SetKeyTip(SendButton, "Envoyer", "Entrée");
        ModelButton.IsEnabled = AttachButton.IsEnabled = ClearChatButton.IsEnabled = !busy;
    }

    /// <summary>Saisie possible seulement en ligne, connecté, avec un cours ouvert.</summary>
    private void UpdateComposer()
    {
        var canAsk = _current is not null && !_app.IsReadOnly && _net.IsOnline;
        QuestionBox.IsEnabled = SendButton.IsEnabled = AttachButton.IsEnabled = ModelButton.IsEnabled = ClearChatButton.IsEnabled = canAsk;
        if (_current is not null && !canAsk)
            Notice(AgentNotice, InfoBarSeverity.Informational, "Hors connexion",
                "Relis ton cours et la discussion ; tu pourras de nouveau poser des questions dès le retour de la connexion.");
    }

    private async void OnClearChat(object sender, RoutedEventArgs e)
    {
        if (_current is not { } course) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Effacer la discussion ?",
            Content = $"Tous les échanges avec le tuteur sur « {course.Title} » seront supprimés, ici et sur le site.",
            PrimaryButtonText = "Effacer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var done = await _tutor.ClearAsync(course.Id);
        if (!done.Ok) { Notice(AgentNotice, InfoBarSeverity.Error, "", done.Error ?? ""); return; }
        _conversation = new TutorConversation(null, course.Id, course.Title, []);
        await _data.SaveConversationAsync(_conversation);
        await RenderConversationAsync();
    }

    // ── Photo de la copie ──

    private async void OnAttachPhoto(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFileAsync() is not { } file) return;

        var props = await file.GetBasicPropertiesAsync();
        if (props.Size > 6 * 1024 * 1024)
        {
            Notice(AgentNotice, InfoBarSeverity.Warning, "", "La photo doit faire moins de 6 Mo.");
            return;
        }
        var bytes = await File.ReadAllBytesAsync(file.Path);
        var type = Path.GetExtension(file.Name).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        SetAttachment(new ImageUpload(file.Name, type, bytes));
    }

    private void OnRemoveAttachment(object sender, RoutedEventArgs e) => SetAttachment(null);

    private void SetAttachment(ImageUpload? image)
    {
        _attachment = image;
        AttachmentName.Text = image?.FileName ?? "";
        AttachmentChip.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        BuildModelMenu();   // avec une photo, seuls les modèles qui lisent les images comptent
    }

    // ── Choix du modèle ──

    private async Task LoadModelsAsync()
    {
        var result = await _desktopTutor.ModelsAsync();
        if (result.Ok) _models = result.Value;
        BuildModelMenu();
    }

    /// <summary>
    /// Zone de choix du modèle : « Automatique » (le serveur choisit et passe au suivant si un modèle est saturé),
    /// ou un modèle précis de la liste du serveur, essayé en premier. Avec une photo jointe, les modèles
    /// qui ne lisent pas les images sont grisés.
    /// </summary>
    private void BuildModelMenu()
    {
        ModelFlyout.Items.Clear();
        ModelFlyout.Items.Add(new MenuFlyoutItem { Text = "Modèle de réponse", IsEnabled = false });

        var auto = new RadioMenuFlyoutItem { Text = "Automatique (recommandé)", GroupName = "model", IsChecked = _model is null };
        ToolTipService.SetToolTip(auto, "Le serveur choisit le modèle et bascule sur le suivant si l'un est saturé.");
        auto.Click += async (_, _) => await ChooseModelAsync(null);
        ModelFlyout.Items.Add(auto);

        if (_models is { } models)
        {
            ModelFlyout.Items.Add(new MenuFlyoutSeparator());
            var vision = models.Vision.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in models.Text.Concat(models.Vision).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var readsPhotos = vision.Contains(id);
                var item = new RadioMenuFlyoutItem
                {
                    Text = ModelNames.Display(id) + (ModelNames.IsFree(id) ? "  · gratuit" : "") + (readsPhotos ? "  · lit les photos" : ""),
                    GroupName = "model",
                    IsChecked = string.Equals(_model, id, StringComparison.OrdinalIgnoreCase),
                    IsEnabled = _attachment is null || readsPhotos
                };
                ToolTipService.SetToolTip(item, id);
                item.Click += async (_, _) => await ChooseModelAsync(id);
                ModelFlyout.Items.Add(item);
            }
        }
        else if (!_app.IsReadOnly)
        {
            ModelFlyout.Items.Add(new MenuFlyoutItem { Text = "Liste des modèles indisponible pour le moment", IsEnabled = false });
        }

        // Un modèle choisi qui n'est plus proposé par le serveur : retour à Automatique
        if (_model is not null && _models is not null && !_models.Text.Concat(_models.Vision).Contains(_model, StringComparer.OrdinalIgnoreCase))
            _model = null;
        ModelLabel.Text = ModelNames.Display(_model);
    }

    private async Task ChooseModelAsync(string? id)
    {
        _model = id;
        ModelLabel.Text = ModelNames.Display(id);
        await _store.SetSettingAsync(ModelKey, id);
    }

    // ══════════════════════════ Connexion et copie locale ══════════════════════════

    private async Task SyncAsync()
    {
        if (_syncing || _app.IsReadOnly) return;
        _syncing = true;
        SyncButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(text => SyncDetails.Text = text);
            var count = await _data.SyncAsync(progress);
            if (count > 0) await RefreshCoursesAsync();
            await RefreshNavAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _net.Report(false);
        }
        finally
        {
            _syncing = false;
            SyncButton.IsEnabled = true;
            await UpdateSyncDetailsAsync();
        }
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e) => await SyncAsync();

    private void UpdateConnection()
    {
        var online = _net.IsOnline && !_app.IsReadOnly;
        ConnectionDot.Fill = new SolidColorBrush(online ? Microsoft.UI.Colors.SeaGreen : Microsoft.UI.Colors.DarkOrange);
        var state = _app.IsReadOnly ? "Hors connexion (lecture seule)" : online ? "En ligne" : "Hors connexion";
        ConnectionTitle.Text = state;
        ConnectionText.Text = online ? "" : state;   // en ligne, le point vert suffit
        ConnectionText.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(ConnectionButton, $"{state} · copie locale");
        SyncButton.Visibility = _app.IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        UpdateComposer();
        _ = UpdateSyncDetailsAsync();
    }

    private async Task UpdateSyncDetailsAsync()
    {
        if (_syncing) return;
        var last = await _data.LastSyncAsync();
        var copy = last is null ? "Aucune copie complète pour l'instant." : $"Dernière copie complète : {Local(last)}.";
        SyncDetails.Text = _app.IsReadOnly
            ? $"{copy} Reconnecte-toi pour mettre tes cours à jour et poser des questions au tuteur."
            : $"{copy} Tes cours et tes discussions sont copiés sur ce poste pour être relus hors connexion.";
    }

    // ══════════════════════════ Disposition des panneaux ══════════════════════════

    /// <summary>Largeurs et panneaux ouverts, gardés pour toutes les sessions (comme gpoe26.workspace sur le site).</summary>
    private sealed record Layout(double MenuWidth, double AgentWidth, bool MenuOpen, bool AgentOpen)
    {
        public static readonly Layout Default = new(248, 380, true, true);
    }

    private async Task LoadLayoutAsync()
    {
        try
        {
            _layout = await _store.GetSettingAsync(LayoutKey) is { } json
                ? JsonSerializer.Deserialize<Layout>(json, LocalStore.Json) ?? Layout.Default
                : Layout.Default;
        }
        catch (JsonException) { _layout = Layout.Default; }
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        MenuColumn.Width = new GridLength(_layout.MenuOpen ? Math.Clamp(_layout.MenuWidth, 200, 420) : 0);
        AgentColumn.Width = new GridLength(_layout.AgentOpen ? Math.Clamp(_layout.AgentWidth, 300, 720) : 0);
        MenuPane.Visibility = MenuSplitter.Visibility = _layout.MenuOpen ? Visibility.Visible : Visibility.Collapsed;
        AgentPane.Visibility = AgentSplitter.Visibility = _layout.AgentOpen ? Visibility.Visible : Visibility.Collapsed;
        ReadingPane.Margin = new Thickness(_layout.MenuOpen ? 0 : 6, 0, _layout.AgentOpen ? 0 : 6, 6);
        FocusIcon.Glyph = !_layout.MenuOpen && !_layout.AgentOpen ? "\uE73F" : "\uE740";   // quitter / entrer en mode lecture
        ApplyStage();   // un outil ouvert garde sa place (plein écran, ou au milieu)
    }

    private async Task SaveLayoutAsync()
    {
        _layout = _layout with
        {
            MenuWidth = _layout.MenuOpen ? MenuColumn.ActualWidth : _layout.MenuWidth,
            AgentWidth = _layout.AgentOpen ? AgentColumn.ActualWidth : _layout.AgentWidth
        };
        await _store.SetSettingAsync(LayoutKey, JsonSerializer.Serialize(_layout, LocalStore.Json));
    }

    private async void SetMenuOpen(bool open)
    {
        await SaveLayoutAsync();
        _layout = _layout with { MenuOpen = open };
        ApplyLayout();
        await SaveLayoutAsync();
    }

    private async void SetAgentOpen(bool open)
    {
        await SaveLayoutAsync();
        _layout = _layout with { AgentOpen = open };
        ApplyLayout();
        await SaveLayoutAsync();
    }

    /// <summary>Mode lecture : le cours seul ; un second appui rouvre les deux panneaux.</summary>
    private async void ToggleFocus()
    {
        await SaveLayoutAsync();
        var focused = !_layout.MenuOpen && !_layout.AgentOpen;
        _layout = _layout with { MenuOpen = focused, AgentOpen = focused };
        ApplyLayout();
        await SaveLayoutAsync();
    }

    private void OnMenuToggle(object sender, RoutedEventArgs e) => SetMenuOpen(!_layout.MenuOpen);
    private void OnAgentToggle(object sender, RoutedEventArgs e) => SetAgentOpen(!_layout.AgentOpen);
    private void OnCloseAgent(object sender, RoutedEventArgs e) => SetAgentOpen(false);
    private void OnFocusToggle(object sender, RoutedEventArgs e) => ToggleFocus();

    private async void OnResetLayout(object sender, RoutedEventArgs e)
    {
        _layout = Layout.Default;
        ApplyLayout();
        await SaveLayoutAsync();
    }

    // ══════════════════════════ Thème et utilitaires ══════════════════════════

    private async void OnThemeClick(object sender, RoutedEventArgs e)
    {
        var theme = (string)((FrameworkElement)sender).Tag;
        await _store.SetSettingAsync(ThemeKey, theme);
        await ApplyThemeAsync(theme);
    }

    private Task ApplyThemeAsync(string? theme)
    {
        Root.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        ThemeSystem.IsChecked = theme is not ("light" or "dark");
        ThemeLight.IsChecked = theme == "light";
        ThemeDark.IsChecked = theme == "dark";
        UpdateCaptionButtons();
        return Task.CompletedTask;
    }

    // ── Taille du texte : petite par défaut, un ou deux points de plus pour qui a du mal à lire petit ──

    private async void OnTextSizeClick(object sender, RoutedEventArgs e)
    {
        var value = (string)((FrameworkElement)sender).Tag;
        await _store.SetSettingAsync(Typography.SettingKey, value);
        await ApplyTextSizeAsync(value, refresh: true);
    }

    private async Task ApplyTextSizeAsync(string? value, bool refresh)
    {
        _textSize = value;
        var step = Typography.Step(value);
        if (TextSizeMenu.Items.Count == 0)
            foreach (var (choice, label) in Typography.Choices)
            {
                var item = new RadioMenuFlyoutItem { Text = label, Tag = choice, GroupName = "text-size" };
                item.Click += OnTextSizeClick;
                TextSizeMenu.Items.Add(item);
            }
        foreach (var item in TextSizeMenu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = Typography.Step((string)item.Tag) == step;

        Shell.FontSize = Typography.Body(step);
        if (refresh)
        {
            // Les contrôles lisent leurs tailles en ressources de thème : un aller-retour de thème les fait relire
            Typography.Apply(Application.Current.Resources, step);
            var requested = Root.RequestedTheme;
            Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            Root.RequestedTheme = requested;
        }
        await Task.WhenAll(CourseReader.SetTextStepAsync(step), ChatView.SetTextStepAsync(step), ToolView.SetTextStepAsync(step),
            _firstSpace?.SetTextStepAsync(step) ?? Task.CompletedTask, _secondSpace?.SetTextStepAsync(step) ?? Task.CompletedTask);
    }

    // ── Barre de titre de Windows : boutons Réduire/Agrandir/Fermer aux couleurs du thème ──

    private void UpdateCaptionButtons()
    {
        var bar = AppWindow.TitleBar;
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = dark ? global::Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80) : global::Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);
        bar.ButtonHoverBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : global::Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : global::Windows.UI.Color.FromArgb(0x1C, 0, 0, 0);
    }

    /// <summary>Place réservée à droite pour les boutons de Windows (la largeur dépend de l'échelle d'affichage).</summary>
    private void UpdateCaptionInset()
    {
        if (Root.XamlRoot is not { } xamlRoot) return;
        CaptionColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / xamlRoot.RasterizationScale);
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => { e.Handled = true; action(); };
        Root.KeyboardAccelerators.Add(accelerator);
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;   // pas de bulle « Ctrl+B » au survol de la fenêtre
    }

    private static void Notice(InfoBar bar, InfoBarSeverity severity, string title, string message)
    {
        bar.Severity = severity;
        bar.Title = title;
        bar.Message = message;
        bar.IsOpen = true;
    }

    private static string Local(DateTime? utc) =>
        utc?.ToLocalTime().ToString("dd MMM 'à' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "—";
}
