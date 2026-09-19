public enum RunEndReason
{
    Extracted,
    Death
}

public enum RunEndPhase
{
    Inactive,
    Exploring,
    Settling,
    ShowingResult,
    Transitioning
}

public sealed class RunEndFlow
{
    public RunEndPhase Phase { get; private set; } = RunEndPhase.Inactive;
    public RunEndReason? EndReason { get; private set; }

    public bool Begin()
    {
        if (Phase != RunEndPhase.Inactive && Phase != RunEndPhase.Transitioning)
            return false;

        EndReason = null;
        Phase = RunEndPhase.Exploring;
        return true;
    }

    public bool TryBeginEnd(RunEndReason reason)
    {
        if (Phase != RunEndPhase.Exploring ||
            (reason != RunEndReason.Extracted && reason != RunEndReason.Death))
            return false;

        EndReason = reason;
        Phase = RunEndPhase.Settling;
        return true;
    }

    public bool ShowResult()
    {
        if (Phase != RunEndPhase.Settling) return false;
        Phase = RunEndPhase.ShowingResult;
        return true;
    }

    public bool TryBeginTransition()
    {
        if (Phase != RunEndPhase.ShowingResult) return false;
        Phase = RunEndPhase.Transitioning;
        return true;
    }

    public bool CancelTransition()
    {
        if (Phase != RunEndPhase.Transitioning) return false;
        Phase = RunEndPhase.ShowingResult;
        return true;
    }

    public bool FinishToHome()
    {
        if (Phase != RunEndPhase.Transitioning) return false;
        Reset();
        return true;
    }

    public void Reset()
    {
        Phase = RunEndPhase.Inactive;
        EndReason = null;
    }
}
