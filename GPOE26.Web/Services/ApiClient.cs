using System.Net.Http.Json;
using System.Net.Http.Headers;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services
{
    public class ApiClient
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<ApiClient> _logger;
        private readonly AuthTokenProvider _tokenProvider;

        public ApiClient(IHttpClientFactory httpClientFactory, ILogger<ApiClient> logger, AuthTokenProvider tokenProvider)
        {
            _factory = httpClientFactory;
            _logger = logger;
            _tokenProvider = tokenProvider;
        }

        /// <summary>
        /// Creates an HttpClient with the JWT Bearer token injected (for protected APIs).
        /// </summary>
        private HttpClient CreateAuthClient(string name)
        {
            var client = _factory.CreateClient(name);
            var token = _tokenProvider.Token;
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }


        #region Contact API (GPO26ApiService)

        public async Task<Contact?> GetFooterContactAsync()
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<Contact?>("/api/contact");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching footer contact");
            }
            return null;
        }

        public async Task<List<Contact>> GetAllContactsAsync()
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var contact = await client.GetFromJsonAsync<Contact?>("/api/contact");
                return contact is not null ? new List<Contact> { contact } : new List<Contact>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching contacts");
                return new List<Contact>();
            }
        }

        public async Task<Contact?> GetPrimaryContactAsync()
        {
            var all = await GetAllContactsAsync();
            return all.FirstOrDefault();
        }

        public async Task<Contact?> GetContactByIdAsync(int id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<Contact>($"/api/contact/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching contact {id}");
                return null;
            }
        }

        public async Task<bool> CreateContactAsync(Contact contact)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var response = await client.PostAsJsonAsync("/api/contact", contact);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating contact");
                return false;
            }
        }


        // Pareil pour les autres méthodes Update:

        public async Task<bool> UpdateContactAsync(int id, Contact contact)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                // ✅ Avec l'ID dans l'URL
                var response = await client.PutAsJsonAsync($"/api/contact/{id}", contact);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating contact {id}");
                return false;
            }
        }

        

        public async Task<bool> DeleteContactAsync(int id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var response = await client.DeleteAsync($"/api/contact/{id}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting contact {id}");
                return false;
            }
        }

        #endregion


        #region News API (GPO26ApiService)

        public async Task<(int total, List<NewArticle> items)> GetNewsAsync(string? category = null, bool? published = null, int page = 1, int pageSize = 10)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var url = "/api/news";
                var qs = new List<string>();
                if (!string.IsNullOrEmpty(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
                if (published.HasValue) qs.Add($"published={published.Value.ToString().ToLowerInvariant()}");
                if (page != 1) qs.Add($"page={page}");
                if (pageSize != 10) qs.Add($"pageSize={pageSize}");
                if (qs.Any()) url += "?" + string.Join("&", qs);

                var resp = await client.GetFromJsonAsync<NewsListResponse?>(url);
                return resp is null ? (0, new List<NewArticle>()) : (resp.total, resp.items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching news");
                return (0, new List<NewArticle>());
            }
        }

        public async Task<NewArticle?> GetNewsByIdAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<NewArticle>($"/api/news/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching news {id}");
                return null;
            }
        }

        public async Task<bool> CreateNewsAsync(NewArticle article)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PostAsJsonAsync("/api/news", article);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating news");
                return false;
            }
        }

        public async Task<bool> UpdateNewsAsync(Guid id, NewArticle updated)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PutAsJsonAsync($"/api/news/{id}", updated);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating news {id}");
                return false;
            }
        }

        public async Task<bool> DeleteNewsAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.DeleteAsync($"/api/news/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting news {id}");
                return false;
            }
        }

        private record NewsListResponse(int total, int page, int pageSize, List<NewArticle> items);

        #endregion


        #region EVents API (GPO26ApiService)

        public async Task<List<SchoolEvent>> GetUpcomingEventsAsync(int limit = 5)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.GetFromJsonAsync<List<SchoolEvent>>($"/api/events/upcoming?limit={limit}");
                return resp ?? new List<SchoolEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching upcoming events");
                return new List<SchoolEvent>();
            }
        }

        public async Task<List<SchoolEvent>> GetAllEventsAsync(string? type = null)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var url = "/api/events" + (string.IsNullOrEmpty(type) ? "" : $"?type={Uri.EscapeDataString(type)}");
                var resp = await client.GetFromJsonAsync<List<SchoolEvent>>(url);
                return resp ?? new List<SchoolEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching events");
                return new List<SchoolEvent>();
            }
        }

        public async Task<SchoolEvent?> GetEventByIdAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<SchoolEvent>($"/api/events/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching event {id}");
                return null;
            }
        }

        public async Task<bool> CreateEventAsync(SchoolEvent ev)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PostAsJsonAsync("/api/events", ev);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event");
                return false;
            }
        }

        public async Task<bool> UpdateEventAsync(Guid id, SchoolEvent ev)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PutAsJsonAsync($"/api/events/{id}", ev);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating event {id}");
                return false;
            }
        }

        public async Task<bool> DeleteEventAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.DeleteAsync($"/api/events/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting event {id}");
                return false;
            }
        }

        #endregion



        #region SPEECH API (GPO26ApiService)

        public async Task<List<Speech>> GetAllSpeechesAsync()
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.GetFromJsonAsync<List<Speech>>("/api/speeches");
                return resp ?? new List<Speech>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching speeches");
                return new List<Speech>();
            }
        }

        #endregion


        #region ACTIVITIES API (GPO26ApiService)

        public async Task<List<SchoolActivity>> GetActiveActivitiesAsync(string? category = null)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var url = "/api/activities" + (string.IsNullOrEmpty(category) ? "" : $"?category={Uri.EscapeDataString(category)}");
                var resp = await client.GetFromJsonAsync<List<SchoolActivity>>(url);
                return resp ?? new List<SchoolActivity>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching activities");
                return new List<SchoolActivity>();
            }
        }

        public async Task<SchoolActivity?> GetActivityByIdAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<SchoolActivity>($"/api/activities/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching activity {id}");
                return null;
            }
        }

        public async Task<bool> CreateActivityAsync(SchoolActivity activity)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PostAsJsonAsync("/api/activities", activity);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating activity");
                return false;
            }
        }

        public async Task<bool> UpdateActivityAsync(Guid id, SchoolActivity activity)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PutAsJsonAsync($"/api/activities/{id}", activity);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating activity {id}");
                return false;
            }
        }

        public async Task<bool> DeleteActivityAsync(Guid id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.DeleteAsync($"/api/activities/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting activity {id}");
                return false;
            }
        }

        #endregion

        #region HIERARCHY API (GPO26ApiService)

        public async Task<List<Hierarchy>> GetHierarchyAsync()
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.GetFromJsonAsync<List<Hierarchy>>("/api/hierarchy");
                return resp ?? new List<Hierarchy>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching hierarchy");
                return new List<Hierarchy>();
            }
        }

        public async Task<Hierarchy?> GetHierarchyByIdAsync(int id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                return await client.GetFromJsonAsync<Hierarchy>($"/api/hierarchy/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching hierarchy member {id}");
                return null;
            }
        }

        public async Task<bool> CreateHierarchyAsync(Hierarchy member)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PostAsJsonAsync("/api/hierarchy", member);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating hierarchy member");
                return false;
            }
        }

        public async Task<bool> UpdateHierarchyAsync(int id, Hierarchy member)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.PutAsJsonAsync($"/api/hierarchy/{id}", member);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating hierarchy member {id}");
                return false;
            }
        }

        public async Task<bool> DeleteHierarchyAsync(int id)
        {
            var client = _factory.CreateClient("apiservice");
            try
            {
                var resp = await client.DeleteAsync($"/api/hierarchy/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting hierarchy member {id}");
                return false;
            }
        }

        #endregion



        #region COURS API (cours)

        public async Task<List<CourseSummaryDto>> GetMyCoursesAsync(string? subject = null)
        {
            if (string.IsNullOrEmpty(_tokenProvider.Token)) return new List<CourseSummaryDto>();
            
            var client = CreateAuthClient("cours");
            try
            {
                var url = subject is null ? "/cours" : $"/cours?subject={Uri.EscapeDataString(subject)}";
                var resp = await client.GetFromJsonAsync<List<CourseSummaryDto>>(url);
                return resp ?? new List<CourseSummaryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching my courses");
                return new List<CourseSummaryDto>();
            }
        }

        public async Task<CourseDto?> GetCourseAsync(Guid id)
        {
            if (string.IsNullOrEmpty(_tokenProvider.Token)) return null;

            var client = CreateAuthClient("cours");
            try
            {
                return await client.GetFromJsonAsync<CourseDto>($"/cours/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching course {id}");
                return null;
            }
        }

        public async Task<CourseDto?> CreateCourseAsync(CreateCourseRequest request)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PostAsJsonAsync("/cours", request);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<CourseDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating course");
                return null;
            }
        }

        public async Task<bool> UpdateCourseAsync(Guid id, UpdateCourseRequest request)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PutAsJsonAsync($"/cours/{id}", request);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating course {id}");
                return false;
            }
        }

        public async Task<bool> DeleteCourseAsync(Guid id)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.DeleteAsync($"/cours/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting course {id}");
                return false;
            }
        }

        /// <summary>Un document à téléverser : PDF ou photo de cours.</summary>
        public sealed record CourseUpload(Stream Content, string FileName, string ContentType);

        /// <summary>
        /// Téléverse un PDF ou une série de photos sur un cours.
        ///
        /// L'API répond dès que les fichiers sont écrits ; la transcription et la mise en
        /// forme se poursuivent en tâche de fond (FormatStatus = Pending), sinon la requête
        /// expirerait avant la fin du traitement.
        /// </summary>
        public async Task<(bool ok, string? error)> UploadCourseDocumentsAsync(
            Guid courseId, IReadOnlyList<CourseUpload> files)
        {
            if (files.Count == 0) return (false, "Aucun fichier sélectionné.");

            var client = CreateAuthClient("cours");
            try
            {
                using var content = new MultipartFormDataContent();

                foreach (var file in files)
                {
                    var part = new StreamContent(file.Content);
                    part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                    // Le nom de champ "files" doit correspondre au binding IFormFileCollection
                    // côté API ; le nom de fichier est conservé pour l'affichage.
                    content.Add(part, "files", file.FileName);
                }

                var resp = await client.PostAsync($"/cours/{courseId}/upload", content);
                if (resp.IsSuccessStatusCode) return (true, null);

                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("Upload refusé pour le cours {CourseId} : {Status} {Body}",
                    courseId, resp.StatusCode, body);

                return (false, ExtractMessage(body) ?? "Le téléversement a échoué.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading documents for course {courseId}");
                return (false, "Le service de cours est injoignable.");
            }
        }

        /// <summary>Relance la mise en forme et la réindexation (bouton « Régénérer »).</summary>
        public async Task<bool> RequestCourseFormatAsync(Guid courseId)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PostAsync($"/cours/{courseId}/format", content: null);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error requesting formatting for course {courseId}");
                return false;
            }
        }

        /// <summary>Recherche sémantique dans un cours (diagnostic, et réutilisable côté UI).</summary>
        public async Task<List<SearchHitDto>> SearchCourseAsync(Guid courseId, string query, int k = 5)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PostAsJsonAsync($"/cours/{courseId}/search", new { Query = query, K = k });
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<List<SearchHitDto>>() ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching course {courseId}");
                return [];
            }
        }

        // ── Parcours d'apprentissage ────────────────────────────────────────────

        public async Task<JourneyDto?> GetJourneyAsync(Guid courseId)
        {
            var client = CreateAuthClient("cours");
            try
            {
                return await client.GetFromJsonAsync<JourneyDto>($"/cours/{courseId}/parcours");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching journey for course {courseId}");
                return null;
            }
        }

        /// <summary>Marque une étape de lecture comme faite et renvoie le parcours à jour.</summary>
        public async Task<JourneyDto?> MarkStepReadAsync(Guid courseId, Guid stepId)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PostAsync($"/cours/{courseId}/parcours/{stepId}/lu", content: null);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<JourneyDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking step {stepId} as read");
                return null;
            }
        }

        /// <summary>
        /// Enregistre le résultat d'une étape évaluée. Le seuil est appliqué côté API :
        /// le parcours renvoyé dit ce qui s'est déverrouillé.
        /// </summary>
        public async Task<JourneyDto?> SubmitStepResultAsync(
            Guid courseId, Guid stepId, StepResultRequest request)
        {
            var client = CreateAuthClient("cours");
            try
            {
                var resp = await client.PostAsJsonAsync($"/cours/{courseId}/parcours/{stepId}/resultat", request);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<JourneyDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting result for step {stepId}");
                return null;
            }
        }

        /// <summary>
        /// Signale une activité d'étude, pour le journal des séances de révision.
        ///
        /// Silencieux en cas d'échec : perdre un signal de présence ne doit jamais
        /// interrompre le travail de l'élève.
        /// </summary>
        public async Task ReportActivityAsync(Guid courseId, StudyActivity activity)
        {
            var client = CreateAuthClient("cours");
            try
            {
                // L'enum part en nombre, comme partout ailleurs dans ce projet :
                // aucun convertisseur de chaîne n'est enregistré côté Cours.
                await client.PostAsJsonAsync($"/cours/{courseId}/seance/activite",
                    new { Activity = activity });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Activity signal dropped for course {courseId}");
            }
        }

        /// <summary>Extrait le champ `message` d'une réponse d'erreur JSON de l'API.</summary>
        private static string? ExtractMessage(string body)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        // ── Upload Image (Hébergée sur ApiService) ──────────────────────────────────
        public async Task<string?> UploadImageAsync(Stream fileStream, string fileName)
        {
            try
            {
                using var client = _factory.CreateClient("apiservice");
                using var content = new MultipartFormDataContent();

                var fileContent = new StreamContent(fileStream);
                content.Add(fileContent, "file", fileName);

                var resp = await client.PostAsync("/api/upload", content);
                if (resp.IsSuccessStatusCode)
                {
                    var result = await resp.Content.ReadFromJsonAsync<UploadResponse>();
                    if (result != null && !string.IsNullOrEmpty(result.Url))
                    {
                        // Build absolute URL from the request URI (avoids the http+https:// scheme from Aspire)
                        var requestUri = resp.RequestMessage?.RequestUri;
                        if (requestUri != null)
                        {
                            return $"{requestUri.Scheme}://{requestUri.Host}:{requestUri.Port}{result.Url}";
                        }
                        // Fallback: use the base address if request URI is unavailable
                        var baseAddress = client.BaseAddress?.ToString().TrimEnd('/');
                        if (baseAddress != null)
                        {
                            // Strip any non-standard scheme (e.g., "https+http://")
                            var uri = new Uri(baseAddress);
                            return $"https://{uri.Host}:{uri.Port}{result.Url}";
                        }
                    }
                }
                _logger.LogWarning($"Image upload failed: {resp.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception uploading image");
                return null;
            }
        }

        private class UploadResponse
        {
            public string? Url { get; set; }
        }

        #endregion



        #region CHAT API (chat)

        public async Task<ChatMessageResponse?> SendChatMessageAsync(string message, List<ConversationMessage> history, string? courseContent = null, string? courseId = null)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var req = new ChatMessageRequest(message, history, courseContent, courseId);
                var resp = await client.PostAsJsonAsync("/chat/message", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ChatMessageResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending chat message");
                return null;
            }
        }

        /// <summary>
        /// Interroge le répétiteur multi-agents en flux (SSE).
        ///
        /// Le pipeline enchaîne plusieurs appels LLM : sans flux, l'élève attendrait
        /// une dizaine de secondes devant un écran figé. On remonte donc les étapes
        /// (« Recherche dans le cours… ») puis la réponse au fil de sa rédaction.
        ///
        /// Le contenu du cours n'est plus envoyé : le service Chat le récupère et n'en
        /// retient que les passages pertinents.
        /// </summary>
        public async IAsyncEnumerable<TutorStreamEvent> StreamTutorAsync(
            Guid courseId,
            string message,
            IReadOnlyList<ConversationMessage> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var client = CreateAuthClient("chat");

            var request = new HttpRequestMessage(HttpMethod.Post, "/chat/tuteur/stream")
            {
                Content = JsonContent.Create(new TutorRequest(courseId, message, history.ToList())),
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Le service de répétition est injoignable");
                yield return new TutorStreamEvent("error",
                    Error: "Le répétiteur est momentanément injoignable. Réessayez dans un instant.");
                yield break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Le répétiteur a répondu {Status}", response.StatusCode);
                    yield return new TutorStreamEvent("error",
                        Error: "Le répétiteur n'a pas pu traiter la question.");
                    yield break;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                    var payload = line[5..].Trim();
                    if (payload is "[DONE]") yield break;

                    TutorStreamEvent? evt = null;
                    try
                    {
                        evt = System.Text.Json.JsonSerializer.Deserialize<TutorStreamEvent>(
                            payload,
                            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        // Fragment SSE illisible : on l'ignore plutôt que d'interrompre
                        // une réponse déjà partiellement affichée.
                        _logger.LogWarning(ex, "Évènement SSE illisible du répétiteur");
                    }

                    if (evt is not null) yield return evt;
                }
            }
        }

        /// <summary>Demande un exercice ouvert sur un cours, ou sur une de ses parties.</summary>
        public async Task<string?> GenerateExerciseAsync(Guid courseId, string? headingPath)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var resp = await client.PostAsJsonAsync("/chat/exercice",
                    new { CourseId = courseId, HeadingPath = headingPath });

                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadFromJsonAsync<ExerciceResponse>();
                return body?.Statement;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating exercise for course {courseId}");
                return null;
            }
        }

        /// <summary>
        /// Demande la question ouverte de synthèse : ce que l'élève a retenu du cours
        /// dans son ensemble, et non d'une de ses parties. Pas de <c>headingPath</c>
        /// ici — c'est justement une question sur le tout.
        /// </summary>
        public async Task<string?> GenerateSynthesisAsync(Guid courseId)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var resp = await client.PostAsJsonAsync("/chat/synthese",
                    new { CourseId = courseId, HeadingPath = (string?)null });

                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadFromJsonAsync<ExerciceResponse>();
                return body?.Statement;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating synthesis question for course {courseId}");
                return null;
            }
        }

        /// <summary>Fait corriger la réponse rédigée par l'élève.</summary>
        public async Task<ExerciseCorrection?> CorrectExerciseAsync(
            Guid courseId, string? headingPath, string statement, string answer)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var resp = await client.PostAsJsonAsync("/chat/exercice/corriger",
                    new { CourseId = courseId, HeadingPath = headingPath, Statement = statement, Answer = answer });

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ExerciseCorrection>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error correcting exercise for course {courseId}");
                return null;
            }
        }

        /// <summary>
        /// Récupère la lecture à voix haute d'un texte, en base64 prêt à être joué.
        ///
        /// L'audio transite par le circuit Blazor plutôt que par une URL directe :
        /// l'explication est courte, et cela évite d'exposer une seconde route
        /// authentifiée au navigateur.
        /// </summary>
        public async Task<string?> SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var client = CreateAuthClient("chat");
            try
            {
                var resp = await client.PostAsJsonAsync("/chat/voix", new { Text = text });
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Synthèse vocale refusée : {Status}", resp.StatusCode);
                    return null;
                }

                return Convert.ToBase64String(await resp.Content.ReadAsByteArrayAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting speech synthesis");
                return null;
            }
        }

        public async Task<CourseSummaryResponse?> GetCourseSummaryAsync(string? courseContent = null, string? courseId = null)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var req = new CourseSummaryRequest(courseContent, courseId);
                var resp = await client.PostAsJsonAsync("/chat/summary", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<CourseSummaryResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting course summary");
                return null;
            }
        }

        public async Task<string?> GenerateCourseDraftAsync(string subject, string? additionalInstructions = null)
        {
            var client = CreateAuthClient("chat");
            try
            {
                // Nous reproduisons ici l'objet CourseDraftRequest
                var req = new { Subject = subject, AdditionalInstructions = additionalInstructions };
                var resp = await client.PostAsJsonAsync("/chat/draft", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating course draft");
                return null;
            }
        }
        #endregion


        #region QUIZ API (quiz)

        public async Task<GenerateQuizResponse?> GenerateQuizAsync(string title, string courseText, int numberOfQuestions = 5)
        {
            var client = CreateAuthClient("quiz");
            try
            {
                var req = new GenerateQuizRequest(title, courseText, numberOfQuestions);
                var resp = await client.PostAsJsonAsync("/api/quiz/generate", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<GenerateQuizResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating quiz");
                return null;
            }
        }

        public async Task<GenerateQuizResponse?> GetQuizAsync(Guid id)
        {
            var client = CreateAuthClient("quiz");
            try
            {
                var resp = await client.GetAsync($"/api/quiz/{id}");
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<GenerateQuizResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching quiz {id}");
                return null;
            }
        }

        /// <summary>
        /// Corrige une seule question, pour expliquer à l'élève au moment où il se
        /// trompe plutôt qu'à la fin du test.
        /// </summary>
        public async Task<AnswerFeedback?> AnswerQuizQuestionAsync(Guid quizId, Guid questionId, int selectedIndex)
        {
            var client = CreateAuthClient("quiz");
            try
            {
                var resp = await client.PostAsJsonAsync(
                    $"/api/quiz/{quizId}/questions/{questionId}/answer",
                    new { SelectedOptionIndex = selectedIndex });

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<AnswerFeedback>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error answering question {questionId} of quiz {quizId}");
                return null;
            }
        }

        public async Task<SubmitAnswersResponse?> SubmitQuizAnswersAsync(Guid quizId, List<StudentAnswer> answers)
        {
            var client = CreateAuthClient("quiz");
            try
            {
                var req = new SubmitAnswersRequest(answers);
                var resp = await client.PostAsJsonAsync($"/api/quiz/{quizId}/submit", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<SubmitAnswersResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting answers for quiz {quizId}");
                return null;
            }
        }

        #endregion


        #region AUTH API (user)

        public async Task<AuthResponse?> LoginAsync(string email, string password)
        {
            var client = _factory.CreateClient("user");
            try
            {
                var resp = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<AuthResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging in");
                return null;
            }
        }

        /// <summary>
        /// Inscription. Renvoie le message du serveur en cas de refus : l'inscription
        /// d'un parent peut échouer pour une raison précise (« un parent n'a ni niveau
        /// ni filière »), et « Erreur lors de la création du compte » n'aiderait personne.
        /// </summary>
        public async Task<(AuthResponse? auth, string? error)> RegisterAsync(RegisterRequest req)
        {
            var client = _factory.CreateClient("user");
            try
            {
                var resp = await client.PostAsJsonAsync("/auth/register", req);

                if (!resp.IsSuccessStatusCode)
                    return (null, await ReadProblemAsync(resp));

                return (await resp.Content.ReadFromJsonAsync<AuthResponse>(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering");
                return (null, "Le service d'inscription est injoignable.");
            }
        }

        // ── Lien famille ─────────────────────────────────────────────────────────

        /// <summary>L'élève émet un code que son parent saisira. À usage unique, 30 minutes.</summary>
        public async Task<LinkCodeResponse?> GenerateLinkCodeAsync()
        {
            var client = CreateAuthClient("user");
            try
            {
                var resp = await client.PostAsync("/auth/me/code-parent", content: null);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<LinkCodeResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating parent link code");
                return null;
            }
        }

        /// <summary>
        /// Le parent saisit le code. La réponse porte un jeton rafraîchi : l'appelant doit
        /// le passer à AuthService, sans quoi l'enfant restera invisible une heure durant.
        /// </summary>
        public async Task<(LinkChildResponse? link, string? error)> LinkChildAsync(string code)
        {
            var client = CreateAuthClient("user");
            try
            {
                var resp = await client.PostAsJsonAsync("/auth/parent/enfants", new { Code = code });

                if (!resp.IsSuccessStatusCode)
                    return (null, await ReadProblemAsync(resp) ?? "Ce code n'est pas valide.");

                return (await resp.Content.ReadFromJsonAsync<LinkChildResponse>(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error linking child");
                return (null, "Le service est injoignable.");
            }
        }

        public async Task<List<ChildSummaryDto>> GetChildrenAsync()
        {
            var client = CreateAuthClient("user");
            try
            {
                return await client.GetFromJsonAsync<List<ChildSummaryDto>>("/auth/parent/enfants") ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching linked children");
                return [];
            }
        }

        /// <summary>Délie un enfant. Renvoie le jeton rafraîchi, à réappliquer.</summary>
        public async Task<RefreshedTokenResponse?> UnlinkChildAsync(Guid childId)
        {
            var client = CreateAuthClient("user");
            try
            {
                var resp = await client.DeleteAsync($"/auth/parent/enfants/{childId}");
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<RefreshedTokenResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlinking child {childId}");
                return null;
            }
        }

        /// <summary>Les parents rattachés à l'élève connecté — il doit savoir qui le suit.</summary>
        public async Task<List<LinkedParentDto>> GetMyParentsAsync()
        {
            if (string.IsNullOrEmpty(_tokenProvider.Token)) return [];

            var client = CreateAuthClient("user");
            try
            {
                return await client.GetFromJsonAsync<List<LinkedParentDto>>("/auth/me/parents") ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching linked parents");
                return [];
            }
        }

        public async Task<bool> UnlinkParentAsync(Guid parentId)
        {
            var client = CreateAuthClient("user");
            try
            {
                var resp = await client.DeleteAsync($"/auth/me/parents/{parentId}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlinking parent {parentId}");
                return false;
            }
        }

        // ── Suivi parental ───────────────────────────────────────────────────────

        /// <summary>Vue d'ensemble d'un enfant : a-t-il travaillé, combien, sur quoi.</summary>
        public async Task<ChildOverviewDto?> GetChildOverviewAsync(Guid studentId, int jours = 7)
        {
            var client = CreateAuthClient("cours");
            try
            {
                return await client.GetFromJsonAsync<ChildOverviewDto>(
                    $"/suivi/{studentId}/resume?jours={jours}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching overview for student {studentId}");
                return null;
            }
        }

        public async Task<List<StudySessionDto>> GetChildSessionsAsync(Guid studentId, int take = 30)
        {
            var client = CreateAuthClient("cours");
            try
            {
                return await client.GetFromJsonAsync<List<StudySessionDto>>(
                    $"/suivi/{studentId}/seances?take={take}") ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching sessions for student {studentId}");
                return [];
            }
        }

        public async Task<ChildCourseDetailDto?> GetChildCourseAsync(Guid studentId, Guid courseId)
        {
            var client = CreateAuthClient("cours");
            try
            {
                return await client.GetFromJsonAsync<ChildCourseDetailDto>(
                    $"/suivi/{studentId}/cours/{courseId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching course {courseId} for student {studentId}");
                return null;
            }
        }

        /// <summary>Le bilan rédigé pour le parent. Généré une fois par jour côté Chat.</summary>
        public async Task<BilanResponse?> GetBilanAsync(Guid studentId, Guid courseId)
        {
            var client = CreateAuthClient("chat");
            try
            {
                var resp = await client.PostAsJsonAsync("/chat/bilan",
                    new { StudentId = studentId, CourseId = courseId });

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<BilanResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching bilan for student {studentId}, course {courseId}");
                return null;
            }
        }

        /// <summary>
        /// Message d'erreur renvoyé par nos services, qui répondent tous
        /// <c>{ "message": "…" }</c>. Null si la réponse ne dit rien d'exploitable.
        /// </summary>
        private static async Task<string?> ReadProblemAsync(HttpResponseMessage response)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());

                return document.RootElement.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        #endregion



    }
}
