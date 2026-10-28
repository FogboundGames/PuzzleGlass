using System;
using UnityEngine;

namespace PuzzleGlass
{
    public enum PuzzleColor
    {
        Blue,
        Yellow,
        Red,
        Green,
        Purple,
        Orange,
        Pink
    }

    public static class PuzzleColorPalette
    {
        // Reference image colors (Birebir aynısı):
        // Green: fresh candy lime-apple (#3CD045 / #4ADE80)
        // Orange: rich vibrant tangerine (#FF9214 / #FFA024)
        // Purple: sweet grape lavender (#9B5DE5 / #9452F5)
        // Pink: bubblegum candy pink (#FF52A3 / #FF5FA5)
        // Blue: sky-blue (#2E9AF7)
        // Yellow: warm gold (#FFBE26)

        public static Color GetBodyColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.18f, 0.60f, 0.97f, 1f); // #2E9AF7
                case PuzzleColor.Yellow: return new Color(1.00f, 0.74f, 0.15f, 1f); // #FFBD26
                case PuzzleColor.Red:    return new Color(0.98f, 0.31f, 0.37f, 1f); // #FA4F5E
                case PuzzleColor.Green:  return new Color(0.24f, 0.82f, 0.27f, 1f); // #3CD045 fresh lime
                case PuzzleColor.Purple: return new Color(0.61f, 0.36f, 0.90f, 1f); // #9B5DE5 grape lavender
                case PuzzleColor.Orange: return new Color(1.00f, 0.57f, 0.08f, 1f); // #FF9214 tangerine
                case PuzzleColor.Pink:   return new Color(1.00f, 0.32f, 0.64f, 1f); // #FF52A3 bubblegum pink
                default: return Color.white;
            }
        }

        public static Color GetDarkMouthColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.08f, 0.34f, 0.62f, 1f);
                case PuzzleColor.Yellow: return new Color(0.68f, 0.42f, 0.05f, 1f);
                case PuzzleColor.Red:    return new Color(0.58f, 0.10f, 0.16f, 1f);
                case PuzzleColor.Green:  return new Color(0.10f, 0.46f, 0.14f, 1f);
                case PuzzleColor.Purple: return new Color(0.35f, 0.18f, 0.58f, 1f);
                case PuzzleColor.Orange: return new Color(0.65f, 0.32f, 0.04f, 1f);
                case PuzzleColor.Pink:   return new Color(0.65f, 0.14f, 0.38f, 1f);
                default: return Color.black;
            }
        }

        public static Color GetTextColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.12f, 0.42f, 0.74f, 1f);
                case PuzzleColor.Yellow: return new Color(0.82f, 0.48f, 0.08f, 1f);
                case PuzzleColor.Red:    return new Color(0.78f, 0.16f, 0.22f, 1f);
                case PuzzleColor.Green:  return new Color(0.12f, 0.52f, 0.16f, 1f);
                case PuzzleColor.Purple: return new Color(0.42f, 0.22f, 0.68f, 1f);
                case PuzzleColor.Orange: return new Color(0.80f, 0.40f, 0.05f, 1f);
                case PuzzleColor.Pink:   return new Color(0.80f, 0.18f, 0.48f, 1f);
                default: return Color.black;
            }
        }

        public static Color GetCubeColor(PuzzleColor color)
        {
            return GetBodyColor(color);
        }
    }
}
