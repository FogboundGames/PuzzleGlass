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
        [Range(3, 6)] public int cols = 5;
        [Range(3, 5)] public int rows = 4;

        [Header("World Layout")]
        public Vector3 boardCenter = new Vector3(0f, -0.82f, 0.30f);
        public float maxBoardWidth = 2.822f;
        public float maxBoardHeight = 3.20f;
        public float framePadding = 0.115f;
        public float innerMargin = 0.06f;
        public float gapBelowGlass = 0.11f;
        public bool matchGlassWidth = true;

        [HideInInspector] public float cellSize = 0.506f;

        private GameObject _visualRoot;

        public float GridOriginX => boardCenter.x - cols * cellSize * 0.5f;
        public float GridOriginY => boardCenter.y - rows * cellSize * 0.5f;
        public float TopFrameInnerY => boardCenter.y + rows * cellSize * 0.5f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        public void Configure(int gridCols, int gridRows, ShapedGlassContainer alignGlass = null)
        {
            cols = Mathf.Clamp(gridCols, 3, 6);
            rows = Mathf.Clamp(gridRows, 3, 5);

            ShapedGlassContainer glass = alignGlass ?? FindFirstObjectByType<ShapedGlassContainer>();

            float targetBoardWidth = maxBoardWidth;
            if (matchGlassWidth && glass != null && glass.TryGetVisibleFrameWorldBounds(out float gMinX, out float gMaxX, out _, out _))
            {
                float glassOuterW = gMaxX - gMinX;
                if (glassOuterW > 1.0f) targetBoardWidth = glassOuterW;
            }

            float frameOverhead = innerMargin + framePadding * 2f;
            float usableW = Mathf.Max(1.2f, targetBoardWidth - frameOverhead);
            float usableH = Mathf.Max(1.0f, maxBoardHeight - frameOverhead);
            cellSize = Mathf.Clamp(Mathf.Min(usableW / cols, usableH / rows), 0.42f, 0.66f);

            if (glass != null)
            {
                AlignBelowGlass(glass);
            }

            BuildBoardVisuals();
        }

        /// <summary>
        /// Places the grid tray snugly beneath the upper glass container's visible frame bottom so there is no dead gap in the middle.
        /// </summary>
        public void AlignBelowGlass(ShapedGlassContainer glass)
        {
            if (glass == null) return;

            float glassCenterX = 0f;
            float glassBottomY = glass.transform.position.y - 0.68f;
            if (glass.TryGetVisibleFrameWorldBounds(out float gMinX, out float gMaxX, out float gMinY, out _))
            {
                glassCenterX = (gMinX + gMaxX) * 0.5f;
                glassBottomY = gMinY;
            }

            float innerH = rows * cellSize + innerMargin;
            float outerH = innerH + framePadding * 2f;
            float targetCenterY = glassBottomY - Mathf.Max(0.06f, gapBelowGlass) - outerH * 0.5f;

            // Keep well inside the bottom edge of orthographic portrait view
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                float camBottom = cam.transform.position.y - cam.orthographicSize;
                float minCenterY = camBottom + outerH * 0.5f + 0.18f;
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

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");

            // 1. Soft Caramel Warm Drop Shadow behind tray
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
                shMat.SetColor("_ShadowColor", new Color(0.68f, 0.56f, 0.44f, 0.34f));
                shMat.SetFloat("_FeatherWidth", 0.35f);
                shMat.SetFloat("_IsRadial", 0f);
                var shMr = shObj.GetComponent<MeshRenderer>();
                shMr.sharedMaterial = shMat;
                shMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shMr.receiveShadows = false;
            }

            // 2. Outer 3D Lower Lip & Glossy Periwinkle-Purple Frame (matches Image 1 + Image 2 palette)
            Material frameDarkMat = CreateUnlitMat(unlit, "Mat_GridFrameDark", new Color(0.30f, 0.31f, 0.72f, 1f));
            Material frameFrontMat = CreateUnlitMat(unlit, "Mat_GridFrameFront", new Color(0.49f, 0.52f, 0.94f, 1f));
            Material trayFloorMat = CreateUnlitMat(unlit, "Mat_GridTrayFloor", new Color(0.11f, 0.09f, 0.29f, 1f));
            Material tileBevelMat = CreateUnlitMat(unlit, "Mat_GridTileBevel", new Color(0.28f, 0.25f, 0.58f, 1f));
            Material tileBodyMat = CreateUnlitMat(unlit, "Mat_GridTileBody", new Color(0.19f, 0.17f, 0.44f, 1f));
            Material exitRimMat = CreateUnlitMat(unlit, "Mat_GridTopExitRim", new Color(0.68f, 0.80f, 1.00f, 0.90f));

            CreateRoundedPart("FrameLowerBevel", _visualRoot.transform, new Vector3(0f, -0.045f, 0.08f), outerW, outerH, 0.06f, 0.18f, frameDarkMat);
            CreateRoundedPart("FrameMain", _visualRoot.transform, new Vector3(0f, 0f, 0.05f), outerW, outerH, 0.06f, 0.18f, frameFrontMat);
            CreateRoundedPart("TrayRecessedBed", _visualRoot.transform, new Vector3(0f, 0f, 0.02f), innerW, innerH, 0.04f, 0.11f, trayFloorMat);

            // Top exit rail accent showing blocks pull UP toward the upper glass container
            CreateRoundedPart("TopExitHighlightBar", _visualRoot.transform, new Vector3(0f, innerH * 0.5f + framePadding * 0.50f, 0.00f), innerW * 0.90f, 0.038f, 0.02f, 0.018f, exitRimMat);

            // 3. Individual Rounded Recessed Grid Tiles (cols x rows)
            float tileOuter = cellSize * 0.89f;
            float tileInner = cellSize * 0.85f;
            float tileRad = cellSize * 0.14f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float lx = -gridW * 0.5f + (c + 0.5f) * cellSize;
                    float ly = -gridH * 0.5f + (r + 0.5f) * cellSize;

                    CreateRoundedPart($"CellBevel_{c}_{r}", _visualRoot.transform, new Vector3(lx, ly + 0.008f, 0.005f), tileOuter, tileOuter, 0.02f, tileRad, tileBevelMat);
                    CreateRoundedPart($"CellTile_{c}_{r}", _visualRoot.transform, new Vector3(lx, ly, -0.005f), tileInner, tileInner, 0.02f, tileRad * 0.9f, tileBodyMat);
                }
            }
        }

        private static Material CreateUnlitMat(Shader shader, string matName, Color col)
        {
            Material m = new Material(shader) { name = matName, color = col };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            return m;
        }

        private static void CreateRoundedPart(string partName, Transform parent, Vector3 localPos, float w, float h, float depth, float radius, Material mat)
        {
            GameObject obj = new GameObject(partName);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(w, h, depth, Mathf.Min(radius, Mathf.Min(w, h) * 0.48f));
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

        public bool CanPlaceBlockAt(MonsterCharacter movingBlock, Vector2Int candidateAnchor, List<MonsterCharacter> allMonsters)
        {
            CarrierBlockShape shape = movingBlock != null ? movingBlock.blockShape : CarrierBlockShape.Rect1x1;
            if (!IsInsideGrid(candidateAnchor, shape)) return false;

            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(shape);
            if (allMonsters == null) return true;

            for (int m = 0; m < allMonsters.Count; m++)
            {
                var other = allMonsters[m];
                if (other == null || other == movingBlock || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0)
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

            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(block.blockShape);
            for (int m = 0; m < allMonsters.Count; m++)
            {
                var other = allMonsters[m];
                if (other == null || other == block || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0)
                    continue;

                Vector2Int[] otherCells = CarrierBlockShapeUtility.GetCells(other.blockShape);
                for (int i = 0; i < myCells.Length; i++)
                {
                    int mx = block.gridAnchor.x + myCells[i].x;
                    int my = block.gridAnchor.y + myCells[i].y;
                    for (int j = 0; j < otherCells.Length; j++)
                    {
                        int ox = other.gridAnchor.x + otherCells[j].x;
                        int oy = other.gridAnchor.y + otherCells[j].y;
                        if (ox == mx && oy > my)
                        {
                            return false;
                        }
                    }
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

            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(block.blockShape);
            for (int i = 0; i < myCells.Length; i++)
            {
                int cx = block.gridAnchor.x + myCells[i].x;
                int cy = block.gridAnchor.y + myCells[i].y;

                // Evaluate only the topmost cell of this block in column cx
                bool hasOwnCellAbove = false;
                for (int k = 0; k < myCells.Length; k++)
                {
                    if (block.gridAnchor.x + myCells[k].x == cx && block.gridAnchor.y + myCells[k].y > cy)
                    {
                        hasOwnCellAbove = true;
                        break;
                    }
                }
                if (hasOwnCellAbove) continue;

                // Skip columns blocked above by another active grid block
                bool blockedAbove = false;
                if (allMonsters != null)
                {
                    for (int m = 0; m < allMonsters.Count; m++)
                    {
                        var other = allMonsters[m];
                        if (other == null || other == block || other.assignedSlot != null || other.IsCompleted || other.remainingCount <= 0)
                            continue;
                        Vector2Int[] otherCells = CarrierBlockShapeUtility.GetCells(other.blockShape);
                        for (int j = 0; j < otherCells.Length; j++)
                        {
                            if (other.gridAnchor.x + otherCells[j].x == cx && other.gridAnchor.y + otherCells[j].y > cy)
                            {
                                blockedAbove = true;
                                break;
                            }
                        }
                        if (blockedAbove) break;
                    }
                }
                if (blockedAbove) continue;

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
            if (allMonsters == null) return;
            for (int i = 0; i < allMonsters.Count; i++)
            {
                var m = allMonsters[i];
                if (m == null || m.assignedSlot != null || m.IsCompleted || m.remainingCount <= 0) continue;
                bool atTopExit = IsBlockAtTopExit(m);
                m.SetExitClearHighlight(atTopExit);
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
