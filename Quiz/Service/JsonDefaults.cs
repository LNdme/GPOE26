using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quiz.Service;

public static class JsonDefaults
{
    /// <summary>Lecture des réponses du service User (camelCase, enums en texte)</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
