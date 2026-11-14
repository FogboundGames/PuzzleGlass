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
        Pink,
        Black,
        White,
        Cyan,
        Brown
    }

    public static class PuzzleColorPalette
    {
        public static Color GetBodyColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.18f, 0.58f, 0.90f, 1f); // Balanced cerulean toy blue
                case PuzzleColor.Yellow: return new Color(0.94f, 0.75f, 0.22f, 1f); // Warm butter-marigold yellow
                case PuzzleColor.Red:    return new Color(0.90f, 0.32f, 0.30f, 1f); // Balanced coral-crimson red
                case PuzzleColor.Green:  return new Color(0.32f, 0.76f, 0.38f, 1f); // Fresh meadow-apple green
                case PuzzleColor.Purple: return new Color(0.58f, 0.39f, 0.86f, 1f); // Soft rich grape-violet
                case PuzzleColor.Orange: return new Color(0.93f, 0.54f, 0.20f, 1f); // Warm apricot-tangerine
                case PuzzleColor.Pink:   return new Color(0.89f, 0.37f, 0.62f, 1f); // Balanced berry-rose pink
                case PuzzleColor.Black:  return new Color(0.19f, 0.20f, 0.25f, 1f); // Soft slate charcoal
                case PuzzleColor.White:  return new Color(0.92f, 0.93f, 0.96f, 1f); // Porcelain pearl white
                case PuzzleColor.Cyan:   return new Color(0.20f, 0.76f, 0.78f, 1f); // Balanced seafoam teal
                case PuzzleColor.Brown:  return new Color(0.74f, 0.55f, 0.39f, 1f); // Warm caramel tan
                default: return Color.white;
            }
        }

        public static Color GetCubeColor(PuzzleColor color)
        {
            return GetBodyColor(color);
        }

        public static Color GetColor(PuzzleColor color)
        {
            return GetCubeColor(color);
        }

        public static Color GetPrimaryColor(PuzzleColor color)
        {
            return GetBodyColor(color);
        }

        /// <summary>
        /// Multiplicative hue-shifted shadow tint (_SColor) for Toony Colors Pro 2 Hybrid Shader 2.
        /// Keeps shadows clean and lively without turning muddy or gray.
        /// </summary>
        public static Color GetTcp2ShadowTint(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.54f, 0.60f, 0.82f, 1f);
                case PuzzleColor.Yellow: return new Color(0.78f, 0.60f, 0.42f, 1f);
                case PuzzleColor.Red:    return new Color(0.68f, 0.48f, 0.58f, 1f);
                case PuzzleColor.Green:  return new Color(0.48f, 0.68f, 0.60f, 1f);
                case PuzzleColor.Purple: return new Color(0.54f, 0.48f, 0.78f, 1f);
                case PuzzleColor.Orange: return new Color(0.76f, 0.52f, 0.46f, 1f);
                case PuzzleColor.Pink:   return new Color(0.68f, 0.48f, 0.66f, 1f);
                case PuzzleColor.Black:  return new Color(0.55f, 0.58f, 0.68f, 1f);
                case PuzzleColor.White:  return new Color(0.74f, 0.77f, 0.86f, 1f);
                case PuzzleColor.Cyan:   return new Color(0.48f, 0.66f, 0.78f, 1f);
                case PuzzleColor.Brown:  return new Color(0.66f, 0.52f, 0.46f, 1f);
                default: return new Color(0.62f, 0.64f, 0.76f, 1f);
            }
        }

        /// <summary>
        /// Warm subsurface-scattered shadow tone for tactile candy/resin cubes.
        /// </summary>
        public static Color GetCubeShadowColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.09f, 0.36f, 0.68f, 1f);
                case PuzzleColor.Yellow: return new Color(0.76f, 0.48f, 0.10f, 1f);
                case PuzzleColor.Red:    return new Color(0.66f, 0.16f, 0.22f, 1f);
                case PuzzleColor.Green:  return new Color(0.16f, 0.52f, 0.22f, 1f);
                case PuzzleColor.Purple: return new Color(0.34f, 0.20f, 0.64f, 1f);
                case PuzzleColor.Orange: return new Color(0.74f, 0.32f, 0.08f, 1f);
                case PuzzleColor.Pink:   return new Color(0.64f, 0.18f, 0.44f, 1f);
                case PuzzleColor.Black:  return new Color(0.08f, 0.09f, 0.13f, 1f);
                case PuzzleColor.White:  return new Color(0.68f, 0.72f, 0.82f, 1f);
                case PuzzleColor.Cyan:   return new Color(0.10f, 0.50f, 0.58f, 1f);
                case PuzzleColor.Brown:  return new Color(0.50f, 0.34f, 0.22f, 1f);
                default: return new Color(0.62f, 0.64f, 0.70f, 1f);
            }
        }

        /// <summary>
        /// Balanced top-bevel catchlight color for tactile candy/resin cubes.
        /// </summary>
        public static Color GetCubeHighlightColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.56f, 0.84f, 0.96f, 1f);
                case PuzzleColor.Yellow: return new Color(0.98f, 0.90f, 0.54f, 1f);
                case PuzzleColor.Red:    return new Color(0.96f, 0.64f, 0.60f, 1f);
                case PuzzleColor.Green:  return new Color(0.68f, 0.90f, 0.62f, 1f);
                case PuzzleColor.Purple: return new Color(0.80f, 0.68f, 0.96f, 1f);
                case PuzzleColor.Orange: return new Color(0.98f, 0.78f, 0.52f, 1f);
                case PuzzleColor.Pink:   return new Color(0.96f, 0.68f, 0.84f, 1f);
                case PuzzleColor.Black:  return new Color(0.42f, 0.44f, 0.50f, 1f);
                case PuzzleColor.White:  return new Color(0.98f, 0.98f, 1.00f, 1f);
                case PuzzleColor.Cyan:   return new Color(0.64f, 0.92f, 0.92f, 1f);
                case PuzzleColor.Brown:  return new Color(0.88f, 0.74f, 0.62f, 1f);
                default: return Color.white;
            }
        }

        public static Shader FindToonyHybridShader()
        {
            Shader s = Shader.Find("Toony Colors Pro 2/Hybrid Shader 2");
            if (s != null) return s;

            Material template = Resources.Load<Material>("TCP2_Hypercasual_Template");
            if (template != null && template.shader != null)
                return template.shader;

            return Shader.Find("PuzzleGlass/CandyCubeLit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
        }

        public static Material CreateToonyHypercasualMaterial(
            PuzzleColor color,
            string matName = null,
            bool glossyCandy = true,
            float indirectIntensity = 0.18f)
        {
            Material m = CreateToonyHypercasualMaterial(
                GetBodyColor(color),
                GetTcp2ShadowTint(color),
                string.IsNullOrEmpty(matName) ? $"Mat_CandyCube_{color}" : matName,
                glossyCandy,
                indirectIntensity);

            // Preserve fallback properties for PuzzleGlass/CandyCubeLit if TCP2 isn't loaded
            if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", GetCubeShadowColor(color));
            if (m.HasProperty("_HighlightColor")) m.SetColor("_HighlightColor", GetCubeHighlightColor(color));
            return m;
        }

        public static Material CreateToonyHypercasualMaterial(
            Color baseColor,
            Color shadowTint,
            string matName = null,
            bool glossyCandy = true,
            float indirectIntensity = 0.18f)
        {
            Shader shader = FindToonyHybridShader();
            Material m = new Material(shader);
            if (!string.IsNullOrEmpty(matName))
                m.name = matName;

            ConfigureToonyMaterial(m, baseColor, shadowTint, glossyCandy, indirectIntensity);
            return m;
        }

        public static void ConfigureToonyMaterial(
            Material m,
            Color baseColor,
            Color shadowTint,
            bool glossyCandy = true,
            float indirectIntensity = 0.18f)
        {
            if (m == null) return;

            m.enableInstancing = true;
            m.color = baseColor;

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);

            // Toony Colors Pro 2 Hybrid Shader 2 parameters
            if (m.HasProperty("_HColor")) m.SetColor("_HColor", new Color(0.95f, 0.94f, 0.92f, 1f));
            if (m.HasProperty("_SColor")) m.SetColor("_SColor", shadowTint);
            if (m.HasProperty("_ShadowColorLightAtten")) m.SetFloat("_ShadowColorLightAtten", 1f);
            m.EnableKeyword("TCP2_SHADOW_LIGHT_COLOR");

            if (m.HasProperty("_RampType")) m.SetFloat("_RampType", 0f);
            if (m.HasProperty("_RampThreshold")) m.SetFloat("_RampThreshold", glossyCandy ? 0.48f : 0.50f);
            if (m.HasProperty("_RampSmoothing")) m.SetFloat("_RampSmoothing", glossyCandy ? 0.24f : 0.28f);

            // Stylized soft specular (scaled directly by _SpecularColor.rgb in TCP2 Hybrid 2)
            if (m.HasProperty("_UseSpecular")) m.SetFloat("_UseSpecular", 1f);
            if (m.HasProperty("_SpecularType")) m.SetFloat("_SpecularType", 1f);
            if (m.HasProperty("_SpecularMapType")) m.SetFloat("_SpecularMapType", 0f);
            if (m.HasProperty("_SpecularColor"))
            {
                m.SetColor("_SpecularColor", glossyCandy
                    ? new Color(0.28f, 0.28f, 0.26f, 1f)
                    : new Color(0.08f, 0.08f, 0.10f, 1f));
            }
            if (m.HasProperty("_SpecularToonSize")) m.SetFloat("_SpecularToonSize", glossyCandy ? 0.18f : 0.12f);
            if (m.HasProperty("_SpecularToonSmoothness")) m.SetFloat("_SpecularToonSmoothness", glossyCandy ? 0.14f : 0.22f);
            m.EnableKeyword("TCP2_SPECULAR");
            m.EnableKeyword("TCP2_SPECULAR_STYLIZED");

            // Subtle light-masked rim light (scaled directly by _RimColor.rgb in TCP2 Hybrid 2)
            if (m.HasProperty("_UseRim")) m.SetFloat("_UseRim", 1f);
            if (m.HasProperty("_UseRimLightMask")) m.SetFloat("_UseRimLightMask", 1f);
            if (m.HasProperty("_RimColor"))
            {
                m.SetColor("_RimColor", glossyCandy
                    ? new Color(0.16f, 0.17f, 0.20f, 1f)
                    : new Color(0.09f, 0.10f, 0.13f, 1f));
            }
            if (m.HasProperty("_RimMin")) m.SetFloat("_RimMin", glossyCandy ? 0.58f : 0.64f);
            if (m.HasProperty("_RimMax")) m.SetFloat("_RimMax", 0.94f);
            m.EnableKeyword("TCP2_RIM_LIGHTING");
            m.EnableKeyword("TCP2_RIM_LIGHTING_LIGHTMASK");

            // Restrained indirect GI so bright ambient light does not wash out colors
            if (m.HasProperty("_IndirectIntensity")) m.SetFloat("_IndirectIntensity", indirectIntensity);
            if (m.HasProperty("_SingleIndirectColor")) m.SetFloat("_SingleIndirectColor", 0f);
            if (m.HasProperty("_ReceiveShadowsOff")) m.SetFloat("_ReceiveShadowsOff", 1f);
            m.DisableKeyword("_RECEIVE_SHADOWS_OFF");

            // Standard URP/fallback properties (if TCP2 shader is unavailable)
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glossyCandy ? 0.54f : 0.25f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossyCandy ? 0.54f : 0.25f);
            if (m.HasProperty("_BevelHighlight")) m.SetFloat("_BevelHighlight", 0.20f);
            if (m.HasProperty("_RimSeparation")) m.SetFloat("_RimSeparation", 0.36f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// Creates a balanced hypercasual candy/toy material powered by Toony Colors Pro 2 Hybrid Shader 2.
        /// </summary>
        public static Material CreateCandyCubeMaterial(PuzzleColor color, string matName = null)
        {
            Material m = CreateToonyHypercasualMaterial(
                color,
                string.IsNullOrEmpty(matName) ? $"Mat_CandyCube_{color}" : matName,
                glossyCandy: true,
                indirectIntensity: 0.18f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 15;
            return m;
        }

        public static Color GetDarkMouthColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.10f, 0.34f, 0.58f, 1f);
                case PuzzleColor.Yellow: return new Color(0.64f, 0.42f, 0.08f, 1f);
                case PuzzleColor.Red:    return new Color(0.54f, 0.14f, 0.18f, 1f);
                case PuzzleColor.Green:  return new Color(0.14f, 0.44f, 0.20f, 1f);
                case PuzzleColor.Purple: return new Color(0.34f, 0.20f, 0.56f, 1f);
                case PuzzleColor.Orange: return new Color(0.62f, 0.32f, 0.08f, 1f);
                case PuzzleColor.Pink:   return new Color(0.58f, 0.18f, 0.38f, 1f);
                case PuzzleColor.Black:  return new Color(0.08f, 0.09f, 0.12f, 1f);
                case PuzzleColor.White:  return new Color(0.58f, 0.62f, 0.70f, 1f);
                case PuzzleColor.Cyan:   return new Color(0.10f, 0.44f, 0.48f, 1f);
                case PuzzleColor.Brown:  return new Color(0.45f, 0.28f, 0.18f, 1f);
                default: return Color.black;
            }
        }

        public static Color GetTextColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.14f, 0.42f, 0.72f, 1f);
                case PuzzleColor.Yellow: return new Color(0.76f, 0.48f, 0.10f, 1f);
                case PuzzleColor.Red:    return new Color(0.72f, 0.18f, 0.22f, 1f);
                case PuzzleColor.Green:  return new Color(0.16f, 0.50f, 0.22f, 1f);
                case PuzzleColor.Purple: return new Color(0.42f, 0.24f, 0.66f, 1f);
                case PuzzleColor.Orange: return new Color(0.76f, 0.38f, 0.10f, 1f);
                case PuzzleColor.Pink:   return new Color(0.74f, 0.20f, 0.46f, 1f);
                case PuzzleColor.Black:  return new Color(0.18f, 0.19f, 0.23f, 1f);
                case PuzzleColor.White:  return new Color(0.80f, 0.84f, 0.90f, 1f);
                case PuzzleColor.Cyan:   return new Color(0.12f, 0.56f, 0.60f, 1f);
                case PuzzleColor.Brown:  return new Color(0.58f, 0.40f, 0.24f, 1f);
                default: return Color.black;
            }
        }
    }

    public static class PuzzleColorUtility
    {
        private static readonly System.Collections.Generic.Dictionary<PuzzleColor, Material> _cachedMaterials 
            = new System.Collections.Generic.Dictionary<PuzzleColor, Material>();

        public static Material GetMaterial(PuzzleColor color)
        {
            if (!_cachedMaterials.TryGetValue(color, out var mat) || mat == null)
            {
                mat = PuzzleColorPalette.CreateCandyCubeMaterial(color);
                _cachedMaterials[color] = mat;
            }
            return mat;
        }

        public static void ClearMaterialCache()
        {
            _cachedMaterials.Clear();
        }
    }
}
