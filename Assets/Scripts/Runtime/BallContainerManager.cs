using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class BallContainerManager : MonoBehaviour
    {
        public static BallContainerManager Instance { get; private set; }

        public ShapedGlassContainer glassContainer;

        [Header("Physical Wall Settings")]
        public float wallDepth = 0.65f;
        public float wallThickness = 0.12f;
        public float frontPlaneZ = -0.35f;
        public float backPlaneZ = +0.15f;
        public bool showColliderGizmos = true;

        [Header("Spout Exit Gate")]
        public BoxCollider bottomSpoutStopper;
        public BoxCollider spoutExitTrigger;

        private GameObject _collidersRoot;
        private PhysicsMaterial _glassPhysMaterial;

        private void Awake()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void OnEnable()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void EnsurePhysMaterial()
        {
            if (_glassPhysMaterial == null)
            {
                _glassPhysMaterial = new PhysicsMaterial("Mat_GlassContainerWall")
                {
                    bounciness = 0.10f,
                    dynamicFriction = 0.04f,
                    staticFriction = 0.05f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Minimum
                };
            }
        }

        /// <summary>
        /// Generates an airtight 3D physics collision cage matching the container's exact glass contour!
        /// Balls are physically contained by Left & Right sloped walls, Front & Back glass planes,
        /// Top rim ceiling, and Bottom spout stopper. Zero ball escape or wall tunneling!
        /// </summary>
        [ContextMenu("Rebuild Container Colliders")]
        public void RebuildColliders()
        {
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
                if (glassContainer == null) return;
            }

            EnsurePhysMaterial();

            Transform existing = transform.Find("GlassPhysicsColliders");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            _collidersRoot = new GameObject("GlassPhysicsColliders");
            _collidersRoot.transform.SetParent(transform, false);
            _collidersRoot.transform.localPosition = Vector3.zero;
            _collidersRoot.transform.localRotation = Quaternion.identity;

            if (glassContainer.IsFlatBottomBox)
            {
                float floorY = glassContainer.GetChamberFloorY();
                float ceilY = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                    ? glassContainer.contourPoints[glassContainer.contourPoints.Count - 1].y * glassContainer.heightScale
                    : 0.62f * glassContainer.heightScale;
                float innerHalfW = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                    ? glassContainer.contourPoints[0].halfWidth * glassContainer.widthScale
                    : 1.32f * glassContainer.widthScale;

                float centerY = (floorY + ceilY) * 0.5f;
                float boxH = Mathf.Max(0.5f, (ceilY - floorY) + 0.40f);
                float boxW = innerHalfW * 2f + 0.60f;

                // Left vertical wall (inner face at -innerHalfW)
                GameObject leftBoxWall = new GameObject("LeftGlassWallCollider");
                leftBoxWall.transform.SetParent(_collidersRoot.transform, false);
                leftBoxWall.transform.localPosition = new Vector3(-innerHalfW - wallThickness * 0.5f, centerY, -0.10f);
                var leftCol = leftBoxWall.AddComponent<BoxCollider>();
                leftCol.size = new Vector3(wallThickness, boxH, wallDepth);
                leftCol.sharedMaterial = _glassPhysMaterial;

                // Right vertical wall (inner face at +innerHalfW)
                GameObject rightBoxWall = new GameObject("RightGlassWallCollider");
                rightBoxWall.transform.SetParent(_collidersRoot.transform, false);
                rightBoxWall.transform.localPosition = new Vector3(innerHalfW + wallThickness * 0.5f, centerY, -0.10f);
                var rightCol = rightBoxWall.AddComponent<BoxCollider>();
                rightCol.size = new Vector3(wallThickness, boxH, wallDepth);
                rightCol.sharedMaterial = _glassPhysMaterial;

                // Front & Back Z containment walls
                GameObject frontBoxWall = new GameObject("FrontGlassWallCollider");
                frontBoxWall.transform.SetParent(_collidersRoot.transform, false);
                frontBoxWall.transform.localPosition = new Vector3(0f, centerY, frontPlaneZ);
                var frontBoxCol = frontBoxWall.AddComponent<BoxCollider>();
                frontBoxCol.size = new Vector3(boxW + 1.0f, boxH + 1.0f, 0.08f);
                frontBoxCol.sharedMaterial = _glassPhysMaterial;

                GameObject backBoxWall = new GameObject("BackGlassWallCollider");
                backBoxWall.transform.SetParent(_collidersRoot.transform, false);
                backBoxWall.transform.localPosition = new Vector3(0f, centerY, backPlaneZ);
                var backBoxCol = backBoxWall.AddComponent<BoxCollider>();
                backBoxCol.size = new Vector3(boxW + 1.0f, boxH + 1.0f, 0.08f);
                backBoxCol.sharedMaterial = _glassPhysMaterial;

                // Top Ceiling (bottom face at ceilY)
                GameObject topBoxCeiling = new GameObject("TopCeilingCollider");
                topBoxCeiling.transform.SetParent(_collidersRoot.transform, false);
                topBoxCeiling.transform.localPosition = new Vector3(0f, ceilY + 0.075f, -0.10f);
                var topBoxCol = topBoxCeiling.AddComponent<BoxCollider>();
                topBoxCol.size = new Vector3(boxW, 0.15f, wallDepth);
                topBoxCol.sharedMaterial = _glassPhysMaterial;

                // Flat Floor Stopper (top surface at floorY)
                GameObject floorStopperObj = new GameObject("BottomSpoutStopperGate");
                floorStopperObj.transform.SetParent(_collidersRoot.transform, false);
                floorStopperObj.transform.localPosition = new Vector3(0f, floorY - 0.04f, -0.10f);
                bottomSpoutStopper = floorStopperObj.AddComponent<BoxCollider>();
                bottomSpoutStopper.size = new Vector3(boxW, 0.08f, wallDepth);
                bottomSpoutStopper.sharedMaterial = _glassPhysMaterial;

                // Rounded Bottom Corner Fillets (matches the curved bottom-left and bottom-right glass corners of Image 2
                // so the 10 bottom-row balls sit snugly between [-1.20, +1.20] while the 11 middle-row balls span [-1.32, +1.32])
                float filletW = 0.116f;
                float filletH = 0.18f;
                GameObject leftFilletObj = new GameObject("LeftBottomCornerFillet");
                leftFilletObj.transform.SetParent(_collidersRoot.transform, false);
                leftFilletObj.transform.localPosition = new Vector3(-innerHalfW + filletW * 0.5f, floorY + filletH * 0.5f, -0.10f);
                var leftFilletCol = leftFilletObj.AddComponent<BoxCollider>();
                leftFilletCol.size = new Vector3(filletW, filletH, wallDepth);
                leftFilletCol.sharedMaterial = _glassPhysMaterial;

                GameObject rightFilletObj = new GameObject("RightBottomCornerFillet");
                rightFilletObj.transform.SetParent(_collidersRoot.transform, false);
                rightFilletObj.transform.localPosition = new Vector3(innerHalfW - filletW * 0.5f, floorY + filletH * 0.5f, -0.10f);
                var rightFilletCol = rightFilletObj.AddComponent<BoxCollider>();
                rightFilletCol.size = new Vector3(filletW, filletH, wallDepth);
                rightFilletCol.sharedMaterial = _glassPhysMaterial;

                // Exit Trigger below floor
                GameObject boxTriggerObj = new GameObject("SpoutExitTrigger");
                boxTriggerObj.transform.SetParent(_collidersRoot.transform, false);
                boxTriggerObj.transform.localPosition = new Vector3(0f, floorY - 0.12f, -0.10f);
                spoutExitTrigger = boxTriggerObj.AddComponent<BoxCollider>();
                spoutExitTrigger.isTrigger = true;
                spoutExitTrigger.size = new Vector3(boxW, 0.16f, wallDepth);

                BuildDividerColliders();
                return;
            }

            // Generate latest smooth paths
            glassContainer.GenerateFlaskPaths(
                out Vector3[] leftOuter,
                out Vector3[] rightOuter,
                out Vector3[] leftInner,
                out Vector3[] rightInner);

            if (leftInner == null || rightInner == null || leftInner.Length < 2) return;

            // 1. Build Chained Left Inner Wall Box Colliders
            BuildWallSegmentColliders(leftInner, isLeft: true);

            // 2. Build Chained Right Inner Wall Box Colliders
            BuildWallSegmentColliders(rightInner, isLeft: false);

            // 3. Front Glass Wall Collider (Seals balls in front)
            float midY = (glassContainer.spoutBottomY + glassContainer.neckTopY) * 0.5f;
            float totalH = (glassContainer.neckTopY - glassContainer.spoutBottomY) + 2.0f;
            float totalW = glassContainer.chamberWidth + 3.0f;

            GameObject frontWall = new GameObject("FrontGlassWallCollider");
            frontWall.transform.SetParent(_collidersRoot.transform, false);
            frontWall.transform.localPosition = new Vector3(0f, midY, frontPlaneZ);
            var frontCol = frontWall.AddComponent<BoxCollider>();
            frontCol.size = new Vector3(totalW, totalH, 0.08f);
            frontCol.sharedMaterial = _glassPhysMaterial;

            // 4. Back Glass Wall Collider (Seals balls in back)
            GameObject backWall = new GameObject("BackGlassWallCollider");
            backWall.transform.SetParent(_collidersRoot.transform, false);
            backWall.transform.localPosition = new Vector3(0f, midY, backPlaneZ);
            var backCol = backWall.AddComponent<BoxCollider>();
            backCol.size = new Vector3(totalW, totalH, 0.08f);
            backCol.sharedMaterial = _glassPhysMaterial;

            // 5. Top Ceiling Collider (Seals neck top)
            GameObject topCeiling = new GameObject("TopCeilingCollider");
            topCeiling.transform.SetParent(_collidersRoot.transform, false);
            topCeiling.transform.localPosition = new Vector3(0f, glassContainer.neckTopY + 0.12f, -0.10f);
            var topCol = topCeiling.AddComponent<BoxCollider>();
            topCol.size = new Vector3(glassContainer.neckWidth + 0.40f, 0.15f, wallDepth);
            topCol.sharedMaterial = _glassPhysMaterial;

            // 6. Bottom Spout Stopper Gate (Holds balls inside funnel/chamber until collection)
            bool isFlatBox = glassContainer.shapePreset == GlassShapePreset.SquareBox || glassContainer.shapePreset == GlassShapePreset.RectangleBox;
            float throatY = isFlatBox ? (glassContainer.contourPoints[0].y * glassContainer.heightScale) : glassContainer.GetSpoutThroatY();
            float stopperW = isFlatBox ? (glassContainer.chamberWidth + 0.40f) : (glassContainer.spoutWidth + 0.18f);

            float spoutCenterX = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? (glassContainer.symmetricMode ? 0f : glassContainer.contourPoints[0].centerX) * glassContainer.widthScale
                : 0f;

            GameObject stopperObj = new GameObject("BottomSpoutStopperGate");
            stopperObj.transform.SetParent(_collidersRoot.transform, false);
            stopperObj.transform.localPosition = new Vector3(spoutCenterX, throatY - 0.02f, -0.10f);
            bottomSpoutStopper = stopperObj.AddComponent<BoxCollider>();
            bottomSpoutStopper.size = new Vector3(stopperW, 0.08f, wallDepth);
            bottomSpoutStopper.sharedMaterial = _glassPhysMaterial;

            // 7. Spout Exit Trigger (Detects when a collecting ball clears the spout tip)
            float botY = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? glassContainer.contourPoints[0].y * glassContainer.heightScale
                : glassContainer.spoutBottomY;

            GameObject triggerObj = new GameObject("SpoutExitTrigger");
            triggerObj.transform.SetParent(_collidersRoot.transform, false);
            triggerObj.transform.localPosition = new Vector3(spoutCenterX, botY - 0.04f, -0.10f);
            spoutExitTrigger = triggerObj.AddComponent<BoxCollider>();
            spoutExitTrigger.isTrigger = true;
            spoutExitTrigger.size = new Vector3(stopperW + 0.20f, 0.16f, wallDepth);

            BuildDividerColliders();
        }

        private void BuildDividerColliders()
        {
            if (glassContainer == null || glassContainer.dividers == null || _collidersRoot == null) return;

            for (int i = 0; i < glassContainer.dividers.Count; i++)
            {
                var div = glassContainer.dividers[i];
                if (div == null || !div.enabled) continue;

                GameObject divColObj = new GameObject($"DividerCollider_{i + 1}_{div.name}");
                divColObj.transform.SetParent(_collidersRoot.transform, false);
                divColObj.transform.localPosition = new Vector3(div.center.x, div.center.y, -0.10f);
                divColObj.transform.localRotation = Quaternion.Euler(0f, 0f, div.angleDegrees);

                var box = divColObj.AddComponent<BoxCollider>();
                box.size = new Vector3(Mathf.Max(0.08f, div.length), Mathf.Max(0.03f, div.thickness), wallDepth);
                box.sharedMaterial = _glassPhysMaterial;
            }
        }

        private void BuildWallSegmentColliders(Vector3[] path, bool isLeft)
        {
            int n = path.Length;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 pA = path[i];
                Vector3 pB = path[i + 1];

                Vector3 mid = (pA + pB) * 0.5f;
                float segLen = Vector3.Distance(pA, pB);
                if (segLen < 0.005f) continue;

                Vector3 dir = (pB - pA).normalized;
                float angleZ = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                // Shift collider outward by half wall thickness so inner face matches the glass contour exactly
                Vector3 outNormal = isLeft ? new Vector3(-dir.y, dir.x, 0f) : new Vector3(dir.y, -dir.x, 0f);
                Vector3 colPos = mid + outNormal * (wallThickness * 0.5f);
                colPos.z = -0.10f; // Center of 2D ball flight plane

                GameObject segObj = new GameObject($"{(isLeft ? "Left" : "Right")}_WallSeg_{i}");
                segObj.transform.SetParent(_collidersRoot.transform, false);
                segObj.transform.localPosition = colPos;
                segObj.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);

                var box = segObj.AddComponent<BoxCollider>();
                // Slight overlap (+0.035f) between segments ensures zero gaps or edge snagging
                box.size = new Vector3(segLen + 0.035f, wallThickness, wallDepth);
                box.sharedMaterial = _glassPhysMaterial;
            }
        }

        public void SetSpoutStopperActive(bool active)
        {
            if (bottomSpoutStopper != null)
            {
                bottomSpoutStopper.enabled = active;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showColliderGizmos || _collidersRoot == null) return;

            var boxColliders = _collidersRoot.GetComponentsInChildren<BoxCollider>();
            for (int i = 0; i < boxColliders.Length; i++)
            {
                var box = boxColliders[i];
                if (box == null) continue;

                Gizmos.matrix = box.transform.localToWorldMatrix;
                if (box == bottomSpoutStopper)
                {
                    Gizmos.color = box.enabled ? new Color(1.0f, 0.22f, 0.22f, 0.55f) : new Color(0.2f, 0.85f, 0.2f, 0.25f);
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                else if (box == spoutExitTrigger)
                {
                    Gizmos.color = new Color(1.0f, 0.85f, 0.15f, 0.30f);
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                else
                {
                    Gizmos.color = new Color(0.18f, 0.82f, 1.0f, 0.20f);
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.color = new Color(0.12f, 0.65f, 1.0f, 0.70f);
                    Gizmos.DrawWireCube(box.center, box.size);
                }
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}