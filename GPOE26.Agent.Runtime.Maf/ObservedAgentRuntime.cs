using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using GPOE26.Agent.Abstractions;
using Microsoft.Extensions.Logging;

namespace GPOE26.Agent.Runtime.Maf;

public sealed class ObservedAgentRuntime(
    IAgentRuntime runtime,
    ILogger<ObservedAgentRuntime> logger) : IAgentRuntime
{
    public const string InstrumentationName = "GPOE26.Agent.Runtime";
    private const string RuntimeName = "MicrosoftAgentFramework";
    private static readonly ActivitySource ActivitySource = new(InstrumentationName);
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> RunsStarted = Meter.CreateCounter<long>("agent.runs.started");
    private static readonly Counter<long> RunsCompleted = Meter.CreateCounter<long>("agent.runs.completed");
    private static readonly Counter<long> RunsFailed = Meter.CreateCounter<long>("agent.runs.failed");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>("agent.run.duration", "ms");
    private static readonly Counter<long> ModelAttempts = Meter.CreateCounter<long>("agent.model.attempts");
    private static readonly Histogram<double> ModelAttemptDuration =
        Meter.CreateHistogram<double>("agent.model.attempt.duration", "ms");
    private static readonly Counter<long> ToolExecutions = Meter.CreateCounter<long>("agent.tool.executions");

    public async IAsyncEnumerable<AgentRunEvent> RunAsync(
        AgentRunRequest request,
        IAgentToolExecutor toolExecutor,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tags = new ActivityTagsCollection
        {
            { "agent.run.id", request.RunId },
            { "agent.profile.id", request.Profile.Id },
            { "agent.runtime", RuntimeName }
        };
        using var activity = ActivitySource.StartActivity("agent.run", ActivityKind.Internal);
        if (activity is not null)
        {
            foreach (var tag in tags)
                activity.SetTag(tag.Key, tag.Value);
        }
        var metricTags = new TagList
        {
            { "runtime", RuntimeName },
            { "profile_id", request.Profile.Id }
        };
        var startedAt = Stopwatch.GetTimestamp();
        RunsStarted.Add(1, metricTags);
        logger.LogInformation(
            "Agent run started: {RunId} {ProfileId} {Runtime}",
            request.RunId,
            request.Profile.Id,
            RuntimeName);

        var terminalEventSeen = false;
        var enumerator = runtime.RunAsync(request, toolExecutor, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                var moved = false;
                AgentRunEvent? current = null;
                Exception? moveNextError = null;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                    if (moved)
                        current = enumerator.Current;
                }
                catch (Exception exception)
                {
                    moveNextError = exception;
                }

                if (moveNextError is not null)
                {
                    RecordFailure(request, activity, metricTags, moveNextError);
                    ExceptionDispatchInfo.Capture(moveNextError).Throw();
                }

                if (!moved)
                    break;

                var runEvent = current!;
                switch (runEvent)
                {
                    case AgentModelAttemptStarted attempt:
                        var modelTags = new TagList
                        {
                            { "runtime", RuntimeName },
                            { "profile_id", request.Profile.Id },
                            { "model", attempt.Model }
                        };
                        ModelAttempts.Add(1, modelTags);
                        activity?.SetTag("agent.model", attempt.Model);
                        activity?.AddEvent(new ActivityEvent(
                            "agent.model.attempt.started",
                            tags: new ActivityTagsCollection { { "model", attempt.Model } }));
                        logger.LogInformation(
                            "Agent model attempt started: {RunId} {ProfileId} {Runtime} {Model}",
                            request.RunId,
                            request.Profile.Id,
                            RuntimeName,
                            attempt.Model);
                        break;
                    case AgentModelAttemptCompleted attempt:
                        var attemptTags = new TagList
                        {
                            { "runtime", RuntimeName },
                            { "profile_id", request.Profile.Id },
                            { "model", attempt.Model },
                            { "outcome", attempt.Succeeded ? "success" : "failure" }
                        };
                        ModelAttemptDuration.Record(attempt.DurationMilliseconds, attemptTags);
                        activity?.AddEvent(new ActivityEvent(
                            "agent.model.attempt.completed",
                            tags: new ActivityTagsCollection
                            {
                                { "model", attempt.Model },
                                { "outcome", attempt.Succeeded ? "success" : "failure" },
                                { "duration_ms", attempt.DurationMilliseconds }
                            }));
                        break;
                    case AgentToolExecutionStarted toolStarted:
                        ToolExecutions.Add(1, new TagList
                        {
                            { "runtime", RuntimeName },
                            { "profile_id", request.Profile.Id },
                            { "tool", toolStarted.ToolName },
                            { "outcome", "started" }
                        });
                        activity?.AddEvent(new ActivityEvent(
                            "agent.tool.execution.started",
                            tags: new ActivityTagsCollection { { "tool", toolStarted.ToolName } }));
                        logger.LogInformation(
                            "Agent tool execution started: {RunId} {ProfileId} {Runtime} {Tool}",
                            request.RunId,
                            request.Profile.Id,
                            RuntimeName,
                            toolStarted.ToolName);
                        break;
                    case AgentToolExecutionCompleted toolCompleted:
                        var outcome = toolCompleted.Result.IsError ? "failure" : "success";
                        ToolExecutions.Add(1, new TagList
                        {
                            { "runtime", RuntimeName },
                            { "profile_id", request.Profile.Id },
                            { "tool", toolCompleted.ToolName },
                            { "outcome", outcome }
                        });
                        activity?.AddEvent(new ActivityEvent(
                            "agent.tool.execution.completed",
                            tags: new ActivityTagsCollection
                            {
                                { "tool", toolCompleted.ToolName },
                                { "outcome", outcome }
                            }));
                        break;
                    case AgentRunCompleted:
                        terminalEventSeen = true;
                        RunsCompleted.Add(1, metricTags);
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        activity?.AddEvent(new ActivityEvent("agent.run.completed"));
                        logger.LogInformation(
                            "Agent run completed: {RunId} {ProfileId} {Runtime}",
                            request.RunId,
                            request.Profile.Id,
                            RuntimeName);
                        break;
                    case AgentRunFailed failed:
                        terminalEventSeen = true;
                        RunsFailed.Add(1, new TagList
                        {
                            { "runtime", RuntimeName },
                            { "profile_id", request.Profile.Id },
                            { "failure_code", failed.Code }
                        });
                        activity?.SetStatus(ActivityStatusCode.Error, failed.Code);
                        activity?.AddEvent(new ActivityEvent(
                            "agent.run.failed",
                            tags: new ActivityTagsCollection
                            {
                                { "failure_code", failed.Code },
                                { "retryable", failed.IsRetryable }
                            }));
                        logger.LogWarning(
                            "Agent run failed: {RunId} {ProfileId} {Runtime} {FailureCode} {Retryable}",
                            request.RunId,
                            request.Profile.Id,
                            RuntimeName,
                            failed.Code,
                            failed.IsRetryable);
                        break;
                }

                yield return runEvent;
            }

            if (!terminalEventSeen)
            {
                var failure = new AgentRuntimeException(
                    "missing_terminal_event",
                    "The agent runtime ended without a terminal event.",
                    isRetryable: false);
                RecordFailure(request, activity, metricTags, failure);
                throw failure;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            RunDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, metricTags);
        }
    }

    private void RecordFailure(
        AgentRunRequest request,
        Activity? activity,
        TagList metricTags,
        Exception exception)
    {
        RunsFailed.Add(1, metricTags);
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.AddEvent(new ActivityEvent("agent.run.failed"));
        logger.LogError(
            "Agent run failed unexpectedly: {RunId} {ProfileId} {Runtime} {FailureType}",
            request.RunId,
            request.Profile.Id,
            RuntimeName,
            exception.GetType().Name);
    }
}
