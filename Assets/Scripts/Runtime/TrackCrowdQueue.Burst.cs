using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;

namespace PuzzleGlass
{
    /// <summary>
    /// Pist topları için PhysX'siz 2D simülasyon (Burst + Jobs).
    ///
    /// Neden: telefonda (Galaxy M34) ~360 dinamik Rigidbody'li pistte tek PhysX adımı ~9,6 ms sürüyordu ve telefon
    /// ısınınca oyun ~27 FPS'e düşüyordu. Toplar zaten düzlemde (Z kilitli) aktığı için 3B fizik motoruna gerek yok.
    ///
    /// Nasıl:
    ///  - Toplar pistteyken kinematik olur ve collider'ları kapanır (PhysX onları hiç görmez).
    ///  - Her FixedUpdate'te konum/hız Burst işlerinde, çekirdeklere dağıtılarak çözülür:
    ///      sınıflandırma (halka/yol, yol teğeti, katman konumu) → ızgara → kuvvetler (Rigidbody modundaki sürüş,
    ///      yerçekimi, kohezyon, hız hizalama, kenar ve katman bandı aynen) → konum tabanlı temas çözümü (Jacobi)
    ///      + cam duvarları (mesafe alanı) + giriş yolu kapısı + katman duvarı → hız ve yuvarlanma.
    ///  - Görüntü LateUpdate'te iki fizik adımı arasında yumuşatılarak (interpolation) transform'lara yazılır.
    ///  - Çarpışma ezilmesi/tık sesi simülasyonun ölçtüğü yaklaşma hızından tetiklenir.
    /// Oyun kuralları (toplama, kapasite, gizli kuyruk, katman kilidi) Rigidbody/granül modla aynı alanları kullanır.
    /// </summary>
    public static partial class TrackCrowdQueue
    {
        /// <summary>true: pist topları Burst simülasyonuyla hareket eder (önerilen). PerfProbe "burst=0" ile Rigidbody moduna döner.</summary>
        public static bool UseBurstSim = true;

        /// <summary>Temas çözümü yineleme sayısı (fazlası daha sert kalabalık, daha pahalı).</summary>
        public static int BurstSolverIterations = 4;
        /// <summary>Temas düzeltmesinin her yinelemede uygulanan oranı (Jacobi gevşetme).</summary>
        public static float BurstContactRelax = 0.8f;
        /// <summary>Temas mesafesi (top çapı oranı).</summary>
        public static float BurstContactRatio = 0.985f;
        /// <summary>Doğrusal sönüm (1/sn) — Rigidbody modundaki linearDamping ile aynı.</summary>
        public static float BurstLinearDamping = 0.78f;
        /// <summary>Üst üste binmeyi çözerken doğan en büyük hız (dünya birimi/sn) — Rigidbody'deki maxDepenetrationVelocity.</summary>
        public static float BurstMaxDepenetration = 0.90f;
        /// <summary>Ezilme/ses için çarpma hızının ölçeği. Burst kalabalığı PhysX'ten sakin aktığı için (titreşimsiz)
        /// aynı eşiklerle daha az ezilme olur; 2.0'da Level 8'de eş zamanlı ezilen top sayısı PhysX moduna yakın
        /// (~100'e karşı ~120). Daha sakin his için düşür.</summary>
        public static float BurstImpactScale = 2.0f;

        /// <summary>Pist topu görsel dönmesi.</summary>
        public enum TrackSpinMode
        {
            /// <summary>Dönme yok: toplar kalabalıkla taşınır.</summary>
            Off = 0,
            /// <summary>Kat edilen yola göre yuvarlanma × <see cref="BurstSpinReducedRatio"/>, ortam dönmesi yok.</summary>
            Reduced = 1,
            /// <summary>Yalnızca kalabalığa göre göreli hareket kadar yuvarlanma (birlikte akan top dönmez).</summary>
            Relative = 2,
            /// <summary>Eski: tam yuvarlanma (yol / yarıçap × 1.25) + sürekli ortam dönmesi.</summary>
            Full = 3,
        }
        /// <summary>Pist topu dönme kipi. PerfProbe "spin=0..3" ile değiştirilebilir.</summary>
        public static TrackSpinMode BurstSpinMode = TrackSpinMode.Off;
        /// <summary>Reduced kipinde yuvarlanma oranı (tam yuvarlanmanın kesri).</summary>
        public static float BurstSpinReducedRatio = 0.25f;

        private static readonly ProfilerMarker s_MBurstGather = new ProfilerMarker("TrackBurst.1_Gather");
        private static readonly ProfilerMarker s_MBurstJobs = new ProfilerMarker("TrackBurst.2_Jobs");
        private static readonly ProfilerMarker s_MBurstWrite = new ProfilerMarker("TrackBurst.3_WriteBack");
        private static readonly ProfilerMarker s_MBurstRender = new ProfilerMarker("TrackBurst.4_Render");

        // ==================== Kalıcı yerel (native) veri ====================

        private static GlassTrackLayout.CompiledTrack s_BTrack;
        private static BeltLoop s_BLoop;
        private static NativeArray<float> s_FSdf;
        private static NativeArray<float2> s_FFlow, s_FNrm;
        private static int s_FRes;
        private static NativeArray<int> s_CLookup;
        private static NativeArray<float2> s_CPts, s_CTang, s_CNorm;

        private static int s_BCap;
        private static NativeArray<float2> s_BPos0, s_BPos, s_BVel, s_BVelPred, s_BTang, s_BOutDir, s_BCorr, s_BImpactN, s_BRelVel;
        private static NativeArray<float> s_BOffset, s_BOutOff, s_BImpact, s_BPhase;
        private static NativeArray<byte> s_BOnRing;
        private static NativeArray<int> s_BLayer, s_BCell, s_BSorted;
        private static NativeArray<quaternion> s_BRot;
        private static NativeArray<int> s_GridStart, s_GridCount;
        private static int s_GridCap;

        // Çizim (LateUpdate) için: son adımdaki top sırası
        private static BallPhysicsController[] s_RBalls = new BallPhysicsController[0];
        private static int s_RCount;
        private static TransformAccessArray s_RTransforms;
        private static NativeArray<float3> s_RPrev, s_RCur;
        private static NativeArray<quaternion> s_RRotPrev, s_RRotCur;
        private static NativeArray<byte> s_RSkip;
        private static int s_RCap;
        private static bool s_RDirty = true;
        private static Transform s_RContainer;

        private static BurstTrackDriver s_Driver;

