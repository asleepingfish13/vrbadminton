using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SpatialTracking;
using Object = UnityEngine.Object;

namespace ViveShuttleVR.Editor
{
    public static class ChineseLocalizationRegression
    {
        private static int checks;
        private static void Require(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException("[Chinese UI Test] " + message); }

        public static int Run()
        {
            checks = 0;
            try
            {
                EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath);
                Font font = GameLocalization.ChineseFont;
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(font)) as TrueTypeFontImporter;
                Require(importer != null && importer.includeFontData && font.dynamic,
                    "Chinese font must be embedded and dynamic, independent of system fonts");
                // Validate all Han characters currently present in UI code, not
                // only a tiny sample that would miss rarely used settings.
                string corpus = "";
                foreach (string path in new[] { "Assets/Scripts/BadmintonSession.cs", "Assets/Scripts/StandardSinglesSession.cs", "Assets/Scripts/GameLocalization.cs",
                    "Assets/Editor/RegulationHallBuilder.cs", "Assets/Editor/BadmintonProjectBuilder.cs" })
                    corpus += File.ReadAllText(path);
                var characters = new System.Collections.Generic.HashSet<char>();
                foreach (Match match in Regex.Matches(corpus, @"[\u3400-\u9fff]")) characters.Add(match.Value[0]);
                Require(characters.Count > 100, "Chinese localization corpus is incomplete");
                foreach (char character in characters)
                {
                    Require(font.HasCharacter(character), "Missing Chinese glyph: " + character);
                    font.RequestCharactersInTexture(character.ToString(), 80, FontStyle.Normal);
                    CharacterInfo info;
                    Require(font.GetCharacterInfo(character, out info, 80) && info.advance > 0,
                        "Chinese glyph cannot be rasterized: " + character);
                }
                foreach (TextMesh label in Object.FindObjectsOfType<TextMesh>()) CheckLabel(label);
                foreach (OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
                    Require(HasChinese(GameLocalization.Difficulty(difficulty)), "Difficulty not localized");
                foreach (ShotTechnique technique in Enum.GetValues(typeof(ShotTechnique)))
                    Require(HasChinese(GameLocalization.Technique(technique)), "Technique not localized");
                foreach (FeedKind kind in Enum.GetValues(typeof(FeedKind)))
                    Require(HasChinese(GameLocalization.Feed(kind)), "Feed not localized");

                BadmintonSession session = Object.FindObjectOfType<BadmintonSession>();
                session.mode = PracticeMode.Training;
                session.playerCamera.GetComponent<TrackedPoseDriver>().enabled = false;
                session.playerCamera.transform.SetPositionAndRotation(new Vector3(0f, 1.63f, 0f), Quaternion.identity);
                Require(session.Recenter(), "Desktop UI fixture could not recenter");
                typeof(BadmintonSession).GetMethod("CreateAlignmentHint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, null);
                string physicsBefore = JsonUtility.ToJson(session.physics);
                for (int i = 0; i < Enum.GetValues(typeof(EquipmentSetting)).Length; i++)
                {
                    session.CycleEquipmentSelection();
                    CheckLabel(session.statusText); CheckLabel(session.detailText);
                    Require(session.detailText.text.Contains("触控板"), "Settings navigation help missing");
                }
                Require(JsonUtility.ToJson(session.physics) == physicsBefore, "Browsing translations changed physics");
                foreach (OpponentDifficulty difficulty in Enum.GetValues(typeof(OpponentDifficulty)))
                {
                    session.SetDifficulty(difficulty);
                    Require(session.statusText.text.Contains(GameLocalization.Difficulty(difficulty)), "Runtime difficulty untranslated");
                }
                foreach (PracticeDrill drill in Enum.GetValues(typeof(PracticeDrill)))
                { session.SetDrill(drill); CheckLabel(session.statusText); CheckLabel(session.detailText); }
                session.AdjustTension(0); Require(session.statusText.text.Contains("磅"), "Tension unit untranslated");
                session.AdjustMass(0f); Require(session.statusText.text.Contains("克"), "Mass unit untranslated");
                session.ToggleAssist(); Require(HasChinese(session.EquipmentSummary), "Assist HUD untranslated");
                foreach (TextMesh label in Object.FindObjectsOfType<TextMesh>()) CheckLabel(label);
                Debug.Log("[Chinese UI Test] PASS " + checks + " checks: bundled dynamic font, " + characters.Count +
                    " Han glyphs, scene/runtime labels, all settings categories and unchanged physics while browsing.");
                return checks;
            }
            finally { EditorSceneManager.OpenScene(BadmintonProjectBuilder.ScenePath); }
        }

        private static bool HasChinese(string value) { return Regex.IsMatch(value ?? "", @"[\u3400-\u9fff]"); }
        private static void CheckLabel(TextMesh label)
        {
            Require(label.font == GameLocalization.ChineseFont, "Wrong font on " + label.name);
            Require(label.GetComponent<Renderer>().sharedMaterial == label.font.material, "Wrong font material on " + label.name);
            if (!string.IsNullOrWhiteSpace(label.text)) Require(HasChinese(label.text), "English-only display: " + label.name + " = " + label.text);
        }
    }
}
