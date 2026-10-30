using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuzzleGlass
{
    public class PuzzleGlassGameManager : MonoBehaviour
    {
        public static PuzzleGlassGameManager Instance { get; private set; }

        [Header("Core References")]
        public ShapedGlassContainer glassContainer;
        public SlotSystem slotSystem;
        public CubeDispenser cubeDispenser;
        public Camera mainCamera;

        [Header("Level System")]
        public List<PuzzleGlassLevelData> levels = new List<PuzzleGlassLevelData>();
        public int currentLevelIndex = 0;
        public PuzzleGlassLevelData currentLevel;

        [Header("Active Monsters")]
        public List<MonsterCharacter> activeMonsters = new List<MonsterCharacter>();

        [Header("Testing & Debug GUI")]
        public bool showDebugGui = true;

        private void Awake()
        {
            Instance = this;
            SetupCamera();

            if (activeMonsters.Count > 0 && cubeDispenser != null)
            {
                cubeDispenser.ClearOrders();
                foreach (var m in activeMonsters)
                {
                    if (m != null) cubeDispenser.RegisterMonsterOrder(m);
                }
            }
        }

        private void Start()
        {
            if (activeMonsters.Count == 0)
            {
                SetupReferenceScenario();
            }
        }

        private void SetupCamera()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = new Color(0.965f, 0.933f, 0.882f, 1f); // #F6EEE1 warm cream

                // Orthographic framing proportioned cleanly for vertical mobile screen
                mainCamera.transform.position = new Vector3(0, 0.30f, -10f);
                mainCamera.transform.rotation = Quaternion.identity;
                mainCamera.orthographic = true;
                mainCamera.orthographicSize = 4.2f;
            }
        }

        private void Update()
        {
            HandleKeyboardInput();
            HandlePointerInput();
        }

        private void HandleKeyboardInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) FeedOneCube();
                if (kb.aKey.wasPressedThisFrame) ToggleAutoFeed();
                if (kb.digit1Key.wasPressedThisFrame) SetupReferenceScenario();
                if (kb.digit2Key.wasPressedThisFrame) SetupSameColorScenario();
                if (kb.digit3Key.wasPressedThisFrame) SetupFourColorsScenario();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) FeedOneCube();
            if (Input.GetKeyDown(KeyCode.A)) ToggleAutoFeed();
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetupReferenceScenario();
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetupSameColorScenario();
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetupFourColorsScenario();
#endif
        }

        private void HandlePointerInput()
        {
            bool pointerDown = false;
            Vector2 screenPos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                pointerDown = true;
                screenPos = Mouse.current.position.ReadValue();
            }
            else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                pointerDown = true;
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                pointerDown = true;
                screenPos = Pointer.current.position.ReadValue();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                pointerDown = true;
                screenPos = Input.mousePosition;
            }