        /// <summary>Burst simülasyonu bu adımda çalıştı mı (Rigidbody moduna özgü kodlar bunu kontrol eder).</summary>
        public static bool BurstActive { get; private set; }

        private static void EnsureBurstDriver()
        {
            if (s_Driver != null || !Application.isPlaying) return;
            var go = new GameObject("TrackBurstDriver") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_Driver = go.AddComponent<BurstTrackDriver>();
        }

        internal static void DisposeBurst()
        {
            if (s_RTransforms.isCreated) s_RTransforms.Dispose();
            DisposeArr(ref s_FSdf); DisposeArr(ref s_FFlow); DisposeArr(ref s_FNrm);
            DisposeArr(ref s_CLookup); DisposeArr(ref s_CPts); DisposeArr(ref s_CTang); DisposeArr(ref s_CNorm);
            DisposeArr(ref s_BPos0); DisposeArr(ref s_BPos); DisposeArr(ref s_BVel); DisposeArr(ref s_BVelPred);
            DisposeArr(ref s_BTang); DisposeArr(ref s_BOutDir); DisposeArr(ref s_BCorr); DisposeArr(ref s_BImpactN); DisposeArr(ref s_BRelVel);
            DisposeArr(ref s_BOffset); DisposeArr(ref s_BOutOff); DisposeArr(ref s_BImpact); DisposeArr(ref s_BPhase);
            DisposeArr(ref s_BOnRing); DisposeArr(ref s_BLayer); DisposeArr(ref s_BCell); DisposeArr(ref s_BSorted);
            DisposeArr(ref s_BRot); DisposeArr(ref s_GridStart); DisposeArr(ref s_GridCount);
            DisposeArr(ref s_RPrev); DisposeArr(ref s_RCur); DisposeArr(ref s_RRotPrev); DisposeArr(ref s_RRotCur); DisposeArr(ref s_RSkip);
            s_BCap = 0; s_GridCap = 0; s_RCap = 0; s_RCount = 0;
            s_BTrack = null; s_BLoop = null;
            s_RDirty = true;
            s_Driver = null;
            BurstActive = false;
        }

        private static void DisposeArr<T>(ref NativeArray<T> a) where T : struct
        {
            if (a.IsCreated) a.Dispose();
            a = default;
        }

        private static void EnsureBallCapacity(int n)
        {
            if (s_BCap >= n) return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
            DisposeArr(ref s_BPos0); DisposeArr(ref s_BPos); DisposeArr(ref s_BVel); DisposeArr(ref s_BVelPred);
            DisposeArr(ref s_BTang); DisposeArr(ref s_BOutDir); DisposeArr(ref s_BCorr); DisposeArr(ref s_BImpactN);
            DisposeArr(ref s_BOffset); DisposeArr(ref s_BOutOff); DisposeArr(ref s_BImpact); DisposeArr(ref s_BPhase);
            DisposeArr(ref s_BOnRing); DisposeArr(ref s_BLayer); DisposeArr(ref s_BCell); DisposeArr(ref s_BSorted);
            DisposeArr(ref s_BRot); DisposeArr(ref s_BRelVel);
            var a = Allocator.Persistent;
            s_BRelVel = new NativeArray<float2>(cap, a);
            s_BPos0 = new NativeArray<float2>(cap, a); s_BPos = new NativeArray<float2>(cap, a);
            s_BVel = new NativeArray<float2>(cap, a); s_BVelPred = new NativeArray<float2>(cap, a);
            s_BTang = new NativeArray<float2>(cap, a); s_BOutDir = new NativeArray<float2>(cap, a);
            s_BCorr = new NativeArray<float2>(cap, a); s_BImpactN = new NativeArray<float2>(cap, a);
            s_BOffset = new NativeArray<float>(cap, a); s_BOutOff = new NativeArray<float>(cap, a);
            s_BImpact = new NativeArray<float>(cap, a); s_BPhase = new NativeArray<float>(cap, a);
            s_BOnRing = new NativeArray<byte>(cap, a); s_BLayer = new NativeArray<int>(cap, a);
            s_BCell = new NativeArray<int>(cap, a); s_BSorted = new NativeArray<int>(cap, a);
            s_BRot = new NativeArray<quaternion>(cap, a);
            s_BCap = cap;
        }

        private static void EnsureGridCapacity(int cells)
        {
            if (s_GridCap >= cells + 1) return;
            DisposeArr(ref s_GridStart); DisposeArr(ref s_GridCount);
            int cap = Mathf.NextPowerOfTwo(cells + 1);
            s_GridStart = new NativeArray<int>(cap, Allocator.Persistent);
            s_GridCount = new NativeArray<int>(cap, Allocator.Persistent);
            s_GridCap = cap;
        }

        /// <summary>Pist alanını ve halka orta çizgisini yerel diziye kopyalar (pist değişince bir kez).</summary>
        private static void EnsureBurstTrackData(GlassTrackLayout.CompiledTrack track, BeltLoop loop)
        {
            if (s_BTrack == track && s_BLoop == loop && s_FSdf.IsCreated) return;
            DisposeArr(ref s_FSdf); DisposeArr(ref s_FFlow); DisposeArr(ref s_FNrm);
            DisposeArr(ref s_CLookup); DisposeArr(ref s_CPts); DisposeArr(ref s_CTang); DisposeArr(ref s_CNorm);

            int fn = track.sdf != null ? track.sdf.Length : 0;
            s_FRes = Mathf.RoundToInt(Mathf.Sqrt(fn));
            if (s_FRes * s_FRes != fn || s_FRes < 2) s_FRes = 0;
            int fl = Mathf.Max(1, fn);
            s_FSdf = new NativeArray<float>(fl, Allocator.Persistent);
            s_FFlow = new NativeArray<float2>(fl, Allocator.Persistent);
            s_FNrm = new NativeArray<float2>(fl, Allocator.Persistent);
            for (int i = 0; i < fn; i++)
            {
                s_FSdf[i] = track.sdf[i];
                s_FFlow[i] = track.flowDir != null && i < track.flowDir.Length ? (float2)track.flowDir[i] : new float2(0f, -1f);
                s_FNrm[i] = track.wallNormal != null && i < track.wallNormal.Length ? (float2)track.wallNormal[i] : new float2(0f, 1f);
            }

            EnsureCenterLookup(loop);
            s_CLookup = new NativeArray<int>(loop.nearLookup, Allocator.Persistent);
            int cn = loop.centerPts.Length;
            s_CPts = new NativeArray<float2>(cn, Allocator.Persistent);
            s_CTang = new NativeArray<float2>(cn, Allocator.Persistent);
            s_CNorm = new NativeArray<float2>(cn, Allocator.Persistent);
            for (int i = 0; i < cn; i++)
            {
                s_CPts[i] = loop.centerPts[i];
                s_CTang[i] = loop.centerTang[i];
                s_CNorm[i] = loop.centerNorm[i];
            }
            s_BTrack = track;
            s_BLoop = loop;
        }

