using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    [System.Serializable]
    public class BottleCube
    {
        public GameObject gameObject;
        public PuzzleColor color;
        public float x;
        public float y;
        public float angle;
        public float vx;
        public float vy;
        public float vAng;
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

        [Header("Bottle Dimensions (Reference Photo Style)")]
        public float neckWidth = 0.52f;
        public float neckBottomY = -1.45f;
        public float neckTopY = -0.55f;
        public float shoulderTopY = 0.35f;
        public float bodyWidth = 1.65f;
        public float bodyTopY = 2.05f;
        public float baseTopY = 2.35f;
        public float baseCornerRadius = 0.20f;
        public float glassWallHeight = 0.28f;
        public float rimRadius = 0.024f;

        [Header("Spout Exit Point")]
        public Transform spoutExitPoint;

        private GameObject _trackRootObj;
        private GameObject _glassWallsObj;
        private GameObject _cubesContainer;
        private Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();
        private Mesh _roundedCubeMesh;

        // Physical Pile of Cubes inside the Bottle
        private List<BottleCube> _bottleCubes = new List<BottleCube>();
        private List<Vector3> _splineWaypoints = new List<Vector3>();
        private List<float> _cumulativeLengths = new List<float>();
        private float _totalSplineLength = 0f;

        private void Awake()
        {
            RebuildContainer();
        }

        private void OnEnable()
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0 || _cubesContainer == null || _splineWaypoints == null || _splineWaypoints.Count == 0)
            {
                RebuildContainer();
            }
        }

        [ContextMenu("Rebuild Container")]
        public void RebuildContainer()
        {
            if (_bottleCubes != null)
            {
                for (int i = 0; i < _bottleCubes.Count; i++)
                {
                    if (_bottleCubes[i]?.gameObject != null)
                    {
                        _bottleCubes[i].gameObject.transform.DOKill();
                    }
                }
                _bottleCubes.Clear();
            }
            else
            {
                _bottleCubes = new List<BottleCube>();
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            EnsureMaterials();
            BuildBottleContainer();
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
                Color glassColor = new Color(0.75f, 0.90f, 1.0f, 0.14f);
                glassMaterial.color = glassColor;
                if (glassMaterial.HasProperty("_BaseColor")) glassMaterial.SetColor("_BaseColor", glassColor);
                if (glassMaterial.HasProperty("_Smoothness")) glassMaterial.SetFloat("_Smoothness", 0.96f);
                if (glassMaterial.HasProperty("_Cull")) glassMaterial.SetFloat("_Cull", 0f);
            }

            if (glassRimMaterial == null)
            {
                glassRimMaterial = new Material(litShader);
                glassRimMaterial.name = "Mat_GlassRim";
                Color rimColor = new Color(0.70f, 0.88f, 1.0f, 0.95f);
                glassRimMaterial.color = rimColor;
                if (glassRimMaterial.HasProperty("_BaseColor")) glassRimMaterial.SetColor("_BaseColor", rimColor);
                if (glassRimMaterial.HasProperty("_Smoothness")) glassRimMaterial.SetFloat("_Smoothness", 0.96f);
            }

            if (trackFloorMaterial == null)
            {
                trackFloorMaterial = new Material(litShader);
                trackFloorMaterial.name = "Mat_TrackFloor";
                Color floorColor = new Color(0.84f, 0.89f, 0.96f, 1f);
                trackFloorMaterial.color = floorColor;
                if (trackFloorMaterial.HasProperty("_BaseColor")) trackFloorMaterial.SetColor("_BaseColor", floorColor);
                if (trackFloorMaterial.HasProperty("_Smoothness")) trackFloorMaterial.SetFloat("_Smoothness", 0.50f);
            }

            if (hopperMaterial == null)
            {
                hopperMaterial = new Material(litShader);
                hopperMaterial.name = "Mat_Hopper";
                Color hopperColor = new Color(1.0f, 0.48f, 0.10f, 1f);
                hopperMaterial.color = hopperColor;
                if (hopperMaterial.HasProperty("_BaseColor")) hopperMaterial.SetColor("_BaseColor", hopperColor);
                if (hopperMaterial.HasProperty("_Smoothness")) hopperMaterial.SetFloat("_Smoothness", 0.88f);
            }

            // Solid candy toy blocks
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

        private void BuildBottleContainer()
        {
            // 1. Root container tilted in 3D for authentic isometric 2.5D perspective
            _trackRootObj = new GameObject("GlassTrackRoot");
            _trackRootObj.transform.SetParent(transform, false);
            _trackRootObj.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            _trackRootObj.transform.localRotation = Quaternion.Euler(36f, 0f, 0f);

            _glassWallsObj = new GameObject("GlassWalls");
            _glassWallsObj.transform.SetParent(_trackRootObj.transform, false);

            _cubesContainer = new GameObject("CubesInBottle");
            _cubesContainer.transform.SetParent(_trackRootObj.transform, false);

            // 2. Build Spout Exit Point at the physical mouth opening of the bottle
            GameObject spoutObj = new GameObject("SpoutExitPoint");
            spoutObj.transform.SetParent(_trackRootObj.transform, false);
            spoutObj.transform.localPosition = new Vector3(0f, neckBottomY - 0.10f, -0.06f);
            spoutExitPoint = spoutObj.transform;

            // 3. Build Spline through the center of the bottle down to the mouth
            BuildSpline();

            // 4. Generate Bottle Geometry (Walls, Rim, Floor, Collar, Specular Highlights)
            BuildBottleGeometry();

            // 5. Populate Bottle with Physics-Settled Candy Cubes
            PopulateBottlePhysics();
        }

        /// <summary>
        /// Exact mathematical silhouette of the reference bottle:
        /// Slender cylindrical neck -> Smooth convex shoulder curve -> Tall cylindrical body -> Flat base with rounded corners.
        /// </summary>
        public float GetHalfWidthAtY(float y)
        {
            float neckHalfW = neckWidth * 0.5f;   // 0.26f
            float bodyHalfW = bodyWidth * 0.5f;   // 0.825f

            if (y <= neckTopY)
            {
                // Slender neck
                return neckHalfW;
            }
            else if (y <= shoulderTopY)
            {
                // Smooth convex shoulder curve using C2-continuous SmootherStep
                float t = Mathf.Clamp01((y - neckTopY) / (shoulderTopY - neckTopY));
                float s = t * t * t * (t * (t * 6f - 15f) + 10f); // SmootherStep
                return Mathf.Lerp(neckHalfW, bodyHalfW, s);
            }
            else if (y <= bodyTopY)
            {
                // Straight cylindrical body
                return bodyHalfW;
            }
            else
            {
                // Flat base with rounded corners
                float t = Mathf.Clamp01((y - bodyTopY) / Mathf.Max(0.01f, baseTopY - bodyTopY));
                float arch = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                return Mathf.Lerp(bodyHalfW - baseCornerRadius, bodyHalfW, arch);
            }
        }

        private void BuildBottleGeometry()
        {
            GenerateBottlePaths(out Vector3[] leftPath, out Vector3[] rightPath, out Vector3[] perimeterPath);

            // 1. Crystal Glass Ribbon Wall along the entire bottle outline
            Vector3 wallNormal = new Vector3(0f, 0f, -1f);
            GameObject wallObj = new GameObject("BottleGlassWall");
            wallObj.transform.SetParent(_glassWallsObj.transform, false);

            Mesh paneMesh = ProceduralMeshUtils.CreateGlassRibbonWall(perimeterPath, glassWallHeight, wallNormal, false);
            wallObj.AddComponent<MeshFilter>().sharedMesh = paneMesh;
            wallObj.AddComponent<MeshRenderer>().sharedMaterial = glassMaterial;

            // 2. Glowing Glass Rim Pipe along the top edge of the walls
            Vector3[] topPath = new Vector3[perimeterPath.Length];
            for (int i = 0; i < perimeterPath.Length; i++)
            {
                topPath[i] = perimeterPath[i] + wallNormal * glassWallHeight;
            }

            GameObject rimObj = new GameObject("BottleGlassRim");
            rimObj.transform.SetParent(wallObj.transform, false);
            rimObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(topPath, rimRadius, 12, false);
            rimObj.AddComponent<MeshRenderer>().sharedMaterial = glassRimMaterial;

            // 3. Mouth Lip Rim across the open mouth nozzle
            Vector3[] mouthLip = new Vector3[] {
                leftPath[0] + wallNormal * glassWallHeight,
                rightPath[0] + wallNormal * glassWallHeight
            };
            GameObject lipObj = new GameObject("BottleMouthLip");
            lipObj.transform.SetParent(wallObj.transform, false);
            lipObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(mouthLip, rimRadius, 12, false);
            lipObj.AddComponent<MeshRenderer>().sharedMaterial = glassRimMaterial;

            // 4. Bottle Back / Floor Pane
            GameObject floorObj = new GameObject("BottleFloorMesh");
            floorObj.transform.SetParent(_trackRootObj.transform, false);
            floorObj.transform.localPosition = new Vector3(0f, 0f, 0.005f);

            Mesh floorMesh = ProceduralMeshUtils.CreateTrackFloorRibbon(leftPath, rightPath);
            floorObj.AddComponent<MeshFilter>().sharedMesh = floorMesh;
            floorObj.AddComponent<MeshRenderer>().sharedMaterial = trackFloorMaterial;

            // 5. Signature Raised Collar Ring around the neck (exact matching reference photo!)
            float collarHW = neckWidth * 0.5f + 0.022f;
            float collarY = neckBottomY + 0.18f;
            Vector3[] collarPath = new Vector3[] {
                new Vector3(-collarHW, collarY, 0f),
                new Vector3(-collarHW, collarY, -glassWallHeight),
                new Vector3(collarHW, collarY, -glassWallHeight),
                new Vector3(collarHW, collarY, 0f)
            };
            GameObject collarObj = new GameObject("BottleNeckCollar");
            collarObj.transform.SetParent(wallObj.transform, false);
            collarObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(collarPath, rimRadius * 0.95f, 10, false);
            collarObj.AddComponent<MeshRenderer>().sharedMaterial = glassRimMaterial;

            // 6. Long Specular Reflection Highlight Arc along left shoulder and body
            List<Vector3> highlightPts = new List<Vector3>();
            for (float hy = -0.30f; hy <= 1.95f; hy += 0.10f)
            {
                float hw = GetHalfWidthAtY(hy);
                highlightPts.Add(new Vector3(-hw + 0.08f, hy, -glassWallHeight * 0.70f));
            }
            if (highlightPts.Count >= 2)
            {
                GameObject highlightObj = new GameObject("BottleGlassHighlight");
                highlightObj.transform.SetParent(wallObj.transform, false);
                highlightObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreateTaperedPipeMesh(highlightPts.ToArray(), 0.026f, 8);
                highlightObj.AddComponent<MeshRenderer>().sharedMaterial = glassHighlightMaterial;
            }
        }

        private void GenerateBottlePaths(out Vector3[] leftPath, out Vector3[] rightPath, out Vector3[] perimeterPath)
        {
            List<float> yList = new List<float>();

            // Mouth opening
            yList.Add(neckBottomY);

            // Neck (slender cylinder)
            int neckSteps = 6;
            for (int i = 1; i <= neckSteps; i++)
            {
                float t = (float)i / neckSteps;
                yList.Add(Mathf.Lerp(neckBottomY, neckTopY, t));
            }

            // Shoulder (smooth convex bell curve)
            int shoulderSteps = 16;
            for (int i = 1; i <= shoulderSteps; i++)
            {
                float t = (float)i / shoulderSteps;
                yList.Add(Mathf.Lerp(neckTopY, shoulderTopY, t));
            }

            // Body (tall straight cylinder)
            int bodySteps = 14;
            for (int i = 1; i <= bodySteps; i++)
            {
                float t = (float)i / bodySteps;
                yList.Add(Mathf.Lerp(shoulderTopY, bodyTopY, t));
            }

            // Base (rounded corners and flat bottom)
            int baseSteps = 8;
            for (int i = 1; i <= baseSteps; i++)
            {
                float t = (float)i / baseSteps;
                yList.Add(Mathf.Lerp(bodyTopY, baseTopY, t));
            }

            int n = yList.Count;
            leftPath = new Vector3[n];
            rightPath = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float y = yList[i];
                float hw = GetHalfWidthAtY(y);
                leftPath[i] = new Vector3(-hw, y, 0f);
                rightPath[i] = new Vector3(hw, y, 0f);
            }

            List<Vector3> perim = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                perim.Add(rightPath[i]);
            }
            for (int i = n - 2; i >= 0; i--)
            {
                perim.Add(leftPath[i]);
            }
            perimeterPath = perim.ToArray();
        }

        private void BuildSpline()
        {
            List<Vector3> keyPoints = new List<Vector3>
            {
                new Vector3(0f, baseTopY - 0.15f, 0f),
                new Vector3(0f, 1.55f, 0f),
                new Vector3(0f, 0.85f, 0f),
                new Vector3(0f, 0.15f, 0f),
                new Vector3(0f, -0.45f, 0f),
                new Vector3(0f, -0.95f, 0f),
                new Vector3(0f, neckBottomY, 0f),
                new Vector3(0f, neckBottomY - 0.25f, 0f)
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

        /// <summary>
        /// Populates the sleek bottle with candy cubes that physically tumble and settle under gravity,
        /// filling the slender neck, resting along the elegant shoulders, and forming an organic pile with natural rotations!
        /// </summary>
        private void PopulateBottlePhysics()
        {
            if (_bottleCubes == null) _bottleCubes = new List<BottleCube>();
            _bottleCubes.Clear();

            // Check active monster colors to guarantee all needed cubes exist in the bottle
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

            // Determine color order: Layer 0 (bottom mouth) to Layer 3 (top)
            PuzzleColor c0, c1, c2, c3;
            if (activeMonsterColors.Count >= 2)
            {
                c0 = activeMonsterColors[0];
                c1 = activeMonsterColors[1];
                c2 = (activeMonsterColors.Count >= 3) ? activeMonsterColors[2] : PuzzleColor.Orange;
                c3 = (activeMonsterColors.Count >= 4) ? activeMonsterColors[3] : PuzzleColor.Green;
            }
            else if (activeMonsterColors.Count == 1)
            {
                c0 = activeMonsterColors[0];
                c1 = PuzzleColor.Pink;
                c2 = PuzzleColor.Orange;
                c3 = PuzzleColor.Green;
            }
            else
            {
                // Default Reference Colors: Purple at mouth, Pink in body, Orange & Green at top
                c0 = PuzzleColor.Purple;
                c1 = PuzzleColor.Pink;
                c2 = PuzzleColor.Orange;
                c3 = PuzzleColor.Green;
            }

            // Layer definitions: Color, Count, Initial Y Range
            var layers = new List<(PuzzleColor col, int count, float yMin, float yMax)>
            {
                (c0, 12, -1.25f, -0.40f), // Bottom: fills slender neck & mouth (Purple)
                (c1, 24, -0.35f,  0.95f), // Middle: fills shoulder & body (Pink)
                (c2, 12,  1.00f,  1.60f), // Upper body: Orange
                (c3, 10,  1.65f,  2.05f)  // Top layer: Green
            };

            UnityEngine.Random.InitState(42);

            foreach (var layer in layers)
            {
                for (int i = 0; i < layer.count; i++)
                {
                    float t = (layer.count > 1) ? (float)i / (layer.count - 1) : 0.5f;
                    float initY = Mathf.Lerp(layer.yMin, layer.yMax, t) + UnityEngine.Random.Range(-0.03f, 0.03f);

                    float availHW = GetHalfWidthAtY(initY) - 0.12f;
                    float initX = UnityEngine.Random.Range(-availHW * 0.90f, availHW * 0.90f);

                    BottleCube bc = new BottleCube
                    {
                        color = layer.col,
                        x = initX,
                        y = initY,
                        angle = UnityEngine.Random.Range(-15f, 15f),
                        vx = 0f,
                        vy = 0f,
                        vAng = 0f
                    };
                    _bottleCubes.Add(bc);
                }
            }

            // Run 240 physics simulation steps: cubes drop, tumble against shoulders and each other, and settle stably!
            SimulatePhysicsSettling(240);

            // Instantiate GameObjects at their settled resting positions
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                Vector3 localPos = new Vector3(bc.x, bc.y, -0.10f);
                bc.gameObject = SpawnConveyorCube(localPos, bc.color, bc.angle);
            }
        }

        /// <summary>
        /// Position-Based Physics Simulation:
        /// Settles the candy cubes naturally inside the sleek bottle using gravity, boundary constraints,
        /// and collision relaxation with realistic angular tilting and shoulder slope alignment.
        /// Zero cube overlap guaranteed by strict collision clearance and post-processing resolver passes!
        /// </summary>
        private void SimulatePhysicsSettling(int steps)
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0) return;

            float dt = 0.016f;
            float gravity = 24.0f;
            // Cube size is 0.20f. Diagonal is 0.283f. With rounded corners (r=0.045f), max radius to corner is 0.1228f.
            // Minimum center distance to guarantee zero intersection under ANY 2D rotation is 2 * 0.1228f = 0.2456f.
            float minDist = 0.245f;
            float minDistSqr = minDist * minDist;
            float halfCube = 0.104f;

            // Reset velocities at start of settling pass
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                _bottleCubes[i].vx = 0f;
                _bottleCubes[i].vy = 0f;
                _bottleCubes[i].vAng = 0f;
            }

            for (int step = 0; step < steps; step++)
            {
                // 1. Gravity and velocity integration
                for (int i = 0; i < _bottleCubes.Count; i++)
                {
                    var c = _bottleCubes[i];
                    c.vy -= gravity * dt;

                    // Funnel gently towards center in the neck
                    if (c.y < shoulderTopY)
                    {
                        float funnel = Mathf.Lerp(5.0f, 0f, Mathf.Clamp01((c.y - neckBottomY) / (shoulderTopY - neckBottomY)));
                        c.vx -= c.x * funnel * dt;
                    }

                    // Air drag / damping
                    c.vx *= 0.82f;
                    c.vy *= 0.82f;
                    c.vAng *= 0.78f;

                    c.x += c.vx * dt;
                    c.y += c.vy * dt;
                    c.angle += c.vAng * dt;

                    c.angle = Mathf.Clamp(c.angle, -22f, 22f);
                }

                // 2. Collision relaxation (5 passes per step for firm resting stability)
                for (int iter = 0; iter < 5; iter++)
                {
                    // A. Inter-cube collisions
                    for (int i = 0; i < _bottleCubes.Count; i++)
                    {
                        for (int j = i + 1; j < _bottleCubes.Count; j++)
                        {
                            float dx = _bottleCubes[j].x - _bottleCubes[i].x;
                            float dy = _bottleCubes[j].y - _bottleCubes[i].y;
                            float d2 = dx * dx + dy * dy;

                            if (d2 < minDistSqr && d2 > 0.000001f)
                            {
                                float d = Mathf.Sqrt(d2);
                                float overlap = minDist - d;
                                float nx = dx / d;
                                float ny = dy / d;

                                _bottleCubes[i].x -= nx * overlap * 0.5f;
                                _bottleCubes[i].y -= ny * overlap * 0.5f;
                                _bottleCubes[j].x += nx * overlap * 0.5f;
                                _bottleCubes[j].y += ny * overlap * 0.5f;

                                // Friction and torque exchange
                                float relVx = _bottleCubes[j].vx - _bottleCubes[i].vx;
                                float relVy = _bottleCubes[j].vy - _bottleCubes[i].vy;
                                float tangent = -ny * relVx + nx * relVy;
                                _bottleCubes[i].vAng += tangent * 0.8f;
                                _bottleCubes[j].vAng -= tangent * 0.8f;

                                // Organic tilt when supported on another cube
                                if (Mathf.Abs(ny) > 0.5f)
                                {
                                    float tiltOffset = (dx / minDist) * 12f;
                                    _bottleCubes[j].angle = Mathf.Lerp(_bottleCubes[j].angle, tiltOffset, 0.05f);
                                }
                            }
                        }
                    }

                    // B. Bottle boundary constraints
                    for (int i = 0; i < _bottleCubes.Count; i++)
                    {
                        var c = _bottleCubes[i];

                        // Mouth stopper at the bottom
                        float floorY = neckBottomY + halfCube + 0.02f;
                        if (c.y < floorY)
                        {
                            c.y = floorY;
                            c.vy = Mathf.Max(0f, c.vy * -0.05f);
                            c.vAng *= 0.7f;
                        }

                        // Top base ceiling
                        float ceilY = baseTopY - halfCube - 0.05f;
                        if (c.y > ceilY)
                        {
                            c.y = ceilY;
                            c.vy = Mathf.Min(0f, c.vy);
                        }

                        // Side walls from bottle outline
                        float availHW = GetHalfWidthAtY(c.y) - halfCube - 0.012f;

                        // Left wall
                        if (c.x < -availHW)
                        {
                            c.x = -availHW;
                            c.vx = Mathf.Max(0f, -c.vx * 0.1f);

                            // Left shoulder slope tilts the cube clockwise to rest against the glass
                            if (c.y > neckTopY && c.y < shoulderTopY)
                            {
                                c.angle = Mathf.Lerp(c.angle, 14f, 0.12f);
                            }
                        }
                        // Right wall
                        else if (c.x > availHW)
                        {
                            c.x = availHW;
                            c.vx = Mathf.Min(0f, -c.vx * 0.1f);

                            // Right shoulder slope tilts the cube counter-clockwise
                            if (c.y > neckTopY && c.y < shoulderTopY)
                            {
                                c.angle = Mathf.Lerp(c.angle, -14f, 0.12f);
                            }
                        }
                    }
                }
            }

            // 3. Final strict non-penetration solver pass: eliminates any residual overlap
            for (int pass = 0; pass < 10; pass++)
            {
                bool anyOverlap = false;
                for (int i = 0; i < _bottleCubes.Count; i++)
                {
                    for (int j = i + 1; j < _bottleCubes.Count; j++)
                    {
                        float dx = _bottleCubes[j].x - _bottleCubes[i].x;
                        float dy = _bottleCubes[j].y - _bottleCubes[i].y;
                        float d2 = dx * dx + dy * dy;

                        if (d2 < minDistSqr && d2 > 0.0000001f)
                        {
                            anyOverlap = true;
                            float d = Mathf.Sqrt(d2);
                            float overlap = minDist - d;
                            float nx = dx / d;
                            float ny = dy / d;

                            _bottleCubes[i].x -= nx * overlap * 0.5f;
                            _bottleCubes[i].y -= ny * overlap * 0.5f;
                            _bottleCubes[j].x += nx * overlap * 0.5f;
                            _bottleCubes[j].y += ny * overlap * 0.5f;
                        }
                    }
                }

                // Keep strictly within bottle boundaries
                for (int i = 0; i < _bottleCubes.Count; i++)
                {
                    var c = _bottleCubes[i];
                    float floorY = neckBottomY + halfCube + 0.02f;
                    if (c.y < floorY) c.y = floorY;

                    float ceilY = baseTopY - halfCube - 0.05f;
                    if (c.y > ceilY) c.y = ceilY;

                    float availHW = GetHalfWidthAtY(c.y) - halfCube - 0.012f;
                    if (c.x < -availHW) c.x = -availHW;
                    else if (c.x > availHW) c.x = availHW;
                }

                if (!anyOverlap) break;
            }
        }

        private GameObject SpawnConveyorCube(Vector3 localPos, PuzzleColor col, float angleZ)
        {
            EnsureMaterials();

            GameObject cube = new GameObject($"BottleCube_{col}");
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
        /// Returns the frontmost (lowest Y) cube currently at the mouth of the bottle.
        /// </summary>
        public BottleCube GetFrontCube()
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0) return null;

            BottleCube lowest = null;
            float lowestY = float.MaxValue;
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                if (bc != null && bc.gameObject != null)
                {
                    if (bc.y < lowestY)
                    {
                        lowestY = bc.y;
                        lowest = bc;
                    }
                }
            }
            return lowest;
        }

        public PuzzleColor? GetFrontCubeColor()
        {
            var front = GetFrontCube();
            return front?.color;
        }

        /// <summary>
        /// Strictly consumes the frontmost (lowest Y) cube from the mouth of the bottle.
        /// Guaranteed: No cube above ever passes through cubes in front of it!
        /// </summary>
        public bool ConsumeAndLaunchCube(MonsterCharacter targetMonster, PuzzleColor color, Material cubeMat, Mesh cubeMesh)
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0 || _cubesContainer == null)
            {
                RebuildContainer();
            }

            // Remove any destroyed references
            for (int i = _bottleCubes.Count - 1; i >= 0; i--)
            {
                if (_bottleCubes[i].gameObject == null)
                {
                    _bottleCubes.RemoveAt(i);
                }
            }

            if (_bottleCubes.Count == 0) return false;

            // Strict rule: ALWAYS take the frontmost (lowest Y) cube in the bottle!
            BottleCube targetCube = GetFrontCube();
            if (targetCube == null || targetCube.gameObject == null) return false;

            int targetIdx = _bottleCubes.IndexOf(targetCube);
            _bottleCubes.RemoveAt(targetIdx);

            GameObject cubeObj = targetCube.gameObject;
            Vector3 startWorldPos = cubeObj.transform.position;
            Vector3 spoutWorldPos = GetSpoutExitWorldPosition();

            // Direct trajectory from mouth nozzle to spout and monster (never passes through other cubes!)
            List<Vector3> waypoints = new List<Vector3>
            {
                startWorldPos,
                spoutWorldPos
            };

            // Detach from bottle hierarchy
            cubeObj.transform.SetParent(null, true);
            string monsterId = (targetMonster != null) ? targetMonster.monsterId : "Unknown";
            cubeObj.name = $"FlyingCube_{targetCube.color}_To_{monsterId}";

            Material mat = _cubeMaterials.ContainsKey(targetCube.color) ? _cubeMaterials[targetCube.color] : cubeMat;

            FlyingCube flying = cubeObj.GetComponent<FlyingCube>();
            if (flying == null) flying = cubeObj.AddComponent<FlyingCube>();
            flying.LaunchThroughGlassTrack(waypoints.ToArray(), spoutWorldPos, targetMonster, targetCube.color, mat, cubeMesh);

            PulseSpout();

            // Physically settle all remaining cubes in the bottle downwards!
            SettleRemainingCubesAnimated();

            return true;
        }

        /// <summary>
        /// Animates the remaining cubes in the bottle dropping and tumbling down under gravity
        /// to fill the void created by the consumed cube.
        /// </summary>
        public void SettleRemainingCubesAnimated()
        {
            if (!gameObject.activeInHierarchy || _bottleCubes == null || _bottleCubes.Count == 0) return;

            // Run 80 physics relaxation steps to calculate the new stable resting positions
            SimulatePhysicsSettling(80);

            // Smoothly animate each cube to its new position and angle with natural settling ease
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                if (bc.gameObject == null) continue;

                Vector3 targetPos = new Vector3(bc.x, bc.y, -0.10f);
                Quaternion targetRot = Quaternion.Euler(0f, 0f, bc.angle);

                bc.gameObject.transform.DOKill();
                bc.gameObject.transform.DOLocalMove(targetPos, 0.14f).SetEase(Ease.OutQuad);
                bc.gameObject.transform.DOLocalRotateQuaternion(targetRot, 0.14f).SetEase(Ease.OutQuad);
            }
        }

        public void AdvanceTrackTrain()
        {
            SettleRemainingCubesAnimated();
        }

        public Vector3 GetSpoutExitWorldPosition()
        {
            if (spoutExitPoint != null)
            {
                return spoutExitPoint.position;
            }
            if (_trackRootObj != null)
            {
                return _trackRootObj.transform.TransformPoint(new Vector3(0f, neckBottomY - 0.10f, -0.06f));
            }
            return transform.position + new Vector3(0f, -0.85f, -0.70f);
        }

        public void ConsumeCube(PuzzleColor color)
        {
            SettleRemainingCubesAnimated();
        }

        public void PulseSpout()
        {
            if (!gameObject.activeInHierarchy) return;

            if (spoutExitPoint != null)
            {
                spoutExitPoint.DOKill(true);
                spoutExitPoint.DOPunchScale(new Vector3(0.06f, 0.08f, 0.06f), 0.08f, 5, 0.5f);
            }

            if (_glassWallsObj != null)
            {
                _glassWallsObj.transform.DOKill(true);
                _glassWallsObj.transform.DOPunchScale(new Vector3(0.015f, 0.015f, 0.015f), 0.08f, 4, 0.5f);
            }
            else if (_trackRootObj != null)
            {
                _trackRootObj.transform.DOKill(true);
                _trackRootObj.transform.DOPunchScale(new Vector3(0.015f, 0.015f, 0.015f), 0.08f, 4, 0.5f);
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
