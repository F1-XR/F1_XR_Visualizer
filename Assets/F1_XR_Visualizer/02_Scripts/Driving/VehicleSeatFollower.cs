using UnityEngine;

namespace F1XR.Driving
{
    [DisallowMultipleComponent]
    public sealed class VehicleSeatFollower : MonoBehaviour
    {
        [SerializeField] Transform seatAnchor;

        public void Configure(Transform anchor)
        {
            seatAnchor = anchor;
            FollowSeat();
        }

        void LateUpdate()
        {
            FollowSeat();
        }

        void FollowSeat()
        {
            if (seatAnchor == null)
                return;

            transform.SetPositionAndRotation(seatAnchor.position, seatAnchor.rotation);
        }
    }
}
