using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoboTransfer.Core;

public static class PolicyFingerprint
{
    public static string Create(PolicyProfile policy) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy)));
}

public static class ExecutionPlanFingerprint
{
    public static string Create(MigrationExecutionPlan plan)
    {
        var snapshot = plan with { ApplicationVersion = plan.ApplicationVersion };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot))));
    }
}

public static class ConflictResolver
{
    public static string GetKeepBothPath(string desiredPath, Func<string, bool> exists)
    {
        if (!exists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        for (var suffix = 1; ; suffix++)
        {
            var label = suffix == 1 ? " (RoboTransfer copy)" : $" (RoboTransfer copy {suffix})";
            var candidate = Path.Combine(directory, name + label + extension);
            if (!exists(candidate)) return candidate;
        }
    }

    public static bool CanReplace(ConflictPolicy policy, bool policyAllowsReplace, bool technicianConfirmed) =>
        policy != ConflictPolicy.Replace || (policyAllowsReplace && technicianConfirmed);
}
