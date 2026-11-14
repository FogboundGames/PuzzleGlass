using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PuzzleGlass.Editor
{
    /// <summary>
    /// Ensures Toony Colors Pro 2 Hybrid Shader 2 is compiled for URP and applies
    /// a balanced, cohesive hypercasual color setup across materials, lighting, and scene visuals.
    /// </summary>
    [InitializeOnLoad]
    public static class ToonyColorsProColorSetup
    {
        private const string HybridShaderPath = "Assets/JMO Assets/Toony Colors Pro/Shaders/Hybrid 2/TCP2 Hybrid Shader 2.tcp2shader";
        private const string TemplateMaterialPath = "Assets/Resources/TCP2_Hypercasual_Template.mat";
        private const string SessionUrpCheckKey = "PuzzleGlass_TCP2_UrpVerified_v1";

        static ToonyColorsProColorSetup()
        {
            EditorApplication.delayCall += RunStartupCheck;
        }

        private static void RunStartupCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                return;

            bool reimportedShader = EnsureHybridShaderCompiledForUrp();

            if (!File.Exists(TemplateMaterialPath) || reimportedShader)
            {
                ApplyThemeInternal(saveSceneIfClean: true);
            }
        }

        public static void ApplyToonyHypercasualTheme()
        {
            EnsureHybridShaderCompiledForUrp(forceIfNotUrp: true);
            ApplyThemeInternal(saveSceneIfClean: true);
        }

        private static bool EnsureHybridShaderCompiledForUrp(bool forceIfNotUrp = false)
        {
            if (!forceIfNotUrp && SessionState.GetBool(SessionUrpCheckKey, false))
                return false;

            SessionState.SetBool(SessionUrpCheckKey, true);

            string metaPath = HybridShaderPath + ".meta";
            if (!File.Exists(HybridShaderPath) || !File.Exists(metaPath))
                return false;

            string metaContent = File.ReadAllText(metaPath);
            if (!metaContent.Contains("detectedRenderPipeline: Universal Render Pipeline"))
            {
                Debug.Log("[PuzzleGlass] Reimporting TCP2 Hybrid Shader 2 for Universal Render Pipeline...");
                AssetDatabase.ImportAsset(HybridShaderPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                return true;
            }

            return false;
        }

        private static void ApplyThemeInternal(bool saveSceneIfClean)
        {
            EnsureTemplateMaterial();
            PuzzleColorUtility.ClearMaterialCache();

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            PuzzleGlassPrefabGenerator.GenerateMaterials();

            bool sceneModified = ApplySceneLightingAndVisuals();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (sceneModified && saveSceneIfClean)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path))
                {
                    EditorSceneManager.SaveScene(activeScene);
                }
            }

            Debug.Log("<color=#32C260><b>[PuzzleGlass]</b></color> Toony Colors Pro 2 Hypercasual theme applied cleanly!");
        }

        private static void EnsureTemplateMaterial()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            Shader toonyShader = PuzzleColorPalette.FindToonyHybridShader();
            Material template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);

            if (template == null)
            {
                template = new Material(toonyShader) { name = "TCP2_Hypercasual_Template" };
                PuzzleColorPalette.ConfigureToonyMaterial(
                    template,
                    PuzzleColorPalette.GetBodyColor(PuzzleColor.Blue),
                    PuzzleColorPalette.GetTcp2ShadowTint(PuzzleColor.Blue),
                    glossyCandy: true,
                    indirectIntensity: 0.18f);
                AssetDatabase.CreateAsset(template, TemplateMaterialPath);
            }
            else
            {
                if (toonyShader != null && template.shader != toonyShader)
                    template.shader = toonyShader;

                PuzzleColorPalette.ConfigureToonyMaterial(
                    template,
                    PuzzleColorPalette.GetBodyColor(PuzzleColor.Blue),
                    PuzzleColorPalette.GetTcp2ShadowTint(PuzzleColor.Blue),
                    glossyCandy: true,
                    indirectIntensity: 0.18f);
                EditorUtility.SetDirty(template);
            }
        }

        private static bool ApplySceneLightingAndVisuals()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
                return false;

            bool wasAlreadyDirty = activeScene.isDirty;
            bool modified = false;

            // 1. Directional key light: clean warm daylight without overexposing colors
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional)
                    {
                        sun = light;
                        break;
                    }
                }
            }

            if (sun != null)
            {
                sun.color = new Color(1.00f, 0.98f, 0.95f, 1f);
                sun.intensity = 1.08f;
                sun.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(sun);
                modified = true;
            }

            // 2. Ambient GI: restrained cool-neutral fill so shadows keep rich saturation
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.79f, 0.84f, 1f);
            modified = true;

            // 3. Enable URP Post-Processing on Main Camera so SampleSceneProfile (Neutral Tonemapping + soft Bloom) renders
            Camera mainCam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (mainCam != null)
            {
                var camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
                if (camData != null && !camData.renderPostProcessing)
                {
                    camData.renderPostProcessing = true;
                    EditorUtility.SetDirty(camData);
                    modified = true;
                }
            }

            // 4. Sync Backdrop palette
            foreach (var backdrop in Object.FindObjectsByType<PuzzleGlassBackdrop>(FindObjectsSortMode.None))
            {
                backdrop.topColor  = new Color(0.905f, 0.910f, 0.965f, 1f);
                backdrop.midColor  = new Color(0.950f, 0.922f, 0.885f, 1f);
                backdrop.botColor  = new Color(0.920f, 0.862f, 0.798f, 1f);
                backdrop.glowColor = new Color(0.975f, 0.960f, 0.938f, 1f);
                backdrop.dotColor  = new Color(0.810f, 0.790f, 0.840f, 1f);
                backdrop.dotStrength = 0.055f;
                backdrop.EnsureBackdropQuad();
                backdrop.SyncWithCamera(true);
                EditorUtility.SetDirty(backdrop);
                modified = true;
            }

            // 5. Rebuild scene 3D components with updated TCP2 materials
            foreach (var board in Object.FindObjectsByType<StagingGridBoard>(FindObjectsSortMode.None))
            {
                board.BuildBoardVisuals();
                EditorUtility.SetDirty(board);
                modified = true;
            }

            foreach (var glass in Object.FindObjectsByType<ShapedGlassContainer>(FindObjectsSortMode.None))
            {
                glass.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(glass);
                modified = true;
            }

            foreach (var monster in Object.FindObjectsByType<MonsterCharacter>(FindObjectsSortMode.None))
            {
                monster.RebuildMonsterVisuals();
                EditorUtility.SetDirty(monster);
                modified = true;
            }

            if (modified)
                EditorSceneManager.MarkSceneDirty(activeScene);

            // Preserve user's prior dirty state decision: only auto-save if scene wasn't dirty before or user explicitly ran menu
            return modified && !wasAlreadyDirty;
        }
    }
}
