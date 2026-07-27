using UnityEngine;

namespace F1XR.PlayPanel
{
    /// <summary>
    /// Gentle "alive" idle motion for a floating 3D object: a slow vertical bob plus a subtle left/right
    /// yaw rock (so a flat glyph never turns fully edge-on and becomes unreadable). Applied to the top
    /// play icon. Runs in edit mode too so the pose previews in the Scene view.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class FloatingIdle : MonoBehaviour
    {
        [Header("Bob (local Y)")]
        [SerializeField, Min(0f)] float bobAmplitude = 0.008f;
        [SerializeField, Min(0f)] float bobSpeed = 1.1f;

        [Header("Yaw rock (local Y axis)")]
        [SerializeField, Min(0f)] float rockAngle = 10f;
        [SerializeField, Min(0f)] float rockSpeed = 0.7f;

        Vector3 baseLocalPos;
        Quaternion baseLocalRot;
        bool captured;

        void OnEnable()
        {
            baseLocalPos = transform.localPosition;
            baseLocalRot = transform.localRotation;
            captured = true;
        }

        void OnDisable()
        {
            if (captured)
            {
                transform.localPosition = baseLocalPos;
                transform.localRotation = baseLocalRot;
            }
        }

        void Update()
        {
            if (!captured)
                return;

            float t = Application.isPlaying
                ? Time.time
#if UNITY_EDITOR
                : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
                : Time.time;
#endif

            float bob = Mathf.Sin(t * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
            float yaw = Mathf.Sin(t * rockSpeed * Mathf.PI * 2f) * rockAngle;

            transform.localPosition = baseLocalPos + new Vector3(0f, bob, 0f);
            transform.localRotation = baseLocalRot * Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
