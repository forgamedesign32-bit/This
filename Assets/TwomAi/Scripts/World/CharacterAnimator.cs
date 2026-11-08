using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Drives the shared humanoid animator: locomotion (Speed / MotionSpeed / Grounded) from movement,
    // plus combat actions (Mixamo clips wired by the scene builder) and a cower loop.
    public sealed class CharacterAnimator : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int CowerId = Animator.StringToHash("Cower");
        private static readonly int AimId = Animator.StringToHash("Aim");
        private static readonly int CowerSpeedId = Animator.StringToHash("CowerSpeed");
        private static readonly int CowerStateId = Animator.StringToHash("Cower");
        private static readonly int[] ActionIds =
        {
            Animator.StringToHash("Attack"),
            Animator.StringToHash("AttackAlt"),
            Animator.StringToHash("Kick"),
            Animator.StringToHash("HitReact"),
            Animator.StringToHash("Block"),
            Animator.StringToHash("Dodge"),
            Animator.StringToHash("Die"),
            Animator.StringToHash("Fire"),
            Animator.StringToHash("Stagger"),
        };

        // Seconds the character is committed to the action (no locomotion blend shown meanwhile).
        private static readonly float[] ActionDurations = { 0.8f, 0.9f, 1.1f, 0.6f, 0.7f, 0.9f, 99f, 0.45f, 0.7f };

        [SerializeField] private float _speedDamp = 10f;
        [Tooltip("Cower/beg clip: after kneeling down once, only this normalized segment repeats.")]
        [SerializeField, Range(0f, 1f)] private float _cowerLoopStart = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _cowerLoopEnd = 0.92f;

        private readonly HashSet<int> _parameters = new HashSet<int>();
        private Animator _animator;
        private NpcMover _mover;
        private IGraphAgent _agent;
        private float _speed;
        private float _busyUntil;
        private bool _isDead;
        private bool _cowering;

        public bool IsBusy => Time.time < _busyUntil;
        public bool HasDeathAnimation => _parameters.Contains(ActionIds[(int)CharacterAction.Die]);

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _mover = GetComponent<NpcMover>();
            _agent = GetComponent<IGraphAgent>();
            if (_animator == null)
                return;

            _animator.applyRootMotion = false;
            foreach (var parameter in _animator.parameters)
                _parameters.Add(parameter.nameHash);
        }

        public void Play(CharacterAction action)
        {
            if (_animator == null || _isDead)
                return;

            if (action == CharacterAction.Die)
            {
                SetCower(false);
                SetAim(false);
                _isDead = true;
            }

            int id = ActionIds[(int)action];
            if (!_parameters.Contains(id))
                return;

            _animator.SetTrigger(id);
            _busyUntil = Time.time + ActionDurations[(int)action];
        }

        // Rifle stance (aim pose; walking while aiming uses the rifle walk clip).
        public void SetAim(bool aim)
        {
            if (_animator != null && !_isDead && _parameters.Contains(AimId))
                _animator.SetBool(AimId, aim);
        }

        public void SetCower(bool cower)
        {
            if (_animator == null || !_parameters.Contains(CowerId) || _cowering == cower)
                return;

            _cowering = cower;
            _animator.SetBool(CowerId, cower);
            if (_parameters.Contains(CowerSpeedId))
                _animator.SetFloat(CowerSpeedId, 1f);
        }

        // Kneel down once, then repeat only the begging part (never back to standing).
        private void LoopCowerSegment()
        {
            if (!_cowering)
                return;

            var state = _animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != CowerStateId || _animator.IsInTransition(0))
                return;

            if (state.normalizedTime >= _cowerLoopEnd)
                _animator.Play(CowerStateId, 0, _cowerLoopStart);
        }

        private void Update()
        {
            if (_animator == null || !_animator.enabled || !_parameters.Contains(SpeedId))
                return;

            bool canMove = _mover != null && !_isDead && (_agent == null || !_agent.IsIncapacitated);
            float target = canMove ? _mover.CurrentSpeed : 0f;
            _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-_speedDamp * Time.deltaTime));
            _animator.SetFloat(SpeedId, _speed);
            LoopCowerSegment();
            if (_parameters.Contains(MotionSpeedId))
                _animator.SetFloat(MotionSpeedId, 1f);
            if (_parameters.Contains(GroundedId))
                _animator.SetBool(GroundedId, true);
        }
    }
}
