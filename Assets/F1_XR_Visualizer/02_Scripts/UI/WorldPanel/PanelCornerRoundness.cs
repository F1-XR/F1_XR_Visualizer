using UnityEngine;
using UnityEngine.UI;

namespace F1XR.UI.WorldPanel
{
    [ExecuteAlways]
    public sealed class PanelCornerRoundness : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 4f)] float cornerRoundness = 1f;

        void Awake()
        {
            Apply();
        }

        void OnValidate()
        {
            Apply();
        }

        void Apply()
        {
            var images = GetComponentsInChildren<Image>(includeInactive: true);
            foreach (var image in images)
            {
                if (image.sprite == null || image.sprite.border == Vector4.zero)
                    continue;

                image.pixelsPerUnitMultiplier = cornerRoundness;
                image.SetVerticesDirty();
            }
        }
    }
}
