using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PuzzleGlass.Editor
{
    /// <summary>
    /// Build sırasında yalnızca paketlenen sahne kopyasındaki geçici havuz toplarını temizler (açılış süresi + APK boyutu)
    /// ve Android build'lerinde Optimized Frame Pacing (Swappy) + kullanılmayan mesh kanalı temizliğini garanti eder.
    /// Editördeki açık sahneye dokunmaz, oyun mantığını değiştirmez.
    /// </summary>
    public sealed class PuzzleGlassBuildOptimizer : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null) return;

            if (!PlayerSettings.stripUnusedMeshComponents)
            {
                PlayerSettings.stripUnusedMeshComponents = true;
            }

            if (report.summary.platform == BuildTarget.Android)
            {
                if (!PlayerSettings.Android.optimizedFramePacing)
                {
                    PlayerSettings.Android.optimizedFramePacing = true;
                }
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Yalnızca gerçek oyuncu build'lerinde çalışır (Play Mode'a ve editör sahnesine dokunmaz)
            if (report == null || !scene.IsValid()) return;

            int removed = StripTransientPoolBallsFromScene(scene);
            if (removed > 0)
            {
                Debug.Log($"[PuzzleGlassBuildOptimizer] Build sahnesinden '{scene.name}' {removed} geçici havuz topu temizlendi (runtime'da BallObjectPool otomatik kurar).");
            }
        }

        private static int StripTransientPoolBallsFromScene(Scene scene)
        {
            int removed = 0;
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                var pools = roots[r].GetComponentsInChildren<BallObjectPool>(true);
                for (int p = 0; p < pools.Length; p++)
                {
                    Transform poolRoot = pools[p].transform.Find("BallObjectPool_Root");
                    if (poolRoot == null) continue;
                    for (int i = poolRoot.childCount - 1; i >= 0; i--)
                    {
                        GameObject child = poolRoot.GetChild(i).gameObject;
                        if (child.GetComponent<BallPhysicsController>() != null)
                        {
                            Object.DestroyImmediate(child);
                            removed++;
                        }
                    }
                }
            }
            return removed;
        }

        [MenuItem("PuzzleGlass/Performance/Apply Recommended Android Settings")]
        public static void ApplyRecommendedAndroidSettings()
        {
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.stripUnusedMeshComponents = true;
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Master);
            AssetDatabase.SaveAssets();
            Debug.Log("[PuzzleGlassBuildOptimizer] Android performans ayarları uygulandı: Swappy=ON, StripUnusedMeshComponents=ON, Android IL2CPP=Master.");
        }

        [MenuItem("PuzzleGlass/Performance/Clean Transient Pool Balls From Open Scene")]
        public static void CleanOpenScenePoolBloat()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid()) return;

            int removed = StripTransientPoolBallsFromScene(activeScene);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                Debug.Log($"[PuzzleGlassBuildOptimizer] Açık sahneden {removed} geçici havuz topu temizlendi. Küçültülmüş sahneyi kaydetmek için Ctrl+S yapabilirsiniz.");
            }
            else
            {
                Debug.Log("[PuzzleGlassBuildOptimizer] Açık sahnede temizlenecek geçici havuz topu bulunamadı.");
            }
        }
    }
}
