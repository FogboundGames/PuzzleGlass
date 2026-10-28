using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    public class TrackRow
    {
        public int rowIndex;
        public float currentDistance;
        public float targetDistance;
        public PuzzleColor color;
        public List<GameObject> cubes = new List<GameObject>();
    }

    [ExecuteAlways]
    public class ShapedGlassContainer : MonoBehaviour
    {
        [Header("Materials")]
        public Material glassMaterial;
        public Material glassRimMaterial;
        public Material glassHighlightMaterial;
        public Material trackFloorMaterial;
        public Material hopperMaterial;

        [Header("Track Dimensions (Reference Image Birebir)")]
        public float ringCenterX = 0f;
        public float ringCenterY = 0.35f;
        public float radiusX = 1.55f;
        public float radiusY = 0.90f;
        public float trackWidth = 0.68f;
        public float glassWallHeight = 0.26f;
        public float rimRadius = 0.024f;

        [Header("Spout Exit Point")]
        public Transform spoutExitPoint;

        private GameObject _trackRootObj;
        private GameObject _glassWallsObj;
        private GameObject _cubesContainer;
        private Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();
        private Mesh _roundedCubeMesh;

        // Dynamic Conveyor Queue
        private List<TrackRow> _trackRows = new List<TrackRow>();
        private List<Vector3> _splineWaypoints = new List<Vector3>();
        private List<float> _cumulativeLengths = new List<float>();
        private float _totalSplineLength = 0f;
        private Coroutine _advanceRoutine;
        private float _rowSpacing = 0.20f;
        private float _colOffset = 0.21f;

        private void Awake()
        {
            // Always rebuild on Awake so all runtime references, spline points, and rows are fresh in Play mode
            RebuildContainer();
        }

        private void OnEnable()
        {
            if (_trackRows == null || _trackRows.Count == 0 || _cubesContainer == null || _splineWaypoints == null || _splineWaypoints.Count == 0)
            {
                RebuildContainer();
            }
        }

        [ContextMenu("Rebuild Container")]
        public void RebuildContainer()
        {
            if (_advanceRoutine != null)
            {
                StopCoroutine(_advanceRoutine);
                _advanceRoutine = null;
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }
            if (_trackRows == null) _trackRows = new List<TrackRow>();
            _trackRows.Clear();

            EnsureMaterials();
            BuildFullTrackCircuit();
        }

        private void EnsureMaterials()
        {
#if UNITY_EDITOR
            if (glassMaterial == null)
                glassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            if (glassRimMaterial == null)
                glassRimMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
#endif
            if (glassHighlightMaterial == null && glassRimMaterial != null)
                glassHighlightMaterial = glassRimMaterial;

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (glassMaterial == null)
            {
                glassMaterial = new Material(litShader);
                glassMaterial.name = "Mat_GlassPane";
                Color glassColor = new Color(0.75f, 0.90f, 1.0f, 0.14f); // Crystal clear light cyan
                glassMaterial.color = glassColor;
                if (glassMaterial.HasProperty("_BaseColor")) glassMaterial.SetColor("_BaseColor", glassColor);
                if (glassMaterial.HasProperty("_Smoothness")) glassMaterial.SetFloat("_Smoothness", 0.96f);
                if (glassMaterial.HasProperty("_Cull")) glassMaterial.SetFloat("_Cull", 0f); // Double-sided
            }

            if (glassRimMaterial == null)
            {
                glassRimMaterial = new Material(litShader);
                glassRimMaterial.name = "Mat_GlassRim";
                Color rimColor = new Color(0.70f, 0.88f, 1.0f, 0.95f); // Glowing sky-blue rim highlight
                glassRimMaterial.color = rimColor;
                if (glassRimMaterial.HasProperty("_BaseColor")) glassRimMaterial.SetColor("_BaseColor", rimColor);
                if (glassRimMaterial.HasProperty("_Smoothness")) glassRimMaterial.SetFloat("_Smoothness", 0.96f);
            }

            if (trackFloorMaterial == null)
            {
                trackFloorMaterial = new Material(litShader);
                trackFloorMaterial.name = "Mat_TrackFloor";
                Color floorColor = new Color(0.84f, 0.89f, 0.96f, 1f); // Smooth light periwinkle track surface
                trackFloorMaterial.color = floorColor;
                if (trackFloorMaterial.HasProperty("_BaseColor")) trackFloorMaterial.SetColor("_BaseColor", floorColor);
                if (trackFloorMaterial.HasProperty("_Smoothness")) trackFloorMaterial.SetFloat("_Smoothness", 0.50f);
            }

            if (hopperMaterial == null)
            {
                hopperMaterial = new Material(litShader);
                hopperMaterial.name = "Mat_Hopper";
                Color hopperColor = new Color(1.0f, 0.48f, 0.10f, 1f); // Warm tangerine hopper
                hopperMaterial.color = hopperColor;
                if (hopperMaterial.HasProperty("_BaseColor")) hopperMaterial.SetColor("_BaseColor", hopperColor);
                if (hopperMaterial.HasProperty("_Smoothness")) hopperMaterial.SetFloat("_Smoothness", 0.88f);
            }

            // Cube materials: Solid, matte/satin toy/candy blocks (no glassy sheen)
            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                if (!_cubeMaterials.ContainsKey(col))
                {
                    Material m = new Material(litShader);
                    m.name = $"Mat_PileCube_{col}";
                    Color c = PuzzleColorPalette.GetCubeColor(col);
                    m.color = c;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    _cubeMaterials[col] = m;
                }
                else
                {
                    Material m = _cubeMaterials[col];
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                }
            }

            if (_roundedCubeMesh == null)
            {
                _roundedCubeMesh = ProceduralMeshUtils.CreateRoundedCube(0.20f, 0.045f, 3);
            }
        }

        private void BuildFullTrackCircuit()
        {
            // 1. Root container tilted in 3D for authentic isometric 2.5D perspective
            _trackRootObj = new GameObject("GlassTrackRoot");
            _trackRootObj.transform.SetParent(transform, false);
            _trackRootObj.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            _trackRootObj.transform.localRotation = Quaternion.Euler(36f, 0f, 0f);

            _glassWallsObj = new GameObject("GlassWalls");
            _glassWallsObj.transform.SetParent(_trackRootObj.transform, false);

            _cubesContainer = new GameObject("CubesOnTrack");
            _cubesContainer.transform.SetParent(_trackRootObj.transform, false);

            // 2. Build Spout Exit Point at the physical mouth of the glass chute
            GameObject spoutObj = new GameObject("SpoutExitPoint");
            spoutObj.transform.SetParent(_trackRootObj.transform, false);
            spoutObj.transform.localPosition = new Vector3(0f, -1.18f, -0.06f);
            spoutExitPoint = spoutObj.transform;

            // 3. Generate Track Spline & Glass Boundaries
            BuildSpline();

            // Inner Island Ring
            Vector3[] innerPath = GenerateInnerIslandPath(36);
            BuildGlassBarrier(innerPath, true, "InnerIslandWall");

            // Outer Perimeter Paths
            GenerateOuterPaths(out Vector3[] outerLeftPath, out Vector3[] outerRightPath,
                               out Vector3[] exitLeftPath, out Vector3[] exitRightPath,
                               out Vector3[] chuteLeftPath, out Vector3[] chuteRightPath);

            BuildGlassBarrier(outerLeftPath, false, "OuterWall_Left");
            BuildGlassBarrier(outerRightPath, false, "OuterWall_Right");
            BuildGlassBarrier(exitLeftPath, false, "ExitWall_Left");
            BuildGlassBarrier(exitRightPath, false, "ExitWall_Right");
            BuildGlassBarrier(chuteLeftPath, false, "EntranceChute_Left");
            BuildGlassBarrier(chuteRightPath, false, "EntranceChute_Right");

            // 4. Track Floor
            BuildTrackFloor();

            // 5. Orange Dispenser Hopper at top right
            BuildOrangeHopper();

            // 6. Spawn the Dynamic Conveyor Cube Train
            PopulateConveyorTrain();
        }

        private void BuildSpline()
        {
            List<Vector3> keyPoints = new List<Vector3>
            {
                new Vector3(2.05f, 1.70f, 0f),   // 0: Inside Orange Hopper
                new Vector3(1.75f, 1.35f, 0f),   // 1: Hopper mouth
                new Vector3(1.42f, 1.05f, 0f),   // 2: Feeder chute mid
                new Vector3(1.08f, 0.72f, 0f),   // 3: Chute meets ring
                new Vector3(0.70f, 0.95f, 0f),   // 4: Upper ring
                new Vector3(0.00f, 1.10f, 0f),   // 5: Top center of ring
                new Vector3(-0.75f, 0.95f, 0f),  // 6: Upper-left ring
                new Vector3(-1.22f, 0.40f, 0f),  // 7: Left apex of ring
                new Vector3(-1.12f, -0.15f, 0f), // 8: Lower-left ring
                new Vector3(-0.65f, -0.55f, 0f), // 9: Approaching bottom exit
                new Vector3(0.00f, -0.65f, 0f),  // 10: Exit chute entry
                new Vector3(0.00f, -0.90f, 0f),  // 11: Exit chute mid
                new Vector3(0.00f, -1.18f, 0f)   // 12: Exit chute opening (spout)
            };

            if (_splineWaypoints == null) _splineWaypoints = new List<Vector3>();
            if (_cumulativeLengths == null) _cumulativeLengths = new List<float>();

            _splineWaypoints.Clear();
            _cumulativeLengths.Clear();

            int n = keyPoints.Count;
            int subdivisions = 12;

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = keyPoints[Mathf.Max(0, i - 1)];
                Vector3 p1 = keyPoints[i];
                Vector3 p2 = keyPoints[i + 1];
                Vector3 p3 = keyPoints[Mathf.Min(n - 1, i + 2)];

                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions;
                    Vector3 pt = CatmullRom(p0, p1, p2, p3, t);
                    _splineWaypoints.Add(pt);
                }
            }
            _splineWaypoints.Add(keyPoints[n - 1]);

            _totalSplineLength = 0f;
            _cumulativeLengths.Add(0f);
            for (int i = 1; i < _splineWaypoints.Count; i++)
            {
                float d = Vector3.Distance(_splineWaypoints[i], _splineWaypoints[i - 1]);
                _totalSplineLength += d;
                _cumulativeLengths.Add(_totalSplineLength);
            }
        }

        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public Vector3 EvaluateSpline(float distance, out Vector3 tangent, out Vector3 normal)
        {
            if (_splineWaypoints == null || _splineWaypoints.Count < 2 || _cumulativeLengths == null || _cumulativeLengths.Count < 2)
            {
                BuildSpline();
            }

            distance = Mathf.Clamp(distance, 0f, _totalSplineLength);

            int low = 0;
            int high = _cumulativeLengths.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (_cumulativeLengths[mid] < distance) low = mid + 1;
                else high = mid - 1;
            }

            int idx = Mathf.Clamp(low - 1, 0, _splineWaypoints.Count - 2);
            float segStart = _cumulativeLengths[idx];
            float segEnd = _cumulativeLengths[idx + 1];
            float segLen = segEnd - segStart;
            float t = segLen > 0.0001f ? (distance - segStart) / segLen : 0f;

            Vector3 pA = _splineWaypoints[idx];
            Vector3 pB = _splineWaypoints[idx + 1];
            Vector3 pos = Vector3.Lerp(pA, pB, t);

            tangent = (pB - pA).normalized;
            normal = new Vector3(-tangent.y, tangent.x, 0f).normalized;
            return pos;
        }

        private Vector3[] GenerateInnerIslandPath(int segments)
        {
            float innerRx = radiusX - trackWidth;
            float innerRy = radiusY - trackWidth * 0.75f;

            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = ringCenterX + Mathf.Cos(angle) * innerRx;
                float y = ringCenterY + Mathf.Sin(angle) * innerRy;
                pts.Add(new Vector3(x, y, 0f));
            }
            return pts.ToArray();
        }

        private void GenerateOuterPaths(
            out Vector3[] outerLeft, out Vector3[] outerRight,
            out Vector3[] exitLeft, out Vector3[] exitRight,
            out Vector3[] chuteLeft, out Vector3[] chuteRight)
        {
            float halfW = trackWidth * 0.5f;

            float exitTopY = ringCenterY - radiusY;
            float exitBottomY = -1.15f;
            exitLeft = new Vector3[] {
                new Vector3(-halfW, exitTopY, 0f),
                new Vector3(-halfW, exitBottomY, 0f)
            };
            exitRight = new Vector3[] {
                new Vector3(halfW, exitTopY, 0f),
                new Vector3(halfW, exitBottomY, 0f)
            };

            chuteLeft = new Vector3[] {
                new Vector3(radiusX * 0.65f, ringCenterY + radiusY * 0.85f, 0f),
                new Vector3(radiusX * 0.90f, ringCenterY + radiusY * 1.35f, 0f),
                new Vector3(radiusX * 1.25f, ringCenterY + radiusY * 1.75f, 0f)
            };
            chuteRight = new Vector3[] {
                new Vector3(radiusX * 0.95f, ringCenterY + radiusY * 0.55f, 0f),
                new Vector3(radiusX * 1.20f, ringCenterY + radiusY * 1.05f, 0f),
                new Vector3(radiusX * 1.55f, ringCenterY + radiusY * 1.45f, 0f)
            };

            List<Vector3> leftWall = new List<Vector3>();
            leftWall.Add(exitLeft[0]);

            int stepsLeft = 24;
            for (int i = 1; i <= stepsLeft; i++)
            {
                float t = (float)i / stepsLeft;
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.55f, t);
                float x = ringCenterX + Mathf.Cos(angle) * radiusX;
                float y = ringCenterY + Mathf.Sin(angle) * radiusY;
                leftWall.Add(new Vector3(x, y, 0f));
            }
            leftWall.Add(chuteLeft[0]);
            outerLeft = leftWall.ToArray();

            List<Vector3> rightWall = new List<Vector3>();
            rightWall.Add(exitRight[0]);

            int stepsRight = 16;
            for (int i = 1; i <= stepsRight; i++)
            {
                float t = (float)i / stepsRight;
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.18f, t);
                float x = ringCenterX + Mathf.Cos(angle) * radiusX;
                float y = ringCenterY + Mathf.Sin(angle) * radiusY;
                rightWall.Add(new Vector3(x, y, 0f));
            }
            rightWall.Add(chuteRight[0]);
            outerRight = rightWall.ToArray();
        }

        private void BuildGlassBarrier(Vector3[] path, bool isClosed, string name)
        {
            if (path == null || path.Length < 2) return;

            GameObject barrierObj = new GameObject(name);
            barrierObj.transform.SetParent(_glassWallsObj.transform, false);

            Vector3 wallNormal = new Vector3(0f, 0f, -1f);

            Mesh paneMesh = ProceduralMeshUtils.CreateGlassRibbonWall(path, glassWallHeight, wallNormal, isClosed);
            MeshFilter mfPane = barrierObj.AddComponent<MeshFilter>();
            MeshRenderer mrPane = barrierObj.AddComponent<MeshRenderer>();
            mfPane.sharedMesh = paneMesh;
            mrPane.sharedMaterial = glassMaterial;

            Vector3[] topPath = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                topPath[i] = path[i] + wallNormal * glassWallHeight;
            }

            GameObject rimObj = new GameObject($"{name}_Rim");
            rimObj.transform.SetParent(barrierObj.transform, false);
            MeshFilter mfRim = rimObj.AddComponent<MeshFilter>();
            MeshRenderer mrRim = rimObj.AddComponent<MeshRenderer>();
            mfRim.sharedMesh = ProceduralMeshUtils.CreatePipeMesh(topPath, rimRadius, 12, isClosed);
            mrRim.sharedMaterial = glassRimMaterial;
        }

        private void BuildTrackFloor()
        {
            GameObject floorObj = new GameObject("TrackFloorMesh");
            floorObj.transform.SetParent(_trackRootObj.transform, false);
            floorObj.transform.localPosition = new Vector3(0f, 0f, 0.005f);

            int segments = 40;
            Vector3[] innerLoop = new Vector3[segments + 1];
            Vector3[] outerLoop = new Vector3[segments + 1];

            float innerRx = radiusX - trackWidth;
            float innerRy = radiusY - trackWidth * 0.75f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                innerLoop[i] = new Vector3(ringCenterX + Mathf.Cos(angle) * innerRx, ringCenterY + Mathf.Sin(angle) * innerRy, 0f);
                outerLoop[i] = new Vector3(ringCenterX + Mathf.Cos(angle) * radiusX, ringCenterY + Mathf.Sin(angle) * radiusY, 0f);
            }

            Mesh floorMesh = ProceduralMeshUtils.CreateTrackFloorRibbon(innerLoop, outerLoop);
            floorObj.AddComponent<MeshFilter>().sharedMesh = floorMesh;
            floorObj.AddComponent<MeshRenderer>().sharedMaterial = trackFloorMaterial;

            GameObject exitFloorObj = new GameObject("ExitChuteFloor");
            exitFloorObj.transform.SetParent(floorObj.transform, false);
            float halfW = trackWidth * 0.5f;
            float exitTopY = ringCenterY - radiusY;
            float exitBottomY = -1.15f;
            Vector3[] leftExit = new Vector3[] { new Vector3(-halfW, exitTopY, 0f), new Vector3(-halfW, exitBottomY, 0f) };
            Vector3[] rightExit = new Vector3[] { new Vector3(halfW, exitTopY, 0f), new Vector3(halfW, exitBottomY, 0f) };
            exitFloorObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreateTrackFloorRibbon(leftExit, rightExit);
            exitFloorObj.AddComponent<MeshRenderer>().sharedMaterial = trackFloorMaterial;
        }

        private void BuildOrangeHopper()
        {
            GameObject hopper = new GameObject("OrangeHopper");
            hopper.transform.SetParent(_trackRootObj.transform, false);
            hopper.transform.localPosition = new Vector3(radiusX * 1.45f, ringCenterY + radiusY * 1.68f, -glassWallHeight * 0.5f);
            hopper.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            hopper.transform.localScale = new Vector3(trackWidth * 1.25f, 0.45f, glassWallHeight * 1.4f);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "HopperBody";
            box.transform.SetParent(hopper.transform, false);
            box.GetComponent<MeshRenderer>().sharedMaterial = hopperMaterial;
            DestroyCollider(box);
        }

        /// <summary>
        /// Populates the continuous dynamic conveyor train with exact color blocks matching the reference image:
        /// 1. Exit chute: Purple (front rows)
        /// 2. Left side: Pink (middle rows)
        /// 3. Upper left/top: Orange
        /// 4. Feeder chute: Green (top-right emerging from hopper)
        /// </summary>
        /// <summary>
        /// Populates the continuous dynamic conveyor train with colors matching the active monsters and reference image.
        /// </summary>
        private void PopulateConveyorTrain()
        {
            _trackRows.Clear();

            int totalRows = 28;
            float frontDistance = _totalSplineLength - 0.05f;

            // Check active monster colors to ensure matching cubes are present in the track
            List<PuzzleColor> activeMonsterColors = new List<PuzzleColor>();
            PuzzleGlassGameManager gm = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm != null && gm.activeMonsters != null)
            {
                for (int m = 0; m < gm.activeMonsters.Count; m++)
                {
                    var mon = gm.activeMonsters[m];
                    if (mon != null && !activeMonsterColors.Contains(mon.colorType))
                    {
                        activeMonsterColors.Add(mon.colorType);
                    }
                }
            }

            for (int r = 0; r < totalRows; r++)
            {
                PuzzleColor rowColor;
                if (activeMonsterColors.Count >= 2)
                {
                    if (r < 6) rowColor = activeMonsterColors[0];
                    else if (r < 15) rowColor = activeMonsterColors[1];
                    else if (r < 21) rowColor = (activeMonsterColors.Count >= 3) ? activeMonsterColors[2] : PuzzleColor.Orange;
                    else rowColor = (activeMonsterColors.Count >= 4) ? activeMonsterColors[3] : PuzzleColor.Green;
                }
                else if (activeMonsterColors.Count == 1)
                {
                    if (r < 12) rowColor = activeMonsterColors[0];
                    else if (r < 18) rowColor = PuzzleColor.Orange;
                    else rowColor = PuzzleColor.Green;
                }
                else
                {
                    // Default Reference layout: Purple at chute, Pink on left curve, Orange on top, Green from hopper
                    if (r < 5) rowColor = PuzzleColor.Purple;
                    else if (r < 14) rowColor = PuzzleColor.Pink;
                    else if (r < 20) rowColor = PuzzleColor.Orange;
                    else rowColor = PuzzleColor.Green;
                }

                float dist = frontDistance - r * _rowSpacing;

                TrackRow row = new TrackRow
                {
                    rowIndex = r,
                    currentDistance = dist,
                    targetDistance = dist,
                    color = rowColor
                };

                int cubeCount = (r == 0) ? 2 : 3;
                int[] cols = (cubeCount == 2) ? new int[] { -1, 1 } : new int[] { -1, 0, 1 };

                Vector3 centerPt = EvaluateSpline(dist, out Vector3 tangent, out Vector3 normal);
                float angleZ = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

                foreach (int col in cols)
                {
                    Vector3 localPos = centerPt + normal * (col * _colOffset) + new Vector3(0, 0, -0.10f);
                    GameObject cube = SpawnConveyorCube(localPos, rowColor, angleZ);
                    row.cubes.Add(cube);
                }

                _trackRows.Add(row);
            }

            SpawnLowerRightBranch();
        }

        private void SpawnLowerRightBranch()
        {
            float midRx = radiusX - trackWidth * 0.5f;
            float midRy = radiusY - trackWidth * 0.38f;

            for (int r = 0; r < 4; r++)
            {
                float t = (float)r / 3;
                float angle = Mathf.Lerp(1.58f * Mathf.PI, 1.88f * Mathf.PI, t);

                float cx = ringCenterX + Mathf.Cos(angle) * midRx;
                float cy = ringCenterY + Mathf.Sin(angle) * midRy;
                Vector3 centerPos = new Vector3(cx, cy, 0f);

                Vector3 tangent = new Vector3(-Mathf.Sin(angle) * midRx, Mathf.Cos(angle) * midRy, 0f).normalized;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f).normalized;
                float angleZ = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

                for (int col = -1; col <= 1; col++)
                {
                    Vector3 pos = centerPos + radial * (col * _colOffset) + new Vector3(0, 0, -0.10f);
                    SpawnConveyorCube(pos, PuzzleColor.Purple, angleZ);
                }
            }
        }

        private GameObject SpawnConveyorCube(Vector3 localPos, PuzzleColor col, float angleZ)
        {
            EnsureMaterials();

            GameObject cube = new GameObject($"TrackCube_{col}");
            if (_cubesContainer != null)
            {
                cube.transform.SetParent(_cubesContainer.transform, false);
            }
            else if (_trackRootObj != null)
            {
                cube.transform.SetParent(_trackRootObj.transform, false);
            }
            else
            {
                cube.transform.SetParent(transform, false);
            }

            cube.transform.localPosition = localPos;
            cube.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);

            MeshFilter mf = cube.AddComponent<MeshFilter>();
            mf.sharedMesh = _roundedCubeMesh;

            MeshRenderer mr = cube.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _cubeMaterials[col];

            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return cube;
        }

        /// <summary>
        /// Consumes the front cube from the exit chute, launches it as a FULL-SIZE (0.28f) chunky flying candy cube,
        /// and smoothly advances all remaining cubes along the track!
        /// </summary>
        public bool ConsumeAndLaunchCube(MonsterCharacter targetMonster, PuzzleColor color, Material cubeMat, Mesh cubeMesh)
        {
            // Safety check: ensure runtime track queue is initialized
            if (_trackRows == null || _trackRows.Count == 0 || _cubesContainer == null || _splineWaypoints == null || _splineWaypoints.Count == 0)
            {
                RebuildContainer();
            }

            GameObject targetCube = null;
            float startDist = -1f;

            // 1. Search in active track rows starting from the frontmost row at the exit
            for (int i = 0; i < _trackRows.Count; i++)
            {
                var row = _trackRows[i];
                if (row == null) continue;

                // Remove any destroyed references
                for (int c = row.cubes.Count - 1; c >= 0; c--)
                {
                    if (row.cubes[c] == null) row.cubes.RemoveAt(c);
                }

                if (row.color == color && row.cubes.Count > 0)
                {
                    targetCube = row.cubes[row.cubes.Count - 1];
                    row.cubes.RemoveAt(row.cubes.Count - 1);
                    startDist = row.currentDistance;
                    break;
                }
            }

            // 2. Fallback search inside container children if row had no match
            if (targetCube == null && _cubesContainer != null)
            {
                string match = $"TrackCube_{color}";
                for (int i = 0; i < _cubesContainer.transform.childCount; i++)
                {
                    Transform t = _cubesContainer.transform.GetChild(i);
                    if (t != null && t.gameObject != null && t.name.Contains(match))
                    {
                        targetCube = t.gameObject;
                        startDist = _totalSplineLength * 0.5f;
                        break;
                    }
                }
            }

            Vector3 spoutWorldPos = GetSpoutExitWorldPosition();

            if (targetCube != null)
            {
                Vector3 cubeWorldPos = targetCube.transform.position;

                // Build physics-compliant path strictly INSIDE the glass track leading to the spout exit
                float endDist = _totalSplineLength;
                if (startDist < 0f) startDist = _totalSplineLength - 0.20f;

                int numSamples = Mathf.Clamp(Mathf.RoundToInt((endDist - startDist) / 0.14f), 2, 14);
                List<Vector3> waypoints = new List<Vector3>(numSamples + 2);
                waypoints.Add(cubeWorldPos);

                for (int s = 1; s <= numSamples; s++)
                {
                    float t = (float)s / (numSamples + 1);
                    float d = Mathf.Lerp(startDist, endDist, t);
                    Vector3 splinePt = EvaluateSpline(d, out Vector3 tangent, out Vector3 normal);
                    Vector3 localTrackPt = splinePt + new Vector3(0f, 0f, -0.06f);
                    waypoints.Add(_trackRootObj.transform.TransformPoint(localTrackPt));
                }
                waypoints.Add(spoutWorldPos);

                // Detach from track hierarchy so conveyor animation won't interfere
                targetCube.transform.SetParent(null, true);
                targetCube.name = $"FlyingCube_{color}_To_{targetMonster.monsterId}";

                FlyingCube flying = targetCube.GetComponent<FlyingCube>();
                if (flying == null) flying = targetCube.AddComponent<FlyingCube>();
                flying.LaunchThroughGlassTrack(waypoints.ToArray(), spoutWorldPos, targetMonster, color, cubeMat, cubeMesh);

                PulseSpout();

                // Advance remaining conveyor train forward towards the exit chute!
                AdvanceTrackTrain();

                return true;
            }
            else
            {
                // Fallback: spawn cube at top-right hopper entrance inside the glass track
                float sDist = 0f;
                float eDist = _totalSplineLength;
                int numSamples = 14;
                List<Vector3> waypoints = new List<Vector3>(numSamples + 1);

                for (int s = 0; s <= numSamples; s++)
                {
                    float t = (float)s / numSamples;
                    float d = Mathf.Lerp(sDist, eDist, t);
                    Vector3 splinePt = EvaluateSpline(d, out Vector3 tangent, out Vector3 normal);
                    Vector3 localTrackPt = splinePt + new Vector3(0f, 0f, -0.06f);
                    waypoints.Add(_trackRootObj.transform.TransformPoint(localTrackPt));
                }

                GameObject cubeObj = new GameObject($"DispensedCube_{color}_To_{targetMonster.monsterId}");
                cubeObj.transform.position = waypoints[0];
                cubeObj.transform.localScale = Vector3.one;

                FlyingCube flying = cubeObj.AddComponent<FlyingCube>();
                flying.LaunchThroughGlassTrack(waypoints.ToArray(), spoutWorldPos, targetMonster, color, cubeMat, cubeMesh);

                PulseSpout();
                AdvanceTrackTrain();
                return true;
            }
        }

        public Vector3 GetSpoutExitWorldPosition()
        {
            if (spoutExitPoint != null)
            {
                return spoutExitPoint.position;
            }
            if (_trackRootObj != null)
            {
                return _trackRootObj.transform.TransformPoint(new Vector3(0f, -1.18f, -0.06f));
            }
            return transform.position + new Vector3(0f, -0.85f, -0.70f);
        }

        public void AdvanceTrackTrain()
        {
            if (!gameObject.activeInHierarchy || _trackRows == null || _trackRows.Count == 0) return;

            // Remove any completely empty rows
            for (int i = _trackRows.Count - 1; i >= 0; i--)
            {
                var row = _trackRows[i];
                if (row == null || row.cubes == null || row.cubes.Count == 0)
                {
                    _trackRows.RemoveAt(i);
                }
            }

            float advanceStep = _rowSpacing * 0.45f;

            for (int i = 0; i < _trackRows.Count; i++)
            {
                var row = _trackRows[i];
                if (row == null) continue;

                float startDist = row.currentDistance;
                float maxDist = _totalSplineLength - 0.05f - (i * _rowSpacing);
                float endDist = Mathf.Min(startDist + advanceStep, maxDist);

                DOTween.To(() => row.currentDistance, x =>
                {
                    row.currentDistance = x;
                    UpdateRowCubesPosition(row);
                }, endDist, 0.10f).SetEase(Ease.OutQuad);
            }
        }

        private void UpdateRowCubesPosition(TrackRow row)
        {
            if (row == null || row.cubes == null) return;
            Vector3 centerPt = EvaluateSpline(row.currentDistance, out Vector3 tangent, out Vector3 normal);
            float angleZ = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

            int count = row.cubes.Count;
            for (int c = 0; c < count; c++)
            {
                GameObject cube = row.cubes[c];
                if (cube != null)
                {
                    float col = (count == 2) ? (c == 0 ? -1f : 1f) : (c - 1f);
                    Vector3 pos = centerPt + normal * (col * _colOffset) + new Vector3(0, 0, -0.10f);
                    cube.transform.localPosition = pos;
                    cube.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
                }
            }
        }

        public void ConsumeCube(PuzzleColor color)
        {
            AdvanceTrackTrain();
        }

        public void PulseSpout()
        {
            if (!gameObject.activeInHierarchy) return;

            if (spoutExitPoint != null)
            {
                spoutExitPoint.DOKill(true);
                spoutExitPoint.DOPunchScale(new Vector3(0.04f, -0.06f, 0.04f), 0.08f, 5, 0.5f);
            }

            if (_glassWallsObj != null)
            {
                _glassWallsObj.transform.DOKill(true);
                _glassWallsObj.transform.DOPunchScale(new Vector3(0.012f, 0.012f, 0.012f), 0.08f, 4, 0.5f);
            }
            else if (_trackRootObj != null)
            {
                _trackRootObj.transform.DOKill(true);
                _trackRootObj.transform.DOPunchScale(new Vector3(0.012f, 0.012f, 0.012f), 0.08f, 4, 0.5f);
            }
        }

        private void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }
    }
}
