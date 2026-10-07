using AILedger.Core.Authority;
using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService : IAgentSessionServiceFactory
{
    public IGovernedTaskService BindPreparation(PreparationAuthority authority) =>
        CopyService(new PreparationCommandHandler(authority, _commandHandler));

    public IGovernedTaskService BindRoutineOrchestration(RoutineOrchestrationAuthority authority) =>
        CopyService(new RoutineOrchestrationCommandHandler(authority, _commandHandler));

    public IGovernedTaskService BindAgentSession(AgentSessionAuthority authority)
    {
        var service = CopyService(new AgentSessionCommandHandler(authority, _commandHandler));
        service._agentAuthority = authority;
        return service;
    }
}
