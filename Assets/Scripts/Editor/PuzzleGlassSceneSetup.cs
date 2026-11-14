using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleGlass.Editor
{
    [InitializeOnLoad]
    public static class PuzzleGlassSceneSetup
    {
        static PuzzleGlassSceneSetup()
        {
            EditorApplication.delayCall += CheckAndAutoSetup;
        }

        private static void CheckAndAutoSetup()
        {
            if (Application.isPlaying) return;

            // Generate prefabs if missing
            if (!File.Exists("Assets/Prefabs/Monster_Blue.prefab"))
            {
                PuzzleGlassPrefabGenerator.GenerateAllAssets();
            }

            // Ensure open scene container is enabled and populated with physical 3D balls
            var glass = Object.FindFirstObjectByType<ShapedGlassContainer>();
            if (glass != null)
            {
                bool needsRebuild = !glass.enabled ||
                                    glass.shapePreset == GlassShapePreset.SquareFlask ||
                                    BallObjectPool.Instance == null ||
                                    BallObjectPool.Instance.ActiveBalls == null ||
                                    BallObjectPool.Instance.ActiveBalls.Count == 0;

                if (needsRebuild)
                {
                    glass.enabled = true;
                    glass.shapePreset = GlassShapePreset.RectangleBox;
                    glass.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                    glass.particleMode = ParticleArrangementMode.PhysicsBalls;
                    glass.particleSize = 0.265f;
                    glass.ApplyShapePreset(GlassShapePreset.RectangleBox, false);
                    glass.RebuildContainer();
                    SceneView.RepaintAll();
                }
            }

            var gm = Object.FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm != null)
            {
                gm.EnsureGameplayControllers();
            }
        }

        [MenuItem("Tools/PuzzleGlass/🧹 Sahneyi Temizle ve Kutuları Senkronize Et", false, 20)]
        public static void CleanAndSyncActiveScene()
        {
            var glass = Object.FindFirstObjectByType<ShapedGlassContainer>();
            var gm = Object.FindFirstObjectByType<PuzzleGlassGameManager>();

            if (gm != null && gm.currentLevel != null)
            {
                gm.LoadLevel(gm.currentLevel);
                EditorUtility.SetDirty(gm);
            }
            else if (gm != null && gm.levels != null && gm.levels.Count > 0 && gm.levels[0] != null)
            {
                gm.LoadLevel(gm.levels[0]);
                EditorUtility.SetDirty(gm);
            }
            else
            {
                if (glass != null)
                {
                    glass.shapePreset = GlassShapePreset.RectangleBox;
                    glass.particleMode = ParticleArrangementMode.PhysicsBalls;
                    glass.particleSize = 0.265f;
                    glass.CleanOrphanedChildren();
                    glass.freezePatternInPlace = false;
                    glass.ApplyShapePreset(GlassShapePreset.RectangleBox, false);
                    glass.RebuildContainer();

                    var bcm = glass.GetComponent<BallContainerManager>().OrNull() ?? glass.gameObject.AddComponent<BallContainerManager>();
                    bcm.glassContainer = glass;
                    bcm.RebuildColliders();
                }

                if (gm != null)
                {
                    gm.activePatternType = ShapePatternType.GumballMix;
                    gm.ClearMonsters();
                    if (glass != null)
                    {
                        glass.PopulateBottlePhysics();
                    }
                    EditorUtility.SetDirty(gm);
                }
            }

            if (glass != null) EditorUtility.SetDirty(glass);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            SceneView.RepaintAll();
            Debug.Log("<color=#00FF88><b>[PuzzleGlass]</b></color> Sahne başarıyla temizlendi ve sıralı top havuzu ile senkronize edildi!");
        }

        [MenuItem("GameObject/PuzzleGlass/Create Complete Scene", false, 10)]
        public static void BuildCompleteScene()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup PuzzleGlass Reference Scene");

            // Ensure Materials & Prefabs exist on disk first!
            PuzzleGlassPrefabGenerator.GenerateAllAssets();

            // 1. Setup Camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                camObj.AddComponent<AudioListener>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.965f, 0.933f, 0.882f, 1f); // #F6EEE1 warm cream
            cam.transform.position = new Vector3(0, 0.30f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = 4.2f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // 2. Setup Directional Light
            Light dirLight = Object.FindFirstObjectByType<Light>();
            if (dirLight == null || dirLight.type != LightType.Directional)
            {
                GameObject lightObj = new GameObject("Directional Light");
                dirLight = lightObj.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }

            dirLight.color = new Color(1.0f, 0.98f, 0.95f, 1f);
            dirLight.intensity = 1.35f;
            dirLight.shadows = LightShadows.None;
            dirLight.transform.rotation = Quaternion.Euler(45f, -25f, 0);

            // Subtle ambient light
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.96f, 0.94f, 0.91f, 1f);

            // 3. Root Manager Object
            GameObject root = GameObject.Find("PuzzleGlassGameManager");
            if (root == null)
            {
                root = new GameObject("PuzzleGlassGameManager");
                Undo.RegisterCreatedObjectUndo(root, "Create PuzzleGlassGameManager");
            }

            PuzzleGlassGameManager manager = root.GetComponent<PuzzleGlassGameManager>();
            if (manager == null) manager = root.AddComponent<PuzzleGlassGameManager>();
            manager.mainCamera = cam;

            // 4. Shaped Glass Container
            GameObject glassObj = GameObject.Find("ShapedGlassContainer");
            if (glassObj == null)
            {
                glassObj = new GameObject("ShapedGlassContainer");
                glassObj.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(glassObj, "Create ShapedGlassContainer");
            }
            glassObj.transform.localPosition = new Vector3(0, 1.35f, 0);

            ShapedGlassContainer glass = glassObj.GetComponent<ShapedGlassContainer>();
            if (glass == null) glass = glassObj.AddComponent<ShapedGlassContainer>();
            glass.glassMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            glass.glassHighlightMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
            manager.glassContainer = glass;

            // 5. Disable legacy 4 Slot Bases & configure framed bottom StagingGridBoard directly under the glass
            GameObject slotsObj = GameObject.Find("SlotSystem");
            if (slotsObj != null)
            {
                slotsObj.SetActive(false);
                manager.slotSystem = slotsObj.GetComponent<SlotSystem>();
            }

            GameObject gridObj = GameObject.Find("StagingGridBoard");
            if (gridObj == null)
            {
                gridObj = new GameObject("StagingGridBoard");
                gridObj.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(gridObj, "Create StagingGridBoard");
            }
            StagingGridBoard gridBoard = gridObj.GetComponent<StagingGridBoard>();
            if (gridBoard == null) gridBoard = gridObj.AddComponent<StagingGridBoard>();
            manager.stagingGridBoard = gridBoard;

            // 6. Cube Dispenser
            CubeDispenser dispenser = root.GetComponent<CubeDispenser>();
            if (dispenser == null) dispenser = root.AddComponent<CubeDispenser>();
            dispenser.glassContainer = glass;
            manager.cubeDispenser = dispenser;

            // 7. Rebuild Rounded-Rectangle Glass Ball Pool & align StagingGridBoard snugly below it
            manager.activePatternType = ShapePatternType.GumballMix;
            glass.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            glass.curveSubdivisions = 14;
            glass.particleSize = 0.24f;
            glass.ApplyShapePreset(GlassShapePreset.RectangleBox, false);
            glass.ApplyColorTheme(GlassColorTheme.SkyBlueGlossy, true);
            gridBoard.Configure(5, 4, glass);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<color=#2E9AF7><b>[PuzzleGlass]</b></color> Reference scene successfully generated with Rounded-Rectangle Glass Ball Pool!");
        }
    }
}
