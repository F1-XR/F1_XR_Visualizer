using TMPro;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [CreateAssetMenu(
        fileName = "PitShowcaseAssets",
        menuName = "F1 XR/Replay/Pit Showcase Asset Profile")]
    public sealed class PitShowcaseAssetProfile : ScriptableObject
    {
        [SerializeField] private GameObject wheelGunPrefab;
        [SerializeField] private AudioClip wheelGunLoopClip;
        [SerializeField] private AudioClip suspensionSettleClip;
        [SerializeField] private AudioClip serviceClunkClip;
        [SerializeField] private AudioClip launchClip;
        [SerializeField] private GameObject tyreVisualPrefab;
        [SerializeField] private TMP_FontAsset displayFont;

        public GameObject WheelGunPrefab => wheelGunPrefab;
        public AudioClip WheelGunLoopClip => wheelGunLoopClip;
        public AudioClip SuspensionSettleClip => suspensionSettleClip;
        public AudioClip ServiceClunkClip => serviceClunkClip;
        public AudioClip LaunchClip => launchClip;
        public GameObject TyreVisualPrefab => tyreVisualPrefab;
        public TMP_FontAsset DisplayFont => displayFont;
    }
}
