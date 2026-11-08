using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public static class PixelArtQuickTester
    {
        [MenuItem("Tools/PuzzleGlass/🐸 Apply Square Glass + Frog Visual (Kare Cam + Kurbağa Görseli)", false, 20)]
        public static void ApplySquareGlassAndFrog()
        {
            var container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            if (container == null)
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
                container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (container != null)
            {
                Undo.RecordObject(container, "Apply Square Glass + Frog");
                container.ApplyShapePreset(GlassShapePreset.SquareFlask, false);
                container.ApplyPixelArtPreset(PixelArtPreset.Frog, true);
                EditorUtility.SetDirty(container);
                SceneView.RepaintAll();
                Debug.Log("<color=#2ED170><b>[PuzzleGlass]</b></color> Kare cam ve Kurbağa piksel sanatı görseli başarıyla sahneye uygulandı!");
            }
        }

        [MenuItem("Tools/PuzzleGlass/❤️ Apply Square Glass + Heart Visual (Kare Cam + Kalp Görseli)", false, 21)]
        public static void ApplySquareGlassAndHeart()
        {
            var container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            if (container == null)
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
                container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (container != null)
            {
                Undo.RecordObject(container, "Apply Square Glass + Heart");
                container.ApplyShapePreset(GlassShapePreset.SquareFlask, false);
                container.ApplyPixelArtPreset(PixelArtPreset.Heart, true);
                EditorUtility.SetDirty(container);
                SceneView.RepaintAll();
                Debug.Log("<color=#2ED170><b>[PuzzleGlass]</b></color> Kare cam ve Kalp piksel sanatı görseli başarıyla sahneye uygulandı!");
            }
        }

        [MenuItem("Tools/PuzzleGlass/🍄 Apply Square Glass + Mushroom Visual (Kare Cam + Mantar Görseli)", false, 22)]
        public static void ApplySquareGlassAndMushroom()
        {
            var container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            if (container == null)
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
                container = Object.FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (container != null)
            {
                Undo.RecordObject(container, "Apply Square Glass + Mushroom");
                container.ApplyShapePreset(GlassShapePreset.SquareFlask, false);
                container.ApplyPixelArtPreset(PixelArtPreset.Mushroom, true);
                EditorUtility.SetDirty(container);
                SceneView.RepaintAll();
                Debug.Log("<color=#2ED170><b>[PuzzleGlass]</b></color> Kare cam ve Mantar piksel sanatı görseli başarıyla sahneye uygulandı!");
            }
        }
    }
}
