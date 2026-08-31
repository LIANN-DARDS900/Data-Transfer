using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public static class RobocopyArgumentPolicy
{
    public static IReadOnlyList<string> Build(MigrationExecutionRequest request)
    {
        if (request.Plan.ConflictPolicy is ConflictPolicy.KeepBoth or ConflictPolicy.ManualDecision)
            throw new InvalidOperationException("This conflict policy requires the controlled preparation layer before Robocopy execution.");
        if (request.Plan.ConflictPolicy == ConflictPolicy.Replace && (!request.Plan.ReplaceAuthorizedByPolicy || !request.Plan.DestructiveReplaceConfirmed))
            throw new InvalidOperationException("Replace requires policy authorization and explicit technician confirmation.");
        var arguments = new List<string> { request.SourceRoot, request.DestinationRoot, "/E", "/COPY:DAT", "/DCOPY:T", "/R:2", "/W:2", "/XJ", "/Z", "/NP", "/BYTES" };
        if (request.Plan.ConflictPolicy == ConflictPolicy.Skip) arguments.AddRange(["/XC", "/XN", "/XO"]);
        else if (request.Plan.ConflictPolicy == ConflictPolicy.ReplaceIfSourceNewer) arguments.Add("/XO");
        return arguments;
    }
}
