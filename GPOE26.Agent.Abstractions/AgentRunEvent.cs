namespace GPOE26.Agent.Abstractions;

public abstract record AgentRunEvent;

public sealed record AgentRunStarted(string RunId) : AgentRunEvent;

public sealed record AgentModelAttemptStarted(string Model) : AgentRunEvent;

public sealed record AgentModelAttemptCompleted(
    string Model,
    bool Succeeded,
    double DurationMilliseconds) : AgentRunEvent;

public sealed record AgentTextDelta(string Text) : AgentRunEvent;

public sealed record AgentToolExecutionStarted(string CallId, string ToolName) : AgentRunEvent;

public sealed record AgentToolExecutionCompleted(
    string CallId,
    string ToolName,
    AgentToolResult Result) : AgentRunEvent;

public sealed record AgentRunCompleted(AgentRunResult Result) : AgentRunEvent;

public sealed record AgentRunFailed(
    string Code,
    string Message,
    bool IsRetryable) : AgentRunEvent;

public sealed record AgentRunResult(
    string RunId,
    string Answer,
    IReadOnlyList<string> ExecutedToolNames);
