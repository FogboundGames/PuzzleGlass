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

            // Ensure scene matches the 1-to-1 Reference Design (Blue 10 + Yellow 20 + Wide Bulbous Sky-Blue Flask)
            var manager = Object.FindFirstObjectByType<PuzzleGlassGameManager>();
            bool needsRebuild = manager == null ||
                                manager.activeMonsters == null ||
                                manager.activeMonsters.Count != 2 ||
                                manager.activeMonsters[0] == null ||
                                manager.activeMonsters[0].colorType != PuzzleColor.Blue ||
                                manager.activeMonsters[1] == null ||
                                manager.activeMonsters[1].colorType != PuzzleColor.Yellow;

            if (needsRebuild)
            {
                Debug.Log("<color=#2E9AF7><b>[PuzzleGlass]</b></color> Building 1-to-1 Reference Flask & Monster Scene...");
                BuildCompleteScene();
            }
            else
            {
                var glass = Object.FindFirstObjectByType<ShapedGlassContainer>();
                var dispenser = Object.FindFirstObjectByType<CubeDispenser>();
                bool dirty = false;

                if (glass != null)
                {
                    glass.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                    glass.particlesPerMonsterCount = 1;
                    if (glass.particleSize < 0.145f)
                    {
                        glass.particleSize = 0.155f;
                    }
                    glass.curveSubdivisions = 14;
                    glass.ApplyShapePreset(GlassShapePreset.DiamondCarafe, false);
                    glass.ApplyColorTheme(GlassColorTheme.SkyBlueGlossy, true);
                    EditorUtility.SetDirty(glass);
                    dirty = true;
                }

                if (dispenser != null && manager != null && manager.activeMonsters != null)
                {
                    dispenser.feedInterval = 0.11f;
                    dispenser.ClearOrders();
                    for (int i = 0; i < manager.activeMonsters.Count; i++)
                    {
                        if (manager.activeMonsters[i] != null)
                            dispenser.RegisterMonsterOrder(manager.activeMonsters[i]);
                    }
                    EditorUtility.SetDirty(dispenser);
                    dirty = true;
                }

                var slots = Object.FindFirstObjectByType<SlotSystem>();
                if (slots != null)
                {
                    slots.RebuildSlots();
                    EditorUtility.SetDirty(slots);
                    dirty = true;
                }

                var dirLight = Object.FindFirstObjectByType<Light>();
                if (dirLight != null && dirLight.shadows != LightShadows.None)
                {
                    dirLight.shadows = LightShadows.None;
                    EditorUtility.SetDirty(dirLight);
                    dirty = true;
                }

                if (manager != null && manager.activeMonsters != null)
                {
                    for (int i = 0; i < manager.activeMonsters.Count; i++)
                    {
                        if (manager.activeMonsters[i] != null)
                        {
                            manager.activeMonsters[i].RebuildMonsterVisuals();
                            EditorUtility.SetDirty(manager.activeMonsters[i]);
                        }
                    }
                }

                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                    EditorSceneManager.SaveOpenScenes();
                }
            }
        }

        [MenuItem("Tools/PuzzleGlass/Build Reference Scene", false, 10)]
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
            glassObj.transform.localPosition = new Vector3(0, 1.80f, 0);

            ShapedGlassContainer glass = glassObj.GetComponent<ShapedGlassContainer>();
            if (glass == null) glass = glassObj.AddComponent<ShapedGlassContainer>();
            glass.glassMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            glass.glassHighlightMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
            glass.RebuildContainer();
            manager.glassContainer = glass;

            // 5. 4 Slot Bases
            GameObject slotsObj = GameObject.Find("SlotSystem");
            if (slotsObj == null)
            {
                slotsObj = new GameObject("SlotSystem");
                slotsObj.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(slotsObj, "Create SlotSystem");
            }
            slotsObj.transform.localPosition = new Vector3(0, -0.35f, 0);

            SlotSystem slots = slotsObj.GetComponent<SlotSystem>();
            if (slots == null) slots = slotsObj.AddComponent<SlotSystem>();
            slots.frameMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotFrame.mat");
            slots.cavityMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotCavity.mat");
            slots.slotSpacing = 0.84f;
            slots.RebuildSlots();
            manager.slotSystem = slots;

            // 6. Cube Dispenser
            CubeDispenser dispenser = root.GetComponent<CubeDispenser>();
            if (dispenser == null) dispenser = root.AddComponent<CubeDispenser>();
            dispenser.glassContainer = glass;
            manager.cubeDispenser = dispenser;

            // 7. Remove previous monsters from scene
            foreach (var existingMonster in Object.FindObjectsByType<MonsterCharacter>(FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(existingMonster.gameObject);
            }
            manager.activeMonsters.Clear();
            dispenser.ClearOrders();

            // 8. Place the 2 Reference Monsters at the bottom (Blue 10 on Left, Yellow 20 on Right)
            // matching the user's reference image 1-to-1!
            float[] xPositions = new float[] { -0.75f, 0.75f };
            PuzzleColor[] colors = new PuzzleColor[]
            {
                PuzzleColor.Blue,
                PuzzleColor.Yellow
            };
            int[] counts = new int[] { 10, 20 };

            for (int i = 0; i < 2; i++)
            {
                GameObject monObj = new GameObject($"Monster_{colors[i]}_{counts[i]}");
                monObj.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(monObj, $"Create Monster {colors[i]}");
                monObj.transform.position = new Vector3(xPositions[i], -1.98f, 0f);
                monObj.transform.localScale = Vector3.one * 0.72f;

                MonsterCharacter mon = monObj.AddComponent<MonsterCharacter>();
                mon.Initialize(colors[i], counts[i], $"{colors[i]}_{counts[i]}");
                manager.activeMonsters.Add(mon);
                dispenser.RegisterMonsterOrder(mon);
            }

            // Rebuild flask AFTER monsters are registered
            glass.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            glass.curveSubdivisions = 14;
            glass.ApplyShapePreset(GlassShapePreset.DiamondCarafe, false);
            glass.ApplyColorTheme(GlassColorTheme.SkyBlueGlossy, true);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<color=#2E9AF7><b>[PuzzleGlass]</b></color> Reference scene successfully generated with 3D Monsters & Shaped Glass!");
        }
    }
}
