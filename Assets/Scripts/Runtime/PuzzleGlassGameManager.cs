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
                    Destroy(activeMonsters[i].gameObject);
                }
            }
            activeMonsters.Clear();
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
        /// Scenario 1: Reference image setup (Purple Monster 10 + Pink Monster 20 matching the track cubes).
        /// </summary>
        [ContextMenu("Setup Reference Scenario")]
        public void SetupReferenceScenario()
        {
            ClearMonsters();

            // Purple Monster (10) under Slots 1 & 2, Pink Monster (20) under Slots 3 & 4
            MonsterCharacter purpleMonster = SpawnMonster(PuzzleColor.Purple, 10, new Vector3(-0.72f, -1.95f, 0f), "Purple_10");
            MonsterCharacter pinkMonster = SpawnMonster(PuzzleColor.Pink, 20, new Vector3(0.72f, -1.95f, 0f), "Pink_20");

            activeMonsters.Add(purpleMonster);
            activeMonsters.Add(pinkMonster);

            if (cubeDispenser != null)
            {
                cubeDispenser.RegisterMonsterOrder(purpleMonster);
                cubeDispenser.RegisterMonsterOrder(pinkMonster);
            }

            if (glassContainer != null)
            {
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
