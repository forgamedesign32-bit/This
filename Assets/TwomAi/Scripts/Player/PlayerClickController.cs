using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TwomAi
{
    // TWOM-style control. The mouse cursor never changes: every interaction is a round button drawn
    // at the center of its target (enemy body, door, wardrobe, rubble). LMB on a button = act,
    // LMB on the floor = walk (double-click run), RMB = cancel.
    // Rifle: hovering an enemy's button aims at it; the aim gauge fills while standing still,
    // drains while moving, scales with distance, and a click fires (damage scales with the gauge).
    [RequireComponent(typeof(PlayerTarget), typeof(NpcMover))]
    public sealed class PlayerClickController : MonoBehaviour
    {
        public enum ButtonKind { OpenDoor, CloseDoor, LockedDoor, Search, Loot, Melee, SneakAttack, Shoot, Hide, LeaveHide, Peek, Group }

        public struct InteractionButton
        {
            public Vector2 Center;
            public float Radius;
            public ButtonKind Kind;
            public Object Target;
            // Group only: member count, open state and radius of the ring the members sit on.
            public int Count;
            public bool Expanded;
            public float RingRadius;
        }

        private enum PendingKind { None, Door, Search, Loot, Attack, Exit, Hide, Peek }

        public enum TimedKind { None, Loot, PickLock, PryDoor }

        private const float RepathInterval = 0.25f;
        private const float MessageDuration = 2.2f;
        private const float AttackImpactDelay = 0.35f;
        private const float KickImpactDelay = 0.5f;
        private const float ShotImpactDelay = 0.12f;
        private const float ButtonRadius = 42f;
        private const float GroupButtonRadius = 34f;
        private const float RadialItemRadius = 36f;
        private const float RadialItemGap = 10f;
        private const float RadialMinRing = 74f;
        // Buttons closer than this (center to center) are merged into one radial group.
        private const float GroupMergeDistance = ButtonRadius * 1.9f;

        [SerializeField] private float _walkSpeed = 2f;
        [SerializeField] private float _runSpeed = 5.2f;
        [SerializeField] private float _floorHeight = 3.2f;
        [SerializeField] private int _floorCount = 2;
        [SerializeField] private float _doubleClickTime = 0.3f;
        [SerializeField] private float _doubleClickDistance = 1f;
        [SerializeField] private float _doorApproachOffset = 0.9f;
        [SerializeField] private float _interactRange = 1.2f;
        [SerializeField] private float _objectButtonRange = 9f;

        [Header("Melee")]
        [SerializeField] private float _attackRange = 1.4f;
        [SerializeField] private float _attackCooldown = 0.8f;
        [SerializeField] private float _attackDamage = 45f;
        [SerializeField] private float _stealthDamage = 999f;

        [Header("Rifle")]
        [SerializeField] private int _ammo = 30;
        [SerializeField] private float _rifleRange = 14f;
        [SerializeField] private float _aimFillPerSecond = 0.55f;
        [SerializeField] private float _aimDrainPerSecond = 0.9f;
        [SerializeField] private float _aimHitPenalty = 0.3f;
        [SerializeField] private float _shotMinDamage = 12f;
        [SerializeField] private float _shotMaxDamage = 70f;
        [SerializeField] private float _fireCooldown = 0.55f;
        [SerializeField] private float _staggerThreshold = 0.95f;

        [SerializeField] private Waypoint _exitNode;
        [SerializeField] private WeaponMount _rifleModel;
        [SerializeField] private HandPropMount _crowbarModel;

        [Header("Tools")]
        [SerializeField] private bool _hasLockpick;
        [SerializeField] private float _pickLockDuration = 4f;
        [SerializeField] private float _pryDuration = 2.2f;
        [SerializeField] private float _pryNoiseInterval = 0.6f;


        private readonly List<InteractionButton> _buttons = new List<InteractionButton>();
        private readonly List<InteractionButton> _rawButtons = new List<InteractionButton>();
        private readonly List<int> _groupRoots = new List<int>();
        private Object _expandedTarget;

        private PlayerTarget _player;
        private NpcMover _mover;
        private CharacterAnimator _animator;
        private Camera _camera;
        private AiTuningConfig _config;

        private PendingKind _pending;
        private LockableDoor _pendingDoor;
        private HideSpot _pendingSpot;
        private LootPile _pendingLoot;
        private NpcActor _pendingTarget;
        private Vector3 _pendingPoint;
        private bool _pendingRun;
        private float _nextRepathTime;
        private float _nextAttackTime;
        private float _lastClickTime = float.NegativeInfinity;
        private Vector3 _lastClickPoint;
        private string _message;
        private float _messageUntil;

        private NpcActor _impactTarget;
        private bool _impactStealth;
        private bool _impactIsShot;
        private bool _impactHit;
        private readonly System.Random _shotRandom = new System.Random();
        private float _impactDamage;
        private bool _impactStagger;
        private float _impactAt = float.PositiveInfinity;
        private int _attackCount;

        private NpcActor _aimTarget;
        private float _aimGauge;
        private float _nextFireTime;

        private TimedKind _timed;
        private LootPile _timedLoot;
        private LockableDoor _timedDoor;
        private float _timedStartedAt;
        private float _timedEndsAt;
        private float _nextTimedNoise;
        private float _meleePropUntil;
        private PlayerHideSpot _pendingHide;
        private PlayerHideSpot _hideSpot;
        private RoomVolume _peekRoom;
        private LootPile _openContainer;
        private bool _dawnWarned;
        private readonly Dictionary<string, int> _inventory = new Dictionary<string, int>();

        public bool RunMode { get; set; }
        public WeaponKind Weapon { get; private set; } = WeaponKind.Rifle;
        public IReadOnlyList<InteractionButton> Buttons => _buttons;
        public int HoveredButton { get; private set; } = -1;
        public string Message => Time.time < _messageUntil ? _message : null;
        public int Items
        {
            get
            {
                int total = 0;
                foreach (var pair in _inventory)
                    total += pair.Value;
                return total;
            }
        }

        public LootPile OpenContainer => _openContainer;
        public bool EndedAtDawn { get; private set; }
        public int Ammo => _ammo;
        public bool HasExited { get; private set; }
        public bool HasLockpick => _hasLockpick;
        public bool IsHidden => _hideSpot != null;
        public bool IsPeeking => _peekRoom != null;
        public IReadOnlyDictionary<string, int> Backpack => _inventory;
        public TimedKind Timed => _timed;
        public bool IsLooting => _timed == TimedKind.Loot;
        public bool IsBusy => _timed != TimedKind.None;
        public float TimedProgress01 => IsBusy ? Mathf.InverseLerp(_timedStartedAt, _timedEndsAt, Time.time) : 0f;
        public float LootProgress01 => IsLooting ? TimedProgress01 : 0f;
        public NpcActor AimTarget => _aimTarget;
        public float AimGauge => _aimGauge;
        public float AimMax => _aimTarget != null ? AimCap(Vector3.Distance(transform.position, _aimTarget.transform.position)) : 0f;
        public bool IsFullAim => _aimTarget != null && _aimGauge >= _staggerThreshold * AimMax && AimMax >= _staggerThreshold;

        private void Awake()
        {
            _player = GetComponent<PlayerTarget>();
            _mover = GetComponent<NpcMover>();
            _animator = GetComponent<CharacterAnimator>();
        }

        private void Start()
        {
            _camera = Camera.main;
            _config = AiDirector.Instance.Config;
            _mover.DoorOpenNoise = _config.DoorNoise;
            _player.Damaged += OnDamaged;
            EquipWeapon(Weapon);
            _floorCount = Mathf.Max(_floorCount, CountGraphFloors());
        }

        // The serialized value predates the 4-floor map; derive it from the waypoint graph instead.
        private int CountGraphFloors()
        {
            var graph = WaypointGraph.Instance;
            if (graph == null)
                return 1;

            float maxY = 0f;
            var nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null)
                    maxY = Mathf.Max(maxY, nodes[i].Position.y);
            }

            return Mathf.RoundToInt(maxY / _floorHeight) + 1;
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.Damaged -= OnDamaged;
        }

        private void Update()
        {
            if (_player.IsDead || HasExited)
            {
                _mover.Stop();
                CancelTimed();
                ClearAim();
                _buttons.Clear();
                _expandedTarget = null;
                HoveredButton = -1;
                return;
            }

            float now = Time.time;
            UpdateNight();
            if (_openContainer != null && (_mover.IsMoving || _openContainer.IsEmpty
                || Vector3.Distance(transform.position, _openContainer.transform.position) > 2.5f))
                CloseContainer();

            if (_hideSpot != null && !_player.IsHidden)
                _hideSpot = null;
            if (_peekRoom != null && _mover.IsMoving)
                EndPeek();

            UpdateImpact(now);
            UpdateTimed(now);
            BuildButtons();

            var mouse = Mouse.current;
            if (mouse != null && _camera != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                var gui = new Vector2(screen.x, Screen.height - screen.y);
                bool overUi = UiBlocker.IsPointerOverUi(screen);
                HoveredButton = overUi ? -1 : HitButton(gui);
                UpdateAimSelection();

                if (mouse.rightButton.wasPressedThisFrame)
                {
                    _expandedTarget = null;
                    StopAll();
                }
                else if (mouse.leftButton.wasPressedThisFrame && !overUi)
                {
                    CloseContainer();
                    HandleClick(screen);
                }
            }

            UpdateAimGauge(Time.deltaTime);
            UpdatePending(now);

            UpdateProps(now);
        }

        // ---------------- Public commands (HUD) ----------------

        public void StopAll()
        {
            ClearPending();
            CancelTimed();
            _mover.Stop();
        }

        // Survivor's own line above the head (state feedback, like the original).
        public void Think(string text)
        {
            _player.Say(text);
        }

        // Rifle stance while the rifle is out; crowbar shown in melee mode or during a close-range blow.
        private void UpdateProps(float now)
        {
            bool meleeBlow = now < _meleePropUntil;
            bool rifleOut = Weapon == WeaponKind.Rifle && !meleeBlow;
            if (_rifleModel != null)
                _rifleModel.SetVisible(rifleOut);
            if (_crowbarModel != null)
                _crowbarModel.SetVisible(!rifleOut);
            if (_animator != null)
            {
                _animator.SetAim(rifleOut && !IsBusy && !IsHidden && !IsPeeking);
                _animator.SetCower(IsBusy || IsHidden || IsPeeking);
            }
        }

        public void EquipWeapon(WeaponKind weapon)
        {
            Weapon = weapon;
            if (weapon != WeaponKind.Rifle)
                ClearAim();
            UpdateProps(Time.time);
        }

        public void CommandExit()
        {
            if (_exitNode == null || _player.IsDead)
                return;

            StopAll();
            _pending = PendingKind.Exit;
            _pendingPoint = _exitNode.Position;
            MoveTo(_pendingPoint, true);
        }

        public void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = Time.time + MessageDuration;
        }

        public static bool IsAnyGuardInCombat()
        {
            var coordinator = SquadCoordinator.Instance;
            if (coordinator == null)
                return false;

            var guards = coordinator.Guards;
            for (int i = 0; i < guards.Count; i++)
            {
                if (guards[i] != null && guards[i].State == GuardState.Chase)
                    return true;
            }

            return false;
        }

        // ---------------- Buttons ----------------

        private void BuildButtons()
        {
            _buttons.Clear();
            _rawButtons.Clear();
            if (_camera == null)
                return;

            CollectButtons();
            GroupButtons();
        }

        private void CollectButtons()
        {

            var visibility = VisibilitySystem.Instance;
            Vector3 self = transform.position;

            var actors = NpcActor.All;
            for (int i = 0; i < actors.Count; i++)
            {
                var npc = actors[i];
                if (npc.IsDead || npc.IsHidden || !npc.IsVisibleToPlayer)
                    continue;

                // With the rifle out, a target already in reach gets the crowbar instead (original: "rifle + axe").
                bool close = InMeleeReach(npc.transform.position, 0.2f);
                ButtonKind kind = Weapon == WeaponKind.Rifle && !close ? ButtonKind.Shoot
                    : npc.IsAlertedToPlayer ? ButtonKind.Melee : ButtonKind.SneakAttack;
                AddButton(npc.transform.position + Vector3.up * 1.1f, kind, npc);
            }

            var doors = LockableDoor.All;
            for (int i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                Vector3 center = door.transform.position + Vector3.up * 1.1f;
                if (!IsDoorReachable(door, center, self, visibility))
                    continue;

                if (door.IsOpen)
                {
                    AddButton(center, ButtonKind.CloseDoor, door);
                    continue;
                }

                // Closed door: [open / locked] [peek] pair.
                AddButton(center, door.IsLocked ? ButtonKind.LockedDoor : ButtonKind.OpenDoor, door, new Vector2(-ButtonRadius * 1.1f, 0f));
                AddButton(center, ButtonKind.Peek, door, new Vector2(ButtonRadius * 1.1f, 0f));
            }

            var hideSpots = PlayerHideSpot.All;
            for (int i = 0; i < hideSpots.Count; i++)
            {
                var hide = hideSpots[i];
                Vector3 center = hide.HidePosition + Vector3.up * 1.1f;
                if (hide == _hideSpot)
                {
                    AddButton(center, ButtonKind.LeaveHide, hide, Vector2.zero);
                }
                else if (!IsHidden && IsObjectReachable(center, self, visibility))
                {
                    AddButton(center, ButtonKind.Hide, hide, Vector2.zero);
                }
            }

            var spots = HideSpot.All;
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 center = spots[i].transform.position + Vector3.up * 1.1f;
                if (IsObjectReachable(center, self, visibility))
                    AddButton(center, ButtonKind.Search, spots[i]);
            }

            var piles = LootPile.All;
            for (int i = 0; i < piles.Count; i++)
            {
                Vector3 center = piles[i].transform.position + Vector3.up * 0.6f;
                if (!piles[i].IsEmpty && IsObjectReachable(center, self, visibility))
                    AddButton(center, ButtonKind.Loot, piles[i]);
            }
        }

        // A door sits on the boundary of two rooms: show it if either side is visible.
        private bool IsDoorReachable(LockableDoor door, Vector3 center, Vector3 self, VisibilitySystem visibility)
        {
            if (Vector3.Distance(center, self) > _objectButtonRange)
                return false;

            if (visibility == null)
                return true;

            Vector3 normal = AiMath.Flat(door.transform.forward).normalized * 0.6f;
            return visibility.IsPointVisible(center + normal) || visibility.IsPointVisible(center - normal);
        }

        private bool IsObjectReachable(Vector3 center, Vector3 self, VisibilitySystem visibility)
        {
            if (Vector3.Distance(center, self) > _objectButtonRange)
                return false;

            return visibility == null || visibility.IsPointVisible(center);
        }

        private void AddButton(Vector3 world, ButtonKind kind, Object target)
        {
            AddButton(world, kind, target, Vector2.zero);
        }

        private void AddButton(Vector3 world, ButtonKind kind, Object target, Vector2 screenOffset)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
                return;

            _rawButtons.Add(new InteractionButton
            {
                Center = new Vector2(screen.x, Screen.height - screen.y) + screenOffset,
                Radius = ButtonRadius,
                Kind = kind,
                Target = target,
            });
        }

        // Overlapping buttons (transitively) collapse into one Group button at their centroid.
        // The open group keeps its button as a close toggle and lays its members on a ring around it.
        private void GroupButtons()
        {
            int count = _rawButtons.Count;
            _groupRoots.Clear();
            for (int i = 0; i < count; i++)
                _groupRoots.Add(i);

            float mergeSqr = GroupMergeDistance * GroupMergeDistance;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if ((_rawButtons[i].Center - _rawButtons[j].Center).sqrMagnitude > mergeSqr)
                        continue;

                    int a = FindRoot(i);
                    int b = FindRoot(j);
                    if (a != b)
                        _groupRoots[b] = a;
                }
            }

            bool expandedFound = false;
            for (int root = 0; root < count; root++)
            {
                if (FindRoot(root) != root)
                    continue;

                int members = 0;
                bool expanded = false;
                Vector2 sum = Vector2.zero;
                for (int i = root; i < count; i++)
                {
                    if (FindRoot(i) != root)
                        continue;

                    members++;
                    sum += _rawButtons[i].Center;
                    if (_expandedTarget != null && _rawButtons[i].Target == _expandedTarget)
                        expanded = true;
                }

                if (members == 1)
                {
                    _buttons.Add(_rawButtons[root]);
                    continue;
                }

                Vector2 anchor = sum / members;
                expandedFound |= expanded;
                // Ring sized so neighbouring items never overlap.
                float ring = Mathf.Max(RadialMinRing, members * (RadialItemRadius * 2f + RadialItemGap) / (2f * Mathf.PI));
                _buttons.Add(new InteractionButton
                {
                    Center = anchor,
                    Radius = GroupButtonRadius,
                    Kind = ButtonKind.Group,
                    Target = _rawButtons[root].Target,
                    Count = members,
                    Expanded = expanded,
                    RingRadius = ring,
                });

                if (!expanded)
                    continue;

                float step = Mathf.PI * 2f / members;
                int slot = 0;
                for (int i = root; i < count; i++)
                {
                    if (FindRoot(i) != root)
                        continue;

                    float angle = slot++ * step - Mathf.PI * 0.5f;
                    var item = _rawButtons[i];
                    item.Center = anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
                    item.Radius = RadialItemRadius;
                    _buttons.Add(item);
                }
            }

            // The opened group dissolved (moved away, target gone): drop the open state.
            if (!expandedFound)
                _expandedTarget = null;
        }

        private int FindRoot(int index)
        {
            while (_groupRoots[index] != index)
            {
                _groupRoots[index] = _groupRoots[_groupRoots[index]];
                index = _groupRoots[index];
            }

            return index;
        }

        private int HitButton(Vector2 gui)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _buttons.Count; i++)
            {
                float distance = Vector2.Distance(gui, _buttons[i].Center);
                if (distance > _buttons[i].Radius || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        // ---------------- Clicks ----------------

        private void HandleClick(Vector2 screen)
        {
            float now = Time.time;
            if (HoveredButton >= 0)
            {
                var button = _buttons[HoveredButton];
                if (button.Kind == ButtonKind.Group)
                {
                    // Toggle only; does not count toward the double-click run timer.
                    _expandedTarget = button.Expanded ? null : button.Target;
                    return;
                }

                _expandedTarget = null;
                bool run = RunMode || (now - _lastClickTime <= _doubleClickTime);
                _lastClickTime = now;
                ExecuteButton(button, run);
                return;
            }

            if (_expandedTarget != null)
            {
                // First click outside an open radial menu just closes it.
                _expandedTarget = null;
                return;
            }

            if (!TryGetFloorPoint(screen, out Vector3 ground))
                return;

            bool doubleClick = now - _lastClickTime <= _doubleClickTime
                && Vector3.Distance(ground, _lastClickPoint) <= _doubleClickDistance;
            _lastClickTime = now;
            _lastClickPoint = ground;

            ClearPending();
            CancelTimed();
            LeaveHide();
            EndPeek();
            var graph = WaypointGraph.Instance;
            if (graph != null && graph.TrySnapToWalkable(ground, out Vector3 snapped))
                ground = snapped;

            MoveTo(ground, RunMode || doubleClick);
        }

        private void ExecuteButton(InteractionButton button, bool run)
        {
            if (button.Kind == ButtonKind.LeaveHide)
            {
                LeaveHide();
                return;
            }

            // Any other action gives the hiding spot away (sneak attacks still land: the target is unaware).
            LeaveHide();
            EndPeek();
            if (button.Kind == ButtonKind.Shoot)
            {
                TryFire(button.Target as NpcActor);
                return;
            }

            ClearPending();
            CancelTimed();
            _pendingRun = run;
            switch (button.Kind)
            {
                case ButtonKind.Melee:
                case ButtonKind.SneakAttack:
                    _pending = PendingKind.Attack;
                    _pendingTarget = (NpcActor)button.Target;
                    _nextRepathTime = 0f;
                    break;
                case ButtonKind.OpenDoor:
                case ButtonKind.CloseDoor:
                case ButtonKind.LockedDoor:
                    _pending = PendingKind.Door;
                    _pendingDoor = (LockableDoor)button.Target;
                    _pendingPoint = DoorApproachPoint(_pendingDoor);
                    MoveTo(_pendingPoint, run);
                    break;
                case ButtonKind.Search:
                    _pending = PendingKind.Search;
                    _pendingSpot = (HideSpot)button.Target;
                    _pendingPoint = _pendingSpot.transform.position;
                    MoveTo(_pendingPoint, run);
                    break;
                case ButtonKind.Peek:
                    _pending = PendingKind.Peek;
                    _pendingDoor = (LockableDoor)button.Target;
                    _pendingPoint = DoorApproachPoint(_pendingDoor);
                    MoveTo(_pendingPoint, run);
                    break;
                case ButtonKind.Hide:
                    _pending = PendingKind.Hide;
                    _pendingHide = (PlayerHideSpot)button.Target;
                    _pendingPoint = _pendingHide.ApproachPosition;
                    MoveTo(_pendingPoint, run);
                    break;
                case ButtonKind.Loot:
                    _pending = PendingKind.Loot;
                    _pendingLoot = (LootPile)button.Target;
                    _pendingPoint = _pendingLoot.transform.position;
                    MoveTo(_pendingPoint, run);
                    break;
            }
        }

        private bool TryGetFloorPoint(Vector2 screen, out Vector3 ground)
        {
            Ray ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.back, Vector3.zero);
            ground = Vector3.zero;
            if (!plane.Raycast(ray, out float enter))
                return false;

            Vector3 point = ray.GetPoint(enter);
            int floor = Mathf.Clamp(Mathf.FloorToInt((point.y + 0.5f) / _floorHeight), 0, _floorCount - 1);
            ground = new Vector3(point.x, floor * _floorHeight, 0f);
            return true;
        }

        private bool MoveTo(Vector3 point, bool run)
        {
            _mover.SetSpeed(run ? _runSpeed : _walkSpeed);
            if (_mover.SetDestination(point))
                return true;

            Think("저기로는 갈 수 없어.");
            return false;
        }

        private Vector3 DoorApproachPoint(LockableDoor door)
        {
            Vector3 normal = AiMath.Flat(door.transform.forward).normalized;
            float side = Vector3.Dot(transform.position - door.transform.position, normal) >= 0f ? 1f : -1f;
            Vector3 point = door.transform.position + normal * (side * _doorApproachOffset);
            point.z = 0f;
            return point;
        }

        // ---------------- Aim gauge (rifle) ----------------

        // Hovering an enemy's button selects it as the aim target; the last target is kept otherwise.
        private void UpdateAimSelection()
        {
            if (Weapon != WeaponKind.Rifle || HoveredButton < 0)
                return;

            var button = _buttons[HoveredButton];
            if (button.Kind != ButtonKind.Shoot)
                return;

            var npc = button.Target as NpcActor;
            if (npc == _aimTarget)
                return;

            _aimTarget = npc;
            _aimGauge = 0f;
        }

        private void UpdateAimGauge(float deltaTime)
        {
            if (_aimTarget == null)
                return;

            if (_aimTarget.IsDead || !_aimTarget.IsVisibleToPlayer || !HasLineOfFire(_aimTarget))
            {
                ClearAim();
                return;
            }

            float distance = Vector3.Distance(transform.position, _aimTarget.transform.position);
            bool moving = _mover.CurrentSpeed > 0.1f;
            if (moving)
            {
                // Moving while aiming breaks the aim (gauge drains, re-aim needed).
                _aimGauge = Mathf.Max(0f, _aimGauge - _aimDrainPerSecond * deltaTime);
            }
            else if (distance <= _rifleRange)
            {
                // Closer target: faster fill and higher reachable maximum.
                _aimGauge = Mathf.Min(AimCap(distance), _aimGauge + _aimFillPerSecond * DistanceFactor(distance) * deltaTime);
                _mover.FaceTowards(_aimTarget.transform.position);
            }

        }

        private float DistanceFactor(float distance) => Mathf.Clamp(1.5f - distance / _rifleRange, 0.35f, 1.3f);

        private float AimCap(float distance) => Mathf.Clamp(1.15f - distance / _rifleRange * 0.6f, 0.45f, 1f);

        private bool HasLineOfFire(NpcActor target)
        {
            return !AiPhysics.IsOccluded(_player.ChestPosition, target.transform.position + Vector3.up * 1.2f);
        }

        private void ClearAim()
        {
            _aimTarget = null;
            _aimGauge = 0f;
        }

        private void OnDamaged()
        {
            _aimGauge = Mathf.Max(0f, _aimGauge - _aimHitPenalty);
            CancelTimed();
        }

        private void TryFire(NpcActor target)
        {
            float now = Time.time;
            if (target == null || target.IsDead || now < _nextFireTime || _impactAt < float.PositiveInfinity)
                return;

            if (_ammo <= 0)
            {
                Think("탄약이 떨어졌어.");
                return;
            }

            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (distance > _rifleRange || !HasLineOfFire(target))
            {
                Think("여기선 맞출 수 없어.");
                return;
            }

            if (target != _aimTarget)
            {
                _aimTarget = target;
                _aimGauge = 0f;
            }

            StopAll();
            _mover.FaceTowards(target.transform.position);
            _ammo--;
            _nextFireTime = now + _fireCooldown;

            // Damage from the current gauge; full aim staggers and knocks back.
            float gauge = _aimGauge;
            _impactIsShot = true;
            _impactTarget = target;
            _impactStealth = false;
            _impactDamage = Mathf.Lerp(_shotMinDamage, _shotMaxDamage, gauge) * Mathf.Clamp(1.2f - distance / _rifleRange * 0.5f, 0.6f, 1.1f);
            _impactStagger = IsFullAim;
            _impactAt = now + ShotImpactDelay;
            _aimGauge = 0f;

            if (_animator != null)
                _animator.Play(CharacterAction.Fire);

            // Low aim or long range can miss (original: aim gauge drives accuracy).
            float hitChance = Mathf.Lerp(0.45f, 0.97f, gauge) * Mathf.Clamp(1.15f - distance / _rifleRange * 0.4f, 0.7f, 1f);
            _impactHit = _shotRandom.NextDouble() < hitChance;
            GunFireFx.Instance.Shot(MuzzlePosition(), target.transform.position + Vector3.up * 1.2f, _impactHit);

            AiEventBus.EmitNoise(transform.position, _config.GunshotNoise, AiIds.PlayerSourceId);
        }

        private Vector3 MuzzlePosition()
        {
            return _rifleModel != null && _rifleModel.gameObject.activeInHierarchy ? _rifleModel.MuzzlePosition : _player.ChestPosition;
        }

        // ---------------- Pending actions ----------------

        private void UpdatePending(float now)
        {
            if (_pending == PendingKind.None)
                return;

            if (_pending == PendingKind.Attack)
            {
                UpdateMeleeAction(now);
                return;
            }

            if (_mover.IsMoving)
                return;

            if (Vector3.Distance(transform.position, _pendingPoint) > _interactRange)
            {
                Think("길이 막혔어.");
                ClearPending();
                return;
            }

            switch (_pending)
            {
                case PendingKind.Door:
                    _mover.FaceTowards(_pendingDoor.transform.position);
                    UseDoor(_pendingDoor);
                    break;
                case PendingKind.Search:
                    Think(_pendingSpot.Inspect() ? "누가 숨어 있었잖아!" : "아무도 없네.");
                    break;
                case PendingKind.Loot:
                    if (_pendingLoot.IsSearched)
                        OpenContainerWindow(_pendingLoot);
                    else
                        StartTimed(TimedKind.Loot, _pendingLoot, null, _pendingLoot.SearchDuration);
                    break;
                case PendingKind.Hide:
                    EnterHide(_pendingHide);
                    break;
                case PendingKind.Peek:
                    BeginPeek(_pendingDoor);
                    break;
                case PendingKind.Exit:
                    HasExited = true;
                    AiLog.Add($"Player left the location with {Items} item(s)");
                    break;
            }

            ClearPending();
        }

        // ---------------- Timed actions (scavenge, pick a lock, pry a door) ----------------

        private void StartTimed(TimedKind kind, LootPile loot, LockableDoor door, float duration)
        {
            if (IsAnyGuardInCombat())
            {
                ShowMessage("전투중엔 수집할 수 없습니다.");
                return;
            }

            if (kind == TimedKind.Loot && (loot == null || loot.IsEmpty))
                return;

            ClearAim();
            _timed = kind;
            _timedLoot = loot;
            _timedDoor = door;
            _timedStartedAt = Time.time;
            _timedEndsAt = Time.time + duration;
            _nextTimedNoise = Time.time;
            if (_animator != null)
                _animator.SetCower(true);
        }

        private void UpdateTimed(float now)
        {
            if (_timed == TimedKind.None)
                return;

            if (IsAnyGuardInCombat())
            {
                ShowMessage("전투중엔 수집할 수 없습니다.");
                CancelTimed();
                return;
            }

            // Prying is loud; picking a lock is quiet.
            if (now >= _nextTimedNoise && _timed != TimedKind.Loot)
            {
                bool pry = _timed == TimedKind.PryDoor;
                _nextTimedNoise = now + (pry ? _pryNoiseInterval : 1f);
                AiEventBus.EmitNoise(_timedDoor.transform.position, pry ? _config.RunNoise + 0.1f : 0.2f, AiIds.PlayerSourceId);
            }

            if (now < _timedEndsAt)
                return;

            var kind = _timed;
            var loot = _timedLoot;
            var door = _timedDoor;
            CancelTimed();
            switch (kind)
            {
                case TimedKind.Loot:
                    loot.MarkSearched();
                    OpenContainerWindow(loot);
                    break;
                case TimedKind.PickLock:
                    _hasLockpick = false;
                    door.ForceUnlock();
                    door.TryOpen(AiIds.PlayerSourceId, 0.2f);
                    Think("열렸다. 락픽은 부러졌네.");
                    break;
                case TimedKind.PryDoor:
                    door.ForceUnlock();
                    door.TryOpen(AiIds.PlayerSourceId, _config.DoorNoise);
                    Think("됐다, 열렸어.");
                    break;
            }
        }

        // ---------------- Container window (scavenging) ----------------

        private void OpenContainerWindow(LootPile loot)
        {
            if (loot == null)
                return;

            if (loot.IsEmpty)
            {
                Think("아무것도 없네.");
                return;
            }

            _openContainer = loot;
            Think($"{loot.DisplayName}... 쓸 만한 게 있나?");
        }

        public void CloseContainer() => _openContainer = null;

        // Moves a whole stack from the open container into the backpack (no weight limit).
        public void TakeFromContainer(int index)
        {
            var container = _openContainer;
            if (container == null || index < 0 || index >= container.Entries.Count)
                return;

            var entry = container.Entries[index];
            int taken = container.Take(index, entry.Count);
            if (entry.Name == Inventory.Ammo)
            {
                _ammo += taken;
            }
            else if (entry.Name == Inventory.Lockpick)
            {
                _hasLockpick = true;
                Think("락픽이다! 잠긴 문도 열 수 있겠어.");
            }
            else
            {
                _inventory[entry.Name] = (_inventory.TryGetValue(entry.Name, out int have) ? have : 0) + taken;
            }

        }

        public void TakeAllFromContainer()
        {
            var container = _openContainer;
            if (container == null)
                return;

            for (int i = container.Entries.Count - 1; i >= 0; i--)
                TakeFromContainer(i);
        }

        // The night ends at 05:00: warning at 04:30, then the survivor heads home automatically.
        private void UpdateNight()
        {
            if (!_dawnWarned && NightClock.IsDawnSoon)
            {
                _dawnWarned = true;
                ShowMessage("곧 날이 밝습니다.");
                Think("곧 날이 밝겠어. 서둘러 돌아가야 해.");
            }

            if (!NightClock.IsDawn || HasExited)
                return;

            EndedAtDawn = true;
            HasExited = true;
            StopAll();
            AiLog.Add("Dawn: scavenging ended");
        }

        private void CancelTimed()
        {
            if (_timed == TimedKind.None)
                return;

            _timed = TimedKind.None;
            _timedLoot = null;
            _timedDoor = null;
            if (_animator != null)
                _animator.SetCower(false);
        }

        private void UpdateMeleeAction(float now)
        {
            var target = _pendingTarget;
            if (target == null || target.IsDead || target.IsHidden)
            {
                ClearPending();
                return;
            }

            Vector3 targetPosition = target.transform.position;
            if (!InMeleeReach(targetPosition, 0f))
            {
                if (now < _nextRepathTime)
                    return;

                _nextRepathTime = now + RepathInterval;
                if (!MoveTo(targetPosition, _pendingRun))
                    ClearPending();
                return;
            }

            _mover.Stop();
            _mover.FaceTowards(targetPosition);
            if (now < _nextAttackTime || _impactAt < float.PositiveInfinity)
                return;

            // Unaware target: heavy sneak attack. Aware target: alternating blows it may block/dodge.
            bool stealth = !target.IsAlertedToPlayer;
            _impactIsShot = false;
            _impactStealth = stealth;
            _impactTarget = target;
            _impactDamage = stealth ? _stealthDamage : _attackDamage;
            _impactStagger = false;
            _impactAt = now + (stealth ? KickImpactDelay : AttackImpactDelay);
            _nextAttackTime = now + _attackCooldown;
            _meleePropUntil = now + 0.9f;
            UpdateProps(now);
            if (_animator != null)
                _animator.Play(stealth ? CharacterAction.Kick : _attackCount++ % 2 == 0 ? CharacterAction.Attack : CharacterAction.AttackAlt);

            AiEventBus.EmitNoise(transform.position, _config.AttackNoise, AiIds.PlayerSourceId);
            ClearPending();
        }

        private void UpdateImpact(float now)
        {
            if (now < _impactAt)
                return;

            _impactAt = float.PositiveInfinity;
            var target = _impactTarget;
            _impactTarget = null;
            if (target == null || target.IsDead)
                return;

            if (_impactIsShot)
            {
                if (!_impactHit)
                {
                    AiLog.Add($"Shot at {target.name} missed");
                    return;
                }

                target.ReceiveAttack(_impactDamage, false, _impactStagger, transform.position);
                AiLog.Add($"Shot {target.name} for {_impactDamage:0}{(_impactStagger ? " (stagger)" : "")}");
                return;
            }

            if (!InMeleeReach(target.transform.position, 0.4f))
                return;

            var result = target.ReceiveAttack(_impactDamage, !_impactStealth, false, transform.position);
            AiLog.Add(_impactStealth ? $"Sneak attack on {target.name}" : $"Attack on {target.name} ({result})");
        }

        private bool InMeleeReach(Vector3 position, float slack)
        {
            return AiMath.IsSameFloor(transform.position, position)
                && Vector3.Distance(transform.position, position) <= _attackRange + slack;
        }

        private void UseDoor(LockableDoor door)
        {
            if (door.IsOpen)
            {
                if (!door.TryClose(AiIds.PlayerSourceId, _config.DoorNoise))
                    Think("뭔가 걸려서 안 닫혀.");
                return;
            }

            if (door.TryOpen(AiIds.PlayerSourceId, _config.DoorNoise))
                return;

            // Locked: lockpick (quiet) > crowbar (loud, not on reinforced locks).
            if (_hasLockpick)
            {
                Think("락픽으로 따 보자...");
                StartTimed(TimedKind.PickLock, null, door, _pickLockDuration);
                return;
            }

            if (door.IsReinforced)
            {
                AiEventBus.EmitNoise(door.transform.position, _config.DoorNoise * 0.5f, AiIds.PlayerSourceId);
                Think("자물쇠가 너무 튼튼해. 쇠지레로는 안 되겠어... 락픽이 있다면 열 수 있을 텐데.");
                return;
            }

            Think("잠겼네. 쇠지레로 비틀어 열자... 시끄럽겠지만.");
            StartTimed(TimedKind.PryDoor, null, door, _pryDuration);
        }

        private void ClearPending()
        {
            _pending = PendingKind.None;
            _pendingDoor = null;
            _pendingSpot = null;
            _pendingLoot = null;
            _pendingTarget = null;
            _pendingHide = null;
        }

        // ---------------- Hiding / lure ----------------

        private void EnterHide(PlayerHideSpot spot)
        {
            if (spot == null)
                return;

            ClearAim();
            _hideSpot = spot;
            _mover.SetSpeed(_walkSpeed);
            _mover.MoveDirect(spot.HidePosition);
            _player.SetHidden(true);
            Think("여기라면 안 보이겠지...");
        }

        private void LeaveHide()
        {
            if (_hideSpot == null)
                return;

            var spot = _hideSpot;
            _hideSpot = null;
            _player.SetHidden(false);
            if (!_mover.IsMoving)
                _mover.MoveDirect(spot.ApproachPosition);
        }

        // ---------------- Peeking through a door ----------------

        // Crouch at a closed door and look through: the room behind is revealed from the door outward.
        private void BeginPeek(LockableDoor door)
        {
            if (door == null || door.IsOpen)
                return;

            Vector3 normal = AiMath.Flat(door.transform.forward).normalized;
            float side = Vector3.Dot(transform.position - door.transform.position, normal) >= 0f ? 1f : -1f;
            Vector3 beyond = door.transform.position - normal * side * 1.2f + Vector3.up;
            var rooms = RoomVolume.All;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (!rooms[i].Contains(beyond))
                    continue;

                ClearAim();
                _peekRoom = rooms[i];
                _peekRoom.BeginPeek(door.transform.position.x);
                _mover.FaceTowards(door.transform.position);
                Think("문 너머를 살펴보자...");
                return;
            }
        }

        private void EndPeek()
        {
            if (_peekRoom == null)
                return;

            _peekRoom.EndPeek();
            _peekRoom = null;
        }
    }
}