        /// <summary>Topu Burst simülasyonuna teslim eder: PhysX'ten çıkar (kinematik, collider kapalı).</summary>
        internal static void ConfigureBurstTrackBall(BallPhysicsController b)
        {
            if (b == null) return;
            if (b.rb != null)
            {
                if (!b.rb.isKinematic)
                {
                    b.rb.linearVelocity = Vector3.zero;
                    b.rb.angularVelocity = Vector3.zero;
                }
                b.rb.isKinematic = true;
                b.rb.interpolation = RigidbodyInterpolation.None;
            }
            if (b.sphereCollider != null) b.sphereCollider.enabled = false;
            b.burstRot = b.transform.rotation;
            b.burstPrevRot = b.burstRot;
            b.burstConfigured = true;
            s_RDirty = true;
        }

        // ==================== Adım ====================

        private static bool StepBurst(ShapedGlassContainer container, List<BallPhysicsController> activeBalls, float dt,
            BeltLoop loop, GlassTrackLayout.CompiledTrack track, Transform ct, float speedMul,
            bool holdBelt = false, bool emitFeedback = true)
        {
            IsMoving = false;
            BurstActive = true;
            EnsureBurstDriver();
            EnsureBurstTrackData(track, loop);
            if (s_GateObj != null && s_GateObj.activeSelf) s_GateObj.SetActive(false); // kapı burada bir kısıt; PhysX collider'ı gereksiz

            float d = Mathf.Max(0.04f, track.ballDiameter);
            float r = d * 0.5f;
            Rect fr = track.fieldRect;
            int cols = Mathf.Max(1, Mathf.CeilToInt(fr.width / d));
            int rows = Mathf.Max(1, Mathf.CeilToInt(fr.height / d));
            EnsureGridCapacity(cols * rows);
            EnsureCapacity(activeBalls.Count + 8);
            EnsureBallCapacity(activeBalls.Count + 8);

            float containerScale = Mathf.Max(1e-4f, Mathf.Abs(ct.lossyScale.x));
            bool layerLock = s_LayerLock;
            int openLayer = layerLock ? OpenLayer : int.MaxValue;

            // 1. Topla (yönetilen taraf): yalnızca alan okumaları, native çağrı yok
            s_MBurstGather.Begin();
            int n = 0;
            Matrix4x4 w2l = ct.worldToLocalMatrix;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || !b.trackQueueDriven || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy)
                    continue;
                if (!b.burstConfigured) ConfigureBurstTrackBall(b);
                if (!b.trackQueuePosValid)
                {
                    Vector3 lp = w2l.MultiplyPoint3x4(b.transform.position);
                    b.trackQueueLocalPos = new Vector2(lp.x, lp.y);
                    b.trackQueueLocalZ = lp.z;
                    b.trackQueuePosValid = true;
                    b.burstPrevLocalPos = b.trackQueueLocalPos;
                }
                s_All[n] = b;
                s_BPos0[n] = b.trackQueueLocalPos;
                s_BPos[n] = b.trackQueueLocalPos;
                s_BVel[n] = b.trackVel;
                s_BLayer[n] = b.trackLayer;
                s_BRot[n] = b.burstRot;
                s_BPhase[n] = (b.GetInstanceID() & 1023) * 0.024f;
                n++;
            }
            s_MBurstGather.End();

