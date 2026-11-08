using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Kinematic mover along the waypoint graph (shared by NPCs and the click-controlled player).
    // Moves in 3D so stair edges change height; straight-line shortcuts only on the same floor.
    public sealed class NpcMover : MonoBehaviour
    {
        private const float OcclusionCheckInterval = 0.25f;

        [SerializeField] private float _turnSpeed = 720f;
        [SerializeField] private float _arriveDistance = 0.12f;
        [Tooltip("At or above this speed a closed (unlocked) door is kicked open instead of opened.")]
        [SerializeField] private float _kickSpeed = 3f;
        [SerializeField] private float _kickNoise = 1.1f;
        [SerializeField] private float _kickPause = 0.4f;

        private readonly List<Waypoint> _path = new List<Waypoint>();
        private readonly List<Waypoint> _altPath = new List<Waypoint>();

        private IGraphAgent _agent;
        private CharacterAnimator _animator;
        private float _pauseUntil;
        private WaypointGraph _graph;
        private int _pathIndex;
        private Vector3 _finalPoint;
        private bool _hasFinalPoint;
        private bool _isDirect;
        private Vector3 _directTarget;
        private float _speed = 2f;
        private float _nextOcclusionCheck;

        public bool IsMoving { get; private set; }
        public bool HasArrived { get; private set; }
        public bool IsBlocked { get; private set; }
        public Vector3 Destination { get; private set; }
        public float CurrentSpeed { get; private set; }
        public IReadOnlyList<Waypoint> Path => _path;

        // Loudness emitted when this agent opens a closed door on its path (set by the player).
        public float DoorOpenNoise { get; set; }

        private WaypointGraph Graph => _graph != null ? _graph : (_graph = WaypointGraph.Instance);

        private void Awake()
        {
            _agent = GetComponent<IGraphAgent>();
            _animator = GetComponent<CharacterAnimator>();
        }

        public void SetSpeed(float speed) => _speed = speed;

        // Penalty != null disables the straight-line shortcut so the cost is respected.
        public bool SetDestination(Vector3 target, Func<Waypoint, float> nodePenalty = null)
        {
            BeginMove();
            Destination = target;
            Vector3 from = transform.position;
            if (nodePenalty == null && AiMath.IsSameFloor(from, target)
                && !AiPhysics.IsOccluded(from + Vector3.up, target + Vector3.up))
            {
                _isDirect = true;
                _directTarget = target;
                return true;
            }

            var graph = Graph;
            var goal = graph.FindNearest(target, true);
            bool found = graph.TryGetSlopedEdgeAt(from, out var stairA, out var stairB)
                ? TryPathFromStairs(graph, from, stairA, stairB, goal, nodePenalty)
                : TryPathFromNearest(graph, from, goal, nodePenalty);
            if (!found)
            {
                Fail();
                return false;
            }

            _finalPoint = target;
            _hasFinalPoint = AiMath.IsSameFloor(goal.Position, target)
                && !AiPhysics.IsOccluded(goal.Position + Vector3.up, target + Vector3.up);
            return true;
        }

        private bool TryPathFromNearest(WaypointGraph graph, Vector3 from, Waypoint goal, Func<Waypoint, float> penalty)
        {
            var start = graph.FindNearest(from, true);
            return start != null && goal != null && graph.TryFindPath(start, goal, _path, penalty);
        }

        // On the stairs the only legal exits are the two stair ends (never straight through the air).
        private bool TryPathFromStairs(WaypointGraph graph, Vector3 from, Waypoint a, Waypoint b, Waypoint goal, Func<Waypoint, float> penalty)
        {
            if (goal == null)
                return false;

            bool okA = graph.TryFindPath(a, goal, _path, penalty);
            float costA = okA ? Vector3.Distance(from, a.Position) + WaypointGraph.PathLength(_path) : float.MaxValue;
            bool okB = graph.TryFindPath(b, goal, _altPath, penalty);
            float costB = okB ? Vector3.Distance(from, b.Position) + WaypointGraph.PathLength(_altPath) : float.MaxValue;
            if (!okA && !okB)
                return false;

            if (costB < costA)
            {
                _path.Clear();
                _path.AddRange(_altPath);
            }

            return true;
        }

        // Same-floor straight move (chase with visual contact, hide spot inside a room).
        public void MoveDirect(Vector3 target)
        {
            Destination = target;
            if (IsMoving && _isDirect)
            {
                _directTarget = target;
                return;
            }

            BeginMove();
            _isDirect = true;
            _directTarget = target;
        }

        public void Stop()
        {
            IsMoving = false;
            _isDirect = false;
            _path.Clear();
            CurrentSpeed = 0f;
        }

        public void FaceTowards(Vector3 point)
        {
            Vector3 direction = AiMath.Flat(point - transform.position);
            if (direction.sqrMagnitude > 0.0001f)
                RotateTowards(direction.normalized);
        }

        private void Update()
        {
            if (!IsMoving)
            {
                CurrentSpeed = 0f;
                return;
            }

            if (_agent != null && _agent.IsIncapacitated)
            {
                Stop();
                return;
            }

            if (!TryGetCurrentGoal(out Vector3 goal))
            {
                Finish();
                return;
            }

            if (!HandleDoorOnSegment())
                return;

            if (Time.time < _pauseUntil)
            {
                CurrentSpeed = 0f;
                return;
            }

            if (_isDirect && Time.time >= _nextOcclusionCheck)
            {
                _nextOcclusionCheck = Time.time + OcclusionCheckInterval;
                // A door closed in front of a straight move: fall back to the graph.
                if (AiPhysics.IsOccluded(transform.position + Vector3.up, goal + Vector3.up))
                {
                    SetDestination(goal);
                    return;
                }
            }

            Vector3 position = transform.position;
            Vector3 toGoal = goal - position;
            float distance = toGoal.magnitude;
            if (distance <= _arriveDistance)
            {
                transform.position = goal;
                Advance();
                return;
            }

            float step = Mathf.Min(_speed * Time.deltaTime, distance);
            transform.position = position + toGoal / distance * step;
            CurrentSpeed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;

            Vector3 flat = AiMath.Flat(toGoal);
            if (flat.sqrMagnitude > 0.0001f)
                RotateTowards(flat.normalized);
        }

        private bool HandleDoorOnSegment()
        {
            if (_isDirect || _pathIndex <= 0 || _pathIndex >= _path.Count)
                return true;

            var door = Graph.GetDoor(_path[_pathIndex - 1], _path[_pathIndex]);
            if (door == null)
                return true;

            if (door.IsLocked)
            {
                Fail();
                return false;
            }

            if (door.IsOpen)
                return true;

            // Act on the door only when reaching it (the swing / kick happens at the doorway).
            if (AiMath.Distance(transform.position, door.transform.position) > 1.1f)
                return true;

            int id = _agent != null ? _agent.AgentId : -1;
            if (_speed >= _kickSpeed)
            {
                // Running into a closed door: kick it open (loud, short stop).
                door.TryOpen(id, _kickNoise);
                _pauseUntil = Time.time + _kickPause;
                if (_animator != null)
                    _animator.Play(CharacterAction.Kick);
            }
            else
            {
                door.TryOpen(id, DoorOpenNoise);
            }

            return true;
        }

        private bool TryGetCurrentGoal(out Vector3 goal)
        {
            if (_isDirect)
            {
                goal = _directTarget;
                return true;
            }

            if (_pathIndex < _path.Count)
            {
                goal = _path[_pathIndex].Position;
                return true;
            }

            goal = _finalPoint;
            return _hasFinalPoint;
        }

        private void Advance()
        {
            if (_isDirect)
            {
                Finish();
                return;
            }

            if (_pathIndex < _path.Count)
            {
                _pathIndex++;
                return;
            }

            _hasFinalPoint = false;
        }

        private void BeginMove()
        {
            _path.Clear();
            _pathIndex = 0;
            _hasFinalPoint = false;
            _isDirect = false;
            IsMoving = true;
            HasArrived = false;
            IsBlocked = false;
            _nextOcclusionCheck = Time.time + OcclusionCheckInterval;
        }

        private void Finish()
        {
            IsMoving = false;
            HasArrived = true;
            CurrentSpeed = 0f;
        }

        private void Fail()
        {
            IsMoving = false;
            HasArrived = false;
            IsBlocked = true;
            CurrentSpeed = 0f;
        }

        private void RotateTowards(Vector3 direction)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                _turnSpeed * Time.deltaTime);
        }
    }
}
