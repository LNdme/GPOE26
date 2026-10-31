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
    };

    public static async Task EnsureAsync(EvaluationContext db)
    {
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
