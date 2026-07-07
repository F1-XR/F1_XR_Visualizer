using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace F1XR.RestAPI.AR
{
    public sealed partial class ARBuildRevealPlacer
    {
        void ConfirmPlacement()
        {
            if (!hasCurrentHit || placementPrefab == null)
                return;

            if (spawnedInstance != null && !allowReplaceExisting)
                return;

            if (allowReplaceExisting)
                ClearSpawned();

            GameObject target;

            if (previewInstance != null)
            {
                target = previewInstance;
                previewInstance = null;

                target.name = placementPrefab.name;
                target.SetActive(true);

                RestorePreviewForRealUse(target);
            }
            else
            {
                target = Instantiate(placementPrefab);
                target.name = placementPrefab.name;
                ConfigurePhysics(target);
            }

            currentAnchor = CreateAnchor(currentPose, currentPlane);

            if (currentAnchor != null)
            {
                target.transform.SetParent(currentAnchor.transform, worldPositionStays: false);
                target.transform.localPosition = Vector3.up * verticalOffset;
                target.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Vector3 position = currentPose.position + Vector3.up * verticalOffset;
                Quaternion rotation = useHitRotation ? currentPose.rotation : Quaternion.identity;
                target.transform.SetPositionAndRotation(position, rotation);
            }

            spawnedInstance = target;

            BuildRevealController revealController =
                spawnedInstance.GetComponent<BuildRevealController>();

            if (revealController == null)
                revealController = spawnedInstance.AddComponent<BuildRevealController>();

            revealController.Configure(
                buildDuration,
                buildEdgeWidth,
                buildEdgeColor,
                restoreMaterialsAfterBuild);

            revealController.Play();
        }

        ARAnchor CreateAnchor(Pose pose, ARPlane plane)
        {
            if (anchorManager == null)
                return null;

            if (plane != null)
            {
                ARAnchor attachedAnchor = anchorManager.AttachAnchor(plane, pose);
                if (attachedAnchor != null)
                    return attachedAnchor;
            }

            GameObject anchorObject = new GameObject("Placed Build Anchor");
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return anchorObject.AddComponent<ARAnchor>();
        }

        static void ConfigurePhysics(GameObject target)
        {
            foreach (Rigidbody rigidbody in target.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }
        }

        public void ClearSpawned()
        {
            if (currentAnchor != null)
            {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
                spawnedInstance = null;
                return;
            }

            if (spawnedInstance != null)
            {
                Destroy(spawnedInstance);
                spawnedInstance = null;
            }
        }
    }
}
