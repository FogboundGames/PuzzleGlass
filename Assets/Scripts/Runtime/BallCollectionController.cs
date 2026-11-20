using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public class BallCollectionController : MonoBehaviour
    {
        public static BallCollectionController Instance { get; private set; }

        [Header("Attraction Physics Settings")]
        [Tooltip("Force pulling matching balls down towards the spout exit.")]
        public float attractionPullForce = 15f;

        [Tooltip("Maximum velocity for balls while navigating towards exit.")]
        public float maxBallSpeed = 2.05f;

        [Tooltip("Interval between initiating collection of successive balls from the spout.")]
        [Range(0.05f, 0.50f)]
        public float collectionInterval = 0.085f;

        [Tooltip("Maximum balls simultaneously traveling through the spout neck.")]
        public int maxBallsInSpout = 8;

        [Tooltip("Maximum balls currently traveling in air per monster.")]
        public int maxBallsInTransitPerMonster = 10;

        [Tooltip("Vertical tolerance around bottom exit cap to trigger mouth launch.")]
        public float exitTriggerYOffset = 0.10f;

        [Tooltip("Horizontal tolerance around bottom exit cap center so balls always route through the cap.")]
        public float exitCollarHorizontalTolerance = 0.16f;

        [Tooltip("Vertical tolerance for balls considered part of the bottom layer in container.")]
        public float bottomLayerTolerance = 0.14f;

        [Tooltip("Açıkken tabandaki aynı renk toplar yatay kuvvetle kutuya doğru itilir (kayma). Kapalı: toplar sadece dümdüz aşağı çekilir.")]
        public bool enableFloorDrift = false;

        [Header("State (Read Only)")]
        [SerializeField] private int _totalCollectedThisLevel = 0;
        private float _lastGlobalCollectionTime = -10f;
        private float _levelStartTime = -10f;

        private class ActiveMonsterStream
        {
            public MonsterCharacter monster;
            public float lastCollectionTime;
            public float placedTime;
            public bool playedBlockedFeedback;
            public List<BallPhysicsController> inTransitBalls = new List<BallPhysicsController>();
        }

        private readonly Dictionary<string, ActiveMonsterStream> _activeStreams = new Dictionary<string, ActiveMonsterStream>();
        private readonly List<BallPhysicsController> _attractingBalls = new List<BallPhysicsController>();

        // Pillar 1 & 3: Centralized GPU squash list + Dirty-flagged spatial bottom-candidate cache (0 GC, O(N) instead of O(N^2))
        private readonly List<BallPhysicsController> _squashingBalls = new List<BallPhysicsController>(64);
        private readonly List<BallPhysicsController> _bottomCandidatesCache = new List<BallPhysicsController>(32);
        private readonly List<string> _keysToRemoveCache = new List<string>(8);
        private readonly List<MonsterCharacter> _idleGridToPauseCache = new List<MonsterCharacter>(8);

        private const int SpatialBucketCount = 64;
        private const int SpatialBucketHalf = 32;
        private readonly int[] _colBucketHead = new int[SpatialBucketCount];
        private int[] _colBucketNext = new int[256];
        private Vector3[] _cachedLocalPos = new Vector3[256];
        private bool _ballFieldDirty = true;
        private float _lastBallFieldRebuildTime = -10f;

        public void MarkBallFieldDirty()
        {
            _ballFieldDirty = true;
        }

        public void RegisterSquashingBall(BallPhysicsController ball)
        {
            if (ball == null) return;
            if (!_squashingBalls.Contains(ball))
            {
                _squashingBalls.Add(ball);
            }
        }

        public event Action<BallPhysicsController, MonsterCharacter> OnBallCollected;
        public event Action OnAllMatchingBallsCollected;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Instance = this;
            _levelStartTime = Time.time;
        }

        private void Update()
        {
            StepSquashingBalls(Time.deltaTime);

            // If SandSimulation is actively running with sand grains (and no physical balls), yield to sand engine
            if (SandSimulationEngine.Instance != null && SandSimulationEngine.Instance.enabled && SandSimulationEngine.Instance.sandAmount > 0)
            {
                if (BallObjectPool.Instance == null || BallObjectPool.Instance.ActiveBalls.Count == 0)
                    return;
            }

            AutoActivateMatchingTopEdgeBlocks();
            PollSlottedMonsters();
        }

        private void StepSquashingBalls(float dt)
        {
            for (int i = _squashingBalls.Count - 1; i >= 0; i--)
            {
                var ball = _squashingBalls[i];
                if (ball == null || !ball.IsGpuSquashing || !ball.gameObject.activeInHierarchy)
                {
                    int last = _squashingBalls.Count - 1;
                    if (i != last) _squashingBalls[i] = _squashingBalls[last];
                    _squashingBalls.RemoveAt(last);
                    continue;
                }
                ball.StepGpuVisualSquash(dt);
                if (!ball.IsGpuSquashing)
                {
                    int last = _squashingBalls.Count - 1;
                    if (i != last) _squashingBalls[i] = _squashingBalls[last];
                    _squashingBalls.RemoveAt(last);
                }
            }
        }

        /// <summary>
        /// Automatically activates any grid Carrier Block docked at the top row ("en üst taraf")
        /// whenever the bottom floor of the glass exposes balls of that block's color.
        /// </summary>
        private void AutoActivateMatchingTopEdgeBlocks()
        {
            if (SlotSystem.IsActive) return;
            if (LevelTransitionController.IsTransitioning)
            {
                _levelStartTime = Time.time;
                return;
            }
            if (Time.time - _levelStartTime < 0.35f) return;
            var grid = StagingGridBoard.Instance;
            var gm = PuzzleGlassGameManager.Instance;
            if (grid == null || gm == null || gm.activeMonsters == null || gm.isLevelCompleted || gm.isLevelFailed) return;

            var drag = MonsterDragController.Instance;
            for (int i = 0; i < gm.activeMonsters.Count; i++)
            {
                var m = gm.activeMonsters[i];
                if (m == null || m.IsCompleted || m.remainingCount <= 0 || !m.gameObject.activeInHierarchy || m.isCollectingInGrid || m.IsMovementOrCollectionBlocked)
                    continue;
                if (drag != null && drag.IsBlockCurrentlyDragged(m))
                    continue;

                if (grid.IsBlockAtTopExit(m))
                {
                    m.armedAtTopEdge = true;
                    m.TryStartInGridCollection(playDenialFeedback: false);
                }
            }
        }

        private void FixedUpdate()
        {
            StepAllBallPhysics();
            UpdateAttractionPhysics();
            ApplyFloorDriftTowardHungryMonsters();
        }

        private void StepAllBallPhysics()
        {
            if (BallObjectPool.Instance == null) return;
            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return;

            float fixedDt = Time.fixedDeltaTime;
            bool anyAwakeMoving = false;

            // Pist camı: toplar Bus Traffic Fever tarzı sıralı kuyruk halinde çıkışa yürür (fizik yok)
            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (container != null && container.IsTrackShape && TrackCrowdQueue.Step(container, activeBalls, fixedDt))
            {
                anyAwakeMoving = true;
            }
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null) continue;
                b.StepBallPhysics(fixedDt);
                if (!anyAwakeMoving && b.state == BallState.InContainer && b.rb != null && !b.rb.isKinematic && !b.rb.IsSleeping())
                {
                    if (b.rb.linearVelocity.sqrMagnitude > 0.0025f)
                    {
                        anyAwakeMoving = true;
                    }
                }
            }

            if (anyAwakeMoving)
            {
                _ballFieldDirty = true;
            }
        }

        /// <summary>
        /// Called when a monster is placed into a slot to start its collection stream.
        /// </summary>
        public void OnMonsterPlacedInSlot(MonsterCharacter monster)
        {
            if (monster == null) return;

            string key = monster.monsterId;
            if (!_activeStreams.ContainsKey(key))
            {
                _activeStreams[key] = new ActiveMonsterStream
                {
                    monster = monster,
                    lastCollectionTime = -10f,
                    placedTime = Time.time,
                    playedBlockedFeedback = false
                };
            }
            else
            {
                _activeStreams[key].placedTime = Time.time;
                _activeStreams[key].playedBlockedFeedback = false;
            }
        }

        /// <summary>
        /// Called when a player drags or taps a monster out of its slot to return it to the staging tray or move it.
        /// Immediately stops its collection stream and releases any still-in-container attracting balls back to the pool.
        /// </summary>
        public void OnMonsterRemovedFromSlot(MonsterCharacter monster)
        {
            if (monster == null) return;

            string key = monster.monsterId;
            if (_activeStreams.TryGetValue(key, out ActiveMonsterStream stream))
            {
                for (int i = stream.inTransitBalls.Count - 1; i >= 0; i--)
                {
                    var b = stream.inTransitBalls[i];
                    if (b != null && b.state == BallState.AttractingToExit)
                    {
                        b.CancelAttractionAndReturnToContainer();
                        _attractingBalls.Remove(b);
                        stream.inTransitBalls.RemoveAt(i);
                    }
                }
                _activeStreams.Remove(key);
            }

            for (int i = _attractingBalls.Count - 1; i >= 0; i--)
            {
                var b = _attractingBalls[i];
                if (b != null && b.TargetMonster == monster && b.state == BallState.AttractingToExit)
                {
                    b.CancelAttractionAndReturnToContainer();
                    _attractingBalls.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Registers a grid Carrier Block as an active collector stream in place (no upper slot needed).
        /// </summary>
        public void ActivateGridCollector(MonsterCharacter monster)
        {
            OnMonsterPlacedInSlot(monster);
        }

        /// <summary>
        /// Checks active grid collectors (and any legacy slotted boxes) and pulls matching bottom-floor balls.
        /// </summary>
        private void PollSlottedMonsters()
        {
            // Remove any streams whose block was detached, completed or hidden
            _keysToRemoveCache.Clear();
            foreach (var kvp in _activeStreams)
            {
                var s = kvp.Value;
                if (s.monster == null || !s.monster.IsActivelyCollecting)
                {
                    _keysToRemoveCache.Add(kvp.Key);
                }
            }
            if (_keysToRemoveCache.Count > 0)
            {
                for (int i = 0; i < _keysToRemoveCache.Count; i++)
                {
                    _activeStreams.Remove(_keysToRemoveCache[i]);
                }
            }

            // Register any actively collecting monster from PuzzleGlassGameManager
            if (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.activeMonsters != null)
            {
                var all = PuzzleGlassGameManager.Instance.activeMonsters;
                for (int i = 0; i < all.Count; i++)
                {
                    var m = all[i];
                    if (m != null && m.IsActivelyCollecting)
                    {
                        string id = m.monsterId;
                        if (!string.IsNullOrEmpty(id) && !_activeStreams.ContainsKey(id))
                        {
                            _activeStreams[id] = new ActiveMonsterStream
                            {
                                monster = m,
                                lastCollectionTime = -10f,
                                placedTime = Time.time,
                                playedBlockedFeedback = false
                            };
                        }
                    }
                }
            }

            if (_activeStreams.Count == 0) return;

            // Clean up finished balls in transit for all streams
            foreach (var kvp in _activeStreams)
            {
                var stream = kvp.Value;
                for (int b = stream.inTransitBalls.Count - 1; b >= 0; b--)
                {
                    var ball = stream.inTransitBalls[b];
                    if (ball == null || ball.state == BallState.Pooled || !ball.gameObject.activeInHierarchy)
                    {
                        stream.inTransitBalls.RemoveAt(b);
                    }
                }
            }

            // Pacing & congestion limit: ensure smooth, readable flow without overcrowding
            // During Fever Mode (Streak >= 3) or when a VIP Slot is actively collecting, vacuum speed is 2x faster!
            bool isFever = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive;
            bool isVipCollecting = false;
            foreach (var s in _activeStreams.Values)
            {
                if (s.monster != null && s.monster.assignedSlot != null && s.monster.assignedSlot.isVip && (s.monster.remainingCount - s.inTransitBalls.Count > 0))
                {
                    isVipCollecting = true;
                    break;
                }
            }

            float effectiveInterval = (isFever || isVipCollecting) ? (collectionInterval * 0.55f) : collectionInterval;
            int effectiveMaxInSpout = (isFever || isVipCollecting) ? (maxBallsInSpout + 2) : maxBallsInSpout;

            // Bant çıkışın önünden sürekli top geçirir: uyan topları kaçırmamak için daha sık ve çok top al
            var beltContainer = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (beltContainer != null && TrackCrowdQueue.HasLoop(beltContainer))
            {
                effectiveInterval = Mathf.Min(effectiveInterval, 0.035f);
                effectiveMaxInSpout = Mathf.Max(effectiveMaxInSpout, 14);
            }

            float now = Time.time;
            if (now - _lastGlobalCollectionTime < effectiveInterval) return;
            if (_attractingBalls.Count >= effectiveMaxInSpout) return;

            // Find a physically accessible bottom-floor ball matching an active box's aligned columns
            BallPhysicsController bestBall = FindNextCollectableBall(out ActiveMonsterStream targetStream);
            if (bestBall != null && targetStream != null)
            {
                StartBallAttraction(bestBall, targetStream.monster, targetStream);
                _lastGlobalCollectionTime = now;
                targetStream.lastCollectionTime = now;
                targetStream.playedBlockedFeedback = true;
            }

            // Pause any in-grid stream that has no balls in flight and no reachable matching balls in its own columns
            _idleGridToPauseCache.Clear();
            foreach (var kvp in _activeStreams)
            {
                var s = kvp.Value;
                if (s.monster == null || s == targetStream) continue;

                if (!s.playedBlockedFeedback && s.inTransitBalls.Count == 0 && (now - s.placedTime) > 0.25f)
                {
                    s.playedBlockedFeedback = true;
                    s.monster.PlayWaitingForBallsFeedback();
                }

                if (s.monster.assignedSlot == null &&
                    s.inTransitBalls.Count == 0 &&
                    (now - s.placedTime) > 0.45f &&
                    (now - s.lastCollectionTime) > 0.45f &&
                    !HasReachableBottomBallForColor(s.monster.colorType, s.monster))
                {
                    _idleGridToPauseCache.Add(s.monster);
                }
            }

            if (_idleGridToPauseCache.Count > 0)
            {
                for (int i = 0; i < _idleGridToPauseCache.Count; i++)
                {
                    _idleGridToPauseCache[i].PauseInGridCollection();
                }
            }
        }

        public bool HasBallsInTransitFor(MonsterCharacter monster)
        {
            if (monster == null || string.IsNullOrEmpty(monster.monsterId)) return false;
            if (_activeStreams.TryGetValue(monster.monsterId, out ActiveMonsterStream stream))
            {
                return stream.inTransitBalls != null && stream.inTransitBalls.Count > 0;
            }
            return false;
        }

        private readonly float[] _cachedTrackExitDist = new float[512];
        private readonly Vector2[] _cachedTrackFlowDir = new Vector2[512];

        private bool IsBallInMonsterCollectionZone(MonsterCharacter monster, BallPhysicsController ball)
        {
            if (monster == null || ball == null) return false;
            if (monster.assignedSlot != null) return true;

            var grid = StagingGridBoard.Instance;
            if (grid == null) return true;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;
            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;

            if (container != null && container.IsTrackShape)
            {
                return grid.HasAnyOpenTopColumn(monster, allMonsters);
            }

            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float edgeTolerance = Mathf.Max(0.06f, ballDiam * 0.32f);

            return grid.IsWorldXAlignedWithBlockTopColumns(monster, ball.transform.position.x, edgeTolerance, allMonsters);
        }

        /// <summary>
        /// Returns true if at least one unblocked bottom-row ball inside the glass container matches targetColor
        /// (and lies within forMonster's aligned horizontal zone when provided).
        /// </summary>
        public bool HasReachableBottomBallForColor(PuzzleColor targetColor, MonsterCharacter forMonster = null)
        {
            List<BallPhysicsController> bottomCandidates = CollectBottomCandidates();
            for (int i = 0; i < bottomCandidates.Count; i++)
            {
                var cand = bottomCandidates[i];
                if (cand == null || cand.colorType != targetColor) continue;
                if (forMonster != null && !IsBallInMonsterCollectionZone(forMonster, cand)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Camda şu an alttan toplanabilecek (rafta/sıkışmış olmayan, en altta duran) en az bir top var mı?
        /// Hiç yoksa camdaki toplar hiçbir blok tarafından alınamaz durumdadır.
        /// </summary>
        public bool HasAnyCollectableBottomBall()
        {
            return CollectBottomCandidates().Count > 0;
        }

        private List<BallPhysicsController> CollectBottomCandidates()
        {
            if (BallObjectPool.Instance == null)
            {
                _bottomCandidatesCache.Clear();
                return _bottomCandidatesCache;
            }

            if (BallFeedTubeController.Instance != null && BallFeedTubeController.Instance.IsPouringOrSettling)
            {
                _bottomCandidatesCache.Clear();
                return _bottomCandidatesCache;
            }

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0)
            {
                _bottomCandidatesCache.Clear();
                _ballFieldDirty = false;
                return _bottomCandidatesCache;
            }

            // If cached bottom candidates are clean and still valid in container, reuse in O(1)
            if (!_ballFieldDirty && Time.time - _lastBallFieldRebuildTime < 0.20f)
            {
                bool cacheValid = true;
                for (int k = 0; k < _bottomCandidatesCache.Count; k++)
                {
                    var cb = _bottomCandidatesCache[k];
                    if (cb == null || cb.state != BallState.InContainer || !cb.gameObject.activeInHierarchy)
                    {
                        cacheValid = false;
                        break;
                    }
                }
                if (cacheValid) return _bottomCandidatesCache;
            }

            _ballFieldDirty = false;
            _lastBallFieldRebuildTime = Time.time;
            _bottomCandidatesCache.Clear();

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            float floorLocalY = (container != null) ? container.GetChamberFloorY() : -0.56f;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float ballRad = ballDiam * 0.5f;

            // Maximum local Y center for a ball resting at the bottom of its column with no ball underneath it
            float maxBottomFloorCenterLocalY = floorLocalY + ballDiam * 1.15f;

            int activeCount = activeBalls.Count;
            if (_colBucketNext.Length < activeCount)
            {
                int newCap = Mathf.NextPowerOfTwo(activeCount);
                _colBucketNext = new int[newCap];
                _cachedLocalPos = new Vector3[newCap];
            }

            System.Array.Fill(_colBucketHead, -1);
            float cellW = Mathf.Max(0.05f, ballDiam * 0.90f);
            bool hasContainer = container != null;
            Transform containerTr = hasContainer ? container.transform : null;

            if (hasContainer && container.IsTrackShape)
            {
                if (container.compiledTrack == null) container.RebuildTrackCompiledData();

                // Döngülü pist: banttaki her top er geç çıkışın önünden geçer, hepsi ulaşılabilir sayılır.
                // (Gerçek toplama FindNextCollectableBall'da sadece çıkışın önündeki toplarla yapılır.)
                if (TrackCrowdQueue.HasLoop(container))
                {
                    for (int i = 0; i < activeCount; i++)
                    {
                        var b = activeBalls[i];
                        if (b != null && b.state == BallState.InContainer && b.trackLoopSlot >= 0 && b.gameObject.activeInHierarchy)
                            _bottomCandidatesCache.Add(b);
                    }
                    return _bottomCandidatesCache;
                }

                float maxExitZoneDist = ballDiam * 5.0f;
                float blockDistSq = (ballDiam * 1.18f) * (ballDiam * 1.18f);

                for (int i = 0; i < activeCount; i++)
                {
                    var b = activeBalls[i];
                    if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                    Vector3 bLocal = containerTr.InverseTransformPoint(b.transform.position);
                    _cachedLocalPos[i] = bLocal;
                    if (i < _cachedTrackExitDist.Length)
                    {
                        container.SampleTrackField(bLocal, out _, out Vector2 fDir, out float exDist, out _);
                        _cachedTrackExitDist[i] = exDist;
                        _cachedTrackFlowDir[i] = fDir;
                    }
                }

                for (int i = 0; i < activeCount; i++)
                {
                    var b = activeBalls[i];
                    if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                    if (b.rb != null && !b.rb.isKinematic && b.rb.linearVelocity.sqrMagnitude > 3.8f * 3.8f) continue;

                    float myExitDist = i < _cachedTrackExitDist.Length ? _cachedTrackExitDist[i] : 0f;
                    if (myExitDist > maxExitZoneDist) continue;

                    Vector2 myPos = _cachedLocalPos[i];
                    Vector2 myFlow = i < _cachedTrackFlowDir.Length ? _cachedTrackFlowDir[i] : Vector2.down;
                    bool blockedAhead = false;

                    for (int j = 0; j < activeCount; j++)
                    {
                        if (j == i) continue;
                        var o = activeBalls[j];
                        if (o == null || o.state != BallState.InContainer || !o.gameObject.activeInHierarchy) continue;
                        float oExitDist = j < _cachedTrackExitDist.Length ? _cachedTrackExitDist[j] : 99f;
                        if (oExitDist >= myExitDist - ballRad * 0.25f) continue;

                        Vector2 diff = (Vector2)_cachedLocalPos[j] - myPos;
                        float dSq = diff.sqrMagnitude;
                        if (dSq > 1e-6f && dSq < blockDistSq)
                        {
                            float aheadDot = Vector2.Dot(diff / Mathf.Sqrt(dSq), myFlow);
                            if (aheadDot > 0.25f)
                            {
                                blockedAhead = true;
                                break;
                            }
                        }
                    }

                    if (!blockedAhead)
                    {
                        _bottomCandidatesCache.Add(b);
                    }
                }
                return _bottomCandidatesCache;
            }

            // Pass 1 (O(N)): Transform & bucket ONLY balls inside the bottom floor band into 1D spatial X-columns
            for (int i = 0; i < activeCount; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;

                Vector3 bLocal = hasContainer
                    ? containerTr.InverseTransformPoint(b.transform.position)
                    : b.transform.position;
                _cachedLocalPos[i] = bLocal;

                if (bLocal.y > maxBottomFloorCenterLocalY) continue;

                int col = Mathf.Clamp(Mathf.FloorToInt(bLocal.x / cellW) + SpatialBucketHalf, 0, SpatialBucketCount - 1);
                _colBucketNext[i] = _colBucketHead[col];
                _colBucketHead[col] = i;
            }

            // Pass 2 (O(N)): Evaluate each bottom-band ball against ONLY adjacent spatial X-columns (col-1..col+1)
            for (int col = 0; col < SpatialBucketCount; col++)
            {
                int i = _colBucketHead[col];
                while (i != -1)
                {
                    var b = activeBalls[i];
                    if (b.rb == null || b.rb.isKinematic || Mathf.Abs(b.rb.linearVelocity.y) <= 1.35f)
                    {
                        Vector3 bLocal = _cachedLocalPos[i];

                        bool blockedByShelf = false;
                        if (hasContainer && container.dividers != null)
                        {
                            for (int d = 0; d < container.dividers.Count; d++)
                            {
                                var div = container.dividers[d];
                                if (div == null || !div.enabled || div.runtimeIsOpened || div.mechanicType != DividerMechanicType.StaticGlass)
                                    continue;
                                if (Mathf.Abs(div.angleDegrees) < 55f)
                                {
                                    float halfSpanX = div.length * 0.5f;
                                    if (bLocal.y > div.center.y - 0.02f && Mathf.Abs(bLocal.x - div.center.x) <= halfSpanX)
                                    {
                                        blockedByShelf = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!blockedByShelf)
                        {
                            bool blockedFromBelow = false;
                            int minC = Mathf.Max(0, col - 1);
                            int maxC = Mathf.Min(SpatialBucketCount - 1, col + 1);
                            for (int nc = minC; nc <= maxC && !blockedFromBelow; nc++)
                            {
                                int j = _colBucketHead[nc];
                                while (j != -1)
                                {
                                    if (j != i)
                                    {
                                        Vector3 oLocal = _cachedLocalPos[j];
                                        float dy = bLocal.y - oLocal.y;
                                        float dx = Mathf.Abs(bLocal.x - oLocal.x);
                                        if (dy > ballRad * 0.35f && dx < ballDiam * 0.85f &&
                                            ArePositionsInSameBottomCompartment(container, bLocal.x, oLocal.x, floorLocalY + ballRad))
                                        {
                                            blockedFromBelow = true;
                                            break;
                                        }
                                    }
                                    j = _colBucketNext[j];
                                }
                            }

                            if (!blockedFromBelow)
                            {
                                _bottomCandidatesCache.Add(b);
                            }
                        }
                    }
                    i = _colBucketNext[i];
                }
            }

            return _bottomCandidatesCache;
        }

        /// <summary>
        /// Physically evaluates which balls are resting on the bottom/near side of the glass container with zero balls
        /// blocking them from below, and matches them to the closest hungry active Carrier Block.
        /// </summary>
        private BallPhysicsController FindNextCollectableBall(out ActiveMonsterStream targetStream)
        {
            targetStream = null;
            List<BallPhysicsController> bottomCandidates = CollectBottomCandidates();
            if (bottomCandidates.Count == 0) return null;

            // 3. Match against active slotted monsters, prioritizing the ball closest horizontally to each monster's slot!
            BallPhysicsController bestOverallBall = null;
            ActiveMonsterStream bestOverallStream = null;
            float bestOverallScore = float.MaxValue;

            ShapedGlassContainer trackContainer = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;
            bool isTrack = trackContainer != null && trackContainer.IsTrackShape;
            bool isBelt = isTrack && TrackCrowdQueue.HasLoop(trackContainer);

            foreach (var kvp in _activeStreams)
            {
                var stream = kvp.Value;
                var monster = stream.monster;
                if (monster == null) continue;

                int needed = monster.remainingCount - stream.inTransitBalls.Count;
                if (needed <= 0) continue;
                int maxInTransit = isBelt ? Mathf.Max(maxBallsInTransitPerMonster, 16) : maxBallsInTransitPerMonster;
                if (stream.inTransitBalls.Count >= maxInTransit) continue;

                float monsterWorldX = monster.transform.position.x;

                for (int i = 0; i < bottomCandidates.Count; i++)
                {
                    var cand = bottomCandidates[i];
                    if (cand.colorType != monster.colorType) continue;
                    // Bant: sadece o an çıkışın önünden geçen top ayrılabilir; diğerleri tura devam eder
                    if (isBelt && !TrackCrowdQueue.IsAtExit(cand)) continue;
                    if (!IsBallInMonsterCollectionZone(monster, cand)) continue;

                    // Pist modunda çıkış ağzına en yakın top önce gelir; kutu modunda bloğa yatay en yakın top
                    float score;
                    if (isTrack)
                    {
                        Vector3 lp = trackContainer.transform.InverseTransformPoint(cand.transform.position);
                        trackContainer.SampleTrackField(lp, out _, out _, out float exDist, out _);
                        score = exDist;
                    }
                    else
                    {
                        score = Mathf.Abs(cand.transform.position.x - monsterWorldX);
                    }

                    // VIP Slot priority boost: collects before regular slots
                    if (monster.assignedSlot != null && monster.assignedSlot.isVip)
                    {
                        score -= 50f;
                    }

                    if (score < bestOverallScore)
                    {
                        bestOverallScore = score;
                        bestOverallBall = cand;
                        bestOverallStream = stream;
                    }
                }
            }

            if (bestOverallBall != null && bestOverallStream != null)
            {
                targetStream = bestOverallStream;
                return bestOverallBall;
            }

            return null;
        }

        /// <summary>
        /// Gently rolls resting bottom-floor balls laterally toward a hungry slotted monster of the same color
        /// when space opens up beside them, giving the ball pool a lively, magnetic physical response!
        /// </summary>
        private void ApplyFloorDriftTowardHungryMonsters()
        {
            if (!enableFloorDrift) return;
            if (_activeStreams.Count == 0 || BallObjectPool.Instance == null) return;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;

            float floorLocalY = (container != null) ? container.GetChamberFloorY() : -0.56f;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float maxBottomY = floorLocalY + ballDiam * 0.85f;

            foreach (var kvp in _activeStreams)
            {
                var monster = kvp.Value.monster;
                if (monster == null || monster.remainingCount <= 0) continue;

                float monsterWorldX = monster.transform.position.x;
                float monsterLocalX = (container != null)
                    ? container.transform.InverseTransformPoint(monster.transform.position).x
                    : monsterWorldX;

                for (int i = 0; i < activeBalls.Count; i++)
                {
                    var b = activeBalls[i];
                    if (b == null || b.state != BallState.InContainer || b.colorType != monster.colorType || b.rb == null || b.rb.isKinematic)
                        continue;

                    Vector3 bLocal = (container != null)
                        ? container.transform.InverseTransformPoint(b.transform.position)
                        : b.transform.position;

                    if (bLocal.y > maxBottomY) continue;
                    if (!IsBallInMonsterCollectionZone(monster, b)) continue;
                    if (!ArePositionsInSameBottomCompartment(container, monsterLocalX, bLocal.x, floorLocalY + ballDiam * 0.5f))
                        continue;

                    float dx = monsterWorldX - b.transform.position.x;
                    if (Mathf.Abs(dx) > 0.08f)
                    {
                        float pullAccel = Mathf.Clamp(dx * 3.2f, -2.2f, 2.2f);
                        b.rb.AddForce(new Vector3(pullAccel, 0f, 0f), ForceMode.Acceleration);
                    }
                }
            }
        }

        private bool ArePositionsInSameBottomCompartment(
            ShapedGlassContainer container,
            float localXA,
            float localXB,
            float checkLocalY)
        {
            if (container == null || container.dividers == null || container.dividers.Count == 0) return true;

            for (int d = 0; d < container.dividers.Count; d++)
            {
                var div = container.dividers[d];
                if (div != null && div.enabled && div.BlocksHorizontalBetween(localXA, localXB, checkLocalY))
                {
                    return false;
                }
            }
            return true;
        }

        private Vector3 GetExitWorldPosForBall(ShapedGlassContainer container, BallPhysicsController ball)
        {
            if (container != null)
            {
                return container.GetBottomExitWorldPosition();
            }

            return new Vector3(0f, -1.20f, -0.16f);
        }

        private void StartBallAttraction(BallPhysicsController ball, MonsterCharacter monster, ActiveMonsterStream stream)
        {
            stream.inTransitBalls.Add(ball);

            if (!_attractingBalls.Contains(ball))
            {
                _attractingBalls.Add(ball);
            }

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            Vector3 exitPos = GetExitWorldPosForBall(container, ball);

            ball.BeginAttraction(monster, exitPos);

            if (BallContainerManager.Instance != null && BallContainerManager.Instance.bottomSpoutStopper != null)
            {
                Physics.IgnoreCollision(ball.sphereCollider, BallContainerManager.Instance.bottomSpoutStopper, true);
            }
        }

        private void UpdateAttractionPhysics()
        {
            if (_attractingBalls.Count == 0) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            for (int i = _attractingBalls.Count - 1; i >= 0; i--)
            {
                var ball = _attractingBalls[i];
                if (ball == null || ball.state != BallState.AttractingToExit || !ball.gameObject.activeInHierarchy)
                {
                    _attractingBalls.RemoveAt(i);
                    continue;
                }

                // If the target block finished or became inactive while a ball was inside the container, re-route or return
                MonsterCharacter targetMonster = ball.TargetMonster;
                if (targetMonster == null || !targetMonster.IsActivelyCollecting)
                {
                    targetMonster = FindMonsterForBall(ball);
                    if (targetMonster == null)
                    {
                        ball.CancelAttractionAndReturnToContainer();
                        _attractingBalls.RemoveAt(i);
                        continue;
                    }
                }

                Vector3 exitPos = GetExitWorldPosForBall(container, ball);

                bool isFever = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive;
                float pullF = isFever ? (attractionPullForce * 1.65f) : attractionPullForce;
                float maxSpd = isFever ? (maxBallSpeed * 1.55f) : maxBallSpeed;

                ball.ApplyAttractionToExit(exitPos, pullF, maxSpd);

                Vector3 ballPos = ball.transform.position;
                bool reachedVertical = ballPos.y <= exitPos.y + exitTriggerYOffset;
                bool reachedCollarX = Mathf.Abs(ballPos.x - exitPos.x) <= exitCollarHorizontalTolerance;
                bool reachedExit = reachedVertical && reachedCollarX;
                bool timedOut = ball.AttractionTime > (isFever ? 1.00f : 1.65f);

                if (reachedExit || timedOut)
                {
                    _attractingBalls.RemoveAt(i);

                    if (targetMonster != null)
                    {
                        ball.LaunchToMonsterMouth(ballPos, targetMonster, (b) =>
                        {
                            OnBallReachedMonster(b, targetMonster);
                        });
                    }
                    else
                    {
                        ball.CancelAttractionAndReturnToContainer();
                    }
                }
            }
        }

        private MonsterCharacter FindMonsterForBall(BallPhysicsController ball)
        {
            if (ball == null) return null;
            foreach (var kvp in _activeStreams)
            {
                var m = kvp.Value.monster;
                if (m != null && m.IsActivelyCollecting &&
                    m.colorType == ball.colorType &&
                    IsBallInMonsterCollectionZone(m, ball))
                {
                    return m;
                }
            }
            return null;
        }

        private void OnBallReachedMonster(BallPhysicsController ball, MonsterCharacter monster)
        {
            _totalCollectedThisLevel++;
            bool isFinalInBox = (monster != null && monster.remainingCount <= 0);
            if (PuzzleSoundManager.Instance != null)
            {
                PuzzleSoundManager.Instance.PlayBallTransfer(isFinalInBox);
            }
            OnBallCollected?.Invoke(ball, monster);
            CheckLevelCompletion();
        }

        public void CheckLevelCompletion()
        {
            bool hasPendingTubeBalls = (BallFeedTubeController.Instance != null && BallFeedTubeController.Instance.HasPendingBalls)
                || TrackCrowdQueue.PendingCount > 0;
            if (!hasPendingTubeBalls && BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls.Count == 0)
            {
                OnAllMatchingBallsCollected?.Invoke();
            }

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.CheckLevelCompletionState();
            }
        }

        public bool HasAnyBallInTransitOrAttracting()
        {
            if (_attractingBalls.Count > 0) return true;
            // Pist kuyruğu hâlâ çıkışa doğru yürüyorsa öndeki top henüz ağza ulaşmamış olabilir
            if (TrackCrowdQueue.IsMoving) return true;
            foreach (var kvp in _activeStreams)
            {
                if (kvp.Value != null && kvp.Value.inTransitBalls != null && kvp.Value.inTransitBalls.Count > 0)
                    return true;
            }
            if (BallObjectPool.Instance != null)
            {
                var active = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < active.Count; i++)
                {
                    var b = active[i];
                    if (b != null && b.gameObject.activeInHierarchy && b.state != BallState.InContainer && b.state != BallState.InFeedTube && b.state != BallState.Pooled)
                        return true;
                    // Pistte sürekli dönen bant topları "hamle sürüyor" sayılmaz (yoksa kilitlenme hiç algılanmaz)
                    if (b != null && b.gameObject.activeInHierarchy && b.rb != null && !b.rb.isKinematic && !b.trackQueueDriven && b.rb.linearVelocity.sqrMagnitude > 0.15f)
                        return true;
                }
            }
            return false;
        }

        public bool HasAnyCollectableMoveForSlottedMonsters()
        {
            if (FindNextCollectableBall(out _) != null) return true;

            // Bantlı pist: toplar dönmeye devam eder; eşleşen top şu an çıkışın önünde olmasa da tur atıp gelecektir.
            // Bu yüzden slottaki (ya da toplamakta olan) bloğun rengi bantta/kuyrukta varsa hamle var sayılır.
            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (container != null && container.IsTrackShape && TrackCrowdQueue.HasLoop(container))
            {
                if (HasBeltBallForWaitingMonster()) return true;
                // Gizli kuyrukta top var ve bantta yer varsa yeni toplar girmeye devam eder; henüz kilitlenme yok.
                // Bant doluysa ve slotlardaki renklerin hiçbiri bantta değilse boşluk açılamaz: gerçek kilitlenme.
                // (Giriş yolundaki ya da henüz gelmemiş toplar da bu durumda sırayla banda girer.)
                return (TrackCrowdQueue.PendingCount > 0 || TrackCrowdQueue.RoadQueueCount > 0)
                    && TrackCrowdQueue.HasFreeBeltSlot(container);
            }
            return false;
        }

        private bool HasBeltBallForWaitingMonster()
        {
            var gm = PuzzleGlassGameManager.Instance;
            if (gm == null || gm.activeMonsters == null || BallObjectPool.Instance == null) return false;
            var balls = BallObjectPool.Instance.ActiveBalls;

            for (int i = 0; i < gm.activeMonsters.Count; i++)
            {
                var m = gm.activeMonsters[i];
                if (m == null || m.IsCompleted || m.remainingCount <= 0) continue;
                if (m.assignedSlot == null && !m.IsActivelyCollecting) continue;

                int inTransit = 0;
                if (!string.IsNullOrEmpty(m.monsterId) && _activeStreams.TryGetValue(m.monsterId, out var stream))
                    inTransit = stream.inTransitBalls.Count;
                if (m.remainingCount - inTransit <= 0) continue;

                for (int b = 0; b < balls.Count; b++)
                {
                    var ball = balls[b];
                    if (ball == null || ball.state != BallState.InContainer || !ball.gameObject.activeInHierarchy) continue;
                    if (ball.colorType != m.colorType) continue;
                    // Sadece banttaki toplar; giriş yolundakiler bant doluyken giremez (ayrıca kontrol edilir)
                    if (ball.trackLoopSlot >= 0) return true;
                }
            }
            return false;
        }

        public void ClearAll()
        {
            TrackCrowdQueue.ClearPending();
            _attractingBalls.Clear();
            _activeStreams.Clear();
            _squashingBalls.Clear();
            _bottomCandidatesCache.Clear();
            _ballFieldDirty = true;
            _lastBallFieldRebuildTime = -10f;
            _totalCollectedThisLevel = 0;
            _lastGlobalCollectionTime = -10f;
            _levelStartTime = Time.time;
        }
    }
}
