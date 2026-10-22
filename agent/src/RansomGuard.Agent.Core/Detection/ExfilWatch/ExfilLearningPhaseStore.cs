using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>Reads, and on the very first run writes, the instant EXFIL WATCH began learning.</summary>
public interface IExfilLearningPhaseStore
{
    /// <summary>
    /// The learning start of this endpoint: the stored one, or now when nothing is stored yet (which
    /// is then written). Survives restarts, which is the whole point (AGT-EXF-001).
    /// </summary>
    DateTime StartedAtOrBegin(int learningPhaseDays);
}

/// <inheritdoc />
public sealed class ExfilLearningPhaseStore : IExfilLearningPhaseStore
{
    private readonly AgentDbContext _db;
    private readonly ILogger<ExfilLearningPhaseStore> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates the store.</summary>
    public ExfilLearningPhaseStore(AgentDbContext db, ILogger<ExfilLearningPhaseStore> logger, TimeProvider? time = null)
    {
        _db = db;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public DateTime StartedAtOrBegin(int learningPhaseDays)
    {
        ExfilLearningPhase? stored = _db.ExfilLearningPhases.AsNoTracking()
            .OrderBy(p => p.StartedAt)
            .FirstOrDefault();
        if (stored is not null)
        {
            DateTime ends = stored.StartedAt.AddDays(learningPhaseDays);
            _logger.LogInformation(
                "EXFIL WATCH learning phase began {StartedAt:u} on this endpoint and ends {Ends:u}; a restart does not begin it again",
                stored.StartedAt, ends);
            return stored.StartedAt;
        }

        DateTime now = _time.GetUtcNow().UtcDateTime;
        _db.ExfilLearningPhases.Add(new ExfilLearningPhase { StartedAt = now, LearningPhaseDays = learningPhaseDays });
        _db.SaveChanges();
        _logger.LogWarning(
            "EXFIL WATCH begins its learning phase now ({StartedAt:u}, {Days} days): until it ends, volume anomalies are "
            + "not raised and the module reports degraded / learning_phase",
            now, learningPhaseDays);
        return now;
    }
}
