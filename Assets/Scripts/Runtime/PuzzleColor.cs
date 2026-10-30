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
        public static Color GetBodyColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.14f, 0.66f, 0.96f, 1f); // Vibrant sky-turquoise blue (Image 2)
                case PuzzleColor.Yellow: return new Color(1.00f, 0.78f, 0.14f, 1f); // Sunny buttercup gold (Image 2)
                case PuzzleColor.Red:    return new Color(1.00f, 0.36f, 0.32f, 1f); // Warm coral-strawberry red (Image 2)
                case PuzzleColor.Green:  return new Color(0.36f, 0.85f, 0.30f, 1f); // Fresh lime-apple green (Image 2)
                case PuzzleColor.Purple: return new Color(0.60f, 0.38f, 0.96f, 1f); // Soft violet-lavender (Image 2)
                case PuzzleColor.Orange: return new Color(1.00f, 0.58f, 0.16f, 1f); // Rich tangerine-peach (Image 2)
                case PuzzleColor.Pink:   return new Color(1.00f, 0.32f, 0.66f, 1f); // Candy bubblegum pink (Image 2)
                default: return Color.white;
            }
        }

        public static Color GetCubeColor(PuzzleColor color)
        {
            return GetBodyColor(color);
        }

        /// <summary>
        /// Warm subsurface-scattered shadow tone for Image 2 tactile candy/resin cubes (never muddy gray/black).
        /// </summary>
        public static Color GetCubeShadowColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.05f, 0.38f, 0.72f, 1f); // Deep ocean azure
                case PuzzleColor.Yellow: return new Color(0.86f, 0.48f, 0.04f, 1f); // Warm amber-honey shadow
                case PuzzleColor.Red:    return new Color(0.74f, 0.14f, 0.16f, 1f); // Deep ruby-berry shadow
                case PuzzleColor.Green:  return new Color(0.14f, 0.55f, 0.15f, 1f); // Rich emerald-leaf shadow
                case PuzzleColor.Purple: return new Color(0.34f, 0.16f, 0.70f, 1f); // Deep indigo-grape shadow
                case PuzzleColor.Orange: return new Color(0.80f, 0.32f, 0.04f, 1f); // Warm burnt-tangerine shadow
                case PuzzleColor.Pink:   return new Color(0.72f, 0.12f, 0.42f, 1f); // Deep raspberry shadow
                default: return new Color(0.65f, 0.65f, 0.70f, 1f);
            }
        }

        /// <summary>
        /// Creamy, luminous top-bevel catchlight color for Image 2 tactile candy/resin cubes.
        /// </summary>
        public static Color GetCubeHighlightColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.58f, 0.91f, 1.00f, 1f);
                case PuzzleColor.Yellow: return new Color(1.00f, 0.95f, 0.56f, 1f);
                case PuzzleColor.Red:    return new Color(1.00f, 0.72f, 0.66f, 1f);
                case PuzzleColor.Green:  return new Color(0.76f, 0.98f, 0.62f, 1f);
                case PuzzleColor.Purple: return new Color(0.85f, 0.72f, 1.00f, 1f);
                case PuzzleColor.Orange: return new Color(1.00f, 0.84f, 0.52f, 1f);
                case PuzzleColor.Pink:   return new Color(1.00f, 0.74f, 0.90f, 1f);
                default: return Color.white;
            }
        }

        /// <summary>
        /// Creates a fully configured Image-2 tactile candy/resin cube material using PuzzleGlass/CandyCubeLit.
        /// </summary>
        public static Material CreateCandyCubeMaterial(PuzzleColor color, string matName = null)
        {
            Shader candyShader = Shader.Find("PuzzleGlass/CandyCubeLit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            Material m = new Material(candyShader);
            m.name = string.IsNullOrEmpty(matName) ? $"Mat_CandyCube_{color}" : matName;

            Color baseCol = GetCubeColor(color);
            Color shadowCol = GetCubeShadowColor(color);
            Color highCol = GetCubeHighlightColor(color);

            m.color = baseCol;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseCol);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
            if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", shadowCol);
            if (m.HasProperty("_HighlightColor")) m.SetColor("_HighlightColor", highCol);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.74f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.74f);
            if (m.HasProperty("_BevelHighlight")) m.SetFloat("_BevelHighlight", 0.56f);
            if (m.HasProperty("_RimSeparation")) m.SetFloat("_RimSeparation", 0.38f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 15;
            return m;
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
    }
}
