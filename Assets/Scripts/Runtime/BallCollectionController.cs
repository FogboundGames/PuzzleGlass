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

        [Header("Pool Drain Funnel")]
        [Tooltip("Düz tabanlı havuzlarda alt sıradaki topları merkezdeki çıkış deliğine doğru çeker.")]
        public bool enablePoolDrainFunnel = true;

        [Tooltip("Alt sıra kabul yüksekliği (top çapı cinsinden).")]
        public float poolDrainRowToleranceInDiameters = 1.25f;

        [Tooltip("Deliğe doğru çekim ivmesi.")]
        public float poolDrainAccel = 1.35f;

        [Tooltip("Huninin etki yarıçapı (top çapı cinsinden); dışarıdaki toplar çekilmez.")]
        public float poolDrainRadiusInDiameters = 5.0f;

        [Tooltip("Deliğin hemen yanındaki ölü bölge (top çapı cinsinden); topların delik üzerinde titremesini engeller.")]
        public float poolDrainDeadZoneInDiameters = 0.35f;

        [Tooltip("Slot sisteminde kare/dikdörtgen kutu camlar: yalnızca alt çıkış kapağının (yakanın) üstündeki toplar toplanabilir. " +
                 "Kapalıysa alt sıradaki eşleşen her top toplanır. Açıkken huni tüm tabanı kapağa doğru çeker.")]
        public bool poolHatchOnly = true;

        [Tooltip("Kutu cam: kapaktan çekilen topa değen aynı renk toplar da art arda çekilir (renk öbeği zincirleme akar).")]
        public bool poolChainPull = true;

        [Tooltip("Zincir noktasının geçerli kaldığı süre (sn): bu sürede komşu aynı renk top çekilmezse zincir kopar.")]
        public float poolChainWindow = 0.45f;

        [Tooltip("Kutu cam (kapak kuralı): huninin tabandaki topları kapağa itme ivmesinin çarpanı.")]
        public float poolHatchDrainAccelMultiplier = 3.2f;

        [Tooltip("Kutu cam (kapak kuralı): ardışık top çekme aralığı (sn).")]
        [Range(0.02f, 0.20f)]
        public float poolHatchCollectionInterval = 0.045f;

        [Header("State (Read Only)")]
        [SerializeField] private int _totalCollectedThisLevel = 0;
        private float _lastGlobalCollectionTime = -10f;
        private float _lastDrainTargetTime = -10f;
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
        private readonly List<BallPhysicsController> _glowTransitionBalls = new List<BallPhysicsController>(64);
        private readonly HashSet<BallPhysicsController> _collectableGlowSet = new HashSet<BallPhysicsController>(64);
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
        private float _nextGlowRefreshTime = -10f;
        private float _lastGlowRefreshTime = -10f;
        /// <summary>Alan "kirli" olsa bile parlama hesabı en fazla bu aralıkla yenilenir. Pistte toplar sürekli hareket
        /// ettiği için kirli bayrağı her fizik adımında kalkıyordu ve hesap her karede tüm toplar için yapılıyordu.
        /// Parlama zaten yumuşak geçişle değiştiğinden 50 ms gecikme görünmez.</summary>
        private const float GlowDirtyMinInterval = 0.05f;

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
            float dt = Time.deltaTime;
            StepSquashingBalls(dt);
            UpdateCollectableBallGlow(dt);

            if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.AreGlassBallsPausedForTutorial)
            {
                return;
            }

            // If SandSimulation is actively running with sand grains (and no physical balls), yield to sand engine
            if (SandSimulationEngine.Instance != null && SandSimulationEngine.Instance.enabled && SandSimulationEngine.Instance.sandAmount > 0)
            {
                if (BallObjectPool.Instance == null || BallObjectPool.Instance.ActiveBalls.Count == 0)
                    return;
            }

            AutoActivateMatchingTopEdgeBlocks();
            PollSlottedMonsters();
            UpdateSupplyBoxFeeds();
            if (Debug.isDebugBuild) LogPoolStallOnce();
        }

        // ---------- Teşhis: kutu camda toplama durursa (yalnızca Development Build) ----------
        // Oyuncu: "kutu levelde toplar tıkanıyor, aşağı inmiyor". Editörde tekrarlanamadı; cihazda tıkanma anının
        // durumunu logcat'e ("POOLSTALL") bir kez yazar. Toplama yeniden başlayınca tekrar kurulur.
        private bool _stallLogged;

        private void LogPoolStallOnce()
        {
            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (!UsesPoolHatch(container) || _activeStreams.Count == 0 || _attractingBalls.Count > 0)
            {
                if (_attractingBalls.Count > 0) _stallLogged = false;
                return;
            }
            float now = Time.time;
            if (now - _lastGlobalCollectionTime < 3f) { _stallLogged = false; return; }
            if (_stallLogged) return;
            _stallLogged = true;

            var sb = new System.Text.StringBuilder(512);
            sb.Append("POOLSTALL t=").Append(now.ToString("F1")).Append(" perSlotChannels=").Append(container.HasPerSlotExitChannels);
            foreach (var kvp in _activeStreams)
            {
                var m = kvp.Value.monster;
                if (m == null) continue;
                sb.Append("\n  monster ").Append(m.colorType).Append(" rem=").Append(m.remainingCount)
                  .Append(" active=").Append(m.IsActivelyCollecting).Append(" slot=").Append(m.assignedSlot != null)
                  .Append(" chan=").Append(container.GetChannelIndexForMonster(m)).Append(" inTransit=").Append(kvp.Value.inTransitBalls.Count);
            }
            var cands = CollectBottomCandidates();
            var candSet = new HashSet<BallPhysicsController>(cands);
            var pool = BallObjectPool.Instance;
            int inGlass = 0, atHatch = 0;
            if (pool != null)
            {
                foreach (var b in pool.ActiveBalls)
                {
                    if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                    inGlass++;
                    if (!IsAtPoolHatch(container, b)) continue;
                    atHatch++;
                    if (atHatch > 12) continue;
                    Vector3 lp = container.transform.InverseTransformPoint(b.transform.position);
                    sb.Append("\n  hatchBall ").Append(b.colorType).Append(" local=(").Append(lp.x.ToString("F2")).Append(',').Append(lp.y.ToString("F2"))
                      .Append(") bottomCand=").Append(candSet.Contains(b)).Append(" kin=").Append(b.rb != null && b.rb.isKinematic)
                      .Append(" sleeping=").Append(b.rb != null && b.rb.IsSleeping());
                }
            }
            sb.Append("\n  inGlass=").Append(inGlass).Append(" atHatch=").Append(atHatch).Append(" bottomCands=").Append(cands.Count)
              .Append(" floorY=").Append(container.GetChamberFloorY().ToString("F2"));
            Debug.Log(sb.ToString());
        }

        private readonly Dictionary<string, float> _supplyNextLaunchTime = new Dictionary<string, float>();

        private void UpdateSupplyBoxFeeds()
        {
            var gm = PuzzleGlassGameManager.Instance;
            if (gm == null || gm.activeMonsters == null || gm.isLevelCompleted || gm.isLevelFailed) return;
            if (BallObjectPool.Instance == null) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();
            if (container == null) return;

            bool isFastForward = gm.IsEndgameFastForward;
            bool isFever = gm.IsFeverActive;
            float interval = (isFastForward || isFever) ? 0.035f : 0.068f;
            float ballRadius = (container.particleSize > 0.05f) ? container.particleSize * 0.5f : 0.057f;
            float now = Time.time;

            for (int i = 0; i < gm.activeMonsters.Count; i++)
            {
                var m = gm.activeMonsters[i];
                if (m == null || !m.IsSupplyBoxActive) continue;

                string key = m.monsterId;
                if (string.IsNullOrEmpty(key)) continue;

                if (_supplyNextLaunchTime.TryGetValue(key, out float nextTime) && now < nextTime) continue;

                PuzzleColor launchColor = m.filledBallColor;
                Vector3 startPos = m.PopOneFilledSupplyBallVisual(out Vector3 startScale);
                Vector3 targetWorld = ComputeSupplyLandingWorldPos(container, ballRadius, out Vector3 mouthWorld, out bool isTrack, out Vector2 flowDir);
                Vector3[] reverseChanPts = null;
                if (container.HasPerSlotExitChannels)
                {
                    int chIdx = container.GetChannelIndexForMonster(m);
                    if (chIdx >= 0)
                    {
                        mouthWorld = container.GetChannelMouthWorldPosition(chIdx);
                        mouthWorld.z = -0.10f;
                        reverseChanPts = container.GetChannelWorldPathPoints(chIdx, reverse: true, steps: 7);
                    }
                }

                var ball = BallObjectPool.Instance.SpawnBall(launchColor, startPos, ballRadius);
                if (ball != null)
                {
                    ball.LaunchIntoContainer(startPos, startScale, container, mouthWorld, targetWorld, isTrack, flowDir, reverseChanPts);
                    PuzzleSoundManager.Instance?.PlayBallTap(0.55f);
                }

                _supplyNextLaunchTime[key] = now + interval;
            }
        }

        private static Vector3 ComputeSupplyLandingWorldPos(
            ShapedGlassContainer container,
            float ballRadius,
            out Vector3 mouthWorldPos,
            out bool isTrack,
            out Vector2 trackFlowDir)
        {
            isTrack = container != null && container.IsTrackShape;
            trackFlowDir = Vector2.right;

            if (container == null)
            {
                mouthWorldPos = new Vector3(0f, 1.35f, -0.10f);
                return new Vector3(0f, 1.5f, -0.10f);
            }

            mouthWorldPos = container.GetBottomExitWorldPosition();
            mouthWorldPos.z = -0.10f;

            if (isTrack)
            {
                if (container.compiledTrack == null) container.RebuildTrackCompiledData();
                if (container.compiledTrack != null)
                {
                    Vector2 exitPt = container.compiledTrack.exitPoint;
                    container.compiledTrack.SampleField(exitPt, out _, out trackFlowDir, out _, out _);
                    Vector2 perp = new Vector2(-trackFlowDir.y, trackFlowDir.x);
                    Vector2 jitter = trackFlowDir * UnityEngine.Random.Range(0.02f, ballRadius * 1.15f)
                        + perp * UnityEngine.Random.Range(-ballRadius * 0.25f, ballRadius * 0.25f);
                    Vector2 localPt = exitPt + jitter;
                    // Katmanları koru: dolu kutunun döktüğü top en dış şeride iner (hemen alınabilir); aksi halde dolu dış
                    // şeridin altına sıkışıp kapalı kalıyordu. Oradaki top bir sıra içe itilir.
                    if (TrackCrowdQueue.KeepLayersActive)
                    {
                        Vector2 outward = TrackCrowdQueue.RingOutwardAt(exitPt);
                        float edge = Mathf.Max(0f, container.compiledTrack.innerHalfWidth - ballRadius * 1.15f);
                        localPt = exitPt + outward * edge + trackFlowDir * UnityEngine.Random.Range(-ballRadius, ballRadius);
                    }
                    return container.transform.TransformPoint(new Vector3(localPt.x, localPt.y, -0.10f));
                }
                return mouthWorldPos + new Vector3(0f, ballRadius * 1.8f, 0f);
            }

            Vector3 mouthLocal = container.transform.InverseTransformPoint(mouthWorldPos);
            float floorY = container.GetChamberFloorY();
            float targetY = floorY + ballRadius * 1.55f;
            container.GetSafeCubeBoundsAtY(targetY, out float safeL, out float safeR);
            float pad = ballRadius * 1.25f;
            float minX = safeL + pad;
            float maxX = safeR - pad;
            float targetX = Mathf.Clamp(
                mouthLocal.x + UnityEngine.Random.Range(-ballRadius * 0.45f, ballRadius * 0.45f),
                minX,
                Mathf.Max(minX, maxX));

            if (container.dividers != null)
            {
                for (int d = 0; d < container.dividers.Count; d++)
                {
                    var div = container.dividers[d];
                    if (div != null && div.enabled && !div.runtimeIsOpened && div.OverlapsCircle(new Vector2(targetX, targetY), ballRadius, 0.01f))
                    {
                        targetX = (targetX >= div.center.x)
                            ? Mathf.Min(maxX, div.center.x + div.thickness * 0.5f + ballRadius * 1.4f)
                            : Mathf.Max(minX, div.center.x - div.thickness * 0.5f - ballRadius * 1.4f);
                    }
                }
            }

            return container.transform.TransformPoint(new Vector3(targetX, targetY, -0.10f));
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

        private void RegisterGlowTransitionBall(BallPhysicsController ball)
        {
            if (ball == null) return;
            if (!_glowTransitionBalls.Contains(ball))
            {
                _glowTransitionBalls.Add(ball);
            }
        }

        private void UpdateCollectableBallGlow(float dt)
        {
            if (BallObjectPool.Instance == null) return;
            var activeBalls = BallObjectPool.Instance.ActiveBalls;

            if ((_ballFieldDirty && Time.time >= _lastGlowRefreshTime + GlowDirtyMinInterval) || Time.time >= _nextGlowRefreshTime)
            {
                _lastGlowRefreshTime = Time.time;
                _nextGlowRefreshTime = Time.time + 0.08f;
                _collectableGlowSet.Clear();

                if (activeBalls != null && activeBalls.Count > 0 &&
                    !(BallFeedTubeController.Instance != null && BallFeedTubeController.Instance.IsPouringOrSettling))
                {
                    ShapedGlassContainer container = BallContainerManager.Instance != null
                        ? BallContainerManager.Instance.glassContainer
                        : null;
                    bool isBelt = container != null && container.IsTrackShape && TrackCrowdQueue.HasLoop(container);
                    bool usesHatch = UsesPoolHatch(container);

                    List<BallPhysicsController> candidates = CollectBottomCandidates();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var cand = candidates[i];
                        if (cand == null || cand.state != BallState.InContainer || !cand.gameObject.activeInHierarchy) continue;
                        if (isBelt && !TrackCrowdQueue.IsAtExit(cand)) continue;
                        if (usesHatch && !IsAtPoolHatch(container, cand) && !IsChainedToRecentPull(container, cand)) continue;
                        _collectableGlowSet.Add(cand);
                    }
                }

                if (activeBalls != null)
                {
                    for (int i = 0; i < activeBalls.Count; i++)
                    {
                        var b = activeBalls[i];
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        float target = (b.state == BallState.InContainer && _collectableGlowSet.Contains(b)) ? 1f : 0f;
                        if (b.SetReadyGlowTarget(target))
                        {
                            RegisterGlowTransitionBall(b);
                        }
                    }
                }
            }

            for (int i = _glowTransitionBalls.Count - 1; i >= 0; i--)
            {
                var ball = _glowTransitionBalls[i];
                if (ball == null || !ball.gameObject.activeInHierarchy || ball.state == BallState.Pooled)
                {
                    int last = _glowTransitionBalls.Count - 1;
                    if (i != last) _glowTransitionBalls[i] = _glowTransitionBalls[last];
                    _glowTransitionBalls.RemoveAt(last);
                    continue;
                }

                ball.StepReadyGlow(dt);
                if (!ball.IsReadyGlowTransitioning)
                {
                    int last = _glowTransitionBalls.Count - 1;
                    if (i != last) _glowTransitionBalls[i] = _glowTransitionBalls[last];
                    _glowTransitionBalls.RemoveAt(last);
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
            using (s_MStepBalls.Auto()) StepAllBallPhysics();
            if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.AreGlassBallsPausedForTutorial)
            {
                return;
            }
            using (s_MAttraction.Auto()) UpdateAttractionPhysics();
            using (s_MFunnel.Auto()) ApplyPoolDrainFunnel();
            using (s_MDrift.Auto()) ApplyFloorDriftTowardHungryMonsters();
        }

        // Profiler işaretleri (geliştirme build'lerinde ve editörde görünür; release build'de maliyetsiz)
        private static readonly Unity.Profiling.ProfilerMarker s_MStepBalls = new Unity.Profiling.ProfilerMarker("BallColl.StepAllBallPhysics");
        private static readonly Unity.Profiling.ProfilerMarker s_MAttraction = new Unity.Profiling.ProfilerMarker("BallColl.UpdateAttraction");
        private static readonly Unity.Profiling.ProfilerMarker s_MFunnel = new Unity.Profiling.ProfilerMarker("BallColl.PoolDrainFunnel");
        private static readonly Unity.Profiling.ProfilerMarker s_MDrift = new Unity.Profiling.ProfilerMarker("BallColl.FloorDrift");
        private static readonly Unity.Profiling.ProfilerMarker s_MTrack = new Unity.Profiling.ProfilerMarker("BallColl.TrackStep");
        private static readonly Unity.Profiling.ProfilerMarker s_MPerBall = new Unity.Profiling.ProfilerMarker("BallColl.PerBallStep");

        private void StepAllBallPhysics()
        {
            if (BallObjectPool.Instance == null) return;
            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return;

            float fixedDt = Time.fixedDeltaTime;
            bool anyAwakeMoving = false;

            // Pist camı: toplar Bus Traffic Fever tarzı sıralı kuyruk halinde çıkışa yürür (fizik yok)
            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            s_MTrack.Begin();
            if (container != null && container.IsTrackShape && TrackCrowdQueue.Step(container, activeBalls, fixedDt))
            {
                anyAwakeMoving = true;
            }
            s_MTrack.End();
            using var perBallScope = s_MPerBall.Auto();
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
            bool isFastForward = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsEndgameFastForward;
            bool isVipCollecting = false;
            foreach (var s in _activeStreams.Values)
            {
                if (s.monster != null && s.monster.assignedSlot != null && s.monster.assignedSlot.isVip && (s.monster.remainingCount - s.inTransitBalls.Count > 0))
                {
                    isVipCollecting = true;
                    break;
                }
            }

            float effectiveInterval = (isFever || isVipCollecting || isFastForward) ? (collectionInterval * 0.55f) : collectionInterval;
            int effectiveMaxInSpout = (isFever || isVipCollecting || isFastForward) ? (maxBallsInSpout + 2) : maxBallsInSpout;

            // Bant çıkışın önünden sürekli top geçirir: uyan topları kaçırmamak için daha sık ve çok top al
            var beltContainer = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (beltContainer != null && TrackCrowdQueue.HasLoop(beltContainer))
            {
                effectiveInterval = Mathf.Min(effectiveInterval, isFastForward ? 0.018f : 0.035f);
                effectiveMaxInSpout = Mathf.Max(effectiveMaxInSpout, isFastForward ? 20 : 14);
            }
            // Kutu cam (kapak kuralı): kapaktaki ve zincirdeki toplar beklemeden art arda çekilsin
            if (UsesPoolHatch(beltContainer))
            {
                effectiveInterval = Mathf.Min(effectiveInterval, isFastForward ? 0.025f : poolHatchCollectionInterval);
                effectiveMaxInSpout = Mathf.Max(effectiveMaxInSpout, isFastForward ? 18 : 12);
            }

            float now = Time.time;
            if (now - _lastGlobalCollectionTime < effectiveInterval) return;
            if (_attractingBalls.Count >= effectiveMaxInSpout) return;

            // Find a physically accessible bottom-floor ball matching an active box's aligned columns
            BallPhysicsController bestBall = FindNextCollectableBall(out ActiveMonsterStream targetStream);
            if (bestBall != null && targetStream != null)
            {
                _lastDrainTargetTime = now;
                RecordChainAnchor(beltContainer, bestBall);
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

        /// <summary>Kapak kuralı geçerli mi: slot sistemi + düz tabanlı kutu cam.</summary>
        private bool UsesPoolHatch(ShapedGlassContainer container) =>
            poolHatchOnly && container != null && container.IsFlatBottomBox && SlotSystem.IsActive;

        /// <summary>Top, alt çıkış kapağının (yakanın) üstünde mi? (yaka genişliği + yarım top payı)</summary>
        private static bool IsAtPoolHatch(ShapedGlassContainer container, BallPhysicsController ball)
        {
            Vector3 ballLocal = container.transform.InverseTransformPoint(ball.transform.position);
            Vector3 exitLocal = container.transform.InverseTransformPoint(container.GetBottomExitWorldPosition());
            float ballDiam = container.particleSize > 0.08f ? container.particleSize : 0.114f;
            float halfW = container.GetBottomExitCollarWidthLocal() * 0.5f + ballDiam * 0.35f;
            return Mathf.Abs(ballLocal.x - exitLocal.x) <= Mathf.Max(ballDiam * 0.75f, halfW);
        }

        // ---------- Kutu cam: aynı renk zincirleme çekme ----------
        // Kapaktan çekilen her topun konumu kısa süre "zincir noktası" olur; ona değen aynı renk top kapağın dışında da
        // olsa çekilebilir. Böylece eşleşen renk öbeği tek tek beklemeden art arda akar.
        private struct ChainAnchor { public Vector2 local; public PuzzleColor color; public float time; }
        private readonly List<ChainAnchor> _chainAnchors = new List<ChainAnchor>();

        private void RecordChainAnchor(ShapedGlassContainer container, BallPhysicsController ball)
        {
            if (!poolChainPull || !UsesPoolHatch(container) || ball == null) return;
            Vector3 lp = container.transform.InverseTransformPoint(ball.transform.position);
            float now = Time.time;
            _chainAnchors.RemoveAll(a => now - a.time > poolChainWindow);
            _chainAnchors.Add(new ChainAnchor { local = new Vector2(lp.x, lp.y), color = ball.colorType, time = now });
        }

        private bool IsChainedToRecentPull(ShapedGlassContainer container, BallPhysicsController ball)
        {
            if (!poolChainPull || _chainAnchors.Count == 0) return false;
            float now = Time.time;
            float ballDiam = container.particleSize > 0.08f ? container.particleSize : 0.114f;
            float reachSq = (ballDiam * 1.3f) * (ballDiam * 1.3f);
            Vector3 lp = container.transform.InverseTransformPoint(ball.transform.position);
            Vector2 p = new Vector2(lp.x, lp.y);
            for (int i = 0; i < _chainAnchors.Count; i++)
            {
                var a = _chainAnchors[i];
                if (a.color != ball.colorType || now - a.time > poolChainWindow) continue;
                if ((a.local - p).sqrMagnitude <= reachSq) return true;
            }
            return false;
        }

        private bool IsBallInMonsterCollectionZone(MonsterCharacter monster, BallPhysicsController ball)
        {
            if (monster == null || ball == null) return false;
            ShapedGlassContainer zoneContainer = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;
            // Kutu camda (slot sistemi) yalnızca kapağın üstündeki top ya da az önce çekilen aynı renk topa değen top çıkabilir
            if (UsesPoolHatch(zoneContainer) && !IsAtPoolHatch(zoneContainer, ball) && !IsChainedToRecentPull(zoneContainer, ball)) return false;
            if (zoneContainer != null && zoneContainer.HasPerSlotExitChannels)
            {
                return monster.assignedSlot != null && zoneContainer.GetChannelIndexForMonster(monster) >= 0;
            }
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

            // Düz tabanlı havuzlarda tek merkez deliği vardır: top hangi sütunda olursa olsun
            // aynı delikten çıkar. Bölme varsa yalnızca deliğe giden yol kapalıysa engelle.
            if (container != null && container.IsFlatBottomBox)
            {
                Vector3 ballLocal = container.transform.InverseTransformPoint(ball.transform.position);
                Vector3 exitLocal = container.transform.InverseTransformPoint(container.GetBottomExitWorldPosition());
                return ArePositionsInSameBottomCompartment(container, ballLocal.x, exitLocal.x, ballLocal.y);
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
            bool isFlatBottomPool = trackContainer != null && trackContainer.IsFlatBottomBox;
            Vector3 drainExitWorld = isFlatBottomPool ? trackContainer.GetBottomExitWorldPosition() : Vector3.zero;

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
                bool hasChannels = trackContainer != null && trackContainer.HasPerSlotExitChannels;
                int monsterChanIdx = hasChannels ? trackContainer.GetChannelIndexForMonster(monster) : -1;
                if (hasChannels && monsterChanIdx < 0) continue;
                Vector3 chanEntrance = hasChannels ? trackContainer.GetChannelManifoldEntranceWorldPosition(monsterChanIdx) : Vector3.zero;

                for (int i = 0; i < bottomCandidates.Count; i++)
                {
                    var cand = bottomCandidates[i];
                    if (cand.colorType != monster.colorType) continue;
                    // Bant: sadece o an çıkışın önünden geçen top ayrılabilir; diğerleri tura devam eder
                    if (isBelt && !TrackCrowdQueue.IsAtExit(cand)) continue;
                    if (!IsBallInMonsterCollectionZone(monster, cand)) continue;

                    // Alttan üste ve kendi kanal ağzına en yakın top önceliklidir
                    float score;
                    if (isTrack)
                    {
                        Vector3 lp = trackContainer.transform.InverseTransformPoint(cand.transform.position);
                        trackContainer.SampleTrackField(lp, out _, out _, out float exDist, out _);
                        score = hasChannels
                            ? (cand.transform.position.y * 3.6f + exDist * 0.55f + Vector2.Distance(cand.transform.position, chanEntrance) * 0.45f)
                            : (cand.transform.position.y * 3.0f + exDist);
                    }
                    else if (isFlatBottomPool)
                    {
                        score = hasChannels
                            ? (cand.transform.position.y * 3.6f + Mathf.Abs(cand.transform.position.x - chanEntrance.x) * 0.65f)
                            : Mathf.Abs(cand.transform.position.x - drainExitWorld.x);
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
        /// Düz tabanlı havuzlarda alt sıradaki topları merkezdeki çıkış deliğine doğru iter.
        /// Böylece toplar yan sütunlardan merkeze sürüklenmek yerine delik çevresinde doğal bir huni oluşturur.
        /// </summary>
        private void ApplyPoolDrainFunnel()
        {
            if (!enablePoolDrainFunnel) return;
            if (BallObjectPool.Instance == null) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;
            if (container == null || !container.IsFlatBottomBox) return;
            // Kapak kuralında huni, slotta top bekleyen blok varken hep çalışır ve tüm tabanı kapsar:
            // kapaktan uzaktaki toplar da zamanla kapağa akar (köşede top kalmaz)
            bool hatch = UsesPoolHatch(container);
            if (hatch ? _activeStreams.Count == 0 : (_attractingBalls.Count == 0 && Time.time - _lastDrainTargetTime > 0.40f)) return;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return;

            Vector3 exitWorld = container.GetBottomExitWorldPosition();
            Vector3 exitLocal = container.transform.InverseTransformPoint(exitWorld);
            float floorLocalY = container.GetChamberFloorY();
            float ballDiamLocal = container.particleSize > 0.08f ? container.particleSize : 0.24f;
            float ballDiamWorld = ballDiamLocal * Mathf.Abs(container.transform.lossyScale.x);
            if (ballDiamWorld <= 0.001f) ballDiamWorld = ballDiamLocal;

            // Kapak kuralında alttaki ~3 sıra çekilir: köşe çıkıntılarına takılan tek toplar da kapağa kayar
            float rowTol = hatch ? Mathf.Max(3.2f, poolDrainRowToleranceInDiameters) : Mathf.Max(1.0f, poolDrainRowToleranceInDiameters);
            float maxBottomLocalY = floorLocalY + ballDiamLocal * rowTol;
            float deadZone = ballDiamWorld * Mathf.Max(0f, poolDrainDeadZoneInDiameters);
            float funnelRadius = hatch ? float.MaxValue : ballDiamWorld * Mathf.Max(1.0f, poolDrainRadiusInDiameters);

            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                if (b.rb == null || b.rb.isKinematic) continue;

                Vector3 bLocal = container.transform.InverseTransformPoint(b.transform.position);
                if (bLocal.y > maxBottomLocalY) continue;
                if (!ArePositionsInSameBottomCompartment(container, bLocal.x, exitLocal.x, bLocal.y)) continue;

                float worldDx = exitWorld.x - b.transform.position.x;
                float absDx = Mathf.Abs(worldDx);
                if (absDx <= deadZone || absDx > funnelRadius) continue;

                float dir = Mathf.Sign(worldDx);
                float accel = hatch ? poolDrainAccel * Mathf.Max(1f, poolHatchDrainAccelMultiplier) : poolDrainAccel;
                b.rb.AddForce(new Vector3(dir * accel, 0f, 0f), ForceMode.Acceleration);
                b.rb.WakeUp();
            }
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

            // Kare havuzda merkez deliğe çeken huni varken canavara doğru yatay sürükleme yapma.
            if (enablePoolDrainFunnel && container != null && container.IsFlatBottomBox) return;

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

        private Vector3 GetExitWorldPosForBall(ShapedGlassContainer container, BallPhysicsController ball, MonsterCharacter monster = null)
        {
            if (container != null)
            {
                if (container.HasPerSlotExitChannels)
                {
                    MonsterCharacter target = monster ?? ball?.TargetMonster;
                    int chanIdx = container.GetChannelIndexForMonster(target);
                    if (chanIdx >= 0)
                    {
                        return container.GetChannelManifoldEntranceWorldPosition(chanIdx);
                    }
                }
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

            Vector3 exitPos = GetExitWorldPosForBall(container, ball, monster);

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
                    ball.SetAttractionTarget(targetMonster);
                }

                Vector3 exitPos = GetExitWorldPosForBall(container, ball, targetMonster);

                bool isFever = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive;
                bool isFastForward = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsEndgameFastForward;
                bool fastPull = isFever || isFastForward;
                float pullF = fastPull ? (attractionPullForce * 1.65f) : attractionPullForce;
                float maxSpd = fastPull ? (maxBallSpeed * 1.55f) : maxBallSpeed;

                ball.ApplyAttractionToExit(exitPos, pullF, maxSpd);

                Vector3 ballPos = ball.transform.position;
                bool reachedVertical = ballPos.y <= exitPos.y + exitTriggerYOffset;
                bool reachedCollarX = Mathf.Abs(ballPos.x - exitPos.x) <= exitCollarHorizontalTolerance;
                bool reachedExit = reachedVertical && reachedCollarX;
                bool timedOut = ball.AttractionTime > (fastPull ? 1.00f : 1.65f);

                if (reachedExit || timedOut)
                {
                    _attractingBalls.RemoveAt(i);

                    if (targetMonster != null)
                    {
                        bool hasChannels = container != null && container.HasPerSlotExitChannels;
                        int chanIdx = hasChannels
                            ? container.GetChannelIndexForMonster(targetMonster)
                            : -1;
                        if (chanIdx >= 0)
                        {
                            ball.LaunchThroughChannelToMonsterMouth(container, chanIdx, targetMonster, (b) =>
                            {
                                OnBallReachedMonster(b, targetMonster);
                            });
                        }
                        else if (!hasChannels)
                        {
                            ball.LaunchToMonsterMouth(ballPos, targetMonster, (b) =>
                            {
                                OnBallReachedMonster(b, targetMonster);
                            });
                        }
                        else
                        {
                            // Kanal yoksa top kanal dışından havadan uçmamalı; kabın içine geri bırak.
                            if (_activeStreams.TryGetValue(targetMonster.monsterId, out var stream))
                                stream.inTransitBalls.Remove(ball);
                            ball.state = BallState.InContainer;
                            if (ball.sphereCollider != null) ball.sphereCollider.isTrigger = false;
                            if (ball.rb != null)
                            {
                                Vector3 resetPos = ball.transform.position;
                                resetPos.z = -0.10f;
                                ball.transform.position = resetPos;
                                ball.rb.linearVelocity = Vector3.zero;
                            }
                            MarkBallFieldDirty();
                        }
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
            var gm = PuzzleGlassGameManager.Instance;
            if (gm != null && gm.activeMonsters != null)
            {
                for (int i = 0; i < gm.activeMonsters.Count; i++)
                {
                    var m = gm.activeMonsters[i];
                    if (m != null && m.IsSupplyBoxActive) return true;
                }
            }

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
                    // Katman kilidi: kilitli katmandaki top bu kutuya gelemez; sadece açık katman sayılır
                    if (!TrackCrowdQueue.IsLayerOpen(ball)) continue;
                    // Katmanları koru: dış tarafı başka topla kapalı top dönse de çıkamaz; toplanabilir sayılmaz
                    if (TrackCrowdQueue.IsCoveredOnRing(ball)) continue;
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
            _supplyNextLaunchTime.Clear();
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
