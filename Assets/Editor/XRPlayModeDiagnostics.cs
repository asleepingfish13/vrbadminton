using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace ViveShuttleVR.Editor
{
    [InitializeOnLoad]
    public static class XRPlayModeDiagnostics
    {
        private static double nextReport;
        private static int reportsRemaining;

        static XRPlayModeDiagnostics()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                reportsRemaining = 2;
                nextReport = EditorApplication.timeSinceStartup + 2.0;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
                reportsRemaining = 0;
        }

        private static void Update()
        {
            if (!EditorApplication.isPlaying || reportsRemaining == 0 || EditorApplication.timeSinceStartup < nextReport)
                return;

            LogStatus();
            reportsRemaining--;
            nextReport = EditorApplication.timeSinceStartup + 6.0;
        }

        [MenuItem("Tools/Vive Shuttle VR/Log XR Status _F10")]
        public static void LogStatus()
        {
            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            XRManagerSettings manager = settings != null ? settings.Manager : null;
            var status = new StringBuilder("[Vive XR] ");
            status.AppendFormat("Play={0}; InitializeOnStart={1}; ConfiguredLoaders={2}; ActiveLoader={3}",
                EditorApplication.isPlaying,
                settings != null && settings.InitManagerOnStart,
                manager != null ? manager.activeLoaders.Count : 0,
                manager != null && manager.activeLoader != null ? manager.activeLoader.GetType().Name : "NONE");

            if (manager != null && manager.activeLoader is OpenXRLoader)
                status.AppendFormat("; Runtime={0} {1}", OpenXRRuntime.name, OpenXRRuntime.version);

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            foreach (XRDisplaySubsystem display in displays)
                status.AppendFormat("; Display={0}, Running={1}, RenderPasses={2}",
                    display.SubsystemDescriptor.id, display.running, display.running ? display.GetRenderPassCount() : 0);

            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);
            foreach (InputDevice device in devices)
            {
                bool tracked;
                bool isTracked = device.TryGetFeatureValue(CommonUsages.isTracked, out tracked) && tracked;
                status.AppendFormat("; Device={0}, Tracked={1}", device.name, isTracked);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                TrackedPoseDriver pose = camera.GetComponent<TrackedPoseDriver>();
                status.AppendFormat("; CameraEnabled={0}, Stereo={1}, TargetEye={2}, HeadPoseDriver={3}, Eye={4}",
                    camera.enabled, camera.stereoEnabled, camera.stereoTargetEye, pose != null && pose.enabled, camera.transform.position);
            }

            if (EditorApplication.isPlaying && (manager == null || manager.activeLoader == null))
            {
                var issues = new List<OpenXRFeature.ValidationRule>();
                OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Standalone);
                foreach (OpenXRFeature.ValidationRule issue in issues)
                {
                    if (issue.errorEnteringPlaymode)
                        status.Append("; BlockingValidation=" + issue.message);
                }
                Debug.LogWarning(status.ToString() + "; XR did not start. Check OpenXR validation and SteamVR.");
            }
            else
                Debug.Log(status.ToString());
        }
    }
}
