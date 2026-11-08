using UnityEngine;

namespace TwomAi
{
    // Receives the Starter Assets clip events after ThirdPersonController is stripped from the model,
    // so the animator does not log "has no receiver" errors. Footstep noise is handled by NoiseEmitter.
    public sealed class AnimationEventSink : MonoBehaviour
    {
        private void OnFootstep(AnimationEvent animationEvent)
        {
        }

        private void OnLand(AnimationEvent animationEvent)
        {
        }
    }
}
