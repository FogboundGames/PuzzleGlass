using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public static class PuzzleGlassPrefabGenerator
    {
        [MenuItem("Tools/PuzzleGlass/Generate Prefab & Material Assets", false, 20)]
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

        private static void GenerateMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Rim
            SaveOrUpdateMaterial("Assets/Materials/Mat_GlassRim.mat", litShader, mat =>
            {
                Color col = new Color(0.38f, 0.74f, 0.98f, 1f);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.94f);
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
            SaveOrUpdateMaterial("Assets/Materials/Mat_SlotFrame.mat", litShader, mat =>
            {
                Color col = new Color(0.75f, 0.84f, 0.96f, 1f);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            });

            SaveOrUpdateMaterial("Assets/Materials/Mat_SlotCavity.mat", litShader, mat =>
            {
                Color col = new Color(0.48f, 0.65f, 0.88f, 1f);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.60f);
            });

            // Badge
            SaveOrUpdateMaterial("Assets/Materials/Mat_BellyBadge.mat", litShader, mat =>
            {
                mat.color = Color.white;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);
            });

            // Monsters
            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                Color bodyCol = PuzzleColorPalette.GetBodyColor(col);
                Color mouthCol = PuzzleColorPalette.GetDarkMouthColor(col);

                SaveOrUpdateMaterial($"Assets/Materials/Mat_Monster_{col}.mat", litShader, mat =>
                {
                    mat.color = bodyCol;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", bodyCol);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.88f);
                });

                SaveOrUpdateMaterial($"Assets/Materials/Mat_Mouth_{col}.mat", litShader, mat =>
                {
                    mat.color = mouthCol;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mouthCol);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.50f);
                });

                Shader candyShader = Shader.Find("PuzzleGlass/CandyCubeLit") ?? litShader;
                SaveOrUpdateMaterial($"Assets/Materials/Mat_Cube_{col}.mat", candyShader, mat =>
                {
                    mat.shader = candyShader;
                    Color baseCol = PuzzleColorPalette.GetCubeColor(col);
                    Color shadowCol = PuzzleColorPalette.GetCubeShadowColor(col);
                    Color highCol = PuzzleColorPalette.GetCubeHighlightColor(col);
                    mat.color = baseCol;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
                    if (mat.HasProperty("_ShadowColor")) mat.SetColor("_ShadowColor", shadowCol);
                    if (mat.HasProperty("_HighlightColor")) mat.SetColor("_HighlightColor", highCol);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.74f);
                    if (mat.HasProperty("_BevelHighlight")) mat.SetFloat("_BevelHighlight", 0.56f);
                    if (mat.HasProperty("_RimSeparation")) mat.SetFloat("_RimSeparation", 0.38f);
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
