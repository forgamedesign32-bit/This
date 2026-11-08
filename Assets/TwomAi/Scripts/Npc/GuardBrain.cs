using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Armed guard FSM: Patrol -> Investigate -> Search -> Chase -> Return -> Patrol (spec 7.1).
    // Priority: Dead > direct sighting > team report > noise > current role > return/patrol.
    [RequireComponent(typeof(NpcActor), typeof(PerceptionSensor), typeof(NpcMover))]
    public sealed class GuardBrain : MonoBehaviour, IDefender
    {
        private const float SuspicionReportCooldown = 4f;
        private const float HiddenDiscoverDelay = 1f;
        private const float HiddenDiscoverRadius = 1.1f;
        private const float AttackImpactDelay = 0.35f;
        private const float DodgeChance = 0.2f;
        private const float BlockChance = 0.25f;

        private static readonly string[] NoiseLines = { "누구야?", "무슨 소리지?", "거기 누구 있어?", "...방금 뭐였지?" };
        private static readonly string[] SightLines = { "거기 서!", "저기 있다!", "놓치지 마!" };
        private static readonly string[] ShoutLines = { "침입자다! 모두 일어나!", "도둑이다! 여기야, 여기!", "누가 들어왔다! 다들 모여!" };
        private const float ShoutCooldown = 20f;
        private const float FaintNoiseLevel = 0.35f;
        private static readonly string[] LostLines = { "어디로 갔지?", "놓쳤다...", "숨어봤자 소용없어!" };
        private static readonly string[] CalmLines = { "별일 아니군.", "기분 탓인가...", "쥐새끼였나." };
        private static readonly string[] AttackLines = { "죽어라!", "받아라!", "이 자식이!" };
        private static readonly string[] CorpseLines = { "누가 쓰러졌어!", "젠장, 당했잖아!" };
        private static readonly string[] HitLines = { "어디서 쏘는 거야!", "저쪽이다!", "윽, 맞았다!" };
        private static readonly string[] RetreatLines = { "젠장, 물러나야 해!", "피가... 일단 숨자!" };
        private static readonly string[] AmbushLines = { "들어오기만 해 봐라...", "문 열고 들어오면 끝이다." };

        private const float AmbushDuration = 25f;

        [SerializeField] private List<Waypoint> _patrolRoute = new List<Waypoint>();
        [SerializeField] private List<Waypoint> _patrolZoneExtras = new List<Waypoint>();
        [Tooltip("Node outside the building used when a coward guard runs away.")]
        [SerializeField] private Waypoint _escapeNode;
        [SerializeField] private float _patrolSpeed = 1.6f;
        [SerializeField] private float _searchSpeed = 2.4f;
        [SerializeField] private float _chaseSpeed = 3.8f;
        [SerializeField] private float _attackRange = 1.6f;
        [SerializeField] private float _attackDamage = 20f;
        [SerializeField] private float _attackCooldown = 1.2f;

        [Header("Rifle (same aim-gauge rules as the player)")]
        [SerializeField] private bool _hasRifle = true;
        [SerializeField] private WeaponMount _rifleModel;
        [SerializeField] private float _shootMinDistance = 2.5f;
        [SerializeField] private float _shootRange = 8f;
        [SerializeField] private float _aimFillPerSecond = 0.45f;
        [SerializeField] private float _shotMinDamage = 8f;
        [SerializeField] private float _shotMaxDamage = 30f;
        [SerializeField] private float _fireCooldown = 1f;

        private readonly NpcBlackboard _blackboard = new NpcBlackboard();
        private readonly HashSet<int> _handledReports = new HashSet<int>();
        private readonly List<Vector3> _searchPoints = new List<Vector3>();
        private readonly List<Waypoint> _candidateNodes = new List<Waypoint>();

        private NpcActor _actor;
        private PerceptionSensor _sensor;
        private NpcMover _mover;
        private AiDirector _director;
        private AiTuningConfig _config;
        private WaypointGraph _graph;
        private AlertMeter _alert;
        private PatrolSelector _patrol;
        private System.Random _random;

        private GuardState _state;
        private float _stateEnteredAt;
        private Waypoint _patrolNode;
        private bool _isWaiting;
        private float _waitUntil;
        private float _lookDirection = 1f;

        private Vector3 _investigateTarget;
        private float _investigateStartAt;
        private float _investigateDeadline;
        private bool _investigateMoving;

        private int _searchIndex;
        private float _searchDeadline;
        private bool _isLooking;
        private float _lookUntil;
        private float _lookStartedAt;
        private bool _holdPosition;
        private bool _searchFromReport;
        private float _reportReceivedAt;
        private float _reportExpiresAt;

        private float _nextAttackTime;
        private float _impactAt = float.PositiveInfinity;
        private int _attackCount;
        private int _barkIndex;
        private System.Random _combatRandom;
        private CharacterAnimator _animator;
        private float _aimGauge;
        private float _fireThreshold = 0.7f;
        private float _nextFireTime;
        private float _shotDamageGauge;
        private bool _shotHit;
        private float _nextShoutTime;
        private float _shotImpactAt = float.PositiveInfinity;
        private float _shotDamage;
        private float _nextReportTime;
        private float _nextSuspicionReportTime;
        private float _nextRepathTime;
        private SafeSpot _retreatSpot;
        private bool _hasFled;
        private float _escapeRetryAt;
        private bool _retreatLocks;
        private float _ambushUntil;
        private int _lockAttempts;

        public NpcActor Actor => _actor;
        public GuardState State => _state;
        public SquadRole Role => _blackboard.AssignedRole;
        public NpcBlackboard Blackboard => _blackboard;
        public AlertMeter Alert => _alert;
        public int Seed { get; private set; }
        public GuardTemperament Temperament { get; private set; }
        public bool IsSearching => _state == GuardState.Search;
        public IReadOnlyList<Vector3> SearchPoints => _searchPoints;
        public Vector3 CurrentSearchTarget => _searchIndex < _searchPoints.Count ? _searchPoints[_searchIndex] : transform.position;

        private bool UseF1 => _director.IsF1Active;
        private bool UseF2 => _director.IsF2Active;

        private void Awake()
        {
            _actor = GetComponent<NpcActor>();
            _sensor = GetComponent<PerceptionSensor>();
            _mover = GetComponent<NpcMover>();
            _animator = GetComponent<CharacterAnimator>();
        }

        private void Start()
        {
            _director = AiDirector.Instance;
            _config = _director.Config;
            _graph = WaypointGraph.Instance;
            Seed = _director.GetNpcSeed(_actor.Id);
            _random = new System.Random(Seed);
            // Separate stream so combat rolls never shift patrol/search decisions.
            _combatRandom = new System.Random(Seed ^ 0x5F3759DF);
            Temperament = _combatRandom.NextDouble() < 0.5d ? GuardTemperament.Coward : GuardTemperament.Fighter;
            _alert = new AlertMeter(_config);
            _patrol = new PatrolSelector(_random, _config, _patrolRoute, _patrolZoneExtras);

            _sensor.ContactStarted += OnContactStarted;
            _sensor.ContactUpdated += OnContactUpdated;
            _sensor.ContactLost += OnContactLost;
            _sensor.NoiseHeard += OnNoiseHeard;
            _sensor.CorpseFound += OnCorpseFound;
            _actor.Died += OnDied;
            _actor.Damaged += OnDamaged;

            // Armed guards always hold the rifle stance (both hands on the gun).
            if (_hasRifle && _animator != null)
                _animator.SetAim(true);

            if (SquadCoordinator.Instance != null)
                SquadCoordinator.Instance.Register(this);

            EnterPatrol();
        }

        private void OnDestroy()
        {
            if (_sensor != null)
            {
                _sensor.ContactStarted -= OnContactStarted;
                _sensor.ContactUpdated -= OnContactUpdated;
                _sensor.ContactLost -= OnContactLost;
                _sensor.NoiseHeard -= OnNoiseHeard;
                _sensor.CorpseFound -= OnCorpseFound;
            }

            if (_actor != null)
            {
                _actor.Died -= OnDied;
                _actor.Damaged -= OnDamaged;
            }
        }

        private void Update()
        {
            if (_state == GuardState.Dead || _alert == null)
                return;

            float now = Time.time;
            _alert.Tick(Time.deltaTime, now);
            switch (_state)
            {
                case GuardState.Patrol: UpdatePatrol(now); break;
                case GuardState.Investigate: UpdateInvestigate(now); break;
                case GuardState.Search: UpdateSearch(now); break;
                case GuardState.Chase: UpdateChase(now); break;
                case GuardState.Return: UpdateReturn(now); break;
                case GuardState.Retreat: UpdateRetreat(now); break;
                case GuardState.Ambush: UpdateAmbush(now); break;
                case GuardState.Escape: UpdateEscape(now); break;
            }
        }

        // ---------------- State control ----------------

        private bool TrySetState(GuardState next, bool force)
        {
            if (!force && Time.time - _stateEnteredAt < _config.MinNormalStateDuration)
                return false;

            if (_state != next)
                AiLog.Add($"{name}: {_state} -> {next}");

            _state = next;
            _stateEnteredAt = Time.time;
            _actor.IsAlertedToPlayer = next == GuardState.Chase;
            _impactAt = float.PositiveInfinity;
            _shotImpactAt = float.PositiveInfinity;
            _aimGauge = 0f;
            _isWaiting = false;
            _isLooking = false;
            return true;
        }

        private void BeginWait(float duration)
        {
            _isWaiting = true;
            _waitUntil = Time.time + duration;
            _lookDirection = _random.NextDouble() < 0.5d ? -1f : 1f;
        }

        private void LookAround(float speedScale)
        {
            transform.Rotate(0f, 70f * _lookDirection * speedScale * Time.deltaTime, 0f);
        }

        // ---------------- Patrol (F1-1) ----------------

        private void EnterPatrol()
        {
            TrySetState(GuardState.Patrol, true);
            _mover.SetSpeed(_patrolSpeed);
            _blackboard.AssignedRole = SquadRole.None;
            _patrolNode = _patrol.GetRouteEntry(transform.position);
            if (_patrolNode == null || !_mover.SetDestination(_patrolNode.Position))
                BeginWait(1f);
        }

        private void UpdatePatrol(float now)
        {
            if (_mover.IsBlocked)
            {
                _mover.Stop();
                _patrolNode = _patrol.GetRouteEntry(transform.position);
                BeginWait(0.5f);
                return;
            }

            if (_mover.IsMoving)
            {
                if (UseF1)
                    _mover.SetSpeed(_alert.Stage >= AlertStage.Suspicious ? _patrolSpeed * 1.25f : _patrolSpeed);
                return;
            }

            if (!_isWaiting)
            {
                // Still uneasy (alert not decayed): shorter stops, more looking around.
                float wait = _patrol.NextWaitTime(UseF1);
                if (UseF1 && _alert.Stage >= AlertStage.Suspicious)
                    wait *= 0.6f;
                BeginWait(wait);
                return;
            }

            if (now < _waitUntil)
            {
                LookAround(UseF1 && _alert.Stage >= AlertStage.Suspicious ? 0.8f : 0.35f);
                return;
            }

            _isWaiting = false;
            var next = _patrolNode != null ? _patrol.SelectNext(_patrolNode, _graph, UseF1) : null;
            if (next == null)
            {
                // No valid candidate: re-evaluate from the nearest route node.
                _patrolNode = _patrol.GetRouteEntry(transform.position);
                if (_patrolNode == null || !_mover.SetDestination(_patrolNode.Position))
                    BeginWait(1f);
                return;
            }

            _patrolNode = next;
            if (!_mover.SetDestination(next.Position))
                BeginWait(0.5f);
        }

        // ---------------- Investigate (F1-3) ----------------

        private void StartInvestigate(Vector3 position)
        {
            TrySetState(GuardState.Investigate, true);
            _mover.SetSpeed(_searchSpeed);
            _mover.Stop();
            _investigateTarget = position;
            float hesitation = UseF1 ? (float)_random.NextDouble() * _config.InvestigateHesitationMax : 0f;
            _investigateStartAt = Time.time + hesitation;
            _investigateDeadline = _investigateStartAt + _config.InvestigateDuration;
            _investigateMoving = false;
            Bark(NoiseLines);
        }

        private void UpdateInvestigate(float now)
        {
            if (now < _investigateStartAt)
            {
                _mover.FaceTowards(_investigateTarget);
                return;
            }

            if (!_investigateMoving)
            {
                _investigateMoving = true;
                if (!_mover.SetDestination(_investigateTarget))
                    StartSearch(_investigateTarget, null, false);
                return;
            }

            if (_mover.HasArrived || _mover.IsBlocked || now >= _investigateDeadline)
                StartSearch(_investigateTarget, null, false);
        }

        // ---------------- Search (F1-4, F2-3) ----------------

        private void StartSearch(Vector3 center, Vector3? slot, bool holdPosition)
        {
            TrySetState(GuardState.Search, true);
            _mover.SetSpeed(_searchSpeed);
            _holdPosition = holdPosition;
            _searchPoints.Clear();
            if (slot.HasValue)
                _searchPoints.Add(slot.Value);

            if (!holdPosition)
            {
                bool includeCenter = !slot.HasValue || _blackboard.AssignedRole == SquadRole.Pursue;
                BuildSearchPoints(center, includeCenter);
            }

            if (_searchPoints.Count == 0)
                _searchPoints.Add(center);

            _searchIndex = 0;
            _searchDeadline = Time.time + _config.SearchDuration;
            MoveToSearchPoint();
        }

        private void BuildSearchPoints(Vector3 center, bool includeCenter)
        {
            if (includeCenter)
                _searchPoints.Add(center);

            int firstShuffled = _searchPoints.Count;
            var coordinator = SquadCoordinator.Instance;
            _candidateNodes.Clear();
            var nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                float distance = AiMath.Distance(center, node.Position);
                if (distance < 0.5f || distance > _config.SearchRadius)
                    continue;

                if (coordinator != null && coordinator.IsSlotTaken(node.Position, this, null))
                    continue;

                _candidateNodes.Add(node);
            }

            _candidateNodes.Sort((a, b) =>
                AiMath.Distance(center, a.Position).CompareTo(AiMath.Distance(center, b.Position)));

            int take = Mathf.Min(_config.SearchPointsPerGuard, _candidateNodes.Count);
            for (int i = 0; i < take; i++)
                _searchPoints.Add(_candidateNodes[i].Position);

            if (!UseF1)
                return;

            // F1: vary visiting order of the surrounding points.
            for (int i = _searchPoints.Count - 1; i > firstShuffled; i--)
            {
                int j = _random.Next(firstShuffled, i + 1);
                (_searchPoints[i], _searchPoints[j]) = (_searchPoints[j], _searchPoints[i]);
            }
        }

        private void MoveToSearchPoint()
        {
            for (int attempt = 0; attempt < _searchPoints.Count; attempt++)
            {
                if (_mover.SetDestination(_searchPoints[_searchIndex]))
                    return;

                _searchIndex = (_searchIndex + 1) % _searchPoints.Count;
            }

            // Every point unreachable: look around in place until the deadline.
            _mover.Stop();
            _isLooking = true;
            _lookUntil = Time.time + 1f;
        }

        private void UpdateSearch(float now)
        {
            if (now >= _searchDeadline)
            {
                BeginReturn();
                return;
            }

            if (_searchFromReport && now >= _reportExpiresAt)
            {
                _searchFromReport = false;
                bool ownEvidenceSinceReport = _blackboard.LastEvidenceTime > _reportReceivedAt;
                if (!ownEvidenceSinceReport && _alert.Stage < AlertStage.Searching)
                {
                    AiLog.Add($"{name}: report expired, no own evidence");
                    BeginReturn();
                    return;
                }
            }

            if (_isLooking)
            {
                if (TryDiscoverHiddenPlayer(now))
                    return;

                if (now < _lookUntil)
                {
                    LookAround(1f);
                    return;
                }

                _isLooking = false;
                if (!_holdPosition)
                    _searchIndex = (_searchIndex + 1) % _searchPoints.Count;

                MoveToSearchPoint();
                return;
            }

            if (_mover.IsMoving)
                return;

            _isLooking = true;
            _lookStartedAt = now;
            _lookUntil = now + (UseF1 ? Mathf.Lerp(1f, 2.5f, (float)_random.NextDouble()) : 1.5f);
            _lookDirection = _random.NextDouble() < 0.5d ? -1f : 1f;
        }

        private static bool IsFromGuard(int sourceId)
        {
            var actors = NpcActor.All;
            for (int i = 0; i < actors.Count; i++)
            {
                if (actors[i].Id == sourceId)
                    return actors[i].Kind == NpcKind.Guard;
            }

            return false;
        }

        // ---------------- Damage reaction ----------------

        // Hit by the player: rush the attacker's position; a single blow taking health to 50% or below
        // makes the guard fall back to a room with a door, then lock it or wait beside it to strike first.
        private void OnDamaged(float before, Vector3 from)
        {
            if (_state == GuardState.Dead)
                return;

            if (_state == GuardState.Escape)
                return;

            float now = Time.time;
            RecordEvidence(from, now);
            _alert.Add(_config.AlertOnVisual, now);

            // Coward: once health drops to half or below, flee (lock itself in a room, or leave the map).
            // Fighter: keeps fighting whatever its health.
            if (Temperament == GuardTemperament.Coward && !_hasFled && _actor.Health01 <= 0.5f)
            {
                _hasFled = true;
                if (!StartRetreat(from))
                    StartEscape();
                return;
            }

            if (_state == GuardState.Chase || _state == GuardState.Retreat || _state == GuardState.Ambush)
                return;

            Bark(HitLines);
            _blackboard.AssignedRole = SquadRole.Pursue;
            StartSearch(from, null, false);
            _mover.SetSpeed(_chaseSpeed);
            SubmitReportAt(from, false, 0.9f);
        }

        private bool StartRetreat(Vector3 threat)
        {
            var start = _graph.FindNearest(transform.position, true);
            if (start == null)
                return false;

            SafeSpot best = null;
            float bestCost = float.MaxValue;
            var spots = SafeSpot.All;
            var path = _candidateNodes;
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (spot.Door == null || spot.Door.IsLocked || spot.InsideNode == null)
                    continue;

                if (!_graph.TryFindPath(start, spot.InsideNode, path))
                    continue;

                float cost = WaypointGraph.PathLength(path);
                // Do not run toward the attacker.
                if (AiMath.Distance(spot.InsideNode.Position, threat) < AiMath.Distance(transform.position, threat))
                    cost += 30f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = spot;
                }
            }

            if (best == null)
                return false;

            TrySetState(GuardState.Retreat, true);
            _retreatSpot = best;
            _retreatLocks = true; // cowards barricade themselves
            _lockAttempts = 0;
            _mover.SetSpeed(_chaseSpeed);
            Bark(RetreatLines);
            AiLog.Add($"{name} retreats to {best.name} ({(_retreatLocks ? "lock" : "ambush")})");
            if (!_mover.SetDestination(best.InsideNode.Position))
            {
                StartSearch(threat, null, false);
                return true;
            }

            return true;
        }

        private void UpdateRetreat(float now)
        {
            if (_mover.IsBlocked)
            {
                StartSearch(_blackboard.LastKnownPosition, null, false);
                return;
            }

            if (_mover.IsMoving)
                return;

            var door = _retreatSpot.Door;
            if (_retreatLocks)
            {
                _mover.FaceTowards(door.transform.position);
                if (!(door.TryClose(_actor.Id, _config.DoorNoise * 0.5f) && door.TryLock(_actor.Id)))
                {
                    if (++_lockAttempts < 60)
                        return; // someone in the doorway: keep trying for a moment

                    _retreatLocks = false;
                }
                else
                {
                    _actor.Say("(철컥)");
                }
            }

            TrySetState(GuardState.Ambush, true);
            _ambushUntil = now + AmbushDuration;
            Bark(AmbushLines);
        }

        private void StartEscape()
        {
            if (_escapeNode == null)
                return;

            TrySetState(GuardState.Escape, true);
            _mover.SetSpeed(_chaseSpeed);
            _actor.Say("난 빠진다! 살고 봐야지!");
            AiLog.Add($"{name} runs away");
            if (SquadCoordinator.Instance != null)
                SquadCoordinator.Instance.Unregister(this);
            if (!_mover.SetDestination(_escapeNode.Position))
                _escapeRetryAt = Time.time + 1f;
        }

        private void UpdateEscape(float now)
        {
            if (_mover.IsMoving)
                return;

            if (_mover.HasArrived && AiMath.Distance(transform.position, _escapeNode.Position) < 0.6f)
            {
                AiLog.Add($"{name} escaped the area");
                gameObject.SetActive(false);
                return;
            }

            if (now >= _escapeRetryAt)
            {
                _escapeRetryAt = now + 1f;
                _mover.SetDestination(_escapeNode.Position);
            }
        }

        private void UpdateAmbush(float now)
        {
            _mover.Stop();
            if (_retreatSpot != null && _retreatSpot.Door != null)
                _mover.FaceTowards(_retreatSpot.Door.transform.position);

            if (now < _ambushUntil)
                return;

            if (_retreatSpot != null && _retreatSpot.Door != null)
                _retreatSpot.Door.Unlock(_actor.Id);

            _stateEnteredAt = now - _config.MinNormalStateDuration;
            BeginReturn();
        }

        // A guard searching right next to a hiding spot finds the player after looking around for a moment.
        private bool TryDiscoverHiddenPlayer(float now)
        {
            var player = PlayerTarget.Instance;
            if (player == null || !player.IsHidden || player.IsDead || AiDirector.GhostMode || now - _lookStartedAt < HiddenDiscoverDelay)
                return false;

            if (!AiMath.IsSameFloor(transform.position, player.Position)
                || AiMath.Distance(transform.position, player.Position) > HiddenDiscoverRadius)
                return false;

            player.SetHidden(false);
            _actor.Say("거기 숨어 있었군!");
            AiLog.Add($"{name} found the hidden player");
            OnContactStarted();
            return true;
        }

        // ---------------- Return ----------------

        private void BeginReturn()
        {
            if (!TrySetState(GuardState.Return, false))
                return;

            _mover.SetSpeed(_patrolSpeed);
            _blackboard.AssignedRole = SquadRole.None;
            _searchFromReport = false;
            _holdPosition = false;
            Bark(CalmLines);
            _patrolNode = _patrol.GetRouteEntry(transform.position);
            if (_patrolNode == null || !_mover.SetDestination(_patrolNode.Position))
                BeginWait(1f);
        }

        private void UpdateReturn(float now)
        {
            if (_isWaiting)
            {
                if (now < _waitUntil)
                    return;

                _isWaiting = false;
                _patrolNode = _patrol.GetRouteEntry(transform.position);
                if (_patrolNode == null || !_mover.SetDestination(_patrolNode.Position))
                    BeginWait(1f);
                return;
            }

            if (_mover.IsBlocked)
            {
                BeginWait(1f);
                return;
            }

            if (_mover.IsMoving)
                return;

            // Alert value is not reset; it keeps decaying under F1-4 rules.
            if (TrySetState(GuardState.Patrol, false))
                BeginWait(_patrol.NextWaitTime(UseF1));
        }

        // ---------------- Chase (F1-2) ----------------

        private void OnContactStarted()
        {
            if (_state == GuardState.Dead || _state == GuardState.Escape)
                return;

            var player = PlayerTarget.Instance;
            if (player == null)
                return;

            float now = Time.time;
            RecordEvidence(player.Position, now);
            _alert.Add(_config.AlertOnVisual, now);
            if (_state == GuardState.Ambush)
            {
                // Ready behind the door: the first blow / shot comes immediately.
                _nextAttackTime = 0f;
                _aimGauge = 1f;
                _nextFireTime = 0f;
            }

            bool firstSighting = _state != GuardState.Chase;
            if (firstSighting)
            {
                TrySetState(GuardState.Chase, true);
                _mover.SetSpeed(_chaseSpeed);
                _blackboard.AssignedRole = SquadRole.Pursue;
            }

            if (firstSighting && now >= _nextShoutTime)
            {
                // Loud alarm: big shaking subtitle, a noise ring, and a report heard across floors.
                _nextShoutTime = now + ShoutCooldown;
                _actor.Shout(ShoutLines[_barkIndex++ % ShoutLines.Length]);
                AiEventBus.EmitNoise(transform.position, _config.ShoutNoise, _actor.Id);
                if (UseF2 && SquadCoordinator.Instance != null)
                    SquadCoordinator.Instance.Submit(_actor, _blackboard.LastKnownPosition, 1f, true, true);
            }
            else
            {
                if (firstSighting)
                    Bark(SightLines);
                SubmitReport(true, 1f);
            }

            _nextReportTime = now + _config.ReportRefreshInterval;
        }

        private void OnContactUpdated(Vector3 position)
        {
            if (_state == GuardState.Dead || _state == GuardState.Escape)
                return;

            if (_state != GuardState.Chase)
            {
                OnContactStarted();
                return;
            }

            float now = Time.time;
            RecordEvidence(position, now);
            _alert.Refresh(now);
            if (now < _nextReportTime)
                return;

            SubmitReport(true, 1f);
            _nextReportTime = now + _config.ReportRefreshInterval;
        }

        private void OnContactLost()
        {
            if (_state != GuardState.Chase)
                return;

            Bark(LostLines);
            _blackboard.AssignedRole = SquadRole.Pursue;
            StartSearch(_blackboard.LastKnownPosition, null, false);
            // Submitted after own search points exist so teammates get different slots.
            SubmitReport(false, 0.7f);
        }

        private void UpdateChase(float now)
        {
            var player = PlayerTarget.Instance;
            if (player == null || player.IsDead)
            {
                _mover.Stop();
                return;
            }

            if (_actor.IsStaggered)
                return;

            if (now >= _shotImpactAt)
            {
                _shotImpactAt = float.PositiveInfinity;
                if (_sensor.HasVisualContact && _shotHit)
                {
                    player.TakeDamage(_shotDamage);
                    GunFireFx.Instance.PlayerHit(player.ChestPosition);
                }
            }

            // Chase the last sensed position, not the true transform.
            Vector3 target = _blackboard.LastKnownPosition;
            if (UpdateRifle(now, player, target))
                return;
            if (AiMath.Distance(transform.position, target) > _attackRange * 0.8f)
            {
                if (AiMath.IsSameFloor(transform.position, target))
                {
                    _mover.MoveDirect(target);
                }
                else if (now >= _nextRepathTime || AiMath.Distance(_mover.Destination, target) > 1f)
                {
                    // Target seen on stairs / another floor: go through the graph, never through the air.
                    _nextRepathTime = now + 0.5f;
                    _mover.SetDestination(target);
                }

                return;
            }

            _mover.Stop();
            _mover.FaceTowards(target);
            if (now >= _impactAt)
            {
                // Damage lands at the swing's impact frame, only if the player is still in reach.
                _impactAt = float.PositiveInfinity;
                if (InMeleeReach(player.Position, 0.3f))
                    player.TakeDamage(_attackDamage);
            }

            if (now < _nextAttackTime || !_sensor.HasVisualContact || !InMeleeReach(player.Position, 0f))
                return;

            _nextAttackTime = now + _attackCooldown;
            _impactAt = now + AttackImpactDelay;
            if (_animator != null)
                _animator.Play(_attackCount++ % 2 == 0 ? CharacterAction.Attack : CharacterAction.AttackAlt);
            if (_attackCount % 3 == 1)
                Bark(AttackLines);
        }

        // Mid range with line of sight: stand, aim (gauge fills), fire. Returns true while shooting.
        private bool UpdateRifle(float now, PlayerTarget player, Vector3 target)
        {
            float distance = AiMath.Distance(transform.position, target);
            bool canShoot = _hasRifle && _sensor.HasVisualContact && distance >= _shootMinDistance && distance <= _shootRange
                && !AiPhysics.IsOccluded(_actor.EyePosition, player.ChestPosition);
            if (!canShoot)
            {
                _aimGauge = Mathf.Max(0f, _aimGauge - Time.deltaTime);
                return false;
            }

            _mover.Stop();
            _mover.FaceTowards(target);
            float distanceFactor = Mathf.Clamp(1.5f - distance / _shootRange, 0.4f, 1.3f);
            _aimGauge = Mathf.Min(1f, _aimGauge + _aimFillPerSecond * distanceFactor * Time.deltaTime);
            if (_aimGauge < _fireThreshold || now < _nextFireTime)
                return true;

            _nextFireTime = now + _fireCooldown;
            _shotDamageGauge = _aimGauge;
            _shotDamage = Mathf.Lerp(_shotMinDamage, _shotMaxDamage, _aimGauge);
            _shotImpactAt = now + 0.12f;
            _aimGauge = 0f;
            _fireThreshold = 0.55f + (float)_combatRandom.NextDouble() * 0.45f;
            if (_animator != null) _animator.Play(CharacterAction.Fire);

            // Hurried shots (low gauge) miss more often, as with the player's rifle.
            _shotHit = _combatRandom.NextDouble() < Mathf.Lerp(0.35f, 0.9f, _shotDamageGauge);
            Vector3 muzzle = _rifleModel != null ? _rifleModel.MuzzlePosition : _actor.EyePosition;
            GunFireFx.Instance.Shot(muzzle, player.ChestPosition, _shotHit);

            AiEventBus.EmitNoise(transform.position, _config.GunshotNoise, _actor.Id);
            if (_attackCount++ % 3 == 0)
                Bark(AttackLines);
            return true;
        }

        private bool InMeleeReach(Vector3 position, float slack)
        {
            return AiMath.Distance(transform.position, position) <= _attackRange + slack
                && AiMath.IsSameFloor(transform.position, position);
        }

        // Only an aware guard defends; an unaware one takes the sneak attack in full.
        public DefenseResult Defend()
        {
            if (_state != GuardState.Chase)
                return DefenseResult.None;

            double roll = _combatRandom.NextDouble();
            if (roll < DodgeChance)
                return DefenseResult.Dodge;

            return roll < DodgeChance + BlockChance ? DefenseResult.Block : DefenseResult.None;
        }

        private void Bark(string[] lines)
        {
            _actor.Say(lines[_barkIndex++ % lines.Length]);
        }

        // ---------------- Hearing / corpse / reports ----------------

        private void OnNoiseHeard(NoiseEvent noise, float effective)
        {
            if (_state == GuardState.Dead || _state == GuardState.Escape || noise.EventId <= _blackboard.LastHandledNoiseId)
                return;

            _blackboard.LastHandledNoiseId = noise.EventId;
            if (_state == GuardState.Chase || IsFromGuard(noise.SourceId))
                return;

            if (_state == GuardState.Retreat || _state == GuardState.Ambush)
            {
                // Holding the room: listen, turn toward the sound, wait longer.
                _alert.Add(_config.AlertOnNoise, Time.time);
                _ambushUntil = Mathf.Max(_ambushUntil, Time.time + AmbushDuration * 0.5f);
                _mover.FaceTowards(noise.Position);
                return;
            }

            float now = Time.time;
            var stageBefore = _alert.Stage;
            _blackboard.LastEvidenceTime = now;
            _alert.Add(_config.AlertOnNoise * Mathf.Clamp(effective * 1.5f, 0.5f, 1.5f), now);

            // F1: the reaction depends on this guard's own alert level and how loud the sound reached him.
            if (UseF1 && _state == GuardState.Patrol && stageBefore == AlertStage.Normal && effective < FaintNoiseLevel)
            {
                // Calm guard, faint sound: stop, look toward it, then carry on patrolling.
                _mover.Stop();
                _mover.FaceTowards(noise.Position);
                BeginWait(Mathf.Lerp(1.2f, 2.8f, (float)_random.NextDouble()));
                if (_random.NextDouble() < 0.5d)
                    _actor.Say("...응?");
                return;
            }

            if (effective >= _config.StrongNoiseReportThreshold && now >= _nextSuspicionReportTime)
            {
                _nextSuspicionReportTime = now + SuspicionReportCooldown;
                SubmitReportAt(noise.Position, false, 0.4f);
            }

            if (_state == GuardState.Search || (UseF1 && _alert.Stage >= AlertStage.Searching))
            {
                // Already on edge: skip the cautious approach and sweep the area at once.
                StartSearch(noise.Position, null, false);
                if (_alert.Stage == AlertStage.CombatReady)
                    _mover.SetSpeed(_chaseSpeed * 0.85f);
                return;
            }

            StartInvestigate(noise.Position);
        }

        private void OnCorpseFound(NpcActor corpse)
        {
            if (_state == GuardState.Dead)
                return;

            float now = Time.time;
            _blackboard.LastEvidenceTime = now;
            _alert.Add(_config.AlertOnCorpse, now);
            AiLog.Add($"{name} found body of {corpse.name}");
            if (_state == GuardState.Chase)
                return;

            Bark(CorpseLines);
            _blackboard.AssignedRole = SquadRole.Pursue;
            StartSearch(corpse.transform.position, null, false);
            SubmitReportAt(corpse.transform.position, false, 0.5f);
        }

        // Called by SquadCoordinator after delivery delay. Reports are past information only.
        public bool ReceiveReport(in ThreatReport report, SquadRole role, Vector3? slot)
        {
            if (_state == GuardState.Dead || !_handledReports.Add(report.ReportId))
                return false;

            _blackboard.LastHandledReportId = report.ReportId;
            float now = Time.time;
            if (now - report.ReportedAt > _config.ReportTTL || _state == GuardState.Chase || _state == GuardState.Escape)
                return false;

            float gain = report.HasVisualContact ? _config.AlertOnVisualReport : _config.AlertOnNoise * report.Confidence;
            _alert.Add(gain, now);
            _blackboard.LastKnownPosition = report.LastKnownPosition;
            _blackboard.HasLastKnownPosition = true;
            _searchFromReport = true;
            _reportReceivedAt = now;
            _reportExpiresAt = report.ReportedAt + _config.ReportTTL;

            bool sameAssignment = _state == GuardState.Search && _blackboard.AssignedRole == role && slot.HasValue
                && _searchPoints.Count > 0 && AiMath.Distance(_searchPoints[0], slot.Value) < 1f;
            _blackboard.AssignedRole = role;
            if (sameAssignment)
            {
                _searchDeadline = now + _config.SearchDuration;
                return true;
            }

            _actor.Say(RoleCallout(role));
            StartSearch(report.LastKnownPosition, slot, role == SquadRole.GuardExit);
            return true;
        }

        public void PromoteToFlank(Vector3 slot)
        {
            if (_state != GuardState.Search || _blackboard.AssignedRole != SquadRole.GuardExit)
                return;

            _blackboard.AssignedRole = SquadRole.Flank;
            _actor.Say("내가 앞으로 간다!");
            StartSearch(_blackboard.LastKnownPosition, slot, false);
        }

        public void DebugForceAlert(float value)
        {
            if (_state == GuardState.Dead)
                return;

            _alert.SetValue(value, Time.time);
            StartSearch(transform.position, null, false);
        }

        private void RecordEvidence(Vector3 position, float now)
        {
            _blackboard.LastKnownPosition = position;
            _blackboard.HasLastKnownPosition = true;
            _blackboard.LastEvidenceTime = now;
        }

        private void SubmitReport(bool visual, float confidence)
        {
            SubmitReportAt(_blackboard.LastKnownPosition, visual, confidence);
        }

        private void SubmitReportAt(Vector3 position, bool visual, float confidence)
        {
            if (!UseF2 || SquadCoordinator.Instance == null)
                return;

            SquadCoordinator.Instance.Submit(_actor, position, confidence, visual);
        }

        private void OnDied(NpcActor actor)
        {
            _state = GuardState.Dead;
            _mover.Stop();

            // The body can be searched: ammo (if armed) and a bandage.
            var body = gameObject.AddComponent<LootPile>();
            body.Initialize($"{_actor.DisplayName}의 시체", 1.5f);
            if (_hasRifle)
                body.Add(Inventory.Ammo, 6 + _combatRandom.Next(0, 7));
            body.Add("붕대", 1);
            // Coordinator reads the dead guard's role, so clear it afterwards.
            if (SquadCoordinator.Instance != null)
                SquadCoordinator.Instance.OnGuardDied(this);
            _blackboard.AssignedRole = SquadRole.None;
        }

        private static string RoleCallout(SquadRole role)
        {
            switch (role)
            {
                case SquadRole.Pursue: return "내가 간다!";
                case SquadRole.Flank: return "반대쪽으로 돌아간다!";
                case SquadRole.GuardExit: return "출구는 내가 막는다!";
                default: return "알았다.";
            }
        }

        public string BuildDebugText()
        {
            if (_alert == null)
                return name;

            string lkp = _blackboard.HasLastKnownPosition
                ? $"({_blackboard.LastKnownPosition.x:0.0},{_blackboard.LastKnownPosition.z:0.0})"
                : "-";
            return $"{name} [{_state}] {Temperament} role={Role}\n  alert {_alert.Value:0} {_alert.Stage}  LKP {lkp}\n  report#{_blackboard.LastHandledReportId}  seed {Seed}  decisions {_patrol.DecisionCount}";
        }

        private void OnDrawGizmos()
        {
            float distance = _config != null ? _config.ViewDistance : 6f;
            float halfAngle = (_config != null ? _config.ViewAngle : 110f) * 0.5f;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Gizmos.color = _state == GuardState.Chase ? Color.red
                : _state == GuardState.Search || _state == GuardState.Investigate ? Color.yellow : Color.white;
            Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward * distance);
            Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, halfAngle, 0f) * transform.forward * distance);

            if (!Application.isPlaying)
                return;

            if (_blackboard.HasLastKnownPosition)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_blackboard.LastKnownPosition + Vector3.up * 0.1f, 0.4f);
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < _searchPoints.Count; i++)
                Gizmos.DrawWireCube(_searchPoints[i] + Vector3.up * 0.1f, Vector3.one * 0.3f);
        }
    }
}