            int ringCount = 0;
            if (n > 0)
            {
                s_MBurstJobs.Begin();
                var p = new BurstParams
                {
                    n = n,
                    dt = dt,
                    d = d,
                    r = r,
                    ringBand = track.innerHalfWidth + r * 0.6f,
                    innerHalfWidth = track.innerHalfWidth,
                    exitPoint = track.exitPoint,
                    beltSpeed = holdBelt ? 0f : BeltSpeed * speedMul,
                    roadSpeed = holdBelt ? 0f : MaxSpeed * speedMul * 0.6f,
                    driveGain = PhysDriveGain * speedMul,
                    maxDriveAccel = PhysMaxDriveAccel * speedMul * speedMul,
                    spreadAccel = PhysSpreadAccel * speedMul * speedMul,
                    downG = PhysDownGravity * speedMul,
                    cohesionAccel = PhysCohesionAccel * speedMul,
                    alignGain = PhysVelAlignGain * speedMul,
                    damping = BurstLinearDamping,
                    maxSpeed = MaxBallSpeed * speedMul,
                    contact = d * BurstContactRatio,
                    relax = BurstContactRelax,
                    maxDepen = BurstMaxDepenetration / containerScale,
                    layerLock = layerLock ? 1 : 0,
                    openLayer = openLayer,
                    keepStiff = LayerKeepStiffness,
                    keepDamp = LayerKeepDamping,
                    layerWallTol = LayerWallTolerance,
                    hasRoad = loop.hasRoad ? 1 : 0,
                    roadJunction = loop.roadJunction,
                    roadDir = loop.roadDir,
                    roadHalfW = loop.roadHalfW,
                    roadGate = loop.roadGate,
                    fieldMin = new float2(fr.xMin, fr.yMin),
                    fieldSize = new float2(Mathf.Max(0.001f, fr.width), Mathf.Max(0.001f, fr.height)),
                    fieldRes = s_FRes,
                    gridMin = new float2(fr.xMin, fr.yMin),
                    gridCell = d,
                    gridCols = cols,
                    gridRows = rows,
                    nearMin = new float2(loop.nearRect.xMin, loop.nearRect.yMin),
                    nearCell = loop.nearCell,
                    nearCols = loop.nearCols,
                    nearRows = loop.nearRows,
                    centerCount = loop.centerPts.Length,
                    worldScale = containerScale * BurstImpactScale,
                    ctRot = ct.rotation,
                    ballRadiusWorld = r * containerScale,
                    time = Application.isPlaying ? Time.time : 0f,
                    spinMode = (int)BurstSpinMode,
                    spinRatio = BurstSpinReducedRatio,
                };

                // Kapasite kapısı (yol → halka) ilk sınıflandırmadan sonra belli olur; önce sınıflandır
                var classify = new ClassifyJob
                {
                    p = p, pos = s_BPos, tang = s_BTang, outDir = s_BOutDir, offset = s_BOffset, outOff = s_BOutOff, onRing = s_BOnRing,
                    sdf = s_FSdf, flow = s_FFlow, nrm = s_FNrm, lookup = s_CLookup, cPts = s_CPts, cTang = s_CTang, cNorm = s_CNorm,
                };
                classify.Schedule(n, 32).Complete();
                for (int i = 0; i < n; i++) if (s_BOnRing[i] != 0) ringCount++;
                p.canJoin = CanJoinBelt(ringCount) ? 1 : 0;

                JobHandle h = new GridJob { p = p, pos = s_BPos, cell = s_BCell, sorted = s_BSorted, start = s_GridStart, count = s_GridCount }.Schedule();
                h = new ForceJob
                {
                    p = p, pos = s_BPos, vel = s_BVel, tang = s_BTang, outDir = s_BOutDir, offset = s_BOffset, outOff = s_BOutOff,
                    onRing = s_BOnRing, layer = s_BLayer, sorted = s_BSorted, start = s_GridStart, count = s_GridCount,
                    sdf = s_FSdf, nrm = s_FNrm, velPred = s_BVelPred, posOut = s_BPos0, impact = s_BImpact, impactN = s_BImpactN,
                    relVel = s_BRelVel,
                }.Schedule(n, 32, h);
                // posOut = tahmini konum (s_BPos0'a yazılır); s_BPos başlangıç konumu olarak kalır → yer değiştir
                h.Complete();
                var tmp = s_BPos; s_BPos = s_BPos0; s_BPos0 = tmp; // artık: s_BPos = tahmini, s_BPos0 = adım başı

                h = new GridJob { p = p, pos = s_BPos, cell = s_BCell, sorted = s_BSorted, start = s_GridStart, count = s_GridCount }.Schedule();
                int iters = Mathf.Clamp(BurstSolverIterations, 1, 12);
                for (int it = 0; it < iters; it++)
                {
                    h = new ContactJob { p = p, pos = s_BPos, sorted = s_BSorted, start = s_GridStart, count = s_GridCount, corr = s_BCorr }.Schedule(n, 32, h);
                    h = new ApplyJob
                    {
                        p = p, pos = s_BPos, pos0 = s_BPos0, corr = s_BCorr, onRing = s_BOnRing, layer = s_BLayer, outDir = s_BOutDir, outOff = s_BOutOff,
                        sdf = s_FSdf, nrm = s_FNrm,
                    }.Schedule(n, 32, h);
                }
                h = new FinalizeJob
                {
                    p = p, pos0 = s_BPos0, pos = s_BPos, velPred = s_BVelPred, vel = s_BVel, rot = s_BRot, phase = s_BPhase,
                    relVel = s_BRelVel,
                }.Schedule(n, 32, h);
                h.Complete();
                s_MBurstJobs.End();

                // 2. Geri yaz (yönetilen): oyun kuralları bu alanları okur
                s_MBurstWrite.Begin();
                Quaternion ctRot = ct.rotation;
                for (int i = 0; i < n; i++)
                {
                    var b = s_All[i];
                    float2 np = s_BPos[i];
                    float2 nv = s_BVel[i];
                    b.burstPrevLocalPos = b.trackQueueLocalPos;
                    b.burstPrevRot = b.burstRot;
                    b.trackQueueLocalPos = new Vector2(np.x, np.y);
                    b.trackVel = new Vector2(nv.x, nv.y);
                    b.trackQueueSpeed = math.length(nv);
                    b.trackLoopSlot = s_BOnRing[i] != 0 ? RingMarker : -1;
                    b.burstRot = s_BRot[i];

                    float hit = s_BImpact[i];
                    if (emitFeedback && hit > 0.45f)
                    {
                        float2 hn = s_BImpactN[i];
                        b.ApplyContactFeedback(hit, ctRot * new Vector3(hn.x, hn.y, 0f));
                    }
                    if (s_BOnRing[i] == 0 && math.lengthsq(np - s_BPos0[i]) > MinMoveSq * 4f) IsMoving = true;
                }
                UpdateRenderSet(n, ct);
                s_MBurstWrite.End();
            }
            else
            {
                s_RCount = 0;
                s_RDirty = true;
            }

            s_RingCount = ringCount;
            s_Occupied = ringCount;
            RoadQueueCount = n - ringCount;

            // 3. Gizli kuyruk: yolun tepesi boşsa sıradaki top belirir; yol yoksa halka girişinde (kapasite varsa)
            if (!holdBelt && s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
            {
                Vector2 spawn;
                bool allowed;
                if (loop.hasRoad) { spawn = loop.roadTop; allowed = true; }
                else
                {
                    int ei = Mathf.Clamp(Mathf.RoundToInt(loop.entryU * loop.centerPts.Length), 0, loop.centerPts.Length - 1);
                    spawn = loop.centerPts[ei];
                    allowed = CanJoinBelt(ringCount);
                }
                if (allowed)
                {
                    float clearSq = (d * 1.05f) * (d * 1.05f);
                    bool free = true;
                    for (int i = 0; i < n && free; i++) if (math.lengthsq(s_BPos[i] - (float2)spawn) < clearSq) free = false;
                    if (free && SpawnQueuedBall(container, spawn) != null)
                    {
                        s_NextRoadSpawn = Time.time + 0.06f / speedMul;
                        IsMoving = true;
                    }
                }
            }

            Array.Clear(s_All, 0, n);
            return n > 0;
        }

        // ==================== Çizim (LateUpdate) ====================

        private static void EnsureRenderCapacity(int n)
        {
            if (s_RCap >= n) return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
            DisposeArr(ref s_RPrev); DisposeArr(ref s_RCur); DisposeArr(ref s_RRotPrev); DisposeArr(ref s_RRotCur); DisposeArr(ref s_RSkip);
            s_RPrev = new NativeArray<float3>(cap, Allocator.Persistent);
            s_RCur = new NativeArray<float3>(cap, Allocator.Persistent);
            s_RRotPrev = new NativeArray<quaternion>(cap, Allocator.Persistent);
            s_RRotCur = new NativeArray<quaternion>(cap, Allocator.Persistent);
            s_RSkip = new NativeArray<byte>(cap, Allocator.Persistent);
            if (s_RBalls.Length < cap) Array.Resize(ref s_RBalls, cap);
            s_RCap = cap;
        }

