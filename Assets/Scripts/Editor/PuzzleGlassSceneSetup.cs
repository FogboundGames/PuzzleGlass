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

            // If monsters or manager are missing or using old giant scale, build the reference scene
            var existingMonster = Object.FindFirstObjectByType<MonsterCharacter>();
            if (Object.FindFirstObjectByType<PuzzleGlassGameManager>() == null ||
                existingMonster == null ||
                existingMonster.transform.localScale.x > 0.85f)
            {
                Debug.Log("<color=#2E9AF7><b>[PuzzleGlass]</b></color> Configuring reference scene with 3D open-mouth monsters & balanced proportions...");
                BuildCompleteScene();
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
            dirLight.shadows = LightShadows.Soft;
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

            // 8. Place Monsters directly in the Scene (visible in Edit Mode and Play Mode!)
            // Blue Monster (10) on Left under Slots 1 & 2
            GameObject blueObj = new GameObject("Monster_Blue_10");
            Undo.RegisterCreatedObjectUndo(blueObj, "Create Monster Blue");
            blueObj.transform.position = new Vector3(-0.72f, -1.95f, 0f);
            blueObj.transform.localScale = Vector3.one * 0.70f;
            MonsterCharacter blueMonster = blueObj.AddComponent<MonsterCharacter>();
            blueMonster.Initialize(PuzzleColor.Blue, 10, "Blue_10");
            manager.activeMonsters.Add(blueMonster);
            dispenser.RegisterMonsterOrder(blueMonster);

            // Yellow Monster (20) on Right under Slots 3 & 4
            GameObject yellowObj = new GameObject("Monster_Yellow_20");
            Undo.RegisterCreatedObjectUndo(yellowObj, "Create Monster Yellow");
            yellowObj.transform.position = new Vector3(0.72f, -1.95f, 0f);
            yellowObj.transform.localScale = Vector3.one * 0.70f;
            MonsterCharacter yellowMonster = yellowObj.AddComponent<MonsterCharacter>();
            yellowMonster.Initialize(PuzzleColor.Yellow, 20, "Yellow_20");
            manager.activeMonsters.Add(yellowMonster);
            dispenser.RegisterMonsterOrder(yellowMonster);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<color=#2E9AF7><b>[PuzzleGlass]</b></color> Reference scene successfully generated with 3D Monsters & Shaped Glass!");
        }
    }
}
