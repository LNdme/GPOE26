using Microsoft.EntityFrameworkCore;

namespace Quiz.Data;

/// <summary>
/// Création du schéma au démarrage (pas de migrations EF dans ce projet).
/// Chaque instruction est idempotente.
/// Attention : ExecuteSqlRaw interprète les accolades — utiliser ARRAY[]::type[] plutôt que '{}'.
/// </summary>
public static class EvaluationSchema
{
    private static readonly string[] Statements =
    {
        @"CREATE TABLE IF NOT EXISTS ""Assessments"" (
            ""Id"" uuid NOT NULL,
            ""TeacherId"" uuid NOT NULL,
            ""Title"" character varying(200) NOT NULL,
            ""Description"" text,
            ""Kind"" text NOT NULL,
            ""Status"" text NOT NULL,
            ""Subject"" text,
            ""CourseId"" uuid,
            ""ClassroomIds"" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[],
            ""OpensAt"" timestamp with time zone,
            ""DueAt"" timestamp with time zone,
            ""DurationMinutes"" integer,
            ""MaxAttempts"" integer NOT NULL DEFAULT 1,
            ""Shuffle"" boolean NOT NULL DEFAULT false,
            ""ShowCorrection"" boolean NOT NULL DEFAULT true,
            ""AllowOffline"" boolean NOT NULL DEFAULT false,
            ""AiAssistedGrading"" boolean NOT NULL DEFAULT true,
            ""ScaleTo"" integer NOT NULL DEFAULT 20,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            ""PublishedAt"" timestamp with time zone,
            CONSTRAINT ""PK_Assessments"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_Assessments_TeacherId"" ON ""Assessments"" (""TeacherId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_Assessments_ClassroomIds"" ON ""Assessments"" USING gin (""ClassroomIds"")",

        @"CREATE TABLE IF NOT EXISTS ""AssessmentQuestions"" (
            ""Id"" uuid NOT NULL,
            ""AssessmentId"" uuid NOT NULL REFERENCES ""Assessments"" (""Id"") ON DELETE CASCADE,
            ""Order"" integer NOT NULL,
            ""Type"" text NOT NULL,
            ""Text"" text NOT NULL,
            ""Points"" numeric(6,2) NOT NULL,
            ""Options"" text[] NOT NULL DEFAULT ARRAY[]::text[],
            ""CorrectOptions"" integer[] NOT NULL DEFAULT ARRAY[]::integer[],
            ""TrueFalseAnswer"" boolean,
            ""AcceptedAnswers"" text[] NOT NULL DEFAULT ARRAY[]::text[],
            ""NumericAnswer"" double precision,
            ""Tolerance"" double precision,
            ""Unit"" text,
            ""Criteria"" text,
            ""Explanation"" text,
            ""CourseId"" uuid,
            ""SectionId"" uuid,
            CONSTRAINT ""PK_AssessmentQuestions"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_AssessmentQuestions_AssessmentId_Order"" ON ""AssessmentQuestions"" (""AssessmentId"", ""Order"")",

        @"CREATE TABLE IF NOT EXISTS ""Submissions"" (
            ""Id"" uuid NOT NULL,
            ""AssessmentId"" uuid NOT NULL REFERENCES ""Assessments"" (""Id"") ON DELETE CASCADE,
            ""StudentId"" uuid NOT NULL,
            ""AttemptNumber"" integer NOT NULL,
            ""Status"" text NOT NULL,
            ""StartedAt"" timestamp with time zone NOT NULL,
            ""SubmittedAt"" timestamp with time zone,
            ""IsLate"" boolean NOT NULL DEFAULT false,
            ""Score"" numeric(8,2),
            ""MaxScore"" numeric(8,2) NOT NULL,
            ""GradedAt"" timestamp with time zone,
            CONSTRAINT ""PK_Submissions"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_Submissions_AssessmentId_StudentId"" ON ""Submissions"" (""AssessmentId"", ""StudentId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_Submissions_StudentId"" ON ""Submissions"" (""StudentId"")",
        // Brouillon des réponses pendant la composition
        @"ALTER TABLE ""Submissions"" ADD COLUMN IF NOT EXISTS ""DraftAnswers"" text",

        @"CREATE TABLE IF NOT EXISTS ""SubmissionAnswers"" (
            ""Id"" uuid NOT NULL,
            ""SubmissionId"" uuid NOT NULL REFERENCES ""Submissions"" (""Id"") ON DELETE CASCADE,
            ""QuestionId"" uuid NOT NULL,
            ""SelectedOptions"" integer[] NOT NULL DEFAULT ARRAY[]::integer[],
            ""TrueFalse"" boolean,
            ""Text"" text,
            ""AttachmentUrl"" text,
            ""IsCorrect"" boolean,
            ""Points"" numeric(6,2),
            ""NeedsManualGrading"" boolean NOT NULL DEFAULT false,
            ""AiSuggestedPoints"" numeric(6,2),
            ""AiFeedback"" text,
            ""Feedback"" text,
            CONSTRAINT ""PK_SubmissionAnswers"" PRIMARY KEY (""Id"")
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubmissionAnswers_SubmissionId_QuestionId"" ON ""SubmissionAnswers"" (""SubmissionId"", ""QuestionId"")",

        @"ALTER TABLE ""SubmissionAnswers"" ADD COLUMN IF NOT EXISTS ""StudentAiFeedback"" text",

        // Photos des copies traitées sur papier (réponses ouvertes)
        @"CREATE TABLE IF NOT EXISTS ""AnswerPhotos"" (
            ""Id"" uuid NOT NULL,
            ""SubmissionId"" uuid NOT NULL REFERENCES ""Submissions"" (""Id"") ON DELETE CASCADE,
            ""QuestionId"" uuid NOT NULL,
            ""StudentId"" uuid NOT NULL,
            ""Page"" integer NOT NULL,
            ""ContentType"" text NOT NULL,
            ""Data"" bytea NOT NULL,
            ""Transcription"" text,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_AnswerPhotos"" PRIMARY KEY (""Id"")
        )",
        @"CREATE INDEX IF NOT EXISTS ""IX_AnswerPhotos_SubmissionId_QuestionId"" ON ""AnswerPhotos"" (""SubmissionId"", ""QuestionId"")",
        // Quiz générés par l'IA, comptés par mois pour les formules à quota
        @"CREATE TABLE IF NOT EXISTS ""QuizGenerationUsage"" (
            ""UserId"" uuid NOT NULL,
            ""Month"" text NOT NULL,
            ""Count"" integer NOT NULL,
            CONSTRAINT ""PK_QuizGenerationUsage"" PRIMARY KEY (""UserId"", ""Month"")
        )",
        // Lecture des photos en arrière-plan
        @"ALTER TABLE ""AnswerPhotos"" ADD COLUMN IF NOT EXISTS ""TranscriptionPending"" boolean NOT NULL DEFAULT false",
        // Usage de toutes les fonctions d'IA, par jour : remplace QuizGenerationUsage (quiz seulement, par mois)
        @"CREATE TABLE IF NOT EXISTS ""AiUsage"" (
            ""UserId"" uuid NOT NULL,
            ""Feature"" text NOT NULL,
            ""Day"" date NOT NULL,
            ""Count"" integer NOT NULL,
            CONSTRAINT ""PK_AiUsage"" PRIMARY KEY (""UserId"", ""Feature"", ""Day"")
        )",
        // Les générations du mois en cours, comptées dans l'ancienne table, restent décomptées (une seule fois)
        @"INSERT INTO ""AiUsage"" (""UserId"", ""Feature"", ""Day"", ""Count"")
          SELECT ""UserId"", 'quiz-ai', date_trunc('month', now() AT TIME ZONE 'UTC')::date, ""Count""
          FROM ""QuizGenerationUsage"" WHERE ""Month"" = to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM')
          ON CONFLICT DO NOTHING",
        // Bibliothèque d'établissement (phase 2, lot 8)
        @"ALTER TABLE ""Assessments"" ADD COLUMN IF NOT EXISTS ""LibraryOrganizationIds"" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[]",
        @"ALTER TABLE ""Assessments"" ADD COLUMN IF NOT EXISTS ""SourceAssessmentId"" uuid",
        @"CREATE INDEX IF NOT EXISTS ""IX_Assessments_LibraryOrganizationIds"" ON ""Assessments"" USING gin (""LibraryOrganizationIds"")",
    };

    public static async Task EnsureAsync(EvaluationContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
