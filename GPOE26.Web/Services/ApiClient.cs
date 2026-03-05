using System.Net.Http.Json;
using GPOE26.Web.Models;

namespace GPOE26.Web.Services
{
    public class ApiClient
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<ApiClient> _logger;

        public ApiClient(IHttpClientFactory httpClientFactory, ILogger<ApiClient> logger)
        {
            _factory = httpClientFactory;
            _logger = logger;
        }


        #region Contact API (GPO26ApiService)

        public async Task<Contact?> GetFooterContactAsync()
        {
            var client = _factory.CreateClient("GPOE26ApiService");
            try
            {
                var contacts = await client.GetFromJsonAsync<List<Contact>>("/api/contact");
                return contacts?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching footer contact");
            }
            return null;
        }

        public async Task<List<Contact>> GetAllContactsAsync()
        {
            var client = _factory.CreateClient("PortfolioApiService");
            try
            {
                return await client.GetFromJsonAsync<List<Contact>>("/api/contact")
                       ?? new List<Contact>();
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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

        #endregion



        #region SPEECH API (GPO26ApiService)

        public async Task<List<Speech>> GetAllSpeechesAsync()
        {
            var client = _factory.CreateClient("GPOE26ApiService");
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
            var client = _factory.CreateClient("GPOE26ApiService");
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

        #endregion

        #region HIERARCHY API (GPO26ApiService)

        public async Task<List<Hierarchy>> GetHierarchyAsync()
        {
            var client = _factory.CreateClient("GPOE26ApiService");
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

        #endregion



        #region COURS API (cours)

        public async Task<List<CourseSummaryDto>> GetMyCoursesAsync(string? subject = null)
        {
            var client = _factory.CreateClient("cours");
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
            var client = _factory.CreateClient("cours");
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

        #endregion



        #region CHAT API (chat)

        public async Task<ChatMessageResponse?> SendChatMessageAsync(string message, List<ConversationMessage> history, string? courseContent = null, string? courseId = null)
        {
            var client = _factory.CreateClient("chat");
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

        public async Task<CourseSummaryResponse?> GetCourseSummaryAsync(string courseContent)
        {
            var client = _factory.CreateClient("chat");
            try
            {
                var req = new CourseSummaryRequest(courseContent);
                var resp = await client.PostAsJsonAsync("/chat/summary", req);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<CourseSummaryResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching course summary");
                return null;
            }
        }

        #endregion


        #region QUIZ API (quiz)

        public async Task<GenerateQuizResponse?> GenerateQuizAsync(string title, string courseText, int numberOfQuestions = 5)
        {
            var client = _factory.CreateClient("quiz");
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
            var client = _factory.CreateClient("quiz");
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

        public async Task<SubmitAnswersResponse?> SubmitQuizAnswersAsync(Guid quizId, List<StudentAnswer> answers)
        {
            var client = _factory.CreateClient("quiz");
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

        public async Task<AuthResponse?> RegisterAsync(RegisterRequest req)
        {
            var client = _factory.CreateClient("user");
            try
            {
                var resp = await client.PostAsJsonAsync("/auth/register", req);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<AuthResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering");
                return null;
            }
        }

        #endregion



    }
}
