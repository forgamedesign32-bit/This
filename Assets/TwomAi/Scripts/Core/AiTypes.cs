using UnityEngine;

namespace TwomAi
{
    public enum NpcKind { Guard, Civilian }
    public enum GuardState { Patrol, Investigate, Search, Chase, Return, Dead, Retreat, Ambush, Escape }
    public enum CivilianState { Routine, Flee, LockDoor, Hide, CautiousExit, FleeFallback, Dead, Beg, Escape }
    public enum SquadRole { None, Pursue, Flank, GuardExit }
    public enum AlertStage { Normal, Suspicious, Searching, CombatReady }

    public readonly struct NoiseEvent
    {
        public readonly int EventId;
        public readonly Vector3 Position;
        public readonly float Loudness;
        public readonly float OccurredAt;
        public readonly int SourceId;

        public NoiseEvent(int eventId, Vector3 position, float loudness, float occurredAt, int sourceId)
        {
            EventId = eventId;
            Position = position;
            Loudness = loudness;
            OccurredAt = occurredAt;
            SourceId = sourceId;
        }
    }

    public readonly struct ThreatReport
    {
        public readonly int ReportId;
        public readonly int ReporterId;
        public readonly Vector3 LastKnownPosition;
        public readonly float Confidence;
        public readonly float ReportedAt;
        public readonly bool HasVisualContact;

        public ThreatReport(int reportId, int reporterId, Vector3 lastKnownPosition, float confidence, float reportedAt, bool hasVisualContact)
        {
            ReportId = reportId;
            ReporterId = reporterId;
            LastKnownPosition = lastKnownPosition;
            Confidence = confidence;
            ReportedAt = reportedAt;
            HasVisualContact = hasVisualContact;
        }
    }

    // Plain data bag owned by one brain (spec 3.2).
    public sealed class NpcBlackboard
    {
        public Vector3 LastKnownPosition;
        public bool HasLastKnownPosition;
        public float LastEvidenceTime = float.NegativeInfinity;
        public int LastHandledNoiseId;
        public int LastHandledReportId;
        public SquadRole AssignedRole;
    }

    public enum DefenseResult { None, Block, Dodge }

    public enum CharacterAction { Attack, AttackAlt, Kick, HitReact, Block, Dodge, Die, Fire, Stagger }

    public enum WeaponKind { Melee, Rifle }

    // Guard temperament (rolled per guard from its seed): flees when badly hurt, or fights to the end.
    public enum GuardTemperament { Fighter, Coward }

    // Lets an attacked character react (guards block/dodge only when aware of the attacker).
    public interface IDefender
    {
        DefenseResult Defend();
    }

    // Anything moved by NpcMover (guards, civilians, the click-controlled player).
    public interface IGraphAgent
    {
        int AgentId { get; }
        bool IsIncapacitated { get; }
    }

    public static class AiIds
    {
        public const int PlayerSourceId = 0;

        private static int _next;

        // Monotonic ids: a later event always has a larger id, which makes duplicate checks trivial.
        public static int Next() => ++_next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetIds() => _next = 0;
    }
}
