using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    [InitializeOnLoad]
    public static class StarterLevelsSetup
    {
        static StarterLevelsSetup()
        {
            EditorApplication.delayCall += EnsureStarterLevels;
        }

        [MenuItem("Tools/PuzzleGlass/🚀 Hazır Seviyeleri Kur (Rainbow, Cat, Frog)", false, 10)]
        public static void EnsureStarterLevels()
        {
            const string levelsFolder = "Assets/Levels";
            if (!AssetDatabase.IsValidFolder(levelsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
            }

            var levelRainbow = CreateOrUpdateLevel(
                "Level_Rainbow",
                "Seviye 1 - Renkli Gökkuşağı",
                PixelArtPreset.Rainbow,
                ShapePatternType.Rainbow,
                GlassShapePreset.SquareFlask,
                1);

            var levelCat = CreateOrUpdateLevel(
                "Level_Cat",
                "Seviye 2 - Sevimli Kedi",
                PixelArtPreset.Cat,
                ShapePatternType.Cat,
                GlassShapePreset.SquareFlask,
                2);

            var levelFrog = CreateOrUpdateLevel(
                "Level_Frog",
                "Seviye 3 - Taçlı Kurbağa",
                PixelArtPreset.Frog,
                ShapePatternType.Frog,
                GlassShapePreset.BulbousFlask,
                3);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Sahnede GameManager ve Container bileşenlerini güncelle
            EnsureSceneComponents(levelRainbow, levelCat, levelFrog);
        }

        private static PuzzleGlassLevelData CreateOrUpdateLevel(
            string fileName,
            string displayName,
            PixelArtPreset preset,
            ShapePatternType shapePattern,
            GlassShapePreset glassShape,
            int levelNum)
        {
            string path = $"Assets/Levels/{fileName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            bool isNew = false;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
                isNew = true;
            }

            asset.levelNumber = levelNum;
            asset.levelName = displayName;
            asset.difficulty = LevelDifficulty.Easy;
            asset.slotCount = 4;
            asset.slotSpacing = 0.84f;
            asset.glassPreset = glassShape;
            asset.particleMode = ParticleArrangementMode.PixelArtVisual;
            asset.pixelArtPreset = preset;
            asset.patternShape = shapePattern;
            asset.particleSize = 0.16f;
            asset.gridCellSpacing = 0.165f;
            asset.initialSimulationMode = SandSimulationMode.StaticImage;

            var pattern = PixelArtLibrary.GetPreset(preset);
            if (pattern != null)
            {
                asset.customPattern = pattern;

                // Renk ve canavar kotalarını hesapla
                var grid = pattern.GetColorGrid();
                Dictionary<PuzzleColor, int> counts = new Dictionary<PuzzleColor, int>();
                for (int y = 0; y < pattern.height; y++)
                {
                    for (int x = 0; x < pattern.width; x++)
                    {
                        var col = grid[x, y];
                        if (col.HasValue)
                        {
                            if (!counts.ContainsKey(col.Value)) counts[col.Value] = 0;
                            counts[col.Value]++;
                        }
                    }
                }

                asset.monsters = new List<LevelMonsterEntry>();
                foreach (var kvp in counts)
                {
                    asset.monsters.Add(new LevelMonsterEntry(kvp.Key, kvp.Value));
                }
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static void EnsureSceneComponents(params PuzzleGlassLevelData[] starterLevels)
        {
            var glass = Object.FindFirstObjectByType<ShapedGlassContainer>();
            if (glass != null)
            {
                var engine = glass.GetComponent<SandSimulationEngine>() ?? glass.gameObject.AddComponent<SandSimulationEngine>();
                var bridge = glass.GetComponent<SandMonsterFlowBridge>() ?? glass.gameObject.AddComponent<SandMonsterFlowBridge>();

                engine.glassContainer = glass;
                bridge.sandEngine = engine;
                bridge.glassContainer = glass;

                EditorUtility.SetDirty(glass);
                EditorUtility.SetDirty(engine);
                EditorUtility.SetDirty(bridge);
            }

            var gm = Object.FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm != null)
            {
                if (gm.levels == null) gm.levels = new List<PuzzleGlassLevelData>();

                foreach (var lvl in starterLevels)
                {
                    if (lvl != null && !gm.levels.Contains(lvl))
                    {
                        gm.levels.Add(lvl);
                    }
                }

                if (gm.levels.Count > 0 && gm.currentLevel == null)
                {
                    gm.currentLevel = gm.levels[0];
                }

                EditorUtility.SetDirty(gm);
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
            }
        }
    }
}
