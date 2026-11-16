using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Framed bottom sliding-puzzle Grid Tray matching Reference Image 1:
    /// Holds polyomino Carrier Blocks inside recessed rounded cells. Players slide blocks around empty cells
    /// and pull unobstructed blocks up out of the top exit into the 4 upper slots.
    /// </summary>
    [ExecuteAlways]
    public class StagingGridBoard : MonoBehaviour
    {
        public static StagingGridBoard Instance { get; private set; }

        [Header("Grid Dimensions")]
        [Range(2, 7)] public int cols = 5;
        [Range(2, 6)] public int rows = 4;

        [Header("World Layout")]
        public Vector3 boardCenter = new Vector3(0f, -0.82f, 0.30f);
        public float maxBoardWidth = 2.822f;
        public float maxBoardHeight = 3.20f;
        public float framePadding = 0.068f;
        public float innerMargin = 0.048f;
        public float frameCornerRadius = 0.135f;
        public float gapBelowGlass = 0.11f;
        public bool matchGlassWidth = true;
        public bool autoAlignBelowGlass = true;
        public bool useManualCellSize = false;
        [Range(0.30f, 0.82f)] public float manualCellSize = 0.506f;

        [Header("Tray Colors")]
        public Color frameDarkColor = new Color(0.17f, 0.15f, 0.35f, 1f);
        public Color frameFrontColor = new Color(0.26f, 0.23f, 0.48f, 1f);
        public Color trayFloorColor = new Color(0.14f, 0.12f, 0.28f, 1f);
        public Color tileBevelColor = new Color(0.30f, 0.27f, 0.54f, 1f);
        public Color tileBodyColor = new Color(0.22f, 0.19f, 0.43f, 1f);
        public Color exitRimColor = new Color(0.50f, 0.50f, 0.76f, 1f);
        public bool showOuterGlassJar = true;

        [SerializeField, HideInInspector] private int visualThemeVersion = 0;

        [HideInInspector] public float cellSize = 0.506f;

        private GameObject _visualRoot;
        private Material _jarBackMat;
        private Material _jarFrontMat;

        // Pillar 3: Event-Driven Dirty-Flagged Occupancy Grid + Multi-Source BFS Flow Field
        private bool _isGridDirty = true;
        private int _lastGridCols = -1;
        private int _lastGridRows = -1;
        private int _lastMonsterCount = -1;
        private MonsterCharacter[] _cellOccupancy;
        private MonsterCharacter[] _topExitColumnOwner;
        private int[] _bfsDistanceField;
        private Vector2Int[] _bfsFlowDirField;
        private int[] _bfsQueue;
        private static readonly int[] BfsDx = { 0, 1, -1, 0 };
        private static readonly int[] BfsDy = { 1, 0, 0, -1 };

        public void MarkGridDirty()
        {
            _isGridDirty = true;
        }

        public float GridOriginX => boardCenter.x - cols * cellSize * 0.5f;
        public float GridOriginY => boardCenter.y - rows * cellSize * 0.5f;
        public float TopFrameInnerY => boardCenter.y + rows * cellSize * 0.5f;

        private void Awake()
        {
            Instance = this;
            MigrateThemeIfNeeded();
        }

        private void OnEnable()
        {
            Instance = this;
            MigrateThemeIfNeeded();
        }

        private void MigrateThemeIfNeeded()
        {
            if (visualThemeVersion >= 4) return;
            visualThemeVersion = 4;
            framePadding = 0.068f;
            innerMargin = 0.048f;
            frameCornerRadius = 0.135f;
            ResetDefaultColors();
        }

        public void Configure(int gridCols, int gridRows, ShapedGlassContainer alignGlass = null)
        {
            cols = Mathf.Clamp(gridCols, 2, 7);
            rows = Mathf.Clamp(gridRows, 2, 6);

            ShapedGlassContainer glass = alignGlass ?? FindFirstObjectByType<ShapedGlassContainer>();

            if (useManualCellSize)
            {
                cellSize = Mathf.Clamp(manualCellSize, 0.30f, 0.82f);
            }
            else
            {
                float targetBoardWidth = maxBoardWidth;
                if (matchGlassWidth && glass != null && glass.TryGetVisibleFrameWorldBounds(out float gMinX, out float gMaxX, out _, out _))
                {
                    float glassOuterW = gMaxX - gMinX;
                    // Tepsi camdan biraz geniş ama en fazla maxBoardWidth (dar ters şişede kavanoz ekranı kaplamasın)
                    if (glassOuterW > 1.0f) targetBoardWidth = Mathf.Min(glassOuterW * 1.06f, maxBoardWidth);
                }

                float frameOverhead = innerMargin + framePadding * 2f;
                float usableW = Mathf.Max(0.8f, targetBoardWidth - frameOverhead);
                float usableH = Mathf.Max(0.8f, maxBoardHeight - frameOverhead);
                cellSize = Mathf.Clamp(Mathf.Min(usableW / cols, usableH / rows), 0.32f, 0.78f);
            }

            if (autoAlignBelowGlass && glass != null)
            {
                AlignBelowGlass(glass);
            }

            MarkGridDirty();
            BuildBoardVisuals();
        }

        public void GetOuterWorldDimensions(out float outerW, out float outerH)
        {
            float innerW = cols * cellSize + innerMargin;
            float innerH = rows * cellSize + innerMargin;
            outerW = innerW + framePadding * 2f;
            outerH = innerH + framePadding * 2f;
        }

        private const float JarPadX = 0.036f;
        private const float JarPadY = 0.040f;
        private const float JarNeckNormH = 0.16f;
        private const float JarFrameNormThick = 0.034f;

        private void GetOuterJarExtents(float outerW, float outerH, out float quadHalfW, out float quadHalfH, out float quadCenterYOffset, out float boxAspect, out float neckTopAboveCenter)
        {
            float innerWorldHalfW = outerW * 0.5f + JarPadX;
            float innerWorldHalfH = outerH * 0.5f + JarPadY;
            float innerNormHalfH = 0.76f - JarNeckNormH * 0.5f - JarFrameNormThick;
            quadHalfH = innerWorldHalfH / innerNormHalfH;
            float innerNormHalfW = innerWorldHalfW / quadHalfH;
            boxAspect = Mathf.Max(0.30f, (innerNormHalfW + JarFrameNormThick) / 0.80f);
            quadHalfW = boxAspect * quadHalfH;
            quadCenterYOffset = JarNeckNormH * 0.5f * quadHalfH;
            neckTopAboveCenter = (0.76f + JarNeckNormH * 0.5f) * quadHalfH;
        }

        public void ResetDefaultColors()
        {
            frameDarkColor = new Color(0.17f, 0.15f, 0.35f, 1f);
            frameFrontColor = new Color(0.26f, 0.23f, 0.48f, 1f);
            trayFloorColor = new Color(0.14f, 0.12f, 0.28f, 1f);
            tileBevelColor = new Color(0.30f, 0.27f, 0.54f, 1f);
            tileBodyColor = new Color(0.22f, 0.19f, 0.43f, 1f);
            exitRimColor = new Color(0.50f, 0.50f, 0.76f, 1f);
        }

        /// <summary>
        /// Places the grid tray + surrounding lower glass jar snugly beneath the upper glass container's exit collar.
        /// </summary>
        public void AlignBelowGlass(ShapedGlassContainer glass)
        {
            if (glass == null) return;

            float glassCenterX = 0f;
            float glassBottomY = glass.transform.position.y - 0.82f;
            if (glass.TryGetVisibleFrameWorldBounds(out float gMinX, out float gMaxX, out float gMinY, out _))
            {
                glassCenterX = (gMinX + gMaxX) * 0.5f;
                glassBottomY = gMinY;
            }

            GetOuterWorldDimensions(out float outerW, out float outerH);
            float topExtent;
            float botExtent;
            if (showOuterGlassJar)
            {
                GetOuterJarExtents(outerW, outerH, out _, out float qh, out _, out _, out float neckTop);
                topExtent = neckTop;
                botExtent = (0.76f - JarNeckNormH * 0.5f) * qh;
            }
            else
            {
                topExtent = outerH * 0.5f;
                botExtent = outerH * 0.5f;
            }

            // Camın altında çıkış yakası varsa kavanoz ağzıyla arasında boşluk kalsın
            float minGap = (glass.showBottomExitCollar && glass.IsPoolShape) ? 0.36f : 0.14f;
            float targetCenterY = glassBottomY - Mathf.Max(minGap, gapBelowGlass * 1.18f) - topExtent;

            // Keep well inside the bottom edge of orthographic portrait view
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                float camBottom = cam.transform.position.y - cam.orthographicSize;
                float minCenterY = camBottom + botExtent + 0.24f;
                if (targetCenterY < minCenterY) targetCenterY = minCenterY;
            }

            boardCenter = new Vector3(glassCenterX, targetCenterY, 0.30f);
        }

        [ContextMenu("Rebuild Grid Board Visuals")]
        public void BuildBoardVisuals()
        {
            if (_visualRoot != null)
            {
                if (Application.isPlaying) Destroy(_visualRoot);
                else DestroyImmediate(_visualRoot);
            }

            // Remove any leftover children just in case
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject);
                else DestroyImmediate(transform.GetChild(i).gameObject);
            }

            _visualRoot = new GameObject("StagingGridVisuals");
            _visualRoot.transform.SetParent(transform, false);
            _visualRoot.transform.position = boardCenter;

            float gridW = cols * cellSize;
            float gridH = rows * cellSize;
            float innerW = gridW + innerMargin;
            float innerH = gridH + innerMargin;
            float outerW = innerW + framePadding * 2f;
            float outerH = innerH + framePadding * 2f;

            // 0. Surrounding Lower Glass Jar Envelope (thin, understated rim)
            if (showOuterGlassJar)
            {
                BuildOuterGlassJar(outerW, outerH);
            }
            else
            {
                Shader shadowShader = Shader.Find("PuzzleGlass/FakeDropShadow");
                if (shadowShader != null)
                {
                    GameObject shObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    shObj.name = "BoardFakeShadow";
                    shObj.transform.SetParent(_visualRoot.transform, false);
                    shObj.transform.localPosition = new Vector3(-0.04f, -0.09f, 0.16f);
                    shObj.transform.localScale = new Vector3(outerW * 1.12f, outerH * 1.12f, 1f);
                    DestroyCollider(shObj);
                    Material shMat = new Material(shadowShader) { name = "Mat_GridBoardShadow" };
                    shMat.SetColor("_ShadowColor", new Color(0.64f, 0.54f, 0.68f, 0.30f));
                    shMat.SetFloat("_FeatherWidth", 0.35f);
                    shMat.SetFloat("_IsRadial", 0f);
                    var shMr = shObj.GetComponent<MeshRenderer>();
                    shMr.sharedMaterial = shMat;
                    shMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    shMr.receiveShadows = false;
                }
            }

            // 2. Slimmer, Muted Indigo Frame + Deep Recessed Tray Gutters + Darker Distinct Grid Tiles
            Color coolShadow = new Color(0.46f, 0.48f, 0.66f, 1f);
            Material frameDarkMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(frameDarkColor, coolShadow, "Mat_GridFrameDark", glossyCandy: false, indirectIntensity: 0.14f);
            Material frameFrontMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(frameFrontColor, coolShadow, "Mat_GridFrameFront", glossyCandy: false, indirectIntensity: 0.14f);
            Material trayFloorMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(trayFloorColor, new Color(0.40f, 0.42f, 0.60f, 1f), "Mat_GridTrayFloor", glossyCandy: false, indirectIntensity: 0.12f);
            Material tileBevelMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(tileBevelColor, coolShadow, "Mat_GridTileBevel", glossyCandy: false, indirectIntensity: 0.14f);
            Material tileBodyMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(tileBodyColor, new Color(0.44f, 0.46f, 0.64f, 1f), "Mat_GridTileBody", glossyCandy: false, indirectIntensity: 0.12f);
            Material exitRimMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(exitRimColor, new Color(0.56f, 0.60f, 0.78f, 1f), "Mat_GridTopExitRim", glossyCandy: false, indirectIntensity: 0.15f);

            float cornerR = Mathf.Clamp(frameCornerRadius, 0.04f, 0.40f);
            float innerCornerR = Mathf.Max(0.03f, cornerR - framePadding * 0.55f);
            CreateRoundedPart("FrameLowerBevel", _visualRoot.transform, new Vector3(0f, -0.032f, 0.08f), outerW, outerH, 0.045f, cornerR, frameDarkMat);
            CreateRoundedPart("FrameMain", _visualRoot.transform, new Vector3(0f, 0f, 0.05f), outerW, outerH, 0.048f, cornerR, frameFrontMat);
            CreateRoundedPart("TrayRecessedBed", _visualRoot.transform, new Vector3(0f, 0f, 0.02f), innerW, innerH, 0.04f, innerCornerR, trayFloorMat);

            // Top exit rail accent showing blocks pull UP toward the upper glass container
            CreateRoundedPart("TopExitHighlightBar", _visualRoot.transform, new Vector3(0f, innerH * 0.5f + framePadding * 0.50f, 0.00f), innerW * 0.88f, 0.028f, 0.02f, 0.014f, exitRimMat);

            // 3. Individual Rounded Recessed Grid Tiles (cols x rows) with shared meshes for GPU Instancing
            float tileOuter = cellSize * 0.81f;
            float tileInner = cellSize * 0.76f;
            float tileRad = cellSize * 0.15f;
            Mesh sharedBevelMesh = ProceduralMeshUtils.CreatePillBadge(tileOuter, tileOuter, 0.02f, Mathf.Min(tileRad, tileOuter * 0.48f));
            Mesh sharedInnerMesh = ProceduralMeshUtils.CreatePillBadge(tileInner, tileInner, 0.02f, Mathf.Min(tileRad * 0.88f, tileInner * 0.48f));

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float lx = -gridW * 0.5f + (c + 0.5f) * cellSize;
                    float ly = -gridH * 0.5f + (r + 0.5f) * cellSize;

                    CreateRoundedPartWithMesh($"CellBevel_{c}_{r}", _visualRoot.transform, new Vector3(lx, ly + 0.007f, 0.005f), sharedBevelMesh, tileBevelMat);
                    CreateRoundedPartWithMesh($"CellTile_{c}_{r}", _visualRoot.transform, new Vector3(lx, ly, -0.005f), sharedInnerMesh, tileBodyMat);
                }
            }
        }

        private void BuildOuterGlassJar(float outerW, float outerH)
        {
            Shader chamberShader = Shader.Find("PuzzleGlass/GlassChamberLit");
            if (chamberShader == null) return;

            GetOuterJarExtents(outerW, outerH, out float quadHalfW, out float quadHalfH, out float quadCenterYOffset, out float boxAspect, out _);

            float innerNormHalfW = (outerW * 0.5f + JarPadX) / Mathf.Max(0.01f, quadHalfH);
            // Boyun gövdenin ~%60'ı (hedef görseldeki kavanoz), ağız taşması az
            float neckHalfW = Mathf.Clamp(innerNormHalfW * 0.64f, 0.30f, 0.60f);
            Vector4 neckParams = new Vector4(neckHalfW, JarNeckNormH, 0.03f, 0.036f);

            Material ConfigureJarMat(Material existing, string matName, bool frontLens)
            {
                Material m = (existing != null && existing.shader == chamberShader) ? existing : new Material(chamberShader);
                m.name = matName;
                m.SetColor("_CenterColor", frontLens ? new Color(0.95f, 0.98f, 1.00f, 0.0f) : new Color(0.84f, 0.87f, 0.98f, 0.12f));
                m.SetColor("_EdgeColor", new Color(0.50f, 0.54f, 0.84f, 0.34f));
                m.SetColor("_OuterRimColor", new Color(0.32f, 0.30f, 0.64f, 0.62f));
                m.SetColor("_InnerGrooveColor", new Color(0.28f, 0.26f, 0.56f, 0.52f));
                m.SetColor("_RimGlowColor", new Color(0.86f, 0.88f, 0.98f, 0.42f));
                m.SetColor("_FrameColor", new Color(0.32f, 0.30f, 0.60f, 0.84f));
                m.SetColor("_FrameHighlight", new Color(0.74f, 0.77f, 0.94f, 0.70f));
                m.SetColor("_FrameEdgeShade", new Color(0.22f, 0.20f, 0.46f, 0.88f));
                m.SetColor("_FakeShadowColor", new Color(0.46f, 0.40f, 0.68f, 0.24f));
                m.SetFloat("_IsFrontLens", frontLens ? 1f : 0f);
                m.SetFloat("_IsCollar", 0f);
                m.SetFloat("_IsRoundedBoxPool", 1f);
                m.SetFloat("_PolyCount", 0f);
                m.SetFloat("_BoxAspect", boxAspect);
                m.SetFloat("_BoxCornerRadius", 0.20f);
                m.SetFloat("_FrameThickness", JarFrameNormThick);
                m.SetFloat("_SheenIntensity", 0.42f);
                m.SetFloat("_CenterGlassAlpha", 0.12f);
                m.SetVector("_JarNeckParams", neckParams);
                m.renderQueue = frontLens ? 3022 : 1998;
                return m;
            }

            _jarBackMat = ConfigureJarMat(_jarBackMat, "Mat_LowerJarBack", false);
            _jarFrontMat = ConfigureJarMat(_jarFrontMat, "Mat_LowerJarFront", true);

            GameObject CreateJarQuad(string qName, float localZ, Material mat)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = qName;
                go.transform.SetParent(_visualRoot.transform, false);
                go.transform.localPosition = new Vector3(0f, quadCenterYOffset, localZ);
                go.transform.localScale = new Vector3(quadHalfW * 2f, quadHalfH * 2f, 1f);
                DestroyCollider(go);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return go;
            }

            CreateJarQuad("LowerJarGlassBack", 0.12f, _jarBackMat);
            CreateJarQuad("LowerJarGlassFront", -0.56f, _jarFrontMat);
        }

        private static void CreateRoundedPart(string partName, Transform parent, Vector3 localPos, float w, float h, float depth, float radius, Material mat)
        {
            Mesh mesh = ProceduralMeshUtils.CreatePillBadge(w, h, depth, Mathf.Min(radius, Mathf.Min(w, h) * 0.48f));
            CreateRoundedPartWithMesh(partName, parent, localPos, mesh, mat);
        }

        private static void CreateRoundedPartWithMesh(string partName, Transform parent, Vector3 localPos, Mesh sharedMesh, Material mat)
        {
            GameObject obj = new GameObject(partName);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void DestroyCollider(GameObject obj)
        {
            var col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        // ==================== COORDINATE & OCCUPANCY HELPERS ====================

        public Vector3 GetWorldPositionForAnchor(Vector2Int anchor, CarrierBlockShape shape)
        {
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            float cx = GridOriginX + (anchor.x + w * 0.5f) * cellSize;
            float cy = GridOriginY + (anchor.y + h * 0.5f) * cellSize;
            return new Vector3(cx, cy, 0f);
        }

        public Vector2Int WorldCenterToNearestAnchor(Vector3 worldCenter, CarrierBlockShape shape)
        {
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            float rawX = (worldCenter.x - GridOriginX) / cellSize - w * 0.5f;
            float rawY = (worldCenter.y - GridOriginY) / cellSize - h * 0.5f;
            int ax = Mathf.Clamp(Mathf.RoundToInt(rawX), 0, Mathf.Max(0, cols - w));
            int ay = Mathf.Clamp(Mathf.RoundToInt(rawY), 0, Mathf.Max(0, rows - h));
            return new Vector2Int(ax, ay);
        }

        public float GetSlotFitScaleForShape(CarrierBlockShape shape)
        {
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            int maxDim = Mathf.Max(w, h);
            // Scales 1x1 cleanly inside 0.70, 2x1 / L shapes cleanly inside ~0.72, 3-wide shapes cleanly inside ~0.78
            if (maxDim <= 1) return 0.68f;
            if (maxDim == 2) return 0.37f;
            return 0.265f;
        }

        public bool IsInsideGrid(Vector2Int anchor, CarrierBlockShape shape)
        {
            Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(shape);
            for (int i = 0; i < cells.Length; i++)
            {
                int gx = anchor.x + cells[i].x;
                int gy = anchor.y + cells[i].y;
                if (gx < 0 || gx >= cols || gy < 0 || gy >= rows) return false;
            }
            return true;
        }

        public void EnsureFlowFieldUpdated(List<MonsterCharacter> allMonsters)
        {
            int totalCells = cols * rows;
            int monsterCount = allMonsters != null ? allMonsters.Count : 0;
            bool sizeChanged = _cellOccupancy == null || _cellOccupancy.Length != totalCells || _lastGridCols != cols || _lastGridRows != rows;
            if (!sizeChanged && !_isGridDirty && monsterCount == _lastMonsterCount)
            {
                return;
            }

            if (sizeChanged)
            {
                _cellOccupancy = new MonsterCharacter[totalCells];
                _topExitColumnOwner = new MonsterCharacter[cols];
                _bfsDistanceField = new int[totalCells];
                _bfsFlowDirField = new Vector2Int[totalCells];
                _bfsQueue = new int[totalCells];
                _lastGridCols = cols;
                _lastGridRows = rows;
            }
            else
            {
                System.Array.Clear(_cellOccupancy, 0, totalCells);
                System.Array.Clear(_topExitColumnOwner, 0, cols);
            }

            _lastMonsterCount = monsterCount;
            _isGridDirty = false;

            if (allMonsters != null)
            {
                for (int m = 0; m < allMonsters.Count; m++)
                {
                    var other = allMonsters[m];
                    if (other == null || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0 || !other.gameObject.activeInHierarchy)
                        continue;

                    Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(other.blockShape);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        int gx = other.gridAnchor.x + cells[i].x;
                        int gy = other.gridAnchor.y + cells[i].y;
                        if (gx >= 0 && gx < cols && gy >= 0 && gy < rows)
                        {
                            _cellOccupancy[gy * cols + gx] = other;
                        }
                    }
                }
            }

            // 1. Her sütunda en üstte yer alan ve çıkışı açık olan bloğu O(cols * rows) içinde bul
            for (int x = 0; x < cols; x++)
            {
                for (int y = rows - 1; y >= 0; y--)
                {
                    var occ = _cellOccupancy[y * cols + x];
                    if (occ != null)
                    {
                        if (IsBlockAtTopExit(occ))
                        {
                            _topExitColumnOwner[x] = occ;
                        }
                        break;
                    }
                }
            }

            // 2. Multi-Source BFS Flow Field: Üst çıkış sırasındaki boş hücrelerden aşağı doğru mesafe & akış yönü yay
            for (int i = 0; i < totalCells; i++)
            {
                _bfsDistanceField[i] = -1;
                _bfsFlowDirField[i] = Vector2Int.zero;
            }

            int qHead = 0;
            int qTail = 0;
            int topRow = rows - 1;
            for (int x = 0; x < cols; x++)
            {
                int idx = topRow * cols + x;
                if (_cellOccupancy[idx] == null)
                {
                    _bfsDistanceField[idx] = 0;
                    _bfsFlowDirField[idx] = Vector2Int.up;
                    _bfsQueue[qTail++] = idx;
                }
            }

            while (qHead < qTail)
            {
                int curIdx = _bfsQueue[qHead++];
                int cx = curIdx % cols;
                int cy = curIdx / cols;
                int nextDist = _bfsDistanceField[curIdx] + 1;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + BfsDx[d];
                    int ny = cy + BfsDy[d];
                    if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                    int nIdx = ny * cols + nx;
                    if (_cellOccupancy[nIdx] != null || _bfsDistanceField[nIdx] >= 0) continue;

                    _bfsDistanceField[nIdx] = nextDist;
                    _bfsFlowDirField[nIdx] = new Vector2Int(cx - nx, cy - ny);
                    _bfsQueue[qTail++] = nIdx;
                }
            }
        }

        public bool TryGetFlowDirectionToExit(Vector2Int cell, out Vector2Int flowDir, List<MonsterCharacter> allMonsters = null)
        {
            if (cell.x < 0 || cell.x >= cols || cell.y < 0 || cell.y >= rows)
            {
                flowDir = Vector2Int.zero;
                return false;
            }
            EnsureFlowFieldUpdated(allMonsters ?? (PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null));
            int idx = cell.y * cols + cell.x;
            if (_bfsDistanceField[idx] >= 0)
            {
                flowDir = _bfsFlowDirField[idx];
                return true;
            }
            flowDir = Vector2Int.zero;
            return false;
        }

        public int GetDistanceToExit(Vector2Int cell, List<MonsterCharacter> allMonsters = null)
        {
            if (cell.x < 0 || cell.x >= cols || cell.y < 0 || cell.y >= rows) return -1;
            EnsureFlowFieldUpdated(allMonsters ?? (PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null));
            return _bfsDistanceField[cell.y * cols + cell.x];
        }

        public bool CanPlaceBlockAt(MonsterCharacter movingBlock, Vector2Int candidateAnchor, List<MonsterCharacter> allMonsters)
        {
            CarrierBlockShape shape = movingBlock != null ? movingBlock.blockShape : CarrierBlockShape.Rect1x1;
            if (!IsInsideGrid(candidateAnchor, shape)) return false;
            if (allMonsters == null) return true;

            EnsureFlowFieldUpdated(allMonsters);
            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(shape);
            for (int i = 0; i < myCells.Length; i++)
            {
                int mx = candidateAnchor.x + myCells[i].x;
                int my = candidateAnchor.y + myCells[i].y;
                var occ = _cellOccupancy[my * cols + mx];
                if (occ != null && occ != movingBlock)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Steps 1 orthogonal cell at a time toward desiredAnchor without tunneling through other blocks or grid walls.
        /// </summary>
        public Vector2Int SlideAnchorStepByStep(MonsterCharacter block, Vector2Int desiredAnchor, List<MonsterCharacter> allMonsters)
        {
            if (block == null) return desiredAnchor;
            Vector2Int cur = block.gridAnchor;

            int safety = cols + rows + 4;
            while (cur != desiredAnchor && safety-- > 0)
            {
                int dx = desiredAnchor.x - cur.x;
                int dy = desiredAnchor.y - cur.y;
                bool moved = false;

                // Try dominant axis first, then secondary axis so sliding around corners feels responsive
                if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                {
                    if (dx != 0)
                    {
                        Vector2Int stepX = new Vector2Int(cur.x + (dx > 0 ? 1 : -1), cur.y);
                        if (CanPlaceBlockAt(block, stepX, allMonsters))
                        {
                            cur = stepX;
                            moved = true;
                        }
                    }
                    if (!moved && dy != 0)
                    {
                        Vector2Int stepY = new Vector2Int(cur.x, cur.y + (dy > 0 ? 1 : -1));
                        if (CanPlaceBlockAt(block, stepY, allMonsters))
                        {
                            cur = stepY;
                            moved = true;
                        }
                    }
                }
                else
                {
                    if (dy != 0)
                    {
                        Vector2Int stepY = new Vector2Int(cur.x, cur.y + (dy > 0 ? 1 : -1));
                        if (CanPlaceBlockAt(block, stepY, allMonsters))
                        {
                            cur = stepY;
                            moved = true;
                        }
                    }
                    if (!moved && dx != 0)
                    {
                        Vector2Int stepX = new Vector2Int(cur.x + (dx > 0 ? 1 : -1), cur.y);
                        if (CanPlaceBlockAt(block, stepX, allMonsters))
                        {
                            cur = stepX;
                            moved = true;
                        }
                    }
                }

                if (!moved) break;
            }

            return cur;
        }

        /// <summary>
        /// Checks whether a block's upward lane toward the upper slots is completely clear of other blocks in the grid.
        /// </summary>
        public bool IsBlockExitClearToSlots(MonsterCharacter block, List<MonsterCharacter> allMonsters)
        {
            if (block == null || block.assignedSlot != null || block.IsCompleted || block.remainingCount <= 0)
                return false;

            if (allMonsters == null) return true;

            EnsureFlowFieldUpdated(allMonsters);
            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(block.blockShape);
            for (int i = 0; i < myCells.Length; i++)
            {
                int mx = block.gridAnchor.x + myCells[i].x;
                int my = block.gridAnchor.y + myCells[i].y;
                if (mx < 0 || mx >= cols) continue;
                for (int oy = my + 1; oy < rows; oy++)
                {
                    var occ = _cellOccupancy[oy * cols + mx];
                    if (occ != null && occ != block)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true when at least one cell of this block touches the very top exit row (rows - 1).
        /// Bringing a matching-color block to this top row immediately activates ball collection from the glass!
        /// </summary>
        public bool IsBlockAtTopExit(MonsterCharacter block)
        {
            if (block == null || block.assignedSlot != null || block.IsCompleted || block.remainingCount <= 0 || !block.gameObject.activeInHierarchy)
                return false;
            if (block.IsMovementOrCollectionBlocked)
                return false;

            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(block.blockShape);
            int topRowY = rows - 1;
            for (int i = 0; i < myCells.Length; i++)
            {
                if (block.gridAnchor.y + myCells[i].y >= topRowY)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if worldX falls within the horizontal span of any column occupied by this block
        /// that has an unobstructed vertical path up to the glass container.
        /// </summary>
        public bool IsWorldXAlignedWithBlockTopColumns(
            MonsterCharacter block,
            float worldX,
            float edgeTolerance,
            List<MonsterCharacter> allMonsters)
        {
            if (block == null || !IsBlockAtTopExit(block)) return false;

            EnsureFlowFieldUpdated(allMonsters);
            for (int cx = 0; cx < cols; cx++)
            {
                if (_topExitColumnOwner[cx] != block) continue;

                float colLeft = (cx <= 0) ? float.NegativeInfinity : (GridOriginX + cx * cellSize - edgeTolerance);
                float colRight = (cx >= cols - 1) ? float.PositiveInfinity : (GridOriginX + (cx + 1) * cellSize + edgeTolerance);
                if (worldX >= colLeft && worldX <= colRight)
                {
                    return true;
                }
            }

            return false;
        }

        public void RefreshAllGridBlockHighlightStates(List<MonsterCharacter> allMonsters)
        {
            MarkGridDirty();
            EnsureFlowFieldUpdated(allMonsters);
            if (allMonsters == null) return;
            for (int i = 0; i < allMonsters.Count; i++)
            {
                var m = allMonsters[i];
                if (m == null || m.assignedSlot != null || m.IsCompleted || m.remainingCount <= 0) continue;
                bool atTopExit = IsBlockAtTopExit(m);
                m.SetExitClearHighlight(atTopExit);
                m.armedAtTopEdge = atTopExit;
            }
        }

        public bool TryFindEmptyPlacement(CarrierBlockShape shape, List<MonsterCharacter> allMonsters, out Vector2Int foundAnchor)
        {
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            for (int r = rows - h; r >= 0; r--)
            {
                for (int c = 0; c <= cols - w; c++)
                {
                    Vector2Int cand = new Vector2Int(c, r);
                    if (CanPlaceShapeCells(shape, cand, allMonsters))
                    {
                        foundAnchor = cand;
                        return true;
                    }
                }
            }
            foundAnchor = Vector2Int.zero;
            return false;
        }

        private bool CanPlaceShapeCells(CarrierBlockShape shape, Vector2Int candidateAnchor, List<MonsterCharacter> allMonsters)
        {
            if (!IsInsideGrid(candidateAnchor, shape)) return false;
            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(shape);
            if (allMonsters == null) return true;

            for (int m = 0; m < allMonsters.Count; m++)
            {
                var other = allMonsters[m];
                if (other == null || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0)
                    continue;

                Vector2Int[] otherCells = CarrierBlockShapeUtility.GetCells(other.blockShape);
                for (int i = 0; i < myCells.Length; i++)
                {
                    int mx = candidateAnchor.x + myCells[i].x;
                    int my = candidateAnchor.y + myCells[i].y;
                    for (int j = 0; j < otherCells.Length; j++)
                    {
                        if (mx == other.gridAnchor.x + otherCells[j].x && my == other.gridAnchor.y + otherCells[j].y)
                            return false;
                    }
                }
            }
            return true;
        }
    }
}
