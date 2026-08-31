namespace RoboTransfer.Robocopy;

public enum RobocopyOutcome { CleanSuccess, Copied, ExtraOrMismatch, PartialNonfatal, Failure }
public sealed record RobocopyExitCode(int Code, RobocopyOutcome Outcome, bool Succeeded)
{
    public static RobocopyExitCode Interpret(int code) => code switch
    {
        < 0 => new(code, RobocopyOutcome.Failure, false),
        0 => new(code, RobocopyOutcome.CleanSuccess, true),
        1 => new(code, RobocopyOutcome.Copied, true),
        < 4 => new(code, RobocopyOutcome.ExtraOrMismatch, true),
        < 8 => new(code, RobocopyOutcome.PartialNonfatal, true),
        _ => new(code, RobocopyOutcome.Failure, false)
    };
}
