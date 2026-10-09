using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;

namespace ViveShuttleVR.Editor
{
    /// <summary>
    /// OpenXR 1.0 refuses to initialize in Play Mode unless this Input System
    /// user setting is enabled. It lives in Library, so restore it on a fresh
    /// checkout as well as through the project's repair command.
    /// </summary>
    [InitializeOnLoad]
    public static class XRPlayModeSetup
    {
        private const string InputRuleMessage = "Lock Input to Game View in order for tracked pose driver to work in editor playmode.";

        static XRPlayModeSetup()
        {
            EditorApplication.delayCall += EnsureEditorInput;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    EnsureEditorInput();
            };
        }

        public static void EnsureEditorInput()
        {
            if (EditorApplication.isPlaying)
                return;

            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Standalone);
            foreach (OpenXRFeature.ValidationRule issue in issues)
            {
                if (issue.message != InputRuleMessage)
                    continue;

                // Use the package's own targeted fix instead of skipping its
                // validation or modifying cached package source files.
                issue.fixIt?.Invoke();
                if (issue.checkPredicate != null && issue.checkPredicate())
                    Debug.Log("[Vive XR] Enabled Lock Input to Game View; OpenXR Play Mode input validation passed.");
                else
                    Debug.LogError("[Vive XR] Enable Lock Input to Game View in Window > Analysis > Input Debugger > Options.");
            }
        }
    }
}
