using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public class CarAgent : MonoBehaviour
    {
        public int driverNumber;
        public Vector3 rawPosition;

        private const float LabelSizeRatio = 0.7f;
        private const float LabelGapRatio = 0.25f;
        private TextMesh label;

        public void Init(int number)
        {
            driverNumber = number;
            name = $"Car_{number}";
            SetLabel(number.ToString());
        }

        public void SetPosition(Vector3 position)
        {
            rawPosition = position;
            transform.position = position;
        }

        public void SetLabel(string text)
        {
            if (label == null)
                label = CreateLabel();

            label.text = string.IsNullOrWhiteSpace(text)
                ? driverNumber.ToString()
                : text;
        }
        
        public void SetColor(Color color)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();

            foreach (Renderer item in renderers)
            {
                if (label != null && item.gameObject == label.gameObject)
                    continue;

                MaterialPropertyBlock block = new();
                item.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                item.SetPropertyBlock(block);
            }
        }

        private TextMesh CreateLabel()
        {
            GameObject obj = new GameObject("DriverLabel");
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            TextMesh text = obj.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 32;
            text.characterSize = 0.01f;
            text.color = Color.yellow;
            text.text = driverNumber.ToString();

            return text;
        }

        private void LateUpdate()
        {
            if (label == null || Camera.main == null)
                return;

            UpdateLabelLayout();
            label.transform.rotation = Camera.main.transform.rotation;
        }

        private void UpdateLabelLayout()
        {
            if (!TryGetCarBounds(out Bounds bounds))
                return;

            float carSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float textHeight = carSize * LabelSizeRatio;
            float inheritedScale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))
                )
            );

            label.characterSize = textHeight / (label.fontSize * inheritedScale);
            label.transform.position = new Vector3(
                bounds.center.x,
                bounds.max.y + textHeight * LabelGapRatio,
                bounds.center.z
            );
        }

        private bool TryGetCarBounds(out Bounds bounds)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer item in renderers)
            {
                if (label != null && item.gameObject == label.gameObject)
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
    }
}
