using System.Collections.Generic;
using UnityEngine;
using static F1XR.RestAPI.Replay.ReplayCarVisualUtil;

namespace F1XR.RestAPI.Replay
{
    public partial class ReplayCarView
    {
        private const float SelectionBodyTint = 0.48f;
        private const float SelectionBodyEmission = 0.9f;

        private static readonly bool TintCarBody = false;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly List<Renderer> bodyRenderers = new();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> bodyBlocks = new();
        private bool bodyRenderersDirty = true;
        private float visualWidth;
        private float visualLength;

        private void ApplyBodyHighlight()
        {
            if (!TintCarBody)
                return;

            RefreshBodyRenderers();

            Color fxColor = CurrentSelectionFxColor();
            Color bodyColor = selected
                ? Color.Lerp(labelColor, fxColor, SelectionBodyTint)
                : labelColor;
            Color emissionColor = selected
                ? WithAlpha(fxColor * SelectionBodyEmission, 1f)
                : Color.black;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
                    continue;

                MaterialPropertyBlock block = BodyBlock(item);
                item.GetPropertyBlock(block);
                block.SetColor(BaseColorId, bodyColor);
                block.SetColor(ColorId, bodyColor);
                block.SetColor(EmissionColorId, emissionColor);
                item.SetPropertyBlock(block);
            }
        }

        private void RefreshBodyRenderers()
        {
            if (!bodyRenderersDirty)
                return;

            bodyRenderers.Clear();
            bodyBlocks.Clear();
            visualWidth = 0f;
            visualLength = 0f;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer item in renderers)
            {
                if (item == null || IsIgnoredRenderer(item))
                    continue;

                bodyRenderers.Add(item);
            }

            bodyRenderersDirty = false;
        }

        private MaterialPropertyBlock BodyBlock(Renderer renderer)
        {
            if (!bodyBlocks.TryGetValue(renderer, out MaterialPropertyBlock block) || block == null)
            {
                block = new MaterialPropertyBlock();
                bodyBlocks[renderer] = block;
            }

            return block;
        }

        private bool IsIgnoredRenderer(Renderer renderer)
        {
            return label != null && renderer.gameObject == label.gameObject ||
                labelBackground != null && renderer.gameObject == labelBackground.gameObject ||
                labelLine != null && renderer.gameObject == labelLine.gameObject ||
                labelTopDot != null && renderer.gameObject == labelTopDot.gameObject ||
                labelBottomDot != null && renderer.gameObject == labelBottomDot.gameObject ||
                IsSelectionEffectRenderer(renderer) ||
                IsLeaderEffectRenderer(renderer);
        }

        private bool TryGetCarBounds(out Bounds bounds)
        {
            RefreshBodyRenderers();
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = item.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(item.bounds);
                }
            }

            return hasBounds;
        }

        public float GetVisualWidth()
        {
            if (!bodyRenderersDirty && visualWidth > 0f)
                return visualWidth;

            visualWidth = GetVisualSize(Vector3.right);
            return visualWidth;
        }

        public float GetVisualLength()
        {
            if (!bodyRenderersDirty && visualLength > 0f)
                return visualLength;

            visualLength = GetVisualSize(Vector3.forward);
            return visualLength;
        }

        private float GetVisualSize(Vector3 axis)
        {
            RefreshBodyRenderers();
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;

            foreach (Renderer item in bodyRenderers)
            {
                if (item == null)
                    continue;

                Bounds bounds = item.localBounds;
                Matrix4x4 rendererToCar =
                    LogicalRoot.worldToLocalMatrix * item.transform.localToWorldMatrix;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    float value = Vector3.Dot(
                        rendererToCar.MultiplyPoint3x4(point),
                        axis);
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                }
            }

            return minimum <= maximum
                ? Mathf.Max(0.001f, maximum - minimum)
                : 0f;
        }

        private bool IsSelectionEffectRenderer(Renderer renderer)
        {
            return renderer != null &&
                selectionRoot != null &&
                renderer.transform.IsChildOf(selectionRoot);
        }

        private bool IsLeaderEffectRenderer(Renderer renderer)
        {
            return renderer != null &&
                leaderRoot != null &&
                renderer.transform.IsChildOf(leaderRoot);
        }
    }
}
