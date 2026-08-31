using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public sealed class ManifestTransferReconciler(IManifestReader manifests) : ITransferReconciler
{
    public async Task<TransferReconciliationResult> ReconcileAsync(MigrationExecutionPlan plan, TransferResult transfer, CancellationToken cancellationToken = default)
    {
        var inspection = await manifests.InspectAsync(plan.ManifestPath, cancellationToken);
        if (inspection.State != ManifestReadState.Complete || inspection.Footer is null) return new(TransferCompletionState.Failed, 0, 0, 0, 0, 0, [inspection.Error ?? new(ErrorCategory.ConfigurationInvalid, "Manifest is not complete.")]);
        if (transfer.Cancelled) return new(TransferCompletionState.Interrupted, inspection.Footer.EligibleEntryCount, inspection.Footer.EligibleBytes, transfer.FilesTransferred, transfer.BytesTransferred, inspection.Footer.SkippedCount, transfer.Errors);
        var complete = transfer.Succeeded && transfer.FilesTransferred == inspection.Footer.EligibleEntryCount && transfer.BytesTransferred == inspection.Footer.EligibleBytes;
        IReadOnlyList<OperationError> errors = complete ? transfer.Errors : [.. transfer.Errors, new OperationError(ErrorCategory.ProcessFailure, "Transfer metadata did not reconcile with the approved manifest. Verification remains pending.")];
        return new(complete ? TransferCompletionState.TransferCompletedVerificationPending : TransferCompletionState.Failed, inspection.Footer.EligibleEntryCount, inspection.Footer.EligibleBytes, transfer.FilesTransferred, transfer.BytesTransferred, inspection.Footer.SkippedCount, errors);
    }
}
