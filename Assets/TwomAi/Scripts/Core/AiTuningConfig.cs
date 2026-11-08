using UnityEngine;

namespace TwomAi
{
    // All tuning values are prototype proposals (spec section 8), not official game values.
    [CreateAssetMenu(menuName = "TWOM AI/Tuning Config", fileName = "AiTuningConfig")]
    public sealed class AiTuningConfig : ScriptableObject
    {
        [field: Header("Feature Toggles (EnhancedMode only)")]
        [field: SerializeField] public bool EnableF1 { get; private set; } = true;
        [field: SerializeField] public bool EnableF2 { get; private set; } = true;
        [field: SerializeField] public bool EnableF3 { get; private set; } = true;
        [field: SerializeField] public int BaseRandomSeed { get; private set; } = 10;

        [field: Header("Vision")]
        [field: SerializeField] public float ViewDistance { get; private set; } = 6f;
        [field: SerializeField] public float ViewAngle { get; private set; } = 110f;
        [field: SerializeField] public float VisionDetectHold { get; private set; } = 0.4f;
        [field: SerializeField] public float SenseInterval { get; private set; } = 0.2f;
        [field: SerializeField] public float CloseSenseRadius { get; private set; } = 1.2f;

        [field: Header("Hearing")]
        [field: SerializeField] public float HearingRadius { get; private set; } = 8f;
        [field: SerializeField] public float WalkNoise { get; private set; } = 0.35f;
        [field: SerializeField] public float RunNoise { get; private set; } = 0.8f;
        [field: SerializeField] public float DoorNoise { get; private set; } = 0.6f;
        [field: SerializeField] public float AttackNoise { get; private set; } = 0.5f;
        [field: SerializeField] public float GunshotNoise { get; private set; } = 1.6f;
        [field: SerializeField] public float RunSpeedThreshold { get; private set; } = 3.5f;
        [field: SerializeField] public float HearingThreshold { get; private set; } = 0.25f;
        [field: SerializeField] public float WallNoiseMultiplier { get; private set; } = 0.6f;

        [field: Header("Alert")]
        [field: SerializeField] public float AlertOnNoise { get; private set; } = 20f;
        [field: SerializeField] public float AlertOnVisual { get; private set; } = 55f;
        [field: SerializeField] public float AlertOnCorpse { get; private set; } = 35f;
        [field: SerializeField] public float AlertOnVisualReport { get; private set; } = 30f;
        [field: SerializeField] public float AlertDecayDelay { get; private set; } = 5f;
        [field: SerializeField] public float AlertDecayPerSecond { get; private set; } = 6f;
        [field: SerializeField] public float StageSuspicious { get; private set; } = 20f;
        [field: SerializeField] public float StageSearching { get; private set; } = 50f;
        [field: SerializeField] public float StageCombatReady { get; private set; } = 80f;
        [field: SerializeField] public float StageHysteresis { get; private set; } = 5f;

        [field: Header("Guard Timing")]
        [field: SerializeField] public float MinNormalStateDuration { get; private set; } = 2f;
        [field: SerializeField] public float InvestigateDuration { get; private set; } = 6f;
        [field: SerializeField] public float InvestigateHesitationMax { get; private set; } = 0.8f;
        [field: SerializeField] public float SearchDuration { get; private set; } = 12f;
        [field: SerializeField] public float SearchRadius { get; private set; } = 6f;
        [field: SerializeField] public int SearchPointsPerGuard { get; private set; } = 3;

        [field: Header("F1 Patrol")]
        [field: SerializeField] public float PatrolPrimaryWeight { get; private set; } = 0.65f;
        [field: SerializeField] public float PatrolAlternativeWeight { get; private set; } = 0.35f;
        [field: SerializeField] public float RepeatNodePenalty { get; private set; } = 0.5f;
        [field: SerializeField] public float PatrolWaitMin { get; private set; } = 1f;
        [field: SerializeField] public float PatrolWaitMax { get; private set; } = 3f;
        [field: SerializeField] public float LegacyPatrolWait { get; private set; } = 2f;

        [field: Header("F2 Squad")]
        [field: SerializeField] public float CommunicationRange { get; private set; } = 8f;
        [field: SerializeField] public float ReportDelay { get; private set; } = 0.5f;
        // Alarm yell on first sighting: heard through walls and floors within this range.
        [field: SerializeField] public float ShoutRange { get; private set; } = 16f;
        [field: SerializeField] public float ShoutNoise { get; private set; } = 1.4f;
        [field: SerializeField] public float ReportTTL { get; private set; } = 8f;
        [field: SerializeField] public float ReportRefreshInterval { get; private set; } = 2f;
        [field: SerializeField] public float StrongNoiseReportThreshold { get; private set; } = 0.5f;
        [field: SerializeField] public int MaxAssignedPursuers { get; private set; } = 2;
        [field: SerializeField] public float SearchSlotSpacing { get; private set; } = 2f;

        [field: Header("F3 Civilian")]
        [field: SerializeField] public float CivilianSafeDuration { get; private set; } = 10f;
        [field: SerializeField] public float CivilianDecisionCooldown { get; private set; } = 1.5f;
        [field: SerializeField] public float CivilianOccupiedPenalty { get; private set; } = 15f;
        [field: SerializeField] public float CivilianNoLockPenalty { get; private set; } = 10f;
        [field: SerializeField] public float PathBlockedTimeout { get; private set; } = 2f;
        [field: SerializeField] public int MaxFailures { get; private set; } = 2;
    }
}
