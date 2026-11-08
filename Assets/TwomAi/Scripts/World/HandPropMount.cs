using UnityEngine;

namespace TwomAi
{
    // One-handed prop (crowbar) held in a fist: the shaft runs through the fist along the
    // knuckle line (little finger -> index finger), the hook points the way the fingers curl.
    // Model convention: shaft along local +Z from the claw end (z = 0) to the hook end (z = Length).
    public sealed class HandPropMount : MonoBehaviour
    {
        [SerializeField] private Transform _hand;
        [SerializeField] private Transform _indexBase;
        [SerializeField] private Transform _middleBase;
        [SerializeField] private Transform _littleBase;
        [SerializeField] private float _length = 0.75f;
        [SerializeField, Range(0f, 1f)] private float _gripFraction = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _palmBlend = 0.6f;
        [SerializeField] private bool _flipShaft;

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            if (_hand == null || _indexBase == null || _littleBase == null)
                return;

            Vector3 middle = _middleBase != null ? _middleBase.position : _indexBase.position;
            Vector3 palm = Vector3.Lerp(_hand.position, middle, _palmBlend);
            Vector3 shaft = _indexBase.position - _littleBase.position;
            Vector3 fingers = middle - _hand.position;
            if (shaft.sqrMagnitude < 0.000001f || fingers.sqrMagnitude < 0.000001f)
                return;

            if (_flipShaft)
                shaft = -shaft;

            transform.rotation = Quaternion.LookRotation(shaft, Vector3.ProjectOnPlane(fingers, shaft));
            transform.position = palm - transform.rotation * (Vector3.forward * (_length * _gripFraction));
        }
    }
}
