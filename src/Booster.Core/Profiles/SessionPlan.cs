using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Profiles;

/// <param name="Name">Имя exe без «.exe».</param>
public sealed record GameProcess(string Name, ProcessIdentity Identity);

/// <summary>Действие профиля с проверкой поддержки и решением политики на момент подготовки.</summary>
/// <param name="Mode">Режим из профиля; null — действие профилю неизвестно.</param>
public sealed record PlannedAction(IReversibleAction Action, ActionMode? Mode, PolicyContext Context, ActionSupport Support,
    PolicyDecision Decision)
{
    public bool WillApply => Support.IsSupported && Decision.Verdict == PolicyVerdict.Allow;

    /// <summary>Почему действие не выполнится: причина неподдержки или решение политики.</summary>
    public string SkipReason => Support.IsSupported ? EnumText.Format(Decision.Reason) : Support.Reason ?? ReportStrings.Skip_NotSupported;
}

/// <param name="Game">null — предварительный план до запуска игры.</param>
public sealed record SessionPlan(GamingProfile Profile, GameProcess? Game, ExecutionTrigger Trigger, IReadOnlyList<PlannedAction> Actions);

/// <param name="Load">Нагрузка системы на последнем шаге наблюдения; null — не замерялась.</param>
/// <param name="Sensors">Последний замер датчиков на этом шаге; null — не читались (collectGpuTelemetry выключена) или замер пропущен.</param>
/// <param name="Throttle">Признаки троттлинга по последним thresholds.throttle.sessionWindowSamples замерам сессии.</param>
/// <param name="Frames">Замер кадров начала сессии (collectFrameMetrics); null — вне Gaming или пока проверяется PresentMon.</param>
public sealed record SessionProgress(SessionState State, SessionRecord? Session, SystemLoad? Load, bool GameRunning,
    SensorSnapshot? Sensors = null, ThrottleAssessment? Throttle = null, SessionFrameSummary? Frames = null);
