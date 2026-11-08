using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // F3: unarmed NPC. Routine -> Flee -> LockDoor -> Hide -> CautiousExit -> Routine (spec 7.2).
    // Never attacks. Footsteps alone only make it nervous; only a direct sighting triggers flight.
    [RequireComponent(typeof(NpcActor), typeof(PerceptionSensor), typeof(NpcMover))]
    public sealed class CivilianBrain : MonoBehaviour
    {
        [SerializeField] private List<Waypoint> _routineNodes = new List<Waypoint>();
        [Tooltip("Node outside the building; reaching it removes the civilian from the map.")]
        [SerializeField] private Waypoint _escapeNode;
        [SerializeField] private float _walkSpeed = 1.3f;
        [SerializeField] private float _fleeSpeed = 3.4f;
        [SerializeField] private float _dangerPathRadius = 3f;
        [SerializeField] private float _dangerPathPenalty = 30f;
        [SerializeField] private float _routineWaitMin = 2f;
        [SerializeField] private float _routineWaitMax = 4f;
        [SerializeField] private float _cautiousListenTime = 2f;
        [SerializeField] private int _maxLockAttempts = 4;

        private readonly HashSet<SafeSpot> _rejectedSpots = new HashSet<SafeSpot>();
        private readonly List<Waypoint> _pathScratch = new List<Waypoint>();

        private NpcActor _actor;
        private PerceptionSensor _sensor;
        private NpcMover _mover;
        private CharacterAnimator _animator;
        private AiDirector _director;
        private AiTuningConfig _config;
        private WaypointGraph _graph;
        private System.Random _random;
        private Func<Waypoint, float> _dangerPenalty;

        private CivilianState _state;
        private SafeSpot _targetSpot;
        private HideSpot _hideSpot;
        private bool _hidePending;
        private bool _isWaiting;
        private float _waitUntil;
        private float _lastThreatTime = float.NegativeInfinity;
        private Vector3 _threatPosition;
        private bool _hasThreat;
        private int _failures;
        private float _nextDecisionTime;
        private int _lockAttempts;
        private float _nextLockAttemptTime;
        private float _cautiousUntil;
        private bool _exitDoorOpened;
        private bool _helpRequested;
        private float _fallbackRetryAt;
        private float _lookDirection = 1f;
        private float _begCalmSince = float.PositiveInfinity;

        private static readonly string[] BegLines = { "제발... 살려주세요!", "가진 거 다 드릴게요, 제발!", "아이들이 있어요... 제발..." };
        private int _begLine;

        public NpcActor Actor => _actor;
        public CivilianState State => _state;
        public int Seed { get; private set; }

        private bool UseF2 => _director.IsF2Active;
        private bool UseF3 => _director.IsF3Active;
        private float SafeElapsed => Time.time - _lastThreatTime;

        private void Awake()
        {
            _actor = GetComponent<NpcActor>();
            _sensor = GetComponent<PerceptionSensor>();
            _mover = GetComponent<NpcMover>();
            _animator = GetComponent<CharacterAnimator>();
            _dangerPenalty = DangerPenalty;
        }

        private void Start()
        {
            _director = AiDirector.Instance;
            _config = _director.Config;
            _graph = WaypointGraph.Instance;
            Seed = _director.GetNpcSeed(_actor.Id);
            _random = new System.Random(Seed);

            _sensor.ContactStarted += OnContactStarted;
            _sensor.ContactUpdated += OnContactUpdated;
            _sensor.NoiseHeard += OnNoiseHeard;
            _actor.Died += OnDied;
            _actor.Damaged += OnDamaged;
            StartRoutine();
        }

        private void OnDestroy()
        {
            if (_sensor != null)
            {
                _sensor.ContactStarted -= OnContactStarted;
                _sensor.ContactUpdated -= OnContactUpdated;
                _sensor.NoiseHeard -= OnNoiseHeard;
            }

            if (_actor != null)
            {
                _actor.Died -= OnDied;
                _actor.Damaged -= OnDamaged;
            }

            LeaveHideSpot();
        }

        private void Update()
        {
            if (_state == CivilianState.Dead || _config == null)
                return;

            float now = Time.time;
            switch (_state)
            {
                case CivilianState.Routine: UpdateRoutine(now); break;
                case CivilianState.Flee: UpdateFlee(); break;
                case CivilianState.LockDoor: UpdateLockDoor(now); break;
                case CivilianState.Hide: UpdateHide(); break;
                case CivilianState.CautiousExit: UpdateCautiousExit(now); break;
                case CivilianState.FleeFallback: UpdateFallback(now); break;
                case CivilianState.Beg: UpdateBeg(now); break;
                case CivilianState.Escape: UpdateEscape(now); break;
            }
        }

        private void SetState(CivilianState next)
        {
            if (_state != next)
                AiLog.Add($"{name}: {_state} -> {next}");

            _state = next;
            _isWaiting = false;
            SetCower(false);
        }

        // ---------------- Perception ----------------

        private void OnContactStarted()
        {
            var player = PlayerTarget.Instance;
            if (player == null || _state == CivilianState.Dead)
                return;

            RegisterThreat(player.Position);
            if (!UseF3)
            {
                _actor.Say("헉...!");
                return;
            }

            RequestHelpOnce();
            if (_state == CivilianState.Beg || _state == CivilianState.Escape)
                return;

            switch (_state)
            {
                case CivilianState.Routine:
                case CivilianState.CautiousExit:
                    StartFlee(false);
                    break;
                case CivilianState.Flee:
                    TryReplan();
                    break;
                case CivilianState.FleeFallback:
                    if (Time.time >= _fallbackRetryAt)
                        StartFlee(false);
                    break;
            }
        }

        private void OnContactUpdated(Vector3 position)
        {
            if (_state == CivilianState.Dead)
                return;

            RegisterThreat(position);
            if (UseF3 && _state == CivilianState.Flee)
                TryReplan();
        }

        private void OnNoiseHeard(NoiseEvent noise, float effective)
        {
            if (_state == CivilianState.Dead || !UseF3)
                return;

            switch (_state)
            {
                case CivilianState.Routine:
                    _actor.Say("...무슨 소리지?");
                    break;
                case CivilianState.Hide:
                case CivilianState.LockDoor:
                    _lastThreatTime = Time.time;
                    break;
                case CivilianState.CautiousExit:
                    _lastThreatTime = Time.time;
                    ReturnToHide();
                    break;
            }
        }

        // Hit by the player: run. At 50% health or below: beg for mercy or flee away from the player.
        private void OnDamaged(float before, Vector3 from)
        {
            if (_state == CivilianState.Dead)
                return;

            RegisterThreat(from);
            if (_actor.Health01 <= 0.5f)
            {
                if (_state != CivilianState.Beg && _random.NextDouble() < 0.5d)
                {
                    StartBeg();
                    return;
                }

                EnterFallback();
                return;
            }

            if (_state == CivilianState.Beg)
            {
                EnterFallback();
                return;
            }

            _actor.Say("으악! 살려줘!");
            StartFlee(false);
        }

        private void StartBeg()
        {
            SetState(CivilianState.Beg);
            LeaveHideSpot();
            _mover.Stop();
            SetCower(true);
            _begCalmSince = float.PositiveInfinity;
            _actor.Say(BegLines[_begLine++ % BegLines.Length], 3f);
        }

        // Keeps begging while the player is near; recovers once the player has walked away.
        private void UpdateBeg(float now)
        {
            var player = PlayerTarget.Instance;
            if (player != null)
                _mover.FaceTowards(player.Position);

            bool near = player != null && !player.IsDead && AiMath.Distance(player.Position, transform.position) < 6f;
            if (near)
            {
                _begCalmSince = float.PositiveInfinity;
                if (_actor.Speech == null)
                    _actor.Say(BegLines[_begLine++ % BegLines.Length], 3f);
                return;
            }

            if (float.IsPositiveInfinity(_begCalmSince))
                _begCalmSince = now;

            // The player walked away: get up and run out of the building.
            if (now - _begCalmSince >= 3f)
                StartEscape();
        }

        private float _escapeRetryAt;

        private void StartEscape()
        {
            if (_escapeNode == null)
            {
                EnterFallback();
                return;
            }

            SetState(CivilianState.Escape);
            LeaveHideSpot();
            _mover.SetSpeed(_fleeSpeed);
            _actor.Say("지금이야, 도망쳐!");
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

        private void RegisterThreat(Vector3 position)
        {
            _threatPosition = position;
            _hasThreat = true;
            _lastThreatTime = Time.time;
        }

        private void RequestHelpOnce()
        {
            if (_helpRequested || !UseF2 || SquadCoordinator.Instance == null)
                return;

            _helpRequested = true;
            SquadCoordinator.Instance.Submit(_actor, _threatPosition, 0.8f, true);
            _actor.Say("도와줘요! 누가 들어왔어요!");
        }

        private float DangerPenalty(Waypoint node)
        {
            return _hasThreat && AiMath.Distance(node.Position, _threatPosition) < _dangerPathRadius ? _dangerPathPenalty : 0f;
        }

        // ---------------- Flee (F3-2) ----------------

        private void StartFlee(bool keepRejected)
        {
            SetState(CivilianState.Flee);
            _mover.SetSpeed(_fleeSpeed);
            LeaveHideSpot();
            if (!keepRejected)
                _rejectedSpots.Clear();

            _failures = 0;
            ChooseSafeSpot();
        }

        private void ChooseSafeSpot()
        {
            _nextDecisionTime = Time.time + _config.CivilianDecisionCooldown;
            var best = FindBestSafeSpot();
            if (best == null)
            {
                EnterFallback();
                return;
            }

            _targetSpot = best;
            if (_mover.SetDestination(best.InsideNode.Position, _dangerPenalty))
                return;

            _rejectedSpots.Add(best);
            RegisterFailure();
        }

        // Cost = travel distance + danger along path + occupancy + missing lock.
        private SafeSpot FindBestSafeSpot()
        {
            var start = _graph.FindNearest(transform.position, true);
            if (start == null)
                return null;

            SafeSpot best = null;
            float bestCost = float.MaxValue;
            var spots = SafeSpot.All;
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (spot == null || spot.InsideNode == null || _rejectedSpots.Contains(spot))
                    continue;

                // Locked by someone else: cannot get in.
                if (spot.Door != null && spot.Door.IsLocked)
                    continue;

                if (!_graph.TryFindPath(start, spot.InsideNode, _pathScratch, _dangerPenalty))
                    continue;

                float cost = AiMath.Distance(transform.position, start.Position) + WaypointGraph.PathLength(_pathScratch);
                for (int j = 0; j < _pathScratch.Count; j++)
                    cost += DangerPenalty(_pathScratch[j]);

                if (!spot.HasFreeHideSpot(_actor))
                    cost += _config.CivilianOccupiedPenalty;

                if (spot.Door == null)
                    cost += _config.CivilianNoLockPenalty;

                cost += spot.DangerBias;
                if (cost >= bestCost)
                    continue;

                bestCost = cost;
                best = spot;
            }

            return best;
        }

        private void TryReplan()
        {
            if (_targetSpot == null || Time.time < _nextDecisionTime)
                return;

            _nextDecisionTime = Time.time + _config.CivilianDecisionCooldown;
            Vector3 goal = _targetSpot.InsideNode.Position;
            // Threat is now closer to the destination than we are: path is likely compromised.
            if (AiMath.Distance(_threatPosition, goal) < AiMath.Distance(transform.position, goal))
                ChooseSafeSpot();
        }

        private void RegisterFailure()
        {
            _failures++;
            if (_failures >= _config.MaxFailures)
            {
                EnterFallback();
                return;
            }

            ChooseSafeSpot();
        }

        private void UpdateFlee()
        {
            if (_mover.IsBlocked)
            {
                if (_targetSpot != null)
                    _rejectedSpots.Add(_targetSpot);
                RegisterFailure();
                return;
            }

            if (_mover.IsMoving)
                return;

            if (_targetSpot == null)
            {
                ChooseSafeSpot();
                return;
            }

            var door = _targetSpot.Door;
            if (door == null)
            {
                StartHide();
                return;
            }

            if (door.IsFullyOnSide(transform.position, _targetSpot.InsideNode.Position))
            {
                StartLockDoor();
                return;
            }

            _rejectedSpots.Add(_targetSpot);
            RegisterFailure();
        }

        // ---------------- Lock door (F3-3) ----------------

        private void StartLockDoor()
        {
            SetState(CivilianState.LockDoor);
            _mover.Stop();
            _lockAttempts = 0;
            _nextLockAttemptTime = Time.time + 0.3f;
        }

        private void UpdateLockDoor(float now)
        {
            var door = _targetSpot.Door;
            _mover.FaceTowards(door.transform.position);
            if (now < _nextLockAttemptTime)
                return;

            // Close and lock only when nobody stands in the frame (checked inside LockableDoor).
            if (door.TryClose(_actor.Id, _config.DoorNoise * 0.5f) && door.TryLock(_actor.Id))
            {
                _actor.Say("(철컥)");
                AiLog.Add($"{name} locked {door.name}");
                StartHide();
                return;
            }

            _lockAttempts++;
            _nextLockAttemptTime = now + 0.5f;
            if (_lockAttempts < _maxLockAttempts)
                return;

            AiLog.Add($"{name}: lock failed at {door.name}");
            _rejectedSpots.Add(_targetSpot);
            EnterFallback();
        }

        // ---------------- Hide (F3-4) ----------------

        private void StartHide()
        {
            SetState(CivilianState.Hide);
            _mover.SetSpeed(_walkSpeed);
            _hideSpot = _targetSpot != null ? _targetSpot.FindFreeHideSpot(_actor) : null;
            if (_hideSpot != null && _hideSpot.TryOccupy(_actor))
            {
                _hideSpot.Inspected += OnHideSpotInspected;
                _hidePending = true;
                _mover.MoveDirect(_hideSpot.transform.position);
                return;
            }

            // Room is locked but every spot is taken: stay put inside the room.
            _hideSpot = null;
            _hidePending = false;
            _mover.Stop();
            _actor.Say("여기라도 숨어야 해...");
        }

        private void UpdateHide()
        {
            if (_hidePending)
            {
                if (_mover.IsMoving)
                    return;

                _hidePending = false;
                // Hidden = invisible inside the wardrobe; no begging pose (begging is a separate state).
                SetCower(false);
                _actor.SetHidden(true);
            }

            if (SafeElapsed >= _config.CivilianSafeDuration && !_sensor.HasVisualContact)
                StartCautiousExit();
        }

        private void ReturnToHide()
        {
            var door = _targetSpot != null ? _targetSpot.Door : null;
            if (door != null && door.TryClose(_actor.Id, 0f))
                door.TryLock(_actor.Id);

            StartHide();
        }

        private void OnHideSpotInspected(HideSpot spot)
        {
            if (_state != CivilianState.Hide)
                return;

            var player = PlayerTarget.Instance;
            if (player != null)
                RegisterThreat(player.Position);

            _actor.Say("으악! 해치지 마세요!");
            AiLog.Add($"{name} discovered in {spot.name}");
            var door = _targetSpot != null ? _targetSpot.Door : null;
            if (door != null)
            {
                door.Unlock(_actor.Id);
                door.TryOpen(_actor.Id, 0f);
            }

            if (_targetSpot != null)
                _rejectedSpots.Add(_targetSpot);

            StartFlee(true);
        }

        private void LeaveHideSpot()
        {
            if (_hideSpot != null)
            {
                _hideSpot.Inspected -= OnHideSpotInspected;
                _hideSpot.Release(_actor);
                _hideSpot = null;
            }

            _hidePending = false;
            if (_actor != null)
                _actor.SetHidden(false);
        }

        // ---------------- Cautious exit ----------------

        private void StartCautiousExit()
        {
            SetState(CivilianState.CautiousExit);
            LeaveHideSpot();
            _mover.Stop();
            _exitDoorOpened = false;
            _cautiousUntil = Time.time + _cautiousListenTime;
            _lookDirection = _random.NextDouble() < 0.5d ? -1f : 1f;
            _actor.Say("...간 건가?");
        }

        private void UpdateCautiousExit(float now)
        {
            if (now < _cautiousUntil)
            {
                transform.Rotate(0f, 50f * _lookDirection * Time.deltaTime, 0f);
                return;
            }

            if (!_exitDoorOpened)
            {
                var door = _targetSpot != null ? _targetSpot.Door : null;
                if (door != null)
                {
                    door.Unlock(_actor.Id);
                    door.TryOpen(_actor.Id, 0f);
                }

                _exitDoorOpened = true;
                _cautiousUntil = now + 1f;
                return;
            }

            StartRoutine();
        }

        // ---------------- Fallback (F3-5) ----------------

        private void EnterFallback()
        {
            SetState(CivilianState.FleeFallback);
            _mover.SetSpeed(_fleeSpeed);
            LeaveHideSpot();
            _fallbackRetryAt = Time.time + _config.PathBlockedTimeout;

            var target = FindFallbackNode();
            if (target == null || !_mover.SetDestination(target.Position, _dangerPenalty))
            {
                // No valid destination: hold position, keep calling for help, issue no move orders.
                _mover.Stop();
                _actor.Say("살려줘요!");
                return;
            }

            _actor.Say("오지 마!");
        }

        private Waypoint FindFallbackNode()
        {
            var start = _graph.FindNearest(transform.position, true);
            if (start == null)
                return null;

            Waypoint best = null;
            float bestDistance = AiMath.Distance(transform.position, _threatPosition);
            var nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                float distance = AiMath.Distance(node.Position, _threatPosition);
                if (distance <= bestDistance || !_graph.TryFindPath(start, node, _pathScratch))
                    continue;

                bestDistance = distance;
                best = node;
            }

            return best;
        }

        private void UpdateFallback(float now)
        {
            if (SafeElapsed >= _config.CivilianSafeDuration && !_sensor.HasVisualContact)
            {
                StartRoutine();
                return;
            }

            if (now < _fallbackRetryAt || (_mover.IsMoving && !_mover.IsBlocked))
                return;

            // Periodic retry for a real safe room; never more than once per retry window.
            _fallbackRetryAt = now + _config.PathBlockedTimeout * 2f;
            _rejectedSpots.Clear();
            var best = FindBestSafeSpot();
            if (best != null)
            {
                SetState(CivilianState.Flee);
                _failures = 0;
                _targetSpot = best;
                if (!_mover.SetDestination(best.InsideNode.Position, _dangerPenalty))
                    EnterFallback();
                return;
            }

            if (_mover.IsBlocked)
                EnterFallback();
        }

        // ---------------- Routine ----------------

        private void StartRoutine()
        {
            SetState(CivilianState.Routine);
            _mover.SetSpeed(_walkSpeed);
            _helpRequested = false;
            _hasThreat = false;
            LeaveHideSpot();
            BeginWait(1f);
        }

        private void UpdateRoutine(float now)
        {
            if (_mover.IsMoving)
                return;

            if (!_isWaiting)
            {
                BeginWait(Mathf.Lerp(_routineWaitMin, _routineWaitMax, (float)_random.NextDouble()));
                return;
            }

            if (now < _waitUntil || _routineNodes.Count == 0)
                return;

            _isWaiting = false;
            int index = _random.Next(_routineNodes.Count);
            var node = _routineNodes[index];
            if (node != null && AiMath.Distance(node.Position, transform.position) < 0.5f)
                node = _routineNodes[(index + 1) % _routineNodes.Count];

            if (node == null || !_mover.SetDestination(node.Position))
                BeginWait(1f);
        }

        private void SetCower(bool cower)
        {
            if (_animator != null)
                _animator.SetCower(cower);
        }

        private void BeginWait(float duration)
        {
            _isWaiting = true;
            _waitUntil = Time.time + duration;
        }

        private void OnDied(NpcActor actor)
        {
            SetState(CivilianState.Dead);
            _mover.Stop();

            var body = gameObject.AddComponent<LootPile>();
            body.Initialize($"{_actor.DisplayName}의 시체", 1.5f);
            body.Add("통조림", 1);
            if (_hideSpot != null)
            {
                _hideSpot.Inspected -= OnHideSpotInspected;
                _hideSpot.Release(_actor);
                _hideSpot = null;
            }
        }

        public string BuildDebugText()
        {
            string target = _targetSpot != null ? _targetSpot.name : "-";
            string hide = _hideSpot != null ? _hideSpot.name : "-";
            float safe = float.IsInfinity(_lastThreatTime) ? 0f : Mathf.Min(SafeElapsed, 99f);
            return $"{name} [{_state}]\n  safeRoom {target}  hide {hide}\n  calm {safe:0.0}/{(_config != null ? _config.CivilianSafeDuration : 0f):0}s  seed {Seed}";
        }
    }
}
