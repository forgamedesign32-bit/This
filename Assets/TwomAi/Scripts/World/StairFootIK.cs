using UnityEngine;

namespace TwomAi
{
    // Plants each foot on the stair tread under it (humanoid foot IK, base layer IK pass).
    // On flat floors no StairStep is found and the animation is left untouched.
    [RequireComponent(typeof(Animator))]
    public sealed class StairFootIK : MonoBehaviour
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        [SerializeField] private float _footHeight = 0.08f;
        [SerializeField] private float _probeUp = 0.45f;
        [SerializeField] private float _probeDown = 0.6f;
        [SerializeField] private float _bodySmoothing = 12f;

        private Animator _animator;
        private float _bodyOffset;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0)
                return;

            bool left = Probe(AvatarIKGoal.LeftFoot, out Vector3 leftTarget, out float leftDelta);
            bool right = Probe(AvatarIKGoal.RightFoot, out Vector3 rightTarget, out float rightDelta);

            // Lower the hips when a tread is below the animated foot so the leg can reach it.
            float lowest = Mathf.Min(left ? leftDelta : 0f, right ? rightDelta : 0f, 0f);
            _bodyOffset = Mathf.Lerp(_bodyOffset, lowest, 1f - Mathf.Exp(-_bodySmoothing * Time.deltaTime));
            _animator.bodyPosition += Vector3.up * _bodyOffset;

            Apply(AvatarIKGoal.LeftFoot, left, leftTarget);
            Apply(AvatarIKGoal.RightFoot, right, rightTarget);
        }

        private bool Probe(AvatarIKGoal goal, out Vector3 target, out float delta)
        {
            Vector3 foot = _animator.GetIKPosition(goal);
            target = foot;
            delta = 0f;
            int count = Physics.RaycastNonAlloc(foot + Vector3.up * _probeUp, Vector3.down, Hits, _probeUp + _probeDown,
                ~0, QueryTriggerInteraction.Collide);
            float bestY = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!Hits[i].collider.TryGetComponent(out StairStep _) || Hits[i].point.y <= bestY)
                    continue;

                bestY = Hits[i].point.y;
            }

            if (float.IsNegativeInfinity(bestY))
                return false;

            target.y = bestY + _footHeight;
            delta = target.y - foot.y;
            return true;
        }

        private void Apply(AvatarIKGoal goal, bool active, Vector3 target)
        {
            _animator.SetIKPositionWeight(goal, active ? 1f : 0f);
            if (active)
                _animator.SetIKPosition(goal, target);
        }
    }
}
