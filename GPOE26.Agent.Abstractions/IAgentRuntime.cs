namespace GPOE26.Agent.Abstractions;

/// <summary>
/// Provider-neutral boundary for agent execution. Implementations may use MAF, ADK,
/// Pi, or a remote runtime, but must emit only GPOE26 contract types.
/// </summary>
public interface IAgentRuntime
{
    IAsyncEnumerable<AgentRunEvent> RunAsync(
        AgentRunRequest request,
        IAgentToolExecutor toolExecutor,
        CancellationToken cancellationToken = default);
}

public static class AgentRuntimeExtensions
{
    public static async Task<AgentRunResult> RunToCompletionAsync(
        this IAgentRuntime runtime,
        AgentRunRequest request,
        IAgentToolExecutor toolExecutor,
        CancellationToken cancellationToken = default)
    {
        AgentRunResult? result = null;
        AgentRunFailed? failure = null;

        await foreach (var runEvent in runtime.RunAsync(request, toolExecutor, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            switch (runEvent)
            {
                case AgentRunCompleted completed:
                    result = completed.Result;
                    break;
                case AgentRunFailed failed:
                    failure = failed;
                    break;
            }
        }

        if (failure is not null)
            throw new AgentRuntimeException(failure.Code, failure.Message, failure.IsRetryable);

        return result ?? throw new AgentRuntimeException(
            "missing_completion",
            "The agent runtime ended without a completion event.",
            isRetryable: false);
    }
}

public sealed class AgentRuntimeException(
    string code,
    string message,
    bool isRetryable) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsRetryable { get; } = isRetryable;
}
