using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace PuzzleGlass
{
    /// <summary>
    /// Halkada gerçek PhysX fiziği: toplar dinamik Rigidbody'dir. Çarpışma, birbirini itme, sürtünmeyle yuvarlanma ve
    /// devrilme, momentum, sıkışıp açılma ve uyku tamamen fizik motorundadır. Bu sınıf yalnızca:
    ///  - Yol boyunca hafif bir sürüş kuvveti verir (hedef hıza kadar). Önü tıkalı ve yavaş topa kuvvet vermez; böylece
    ///    dengeye gelen top durup uyuyabilir, önü açılınca ya da bir top çarpınca yeniden hareket eder.
    ///  - Giriş yolu kapısını (kapasite doluyken) gerçek bir collider olarak açıp kapar.
    ///  - Toplama/sayım için topların cam yerel konumunu ve halkada olup olmadığını günceller.
    /// Yön/teğet ve kapasite mantığı granül moddakiyle aynıdır.
    /// </summary>
    public static partial class TrackCrowdQueue
    {
        /// <summary>true: halkadaki toplar gerçek Rigidbody fiziğiyle hareket eder (önerilen).</summary>
        public static bool UseRigidbodyPhysics = true;
        /// <summary>Hedef hıza yaklaşma kazancı (1/sn): sürüş kuvveti = kazanç × hız farkı.</summary>
        public static float PhysDriveGain = 4.0f;
        /// <summary>En büyük sürüş ivmesi (ağırlık hissi: toplar aniden hızlanmaz).</summary>
        public static float PhysMaxDriveAccel = 3.2f;
        /// <summary>Kanal kenarına fazla yığılan topa çok hafif içe ivme (yalnızca kenar bandında).</summary>
        public static float PhysSpreadAccel = 1.6f;

        /// <summary>Tırmanışı durdurmadan toplara ağırlık ve aşağı çökelme veren yerçekimi ivmesi.</summary>
        public static float PhysDownGravity = 6.5f;

        /// <summary>Yakın komşu topların hareket halinde birbirinden kopmayıp hafifçe çekmesi için kohezyon ivmesi.</summary>
        public static float PhysCohesionAccel = 2.4f;

        /// <summary>Komşu topların hız farkını yumuşatan hizalanma kazancı (birlikte akış).</summary>
        public static float PhysVelAlignGain = 3.4f;

        // Top–top: hafif sürtünme ile yuvarlanma/devrilme, düşük sekme. Cam duvar: kaygan, çok az sekme.
        public static float PhysBallFriction = 0.22f;
        public static float PhysBallBounce = 0.08f;

        /// <summary>true: pist topları Discrete çarpışma + düşük solver iterasyonu kullanır (mobil performans).
        /// PerfProbe "trackperf=0" ile eski ayarlar (Speculative CCD, proje solver değerleri) karşılaştırılabilir.</summary>
        public static bool UseTrackPerfPhysics = true;

        /// <summary>Teşhis: duvar dışına çıkıp içeri geri alınan top sayısı (normalde 0 kalmalı).</summary>
        public static int EscapeRecoveries { get; private set; }

        // Profiler işaretleri (geliştirme build'lerinde ve editörde görünür; release build'de maliyetsiz)
        private static readonly ProfilerMarker s_MRead = new ProfilerMarker("TrackRB.1_ReadBalls");
        private static readonly ProfilerMarker s_MGrid = new ProfilerMarker("TrackRB.2_BuildGrid");
        private static readonly ProfilerMarker s_MForces = new ProfilerMarker("TrackRB.3_Forces");
        private static readonly ProfilerMarker s_MNeighbors = new ProfilerMarker("TrackRB.3a_Neighbors");
        private static readonly ProfilerMarker s_MApply = new ProfilerMarker("TrackRB.3b_ApplyRigidbody");
        private static readonly ProfilerMarker s_MSpawn = new ProfilerMarker("TrackRB.4_Spawn");

        private static PhysicsMaterial s_TrackBallMat;
        private static GameObject s_GateObj;
        private static BoxCollider s_GateCol;
        private static GlassTrackLayout.CompiledTrack s_GateTrack;

        /// <summary>Pist topunu dinamik Rigidbody olarak hazırlar (BallPhysicsController.JoinTrackQueue çağırır).</summary>
        public static void ConfigureDynamicTrackBall(BallPhysicsController b)
        {
            if (b == null || b.rb == null) return;
            var rb = b.rb;
            rb.isKinematic = false;
            rb.useGravity = false;                       // üstten/eğimli pist: ağırlık StepRigidbody içinde yönlü uygulanır
            rb.constraints = RigidbodyConstraints.FreezePositionZ;
            rb.mass = Mathf.Max(b.defaultMass, 0.75f);   // ağırlık hissi: çarpışmalarda hafif toplar gibi savrulmaz
            rb.linearDamping = 0.78f;                    // kontrollü direnç: birbirinden bağımsız uçmayı önler
            rb.angularDamping = 0.55f;
            rb.maxAngularVelocity = 35f;
            rb.maxDepenetrationVelocity = 0.90f;         // sıkışmada patlama/teleport olmasın
            rb.sleepThreshold = 0.004f;
            // Performans (telefon profili: tek fizik adımı ~9,6 ms): pist topları yavaş (bant hızı) ve kanal duvarları
            // kalın olduğu için sürekli çarpışma algılaması (CCD) gerekmez; kaçan top olursa ReadBalls'taki güvenlik
            // ağı (EscapeRecoveries) içeri alır. Solver 12/4 → 6/2: yüzlerce topluk yoğun kalabalıkta PhysX'i ~%14 hızlandırır.
            rb.collisionDetectionMode = UseTrackPerfPhysics ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = UseTrackPerfPhysics ? 6 : Physics.defaultSolverIterations;
            rb.solverVelocityIterations = UseTrackPerfPhysics ? 2 : Physics.defaultSolverVelocityIterations;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (b.sphereCollider != null)
            {
                b.sphereCollider.isTrigger = false;
                b.sphereCollider.enabled = true;
                b.sphereCollider.sharedMaterial = GetTrackBallMaterial();
            }
        }

        private static PhysicsMaterial GetTrackBallMaterial()
        {
            if (s_TrackBallMat == null)
            {
                s_TrackBallMat = new PhysicsMaterial("Mat_TrackBall")
                {
                    dynamicFriction = PhysBallFriction,
                    staticFriction = PhysBallFriction * 1.2f,
                    bounciness = PhysBallBounce,
                    frictionCombine = PhysicsMaterialCombine.Average, // cam duvar "Minimum" ile kaygan kalır
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            return s_TrackBallMat;
        }

        private static bool StepRigidbody(ShapedGlassContainer container, List<BallPhysicsController> activeBalls, float dt,
            BeltLoop loop, GlassTrackLayout.CompiledTrack track, Transform ct, float speedMul = 1f, bool holdBelt = false)
        {
            IsMoving = false;
            float beltSpeed = holdBelt ? 0f : BeltSpeed * speedMul;
            float maxSpeed = holdBelt ? 0f : MaxSpeed * speedMul;
            float driveGain = PhysDriveGain * speedMul;
            float maxDriveAccel = PhysMaxDriveAccel * speedMul * speedMul;
            float spreadAccel = PhysSpreadAccel * speedMul * speedMul;
            EnsureCenterLookup(loop);
            EnsureCapacity(activeBalls.Count + 8);
            if (s_GTang.Length < s_All.Length)
            {
                s_GTang = new Vector2[s_All.Length];
                s_GOnRing = new bool[s_All.Length];
                s_GOffset = new float[s_All.Length];
            }
            if (s_LayerOutOff.Length < s_All.Length)
            {
                s_LayerOutOff = new float[s_All.Length];
                s_LayerOutDir = new Vector2[s_All.Length];
            }

            float d = Mathf.Max(0.04f, track.ballDiameter);
            float r = d * 0.5f;
            float ringBand = track.innerHalfWidth + r * 0.6f;
            Rect fr = track.fieldRect;
            int cols = Mathf.Max(1, Mathf.CeilToInt(fr.width / d));
            int rows = Mathf.Max(1, Mathf.CeilToInt(fr.height / d));
            if (s_CellHead.Length < cols * rows) s_CellHead = new int[cols * rows];

            // Cam dönüşümü adım boyunca sabit: top başına Transform çağrısı (native) yerine bir kez alınır.
            // TransformDirection/InverseTransformDirection ölçekten etkilenmez → yalnızca dönüş kullanılır.
            Matrix4x4 w2l = ct.worldToLocalMatrix;
            Quaternion ctRot = ct.rotation;
            Quaternion ctInvRot = Quaternion.Inverse(ctRot);

            // 1. Konumları ve hızları oku (fizik motorunun sonucu)
            s_MRead.Begin();
            int n = 0, ringCount = 0;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || !b.trackQueueDriven || b.state != BallState.InContainer || b.rb == null || !b.gameObject.activeInHierarchy)
                    continue;
                if (b.rb.isKinematic) ConfigureDynamicTrackBall(b);

                Vector3 lp = w2l.MultiplyPoint3x4(b.rb.position);
                Vector2 p = new Vector2(lp.x, lp.y);

                // Güvenlik ağı: merkezi kanal dışına çıkmış top (ör. collider'a gömülü doğup dışa itilmiş) en yakın iç
                // noktaya alınır. Normal oyunda tetiklenmez; EscapeRecoveries ile izlenir.
                track.SampleField(p, out float sdfNow, out _, out _, out Vector2 inwardNow);
                if (sdfNow > 0f)
                {
                    Vector2 inside = p + inwardNow * (sdfNow + r * 1.1f);
                    b.rb.position = ct.TransformPoint(new Vector3(inside.x, inside.y, lp.z));
                    b.rb.linearVelocity = Vector3.zero;
                    p = inside;
                    EscapeRecoveries++;
                }
                b.trackQueueLocalPos = p;
                b.trackQueueLocalZ = lp.z;
                b.trackQueuePosValid = true;

                int ci = NearestCenterIndex(loop, p);
                bool onRing = (p - loop.centerPts[ci]).sqrMagnitude < ringBand * ringBand;
                Vector2 tang;
                float offset = 0f;
                if (onRing)
                {
                    tang = loop.centerTang[ci];
                    offset = Vector2.Dot(p - loop.centerPts[ci], new Vector2(-tang.y, tang.x));
                    // Katman kilidi için: adadan dışa doğru enine konum (ComputeRingLayer ile aynı yön)
                    s_LayerOutDir[n] = loop.centerNorm[ci];
                    s_LayerOutOff[n] = Vector2.Dot(p - loop.centerPts[ci], loop.centerNorm[ci]);

                    // Katman kilidi sert duvarı: kilitli katmandaki top dış şeride geçemez (taşarsa enine geri alınır)
                    if (s_LayerLock && b.trackLayer >= 0 &&
                        ClampToLayerWall(b, ref p, s_LayerOutOff[n], s_LayerOutDir[n], track, d, ct, lp.z))
                    {
                        b.trackQueueLocalPos = p;
                        s_LayerOutOff[n] = Vector2.Dot(p - loop.centerPts[ci], loop.centerNorm[ci]);
                        offset = Vector2.Dot(p - loop.centerPts[ci], new Vector2(-tang.y, tang.x));
                    }
                }
                else
                {
                    track.SampleField(p, out _, out Vector2 flow, out _, out _);
                    tang = flow;
                }
                b.trackLoopSlot = onRing ? RingMarker : -1;
                if (onRing) ringCount++;

                Vector3 lv = ctInvRot * b.rb.linearVelocity;
                s_All[n] = b;
                s_Pos[n] = p;
                s_Vel[n] = new Vector2(lv.x, lv.y);
                s_GTang[n] = tang;
                s_GOnRing[n] = onRing;
                s_GOffset[n] = offset;
                n++;
            }
            s_MRead.End();
            s_RingCount = ringCount;
            s_Occupied = ringCount;
            bool canJoin = CanJoinBelt(ringCount);
            UpdateRoadGate(container, loop, track, ct, !canJoin);

            // 2. Sürüş kuvveti: yol boyunca hedef hıza (ağırlıklı, sınırlı ivme). Önü tıkalı ve yavaşsa kuvvet yok:
            //    top fiziksel dengeye gelip durabilir/uyuyabilir; önü açılınca kuvvet geri gelir ve uyanır.
            s_MGrid.Begin();
            BuildGranularGrid(n, fr, d, cols, rows);
            s_MGrid.End();
            float aheadMax = d * 1.15f, aheadLat = d * 0.7f;
            bool anyMoving = false;
            s_MForces.Begin();
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                Vector2 p = s_Pos[i];
                Vector2 t = s_GTang[i];
                Vector2 nrm = new Vector2(-t.y, t.x);
                Vector2 v = s_Vel[i];
                float along = Vector2.Dot(v, t);

                bool blocked = false;
                Vector2 cohSum = Vector2.zero;
                int cohCount = 0;
                Vector2 velDiffSum = Vector2.zero;
                int velCount = 0;
                float cohMin = d * 1.03f;
                float cohMax = d * 2.00f;
                float alignMaxSq = (d * 1.60f) * (d * 1.60f);

                s_MNeighbors.Begin();
                int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - fr.xMin) / d), 0, cols - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - fr.yMin) / d), 0, rows - 1);
                for (int gy = Mathf.Max(0, cy - 1); gy <= Mathf.Min(rows - 1, cy + 1); gy++)
                {
                    for (int gx = Mathf.Max(0, cx - 1); gx <= Mathf.Min(cols - 1, cx + 1); gx++)
                    {
                        int j = s_CellHead[gy * cols + gx];
                        while (j != -1)
                        {
                            if (j != i)
                            {
                                Vector2 diff = s_Pos[j] - p;
                                float dSq = diff.sqrMagnitude;
                                if (!blocked)
                                {
                                    float al = Vector2.Dot(diff, t);
                                    if (al > 0f && al < aheadMax && Mathf.Abs(Vector2.Dot(diff, nrm)) < aheadLat)
                                    {
                                        // Öndeki top da yavaşsa gerçekten tıkalı (sıkışık kuyruk)
                                        if (Vector2.Dot(s_Vel[j], t) < beltSpeed * 0.25f) blocked = true;
                                    }
                                }

                                if (s_GOnRing[i] && dSq > cohMin * cohMin && dSq < cohMax * cohMax)
                                {
                                    float dist = Mathf.Sqrt(dSq);
                                    float pullFactor = Mathf.Clamp01((dist - cohMin) / Mathf.Max(0.01f, d));
                                    // Öndeki komşuya doğru hafifçe daha güçlü çekim: inişlerde kuyruk dağılıp boşluk bırakmaz
                                    float aheadWeight = Vector2.Dot(diff, t) > 0f ? 1.35f : 0.75f;
                                    cohSum += (diff / dist) * (pullFactor * aheadWeight);
                                    cohCount++;
                                }

                                if (s_GOnRing[i] && dSq < alignMaxSq)
                                {
                                    velDiffSum += (s_Vel[j] - v);
                                    velCount++;
                                }
                            }
                            j = s_Next[j];
                        }
                    }
                }

                s_MNeighbors.End();

                Vector2 accel = Vector2.zero;
                // Dinlenme yalnızca giriş yolunda sırasını bekleyen toplar için: halka doluyken her topun önünde top
                // vardır; halkadakiler de dursaydı bant kendini kilitleyip dönmeyi bırakırdı.
                bool resting = !s_GOnRing[i] && blocked && v.sqrMagnitude < 0.03f * 0.03f;
                if (!resting)
                {
                    float target = s_GOnRing[i] ? beltSpeed : maxSpeed * 0.6f;
                    float a = Mathf.Clamp((target - along) * driveGain, -maxDriveAccel, maxDriveAccel);
                    if (blocked && a > 0f) a *= 0.35f; // tıkalıyken kalabalığı ezmeden hafifçe it
                    accel = t * a;

                    // Viraj/kalabalık dış kenara yığılırsa kenar bandında çok hafif içe ivme (kanal genişliğini doldursun)
                    if (s_GOnRing[i])
                    {
                        float band = Mathf.Max(0f, track.innerHalfWidth - r) * 0.55f;
                        float excess = Mathf.Abs(s_GOffset[i]) - band;
                        if (excess > 0f) accel -= nrm * (Mathf.Sign(s_GOffset[i]) * spreadAccel * Mathf.Clamp01(excess / Mathf.Max(0.01f, r)));
                    }
                }

                if (s_GOnRing[i])
                {
                    // Ağırlık hissi: tırmanışa karşı koymadan enine (-Y) aşağı bastırır;
                    // ekstra -Y çökelme sadece alt manifoldda (t.y <= 0.05f) uygulanır ki üst inişte toplar birbirinden kopmasın.
                    float downG = PhysDownGravity * speedMul;
                    float latGrav = Vector2.Dot(new Vector2(0f, -downG), nrm);
                    Vector2 grav = nrm * latGrav;
                    if (Mathf.Abs(p.y - track.exitPoint.y) <= track.innerHalfWidth * 2.2f && t.y <= 0.05f)
                    {
                        grav.y -= downG * 0.42f;
                    }
                    // Katman kilidi: kilitli topu duvara bastıran (adadan dışa) yerçekimi bileşeni silinir; kanal boyunca ve
                    // içe doğru ağırlık kalır. Yoksa içteki toplar ağırlıklarıyla ortadakileri sürekli duvara yaslıyordu.
                    if (s_LayerLock && b.trackLayer >= 0 && b.trackLayer > OpenLayer)
                    {
                        float gOut = Vector2.Dot(grav, s_LayerOutDir[i]);
                        if (gOut > 0f) grav -= s_LayerOutDir[i] * gOut;
                    }
                    accel += grav;

                    // Yakın komşular hareket halindeyken hafifçe birbirini çekip hızını hizalar
                    if (cohCount > 0) accel += (cohSum / cohCount) * (PhysCohesionAccel * speedMul);
                    if (velCount > 0) accel += (velDiffSum / velCount) * (PhysVelAlignGain * speedMul);

                    // Katman kilidi: top kendi katman bandının (dış/orta/iç) içinde serbest; banttan taşınca yay geri iter.
                    // Yoksa enine yerçekimi ve açılan boşluklar iç topları dışa (ya da tersi) taşıyıp katmanları karıştırıyordu.
                    // Açık katmanın topları serbest: dış katmanlar boşaldığı için doğal olarak dışa/çıkışa yerleşirler.
                    if (s_LayerLock && b.trackLayer >= 0 && b.trackLayer > OpenLayer)
                        accel += LayerBandAccel(b.trackLayer, s_LayerOutOff[i], s_LayerOutDir[i], v, track, d);
                }

                s_MApply.Begin();
                if (accel.sqrMagnitude > 1e-6f)
                    b.rb.AddForce(ctRot * new Vector3(accel.x, accel.y, 0f), ForceMode.Acceleration);

                // Smooth rolling spin along the conveyor belt velocity so marbles visibly tumble around bends
                if (v.sqrMagnitude > 0.012f)
                {
                    Vector3 worldAxis = (ctRot * new Vector3(-v.y, v.x, v.x * 0.28f)).normalized;
                    float spinRate = Mathf.Clamp(v.magnitude / Mathf.Max(0.03f, r), 0f, 24f) * 0.78f;
                    b.rb.angularVelocity = Vector3.Lerp(b.rb.angularVelocity, worldAxis * spinRate, Mathf.Clamp01(dt * 10f));
                }

                s_MApply.End();

                if (!s_GOnRing[i] && v.sqrMagnitude > 0.05f * 0.05f) anyMoving = true;
            }
            s_MForces.End();
            RoadQueueCount = n - ringCount;
            IsMoving = anyMoving; // kilitlenme kontrolü: yalnızca halkaya girmeyi bekleyen yoldaki hareket sayılır

            // 3. Gizli kuyruk: yolun tepesi boşsa sıradaki top belirir; yol yoksa halka girişinde (kapasite varsa)
            using var spawnScope = s_MSpawn.Auto();
            if (!holdBelt && s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
            {
                Vector2 spawn;
                bool allowed;
                if (loop.hasRoad) { spawn = loop.roadTop; allowed = true; }
                else
                {
                    int ei = Mathf.Clamp(Mathf.RoundToInt(loop.entryU * loop.centerPts.Length), 0, loop.centerPts.Length - 1);
                    spawn = loop.centerPts[ei];
                    allowed = canJoin;
                }
                if (allowed)
                {
                    float clearSq = (d * 1.05f) * (d * 1.05f);
                    bool free = true;
                    for (int i = 0; i < n && free; i++) if ((s_Pos[i] - spawn).sqrMagnitude < clearSq) free = false;
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

        /// <summary>Giriş yolu kapısı: halka kapasitesi doluyken yolun halkaya açıldığı yerde gerçek bir collider.</summary>
        private static void UpdateRoadGate(ShapedGlassContainer container, BeltLoop loop, GlassTrackLayout.CompiledTrack track, Transform ct, bool closed)
        {
            if (!loop.hasRoad)
            {
                if (s_GateObj != null) s_GateObj.SetActive(false);
                return;
            }
            if (s_GateObj == null || s_GateTrack != track || s_GateObj.transform.parent != ct)
            {
                if (s_GateObj == null)
                {
                    s_GateObj = new GameObject("TrackRoadGate");
                    s_GateCol = s_GateObj.AddComponent<BoxCollider>();
                }
                s_GateTrack = track;
                s_GateObj.transform.SetParent(ct, false);
                float d = track.ballDiameter;
                Vector2 c = loop.roadJunction + loop.roadDir * (loop.roadGate + d * 0.2f);
                s_GateObj.transform.localPosition = new Vector3(c.x, c.y, -0.10f);
                float ang = Mathf.Atan2(loop.roadDir.y, loop.roadDir.x) * Mathf.Rad2Deg - 90f;
                s_GateObj.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
                s_GateCol.size = new Vector3(loop.roadHalfW * 2.4f, d * 0.3f, 0.65f);
            }
            if (s_GateObj.activeSelf != closed) s_GateObj.SetActive(closed);
        }
    }
}
