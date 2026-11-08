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
        public float wallDepth = 0.55f;
        public float wallThickness = 0.10f;
        public float frontPlaneZ = -0.26f;
        public float backPlaneZ = +0.06f;
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
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void OnEnable()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void EnsurePhysMaterial()
        {
            if (_glassPhysMaterial == null)
            {
                _glassPhysMaterial = new PhysicsMaterial("Mat_GlassContainerWall")
                {
                    bounciness = 0.20f,
                    dynamicFriction = 0.15f,
                    staticFriction = 0.25f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Average
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
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
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

            // 6. Bottom Spout Stopper Gate (Holds balls inside spout until collection)
            float botY = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? glassContainer.contourPoints[0].y * glassContainer.heightScale
                : glassContainer.spoutBottomY;

            bool isFlatBox = glassContainer.shapePreset == GlassShapePreset.SquareBox || glassContainer.shapePreset == GlassShapePreset.RectangleBox;
            float botWidth = isFlatBox ? (glassContainer.chamberWidth + 0.40f) : (glassContainer.spoutWidth + 0.20f);

            float spoutCenterX = (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? (glassContainer.symmetricMode ? 0f : glassContainer.contourPoints[0].centerX) * glassContainer.widthScale
                : 0f;

            GameObject stopperObj = new GameObject("BottomSpoutStopperGate");
            stopperObj.transform.SetParent(_collidersRoot.transform, false);
            stopperObj.transform.localPosition = new Vector3(spoutCenterX, botY - 0.02f, -0.10f);
            bottomSpoutStopper = stopperObj.AddComponent<BoxCollider>();
            bottomSpoutStopper.size = new Vector3(botWidth, 0.10f, wallDepth);
            bottomSpoutStopper.sharedMaterial = _glassPhysMaterial;

            // 7. Spout Exit Trigger (Detects when a collecting ball clears the spout)
            GameObject triggerObj = new GameObject("SpoutExitTrigger");
            triggerObj.transform.SetParent(_collidersRoot.transform, false);
            triggerObj.transform.localPosition = new Vector3(spoutCenterX, botY - 0.08f, -0.10f);
            spoutExitTrigger = triggerObj.AddComponent<BoxCollider>();
            spoutExitTrigger.isTrigger = true;
            spoutExitTrigger.size = new Vector3(botWidth + 0.20f, 0.20f, wallDepth);
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
                // Slight overlap (+0.025f) between segments ensures zero gaps or edge snagging
                box.size = new Vector3(segLen + 0.025f, wallThickness, wallDepth);
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

