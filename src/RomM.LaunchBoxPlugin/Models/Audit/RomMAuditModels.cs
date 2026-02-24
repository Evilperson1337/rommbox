using System;
using System.Collections.Generic;

namespace RomMbox.Models.Audit
{
    /// <summary>
    /// Options controlling audit behavior.
    /// </summary>
    internal sealed class RomMAuditOptions
    {
        public bool RematchMissingRommId { get; set; } = true;
        public bool RevalidateExistingMatches { get; set; } = false;
        public bool ForceFullRematch { get; set; } = false;
        public bool DryRun { get; set; } = false;
        public int MaxParallelism { get; set; } = 4;
        public int ApiDelayMs { get; set; } = 0;
    }

    /// <summary>
    /// Request for a platform-level audit.
    /// </summary>
    internal sealed class RomMAuditRequest
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string RommPlatformId { get; set; } = string.Empty;
        public string RommPlatformName { get; set; } = string.Empty;
        public string LaunchBoxPlatformName { get; set; } = string.Empty;
        public RomMAuditOptions Options { get; set; } = new RomMAuditOptions();
    }

    /// <summary>
    /// Progress payload reported during an audit.
    /// </summary>
    internal sealed class RomMAuditProgress
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public string CurrentGameTitle { get; set; } = string.Empty;
        public int TotalGames { get; set; }
        public int Processed { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Failed { get; set; }
        public int MissingMatches { get; set; }
        public double PercentComplete { get; set; }
    }

    /// <summary>
    /// Result of a platform audit.
    /// </summary>
    internal sealed class RomMAuditResult
    {
        public string CorrelationId { get; set; } = string.Empty;
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset CompletedUtc { get; set; }
        public bool Cancelled { get; set; }
        public bool Failed { get; set; }
        public string FailureMessage { get; set; } = string.Empty;
        public RomMAuditSummary Summary { get; set; } = new RomMAuditSummary();
        public List<RomMAuditGameResult> GameResults { get; set; } = new List<RomMAuditGameResult>();
    }

    /// <summary>
    /// Summary totals for a platform audit.
    /// </summary>
    internal sealed class RomMAuditSummary
    {
        public int TotalGames { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Failed { get; set; }
        public int MissingMatches { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Per-game audit outcome.
    /// </summary>
    internal sealed class RomMAuditGameResult
    {
        public string GameId { get; set; } = string.Empty;
        public string GameTitle { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public string OldRommId { get; set; } = string.Empty;
        public string NewRommId { get; set; } = string.Empty;
        public string MatchStrategy { get; set; } = string.Empty;
        public string MatchConfidence { get; set; } = string.Empty;
        public RomMAuditOutcome Outcome { get; set; } = RomMAuditOutcome.Unchanged;
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    internal enum RomMAuditOutcome
    {
        Updated,
        Unchanged,
        NotFound,
        Skipped,
        Failed
    }
}