        /// <summary>Bu adımın topları ve önceki/sonraki konumları; üye kümesi değiştiyse transform listesi yenilenir.</summary>
        private static void UpdateRenderSet(int n, Transform ct)
        {
            EnsureRenderCapacity(n);
            bool changed = s_RDirty || n != s_RCount || s_RContainer != ct || !s_RTransforms.isCreated;
            if (!changed)
            {
                for (int i = 0; i < n; i++) if (s_RBalls[i] != s_All[i]) { changed = true; break; }
            }
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                s_RBalls[i] = b;
                s_RPrev[i] = new float3(b.burstPrevLocalPos.x, b.burstPrevLocalPos.y, b.trackQueueLocalZ);
                s_RCur[i] = new float3(b.trackQueueLocalPos.x, b.trackQueueLocalPos.y, b.trackQueueLocalZ);
                s_RRotPrev[i] = b.burstPrevRot;
                s_RRotCur[i] = b.burstRot;
            }
            for (int i = n; i < s_RCount; i++) s_RBalls[i] = null;
            if (changed)
            {
                var ts = new Transform[n];
                for (int i = 0; i < n; i++) ts[i] = s_All[i].transform;
                if (s_RTransforms.isCreated) s_RTransforms.SetTransforms(ts);
                else s_RTransforms = new TransformAccessArray(ts, 8);
                s_RDirty = false;
            }
            s_RCount = n;
            s_RContainer = ct;
        }

        /// <summary>LateUpdate: topları iki fizik adımı arasında yumuşatarak transform'lara yazar.</summary>
        internal static void RenderBurst()
        {
            if (!BurstActive || s_RCount == 0 || !s_RTransforms.isCreated || s_RContainer == null) return;
            s_MBurstRender.Begin();
            // Bu kare içinde pistten ayrılan (çıkışa çekilen, havuza dönen) topların transform'una dokunma
            for (int i = 0; i < s_RCount; i++)
            {
                var b = s_RBalls[i];
                s_RSkip[i] = (byte)((b == null || !b.trackQueueDriven || b.state != BallState.InContainer || !b.burstConfigured) ? 1 : 0);
            }
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Mathf.Max(1e-4f, Time.fixedDeltaTime));
            new RenderJob
            {
                l2w = s_RContainer.localToWorldMatrix,
                alpha = alpha,
                prev = s_RPrev,
                cur = s_RCur,
                rotPrev = s_RRotPrev,
                rotCur = s_RRotCur,
                skip = s_RSkip,
            }.Schedule(s_RTransforms).Complete();
            s_MBurstRender.End();
        }

        /// <summary>Simülasyon kapandığında (level/kip değişimi) çizim listesini bırak.</summary>
        internal static void ResetBurstRenderSet()
        {
            s_RCount = 0;
            s_RDirty = true;
            BurstActive = false;
        }

        internal static void SyncBurstRenderAfterPrewarm()
        {
            if (!BurstActive || s_RCount <= 0 || !s_RPrev.IsCreated || !s_RCur.IsCreated) return;
            for (int i = 0; i < s_RCount; i++)
            {
                s_RPrev[i] = s_RCur[i];
                if (s_RRotPrev.IsCreated && s_RRotCur.IsCreated) s_RRotPrev[i] = s_RRotCur[i];
            }
        }

        // ==================== Burst işleri ====================

        private struct BurstParams
        {
            public int n;
            public float dt, d, r, ringBand, innerHalfWidth;
            public float2 exitPoint;
            public float beltSpeed, roadSpeed, driveGain, maxDriveAccel, spreadAccel, downG, cohesionAccel, alignGain;
            public float damping, maxSpeed, contact, relax, maxDepen;
            public int layerLock, openLayer;
            public float keepStiff, keepDamp, layerWallTol;
            public int hasRoad, canJoin;
            public float2 roadJunction, roadDir;
            public float roadHalfW, roadGate;
            public float2 fieldMin, fieldSize;
            public int fieldRes;
            public float2 gridMin;
            public float gridCell;
            public int gridCols, gridRows;
            public float2 nearMin;
            public float nearCell;
            public int nearCols, nearRows, centerCount;
            public float worldScale;
            public quaternion ctRot;
            public float ballRadiusWorld;
            public float time;
            public int spinMode;
            public float spinRatio;
        }

        private static float SampleSdf(in BurstParams p, NativeArray<float> sdf, NativeArray<float2> nrm, float2 pos, out float2 inward)
        {
            int res = p.fieldRes;
            if (res < 2) { inward = new float2(0f, 1f); return -1f; }
            float2 uv = math.saturate((pos - p.fieldMin) / p.fieldSize) * (res - 1);
            int ix = math.clamp((int)math.floor(uv.x), 0, res - 2);
            int iy = math.clamp((int)math.floor(uv.y), 0, res - 2);
            float tx = uv.x - ix, ty = uv.y - iy;
            int i00 = iy * res + ix, i10 = i00 + 1, i01 = i00 + res, i11 = i01 + 1;
            float s = math.lerp(math.lerp(sdf[i00], sdf[i10], tx), math.lerp(sdf[i01], sdf[i11], tx), ty);
            float2 nn = math.lerp(math.lerp(nrm[i00], nrm[i10], tx), math.lerp(nrm[i01], nrm[i11], tx), ty);
            float l = math.length(nn);
            inward = l > 1e-4f ? nn / l : new float2(0f, 1f);
            return s;
        }

        private static float2 SampleFlow(in BurstParams p, NativeArray<float2> flow, float2 pos)
        {
            int res = p.fieldRes;
            if (res < 2) return new float2(0f, -1f);
            float2 uv = math.saturate((pos - p.fieldMin) / p.fieldSize) * (res - 1);
            int ix = math.clamp((int)math.floor(uv.x), 0, res - 2);
            int iy = math.clamp((int)math.floor(uv.y), 0, res - 2);
            float tx = uv.x - ix, ty = uv.y - iy;
            int i00 = iy * res + ix, i10 = i00 + 1, i01 = i00 + res, i11 = i01 + 1;
            float2 f = math.lerp(math.lerp(flow[i00], flow[i10], tx), math.lerp(flow[i01], flow[i11], tx), ty);
            float l = math.length(f);
            return l > 1e-2f ? f / l : new float2(0f, -1f);
        }

        private static int CellOf(in BurstParams p, float2 pos)
        {
            int cx = math.clamp((int)math.floor((pos.x - p.gridMin.x) / p.gridCell), 0, p.gridCols - 1);
            int cy = math.clamp((int)math.floor((pos.y - p.gridMin.y) / p.gridCell), 0, p.gridRows - 1);
            return cy * p.gridCols + cx;
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct ClassifyJob : IJobParallelFor
        {
            public BurstParams p;
            public NativeArray<float2> pos;
            [WriteOnly] public NativeArray<float2> tang, outDir;
            [WriteOnly] public NativeArray<float> offset, outOff;
            [WriteOnly] public NativeArray<byte> onRing;
            [ReadOnly] public NativeArray<float> sdf;
            [ReadOnly] public NativeArray<float2> flow, nrm;
            [ReadOnly] public NativeArray<int> lookup;
            [ReadOnly] public NativeArray<float2> cPts, cTang, cNorm;

            public void Execute(int i)
            {
                float2 q = pos[i];
                // Güvenlik ağı: kanal dışına çıkmış topu en yakın iç noktaya al (Rigidbody modundaki EscapeRecoveries karşılığı)
                float s = SampleSdf(p, sdf, nrm, q, out float2 inward);
                if (s > 0f && s < p.d * 3f)
                {
                    q += inward * (s + p.r * 1.1f);
                    pos[i] = q;
                }

                // En yakın halka orta çizgisi noktası (hücre tablosu + komşu inceltme)
                int lx = math.clamp((int)math.floor((q.x - p.nearMin.x) / p.nearCell), 0, p.nearCols - 1);
                int ly = math.clamp((int)math.floor((q.y - p.nearMin.y) / p.nearCell), 0, p.nearRows - 1);
                int ci = lookup[ly * p.nearCols + lx];
                int cn = p.centerCount;
                float best = math.lengthsq(cPts[ci] - q);
                int bi = ci;
                for (int k = -4; k <= 4; k++)
                {
                    if (k == 0) continue;
                    int j = ((ci + k) % cn + cn) % cn;
                    float dSq = math.lengthsq(cPts[j] - q);
                    if (dSq < best) { best = dSq; bi = j; }
                }

                bool ring = best < p.ringBand * p.ringBand;
                onRing[i] = (byte)(ring ? 1 : 0);
                if (ring)
                {
                    float2 t = cTang[bi];
                    tang[i] = t;
                    float2 rel = q - cPts[bi];
                    offset[i] = math.dot(rel, new float2(-t.y, t.x));
                    outDir[i] = cNorm[bi];
                    outOff[i] = math.dot(rel, cNorm[bi]);
                }
                else
                {
                    tang[i] = SampleFlow(p, flow, q);
                    offset[i] = 0f;
                    outDir[i] = new float2(0f, 1f);
                    outOff[i] = 0f;
                }
            }
        }

        /// <summary>Sayım sıralamalı ızgara: her hücrenin topları sorted[start..start+count).</summary>
        [BurstCompile]
        private struct GridJob : IJob
        {
            public BurstParams p;
            [ReadOnly] public NativeArray<float2> pos;
            public NativeArray<int> cell, sorted, start, count;

            public void Execute()
            {
                int cells = p.gridCols * p.gridRows;
                for (int c = 0; c < cells; c++) count[c] = 0;
                for (int i = 0; i < p.n; i++)
                {
                    int c = CellOf(p, pos[i]);
                    cell[i] = c;
                    count[c]++;
                }
                int acc = 0;
                for (int c = 0; c < cells; c++)
                {
                    start[c] = acc;
                    acc += count[c];
                    count[c] = 0;
                }
                for (int i = 0; i < p.n; i++)
                {
                    int c = cell[i];
                    sorted[start[c] + count[c]] = i;
                    count[c]++;
                }
            }
        }

        /// <summary>Rigidbody modundaki StepRigidbody kuvvetleri + sönüm + tahmini konum; çarpma hızı ölçümü.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct ForceJob : IJobParallelFor
        {
            public BurstParams p;
            [ReadOnly] public NativeArray<float2> pos, vel, tang, outDir;
            [ReadOnly] public NativeArray<float> offset, outOff;
            [ReadOnly] public NativeArray<byte> onRing;
            [ReadOnly] public NativeArray<int> layer, sorted, start, count;
            [ReadOnly] public NativeArray<float> sdf;
            [ReadOnly] public NativeArray<float2> nrm;
            [WriteOnly] public NativeArray<float2> velPred, posOut, impactN, relVel;
            [WriteOnly] public NativeArray<float> impact;

            public void Execute(int i)
            {
                float d = p.d;
                float2 q = pos[i];
                float2 t = tang[i];
                float2 nr = new float2(-t.y, t.x);
                float2 v = vel[i];
                float along = math.dot(v, t);
                bool ring = onRing[i] != 0;

                bool blocked = false;
                float2 cohSum = 0f; int cohCount = 0;
                float2 velDiffSum = 0f; int velCount = 0;
                float2 nbVelSum = 0f; int nbCount = 0; // dönme için: yakın komşuların ortalama hızı (halka/yol fark etmez)
                float cohMin = d * 1.03f, cohMax = d * 2.00f;
                float alignMaxSq = (d * 1.60f) * (d * 1.60f);
                float aheadMax = d * 1.15f, aheadLat = d * 0.7f;
                float hit = 0f; float2 hitN = 0f;
                float touchSq = (p.contact * 1.02f) * (p.contact * 1.02f);

                int cx = math.clamp((int)math.floor((q.x - p.gridMin.x) / p.gridCell), 0, p.gridCols - 1);
                int cy = math.clamp((int)math.floor((q.y - p.gridMin.y) / p.gridCell), 0, p.gridRows - 1);
                for (int gy = math.max(0, cy - 1); gy <= math.min(p.gridRows - 1, cy + 1); gy++)
                {
                    for (int gx = math.max(0, cx - 1); gx <= math.min(p.gridCols - 1, cx + 1); gx++)
                    {
                        int c = gy * p.gridCols + gx;
                        int s0 = start[c], s1 = s0 + count[c];
                        for (int s = s0; s < s1; s++)
                        {
                            int j = sorted[s];
                            if (j == i) continue;
                            float2 diff = pos[j] - q;
                            float dSq = math.lengthsq(diff);
                            if (!blocked)
                            {
                                float al = math.dot(diff, t);
                                if (al > 0f && al < aheadMax && math.abs(math.dot(diff, nr)) < aheadLat)
                                {
                                    if (math.dot(vel[j], t) < p.beltSpeed * 0.25f) blocked = true;
                                }
                            }
                            if (ring && dSq > cohMin * cohMin && dSq < cohMax * cohMax)
                            {
                                float dist = math.sqrt(dSq);
                                float pullFactor = math.saturate((dist - cohMin) / math.max(0.01f, d));
                                float aheadWeight = math.dot(diff, t) > 0f ? 1.35f : 0.75f;
                                cohSum += (diff / dist) * (pullFactor * aheadWeight);
                                cohCount++;
                            }
                            if (dSq < alignMaxSq)
                            {
                                if (ring)
                                {
                                    velDiffSum += vel[j] - v;
                                    velCount++;
                                }
                                nbVelSum += vel[j];
                                nbCount++;
                            }
                            // Çarpma: değen ve birbirine yaklaşan komşu (eski OnCollisionEnter göreli hızı karşılığı)
                            if (dSq < touchSq && dSq > 1e-10f)
                            {
                                float2 nn = -diff * math.rsqrt(dSq); // j'den i'ye
                                float2 relV = v - vel[j];
                                // Yalnızca yaklaşan çiftler; ölçü eski Collision.relativeVelocity gibi toplam göreli hız
                                if (math.dot(relV, nn) < 0f)
                                {
                                    float rs = math.length(relV);
                                    if (rs > hit) { hit = rs; hitN = nn; }
                                }
                            }
                        }
                    }
                }

                // Duvara çarpma
                float wsd = SampleSdf(p, sdf, nrm, q, out float2 inward);
                if (wsd + p.r * 1.02f > 0f)
                {
                    float vn = math.dot(v, inward);
                    if (-vn > hit) { hit = -vn; hitN = inward; }
                }

                float2 accel = 0f;
                bool resting = !ring && blocked && math.lengthsq(v) < 0.03f * 0.03f;
                if (!resting)
                {
                    float target = ring ? p.beltSpeed : p.roadSpeed;
                    float a = math.clamp((target - along) * p.driveGain, -p.maxDriveAccel, p.maxDriveAccel);
                    if (blocked && a > 0f) a *= 0.35f;
                    accel = t * a;
                    if (ring)
                    {
                        float band = math.max(0f, p.innerHalfWidth - p.r) * 0.55f;
                        float excess = math.abs(offset[i]) - band;
                        if (excess > 0f) accel -= nr * (math.sign(offset[i]) * p.spreadAccel * math.saturate(excess / math.max(0.01f, p.r)));
                    }
                }

                if (ring)
                {
                    float latGrav = math.dot(new float2(0f, -p.downG), nr);
                    float2 grav = nr * latGrav;
                    if (math.abs(q.y - p.exitPoint.y) <= p.innerHalfWidth * 2.2f && t.y <= 0.05f) grav.y -= p.downG * 0.42f;
                    int ly = layer[i];
                    bool locked = p.layerLock != 0 && ly >= 0 && ly > p.openLayer;
                    if (locked)
                    {
                        float gOut = math.dot(grav, outDir[i]);
                        if (gOut > 0f) grav -= outDir[i] * gOut;
                    }
                    accel += grav;
                    if (cohCount > 0) accel += (cohSum / cohCount) * p.cohesionAccel;
                    if (velCount > 0) accel += (velDiffSum / velCount) * p.alignGain;
                    if (locked) accel += LayerBand(p, ly, outOff[i], outDir[i], v);
                }

                v += accel * p.dt;
                v *= math.max(0f, 1f - p.damping * p.dt);
                float sp = math.lengthsq(v);
                if (sp > p.maxSpeed * p.maxSpeed) v *= p.maxSpeed * math.rsqrt(sp);
                velPred[i] = v;
                posOut[i] = q + v * p.dt;
                // Kalabalığa göre göreli hız (komşusu yoksa kendi hızı): Relative dönme kipi bunu kullanır
                relVel[i] = nbCount > 0 ? v - nbVelSum / nbCount : v;
                impact[i] = hit * p.worldScale;
                impactN[i] = hitN;
            }

            private static float2 LayerBand(in BurstParams p, int layer, float outOffset, float2 outDir, float2 v)
            {
                float half = math.max(1e-4f, p.innerHalfWidth - p.d * 0.56f);
                float edge = half * 0.33f;
                float pad = p.d * 0.08f;
                float lo, hi;
                if (layer == 0) { lo = edge + pad; hi = float.MaxValue; }
                else if (layer == 1) { lo = -edge + pad; hi = edge - pad; }
                else { lo = float.MinValue; hi = -edge - pad; }
                if (lo > hi) { float mid = (lo + hi) * 0.5f; lo = hi = mid; }
                float target = math.clamp(outOffset, lo, hi);
                float err = target - outOffset;
                if (math.abs(err) < 1e-5f) return 0f;
                float latVel = math.dot(v, outDir);
                return outDir * (err * p.keepStiff - latVel * p.keepDamp);
            }
        }

        /// <summary>Temas: her top için komşulardan gelen ayırma düzeltmesi (Jacobi; karşılıklı simetrik).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct ContactJob : IJobParallelFor
        {
            public BurstParams p;
            [ReadOnly] public NativeArray<float2> pos;
            [ReadOnly] public NativeArray<int> sorted, start, count;
            [WriteOnly] public NativeArray<float2> corr;

            public void Execute(int i)
            {
                float2 q = pos[i];
                float c2 = p.contact * p.contact;
                float2 acc = 0f;
                int cx = math.clamp((int)math.floor((q.x - p.gridMin.x) / p.gridCell), 0, p.gridCols - 1);
                int cy = math.clamp((int)math.floor((q.y - p.gridMin.y) / p.gridCell), 0, p.gridRows - 1);
                for (int gy = math.max(0, cy - 1); gy <= math.min(p.gridRows - 1, cy + 1); gy++)
                {
                    for (int gx = math.max(0, cx - 1); gx <= math.min(p.gridCols - 1, cx + 1); gx++)
                    {
                        int cc = gy * p.gridCols + gx;
                        int s0 = start[cc], s1 = s0 + count[cc];
                        for (int s = s0; s < s1; s++)
                        {
                            int j = sorted[s];
                            if (j == i) continue;
                            float2 diff = q - pos[j];
                            float dSq = math.lengthsq(diff);
                            if (dSq >= c2) continue;
                            float dist = math.sqrt(dSq);
                            // Tam üst üste: indekse göre belirlenimli ayırma yönü
                            float2 nn = dist > 1e-5f ? diff / dist : (i < j ? new float2(0f, 1f) : new float2(0f, -1f));
                            acc += nn * ((p.contact - dist) * 0.5f);
                        }
                    }
                }
                corr[i] = acc;
            }
        }

        /// <summary>Düzeltmeyi uygula; cam duvarı, giriş yolu kapısı ve katman duvarı kısıtları.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct ApplyJob : IJobParallelFor
        {
            public BurstParams p;
            public NativeArray<float2> pos;
            [ReadOnly] public NativeArray<float2> pos0, corr, outDir;
            [ReadOnly] public NativeArray<byte> onRing;
            [ReadOnly] public NativeArray<int> layer;
            [ReadOnly] public NativeArray<float> outOff;
            [ReadOnly] public NativeArray<float> sdf;
            [ReadOnly] public NativeArray<float2> nrm;

            public void Execute(int i)
            {
                float2 c = corr[i] * p.relax;
                float cl = math.length(c);
                float maxStep = p.d * 0.35f; // tek yinelemede büyük ışınlanma olmasın (doğumda üst üste binme)
                if (cl > maxStep) c *= maxStep / cl;
                float2 q = pos[i] + c;

                // Cam duvarı (mesafe alanı)
                float s = SampleSdf(p, sdf, nrm, q, out float2 inward);
                float over = s + p.r * 1.02f;
                if (over > 0f && s < p.d * 2f) q += inward * over;

                // Halka kapasitesi doluyken yoldaki toplar halkanın şeridine giremez
                if (p.canJoin == 0 && onRing[i] == 0 && p.hasRoad != 0)
                {
                    float2 rel = q - p.roadJunction;
                    float al = math.dot(rel, p.roadDir);
                    float lat = math.abs(rel.x * p.roadDir.y - rel.y * p.roadDir.x);
                    if (lat < p.roadHalfW * 1.6f && al > -p.roadHalfW && al < p.roadGate)
                        q += p.roadDir * (p.roadGate - al);
                }

                // Katman kilidi sert duvarı: kilitli katmandaki top kendi bandının dış kenarını aşamaz
                int ly = layer[i];
                if (p.layerLock != 0 && onRing[i] != 0 && ly >= 0 && ly > p.openLayer)
                {
                    float half = math.max(1e-4f, p.innerHalfWidth - p.d * 0.56f);
                    float edge = half * 0.33f;
                    float pad = p.d * 0.08f;
                    float bandOuter = ly == 1 ? (edge - pad) : (-edge - pad);
                    float wall = bandOuter + p.d * p.layerWallTol;
                    float2 od = outDir[i];
                    // Güncel enine konum: adım başındaki ofset + adım boyunca dışa doğru yer değiştirme
                    float curOff = outOff[i] + math.dot(q - pos0[i], od);
                    if (curOff > wall && curOff - wall <= p.d * 0.5f) q -= od * (curOff - wall);
                }
                pos[i] = q;
            }
        }

        /// <summary>Hız (konum değişiminden; çözümün ayırma hızı sınırlı) ve görsel yuvarlanma.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct FinalizeJob : IJobParallelFor
        {
            public BurstParams p;
            [ReadOnly] public NativeArray<float2> pos0, pos, velPred, relVel;
            [ReadOnly] public NativeArray<float> phase;
            [WriteOnly] public NativeArray<float2> vel;
            public NativeArray<quaternion> rot;

            public void Execute(int i)
            {
                float2 predicted = pos0[i] + velPred[i] * p.dt;
                float2 vCorr = (pos[i] - predicted) / p.dt;
                float vcl = math.length(vCorr);
                if (vcl > p.maxDepen) vCorr *= p.maxDepen / vcl;
                float2 v = velPred[i] + vCorr;
                float sp = math.lengthsq(v);
                if (sp > p.maxSpeed * p.maxSpeed) v *= p.maxSpeed * math.rsqrt(sp);
                vel[i] = v;

                if (p.spinMode == (int)TrackSpinMode.Off) return; // dönme yok: rotasyon olduğu gibi kalır

                // Yuvarlanma yolu: Full/Reduced → kat edilen yol, Relative → kalabalığa göre göreli yol
                float2 delta = p.spinMode == (int)TrackSpinMode.Relative ? relVel[i] * p.dt : pos[i] - pos0[i];
                float3 wd = math.mul(p.ctRot, new float3(delta * p.worldScale, 0f));
                float dist = math.length(wd);
                float ph = phase[i];
                float3 amb = math.normalizesafe(new float3(
                    math.cos(p.time * 3.1f + ph),
                    math.sin(p.time * 3.6f + ph * 1.4f),
                    math.sin(p.time * 2.5f - ph) * 0.5f), new float3(0f, 1f, 0f));
                bool full = p.spinMode == (int)TrackSpinMode.Full;
                float ambRad = full ? math.radians(46f) * p.dt : 0f; // ortam dönmesi yalnızca eski (Full) kipte
                float ratio = full ? 1.25f : (p.spinMode == (int)TrackSpinMode.Reduced ? 1.25f * p.spinRatio : 1f);
                quaternion q = rot[i];
                if (dist > 1e-5f)
                {
                    float3 axis = math.normalizesafe(math.cross(new float3(0f, 0f, -1f), wd / dist), new float3(0f, 1f, 0f));
                    float rollRad = (dist / math.max(0.02f, p.ballRadiusWorld)) * ratio;
                    q = math.mul(math.mul(quaternion.AxisAngle(axis, rollRad), quaternion.AxisAngle(amb, ambRad)), q);
                }
                else if (full)
                {
                    q = math.mul(quaternion.AxisAngle(amb, ambRad * 0.65f), q);
                }
                rot[i] = math.normalize(q);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        private struct RenderJob : IJobParallelForTransform
        {
            public float4x4 l2w;
            public float alpha;
            [ReadOnly] public NativeArray<float3> prev, cur;
            [ReadOnly] public NativeArray<quaternion> rotPrev, rotCur;
            [ReadOnly] public NativeArray<byte> skip;

            public void Execute(int i, TransformAccess t)
            {
                if (skip[i] != 0 || !t.isValid) return;
                float3 lp = math.lerp(prev[i], cur[i], alpha);
                t.position = math.transform(l2w, lp);
                t.rotation = math.slerp(rotPrev[i], rotCur[i], alpha);
            }
        }
    }

    /// <summary>Burst pist simülasyonunun LateUpdate çizimi ve yerel bellek ömrü.</summary>
    [DefaultExecutionOrder(8000)] // BallSpeedStreaks (9000) topların güncel konumunu okusun
    internal sealed class BurstTrackDriver : MonoBehaviour
    {
        private void LateUpdate() => TrackCrowdQueue.RenderBurst();
        private void OnDestroy() => TrackCrowdQueue.DisposeBurst();
    }
}
