using UnityEngine;

namespace Before1800
{
    public sealed class InteractionTarget : MonoBehaviour
    {
        public string targetId;
        public string displayName;
        [TextArea] public string description;
        public float range = 3.0f;
    }
}
