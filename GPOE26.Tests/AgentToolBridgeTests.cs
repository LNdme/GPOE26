using System.Text.Json;
using GPOE26.Agent.Abstractions;
using GPOE26.Agent.Runtime.Maf;
using Microsoft.Extensions.AI;

namespace GPOE26.Tests;

public sealed class AgentToolBridgeTests
{
    [Fact]
    public async Task BuildsOnlyProfileAllowedToolsAndRoutesCallsToHost()
    {
        var executor = new RecordingToolExecutor(
        [
            new AgentToolDefinition(
                "course.search",
                "Search the current course.",
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""),
            new AgentToolDefinition("account.delete", "Delete an account.", """{"type":"object"}""")
        ]);
        var events = new List<AgentRunEvent>();

        var success = MafAgentRuntime.TryBuildToolFunctions(
            ["course.search"],
            executor,
            events.Add,
            out var functions,
            out var failure);

        Assert.True(success, failure.Message);
        var function = Assert.Single(functions);
        Assert.Equal("course.search", function.Name);
        Assert.Equal("string", function.JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());

        var result = await function.InvokeAsync(new AIFunctionArguments { ["query"] = "fractions" });

        Assert.Equal(new AgentToolResult("Course notes"), result);
        var call = Assert.Single(executor.Calls);
        Assert.Equal("course.search", call.Name);
        using var arguments = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("fractions", arguments.RootElement.GetProperty("query").GetString());
        Assert.Collection(
            events,
            item => Assert.IsType<AgentToolExecutionStarted>(item),
            item => Assert.IsType<AgentToolExecutionCompleted>(item));
    }

    [Fact]
    public void RejectsToolsNotProvidedByTheHost()
    {
        var executor = new RecordingToolExecutor([]);

        var success = MafAgentRuntime.TryBuildToolFunctions(
            ["course.search"],
            executor,
            _ => { },
            out var functions,
            out var failure);

        Assert.False(success);
        Assert.Empty(functions);
        Assert.Equal("profile_tool_mismatch", failure.Code);
    }

    [Fact]
    public void RejectsMalformedToolSchema()
    {
        var executor = new RecordingToolExecutor(
        [
            new AgentToolDefinition("course.search", "Search the current course.", "{")
        ]);

        var success = MafAgentRuntime.TryBuildToolFunctions(
            ["course.search"],
            executor,
            _ => { },
            out var functions,
            out var failure);

        Assert.False(success);
        Assert.Empty(functions);
        Assert.Equal("invalid_tool_schema", failure.Code);
    }

    private sealed class RecordingToolExecutor(IReadOnlyList<AgentToolDefinition> tools) : IAgentToolExecutor
    {
        public IReadOnlyList<AgentToolDefinition> Tools => tools;

        public List<AgentToolCall> Calls { get; } = [];

        public Task<AgentToolResult> ExecuteAsync(AgentToolCall call, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            return Task.FromResult(new AgentToolResult("Course notes"));
        }
    }
}
