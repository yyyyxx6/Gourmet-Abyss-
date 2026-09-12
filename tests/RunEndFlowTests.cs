using System;

public static class RunEndFlowTests
{
    public static void Run()
    {
        RejectsActionsBeforeExploration();
        FirstEndReasonWins();
        ResultMustBeReadyBeforeTransition();
        OnlyOneTransitionCanBegin();
        CanceledTransitionCanBeRetried();
        NewExplorationClearsPreviousReason();
        ReturningHomeEndsTheRun();
        ExplicitResetClearsEveryPhase();
        InvalidEndReasonDoesNotChangeState();
    }

    private static void RejectsActionsBeforeExploration()
    {
        var flow = new RunEndFlow();
        State(flow, RunEndPhase.Inactive, null);
        False(flow.TryBeginEnd(RunEndReason.Death), "An inactive run cannot end.");
        False(flow.ShowResult(), "An inactive run has no result.");
        False(flow.TryBeginTransition(), "An inactive run cannot start a result transition.");
        False(flow.CancelTransition(), "There is no inactive transition to cancel.");
        False(flow.FinishToHome(), "Returning home requires an accepted transition.");
        State(flow, RunEndPhase.Inactive, null);
    }

    private static void FirstEndReasonWins()
    {
        foreach (RunEndReason reason in new[] { RunEndReason.Extracted, RunEndReason.Death })
        {
            var flow = new RunEndFlow();
            True(flow.Begin(), "The first exploration begins.");
            False(flow.Begin(), "Duplicate Begin does not reset an active run.");
            State(flow, RunEndPhase.Exploring, null);
            True(flow.TryBeginEnd(reason), "The first end request succeeds.");
            False(flow.TryBeginEnd(RunEndReason.Death), "Repeated death cannot settle twice.");
            False(flow.TryBeginEnd(RunEndReason.Extracted), "Extraction cannot replace an accepted end request.");
            False(flow.Begin(), "Begin cannot discard a result being settled.");
            State(flow, RunEndPhase.Settling, reason);
        }
    }

    private static void ResultMustBeReadyBeforeTransition()
    {
        var flow = new RunEndFlow();
        flow.Begin();
        False(flow.TryBeginTransition(), "Result buttons do nothing during exploration.");
        False(flow.ShowResult(), "A result cannot appear before settlement.");
        False(flow.FinishToHome(), "Exploration cannot finish directly through the result button gate.");
        flow.TryBeginEnd(RunEndReason.Extracted);
        False(flow.TryBeginTransition(), "Result buttons do nothing while settlement is pending.");
        False(flow.CancelTransition(), "Settlement is not a transition.");
        False(flow.FinishToHome(), "Settlement cannot bypass result display.");
        State(flow, RunEndPhase.Settling, RunEndReason.Extracted);
        True(flow.ShowResult(), "The settled result can be displayed.");
        False(flow.ShowResult(), "A result cannot be shown twice.");
        False(flow.Begin(), "Exploration cannot bypass a shown result.");
        False(flow.FinishToHome(), "The shown result still requires a transition request.");
        State(flow, RunEndPhase.ShowingResult, RunEndReason.Extracted);
    }

    private static void OnlyOneTransitionCanBegin()
    {
        RunEndFlow flow = ShowingResult(RunEndReason.Death);
        True(flow.TryBeginTransition(), "The first result button acquires the transition.");
        False(flow.TryBeginTransition(), "A second button cannot acquire another transition.");
        False(flow.TryBeginEnd(RunEndReason.Extracted), "Ending again cannot interrupt a transition.");
        False(flow.ShowResult(), "The transition cannot be overwritten by a duplicate show request.");
        State(flow, RunEndPhase.Transitioning, RunEndReason.Death);
    }

    private static void CanceledTransitionCanBeRetried()
    {
        RunEndFlow flow = ShowingResult(RunEndReason.Death);
        flow.TryBeginTransition();
        True(flow.CancelTransition(), "Failed loading restores the shown result.");
        State(flow, RunEndPhase.ShowingResult, RunEndReason.Death);
        False(flow.CancelTransition(), "Canceling twice leaves the result state unchanged.");
        True(flow.TryBeginTransition(), "A result button can retry after cancellation.");
        State(flow, RunEndPhase.Transitioning, RunEndReason.Death);
    }

    private static void NewExplorationClearsPreviousReason()
    {
        RunEndFlow flow = ShowingResult(RunEndReason.Death);
        flow.TryBeginTransition();
        True(flow.Begin(), "A completed scene load can begin the next exploration.");
        State(flow, RunEndPhase.Exploring, null);
        False(flow.CancelTransition(), "The previous transition cannot cancel a new exploration.");
        False(flow.Begin(), "Duplicate Begin leaves the new exploration intact.");
        True(flow.TryBeginEnd(RunEndReason.Extracted), "The new exploration has its own end request.");
        State(flow, RunEndPhase.Settling, RunEndReason.Extracted);
    }

    private static void ReturningHomeEndsTheRun()
    {
        RunEndFlow flow = ShowingResult(RunEndReason.Extracted);
        flow.TryBeginTransition();
        True(flow.FinishToHome(), "The return-home transition can complete.");
        State(flow, RunEndPhase.Inactive, null);
        False(flow.FinishToHome(), "Completing home twice has no effect.");
        False(flow.TryBeginEnd(RunEndReason.Death), "Death cannot settle an already finished run.");
        False(flow.TryBeginEnd(RunEndReason.Extracted), "Extraction cannot settle an already finished run.");
        False(flow.TryBeginTransition(), "Result buttons cannot run after returning home.");
        True(flow.Begin(), "A later exploration can begin from home.");
        State(flow, RunEndPhase.Exploring, null);
    }

    private static void ExplicitResetClearsEveryPhase()
    {
        for (int phase = 0; phase < 5; phase++)
        {
            var flow = new RunEndFlow();
            if (phase >= 1) flow.Begin();
            if (phase >= 2) flow.TryBeginEnd(RunEndReason.Death);
            if (phase >= 3) flow.ShowResult();
            if (phase >= 4) flow.TryBeginTransition();
            flow.Reset();
            State(flow, RunEndPhase.Inactive, null);
            flow.Reset();
            State(flow, RunEndPhase.Inactive, null);
            True(flow.Begin(), "An explicitly reset flow can begin again.");
        }
    }

    private static void InvalidEndReasonDoesNotChangeState()
    {
        var flow = new RunEndFlow();
        flow.Begin();
        False(flow.TryBeginEnd((RunEndReason)(-1)), "Unknown negative reasons are rejected.");
        False(flow.TryBeginEnd((RunEndReason)2), "Unknown positive reasons are rejected.");
        State(flow, RunEndPhase.Exploring, null);
        True(flow.TryBeginEnd(RunEndReason.Death), "An invalid request does not block a later valid request.");
    }

    private static RunEndFlow ShowingResult(RunEndReason reason)
    {
        var flow = new RunEndFlow();
        True(flow.Begin(), "Test exploration starts.");
        True(flow.TryBeginEnd(reason), "Test settlement starts.");
        True(flow.ShowResult(), "Test result is ready.");
        return flow;
    }

    private static void State(RunEndFlow flow, RunEndPhase phase, RunEndReason? reason)
    {
        if (flow.Phase != phase || flow.EndReason != reason)
            throw new Exception("Expected phase " + phase + " and reason " + reason +
                "; actual phase " + flow.Phase + " and reason " + flow.EndReason + ".");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new Exception(message);
    }
}
