using UnityEngine;

namespace TwomAi
{
    // Places the rifle from the animated hands every frame (after the Animator).
    // Armed characters always play rifle clips (aim idle / rifle walk / fire), so both hands are on the gun:
    // the pistol grip sits in the right palm and the barrel points from the right palm toward the left palm.
    // Barrel / up axes are detected from the mesh bounds; flip flags fix assets with other conventions.
    public sealed class WeaponMount : MonoBehaviour
    {
        [SerializeField] private Transform _rightHand;
        [SerializeField] private Transform _rightFinger;
        [SerializeField] private Transform _leftHand;
        [SerializeField] private Transform _leftFinger;
        [SerializeField] private bool _flipBarrel;
        [SerializeField] private bool _flipUp;
        [Tooltip("Pistol grip position along the gun: 0 = stock end, 1 = muzzle end.")]
        [SerializeField, Range(0f, 1f)] private float _gripFraction = 0.33f;
        [Tooltip("Pistol grip height: 0 = barrel line, 1 = lowest point of the gun.")]
        [SerializeField, Range(0f, 1f)] private float _gripDepth = 0.45f;
        [Tooltip("Handguard (left hand) position along the gun: 0 = stock end, 1 = muzzle end.")]
        [SerializeField, Range(0f, 1f)] private float _guardFraction = 0.62f;
        [Tooltip("Palm point between wrist (0) and middle-finger base (1).")]
        [SerializeField, Range(0f, 1f)] private float _palmBlend = 0.55f;
        [SerializeField] private float _rollDegrees;

        private Vector3 _gripLocal;
        private Vector3 _guardLocal;
        private Vector3 _muzzleLocal;
        private Vector3 _upLocal = Vector3.up;

        public Vector3 MuzzlePosition => transform.TransformPoint(_muzzleLocal);

        private void Awake()
        {
            transform.localRotation = Quaternion.identity;
            DetectAxes();
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        // Two-point fit: right palm on the pistol grip, left palm on the handguard (error split at the midpoint).
        private void LateUpdate()
        {
            if (_rightHand == null || _leftHand == null)
                return;

            Vector3 rightPalm = Palm(_rightHand, _rightFinger);
            Vector3 leftPalm = Palm(_leftHand, _leftFinger);
            Vector3 handVector = leftPalm - rightPalm;
            Vector3 modelVector = _guardLocal - _gripLocal;
            if (handVector.sqrMagnitude < 0.0001f || modelVector.sqrMagnitude < 0.0001f)
                return;

            Vector3 modelUp = Vector3.ProjectOnPlane(_upLocal, modelVector);
            Vector3 worldUp = Vector3.ProjectOnPlane(Vector3.up, handVector);
            if (worldUp.sqrMagnitude < 0.0001f)
                worldUp = Vector3.ProjectOnPlane(transform.parent != null ? transform.parent.forward : Vector3.forward, handVector);

            Quaternion world = Quaternion.LookRotation(handVector, worldUp) * Quaternion.AngleAxis(_rollDegrees, Vector3.forward);
            Quaternion model = Quaternion.LookRotation(modelVector, modelUp);
            transform.rotation = world * Quaternion.Inverse(model);

            Vector3 midLocal = (_gripLocal + _guardLocal) * 0.5f;
            transform.position = (rightPalm + leftPalm) * 0.5f - transform.rotation * Vector3.Scale(midLocal, transform.lossyScale);
        }

        private Vector3 Palm(Transform hand, Transform finger)
        {
            return finger != null ? Vector3.Lerp(hand.position, finger.position, _palmBlend) : hand.position;
        }

        private void DetectAxes()
        {
            if (!TryGetLocalBounds(out Bounds bounds, null))
                return;

            // Longest extent = barrel, middle extent = height (magazine/grip side), shortest = width.
            Vector3 size = bounds.size;
            int barrel = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int a = (barrel + 1) % 3;
            int b = (barrel + 2) % 3;
            int height = size[a] >= size[b] ? a : b;

            bool hasTrigger = TryGetLocalBounds(out Bounds trigger, "trigger");
            bool hasMag = TryGetLocalBounds(out Bounds mag, "mag");
            float barrelSign;
            float upSign;
            if (hasTrigger && hasMag)
            {
                // Magazine sits forward of the trigger and hangs below the receiver.
                barrelSign = mag.center[barrel] >= trigger.center[barrel] ? 1f : -1f;
                upSign = mag.center[height] <= bounds.center[height] ? 1f : -1f;
            }
            else
            {
                barrelSign = Mathf.Abs(bounds.max[barrel]) >= Mathf.Abs(bounds.min[barrel]) ? 1f : -1f;
                upSign = Mathf.Abs(bounds.min[height]) >= Mathf.Abs(bounds.max[height]) ? 1f : -1f;
            }

            if (_flipBarrel) barrelSign = -barrelSign;
            if (_flipUp) upSign = -upSign;

            _upLocal = Vector3.zero;
            _upLocal[height] = upSign;

            float stockEnd = barrelSign > 0f ? bounds.min[barrel] : bounds.max[barrel];
            float muzzleEnd = barrelSign > 0f ? bounds.max[barrel] : bounds.min[barrel];
            float topEnd = upSign > 0f ? bounds.max[height] : bounds.min[height];
            float bottomEnd = upSign > 0f ? bounds.min[height] : bounds.max[height];
            float barrelLine = Mathf.Lerp(topEnd, bottomEnd, 0.2f);

            if (hasTrigger && hasMag)
            {
                // Right palm: pistol grip, just below and behind the trigger.
                _gripLocal = trigger.center;
                _gripLocal[barrel] -= barrelSign * 0.03f;
                _gripLocal[height] -= upSign * 0.045f;

                // Left palm: under the handguard, ahead of the magazine, just below the barrel line.
                float magTop = upSign > 0f ? mag.max[height] : mag.min[height];
                float magFront = barrelSign > 0f ? mag.max[barrel] : mag.min[barrel];
                _guardLocal = bounds.center;
                _guardLocal[barrel] = magFront + barrelSign * 0.14f;
                _guardLocal[height] = magTop + upSign * 0.01f;
            }
            else
            {
                _gripLocal = bounds.center;
                _gripLocal[barrel] = Mathf.Lerp(stockEnd, muzzleEnd, _gripFraction);
                _gripLocal[height] = Mathf.Lerp(barrelLine, bottomEnd, _gripDepth);
                _guardLocal = bounds.center;
                _guardLocal[barrel] = Mathf.Lerp(stockEnd, muzzleEnd, _guardFraction);
                _guardLocal[height] = Mathf.Lerp(barrelLine, bottomEnd, 0.12f);
            }

            _muzzleLocal = bounds.center;
            _muzzleLocal[barrel] = muzzleEnd;
            _muzzleLocal[height] = barrelLine;
        }

        // Bounds in this object's local space of all meshes, or only of children whose name contains `part`.
        private bool TryGetLocalBounds(out Bounds bounds, string part)
        {
            bounds = default;
            bool hasBounds = false;
            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;

                if (part != null && filters[i].name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Bounds meshBounds = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                        (c & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                        (c & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
                    Vector3 local = transform.InverseTransformPoint(filters[i].transform.TransformPoint(corner));
                    if (!hasBounds)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return hasBounds;
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                return;

            // Re-detect in the unrotated frame so live tuning is stable.
            var rotation = transform.rotation;
            transform.rotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            DetectAxes();
            transform.rotation = rotation;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.TransformPoint(_gripLocal), 0.03f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(_guardLocal), 0.03f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(MuzzlePosition, 0.03f);
        }
    }
}
