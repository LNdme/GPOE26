using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using GPOE26.Agent.Abstractions;
using GPOE26.Agent.Runtime.Maf;
using Microsoft.Extensions.Logging;

namespace GPOE26.Tests;

public sealed class AgentObservabilityTests
{
    [Fact]
    public async Task RecordsRunModelAndToolTelemetryWithoutRecordingPayloads()
    {
        const string userText = "PRIVATE_USER_MESSAGE";
        const string courseText = "PRIVATE_COURSE_CONTENT";
        const string toolResult = "PRIVATE_TOOL_RESULT";
        var loggerProvider = new RecordingLoggerProvider();
        var activities = new ConcurrentBag<Activity>();
        var instruments = new ConcurrentBag<string>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        using var activityListener = CreateActivityListener(activities);
        using var meterListener = CreateMeterListener(instruments);
        var telemetry = new ObservedAgentRuntime(
            new EventRuntime(
            [
                new AgentRunStarted("run-123"),
                new AgentModelAttemptStarted("model-safe-name"),
                new AgentModelAttemptCompleted("model-safe-name", true, 12.5),
                new AgentToolExecutionStarted("call-1", "course.search"),
                new AgentToolExecutionCompleted(
                    "call-1",
                    "course.search",
                    new AgentToolResult(toolResult)),
                new AgentTextDelta(userText),
                new AgentRunCompleted(new AgentRunResult("run-123", userText, ["course.search"]))
            ]),
            loggerFactory.CreateLogger<ObservedAgentRuntime>());

        var received = new List<AgentRunEvent>();
        await foreach (var runEvent in telemetry.RunAsync(
                           CreateRequest(userText, courseText),
                           EmptyToolExecutor.Instance))
        {
            received.Add(runEvent);
        }

        Assert.Equal(7, received.Count);
        Assert.Contains(loggerProvider.Messages, message => message.Contains("run-123"));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("model-safe-name"));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("course.search"));
        var serializedLog = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(userText, serializedLog);
        Assert.DoesNotContain(courseText, serializedLog);
        Assert.DoesNotContain(toolResult, serializedLog);

        var activity = Assert.Single(activities);
        Assert.Equal("run-123", activity.GetTagItem("agent.run.id"));
        Assert.Equal("pedagogical", activity.GetTagItem("agent.profile.id"));
        Assert.Equal("MicrosoftAgentFramework", activity.GetTagItem("agent.runtime"));
        var activityData = string.Join(
            " ",
            activity.Tags.Select(tag => $"{tag.Key}={tag.Value}")
                .Concat(activity.Events.SelectMany(item => item.Tags.Select(tag => $"{tag.Key}={tag.Value}"))));
        Assert.DoesNotContain(userText, activityData);
        Assert.DoesNotContain(courseText, activityData);
        Assert.DoesNotContain(toolResult, activityData);
        Assert.Contains("agent.runs.started", instruments);
        Assert.Contains("agent.runs.completed", instruments);
        Assert.Contains("agent.run.duration", instruments);
        Assert.Contains("agent.model.attempts", instruments);
        Assert.Contains("agent.model.attempt.duration", instruments);
        Assert.Contains("agent.tool.executions", instruments);
    }

    [Fact]
    public async Task FailureTelemetryExcludesFailureMessageAndUserPayload()
    {
        const string sensitiveText = "PRIVATE_FAILURE_CONTENT";
        var loggerProvider = new RecordingLoggerProvider();
        var activities = new ConcurrentBag<Activity>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        using var activityListener = CreateActivityListener(activities);
        var runtime = new ObservedAgentRuntime(
            new EventRuntime(
            [
                new AgentRunStarted("run-failed"),
                new AgentRunFailed("safe_failure_code", sensitiveText, IsRetryable: false)
            ]),
            loggerFactory.CreateLogger<ObservedAgentRuntime>());

        await Assert.ThrowsAsync<AgentRuntimeException>(() =>
            runtime.RunToCompletionAsync(CreateRequest(sensitiveText, sensitiveText), EmptyToolExecutor.Instance));

        var serializedLog = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(sensitiveText, serializedLog);
        Assert.Contains("safe_failure_code", serializedLog);
        var activity = Assert.Single(activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.DoesNotContain(sensitiveText, string.Join(" ", activity.Events.SelectMany(item => item.Tags)
            .Select(tag => $"{tag.Key}={tag.Value}")));
    }

    private static AgentRunRequest CreateRequest(string userText, string courseText) =>
        new(
            "run-123",
            Guid.NewGuid(),
            new AgentProfileDefinition("pedagogical", "Tutor", "description", "instructions", []),
            new AgentPresentationContext(CourseTitle: courseText),
            [],
            userText,
            []);

    private static ActivityListener CreateActivityListener(ConcurrentBag<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ObservedAgentRuntime.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        listener.ActivityStopped = activity => activities.Add(activity);
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(ConcurrentBag<string> instruments)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ObservedAgentRuntime.InstrumentationName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
            instruments.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
            instruments.Add(instrument.Name));
        listener.Start();
        return listener;
    }

    private sealed class EventRuntime(IReadOnlyList<AgentRunEvent> events) : IAgentRuntime
    {
        public async IAsyncEnumerable<AgentRunEvent> RunAsync(
            AgentRunRequest request,
            IAgentToolExecutor toolExecutor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var runEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return runEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(ConcurrentBag<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }

    private sealed class EmptyToolExecutor : IAgentToolExecutor
    {
        public static EmptyToolExecutor Instance { get; } = new();
        public IReadOnlyList<AgentToolDefinition> Tools => [];

        public Task<AgentToolResult> ExecuteAsync(AgentToolCall call, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
