using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.Input
{
    /// <summary>
    /// Plays a one-shot sound each time something is socketed into the target XRSocketInteractor
    /// (e.g. a tire mounted onto the wheel socket). Auto-uses the socket on this object if not set.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaySoundOnSocketAttach : MonoBehaviour
    {
        [Tooltip("Socket to watch. Defaults to an XRSocketInteractor on this GameObject.")]
        [SerializeField] XRSocketInteractor socket;
        [Tooltip("Sound played once on attach. Defaults to an AudioSource on this GameObject.")]
        [SerializeField] AudioSource sound;
        [Tooltip("Skip playing for anything already socketed when the scene starts.")]
        [SerializeField] bool ignoreInitialAttach = true;
        [Tooltip("Ignore repeat attach events within this many seconds so rapid re-fires don't " +
            "restart the sound (which makes it silent). One play per mount.")]
        [SerializeField, Min(0f)] float retriggerCooldown = 0.4f;

        bool started;
        float lastPlayTime = -999f;

        void Awake()
        {
            if (socket == null)
                socket = GetComponent<XRSocketInteractor>();
            if (sound == null)
                sound = GetComponent<AudioSource>();
            if (sound != null)
            {
                sound.loop = false;
                sound.playOnAwake = false;
            }
        }

        void OnEnable()
        {
            if (socket != null)
                socket.selectEntered.AddListener(OnAttached);
        }

        void OnDisable()
        {
            if (socket != null)
                socket.selectEntered.RemoveListener(OnAttached);
        }

        void Start()
        {
            started = true;
        }

        void OnAttached(SelectEnterEventArgs args)
        {
            if (ignoreInitialAttach && !started)
                return;
            if (Time.unscaledTime - lastPlayTime < retriggerCooldown)
                return; // rapid re-fire; let the current play ring out
            if (sound != null && sound.clip != null)
            {
                lastPlayTime = Time.unscaledTime;
                sound.time = 0f;
                sound.Play();
            }
        }
    }
}
