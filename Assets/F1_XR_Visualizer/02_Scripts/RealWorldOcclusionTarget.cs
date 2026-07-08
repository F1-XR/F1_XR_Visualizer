using System.Collections.Generic;
using UnityEngine;

namespace F1XR.AR
{
    public sealed class RealWorldOcclusionTarget : MonoBehaviour
    {
        [SerializeField] int renderQueue = 3001;

        readonly List<Renderer> renderers = new();

        void OnEnable()
        {
            Apply();
        }

        void OnTransformChildrenChanged()
        {
            Apply();
        }

        public void Apply()
        {
            renderers.Clear();
            GetComponentsInChildren(true, renderers);

            foreach (Renderer item in renderers)
            {
                if (item == null)
                    continue;

                Material[] materials = item.materials;
                foreach (Material material in materials)
                {
                    if (material != null)
                        material.renderQueue = renderQueue;
                }
            }
        }
    }
}
