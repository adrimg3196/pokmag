using UnityEngine;

namespace HoloTable.Entities
{
    /// <summary>
    /// Animation events and OnAnimatorIK only reach components on the Animator's own
    /// GameObject. This relay (auto-added at runtime) forwards them to the entity root.
    /// Add an Animation Event named <c>AnimEvent_AttackImpact</c> on the frame the claw /
    /// muzzle flash lands to sync projectiles and damage perfectly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimationEventRelay : MonoBehaviour
    {
        private LivingEntityController _owner;

        internal void Bind(LivingEntityController owner) => _owner = owner;

        // Called from animation clips.
        public void AnimEvent_AttackImpact()
        {
            if (_owner != null) _owner.NotifyAttackImpact();
        }

        // Called from spawn clips on the roar frame (camera shake / audio hooks).
        public void AnimEvent_Roar()
        {
            if (_owner != null) _owner.NotifyRoar();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_owner != null) _owner.HandleAnimatorIK(layerIndex);
        }
    }
}
