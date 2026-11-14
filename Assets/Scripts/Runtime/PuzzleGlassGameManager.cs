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
        public StagingGridBoard stagingGridBoard;
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
        [Tooltip("Açık olduğunda Play moduna geçerken Level asset'i sahnedeki özel cam şeklini ezmez.")]
        public bool preserveSceneGlassInPlayMode = false;
        [Tooltip("Açık olduğunda Play moduna geçerken Level asset'i sahnedeki tepsi (StagingGridBoard) boyutunu ezmez.")]
        public bool preserveSceneTrayInPlayMode = false;
        public float nextLevelDelay = 1.2f;
        public PuzzleGlassLevelData currentLevel;

        [Header("Active Monsters & Gameplay State")]
        public List<MonsterCharacter> activeMonsters = new List<MonsterCharacter>();
        public bool isLevelCompleted = false;
        public bool isLevelFailed = false;
        public ShapePatternType activePatternType = ShapePatternType.GumballMix;

        [Header("Combo / Fever Mode & Shake Mechanic")]
        public int comboStreak = 0;
        public float feverRemainingTime = 0f;
        public bool IsFeverActive => feverRemainingTime > 0f;
        public int maxShakesPerLevel = 3;
        public int remainingShakes = 3;

        [Header("Testing & Debug GUI")]
        public bool showDebugGui = false;
        private bool _showPatternSelector = false;
        private float _deadlockTimer = 0f;

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
            if (glassContainer != null && (BallObjectPool.Instance == null || BallObjectPool.Instance.ActiveBalls.Count == 0))
            {
                RebuildContainerPhysics();
            }
        }

        private void Start()
        {
            EnsureGameplayControllers();
            if (Application.isPlaying)
            {
                if (currentLevel != null)
                {
                    LoadLevel(currentLevel);
                    return;
                }
                if (levels != null && levels.Count > 0 && levels[0] != null)
                {
                    LoadLevelByIndex(currentLevelIndex);
                    return;
                }
                GenerateAndLoadProceduralLevel(1, LevelDifficulty.Easy);
                return;
            }
            RebuildContainerPhysics();
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
                    new LevelMonsterEntry(PuzzleColor.Blue, 3),
                    new LevelMonsterEntry(PuzzleColor.Yellow, 3),
                    new LevelMonsterEntry(PuzzleColor.Red, 3),
                    new LevelMonsterEntry(PuzzleColor.Blue, 3),
                    new LevelMonsterEntry(PuzzleColor.Yellow, 3),
                    new LevelMonsterEntry(PuzzleColor.Red, 3),
                    new LevelMonsterEntry(PuzzleColor.Blue, 3),
                    new LevelMonsterEntry(PuzzleColor.Yellow, 3),
                    new LevelMonsterEntry(PuzzleColor.Red, 3)
                };
                SpawnMonsterStagingArea(fallback);
                return;
            }

            List<PuzzleColor> colorOrder = new List<PuzzleColor>();
            Dictionary<PuzzleColor, int> colorCounts = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i] == null) continue;
                PuzzleColor c = balls[i].colorType;
                if (!colorCounts.ContainsKey(c))
                {
                    colorCounts[c] = 0;
                    colorOrder.Add(c);
                }
                colorCounts[c]++;
            }

            // Build variable-socket Carrier Boxes matching the spawned ball counts
            int boxCap = (currentLevel != null) ? Mathf.Clamp(currentLevel.ballsPerBox, 1, MonsterCharacter.MaxVisualSockets) : 3;
            List<LevelMonsterEntry> entries = new List<LevelMonsterEntry>();
            bool addedAny = true;
            while (addedAny)
            {
                addedAny = false;
                for (int i = 0; i < colorOrder.Count; i++)
                {
                    PuzzleColor c = colorOrder[i];
                    if (colorCounts[c] > 0)
                    {
                        int take = Mathf.Min(boxCap, colorCounts[c]);
                        entries.Add(new LevelMonsterEntry(c, take));
                        colorCounts[c] -= take;
                        addedAny = true;
                    }
                }
            }

            if (entries.Count == 0)
            {
                entries.Add(new LevelMonsterEntry(PuzzleColor.Blue, boxCap));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Yellow, boxCap));
                entries.Add(new LevelMonsterEntry(PuzzleColor.Red, boxCap));
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
            if (slotSystem != null && slotSystem.gameObject.activeSelf)
            {
                slotSystem.gameObject.SetActive(false);
            }

            if (stagingGridBoard == null)
            {
                stagingGridBoard = FindFirstObjectByType<StagingGridBoard>();
                if (stagingGridBoard == null)
                {
                    GameObject gridObj = new GameObject("StagingGridBoard");
                    stagingGridBoard = gridObj.AddComponent<StagingGridBoard>();
                }
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

            if (FindFirstObjectByType<PuzzleGlassBackdrop>() == null)
            {
                gameObject.AddComponent<PuzzleGlassBackdrop>();
            }
            PuzzleSoundManager.GetOrCreate();
            if (FindFirstObjectByType<PuzzleGlassHUD>() == null)
            {
                gameObject.AddComponent<PuzzleGlassHUD>();
            }

            if (glassContainer != null)
            {
                if (!glassContainer.enabled)
                {
                    glassContainer.enabled = true;
                }

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

            var backdrop = PuzzleGlassBackdrop.Instance ?? FindFirstObjectByType<PuzzleGlassBackdrop>();
            if (backdrop != null)
            {
                backdrop.SyncWithCamera(force: true);
            }
        }

        private void Update()
        {
            HandleKeyboardInput();
            if (Application.isPlaying)
            {
                if (feverRemainingTime > 0f)
                {
                    feverRemainingTime -= Time.deltaTime;
                    if (feverRemainingTime <= 0f)
                    {
                        feverRemainingTime = 0f;
                        if (glassContainer != null)
                        {
                            glassContainer.SetFeverVisualGlow(false);
                        }
                    }
                }
                CheckDeadlockState();
            }
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
                if (kb.sKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame) TryShakeContainer();
                if (kb.rKey.wasPressedThisFrame) RestartCurrentPattern();
                if (kb.nKey.wasPressedThisFrame) LoadNextLevel();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetupPixelArtPatternScenario(PixelArtPreset.Frog);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetupPixelArtPatternScenario(PixelArtPreset.Heart);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetupPixelArtPatternScenario(PixelArtPreset.Mushroom);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetupPixelArtPatternScenario(PixelArtPreset.Bee);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetupPixelArtPatternScenario(PixelArtPreset.OctopusSkull);
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.T)) TryShakeContainer();
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
                    activeMonsters[i].gameObject.SetActive(false);
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
                    strayMonsters[i].gameObject.SetActive(false);
                    if (Application.isPlaying)
                        Destroy(strayMonsters[i].gameObject);
                    else
                        DestroyImmediate(strayMonsters[i].gameObject);
                }
            }

            CleanupLegacyStraySceneObjects();

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

        private static readonly string[] LegacyStrayObjectPrefixes =
        {
            "BallLinkConnector",
            "ChainRod",
            "ChainBead_",
            "LockMedallion",
            "IceShell",
            "IceOuterSphere",
            "IceFacetCube",
            "OverlayBadge",
            "PullPinHandleKnob",
            "PullPinLabel",
            "ColorGateLockBadge",
            "LockBadgeText"
        };

        /// <summary>
        /// Removes leftover experimental scene objects (e.g., unreferenced chain connectors or badges)
        /// that may remain in an unsaved scene hierarchy without valid materials.
        /// </summary>
        private void CleanupLegacyStraySceneObjects()
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform tr = allTransforms[i];
                if (tr == null) continue;

                string objName = tr.gameObject.name;
                bool isLegacyStray = false;
                for (int p = 0; p < LegacyStrayObjectPrefixes.Length; p++)
                {
                    if (objName.StartsWith(LegacyStrayObjectPrefixes[p], StringComparison.Ordinal))
                    {
                        isLegacyStray = true;
                        break;
                    }
                }

                if (!isLegacyStray) continue;

                if (Application.isPlaying)
                    Destroy(tr.gameObject);
                else
                    DestroyImmediate(tr.gameObject);
            }
        }

        /// <summary>
        /// Loads any PuzzleGlassLevelData asset into the scene.
        /// </summary>
        public void LoadLevel(PuzzleGlassLevelData levelData)
        {
            if (levelData == null) return;
            levelData.EnsureCarrierBoxPuzzleFormat();

            currentLevel = levelData;
            isLevelCompleted = false;
            isLevelFailed = false;
            _deadlockTimer = 0f;
            comboStreak = 0;
            feverRemainingTime = 0f;
            remainingShakes = Mathf.Max(1, maxShakesPerLevel);

            int foundIdx = levels != null ? levels.IndexOf(levelData) : -1;
            if (foundIdx >= 0) currentLevelIndex = foundIdx;

            EnsureGameplayControllers();
            ClearMonsters();

            // 1. Upper 4 square slots are disabled — direct in-grid collection is used
            if (slotSystem != null)
            {
                slotSystem.gameObject.SetActive(false);
            }

            // 2. Configure & Rebuild ShapedGlassContainer FIRST so StagingGridBoard aligns snugly right underneath it!
            activePatternType = ShapePatternType.GumballMix;

            if (glassContainer != null)
            {
                if (!preserveSceneGlassInPlayMode)
                {
                    glassContainer.colorTheme = levelData.colorTheme;
                    glassContainer.layerPattern = levelData.layerPattern;

                    glassContainer.glassBodyColor = levelData.glassBodyColor;
                    glassContainer.glassCenterGlowColor = levelData.glassCenterGlowColor;
                    glassContainer.glassOuterRimColor = levelData.glassOuterRimColor;
                    glassContainer.glassInnerRimColor = levelData.glassInnerRimColor;
                    glassContainer.glassFrontLensColor = levelData.glassFrontLensColor;
                    glassContainer.earHandleColor = levelData.earHandleColor;

                    glassContainer.glassRimRadius = levelData.glassRimRadius;
                    glassContainer.innerRimInset = levelData.innerRimInset;
                    glassContainer.glassShellDepth = levelData.glassShellDepth;
                    glassContainer.showSpecularReflections = levelData.showSpecularReflections;

                    glassContainer.boxCornerRadius = levelData.boxCornerRadius > 0.01f ? levelData.boxCornerRadius : 0.22f;
                    glassContainer.boxFrameThickness = levelData.boxFrameThickness > 0.01f ? levelData.boxFrameThickness : 0.115f;
                    if (levelData.boxFrameColor.a > 0.01f) glassContainer.boxFrameColor = levelData.boxFrameColor;
                    if (levelData.boxFrameHighlightColor.a > 0.01f) glassContainer.boxFrameHighlightColor = levelData.boxFrameHighlightColor;
                    if (levelData.boxFrameEdgeShadeColor.a > 0.01f) glassContainer.boxFrameEdgeShadeColor = levelData.boxFrameEdgeShadeColor;
                    if (levelData.dropShadowColor.a > 0.01f) glassContainer.dropShadowColor = levelData.dropShadowColor;

                    if (levelData.useCustomGlassShape)
                    {
                        glassContainer.shapePreset = levelData.glassPreset;
                        glassContainer.symmetricMode = levelData.symmetricMode;
                        glassContainer.curveSubdivisions = Mathf.Clamp(levelData.curveSubdivisions, 4, 24);
                        glassContainer.widthScale = Mathf.Clamp(levelData.widthScale, 0.5f, 1.8f);
                        glassContainer.heightScale = Mathf.Clamp(levelData.heightScale, 0.6f, 1.6f);
                        glassContainer.showShoulderHandles = levelData.showShoulderHandles;
                    }
                    else
                    {
                        glassContainer.shapePreset = GlassShapePreset.RectangleBox;
                        glassContainer.symmetricMode = true;
                        glassContainer.curveSubdivisions = 14;
                        glassContainer.widthScale = 1.0f;
                        glassContainer.heightScale = 1.0f;
                        glassContainer.showShoulderHandles = false;
                    }
                }

                glassContainer.particleSize = 0.265f;
                glassContainer.particlesPerMonsterCount = 1;
                glassContainer.particleMode = ParticleArrangementMode.PhysicsBalls;
                glassContainer.pixelArtPreset = levelData.pixelArtPreset;
                glassContainer.customPattern = levelData.customPattern;
                glassContainer.freezePatternInPlace = false;
                glassContainer.gridCellSpacing = 0.265f;
                glassContainer.patternVerticalOffset = 0f;
                if (sandEngine != null) sandEngine.lockedSectionRatio = levelData.lockedSectionRatio;

                // Copy per-level compartment dividers & color zones
                glassContainer.compartmentPreset = levelData.compartmentPreset;
                glassContainer.dividers = new List<GlassDividerData>();
                if (levelData.dividers != null)
                {
                    for (int i = 0; i < levelData.dividers.Count; i++)
                    {
                        if (levelData.dividers[i] != null)
                            glassContainer.dividers.Add(levelData.dividers[i].Clone());
                    }
                }

                glassContainer.compartmentZones = new List<CompartmentColorZone>();
                if (levelData.compartmentZones != null)
                {
                    for (int i = 0; i < levelData.compartmentZones.Count; i++)
                    {
                        if (levelData.compartmentZones[i] != null)
                            glassContainer.compartmentZones.Add(levelData.compartmentZones[i].Clone());
                    }
                }

                if (levelData.compartmentPreset != GlassCompartmentPreset.Custom &&
                    levelData.compartmentPreset != GlassCompartmentPreset.None_OrderedPool &&
                    glassContainer.dividers.Count == 0)
                {
                    ShapedGlassContainer.BuildCompartmentPresetLists(
                        levelData.compartmentPreset,
                        glassContainer.dividers,
                        glassContainer.compartmentZones);
                }

                if (!preserveSceneGlassInPlayMode)
                {
                    if (levelData.useCustomGlassShape && levelData.contourPoints != null && levelData.contourPoints.Count >= 3)
                    {
                        glassContainer.contourPoints = new List<GlassContourPoint>(levelData.contourPoints);
                        glassContainer.SyncBoundsFromContour();
                    }
                    else
                    {
                        glassContainer.ApplyShapePreset(glassContainer.shapePreset, false);
                    }
                }
                glassContainer.RebuildContainer();

                // 3. Rebuild Airtight Physics Colliders around container
                if (containerPhysics != null)
                {
                    containerPhysics.RebuildColliders();
                }
            }

            // 4. Configure framed bottom StagingGridBoard right beneath ShapedGlassContainer & spawn blocks inside it
            if (stagingGridBoard != null)
            {
                if (!preserveSceneTrayInPlayMode)
                {
                    if (levelData.useCustomTraySettings)
                    {
                        stagingGridBoard.matchGlassWidth = levelData.trayMatchGlassWidth;
                        stagingGridBoard.maxBoardWidth = Mathf.Max(1.0f, levelData.trayMaxBoardWidth);
                        stagingGridBoard.maxBoardHeight = Mathf.Max(1.0f, levelData.trayMaxBoardHeight);
                        stagingGridBoard.framePadding = Mathf.Clamp(levelData.trayFramePadding, 0.03f, 0.30f);
                        stagingGridBoard.innerMargin = Mathf.Clamp(levelData.trayInnerMargin, 0.01f, 0.25f);
                        stagingGridBoard.frameCornerRadius = Mathf.Clamp(levelData.trayFrameCornerRadius, 0.04f, 0.40f);
                        stagingGridBoard.gapBelowGlass = Mathf.Clamp(levelData.trayGapBelowGlass, 0.02f, 0.80f);
                        stagingGridBoard.useManualCellSize = levelData.trayUseManualCellSize;
                        stagingGridBoard.manualCellSize = Mathf.Clamp(levelData.trayManualCellSize, 0.30f, 0.82f);
                    }
                    stagingGridBoard.Configure(levelData.stagingGridCols, levelData.stagingGridRows, glassContainer);
                }
                else
                {
                    stagingGridBoard.Configure(stagingGridBoard.cols, stagingGridBoard.rows, glassContainer);
                }
            }
            SpawnMonsterStagingArea(levelData.monsters);

            PlaySandFillIfPlaying();
        }

        private void SpawnMonsterStagingArea(List<LevelMonsterEntry> monsterEntries)
        {
            if (monsterEntries == null || monsterEntries.Count == 0) return;

            if (stagingGridBoard == null)
            {
                stagingGridBoard = FindFirstObjectByType<StagingGridBoard>();
                if (stagingGridBoard == null)
                {
                    GameObject gridObj = new GameObject("StagingGridBoard");
                    stagingGridBoard = gridObj.AddComponent<StagingGridBoard>();
                }
                int gCols = (currentLevel != null) ? currentLevel.stagingGridCols : 5;
                int gRows = (currentLevel != null) ? currentLevel.stagingGridRows : 4;
                stagingGridBoard.Configure(gCols, gRows);
            }

            float gridCellScale = stagingGridBoard.cellSize;

            for (int i = 0; i < monsterEntries.Count; i++)
            {
                var entry = monsterEntries[i];
                CarrierBlockShape shape = entry.blockShape;

                Vector2Int anchor;
                if (entry.hasGridAnchor && IsAnchorFreeForShape(stagingGridBoard, entry.gridAnchor, shape, activeMonsters))
                {
                    anchor = entry.gridAnchor;
                }
                else
                {
                    if (!stagingGridBoard.TryFindEmptyPlacement(shape, activeMonsters, out anchor))
                    {
                        shape = CarrierBlockShape.Rect1x1;
                        if (!stagingGridBoard.TryFindEmptyPlacement(shape, activeMonsters, out anchor))
                        {
                            continue;
                        }
                    }
                }

                Vector3 pos = stagingGridBoard.GetWorldPositionForAnchor(anchor, shape);
                Vector3 gridScale = Vector3.one * gridCellScale;
                Vector3 slotFitScale = Vector3.one * stagingGridBoard.GetSlotFitScaleForShape(shape);

                GameObject monObj = new GameObject($"CarrierBox_{entry.color}_{shape}_{entry.count}_{i + 1}");
                monObj.transform.SetParent(transform, false);
                monObj.transform.position = pos;
                monObj.transform.localScale = gridScale;

                MonsterCharacter mon = monObj.AddComponent<MonsterCharacter>();
                mon.Initialize(entry.color, entry.count, $"{entry.color}_{entry.count}_{i + 1}", shape);
                mon.SetGridState(anchor, pos, gridScale, slotFitScale, exitClear: true, animateMove: false);
                activeMonsters.Add(mon);
            }

            stagingGridBoard.RefreshAllGridBlockHighlightStates(activeMonsters);
        }

        private static bool IsAnchorFreeForShape(
            StagingGridBoard board,
            Vector2Int anchor,
            CarrierBlockShape shape,
            List<MonsterCharacter> placedBlocks)
        {
            if (board == null || !board.IsInsideGrid(anchor, shape)) return false;
            if (placedBlocks == null || placedBlocks.Count == 0) return true;

            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(shape);
            for (int m = 0; m < placedBlocks.Count; m++)
            {
                var other = placedBlocks[m];
                if (other == null || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0) continue;
                Vector2Int[] otherCells = CarrierBlockShapeUtility.GetCells(other.blockShape);
                for (int i = 0; i < myCells.Length; i++)
                {
                    int mx = anchor.x + myCells[i].x;
                    int my = anchor.y + myCells[i].y;
                    for (int j = 0; j < otherCells.Length; j++)
                    {
                        if (mx == other.gridAnchor.x + otherCells[j].x && my == other.gridAnchor.y + otherCells[j].y)
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Called when a Carrier Block activates collection in place or completes inside the grid.
        /// Refreshes top-exit clearance highlights for remaining blocks in StagingGridBoard.
        /// </summary>
        public void OnStagingBoxPlacedInSlot(MonsterCharacter placedBox)
        {
            if (placedBox == null || activeMonsters == null) return;
            _deadlockTimer = 0f;

            if (stagingGridBoard != null)
            {
                stagingGridBoard.RefreshAllGridBlockHighlightStates(activeMonsters);
            }
        }

        /// <summary>
        /// Called when a Carrier Block finishes filling its sockets and leaves the grid,
        /// freeing its cells so previously blocked blocks behind it can now slide and activate!
        /// </summary>
        public void OnGridBlockCompleted(MonsterCharacter completedBlock)
        {
            _deadlockTimer = 0f;
            if (stagingGridBoard != null && activeMonsters != null)
            {
                stagingGridBoard.RefreshAllGridBlockHighlightStates(activeMonsters);
            }
        }

        /// <summary>
        /// Called when a Carrier Box seals its 3 balls with the transparent lid and completes!
        /// Increments Combo Streak and triggers Fever Mode (2x Vacuum) at 3+ consecutive completions.
        /// </summary>
        public void OnCarrierBoxCompleted(MonsterCharacter completedBox)
        {
            if (completedBox == null) return;
            _deadlockTimer = 0f;

            comboStreak++;
            if (comboStreak >= 3)
            {
                feverRemainingTime = 8.0f;
                if (glassContainer != null)
                {
                    glassContainer.SetFeverVisualGlow(true);
                }
            }
        }

        /// <summary>
        /// Called when a Carrier Box is placed in a slot where its balls are blocked in upper layers.
        /// Resets the consecutive Combo Streak.
        /// </summary>
        public void OnCarrierBoxBlockedInSlot(MonsterCharacter blockedBox)
        {
            comboStreak = 0;
        }

        /// <summary>
        /// Uses 1 Shake / Tilt charge to rock the glass container and unstick wedged balls.
        /// </summary>
        public void TryShakeContainer()
        {
            if (remainingShakes <= 0 || glassContainer == null) return;
            remainingShakes--;
            _deadlockTimer = 0f;
            isLevelFailed = false;
            PuzzleSoundManager.Instance?.PlayShakeGlass();
            glassContainer.ShakeAndTiltContainer(1.0f);
        }

        public void GenerateAndLoadProceduralLevel(int levelNum, LevelDifficulty diff)
        {
            PuzzleGlassLevelData runtimeLvl = ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
            runtimeLvl.levelNumber = Mathf.Max(1, levelNum);
            runtimeLvl.difficulty = diff;
            runtimeLvl.useStagingColumnQueue = true;
            PuzzleLevelGenerator.GenerateForLevelData(runtimeLvl);
            LoadLevel(runtimeLvl);
        }

        public void LoadLevelByIndex(int index)
        {
            if (levels == null || levels.Count == 0)
            {
                int nextNum = (currentLevel != null) ? currentLevel.levelNumber + 1 : 1;
                GenerateAndLoadProceduralLevel(nextNum, PuzzleLevelGenerator.GetProgressiveDifficulty(nextNum));
                return;
            }
            currentLevelIndex = ((index % levels.Count) + levels.Count) % levels.Count;
            LoadLevel(levels[currentLevelIndex]);
        }

        public void LoadNextLevel()
        {
            if (levels != null && levels.Count > 0 && currentLevelIndex + 1 < levels.Count)
            {
                LoadLevelByIndex(currentLevelIndex + 1);
                return;
            }

            // Generate endless progressive levels automatically when reaching the end of saved levels!
            int nextNum = (currentLevel != null) ? currentLevel.levelNumber + 1 : 1;
            LevelDifficulty nextDiff = PuzzleLevelGenerator.GetProgressiveDifficulty(nextNum);
            GenerateAndLoadProceduralLevel(nextNum, nextDiff);
        }

        public void LoadPreviousLevel()
        {
            if (levels != null && levels.Count > 0)
            {
                LoadLevelByIndex(currentLevelIndex - 1);
                return;
            }
            int prevNum = (currentLevel != null) ? Mathf.Max(1, currentLevel.levelNumber - 1) : 1;
            GenerateAndLoadProceduralLevel(prevNum, PuzzleLevelGenerator.GetProgressiveDifficulty(prevNum));
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
                isLevelFailed = false;
                OnLevelCleared();
            }
        }

        private void CheckDeadlockState()
        {
            if (isLevelCompleted || isLevelFailed) return;
            // Upper holding slots are disabled in direct-grid mode; blocks never lock out a slot buffer.
            if (slotSystem == null || !slotSystem.gameObject.activeInHierarchy || slotSystem.slots == null || slotSystem.slots.Count == 0)
            {
                _deadlockTimer = 0f;
                return;
            }

            // Check if every slot is currently occupied by a locked Carrier Box that still needs balls
            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                var slot = slotSystem.slots[i];
                if (slot == null || !slot.isOccupied || slot.currentMonster == null)
                {
                    _deadlockTimer = 0f;
                    return;
                }
                if (slot.currentMonster.IsCompleted || slot.currentMonster.remainingCount <= 0)
                {
                    _deadlockTimer = 0f;
                    return;
                }
            }

            // If any ball is still flying, attracting, or can be collected by any slotted box, not deadlocked
            if (collectionController != null)
            {
                if (collectionController.HasAnyBallInTransitOrAttracting() ||
                    collectionController.HasAnyCollectableMoveForSlottedMonsters())
                {
                    _deadlockTimer = 0f;
                    return;
                }
            }

            _deadlockTimer += Time.deltaTime;
            if (_deadlockTimer >= 1.15f)
            {
                isLevelFailed = true;
                _deadlockTimer = 0f;
                OnLevelDeadlocked();
            }
        }

        private void OnLevelDeadlocked()
        {
            Debug.LogWarning("<color=#FF6655>⚠️ SLOTLAR DOLDU - HAMLE KALMADI! (Alt katmandaki toplarla eşleşen boş slot kalmadı) ⚠️</color>");
            PuzzleSoundManager.Instance?.PlayLevelFail();

            if (slotSystem != null && slotSystem.slots != null)
            {
                for (int i = 0; i < slotSystem.slots.Count; i++)
                {
                    var slot = slotSystem.slots[i];
                    if (slot != null && slot.currentMonster != null)
                    {
                        slot.currentMonster.PlayLockedInSlotFeedback();
                    }
                }
            }
        }

        /// <summary>
        /// Recovery helper when all slots are locked: opens +1 extra slot while keeping existing locked boxes in place!
        /// </summary>
        public void UnlockOneExtraSlotAndContinue()
        {
            if (slotSystem == null || slotSystem.slots == null) return;
            if (slotSystem.slotCount >= 5) return;

            List<MonsterCharacter> currentlySlotted = new List<MonsterCharacter>();
            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                if (slotSystem.slots[i] != null && slotSystem.slots[i].currentMonster != null)
                {
                    currentlySlotted.Add(slotSystem.slots[i].currentMonster);
                }
            }

            slotSystem.slotCount = Mathf.Min(5, slotSystem.slotCount + 1);
            slotSystem.slotSpacing = (slotSystem.slotCount >= 4) ? 0.78f : 0.86f;
            slotSystem.RebuildSlots();

            for (int i = 0; i < currentlySlotted.Count && i < slotSystem.slots.Count; i++)
            {
                var mon = currentlySlotted[i];
                var slot = slotSystem.slots[i];
                if (mon != null && slot != null)
                {
                    slot.AttachMonster(mon);
                }
            }

            isLevelFailed = false;
            _deadlockTimer = 0f;
        }

        private void OnLevelCleared()
        {
            Debug.Log("<color=#00FF88>★ LEVEL CLEARED! ALL CARRIER BOXES FILLED! ★</color>");
            PuzzleSoundManager.Instance?.PlayLevelClear();

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
            GUI.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.92f);

            int panelW = 290;
            int panelH = _showPatternSelector ? 545 : (isLevelFailed ? 415 : 365);
            GUILayout.BeginArea(new Rect(14, 14, panelW, panelH), panelStyle);
            GUILayout.Space(5);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };
            titleStyle.normal.textColor = Color.white;

            int lvlNum = (currentLevel != null) ? currentLevel.levelNumber : 1;
            LevelDifficulty diff = (currentLevel != null) ? currentLevel.difficulty : LevelDifficulty.Easy;
            string diffLabel = PuzzleLevelGenerator.GetDifficultyTurkishLabel(diff).ToUpperInvariant();
            GUILayout.Label($"Level {lvlNum}  •  Zorluk: {diffLabel}", titleStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            subStyle.normal.textColor = new Color(0.75f, 0.88f, 1f, 1f);

            int ballCount = BallObjectPool.Instance != null ? BallObjectPool.Instance.ActiveBalls.Count : 0;
            int remBoxes = 0;
            if (activeMonsters != null)
            {
                for (int i = 0; i < activeMonsters.Count; i++)
                {
                    if (activeMonsters[i] != null && activeMonsters[i].remainingCount > 0) remBoxes++;
                }
            }
            int sCount = (slotSystem != null) ? slotSystem.slotCount : 3;
            GUILayout.Label($"Toplar: {ballCount} | Kalan Kutu: {remBoxes} | Slot: {sCount}", subStyle);

            // Combo Streak / Fever 2x Vacuum Status Bar
            GUIStyle feverStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            if (IsFeverActive)
            {
                feverStyle.normal.textColor = new Color(1.0f, 0.82f, 0.18f, 1f);
                GUILayout.Label($"🔥 FEVER 2X VAKUM AKTİF! ({feverRemainingTime:F1}sn) 🔥", feverStyle);
            }
            else
            {
                feverStyle.normal.textColor = comboStreak > 0 ? new Color(0.45f, 0.95f, 0.65f, 1f) : new Color(0.68f, 0.76f, 0.88f, 1f);
                GUILayout.Label($"⚡ Kombo Zinciri: {comboStreak} / 3 (3'te 2x Vakum!)", feverStyle);
            }

            // Shake / Tilt Action Row
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = remainingShakes > 0 ? new Color(0.96f, 0.64f, 0.16f, 1f) : new Color(0.45f, 0.45f, 0.50f, 1f);
            if (GUILayout.Button($"📳 Camı Salla ({remainingShakes}/{maxShakesPerLevel}) [S]", GUILayout.Height(24)))
            {
                TryShakeContainer();
            }
            GUILayout.EndHorizontal();

            if (isLevelCompleted)
            {
                GUIStyle winStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13
                };
                winStyle.normal.textColor = new Color(0.2f, 1f, 0.4f, 1f);
                GUILayout.Label("★ LEVEL TAMAMLANDI! ★", winStyle);
            }
            else if (isLevelFailed)
            {
                GUIStyle failStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
                };
                failStyle.normal.textColor = new Color(1f, 0.38f, 0.35f, 1f);
                GUILayout.Label("⚠️ SLOTLAR KİLİTLENDİ - HAMLE YOK!", failStyle);

                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.95f, 0.40f, 0.35f, 1f);
                if (GUILayout.Button("🔄 Tekrar Dene", GUILayout.Height(26)))
                {
                    RestartCurrentPattern();
                }
                if (slotSystem != null && slotSystem.slotCount < 5)
                {
                    GUI.backgroundColor = new Color(0.25f, 0.82f, 0.50f, 1f);
                    if (GUILayout.Button("🔓 +1 Ek Slot Aç", GUILayout.Height(26)))
                    {
                        UnlockOneExtraSlotAndContinue();
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };

            // Quick Procedural Level Generator Bar (Easy / Medium / Hard / Boss)
            GUILayout.Label("🎲 Anında Yeni Level Üret (Zorluk Seç):", subStyle);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.22f, 0.82f, 0.46f, 1f);
            if (GUILayout.Button("Kolay", btnStyle, GUILayout.Height(24)))
                GenerateAndLoadProceduralLevel(lvlNum + 1, LevelDifficulty.Easy);

            GUI.backgroundColor = new Color(0.28f, 0.68f, 0.98f, 1f);
            if (GUILayout.Button("Orta", btnStyle, GUILayout.Height(24)))
                GenerateAndLoadProceduralLevel(lvlNum + 1, LevelDifficulty.Medium);

            GUI.backgroundColor = new Color(0.95f, 0.62f, 0.22f, 1f);
            if (GUILayout.Button("Zor", btnStyle, GUILayout.Height(24)))
                GenerateAndLoadProceduralLevel(lvlNum + 1, LevelDifficulty.Hard);

            GUI.backgroundColor = new Color(0.92f, 0.32f, 0.45f, 1f);
            if (GUILayout.Button("Boss", btnStyle, GUILayout.Height(24)))
                GenerateAndLoadProceduralLevel(lvlNum + 1, LevelDifficulty.Boss);
            GUILayout.EndHorizontal();

            GUILayout.Space(3);
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f, 1f);
            if (GUILayout.Button(_showPatternSelector ? "▲ Desenleri Gizle" : "▼ Özel Desen Testi (" + activePatternType + ")", btnStyle, GUILayout.Height(24)))
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
                if (GUILayout.Button("★ Sıralı Havuz", btnStyle, GUILayout.Height(24))) SetupShapePatternScenario(ShapePatternType.GumballMix);
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
            if (GUILayout.Button("🔄 Levelı Yeniden Başlat (R)", btnStyle, GUILayout.Height(25)))
            {
                RestartCurrentPattern();
            }

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.6f, 1f);
            if (GUILayout.Button("◄ Önceki Level", btnStyle, GUILayout.Height(24))) LoadPreviousLevel();
            GUI.backgroundColor = new Color(0.28f, 0.78f, 0.52f, 1f);
            if (GUILayout.Button("Sonraki Level (N) ►", btnStyle, GUILayout.Height(24))) LoadNextLevel();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            GUI.backgroundColor = oldBg;
        }
    }
}
