using System;
using UnityEngine;

namespace ViveShuttleVR
{
    // Display-only translations. Keep simulation enums, point reasons and
    // diagnostic labels independent of the language shown to the player.
    public static class GameLocalization
    {
        public const string FontResource = "Fonts/SourceHanSansSC-Regular";
        private static Font chineseFont;
        public static Font ChineseFont
        {
            get
            {
                if (chineseFont == null) chineseFont = Resources.Load<Font>(FontResource);
                if (chineseFont == null) throw new InvalidOperationException("Bundled Simplified Chinese font is missing: " + FontResource);
                return chineseFont;
            }
        }

        public static void ApplyFont(TextMesh label)
        {
            if (label == null) return;
            label.font = ChineseFont;
            label.GetComponent<Renderer>().sharedMaterial = ChineseFont.material;
        }

        public static string Difficulty(OpponentDifficulty difficulty)
        {
            switch (difficulty)
            {
                case OpponentDifficulty.Easy: return "简单";
                case OpponentDifficulty.Club: return "进阶";
                case OpponentDifficulty.Advanced: return "困难";
                default: return "未知难度";
            }
        }

        public static string Technique(ShotTechnique technique)
        {
            switch (technique)
            {
                case ShotTechnique.Smash: return "杀球";
                case ShotTechnique.Drop: return "吊球";
                case ShotTechnique.SliceDrop: return "劈吊";
                case ShotTechnique.NetShot: return "放网";
                case ShotTechnique.SpinningNet: return "搓球";
                case ShotTechnique.Clear: return "高远球";
                case ShotTechnique.Lift: return "挑球";
                case ShotTechnique.Drive: return "平抽球";
                case ShotTechnique.Block: return "挡球";
                case ShotTechnique.Mishit: return "偏心击球";
                default: return "未分类击球";
            }
        }

        public static string Feed(FeedKind kind)
        {
            switch (kind)
            {
                case FeedKind.Clear: return "高远来球";
                case FeedKind.Smash: return "接杀练习";
                case FeedKind.Drop: return "吊球来球";
                case FeedKind.NetShot: return "网前送球";
                default: return "平抽来球";
            }
        }

        public static string Reach(string key)
        {
            switch (key)
            {
                case "COMPACT NEAR": return "坐姿近点";
                case "COMPACT FRONT": return "坐姿前点";
                case "COMPACT MID": return "坐姿中点";
                case "COMPACT FIXED": return "坐姿固定点";
                case "NEAR REACH": return "近点";
                case "FAR REACH": return "远点";
                case "MID REACH": return "中点";
                case "FIXED REACH": return "固定点";
                default: return "接球点";
            }
        }
    }
}
