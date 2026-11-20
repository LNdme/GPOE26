using System.Text.Json;
using GPOE26.Agent.Abstractions;
using Microsoft.Extensions.AI;

namespace GPOE26.Agent.Runtime.Maf;

internal sealed class MafAgentToolFunction(
    AgentToolDefinition definition,
    JsonElement jsonSchema,
    IAgentToolExecutor executor,
    Action<AgentRunEvent> reportEvent) : AIFunction
{
    public override string Name => definition.Name;

    public override string Description => definition.Description;

    public override JsonElement JsonSchema => jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var callId = Guid.NewGuid().ToString("N");
        reportEvent(new AgentToolExecutionStarted(callId, definition.Name));

        var call = new AgentToolCall(
            callId,
            definition.Name,
            JsonSerializer.Serialize(arguments, AIJsonUtilities.DefaultOptions));
        var result = await executor.ExecuteAsync(call, cancellationToken);

        reportEvent(new AgentToolExecutionCompleted(callId, definition.Name, result));
        return result;
    }
}
