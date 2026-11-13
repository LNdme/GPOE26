namespace Quiz.Model;

/// <summary>
/// Photo d'une copie traitée sur papier (maths, physique…), jointe à une réponse ouverte pendant la composition.
/// Plusieurs pages possibles par question. L'IA en propose une transcription que l'élève peut vérifier.
/// </summary>
public class AnswerPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid StudentId { get; set; }

    public int Page { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public byte[] Data { get; set; } = [];

    /// <summary>Lecture de la copie par l'IA (texte et formules LaTeX), null si l'IA n'a pas pu la lire</summary>
    public string? Transcription { get; set; }

    /// <summary>Lecture par l'IA pas encore faite (elle se fait en arrière-plan, cf. TranscriptionWorker)</summary>
    public bool TranscriptionPending { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
