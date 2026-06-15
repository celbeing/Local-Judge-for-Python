using System;

namespace Local_Judge
{
    public sealed class ContestSessionService
    {
        public ContestContext? CurrentContest { get; private set; }

        public ContestProblemItem? CurrentProblem { get; private set; }

        public bool AutoExportCompleted { get; private set; }

        public bool AutoExportFailed { get; private set; }

        public bool IsAutoExporting { get; private set; }

        public bool IsAutoExportSuppressed { get; private set; }

        public bool IsInfoLockedUntilStart { get; private set; }

        public bool IsOpen => CurrentContest is not null;

        public void Open(ContestContext contest, bool suppressAutoExport = false)
        {
            CurrentContest = contest ?? throw new ArgumentNullException(nameof(contest));
            CurrentProblem = null;
            AutoExportCompleted = false;
            AutoExportFailed = false;
            IsAutoExporting = false;
            IsAutoExportSuppressed = suppressAutoExport;
            IsInfoLockedUntilStart = false;
        }

        public void Close()
        {
            CurrentContest = null;
            CurrentProblem = null;
            AutoExportCompleted = false;
            AutoExportFailed = false;
            IsAutoExporting = false;
            IsAutoExportSuppressed = false;
            IsInfoLockedUntilStart = false;
        }

        public void SelectProblem(ContestProblemItem problem)
        {
            CurrentProblem = problem ?? throw new ArgumentNullException(nameof(problem));
        }

        public void ClearProblem()
        {
            CurrentProblem = null;
        }

        public ContestSessionPhase GetPhase(DateTimeOffset now)
        {
            if (CurrentContest is null)
            {
                return ContestSessionPhase.None;
            }

            if (now < CurrentContest.StartsAt)
            {
                return ContestSessionPhase.BeforeStart;
            }

            return now <= CurrentContest.EndsAt
                ? ContestSessionPhase.Active
                : ContestSessionPhase.Ended;
        }

        public bool CanOpenProblem(DateTimeOffset now)
        {
            return CurrentContest is not null && now >= CurrentContest.StartsAt;
        }

        public bool IsActive(DateTimeOffset now)
        {
            return GetPhase(now) == ContestSessionPhase.Active;
        }

        public bool IsEnded(DateTimeOffset now)
        {
            return GetPhase(now) == ContestSessionPhase.Ended;
        }

        public void SetInfoLockUntilStart(bool lockUntilStart, DateTimeOffset now)
        {
            IsInfoLockedUntilStart = lockUntilStart || GetPhase(now) == ContestSessionPhase.BeforeStart;
        }

        public void ClearInfoLock()
        {
            IsInfoLockedUntilStart = false;
        }

        public void ReleaseInfoLockIfProblemOpenAllowed(DateTimeOffset now)
        {
            if (IsInfoLockedUntilStart && CanOpenProblem(now))
            {
                IsInfoLockedUntilStart = false;
            }
        }

        public bool ShouldAutoExport(DateTimeOffset now, bool isSubmitting, bool isRunnerRunning)
        {
            return CurrentContest is not null
                   && IsEnded(now)
                   && !AutoExportCompleted
                   && !AutoExportFailed
                   && !IsAutoExporting
                   && !IsAutoExportSuppressed
                   && !isSubmitting
                   && !isRunnerRunning;
        }

        public bool CanStartExport()
        {
            return CurrentContest is not null && !IsAutoExporting;
        }

        public void BeginExport()
        {
            IsAutoExporting = true;
        }

        public void CompleteExport(bool markAutoExportCompleted)
        {
            if (markAutoExportCompleted)
            {
                AutoExportCompleted = true;
                AutoExportFailed = false;
            }

            IsAutoExporting = false;
        }

        public void FailExport(bool markAutoExportFailed)
        {
            if (markAutoExportFailed)
            {
                AutoExportFailed = true;
            }

            IsAutoExporting = false;
        }
    }

    public enum ContestSessionPhase
    {
        None,
        BeforeStart,
        Active,
        Ended
    }
}
