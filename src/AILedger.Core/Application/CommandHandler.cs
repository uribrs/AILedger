using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Application;

public sealed class CommandHandler : ICommandHandler
{
    private readonly ITaskReducer _reducer;
    private readonly AuthorizationPolicy _authorizationPolicy;

    public CommandHandler()
        : this(new TaskReducer(), new AuthorizationPolicy())
    {
    }

    public CommandHandler(ITaskReducer reducer, AuthorizationPolicy authorizationPolicy)
    {
        _reducer = reducer;
        _authorizationPolicy = authorizationPolicy;
    }

    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        ValidateEnvelope(command);
        ValidateEnums(command);

        var eventData = state is null
            ? TaskOpeningRules.HandleOpening(command, now)
            : HandleExisting(state, command, now);

        return ApplyEvents(state, command, eventData, now);
    }

    private IReadOnlyList<LedgerEventData> HandleExisting(
        GovernedTaskState state,
        LedgerCommand command,
        DateTimeOffset now)
    {
        if (command is OpenTaskCommand)
        {
            throw new GovernanceException("Task is already open.");
        }

        _authorizationPolicy.Authorize(state, command);

        return command switch
        {
            AssignRoleCommand assign => RoleAssignmentRules.AssignRole(state, assign, now),
            AddClaimCommand add => ClaimRules.AddClaim(state, add, now),
            ResolveClaimCommand resolve => ClaimRules.ResolveClaim(state, resolve),
            AddEvidenceCommand add => EvidenceRules.AddEvidence(state, add, now),
            ProposeDecisionCommand propose => DecisionRules.ProposeDecision(state, propose, now),
            ResolveDecisionCommand resolve => DecisionRules.ResolveDecision(state, resolve),
            RaiseChallengeCommand raise => ChallengeRules.RaiseChallenge(state, raise, now),
            DisposeChallengeCommand dispose => ChallengeRules.DisposeChallenge(state, dispose),
            AddWorkItemCommand add => WorkItemRules.AddWorkItem(state, add),
            StartRunCommand start => RunRules.StartRun(state, start, now),
            CompleteRunCommand complete => RunRules.CompleteRun(state, complete, now),
            RequestStageTransitionCommand transition => StageTransitionRules.TransitionStage(state, transition, now),
            RaiseEscalationCommand raise => EscalationRules.RaiseEscalation(state, raise, now),
            ResolveEscalationCommand resolve => EscalationRules.ResolveEscalation(state, resolve),
            RecordAlternativeCommand record => AlternativeRules.RecordAlternative(state, record, now),
            RecordArtifactCommand record => ArtifactRules.RecordArtifact(state, record, now),
            RecordContextBuiltCommand record => ContextRules.RecordContextBuilt(state, record),
            MarkLessonBearingCommand mark => LessonRules.MarkLessonBearing(state, mark, now),
            AddConstraintCommand add => ConstraintRules.AddConstraint(state, add, now),
            SupersedeConstraintCommand supersede => ConstraintRules.SupersedeConstraint(state, supersede),
            CompleteWorkItemCommand complete => WorkItemRules.CompleteWorkItem(state, complete),
            BlockWorkItemCommand block => WorkItemRules.BlockWorkItem(state, block),
            UnblockWorkItemCommand unblock => WorkItemRules.UnblockWorkItem(state, unblock),
            AbandonWorkItemCommand abandon => WorkItemRules.AbandonWorkItem(state, abandon),
            _ => throw new GovernanceException($"Unsupported command '{command.GetType().Name}'.")
        };
    }

    public static string HashLaunchToken(string token) =>
        RunRules.HashLaunchToken(token);

    private CommandOutcome ApplyEvents(
        GovernedTaskState? initialState,
        LedgerCommand command,
        IReadOnlyList<LedgerEventData> eventData,
        DateTimeOffset now)
    {
        var taskId = initialState?.TaskId ?? ((OpenTaskCommand)command).TaskId;
        var state = initialState;
        var events = new List<LedgerEvent>(eventData.Count);
        LedgerEvent? previous = null;

        for (var index = 0; index < eventData.Count; index++)
        {
            var @event = new LedgerEvent(
                GovernedTaskState.CurrentSchemaVersion,
                CreateEventId(taskId, state?.Version ?? 0),
                taskId,
                command.ActorId,
                now,
                previous?.EventId ?? command.CausationId,
                command.CorrelationId.Trim(),
                eventData[index]);

            state = _reducer.Apply(state, @event);
            events.Add(@event);
            previous = @event;
        }

        return new CommandOutcome(state!, events);
    }

    private static EventId CreateEventId(TaskId taskId, long currentVersion) =>
        new($"{taskId.Value}:{currentVersion + 1:D10}");

    private static void ValidateEnvelope(LedgerCommand command)
    {
        RequireId(command.ActorId.Value, nameof(command.ActorId));
        RequireText(command.CorrelationId, nameof(command.CorrelationId));
        if (command.CausationId is { } causationId)
        {
            RequireId(causationId.Value, nameof(command.CausationId));
        }
    }

    private static void ValidateEnums(LedgerCommand command)
    {
        switch (command)
        {
            case AssignRoleCommand assign:
                RequireDefined(assign.Role, nameof(assign.Role));
                foreach (var capability in assign.Capabilities)
                {
                    RequireDefined(capability, nameof(assign.Capabilities));
                }
                break;
            case ResolveClaimCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case ResolveDecisionCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case DisposeChallengeCommand dispose:
                RequireDefined(dispose.Status, nameof(dispose.Status));
                break;
            case CompleteRunCommand complete:
                RequireDefined(complete.Status, nameof(complete.Status));
                break;
            case RequestStageTransitionCommand transition:
                RequireDefined(transition.TargetStage, nameof(transition.TargetStage));
                break;
            case RaiseEscalationCommand raise:
                RequireDefined(raise.Kind, nameof(raise.Kind));
                break;
            case ResolveEscalationCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case MarkLessonBearingCommand mark:
                RequireDefined(mark.SourceKind, nameof(mark.SourceKind));
                if (mark.Class is not { } lessonClass)
                {
                    throw new GovernanceException("A lesson mark must specify its lesson class.");
                }
                RequireDefined(lessonClass, nameof(mark.Class));
                break;
            case RecordArtifactCommand record:
                RequireDefined(record.Kind, nameof(record.Kind));
                break;
        }
    }

    internal static void RequireDefined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new GovernanceException($"'{value}' is not a defined {field} value.");
        }
    }

    internal static TValue Get<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (!values.TryGetValue(id, out var value))
        {
            throw new GovernanceException($"Unknown {kind} '{id}'.");
        }

        return value;
    }

    internal static void EnsureNew<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (values.ContainsKey(id))
        {
            throw new GovernanceException($"A {kind} with ID '{id}' already exists.");
        }
    }

    internal static void EnsureReferencesExist<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        IEnumerable<TKey> ids,
        string kind)
        where TKey : notnull
    {
        foreach (var id in ids)
        {
            _ = Get(values, id, kind);
        }
    }

    internal static void EnsureUnique<T>(IReadOnlyList<T> values, string label, IEqualityComparer<T>? comparer = null)
    {
        if (values.Count != values.Distinct(comparer).Count())
        {
            throw new GovernanceException($"{label} cannot contain duplicates.");
        }
    }

    internal static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GovernanceException($"{name} is required.");
        }
    }

    internal static void RequireId(string value, string name) => RequireText(value, name);

    internal static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
