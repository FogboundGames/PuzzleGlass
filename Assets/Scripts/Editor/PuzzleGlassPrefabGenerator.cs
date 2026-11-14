using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public static class PuzzleGlassPrefabGenerator
    {
        public static void GenerateAllAssets()
        {
            EnsureDirectories();
            GenerateMaterials();
            GeneratePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=#2ED170><b>[PuzzleGlass]</b></color> All Materials and Prefabs successfully generated in Assets/Materials and Assets/Prefabs!");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        public static void GenerateMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader toonyShader = PuzzleColorPalette.FindToonyHybridShader();

            // Rim
            SaveOrUpdateMaterial("Assets/Materials/Mat_GlassRim.mat", toonyShader, mat =>
            {
                Color col = new Color(0.42f, 0.72f, 0.92f, 1f);
                PuzzleColorPalette.ConfigureToonyMaterial(mat, col, new Color(0.56f, 0.62f, 0.82f, 1f), glossyCandy: true, indirectIntensity: 0.18f);
            });

            // Glass Highlights
            SaveOrUpdateMaterial("Assets/Materials/Mat_GlassHighlight.mat", litShader, mat =>
            {
                Color col = new Color(1f, 1f, 1f, 0.85f);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.98f);
            });

            // Slots
            SaveOrUpdateMaterial("Assets/Materials/Mat_SlotFrame.mat", toonyShader, mat =>
            {
                Color col = new Color(0.72f, 0.80f, 0.91f, 1f);
                PuzzleColorPalette.ConfigureToonyMaterial(mat, col, new Color(0.66f, 0.70f, 0.84f, 1f), glossyCandy: false, indirectIntensity: 0.16f);
            });

            SaveOrUpdateMaterial("Assets/Materials/Mat_SlotCavity.mat", toonyShader, mat =>
            {
                Color col = new Color(0.48f, 0.62f, 0.82f, 1f);
                PuzzleColorPalette.ConfigureToonyMaterial(mat, col, new Color(0.58f, 0.62f, 0.78f, 1f), glossyCandy: false, indirectIntensity: 0.16f);
            });

            // Badge
            SaveOrUpdateMaterial("Assets/Materials/Mat_BellyBadge.mat", toonyShader, mat =>
            {
                Color col = new Color(0.92f, 0.93f, 0.96f, 1f);
                PuzzleColorPalette.ConfigureToonyMaterial(mat, col, new Color(0.74f, 0.77f, 0.86f, 1f), glossyCandy: false, indirectIntensity: 0.16f);
            });

            // Monsters & Cubes
            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                Color bodyCol = PuzzleColorPalette.GetBodyColor(col);
                Color mouthCol = PuzzleColorPalette.GetDarkMouthColor(col);
                Color shadowTint = PuzzleColorPalette.GetTcp2ShadowTint(col);

                SaveOrUpdateMaterial($"Assets/Materials/Mat_Monster_{col}.mat", toonyShader, mat =>
                {
                    PuzzleColorPalette.ConfigureToonyMaterial(mat, bodyCol, shadowTint, glossyCandy: true, indirectIntensity: 0.18f);
                });

                SaveOrUpdateMaterial($"Assets/Materials/Mat_Mouth_{col}.mat", toonyShader, mat =>
                {
                    PuzzleColorPalette.ConfigureToonyMaterial(mat, mouthCol, shadowTint, glossyCandy: false, indirectIntensity: 0.15f);
                });

                SaveOrUpdateMaterial($"Assets/Materials/Mat_Cube_{col}.mat", toonyShader, mat =>
                {
                    PuzzleColorPalette.ConfigureToonyMaterial(mat, bodyCol, shadowTint, glossyCandy: true, indirectIntensity: 0.18f);
                    if (mat.HasProperty("_ShadowColor")) mat.SetColor("_ShadowColor", PuzzleColorPalette.GetCubeShadowColor(col));
                    if (mat.HasProperty("_HighlightColor")) mat.SetColor("_HighlightColor", PuzzleColorPalette.GetCubeHighlightColor(col));
                });
            }
        }

        private static void SaveOrUpdateMaterial(string path, Shader shader, Action<Material> configure)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                configure(mat);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                if (shader != null && mat.shader != shader)
                    mat.shader = shader;
                configure(mat);
                EditorUtility.SetDirty(mat);
            }
        }

        private static void GeneratePrefabs()
        {
            // Monster Prefabs for each color
            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                int defaultCount = (col == PuzzleColor.Blue ? 10 : (col == PuzzleColor.Yellow ? 20 : 15));
                GameObject tempObj = new GameObject($"Monster_{col}");
                MonsterCharacter monster = tempObj.AddComponent<MonsterCharacter>();
                monster.Initialize(col, defaultCount, $"Monster_{col}");

                string prefabPath = $"Assets/Prefabs/Monster_{col}.prefab";
                PrefabUtility.SaveAsPrefabAsset(tempObj, prefabPath);
                GameObject.DestroyImmediate(tempObj);
            }

            // SlotPlatform Prefab
            GameObject slotTemp = new GameObject("SlotPlatform");
            SlotPlatform slot = slotTemp.AddComponent<SlotPlatform>();
            Material frameMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotFrame.mat");
            Material cavMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotCavity.mat");
            slot.Initialize(0, frameMat, cavMat);
            PrefabUtility.SaveAsPrefabAsset(slotTemp, "Assets/Prefabs/SlotPlatform.prefab");
            GameObject.DestroyImmediate(slotTemp);

            // ShapedGlassContainer Prefab
            GameObject glassTemp = new GameObject("ShapedGlassContainer");
            ShapedGlassContainer glass = glassTemp.AddComponent<ShapedGlassContainer>();
            glass.glassMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            glass.glassHighlightMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
            glass.RebuildContainer();
            PrefabUtility.SaveAsPrefabAsset(glassTemp, "Assets/Prefabs/ShapedGlassContainer.prefab");
            GameObject.DestroyImmediate(glassTemp);
        }
    }
}
