using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class PuzzleGlassGameManager : MonoBehaviour
    {
        public static PuzzleGlassGameManager Instance { get; private set; }

        [Header("Core References")]
        public ShapedGlassContainer glassContainer;
        public SlotSystem slotSystem;
        public CubeDispenser cubeDispenser;
        public Camera mainCamera;

        [Header("Physics & Gameplay Controllers")]
        public MonsterDragController dragController;
        public BallCollectionController collectionController;
        public BallPatternGenerator patternGenerator;
        public BallContainerManager containerPhysics;
        public SandSimulationEngine sandEngine;
        public SandMonsterFlowBridge flowBridge;

        [Header("Level System")]
        public List<PuzzleGlassLevelData> levels = new List<PuzzleGlassLevelData>();
        public int currentLevelIndex = 0;
        [Tooltip("Cam şekli bu kadar levelde bir değişir; her grubun şeklini ilk level belirler. 1 = her level kendi şekli.")]
        [Min(1)]
        public int levelsPerGlassShape = 10;
        public float nextLevelDelay = 1.2f;
        public PuzzleGlassLevelData currentLevel;

        [Header("Active Monsters & Gameplay State")]
        public List<MonsterCharacter> activeMonsters = new List<MonsterCharacter>();
        public bool isLevelCompleted = false;
        public ShapePatternType activePatternType = ShapePatternType.GumballMix;

        [Header("Testing & Debug GUI")]
        public bool showDebugGui = true;
        private bool _showPatternSelector = false;

        private void Awake()
        {
            Instance = this;
            SetupCamera();
            EnsureGameplayControllers();
        }

        private void OnEnable()
        {
            Instance = this;
            EnsureGameplayControllers();
            SyncCurrentMonstersWithPattern();
        }

        private void Start()
        {
            EnsureGameplayControllers();

            if (Application.isPlaying)
            {
                RebuildContainerPhysics();
                return;
            }

            SyncCurrentMonstersWithPattern();
        }

        public void RebuildContainerPhysics()
        {
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (glassContainer != null)
            {
                glassContainer.CleanOrphanedChildren();
                glassContainer.RebuildContainer();
            }
        }

        private void RebuildSandVisual()
        {
            RebuildContainerPhysics();
        }

        private void PlaySandFillIfPlaying()
        {
        }

        private PuzzleGlassLevelData GetGlassShapeSource(PuzzleGlassLevelData levelData)
        {
            int index = levels != null ? levels.IndexOf(levelData) : -1;
            if (index < 0 || levelsPerGlassShape <= 1) return levelData;

            var source = levels[index / levelsPerGlassShape * levelsPerGlassShape];
            return source != null ? source : levelData;
        }

        public void SyncCurrentMonstersWithPattern()
        {
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (glassContainer != null)
            {
                PixelArtPattern pat = (glassContainer.pixelArtPreset == PixelArtPreset.Custom && glassContainer.customPattern != null)
                    ? glassContainer.customPattern
                    : PixelArtLibrary.GetPreset(glassContainer.pixelArtPreset);

                if (pat == null) pat = PixelArtLibrary.GetPreset(PixelArtPreset.Frog);
                SynchronizeMonstersForPattern(pat);
            }
            else
            {
                SynchronizeMonstersForPattern(PixelArtLibrary.GetPreset(PixelArtPreset.Frog));
            }
        }

        public void SynchronizeMonstersForPattern(PixelArtPattern pattern)
        {
            if (pattern == null) return;
            ClearMonsters();

            Dictionary<PuzzleColor, int> colorCounts = new Dictionary<PuzzleColor, int>();
            var grid = pattern.GetColorGrid();
            for (int y = 0; y < pattern.height; y++)
            {
                for (int x = 0; x < pattern.width; x++)
                {
                    var col = grid[x, y];
                    if (col.HasValue)
                    {
                        if (!colorCounts.ContainsKey(col.Value)) colorCounts[col.Value] = 0;
                        colorCounts[col.Value]++;
                    }
                }
            }

            List<LevelMonsterEntry> entries = new List<LevelMonsterEntry>();
            foreach (var kvp in colorCounts)
            {
                if (kvp.Value > 0)
                {
                    entries.Add(new LevelMonsterEntry(kvp.Key, kvp.Value));
                }
            }

            if (entries.Count == 0)
            {
                entries.Add(new LevelMonsterEntry(PuzzleColor.Green, 12));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Pink, 8));
            }

            SpawnMonsterStagingArea(entries);
        }

        public void SynchronizeMonstersFromSpawnedBalls(List<BallPhysicsController> balls)
        {
            ClearMonsters();

            if (balls == null || balls.Count == 0)
            {
                List<LevelMonsterEntry> fallback = new List<LevelMonsterEntry>()
                {
                    new LevelMonsterEntry(PuzzleColor.Yellow, 12),
                    new LevelMonsterEntry(PuzzleColor.Green, 12),
                    new LevelMonsterEntry(PuzzleColor.Blue, 12),
                    new LevelMonsterEntry(PuzzleColor.Red, 12)
                };
                SpawnMonsterStagingArea(fallback);
                return;
            }

            Dictionary<PuzzleColor, int> colorCounts = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i] == null) continue;
                PuzzleColor c = balls[i].colorType;
                if (!colorCounts.ContainsKey(c)) colorCounts[c] = 0;
                colorCounts[c]++;
            }

            List<LevelMonsterEntry> entries = new List<LevelMonsterEntry>();
            foreach (var kvp in colorCounts)
            {
                if (kvp.Value > 0)
                {
                    entries.Add(new LevelMonsterEntry(kvp.Key, kvp.Value));
                }
            }

            if (entries.Count == 0)
            {
                entries.Add(new LevelMonsterEntry(PuzzleColor.Yellow, 12));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Green, 12));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Blue, 12));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Red, 12));
            }

            SpawnMonsterStagingArea(entries);
        }

        public void EnsureGameplayControllers()
        {
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (slotSystem == null)
            {
                slotSystem = FindFirstObjectByType<SlotSystem>();
            }

            if (cubeDispenser == null)
            {
                cubeDispenser = GetComponent<CubeDispenser>();
            }

            if (dragController == null)
            {
                dragController = GetComponent<MonsterDragController>().OrNull() ?? gameObject.AddComponent<MonsterDragController>();
            }

            if (collectionController == null)
            {
                collectionController = GetComponent<BallCollectionController>().OrNull() ?? gameObject.AddComponent<BallCollectionController>();
            }

            if (patternGenerator == null)
            {
                patternGenerator = GetComponent<BallPatternGenerator>().OrNull() ?? gameObject.AddComponent<BallPatternGenerator>();
            }

            if (glassContainer != null)
            {
                containerPhysics = glassContainer.GetComponent<BallContainerManager>().OrNull() ?? glassContainer.gameObject.AddComponent<BallContainerManager>();
                containerPhysics.glassContainer = glassContainer;

                if (sandEngine == null)
                {
                    sandEngine = glassContainer.GetComponent<SandSimulationEngine>().OrNull() ?? glassContainer.gameObject.AddComponent<SandSimulationEngine>();
                    sandEngine.glassContainer = glassContainer;
                }

                if (flowBridge == null)
                {
                    flowBridge = glassContainer.GetComponent<SandMonsterFlowBridge>().OrNull() ?? glassContainer.gameObject.AddComponent<SandMonsterFlowBridge>();
                    flowBridge.sandEngine = sandEngine;
                    flowBridge.glassContainer = glassContainer;
                }
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
        }

        private void HandleKeyboardInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetupPixelArtPatternScenario(PixelArtPreset.Frog);
                if (kb.digit2Key.wasPressedThisFrame) SetupPixelArtPatternScenario(PixelArtPreset.Heart);
                if (kb.digit3Key.wasPressedThisFrame) SetupPixelArtPatternScenario(PixelArtPreset.Mushroom);
                if (kb.digit4Key.wasPressedThisFrame) SetupPixelArtPatternScenario(PixelArtPreset.Bee);
                if (kb.digit5Key.wasPressedThisFrame) SetupPixelArtPatternScenario(PixelArtPreset.OctopusSkull);
                if (kb.rKey.wasPressedThisFrame) RestartCurrentPattern();
                if (kb.nKey.wasPressedThisFrame) LoadNextLevel();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetupPixelArtPatternScenario(PixelArtPreset.Frog);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetupPixelArtPatternScenario(PixelArtPreset.Heart);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetupPixelArtPatternScenario(PixelArtPreset.Mushroom);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetupPixelArtPatternScenario(PixelArtPreset.Bee);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetupPixelArtPatternScenario(PixelArtPreset.OctopusSkull);
            if (Input.GetKeyDown(KeyCode.R)) RestartCurrentPattern();
            if (Input.GetKeyDown(KeyCode.N)) LoadNextLevel();
#endif
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
            if (collectionController != null)
            {
                collectionController.ClearAll();
            }
            if (cubeDispenser != null)
            {
                cubeDispenser.ClearOrders();
            }
        }

        /// <summary>
        /// Loads any PuzzleGlassLevelData asset into the scene.
        /// </summary>
        public void LoadLevel(PuzzleGlassLevelData levelData)
        {
            if (levelData == null) return;
            currentLevel = levelData;
            isLevelCompleted = false;

            int foundIdx = levels != null ? levels.IndexOf(levelData) : -1;
            if (foundIdx >= 0) currentLevelIndex = foundIdx;

            EnsureGameplayControllers();
            ClearMonsters();

            // 1. Configure Slot System
            if (slotSystem != null)
            {
                slotSystem.slotCount = Mathf.Clamp(levelData.slotCount, 1, 5);
                slotSystem.slotSpacing = Mathf.Clamp(levelData.slotSpacing, 0.60f, 1.10f);
                slotSystem.RebuildSlots();
            }

            // 2. Spawn Monsters arranged neatly in staging area below slots
            SpawnMonsterStagingArea(levelData.monsters);

            // 3. Configure & Rebuild ShapedGlassContainer
            if (glassContainer != null)
            {
                PuzzleGlassLevelData shapeSource = GetGlassShapeSource(levelData);

                glassContainer.shapePreset = shapeSource.glassPreset;
                glassContainer.colorTheme = levelData.colorTheme;
                glassContainer.layerPattern = levelData.layerPattern;
                glassContainer.symmetricMode = shapeSource.symmetricMode;
                glassContainer.curveSubdivisions = shapeSource.curveSubdivisions;
                glassContainer.widthScale = shapeSource.widthScale;
                glassContainer.heightScale = shapeSource.heightScale;

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
                glassContainer.particleMode = levelData.particleMode;
                glassContainer.pixelArtPreset = levelData.pixelArtPreset;
                glassContainer.customPattern = levelData.customPattern;
                glassContainer.freezePatternInPlace = false; // Allow balls to tumble naturally
                glassContainer.gridCellSpacing = levelData.gridCellSpacing;
                glassContainer.patternVerticalOffset = 0f; // Kum görseli hazne ortasına hizalanır
                if (sandEngine != null) sandEngine.lockedSectionRatio = levelData.lockedSectionRatio;

                if (shapeSource.contourPoints != null && shapeSource.contourPoints.Count >= 3)
                {
                    glassContainer.contourPoints = new List<GlassContourPoint>(shapeSource.contourPoints);
                }
                else
                {
                    glassContainer.ApplyShapePreset(shapeSource.glassPreset, false);
                }

                glassContainer.RebuildContainer();

                // 4. Rebuild Airtight Physics Colliders around container
                if (containerPhysics != null)
                {
                    containerPhysics.RebuildColliders();
                }
            }

            // 5. Yeni görsel üst açıklıktan dökülerek oluşur
            activePatternType = levelData.patternShape;
            PlaySandFillIfPlaying();
        }

        private void SpawnMonsterStagingArea(List<LevelMonsterEntry> monsterEntries)
        {
            if (monsterEntries == null || monsterEntries.Count == 0) return;

            int totalM = monsterEntries.Count;
            int maxPerRow = 4;

            for (int i = 0; i < totalM; i++)
            {
                var entry = monsterEntries[i];
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

        public void RestartCurrentPattern()
        {
            if (currentLevel != null)
            {
                LoadLevel(currentLevel);
            }
            else
            {
                SetupShapePatternScenario(activePatternType);
            }
        }

        // ==================== SCENARIO SETUPS ====================

        /// <summary>
        /// Spawns a Pixel Art notebook silhouette (Frog, Heart, Mushroom, Bee, Octopus, Saturn, Penguin, Butterfly, Tulip)
        /// with real physical 3D balls and matching cute monsters at the bottom staging tray!
        /// </summary>
        [ContextMenu("Setup Pixel Art Pattern Scenario")]
        public void SetupPixelArtPatternScenario(PixelArtPreset preset)
        {
            if (glassContainer != null)
            {
                glassContainer.pixelArtPreset = preset;
                glassContainer.particleMode = ParticleArrangementMode.PixelArtVisual;
                glassContainer.freezePatternInPlace = false;
            }

            ShapePatternType shapeType = ShapePatternType.Frog;
            switch (preset)
            {
                case PixelArtPreset.Frog: shapeType = ShapePatternType.Frog; break;
                case PixelArtPreset.Heart: shapeType = ShapePatternType.Heart; break;
                case PixelArtPreset.Mushroom: shapeType = ShapePatternType.Mushroom; break;
                case PixelArtPreset.Bee: shapeType = ShapePatternType.Bee; break;
                case PixelArtPreset.OctopusSkull: shapeType = ShapePatternType.Octopus; break;
                case PixelArtPreset.Penguin: shapeType = ShapePatternType.Penguin; break;
                case PixelArtPreset.Butterfly: shapeType = ShapePatternType.Butterfly; break;
                case PixelArtPreset.Tulip: shapeType = ShapePatternType.Tulip; break;
                case PixelArtPreset.Saturn: shapeType = ShapePatternType.Saturn; break;
            }

            EnsureGameplayControllers();
            isLevelCompleted = false;
            activePatternType = shapeType;
            RebuildSandVisual();
        }

        public void SetupCustomPatternScenario(PixelArtPattern pattern)
        {
            if (pattern == null) return;
            EnsureGameplayControllers();
            isLevelCompleted = false;

            if (glassContainer != null)
            {
                glassContainer.customPattern = pattern;
                glassContainer.pixelArtPreset = PixelArtPreset.Custom;
                glassContainer.particleMode = ParticleArrangementMode.PixelArtVisual;
                glassContainer.freezePatternInPlace = false;
                RebuildSandVisual();
            }
        }

        /// <summary>
        /// Shows any shape pattern (silhouettes, fruits, geometric shapes, bands, mosaic) as sand.
        /// </summary>
        public void SetupShapePatternScenario(ShapePatternType patternType)
        {
            EnsureGameplayControllers();
            activePatternType = patternType;

            if (glassContainer != null)
            {
                glassContainer.CleanOrphanedChildren();
                glassContainer.RebuildContainer();
            }
        }

        [ContextMenu("Setup Reference Scenario")]
        public void SetupReferenceScenario()
        {
            SetupPixelArtPatternScenario(PixelArtPreset.Frog);
        }

        [ContextMenu("Setup Same Color Scenario")]
        public void SetupSameColorScenario()
        {
            SetupShapePatternScenario(ShapePatternType.Heart);
        }

        [ContextMenu("Setup Four Colors Scenario")]
        public void SetupFourColorsScenario()
        {
            SetupShapePatternScenario(ShapePatternType.Diamond);
        }

        public void CheckLevelCompletionState()
        {
            if (activeMonsters == null || activeMonsters.Count == 0) return;

            bool allFinished = true;
            for (int i = 0; i < activeMonsters.Count; i++)
            {
                var m = activeMonsters[i];
                if (m != null && m.remainingCount > 0)
                {
                    allFinished = false;
                    break;
                }
            }

            if (allFinished && !isLevelCompleted)
            {
                isLevelCompleted = true;
                OnLevelCleared();
            }
        }

        private void OnLevelCleared()
        {
            Debug.Log("<color=#00FF88>★ LEVEL CLEARED! ALL MONSTER ORDERS FULFILLED! ★</color>");

            if (Application.isPlaying)
            {
                Invoke(nameof(LoadNextLevel), nextLevelDelay);
            }
        }

        // ==================== ON-SCREEN TESTING HUD ====================
        private void OnGUI()
        {
            if (!showDebugGui) return;

            GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = Texture2D.whiteTexture;

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.90f);

            int panelW = 270;
            int panelH = _showPatternSelector ? 430 : 250;
            GUILayout.BeginArea(new Rect(14, 14, panelW, panelH), panelStyle);
            GUILayout.Space(6);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };
            titleStyle.normal.textColor = Color.white;
            GUILayout.Label("PuzzleGlass 3D Physics Puzzle", titleStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            subStyle.normal.textColor = new Color(0.7f, 0.85f, 1f, 1f);
            GUILayout.Label($"Active Pattern: {activePatternType}", subStyle);

            int grainCount = sandEngine != null ? sandEngine.sandAmount : 0;
            int ballCount = BallObjectPool.Instance != null ? BallObjectPool.Instance.ActiveBalls.Count : 0;
            GUILayout.Label($"Balls in Jar: {ballCount} | Drag to Slots!", subStyle);

            if (isLevelCompleted)
            {
                GUIStyle winStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13
                };
                winStyle.normal.textColor = new Color(0.2f, 1f, 0.4f, 1f);
                GUILayout.Label("★ LEVEL CLEARED! ★", winStyle);
            }

            GUILayout.Space(4);
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f, 1f);
            if (GUILayout.Button(_showPatternSelector ? "▲ Hide Patterns" : "▼ Choose Pattern (" + activePatternType + ")", btnStyle, GUILayout.Height(26)))
            {
                _showPatternSelector = !_showPatternSelector;
            }

            if (_showPatternSelector)
            {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.1f, 0.85f, 0.4f, 1f);
                if (GUILayout.Button("★ 4 Quadrants (Image 1)", btnStyle, GUILayout.Height(24))) SetupShapePatternScenario(ShapePatternType.FourQuadrants);
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.2f, 1f);
                if (GUILayout.Button("★ Gumballs (Image 2)", btnStyle, GUILayout.Height(24))) SetupShapePatternScenario(ShapePatternType.GumballMix);
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f, 1f);
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🌈 Rainbow", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Rainbow);
                if (GUILayout.Button("🐱 Cat", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Cat);
                if (GUILayout.Button("⭐ Star", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Star);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Frog", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Frog);
                if (GUILayout.Button("Heart", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Heart);
                if (GUILayout.Button("Shroom", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Mushroom);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Bee", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Bee);
                if (GUILayout.Button("Octopus", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Octopus);
                if (GUILayout.Button("Saturn", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Saturn);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Penguin", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Penguin);
                if (GUILayout.Button("Butterfly", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Butterfly);
                if (GUILayout.Button("Tulip", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Tulip);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Berry", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Strawberry);
                if (GUILayout.Button("Cherry", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Cherry);
                if (GUILayout.Button("Orange", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.OrangeFruit);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Diamond", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Diamond);
                if (GUILayout.Button("Pyramid", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.Pyramid);
                if (GUILayout.Button("Mosaic", btnStyle, GUILayout.Height(22))) SetupShapePatternScenario(ShapePatternType.RandomMosaic);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.4f, 1f);
            if (GUILayout.Button("Restart Pattern (R)", btnStyle, GUILayout.Height(26)))
            {
                RestartCurrentPattern();
            }

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.6f, 1f);
            if (GUILayout.Button("◄ Prev", btnStyle, GUILayout.Height(24))) LoadPreviousLevel();
            if (GUILayout.Button("Next (N) ►", btnStyle, GUILayout.Height(24))) LoadNextLevel();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            GUI.backgroundColor = oldBg;
        }
    }
}
