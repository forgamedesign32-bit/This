using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Door on a waypoint edge. Local forward = door normal (sides), local right = door width.
    public sealed class LockableDoor : MonoBehaviour
    {
        private const int NoOwner = int.MinValue;
        private const int LevelOwner = -100;
        private static readonly List<LockableDoor> AllDoors = new List<LockableDoor>();
        private static readonly Collider[] OverlapBuffer = new Collider[8];

        [SerializeField] private Waypoint _sideA;
        [SerializeField] private Waypoint _sideB;
        [SerializeField] private Transform _hinge;
        [SerializeField] private Collider _panelCollider;
        [SerializeField] private Renderer _panelRenderer;
        [SerializeField] private bool _startOpen;
        [Tooltip("Locked from the start by the level (not by an NPC).")]
        [SerializeField] private bool _startLocked;
        [Tooltip("Heavy lock: only a lockpick opens it (a crowbar is not enough).")]
        [SerializeField] private bool _reinforced;
        [SerializeField] private Vector3 _clearanceHalfExtents = new Vector3(0.8f, 1f, 0.5f);
        [SerializeField] private float _openAngle = 95f;
        [SerializeField] private float _swingSpeed = 260f;

        private bool _isOpen;
        private bool _isLocked;
        private int _lockOwnerId = NoOwner;
        private float _currentAngle;

        public static IReadOnlyList<LockableDoor> All => AllDoors;

        public Waypoint SideA => _sideA;
        public Waypoint SideB => _sideB;
        public bool IsOpen => _isOpen;
        public bool IsLocked => _isLocked;
        public bool IsReinforced => _isLocked && _reinforced && _lockOwnerId == LevelOwner;

        public event Action<LockableDoor> StateChanged;

        private void Awake()
        {
            bool open = _startOpen && !_startLocked;
            ApplyOpen(open, false);
            _currentAngle = open ? _openAngle : 0f;
            if (_startLocked)
            {
                _isLocked = true;
                _lockOwnerId = LevelOwner;
                RefreshVisual();
            }
            if (_hinge != null)
                _hinge.localRotation = Quaternion.Euler(0f, _currentAngle, 0f);
        }

        // Smooth swing like the original (logical state changes instantly, the panel follows).
        private void Update()
        {
            if (_hinge == null)
                return;

            float target = _isOpen ? _openAngle : 0f;
            if (Mathf.Approximately(_currentAngle, target))
                return;

            _currentAngle = Mathf.MoveTowards(_currentAngle, target, _swingSpeed * Time.deltaTime);
            _hinge.localRotation = Quaternion.Euler(0f, _currentAngle, 0f);
        }

        private void OnEnable() => AllDoors.Add(this);
        private void OnDisable() => AllDoors.Remove(this);

        public bool TryOpen(int actorId, float noiseLoudness)
        {
            if (_isLocked)
                return false;

            if (_isOpen)
                return true;

            ApplyOpen(true, true);
            EmitNoise(actorId, noiseLoudness);
            return true;
        }

        public bool TryClose(int actorId, float noiseLoudness)
        {
            if (!_isOpen)
                return true;

            if (IsDoorwayObstructed())
                return false;

            ApplyOpen(false, true);
            EmitNoise(actorId, noiseLoudness);
            return true;
        }

        public bool TryLock(int actorId)
        {
            if (_isOpen || IsDoorwayObstructed())
                return false;

            if (_isLocked)
                return _lockOwnerId == actorId;

            _isLocked = true;
            _lockOwnerId = actorId;
            RefreshVisual();
            StateChanged?.Invoke(this);
            return true;
        }

        public bool Unlock(int actorId)
        {
            if (!_isLocked)
                return true;

            if (_lockOwnerId != actorId)
                return false;

            _isLocked = false;
            _lockOwnerId = NoOwner;
            RefreshVisual();
            StateChanged?.Invoke(this);
            return true;
        }

        // Player tools (lockpick / crowbar) remove any lock.
        public void ForceUnlock()
        {
            if (!_isLocked)
                return;

            _isLocked = false;
            _lockOwnerId = NoOwner;
            RefreshVisual();
            StateChanged?.Invoke(this);
        }

        // Anyone (player or living NPC) standing inside the door frame blocks closing/locking.
        public bool IsDoorwayObstructed()
        {
            int count = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up, _clearanceHalfExtents, OverlapBuffer,
                transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = OverlapBuffer[i];
                if (collider.GetComponentInParent<PlayerTarget>() != null)
                    return true;

                var npc = collider.GetComponentInParent<NpcActor>();
                if (npc != null && !npc.IsDead)
                    return true;
            }

            return false;
        }

        // True when position is on the same side as reference and clear of the frame by margin.
        public bool IsFullyOnSide(Vector3 position, Vector3 reference, float margin = 0.6f)
        {
            Vector3 normal = AiMath.Flat(transform.forward).normalized;
            float a = Vector3.Dot(AiMath.Flat(position - transform.position), normal);
            float b = Vector3.Dot(AiMath.Flat(reference - transform.position), normal);
            return Mathf.Sign(a) == Mathf.Sign(b) && Mathf.Abs(a) >= margin;
        }

        private void EmitNoise(int actorId, float loudness)
        {
            if (loudness > 0f)
                AiEventBus.EmitNoise(transform.position, loudness, actorId);
        }

        private void ApplyOpen(bool open, bool notify)
        {
            _isOpen = open;

            if (_panelCollider != null)
                _panelCollider.enabled = !open;

            RefreshVisual();
            if (notify)
                StateChanged?.Invoke(this);
        }

        private void RefreshVisual()
        {
            if (_panelRenderer == null)
                return;

            Color color = _isLocked ? new Color(0.8f, 0.15f, 0.15f) : new Color(0.45f, 0.3f, 0.18f);
            _panelRenderer.material.color = color;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isLocked ? Color.red : (_isOpen ? Color.green : Color.yellow);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, _clearanceHalfExtents * 2f);
        }
    }
}
