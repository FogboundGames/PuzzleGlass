using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Özel camlı 3 örnek level oluşturur (Tools menüsünden bir kez tıklanır).
    /// Her level otomatik üretilir, camı ayarlanır ve o camın ölçüsünde çözücüyle doğrulanır;
    /// çözülemezse başka bir tohumla yeniden üretilir.
    /// </summary>
    public static class SampleGlassLevels
    {
        private const string LevelsFolder = "Assets/Levels";

        private struct Spec
        {
            public string name;
            public LevelDifficulty difficulty;
            public float width, height, corner, frame;
            public GlassColorTheme theme;
            public Color frameMain, frameHigh, frameEdge;
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                name = "Geniş Panorama", difficulty = LevelDifficulty.Easy,
                width = 2.92f, height = 1.05f, corner = 0.20f, frame = 0.105f,
                theme = GlassColorTheme.EmeraldGreen,
                frameMain = new Color(0.30f, 0.76f, 0.62f), frameHigh = new Color(0.76f, 0.96f, 0.88f), frameEdge = new Color(0.18f, 0.56f, 0.46f)
            },
            new Spec
            {
                name = "Kapsül Cam", difficulty = LevelDifficulty.Medium,
                width = 2.40f, height = 1.30f, corner = 0.60f, frame = 0.115f,
                theme = GlassColorTheme.RoseQuartz,
                frameMain = new Color(0.90f, 0.48f, 0.52f), frameHigh = new Color(0.98f, 0.82f, 0.84f), frameEdge = new Color(0.70f, 0.32f, 0.38f)
            },
            new Spec
            {
                name = "Yüksek Kule", difficulty = LevelDifficulty.Hard,
                width = 2.10f, height = 1.75f, corner = 0.30f, frame = 0.115f,
                theme = GlassColorTheme.AmethystPurple,
                frameMain = new Color(0.32f, 0.36f, 0.48f), frameHigh = new Color(0.68f, 0.72f, 0.84f), frameEdge = new Color(0.20f, 0.22f, 0.32f)
            }
        };

        [MenuItem("Tools/PuzzleGlass/Örnek: Özel camlı 3 level oluştur", false, 30)]
        public static void Create()
        {
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);

            int next = 1;
            foreach (string guid in AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder }))
            {
                var a = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) next = Mathf.Max(next, a.levelNumber + 1);
            }

            var report = new List<string>();
            PuzzleGlassLevelData first = null;
            foreach (var spec in Specs)
            {
                var lvl = ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
                lvl.levelNumber = next;
                lvl.difficulty = spec.difficulty;

                SolveResult solve = null;
                int tries = 0;
                // Az deneme + düşük arama sınırı: Unity uzun süre donmasın (ilerleme çubuğundan iptal edilebilir)
                for (int seed = next * 7919; tries < 8; seed += 101, tries++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Özel camlı leveller", $"{spec.name}: deneme {tries + 1}/8", (report.Count + tries / 8f) / Specs.Length))
                        break;
                    PuzzleLevelGenerator.GenerateForLevelData(lvl, randomSeed: seed);
                    ApplyGlass(lvl, spec);
                    solve = PuzzleLevelSolver.Solve(lvl, GlassShapeData.GetDims(lvl), 5000);
                    if (solve.status == SolveStatus.Solved) break;
                }

                lvl.levelName = $"Level {next} - {spec.name}";
                string path = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{next:00}.asset");
                AssetDatabase.CreateAsset(lvl, path);
                if (first == null) first = lvl;

                string state = solve == null ? "kontrol edilemedi"
                    : solve.status == SolveStatus.Solved ? $"çözülebilir (en az {solve.minSlides} kaydırma)"
                    : solve.status == SolveStatus.LimitReached ? "çözüm belirsiz (arama sınırı)"
                    : "ÇÖZÜLEMEDİ";
                report.Add($"Level {next} · {spec.name} · {spec.width:0.00}×{spec.height:0.00} · {lvl.monsters.Count} blok, {lvl.GetTotalBallCount()} top · {state}");
                next++;
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();

            // Oyundaki level listesini güncelle
            var gm = Object.FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm != null)
            {
                var all = new List<PuzzleGlassLevelData>();
                foreach (string guid in AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder }))
                {
                    var a = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (a != null) all.Add(a);
                }
                all.Sort((x, y) => x.levelNumber.CompareTo(y.levelNumber));
                gm.levels = all;
                EditorUtility.SetDirty(gm);
            }

            EditorUtility.DisplayDialog("Özel camlı leveller oluşturuldu", string.Join("\n", report), "Tamam");
            if (first != null) GlassStudioWindow.OpenFor(first);
        }

        private static void ApplyGlass(PuzzleGlassLevelData lvl, Spec s)
        {
            lvl.useCustomGlassShape = true;
            lvl.glassPreset = Mathf.Abs(s.width - s.height) < 0.12f ? GlassShapePreset.SquareBox : GlassShapePreset.RectangleBox;
            lvl.symmetricMode = true;
            lvl.widthScale = 1f;
            lvl.heightScale = 1f;
            lvl.showShoulderHandles = false;
            lvl.contourPoints = GlassShapeData.BuildBoxContour(s.width, s.height, GlassShapeData.DefaultBottomY);
            lvl.boxCornerRadius = Mathf.Min(s.corner, Mathf.Min(s.width, s.height) * 0.5f);
            lvl.boxFrameThickness = s.frame;
            LevelDesignerWindow.ApplyThemeColorsToLevelData(lvl, s.theme);
            lvl.boxFrameColor = s.frameMain;
            lvl.boxFrameHighlightColor = s.frameHigh;
            lvl.boxFrameEdgeShadeColor = s.frameEdge;
        }
    }
}
