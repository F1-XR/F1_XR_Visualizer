using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace F1XR.Editor
{
    /// <summary>
    /// Destroys the Meta plane provider while its OpenXR instance is still valid.
    /// Quest Link can otherwise unload the spatial-entity extension before the
    /// provider releases its discovery callbacks, which crashes the Editor in
    /// OVRInterfaceShutdown after Play mode appears to have stopped.
    /// </summary>
    [InitializeOnLoad]
    internal static class QuestLinkSpatialTeardownGuard
    {
        const string XRSettingsTypeName =
            "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, " +
            "Unity.XR.Management.Editor";

        static QuestLinkSpatialTeardownGuard()
        {
            EditorApplication.delayCall -= RegisterBeforeXRManagement;
            EditorApplication.delayCall += RegisterBeforeXRManagement;
        }

        static void RegisterBeforeXRManagement()
        {
            Type settingsType = Type.GetType(XRSettingsTypeName);
            MethodInfo callbackMethod = settingsType?.GetMethod(
                "PlayModeStateChanged",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (callbackMethod == null)
            {
                Debug.LogWarning(
                    "[QuestLinkShutdown] Could not find the XR Management " +
                    "Play mode callback. Early plane teardown is unavailable.");
                return;
            }

            var xrManagementCallback =
                (Action<PlayModeStateChange>)Delegate.CreateDelegate(
                    typeof(Action<PlayModeStateChange>),
                    callbackMethod);

            // XR Management normally registers before project Editor code and
            // deinitializes OpenXR before this guard runs. Re-register both
            // callbacks so the plane provider is destroyed first, while keeping
            // Unity's original shutdown callback intact immediately afterward.
            EditorApplication.playModeStateChanged -= xrManagementCallback;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += xrManagementCallback;

            Debug.Log(
                "[QuestLinkShutdown] Registered before XR Management teardown.");
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode ||
                Application.platform != RuntimePlatform.WindowsEditor)
            {
                return;
            }

            try
            {
                StopPlaneManagers();

                XRLoaderHelper loader =
                    XRGeneralSettings.Instance?.Manager?.activeLoader
                    as XRLoaderHelper;
                XRPlaneSubsystem planeSubsystem =
                    loader?.GetLoadedSubsystem<XRPlaneSubsystem>();
                if (planeSubsystem == null)
                {
                    Debug.Log(
                        "[QuestLinkShutdown] No loaded plane subsystem to destroy.");
                    return;
                }

                loader.DestroySubsystem<XRPlaneSubsystem>();
                Debug.Log(
                    "[QuestLinkShutdown] Plane subsystem destroyed before " +
                    "OpenXR Play mode teardown.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static void StopPlaneManagers()
        {
            ARPlaneManager[] managers =
                UnityEngine.Object.FindObjectsByType<ARPlaneManager>(
                    FindObjectsInactive.Include);
            int disabledCount = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                ARPlaneManager manager = managers[i];
                if (manager == null || !manager.enabled)
                    continue;

                manager.enabled = false;
                disabledCount++;
            }

            Debug.Log(
                $"[QuestLinkShutdown] Disabled {disabledCount} active plane " +
                "manager(s) before destroying the subsystem.");
        }
    }
}
