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

        [Header("First Milestone Choreography")]
        [SerializeField] private GameObject pitCrewPrefab;
        [SerializeField] private RuntimeAnimatorController choreographyBaseController;
        [SerializeField] private AnimationClip wheelGunnerFull;
        [SerializeField] private AnimationClip wheelOffFullL;
        [SerializeField] private AnimationClip wheelOnFullL;
        [SerializeField] private AnimationClip frontJackFullL;
        [SerializeField] private AnimationClip rearJackFullR;
        [SerializeField] private AnimationClip pitSignalFullR;

        [Header("Racer Tyre-Service Retarget")]
        [SerializeField] private GameObject flWheelGunnerPrefab;
        [SerializeField] private GameObject flWheelGunnerVisualPrefab;
        [SerializeField] private Material flWheelGunnerMaterial;
        [SerializeField] private AnimationClip flWheelGunnerHumanoidFull;
        [SerializeField] private AnimationClip racerWheelOffHumanoidFullL;
        [SerializeField] private AnimationClip racerWheelOffHumanoidFullR;
        [SerializeField] private AnimationClip racerWheelOnHumanoidFullL;
        [SerializeField] private AnimationClip racerWheelOnHumanoidFullR;
        [SerializeField] private AnimationClip flWheelGunnerIdle;
        [SerializeField] private AnimationClip flWheelGunnerFastRun;
        [SerializeField] private AnimationClip flWheelGunnerRunningBackward;

        public GameObject WheelGunPrefab => wheelGunPrefab;
        public AudioClip WheelGunLoopClip => wheelGunLoopClip;
        public AudioClip SuspensionSettleClip => suspensionSettleClip;
        public AudioClip ServiceClunkClip => serviceClunkClip;
        public AudioClip LaunchClip => launchClip;
        public GameObject TyreVisualPrefab => tyreVisualPrefab;
        public TMP_FontAsset DisplayFont => displayFont;
        public GameObject PitCrewPrefab => pitCrewPrefab;
        public RuntimeAnimatorController ChoreographyBaseController =>
            choreographyBaseController;
        public AnimationClip WheelGunnerFull => wheelGunnerFull;
        public AnimationClip WheelOffFullL => wheelOffFullL;
        public AnimationClip WheelOnFullL => wheelOnFullL;
        public AnimationClip FrontJackFullL => frontJackFullL;
        public AnimationClip RearJackFullR => rearJackFullR;
        public AnimationClip PitSignalFullR => pitSignalFullR;
        public GameObject FlWheelGunnerPrefab => flWheelGunnerPrefab;
        public GameObject FlWheelGunnerVisualPrefab =>
            flWheelGunnerVisualPrefab;
        public Material FlWheelGunnerMaterial => flWheelGunnerMaterial;
        public AnimationClip FlWheelGunnerHumanoidFull =>
            flWheelGunnerHumanoidFull;
        public AnimationClip RacerWheelOffHumanoidFullL =>
            racerWheelOffHumanoidFullL;
        public AnimationClip RacerWheelOffHumanoidFullR =>
            racerWheelOffHumanoidFullR;
        public AnimationClip RacerWheelOnHumanoidFullL =>
            racerWheelOnHumanoidFullL;
        public AnimationClip RacerWheelOnHumanoidFullR =>
            racerWheelOnHumanoidFullR;
        public AnimationClip FlWheelGunnerIdle =>
            flWheelGunnerIdle;
        public AnimationClip FlWheelGunnerFastRun =>
            flWheelGunnerFastRun;
        public AnimationClip FlWheelGunnerRunningBackward =>
            flWheelGunnerRunningBackward;

        public bool HasFirstMilestoneChoreographyAssets =>
            pitCrewPrefab != null &&
            choreographyBaseController != null &&
            wheelGunPrefab != null &&
            wheelGunnerFull != null &&
            wheelOffFullL != null &&
            wheelOnFullL != null &&
            frontJackFullL != null &&
            rearJackFullR != null &&
            pitSignalFullR != null;
    }
}