#endif

            if (pointerDown)
            {
                Camera cam = mainCamera != null ? mainCamera : Camera.main;
                if (cam != null)
                {
                    MonsterCharacter clickedMonster = null;

                    // 1. Raycast to 3D BoxCollider
                    Ray ray = cam.ScreenPointToRay(screenPos);
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                    {
                        clickedMonster = hit.collider.GetComponentInParent<MonsterCharacter>();
                    }

                    // 2. Proximity fallback: check screen distance to monster centers (ensures flawless click on mobile or editor)
                    if (clickedMonster == null)
                    {
                        float closestDist = float.MaxValue;
                        float maxPixelRadius = 140f;

                        foreach (var m in activeMonsters)
                        {
                            if (m == null || m.assignedSlot != null) continue;
                            Vector3 screenP = cam.WorldToScreenPoint(m.transform.position);
                            float d = Vector2.Distance(screenPos, new Vector2(screenP.x, screenP.y));
                            if (d < maxPixelRadius && d < closestDist)
                            {
                                closestDist = d;
                                clickedMonster = m;
                            }
                        }
                    }

                    if (clickedMonster != null)
                    {
                        clickedMonster.TryHopToNextAvailableSlot();
                    }
                }
            }
        }

        public void ClearMonsters()
        {
            for (int i = 0; i < activeMonsters.Count; i++)
            {
                if (activeMonsters[i] != null)
                {
                    if (Application.isPlaying)
                        Destroy(activeMonsters[i].gameObject);
                    else
                        DestroyImmediate(activeMonsters[i].gameObject);
                }
            }
            activeMonsters.Clear();

            // Also clean up any orphaned MonsterCharacter children
            var strayMonsters = FindObjectsByType<MonsterCharacter>(FindObjectsSortMode.None);
            for (int i = 0; i < strayMonsters.Length; i++)
            {
                if (strayMonsters[i] != null)
                {
                    if (Application.isPlaying)
                        Destroy(strayMonsters[i].gameObject);
                    else
                        DestroyImmediate(strayMonsters[i].gameObject);
                }
            }

            if (slotSystem != null)
            {
                slotSystem.ClearAllOccupied();
            }
            if (cubeDispenser != null)
            {
                cubeDispenser.ClearOrders();
            }
        }

        /// <summary>
        /// Loads any PuzzleGlassLevelData asset into the scene (works in both Edit Mode and Play Mode!).
        /// </summary>
        public void LoadLevel(PuzzleGlassLevelData levelData)
        {
            if (levelData == null) return;
            currentLevel = levelData;

            int foundIdx = levels != null ? levels.IndexOf(levelData) : -1;
            if (foundIdx >= 0) currentLevelIndex = foundIdx;

            if (glassContainer == null) glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            if (slotSystem == null) slotSystem = FindFirstObjectByType<SlotSystem>();
            if (cubeDispenser == null) cubeDispenser = GetComponent<CubeDispenser>();

            ClearMonsters();

            // 1. Configure Slot System
            if (slotSystem != null)
            {
                slotSystem.slotCount = Mathf.Clamp(levelData.slotCount, 1, 5);
                slotSystem.slotSpacing = Mathf.Clamp(levelData.slotSpacing, 0.60f, 1.10f);
                slotSystem.RebuildSlots();
            }

            // 2. Spawn Monsters arranged neatly in 1 or 2 rows below the slots
            if (levelData.monsters != null && levelData.monsters.Count > 0)
            {
                int totalM = levelData.monsters.Count;
                int maxPerRow = 4;

                for (int i = 0; i < totalM; i++)
                {
                    var entry = levelData.monsters[i];
                    int row = i / maxPerRow;
                    int indexInRow = i % maxPerRow;
                    int countInThisRow = Mathf.Min(maxPerRow, totalM - row * maxPerRow);

                    float spacingX = countInThisRow <= 2 ? 1.50f : (countInThisRow == 3 ? 1.05f : 0.84f);
                    float startX = -(countInThisRow - 1) * spacingX * 0.5f;
                    float x = startX + indexInRow * spacingX;
                    float y = -1.98f - row * 0.92f;
                    float scale = countInThisRow <= 2 ? 0.72f : (countInThisRow == 3 ? 0.64f : 0.58f);

                    GameObject monObj = new GameObject($"Monster_{entry.color}_{entry.count}_{i + 1}");
                    monObj.transform.SetParent(transform, false);
                    monObj.transform.position = new Vector3(x, y, 0f);
                    monObj.transform.localScale = Vector3.one * scale;

                    MonsterCharacter mon = monObj.AddComponent<MonsterCharacter>();
                    mon.Initialize(entry.color, entry.count, $"{entry.color}_{entry.count}_{i + 1}");
                    activeMonsters.Add(mon);
                }
            }

            // 3. Configure & Rebuild ShapedGlassContainer
            if (glassContainer != null)
            {
                glassContainer.shapePreset = levelData.glassPreset;
                glassContainer.colorTheme = levelData.colorTheme;
                glassContainer.layerPattern = levelData.layerPattern;
                glassContainer.symmetricMode = levelData.symmetricMode;
                glassContainer.curveSubdivisions = levelData.curveSubdivisions;
                glassContainer.widthScale = levelData.widthScale;
                glassContainer.heightScale = levelData.heightScale;

                glassContainer.glassBodyColor = levelData.glassBodyColor;
                glassContainer.glassCenterGlowColor = levelData.glassCenterGlowColor;
                glassContainer.glassOuterRimColor = levelData.glassOuterRimColor;
                glassContainer.glassInnerRimColor = levelData.glassInnerRimColor;
                glassContainer.glassFrontLensColor = levelData.glassFrontLensColor;
                glassContainer.earHandleColor = levelData.earHandleColor;

                glassContainer.glassRimRadius = levelData.glassRimRadius;
                glassContainer.innerRimInset = levelData.innerRimInset;
                glassContainer.glassShellDepth = levelData.glassShellDepth;
                glassContainer.showShoulderHandles = levelData.showShoulderHandles;
                glassContainer.showSpecularReflections = levelData.showSpecularReflections;

                glassContainer.particleSize = levelData.particleSize;
                glassContainer.particlesPerMonsterCount = Mathf.Max(1, levelData.particlesPerMonsterCount);

                if (levelData.contourPoints != null && levelData.contourPoints.Count >= 3)
                {
                    glassContainer.contourPoints = new List<GlassContourPoint>(levelData.contourPoints);
                }
                else
                {
                    glassContainer.ApplyShapePreset(levelData.glassPreset, false);
                }

                glassContainer.RebuildContainer();
            }

            // 4. Register all monster orders with CubeDispenser
            if (cubeDispenser != null)
            {
                cubeDispenser.ClearOrders();
                for (int i = 0; i < activeMonsters.Count; i++)
                {
                    cubeDispenser.RegisterMonsterOrder(activeMonsters[i]);
                }
            }
        }

        public void LoadLevelByIndex(int index)
        {
            if (levels == null || levels.Count == 0) return;
            currentLevelIndex = ((index % levels.Count) + levels.Count) % levels.Count;
            LoadLevel(levels[currentLevelIndex]);
        }

        public void LoadNextLevel()
        {
            if (levels == null || levels.Count == 0) return;
            LoadLevelByIndex(currentLevelIndex + 1);
        }

        public void LoadPreviousLevel()
        {
            if (levels == null || levels.Count == 0) return;
            LoadLevelByIndex(currentLevelIndex - 1);
        }

        /// <summary>
        /// Scenario 1: Exact 1-to-1 Reference Design (Blue 10 on Left + Yellow 20 on Right
        /// underneath the 4 baby-blue slots and wide bulbous sky-blue glass flask).
        /// </summary>
        [ContextMenu("Setup Reference Scenario")]
        public void SetupReferenceScenario()
        {
            ClearMonsters();

            MonsterCharacter blueMonster = SpawnMonster(PuzzleColor.Blue, 10, new Vector3(-0.75f, -1.98f, 0f), "Blue_10");
            MonsterCharacter yellowMonster = SpawnMonster(PuzzleColor.Yellow, 20, new Vector3(0.75f, -1.98f, 0f), "Yellow_20");
            blueMonster.transform.localScale = Vector3.one * 0.72f;
            yellowMonster.transform.localScale = Vector3.one * 0.72f;

            activeMonsters.Add(blueMonster);
            activeMonsters.Add(yellowMonster);

            if (cubeDispenser != null)
            {
                cubeDispenser.RegisterMonsterOrder(blueMonster);
                cubeDispenser.RegisterMonsterOrder(yellowMonster);
            }

            if (glassContainer != null)
            {
                glassContainer.ValidateBottleDimensions();
                glassContainer.RebuildContainer();
            }
        }

        /// <summary>
        /// Blue & Yellow scenario (Legacy pairing).
        /// </summary>
        [ContextMenu("Setup Blue-Yellow Scenario")]
        public void SetupBlueYellowScenario()
        {
            ClearMonsters();

            MonsterCharacter blueMonster = SpawnMonster(PuzzleColor.Blue, 10, new Vector3(-0.72f, -1.95f, 0f), "Blue_10");
            MonsterCharacter yellowMonster = SpawnMonster(PuzzleColor.Yellow, 20, new Vector3(0.72f, -1.95f, 0f), "Yellow_20");

            activeMonsters.Add(blueMonster);
            activeMonsters.Add(yellowMonster);

            if (cubeDispenser != null)
            {
                cubeDispenser.RegisterMonsterOrder(blueMonster);
                cubeDispenser.RegisterMonsterOrder(yellowMonster);
            }

            if (glassContainer != null)
            {
                glassContainer.RebuildContainer();
            }
        }

        /// <summary>
        /// Scenario 2: Two monsters of the SAME color to verify isolated queue management.
        /// </summary>
        [ContextMenu("Setup Same Color Scenario")]
        public void SetupSameColorScenario()
        {
            ClearMonsters();

            MonsterCharacter blue1 = SpawnMonster(PuzzleColor.Blue, 5, new Vector3(-0.72f, -1.95f, 0f), "Blue_Monster_A");
            MonsterCharacter blue2 = SpawnMonster(PuzzleColor.Blue, 8, new Vector3(0.72f, -1.95f, 0f), "Blue_Monster_B");

            activeMonsters.Add(blue1);
            activeMonsters.Add(blue2);

            if (cubeDispenser != null)
            {
                cubeDispenser.RegisterMonsterOrder(blue1);
                cubeDispenser.RegisterMonsterOrder(blue2);
            }

            if (glassContainer != null)
            {
                glassContainer.RebuildContainer();
            }
        }

        /// <summary>
        /// Scenario 3: 4 monsters of all distinct colors placed under the 4 slots.
        /// </summary>
        [ContextMenu("Setup Four Colors Scenario")]
        public void SetupFourColorsScenario()
        {
            ClearMonsters();

            float[] xPositions = new float[] { -1.08f, -0.36f, 0.36f, 1.08f };
            PuzzleColor[] colors = new PuzzleColor[] { PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red, PuzzleColor.Green };
            int[] counts = new int[] { 8, 12, 10, 6 };

            for (int i = 0; i < 4; i++)
            {
                GameObject obj = new GameObject($"Monster_{colors[i]}_{counts[i]}");
                obj.transform.position = new Vector3(xPositions[i], -1.95f, 0f);
                obj.transform.localScale = Vector3.one * 0.58f; // Compact to fit 4 across

                MonsterCharacter monster = obj.AddComponent<MonsterCharacter>();
                monster.Initialize(colors[i], counts[i], $"{colors[i]}_{counts[i]}");

                activeMonsters.Add(monster);
                if (cubeDispenser != null)
                {
                    cubeDispenser.RegisterMonsterOrder(monster);
                }
            }

            if (glassContainer != null)
            {
                glassContainer.RebuildContainer();
            }
        }

        private MonsterCharacter SpawnMonster(PuzzleColor color, int count, Vector3 position, string id)
        {
            GameObject obj = new GameObject($"Monster_{id}");
            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * 0.70f;

            MonsterCharacter monster = obj.AddComponent<MonsterCharacter>();
            monster.Initialize(color, count, id);
            return monster;
        }

        public void FeedOneCube()
        {
            if (cubeDispenser != null)
            {
                cubeDispenser.FeedNextCube();
            }
        }

        public void ToggleAutoFeed()
        {
            if (cubeDispenser != null)
            {
                cubeDispenser.SetAutoFeed(!cubeDispenser.isAutoFeeding);
            }
        }

        // ==================== ON-SCREEN TESTING GUI ====================
        private void OnGUI()
        {
            if (!showDebugGui) return;

            GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = Texture2D.whiteTexture;

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.12f, 0.16f, 0.24f, 0.85f);

            GUILayout.BeginArea(new Rect(16, 16, 260, 290), panelStyle);
            GUILayout.Space(8);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 14;
            titleStyle.normal.textColor = Color.white;
            GUILayout.Label("PuzzleGlass Interactive Test", titleStyle);

            GUILayout.Space(6);

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = 12;
            btnStyle.fontStyle = FontStyle.Bold;

            if (GUILayout.Button("Feed 1 Cube (Space)", btnStyle, GUILayout.Height(32)))
            {
                FeedOneCube();
            }

            string autoText = cubeDispenser != null && cubeDispenser.isAutoFeeding ? "Auto Feeding: ON" : "Auto Feed: OFF";
            GUI.backgroundColor = (cubeDispenser != null && cubeDispenser.isAutoFeeding) ? new Color(0.2f, 0.8f, 0.4f, 1f) : new Color(0.8f, 0.3f, 0.3f, 1f);
            if (GUILayout.Button(autoText, btnStyle, GUILayout.Height(32)))
            {
                ToggleAutoFeed();
            }

            GUI.backgroundColor = new Color(0.25f, 0.5f, 0.9f, 1f);
            GUILayout.Space(6);
            GUILayout.Label("Scenarios:", GUI.skin.label);

            if (GUILayout.Button("1. Reference (Blue 10 + Yellow 20)", btnStyle, GUILayout.Height(28)))
            {
                SetupReferenceScenario();
            }

            if (GUILayout.Button("2. Same Color (2x Blue Monsters)", btnStyle, GUILayout.Height(28)))
            {
                SetupSameColorScenario();
            }

            if (GUILayout.Button("3. 4 Colors (Blue, Ylw, Red, Grn)", btnStyle, GUILayout.Height(28)))
            {
                SetupFourColorsScenario();
            }

            if (GUILayout.Button("Reset Scene", btnStyle, GUILayout.Height(24)))
            {
                SetupReferenceScenario();
            }

            GUILayout.EndArea();
            GUI.backgroundColor = oldBg;
        }
    }
}
