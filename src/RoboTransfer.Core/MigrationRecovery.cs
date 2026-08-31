namespace RoboTransfer.Core;

public sealed class MigrationRecovery(IMigrationJournal journal, IDestinationValidator destinationValidator, IManifestReader? manifestReader = null) : IMigrationRecovery
{
    public IAsyncEnumerable<MigrationSession> DiscoverAsync(CancellationToken cancellationToken = default) => journal.FindIncompleteAsync(cancellationToken);

    public async Task<DestinationValidationResult> ValidateResumeAsync(MigrationSession session, MigrationExecutionPlan plan, DestinationValidationContext destination, CancellationToken cancellationToken = default)
    {
        var errors = new List<OperationError>();
        if (session.Id != plan.SessionId || session.ManifestReference != plan.ManifestPath) errors.Add(new(ErrorCategory.ConfigurationInvalid, "Session or manifest identity changed. Prepare a new migration."));
        if (session.Source?.MachineName != plan.SourceMachineIdentity || session.Source?.ProfileId != plan.SourceProfileIdentity) errors.Add(new(ErrorCategory.ConfigurationInvalid, "Source machine or profile identity changed. Resume is blocked."));
        if (!File.Exists(plan.ManifestPath)) errors.Add(new(ErrorCategory.ConfigurationInvalid, "The reviewed manifest is unavailable. Scan again."));
        else if (manifestReader is not null && (await manifestReader.InspectAsync(plan.ManifestPath, cancellationToken)).State != ManifestReadState.Complete) errors.Add(new(ErrorCategory.ConfigurationInvalid, "The reviewed manifest is incomplete or corrupt. Scan again."));
        if (!File.Exists(plan.RobocopyExecutablePath)) errors.Add(new(ErrorCategory.ToolUnavailable, "The reviewed Robocopy executable is unavailable."));
        if (destination.CurrentRobocopy is not null && (!string.Equals(destination.CurrentRobocopy.ExecutablePath, plan.RobocopyExecutablePath, StringComparison.OrdinalIgnoreCase) || destination.CurrentRobocopy.Version != plan.RobocopyVersion)) errors.Add(new(ErrorCategory.ToolUnavailable, "Robocopy path or version changed. Prepare a new migration."));
        if (plan.PolicyFingerprint != PolicyFingerprint.Create(destination.Policy)) errors.Add(new(ErrorCategory.PolicyForbidden, "Policy changed. Prepare and approve a new execution plan."));
        var validated = await destinationValidator.ValidateAsync(destination, cancellationToken);
        errors.AddRange(validated.Errors);
        return new(errors.Count == 0, errors);
    }

    public Task AbandonAsync(MigrationSession session, CancellationToken cancellationToken = default) => journal.SaveAsync(session with { Status = MigrationStatus.Abandoned, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
}
