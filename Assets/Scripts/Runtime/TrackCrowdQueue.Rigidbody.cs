using System;
using System.Collections.Generic;
using UnityEngine;

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

        // Top–top: hafif sürtünme ile yuvarlanma/devrilme, düşük sekme. Cam duvar: kaygan, çok az sekme.
        public static float PhysBallFriction = 0.22f;
        public static float PhysBallBounce = 0.08f;

        /// <summary>Teşhis: duvar dışına çıkıp içeri geri alınan top sayısı (normalde 0 kalmalı).</summary>
        public static int EscapeRecoveries { get; private set; }

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
            rb.useGravity = false;                       // üstten bakış: "aşağı" yol boyunca sürüş kuvvetidir
            rb.constraints = RigidbodyConstraints.FreezePositionZ;
            rb.linearDamping = 0.45f;                    // hafif hava/zemin direnci: momentum korunur ama sonsuz kaymaz
            rb.angularDamping = 0.35f;
            rb.maxAngularVelocity = 35f;
            rb.maxDepenetrationVelocity = 1.2f;          // sıkışmada patlama/teleport olmasın
            rb.sleepThreshold = 0.004f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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
            BeltLoop loop, GlassTrackLayout.CompiledTrack track, Transform ct)
        {
            IsMoving = false;
            EnsureCenterLookup(loop);
            EnsureCapacity(activeBalls.Count + 8);
            if (s_GTang.Length < s_All.Length)
            {
                s_GTang = new Vector2[s_All.Length];
                s_GOnRing = new bool[s_All.Length];
                s_GOffset = new float[s_All.Length];
            }

            float d = Mathf.Max(0.04f, track.ballDiameter);
            float r = d * 0.5f;
            float ringBand = track.innerHalfWidth + r * 0.6f;
            Rect fr = track.fieldRect;
            int cols = Mathf.Max(1, Mathf.CeilToInt(fr.width / d));
            int rows = Mathf.Max(1, Mathf.CeilToInt(fr.height / d));
            if (s_CellHead.Length < cols * rows) s_CellHead = new int[cols * rows];

            // 1. Konumları ve hızları oku (fizik motorunun sonucu)
            int n = 0, ringCount = 0;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || !b.trackQueueDriven || b.state != BallState.InContainer || b.rb == null || !b.gameObject.activeInHierarchy)
                    continue;
                if (b.rb.isKinematic) ConfigureDynamicTrackBall(b);

                Vector3 lp = ct.InverseTransformPoint(b.rb.position);
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
                }
                else
                {
                    track.SampleField(p, out _, out Vector2 flow, out _, out _);
                    tang = flow;
                }
                b.trackLoopSlot = onRing ? RingMarker : -1;
                if (onRing) ringCount++;

                Vector3 lv = ct.InverseTransformDirection(b.rb.linearVelocity);
                s_All[n] = b;
                s_Pos[n] = p;
                s_Vel[n] = new Vector2(lv.x, lv.y);
                s_GTang[n] = tang;
                s_GOnRing[n] = onRing;
                s_GOffset[n] = offset;
                n++;
            }
            s_RingCount = ringCount;
            s_Occupied = ringCount;
            bool canJoin = CanJoinBelt(ringCount);
            UpdateRoadGate(container, loop, track, ct, !canJoin);

            // 2. Sürüş kuvveti: yol boyunca hedef hıza (ağırlıklı, sınırlı ivme). Önü tıkalı ve yavaşsa kuvvet yok:
            //    top fiziksel dengeye gelip durabilir/uyuyabilir; önü açılınca kuvvet geri gelir ve uyanır.
            BuildGranularGrid(n, fr, d, cols, rows);
            float aheadMax = d * 1.15f, aheadLat = d * 0.7f;
            bool anyMoving = false;
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                Vector2 p = s_Pos[i];
                Vector2 t = s_GTang[i];
                Vector2 nrm = new Vector2(-t.y, t.x);
                Vector2 v = s_Vel[i];
                float along = Vector2.Dot(v, t);

                bool blocked = false;
                int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - fr.xMin) / d), 0, cols - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - fr.yMin) / d), 0, rows - 1);
                for (int gy = Mathf.Max(0, cy - 1); gy <= Mathf.Min(rows - 1, cy + 1) && !blocked; gy++)
                {
                    for (int gx = Mathf.Max(0, cx - 1); gx <= Mathf.Min(cols - 1, cx + 1) && !blocked; gx++)
                    {
                        int j = s_CellHead[gy * cols + gx];
                        while (j != -1)
                        {
                            if (j != i)
                            {
                                Vector2 diff = s_Pos[j] - p;
                                float al = Vector2.Dot(diff, t);
                                if (al > 0f && al < aheadMax && Mathf.Abs(Vector2.Dot(diff, nrm)) < aheadLat)
                                {
                                    // Öndeki top da yavaşsa gerçekten tıkalı (sıkışık kuyruk)
                                    if (Vector2.Dot(s_Vel[j], t) < BeltSpeed * 0.25f) { blocked = true; break; }
                                }
                            }
                            j = s_Next[j];
                        }
                    }
                }

                Vector2 accel = Vector2.zero;
                // Dinlenme yalnızca giriş yolunda sırasını bekleyen toplar için: halka doluyken her topun önünde top
                // vardır; halkadakiler de dursaydı bant kendini kilitleyip dönmeyi bırakırdı.
                bool resting = !s_GOnRing[i] && blocked && v.sqrMagnitude < 0.03f * 0.03f;
                if (!resting)
                {
                    float target = s_GOnRing[i] ? BeltSpeed : MaxSpeed * 0.6f;
                    float a = Mathf.Clamp((target - along) * PhysDriveGain, -PhysMaxDriveAccel, PhysMaxDriveAccel);
                    if (blocked && a > 0f) a *= 0.35f; // tıkalıyken kalabalığı ezmeden hafifçe it
                    accel = t * a;

                    // Viraj/kalabalık dış kenara yığılırsa kenar bandında çok hafif içe ivme (kanal genişliğini doldursun)
                    if (s_GOnRing[i])
                    {
                        float band = Mathf.Max(0f, track.innerHalfWidth - r) * 0.55f;
                        float excess = Mathf.Abs(s_GOffset[i]) - band;
                        if (excess > 0f) accel -= nrm * (Mathf.Sign(s_GOffset[i]) * PhysSpreadAccel * Mathf.Clamp01(excess / Mathf.Max(0.01f, r)));
                    }
                }

                if (accel.sqrMagnitude > 1e-6f)
                    b.rb.AddForce(ct.TransformDirection(new Vector3(accel.x, accel.y, 0f)), ForceMode.Acceleration);

                if (!s_GOnRing[i] && v.sqrMagnitude > 0.05f * 0.05f) anyMoving = true;
            }
            RoadQueueCount = n - ringCount;
            IsMoving = anyMoving; // kilitlenme kontrolü: yalnızca halkaya girmeyi bekleyen yoldaki hareket sayılır

            // 3. Gizli kuyruk: yolun tepesi boşsa sıradaki top belirir; yol yoksa halka girişinde (kapasite varsa)
            if (s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
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
                        s_NextRoadSpawn = Time.time + 0.06f;
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
