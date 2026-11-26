using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Shared geometric spline & queueing math for the optional per-level top curved glass feed tubes.
    /// Used by both runtime 3D mesh/physics (ShapedGlassContainer, BallFeedTubeController) and Editor 2D previews.
    /// </summary>
    public struct FeedTubeCurveData
    {
        public Vector3[] centerPath;
        public Vector3[] leftEdgePath;
        public Vector3[] rightEdgePath;
        public Vector3[] leftShadowPath;
        public Vector3[] rightShadowPath;
        public float[] arcLengths;
        public float totalLength;
        public float innerHalfWidth;
        public float wallWidthRatio;
        public Vector2 mouthPoint;
        public float gateDist;
        public Vector2 gatePoint;
        public float gateAngleDeg;
        public float gateWidth;
        /// <summary>Kapak kapalıyken en öndeki topun merkezinin ağızdan uzaklığı (top yatay kapağın tam üstüne oturur).</summary>
        public float restDist;

        public bool IsValid => centerPath != null && centerPath.Length >= 2 && arcLengths != null && arcLengths.Length == centerPath.Length;

        /// <summary>
        /// Evaluates a 2D point and upward-pointing unit tangent along the tube centerline
        /// at a given arc-length distance measured upward from the container shoulder mouth.
        /// </summary>
        public void EvaluateAtDistance(float distFromMouth, out Vector2 pos, out Vector2 tangentUp)
        {
            if (!IsValid)
            {
                pos = Vector2.zero;
                tangentUp = Vector2.up;
                return;
            }

            float d = Mathf.Clamp(distFromMouth, 0f, totalLength);
            int n = centerPath.Length;
            if (d <= 0.0001f)
            {
                pos = centerPath[0];
                tangentUp = ((Vector2)(centerPath[1] - centerPath[0])).normalized;
                if (tangentUp.sqrMagnitude < 0.001f) tangentUp = Vector2.up;
                return;
            }
            if (d >= totalLength - 0.0001f)
            {
                pos = centerPath[n - 1];
                tangentUp = ((Vector2)(centerPath[n - 1] - centerPath[n - 2])).normalized;
                if (tangentUp.sqrMagnitude < 0.001f) tangentUp = Vector2.up;
                return;
            }

            for (int i = 0; i < n - 1; i++)
            {
                float d0 = arcLengths[i];
                float d1 = arcLengths[i + 1];
                if (d <= d1 + 0.0001f)
                {
                    float seg = Mathf.Max(0.0001f, d1 - d0);
                    float t = Mathf.Clamp01((d - d0) / seg);
                    Vector2 a = centerPath[i];
                    Vector2 b = centerPath[i + 1];
                    pos = Vector2.Lerp(a, b, t);
                    tangentUp = (b - a).normalized;
                    if (tangentUp.sqrMagnitude < 0.001f) tangentUp = Vector2.up;
                    return;
                }
            }

            pos = centerPath[n - 1];
            tangentUp = Vector2.up;
        }
    }

    public static class BallFeedTubeUtility
    {
        /// <summary>
        /// Computes how many balls spawn directly inside the main container when feed tubes are enabled.
        /// Remaining balls queue inside the upper curved glass feed tubes.
        /// </summary>
        public static int GetEffectiveContainerLimit(int configuredLimit, int totalBalls)
        {
            if (totalBalls <= 0) return 0;
            if (configuredLimit > 0) return Mathf.Clamp(configuredLimit, 1, totalBalls);

            // Auto mode (0): keep roughly half in the container while ensuring tubes show queued balls
            if (totalBalls <= 4) return Mathf.Max(1, totalBalls - 1);
            int half = Mathf.RoundToInt(totalBalls * 0.45f);
            return Mathf.Clamp(half, 3, Mathf.Max(3, totalBalls - 2));
        }

        public static void GetTopAnchorFromContour(
            IList<GlassContourPoint> contour,
            float widthScale,
            float heightScale,
            out float ceilY,
            out float topHalfW,
            out float topCenterX)
        {
            if (contour == null || contour.Count == 0)
            {
                ceilY = 0.62f;
                topHalfW = 1.32f * Mathf.Max(0.1f, widthScale);
                topCenterX = 0f;
                return;
            }

            var top = contour[contour.Count - 1];
            ceilY = top.y * Mathf.Max(0.1f, heightScale);
            topHalfW = Mathf.Max(0.35f, top.halfWidth * Mathf.Max(0.1f, widthScale));
            topCenterX = top.centerX * Mathf.Max(0.1f, widthScale);
        }

        /// <summary>
        /// Builds a smooth S-curved upper glass tube from the container's upper-left or upper-right corner
        /// rising up into a straight vertical feed shaft off the top of the screen.
        /// Paths are ordered from mouth (index 0) to top end (last index), with leftEdgePath always left of rightEdgePath.
        /// </summary>
        public static FeedTubeCurveData BuildTubeCurve(
            bool isLeft,
            float ceilY,
            float topHalfW,
            float topCenterX,
            float ballRadius,
            float widthMultiplier,
            float anchorSpread,
            float tubeLengthAboveCeil = 3.25f,
            int bendSteps = 36,
            int shaftSteps = 12)
        {
            float sign = isLeft ? -1f : 1f;
            float r = Mathf.Clamp(ballRadius, 0.07f, 0.17f);
            // Leave comfortable crystal breathing room around every ball inside the glass walls
            float clearInnerHalfW = r * Mathf.Clamp(widthMultiplier, 1.05f, 1.65f) * 1.04f;
            float wallPad = 0.056f;
            float tubeInnerHalfW = Mathf.Clamp(clearInnerHalfW + wallPad, r * 1.32f, 0.32f);
            float spread = Mathf.Clamp(anchorSpread, 0.15f, 1.0f);

            // Mouth enters cleanly inside the container's upper corner shoulder (safely below ceilY & inside side walls)
            float maxMouthOffset = Mathf.Max(r * 1.15f, topHalfW - wallPad - r * 1.25f);
            float minMouthOffset = Mathf.Min(maxMouthOffset, Mathf.Max(r * 1.15f, topHalfW * 0.42f));
            float mouthOffset = Mathf.Lerp(minMouthOffset, maxMouthOffset, spread * 0.65f);
            float mouthX = topCenterX + sign * mouthOffset;
            float mouthY = ceilY - r * 1.42f;

            // Upper vertical shaft X curves gently outward beside the top HUD pill
            float topOffset = Mathf.Lerp(Mathf.Min(topHalfW * 0.62f, mouthOffset + 0.06f), topHalfW + tubeInnerHalfW * 0.65f, spread);
            float topX = topCenterX + sign * Mathf.Max(tubeInnerHalfW + 0.08f, topOffset);

            float bendHeight = Mathf.Clamp(topHalfW * 0.72f, 0.62f, 1.15f);
            float shaftBottomY = ceilY + bendHeight;
            float shaftTopY = ceilY + Mathf.Max(bendHeight + 0.35f, tubeLengthAboveCeil);

            // Cubic Bezier S-curve passing straight downward through ceilY so balls drop vertically inside the bottle
            Vector2 p0 = new Vector2(mouthX, mouthY);
            Vector2 p1 = new Vector2(mouthX, ceilY + bendHeight * 0.28f);
            Vector2 p2 = new Vector2(topX, shaftBottomY - bendHeight * 0.42f);
            Vector2 p3 = new Vector2(topX, shaftBottomY);

            int totalPts = bendSteps + shaftSteps + 1;
            Vector3[] center = new Vector3[totalPts];
            Vector3[] left = new Vector3[totalPts];
            Vector3[] right = new Vector3[totalPts];
            Vector3[] leftShad = new Vector3[totalPts];
            Vector3[] rightShad = new Vector3[totalPts];
            float[] arc = new float[totalPts];

            int idx = 0;
            for (int i = 0; i <= bendSteps; i++)
            {
                float t = (float)i / bendSteps;
                center[idx++] = EvalCubicBezier(p0, p1, p2, p3, t);
            }
            for (int s = 1; s <= shaftSteps; s++)
            {
                float t = (float)s / shaftSteps;
                float y = Mathf.Lerp(shaftBottomY, shaftTopY, t);
                center[idx++] = new Vector3(topX, y, 0f);
            }

            const float wallRatio = 0.641f;
            const float padMult = 1f / wallRatio;

            float accLen = 0f;
            arc[0] = 0f;
            for (int i = 0; i < totalPts; i++)
            {
                if (i > 0)
                {
                    accLen += Vector2.Distance(center[i - 1], center[i]);
                    arc[i] = accLen;
                }

                Vector2 prev = i > 0 ? (Vector2)center[i - 1] : (Vector2)center[i];
                Vector2 next = i < totalPts - 1 ? (Vector2)center[i + 1] : (Vector2)center[i];
                Vector2 tan = (next - prev).normalized;
                if (tan.sqrMagnitude < 0.0001f) tan = Vector2.up;

                // Right-pointing normal in 2D XY when tan points upward (+Y): (+tan.y, -tan.x)
                Vector2 rightDir = new Vector2(tan.y, -tan.x).normalized;

                // Tüp camın içine girmediği için ağızda huni genişlemesi yok: sabit genişlik
                float halfW = tubeInnerHalfW;
                float shadHalfW = halfW * padMult;

                Vector2 c2 = center[i];
                Vector2 l2 = c2 - rightDir * halfW;
                Vector2 r2 = c2 + rightDir * halfW;
                Vector2 ls2 = c2 - rightDir * shadHalfW;
                Vector2 rs2 = c2 + rightDir * shadHalfW;

                left[i] = new Vector3(l2.x, l2.y, 0f);
                right[i] = new Vector3(r2.x, r2.y, 0f);
                leftShad[i] = new Vector3(ls2.x, ls2.y, 0f);
                rightShad[i] = new Vector3(rs2.x, rs2.y, 0f);
            }

            // Görsel kesim: tüpün duvarları camın üst kenarında (ceilY) yatay olarak biter, ana camın içine girmez.
            // Topların izlediği merkez çizgisi (center/arc) değişmez; toplar kapaktan sonra camın içine süzülür.
            ClipPairToHeight(ref left, ref right, ceilY);
            ClipPairToHeight(ref leftShad, ref rightShad, ceilY);

            // Kapak: tüp hangi açıyla gelirse gelsin YATAY ve camın üst kenarı (ceilY) hizasında.
            // Genişliği, tüpün iki duvarının bu yükseklikteki yatay kesişimi kadardır (eğik tüpte dik kesitten geniştir).
            float gateTargetY = ceilY;
            float foundGateDist = ArcAtHeight(center, arc, gateTargetY, r * 1.46f);
            float centerXAtGate = XAtHeight(center, gateTargetY, mouthX);
            float leftX = XAtHeight(left, gateTargetY, centerXAtGate - tubeInnerHalfW);
            float rightX = XAtHeight(right, gateTargetY, centerXAtGate + tubeInnerHalfW);
            Vector2 foundGatePt = new Vector2((leftX + rightX) * 0.5f, gateTargetY);
            float foundGateAngle = 0f;
            float foundGateWidth = Mathf.Max(tubeInnerHalfW * 1.5f, Mathf.Abs(rightX - leftX));

            // En öndeki top kapağın üstünde durur: alt noktası kapak çizgisinin hemen üstünde
            const float gateHalfThick = 0.029f;
            float foundRestDist = ArcAtHeight(center, arc, gateTargetY + gateHalfThick + r + 0.008f, foundGateDist + r);

            return new FeedTubeCurveData
            {
                centerPath = center,
                leftEdgePath = left,
                rightEdgePath = right,
                leftShadowPath = leftShad,
                rightShadowPath = rightShad,
                arcLengths = arc,
                totalLength = accLen,
                innerHalfWidth = tubeInnerHalfW,
                wallWidthRatio = wallRatio,
                mouthPoint = p0,
                gateDist = foundGateDist,
                gatePoint = foundGatePt,
                gateAngleDeg = foundGateAngle,
                gateWidth = foundGateWidth,
                restDist = foundRestDist
            };
        }

        /// <summary>
        /// Eşleşmiş sol/sağ kenar çizgilerini verilen yüksekliğin altında kalan kısımdan kırpar. Altta kalan noktalar
        /// kesim çizgisine (o kenarın y = cutY kesişimine) taşınır, böylece tüpün alt ucu düz yatay bir kesim olur.
        /// </summary>
        private static void ClipPairToHeight(ref Vector3[] leftPath, ref Vector3[] rightPath, float cutY)
        {
            int n = Mathf.Min(leftPath.Length, rightPath.Length);
            Vector3 cutL = new Vector3(XAtHeight(leftPath, cutY, leftPath[0].x), cutY, 0f);
            Vector3 cutR = new Vector3(XAtHeight(rightPath, cutY, rightPath[0].x), cutY, 0f);

            var outL = new List<Vector3>(n + 1);
            var outR = new List<Vector3>(n + 1);
            outL.Add(cutL);
            outR.Add(cutR);
            for (int i = 0; i < n; i++)
            {
                bool lBelow = leftPath[i].y <= cutY;
                bool rBelow = rightPath[i].y <= cutY;
                if (lBelow && rBelow) continue; // tamamen camın içinde: çizilmez
                outL.Add(lBelow ? cutL : leftPath[i]);
                outR.Add(rBelow ? cutR : rightPath[i]);
            }
            leftPath = outL.ToArray();
            rightPath = outR.ToArray();
        }

        /// <summary>Çoklu çizginin verilen yüksekliği ilk kestiği noktadaki X (alttan yukarı).</summary>
        private static float XAtHeight(Vector3[] path, float y, float fallback)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float y0 = path[i - 1].y, y1 = path[i].y;
                if ((y0 <= y && y1 >= y) || (y0 >= y && y1 <= y))
                {
                    float frac = Mathf.Abs(y1 - y0) > 0.0001f ? (y - y0) / (y1 - y0) : 0f;
                    return Mathf.Lerp(path[i - 1].x, path[i].x, frac);
                }
            }
            return fallback;
        }

        /// <summary>Merkez çizgisinin verilen yüksekliğe ulaştığı noktanın ağızdan uzaklığı.</summary>
        private static float ArcAtHeight(Vector3[] center, float[] arc, float y, float fallback)
        {
            for (int i = 1; i < center.Length; i++)
            {
                if (center[i].y >= y)
                {
                    float y0 = center[i - 1].y, y1 = center[i].y;
                    float frac = Mathf.Clamp01((y - y0) / Mathf.Max(0.0001f, y1 - y0));
                    return Mathf.Lerp(arc[i - 1], arc[i], frac);
                }
            }
            return fallback;
        }

        private static Vector3 EvalCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            Vector2 p = uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
            return new Vector3(p.x, p.y, 0f);
        }
    }

    /// <summary>
    /// Runtime controller that lines remaining level balls up inside the upper curved glass feed tubes
    /// and pours them smoothly into the main glass container as space opens up.
    /// </summary>
    public class BallFeedTubeController : MonoBehaviour
    {
        public static BallFeedTubeController Instance { get; private set; }

        private sealed class TubeBallSlot
        {
            public BallPhysicsController ball;
            public float currentDist;
            public float targetDist;
        }

        private sealed class TubeLane
        {
            public bool active;
            public FeedTubeCurveData curve;
            public readonly List<TubeBallSlot> visibleSlots = new List<TubeBallSlot>(16);
            public readonly Queue<PuzzleColor> overflowColors = new Queue<PuzzleColor>();
            public float dropCooldown;

            public int TotalPendingCount => visibleSlots.Count + overflowColors.Count;

            public void Clear()
            {
                active = false;
                visibleSlots.Clear();
                overflowColors.Clear();
                dropCooldown = 0f;
            }
        }

        private ShapedGlassContainer _glass;
        private readonly TubeLane _leftLane = new TubeLane();
        private readonly TubeLane _rightLane = new TubeLane();

        private bool _isEnabled = false;
        private int _containerLimit = 12;
        private int _visibleQueueLimit = 5;
        private float _dropInterval = 0.14f;
        private float _ballRadius = 0.12f;
        private bool _preferLeftNext = true;
        private bool _gatesOpened = false;
        private float _elapsedSinceLoad = 0f;
        private float _pourSettleTimer = 0f;
        private float _postOpenElapsed = 0f;
        private float _stuckTimer = 0f;

        /// <summary>
        /// Camda top kaldığı hâlde hiçbiri toplanamıyorsa (rafa/köşeye sıkışmış, hepsi duruyor) kapak bu süre sonra açılır.
        /// Yoksa kapak hiç açılmaz ve level ne biter ne de "hamle kalmadı" der.
        /// </summary>
        private const float StuckGateOpenDelay = 1.5f;

        public bool IsActive => _isEnabled;
        public bool GatesOpened => _gatesOpened;
        public bool IsPouringOrSettling => _isEnabled && _gatesOpened && (PendingQueuedCount > 0 || _pourSettleTimer > 0f) && _postOpenElapsed < 4.0f;
        public int OverflowQueuedCount => _leftLane.overflowColors.Count + _rightLane.overflowColors.Count;
        public int PendingQueuedCount => _leftLane.TotalPendingCount + _rightLane.TotalPendingCount;
        public bool HasPendingBalls => _isEnabled && PendingQueuedCount > 0;

        public static BallFeedTubeController GetOrCreate(Transform parent = null)
        {
            if (Instance != null) return Instance;
            Instance = FindFirstObjectByType<BallFeedTubeController>();
            if (Instance != null) return Instance;

            GameObject obj = new GameObject("BallFeedTubeController");
            if (parent != null) obj.transform.SetParent(parent, false);
            Instance = obj.AddComponent<BallFeedTubeController>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ClearAll()
        {
            _isEnabled = false;
            _gatesOpened = false;
            _elapsedSinceLoad = 0f;
            _pourSettleTimer = 0f;
            _postOpenElapsed = 0f;
            _stuckTimer = 0f;
            _leftLane.Clear();
            _rightLane.Clear();
            _preferLeftNext = true;
        }

        /// <summary>
        /// Queues excess level balls into the active left/right glass feed tubes.
        /// Spawned tube balls enter BallState.InFeedTube (kinematic) and cascade down cleanly into InContainer.
        /// </summary>
        public void ConfigureAndPopulate(
            ShapedGlassContainer glass,
            List<PuzzleColor> queuedColorsInDropOrder,
            int containerLimit,
            int visibleQueueCount,
            float widthMultiplier,
            float anchorSpread,
            float dropInterval,
            float ballRadius)
        {
            ClearAll();
            _glass = glass;
            if (_glass == null || !_glass.useBallFeedTubes) return;

            _containerLimit = Mathf.Max(1, containerLimit);
            _visibleQueueLimit = Mathf.Clamp(visibleQueueCount, 2, 14);
            _dropInterval = Mathf.Clamp(dropInterval, 0.04f, 0.60f);
            _ballRadius = Mathf.Clamp(ballRadius, 0.06f, 0.17f);

            var mode = _glass.feedTubeMode;
            bool useLeft = mode == BallFeedTubeMode.BothLeftAndRight || mode == BallFeedTubeMode.LeftOnly;
            bool useRight = mode == BallFeedTubeMode.BothLeftAndRight || mode == BallFeedTubeMode.RightOnly;

            BallFeedTubeUtility.GetTopAnchorFromContour(
                _glass.contourPoints,
                _glass.widthScale,
                _glass.heightScale,
                out float ceilY,
                out float topHalfW,
                out float topCenterX);

            if (useLeft)
            {
                _leftLane.active = true;
                _leftLane.curve = BallFeedTubeUtility.BuildTubeCurve(
                    true, ceilY, topHalfW, topCenterX, _ballRadius, widthMultiplier, anchorSpread);
            }
            if (useRight)
            {
                _rightLane.active = true;
                _rightLane.curve = BallFeedTubeUtility.BuildTubeCurve(
                    false, ceilY, topHalfW, topCenterX, _ballRadius, widthMultiplier, anchorSpread);
            }

            if (queuedColorsInDropOrder != null && queuedColorsInDropOrder.Count > 0)
            {
                bool flip = true;
                for (int i = 0; i < queuedColorsInDropOrder.Count; i++)
                {
                    PuzzleColor col = queuedColorsInDropOrder[i];
                    TubeLane targetLane = SelectLaneForEnqueue(useLeft, useRight, ref flip);
                    if (targetLane == null) continue;

                    if (targetLane.visibleSlots.Count < _visibleQueueLimit)
                    {
                        AddVisibleTubeBall(targetLane, col, targetLane.visibleSlots.Count, fromTopSpawn: false);
                    }
                    else
                    {
                        targetLane.overflowColors.Enqueue(col);
                    }
                }
            }

            _isEnabled = true;
            _gatesOpened = false;
            _elapsedSinceLoad = 0f;
            _pourSettleTimer = 0f;
            _postOpenElapsed = 0f;
            _glass.SetFeedTubeGatesOpen(false, immediate: true);
        }

        private TubeLane SelectLaneForEnqueue(bool useLeft, bool useRight, ref bool flip)
        {
            if (useLeft && useRight)
            {
                TubeLane chosen = flip ? _leftLane : _rightLane;
                flip = !flip;
                return chosen;
            }
            if (useLeft) return _leftLane;
            if (useRight) return _rightLane;
            return null;
        }

        private void AddVisibleTubeBall(TubeLane lane, PuzzleColor color, int slotIndex, bool fromTopSpawn)
        {
            if (lane == null || !lane.active || !lane.curve.IsValid || BallObjectPool.Instance == null) return;

            float spacing = _ballRadius * 2.15f;
            // While barrier gate is closed, first ball rests cleanly right on top of the gate line
            float firstDist = _gatesOpened ? 0f : lane.curve.restDist;
            float targetDist = Mathf.Min(lane.curve.totalLength - _ballRadius, firstDist + slotIndex * spacing);
            float startDist = fromTopSpawn
                ? Mathf.Min(lane.curve.totalLength - _ballRadius * 0.5f, Mathf.Max(targetDist + spacing * 1.35f, targetDist))
                : targetDist;

            lane.curve.EvaluateAtDistance(startDist, out Vector2 localPos2D, out _);
            Vector3 worldPos = _glass != null
                ? _glass.transform.TransformPoint(new Vector3(localPos2D.x, localPos2D.y, -0.10f))
                : new Vector3(localPos2D.x, localPos2D.y, -0.10f);

            var ball = BallObjectPool.Instance.SpawnBall(color, worldPos, _ballRadius);
            if (ball == null) return;

            ball.state = BallState.InFeedTube;
            ball.MakeKinematic();
            if (ball.sphereCollider != null) ball.sphereCollider.enabled = false;
            ball.ConfigurePoolLayer(99, worldPos.x, -10f, _ballRadius * 1.85f);

            lane.visibleSlots.Add(new TubeBallSlot
            {
                ball = ball,
                currentDist = startDist,
                targetDist = targetDist
            });
        }

        private void Update()
        {
            if (!_isEnabled || !Application.isPlaying || _glass == null) return;

            var gm = PuzzleGlassGameManager.Instance;
            if (gm != null && (gm.isLevelCompleted || gm.isLevelFailed)) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _elapsedSinceLoad += dt;

            // Keep barrier closed until EVERY ball inside the main glass bottle has been collected
            if (!_gatesOpened)
            {
                bool openGate = false;
                if (_elapsedSinceLoad >= 0.30f)
                {
                    int inGlass = CountContainerBallsForGate();
                    if (inGlass == 0)
                    {
                        openGate = true;
                    }
                    else
                    {
                        // Camda top var ama hiçbiri toplanamıyor ve hiçbiri hareket etmiyor: sıkışma. Kısa bir
                        // bekleme sonra kapağı yine de aç ki oyun kilitlenmesin.
                        bool stuck = !HasBallBeingCollected()
                                     && !HasHeldContainerBall()
                                     && !HasFastFallingContainerBall()
                                     && BallCollectionController.Instance != null
                                     && !BallCollectionController.Instance.HasAnyCollectableBottomBall();
                        _stuckTimer = stuck ? _stuckTimer + dt : 0f;
                        if (_stuckTimer >= StuckGateOpenDelay)
                        {
                            openGate = true;
                            Debug.LogWarning($"[BallFeedTube] Camda {inGlass} top toplanamaz durumda kaldı; kilitlenmeyi önlemek için tüp kapakları açıldı.");
                        }
                    }
                }

                if (openGate)
                {
                    _gatesOpened = true;
                    _pourSettleTimer = 0.42f;
                    _postOpenElapsed = 0f;
                    _glass.SetFeedTubeGatesOpen(true, immediate: false);
                    PuzzleSoundManager.Instance?.PlayUnlock();
                    _leftLane.dropCooldown = 0.08f;
                    _rightLane.dropCooldown = 0.12f;
                }
            }
            else
            {
                _postOpenElapsed += dt;
                if (PendingQueuedCount > 0)
                {
                    _pourSettleTimer = 0.42f;
                }
                else if (_pourSettleTimer > 0f)
                {
                    if (HasFastFallingContainerBall())
                    {
                        _pourSettleTimer = Mathf.Max(_pourSettleTimer, 0.20f);
                    }
                    else
                    {
                        _pourSettleTimer = Mathf.Max(0f, _pourSettleTimer - dt);
                        if (_pourSettleTimer <= 0f)
                        {
                            BallCollectionController.Instance?.MarkBallFieldDirty();
                        }
                    }
                }
            }

            StepLaneVisualSlide(_leftLane, dt);
            StepLaneVisualSlide(_rightLane, dt);

            if (!_gatesOpened || PendingQueuedCount <= 0) return;

            int inContainerCount = CountContainerBallsForGate();
            int postOpenCap = Mathf.Max(_containerLimit, 24);
            if (inContainerCount >= postOpenCap) return;

            // Alternate which tube pours next so both sides flow balanced like twin hourglass chutes
            TubeLane first = _preferLeftNext ? _leftLane : _rightLane;
            TubeLane second = _preferLeftNext ? _rightLane : _leftLane;

            if (TryReleaseFrontBall(first))
            {
                _preferLeftNext = !_preferLeftNext;
            }
            else if (TryReleaseFrontBall(second))
            {
                _preferLeftNext = !_preferLeftNext;
            }
        }

        private void StepLaneVisualSlide(TubeLane lane, float dt)
        {
            if (!lane.active || !lane.curve.IsValid) return;
            if (lane.dropCooldown > 0f) lane.dropCooldown = Mathf.Max(0f, lane.dropCooldown - dt);

            float spacing = _ballRadius * 2.15f;
            float firstDist = _gatesOpened ? 0f : lane.curve.restDist;
            float slideSpeed = Mathf.Max(3.6f, _ballRadius * 38f);

            for (int i = 0; i < lane.visibleSlots.Count; i++)
            {
                var slot = lane.visibleSlots[i];
                if (slot.ball == null || slot.ball.state != BallState.InFeedTube) continue;

                float prevDist = slot.currentDist;
                slot.targetDist = Mathf.Min(lane.curve.totalLength - _ballRadius, firstDist + i * spacing);
                slot.currentDist = Mathf.MoveTowards(slot.currentDist, slot.targetDist, slideSpeed * dt);
                float deltaDist = prevDist - slot.currentDist;

                lane.curve.EvaluateAtDistance(slot.currentDist, out Vector2 local2D, out _);
                Vector3 worldPos = _glass.transform.TransformPoint(new Vector3(local2D.x, local2D.y, -0.10f));
                slot.ball.transform.position = worldPos;
                if (Mathf.Abs(deltaDist) > 0.0001f)
                {
                    bool isRightSide = ReferenceEquals(lane, _rightLane);
                    float rollDeg = (deltaDist / Mathf.Max(0.04f, _ballRadius)) * Mathf.Rad2Deg * (isRightSide ? 1f : -1f);
                    slot.ball.transform.Rotate(0f, 0f, rollDeg, Space.Self);
                }
            }
        }

        private bool TryReleaseFrontBall(TubeLane lane)
        {
            if (!lane.active || lane.dropCooldown > 0f || lane.visibleSlots.Count == 0) return false;

            var front = lane.visibleSlots[0];
            if (front.ball == null)
            {
                lane.visibleSlots.RemoveAt(0);
                return false;
            }

            // Wait until front ball clears past the opened gate down inside the shoulder before switching to physics
            if (front.currentDist > _ballRadius * 1.05f) return false;

            BallPhysicsController ball = front.ball;
            lane.visibleSlots.RemoveAt(0);
            lane.dropCooldown = Mathf.Clamp(_dropInterval, 0.095f, 0.22f);

            // Place strictly INSIDE the container chamber below TopCeilingCollider and inside side wall colliders
            lane.curve.EvaluateAtDistance(0f, out Vector2 mouth2D, out Vector2 tangentUp);
            float localX = mouth2D.x;
            float localY = mouth2D.y;

            BallFeedTubeUtility.GetTopAnchorFromContour(_glass.contourPoints, _glass.widthScale, _glass.heightScale, out float ceilY, out float topHalfW, out _);
            localY = Mathf.Min(localY, ceilY - _ballRadius * 1.42f);

            _glass.GetSafeCubeBoundsAtY(localY, out float safeL, out float safeR);
            if (safeR > safeL + 0.02f)
            {
                localX = Mathf.Clamp(localX, safeL + _ballRadius * 0.35f, safeR - _ballRadius * 0.35f);
            }
            else
            {
                float maxInsideX = Mathf.Max(0.05f, topHalfW - _ballRadius * 1.55f);
                localX = Mathf.Clamp(localX, -maxInsideX, maxInsideX);
            }

            Vector3 mouthWorld = _glass.transform.TransformPoint(new Vector3(localX, localY, -0.10f));
            ball.transform.position = mouthWorld;

            ball.state = BallState.InContainer;
            if (ball.sphereCollider != null) ball.sphereCollider.enabled = true;
            ball.ReleaseToPhysics();
            if (ball.rb != null)
            {
                float inwardSign = localX > 0.01f ? -1f : (localX < -0.01f ? 1f : 0f);
                ball.rb.linearVelocity = new Vector3(inwardSign * 0.28f, -0.78f, 0f);
                ball.rb.angularVelocity = new Vector3(0f, 0f, -inwardSign * 5.5f);
            }
            PuzzleSoundManager.Instance?.PlayBallTap(0.22f);

            if (lane.overflowColors.Count > 0)
            {
                PuzzleColor nextColor = lane.overflowColors.Dequeue();
                AddVisibleTubeBall(lane, nextColor, lane.visibleSlots.Count, fromTopSpawn: true);
            }

            BallCollectionController.Instance?.MarkBallFieldDirty();
            return true;
        }

        private static int CountContainerBallsForGate()
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return 0;

            int count = 0;
            var active = pool.ActiveBalls;
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                if (b != null && b.gameObject.activeInHierarchy &&
                    (b.state == BallState.InContainer || b.state == BallState.AttractingToExit))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Level başındaki şekil tutma süresinde camdaki toplar kinematik bekler; bu sırada sıkışma sayılmaz.</summary>
        private static bool HasHeldContainerBall()
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return false;
            var active = pool.ActiveBalls;
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                // Burst pist simülasyonundaki toplar kinematiktir ama "tutulmuş" değildir (serbestçe akarlar)
                if (b != null && b.gameObject.activeInHierarchy && b.state == BallState.InContainer && b.rb != null && b.rb.isKinematic && !b.burstConfigured)
                    return true;
            }
            return false;
        }

        /// <summary>Şu an bir bloğa doğru çekilen (toplanmakta olan) top var mı?</summary>
        private static bool HasBallBeingCollected()
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return false;
            var active = pool.ActiveBalls;
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                if (b != null && b.gameObject.activeInHierarchy && b.state == BallState.AttractingToExit) return true;
            }
            return false;
        }

        private static bool HasFastFallingContainerBall()
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return false;

            var active = pool.ActiveBalls;
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                if (b != null && b.gameObject.activeInHierarchy && b.state == BallState.InContainer && b.rb != null && !b.rb.isKinematic)
                {
                    if (b.rb.linearVelocity.sqrMagnitude > 0.35f) return true;
                }
            }
            return false;
        }
    }
}
