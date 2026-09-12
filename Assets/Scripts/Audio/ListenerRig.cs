using UnityEngine;

namespace CombatPrep.Audio
{
    /// <summary>
    /// Carries the single session AudioListener and follows whichever camera is live.
    ///
    /// It deliberately never re-parents. Unity marks a whole hierarchy for destruction the
    /// moment Destroy() is called on its root, and moving a child out afterwards does not
    /// rescue it - so a listener parented under the player camera dies with the player on
    /// the way back to the menu, taking all audio with it. Following by transform instead
    /// of by parenting keeps the listener permanently outside anything that gets torn down.
    /// </summary>
    public class ListenerRig : MonoBehaviour
    {
        public Transform Follow;

        /// <summary>Late, so it picks up the camera's final position for the frame.</summary>
        void LateUpdate()
        {
            if (Follow == null) return;
            transform.SetPositionAndRotation(Follow.position, Follow.rotation);
        }
    }
}
