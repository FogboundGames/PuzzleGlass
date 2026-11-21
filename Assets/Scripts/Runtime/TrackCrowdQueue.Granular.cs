using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Halkada serbest parçacık (granül) akışı — referans videodaki gibi: toplar sabit yuvalara/şeritlere bağlı değil;
    /// kum gibi gevşek bir kütle halinde yol boyunca sürüklenir, birbirini iter, döner, grup sınırlarında hafifçe
    /// karışır. Önü boş olan top biraz daha hızlı gider (boşluklar kendiliğinden kapanır).
    ///
    /// Oyun kuralları aynen korunur:
    ///  - Toplama: çıkışın önünden fiziksel olarak geçen toplar (IsAtExitGranular).
    ///  - Giriş yolu (gizli kuyruk): halkadaki top sayısı kapasite sınırının altına düşünce yoldaki toplar halkaya girer.
    ///  - Halkadaki toplar trackLoopSlot = RingMarker (0) ile işaretlenir; yoldakiler -1.
    /// </summary>
    public static partial class TrackCrowdQueue
    {
        /// <summary>true: granül akış (referans video). false: eski yuvalı/şeritli bant.</summary>
        public static bool UseGranularFlow = true;
        /// <summary>Topun hedef hıza yaklaşma hızı (1/sn).</summary>
        public static float GranularDriveGain = 5.5f;
        /// <summary>Önü boş olan top bu oranda daha hızlı hedefler (boşluk kapatma).</summary>
        public static float GranularCatchUp = 0.45f;
        /// <summary>Yol enine hız sönümü (1/sn): toplar yanlara savrulup kalmasın.</summary>
        public static float GranularLateralDamping = 12.0f;
        /// <summary>Kanal kenarına fazla yaklaşan topu kanal içine geri çeken yumuşak kuvvet (merkezkaç savrulmasını önler).</summary>
        public static float GranularSpreadStiffness = 45.0f;
        /// <summary>Top-top çarpışmasında sekme (granül: düşük).</summary>
        public static float GranularRestitution = 0.12f;
        /// <summary>Top-top teğetsel sürtünme (0..1).</summary>
        public static float GranularFriction = 0.10f;
        /// <summary>İtişme/canlılık ivmesi (top çapı başına).</summary>
        public static float GranularJitter = 9f;
        /// <summary>Bu mesafenin altında (çap oranı) toplar birbirini iter.</summary>
        public static float GranularContactRatio = 0.97f;
        private const int GranularIterations = 4;
        /// <summary>Granül modda "halkada" işareti (trackLoopSlot değeri).</summary>
        public const int RingMarker = 0;

        private static int s_RingCount;
        private static Vector2[] s_GTang = new Vector2[256];
        private static bool[] s_GOnRing = new bool[256];
        private static float[] s_GOffset = new float[256];

        /// <summary>Level başında top yerleşimine küçük, belirlenimli bir dağınıklık (ızgara görünümü olmasın).</summary>
        private static Vector2 GranularSpawnJitter(int lane, int row, float d)
        {
            uint h = (uint)(lane * 73856093) ^ (uint)(row * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            float a = (h & 0xffff) / 65535f * Mathf.PI * 2f;
            float m = ((h >> 16) & 0xffff) / 65535f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (m * d * 0.06f);
        }

        private static bool IsAtExitGranular(BeltLoop loop, BallPhysicsController ball)
        {
            if (loop == null || ball == null || ball.trackLoopSlot < 0 || !ball.trackQueuePosValid) return false;
            var track = loop.source;
            float d = track.ballDiameter;
            Vector2 rel = ball.trackQueueLocalPos - track.exitPoint;
            float exitHalfX = Mathf.Max(d * 1.6f, 0.30f);
            if (Mathf.Abs(rel.x) > exitHalfX || Mathf.Abs(rel.y) > track.innerHalfWidth + d * 0.6f) return false;
            // Ağız aşağıda: topla ağız arasında (altında) onu kapatan başka bir top varsa çıkamaz.
            // Aksi halde üst sıradaki top, alttaki farklı renkli topun üstünden atlayıp çıkıyordu.
            return !IsBlockedTowardExit(ball, d);
        }

        private static bool IsBlockedTowardExit(BallPhysicsController ball, float d)
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return false;
            var balls = pool.ActiveBalls;
            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            Transform ct = container != null ? container.transform : null;
            Vector2 p = ball.trackQueueLocalPos;
            float laneHalf = d * 0.92f;   // alttan iki top arasına yaslanan üst top da engelli sayılır
            float minDrop = d * 0.28f;    // en az bu kadar aşağıdaysa "altında" sayılır
            for (int i = 0; i < balls.Count; i++)
            {
                var o = balls[i];
                if (o == null || o == ball) continue;
                Vector2 q;
                if (o.state == BallState.InContainer)
                {
                    if (!o.trackQueuePosValid || o.trackLoopSlot < 0) continue;
                    // Katman kilidi: kilitli katmandaki top açık katmanın önünü kesmez (kavramsal olarak arkadadır);
                    // yoksa kanalın ortasına kaymış son dış toplar altlarındaki kilitli toplar yüzünden çıkamıyordu
                    if (!IsLayerOpen(o)) continue;
                    q = o.trackQueueLocalPos;
                }
                else if (o.state == BallState.AttractingToExit && ct != null)
                {
                    // Çıkmaya henüz başlamış alt top kapıdan ayrılana kadar üstündeki topu tut (sıra/diziliş bozulmasın)
                    Vector3 lp = ct.InverseTransformPoint(o.transform.position);
                    q = new Vector2(lp.x, lp.y);
                }
                else
                {
                    continue;
                }
                float drop = p.y - q.y;
                if (drop >= minDrop && drop <= d * 2.5f && Mathf.Abs(q.x - p.x) < laneHalf) return true;
            }
            return false;
        }

        /// <summary>Halka orta çizgisinin en yakın noktası için hücre tablosu (level başına bir kez).</summary>
        private static void EnsureCenterLookup(BeltLoop loop)
        {
            if (loop.nearLookup != null) return;
            var track = loop.source;
            float cell = Mathf.Max(0.02f, track.ballDiameter * 0.5f);
            Rect fr = track.fieldRect;
            int cols = Mathf.Max(1, Mathf.CeilToInt(fr.width / cell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(fr.height / cell));
            var pts = loop.centerPts;
            var lut = new int[cols * rows];
            int hint = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    Vector2 c = new Vector2(fr.xMin + (x + 0.5f) * cell, fr.yMin + (y + 0.5f) * cell);
                    float best = float.MaxValue;
                    int bi = hint;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float dSq = (pts[i] - c).sqrMagnitude;
                        if (dSq < best) { best = dSq; bi = i; }
                    }
                    lut[y * cols + x] = bi;
                    hint = bi;
                }
            }
            loop.nearLookup = lut;
            loop.nearRect = fr;
            loop.nearCell = cell;
            loop.nearCols = cols;
            loop.nearRows = rows;
        }

        private static int NearestCenterIndex(BeltLoop loop, Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - loop.nearRect.xMin) / loop.nearCell), 0, loop.nearCols - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y - loop.nearRect.yMin) / loop.nearCell), 0, loop.nearRows - 1);
            int idx = loop.nearLookup[y * loop.nearCols + x];
            // Komşu noktalarla incelt (hücre içi hassasiyet)
            var pts = loop.centerPts;
            int n = pts.Length;
            float best = (pts[idx] - p).sqrMagnitude;
            for (int k = -4; k <= 4; k++)
            {
                if (k == 0) continue;
                int j = ((idx + k) % n + n) % n;
                float dSq = (pts[j] - p).sqrMagnitude;
                if (dSq < best) { best = dSq; idx = j; }
            }
            return idx;
        }

        private static bool StepGranular(ShapedGlassContainer container, List<BallPhysicsController> activeBalls, float dt,
            BeltLoop loop, GlassTrackLayout.CompiledTrack track, Transform ct, float speedMul = 1f)
        {
            IsMoving = false;
            float beltSpeed = BeltSpeed * speedMul;
            float maxSpeed = MaxSpeed * speedMul;
            float maxBallSpeed = MaxBallSpeed * speedMul;
            float driveGain = GranularDriveGain * speedMul;
            float spreadStiff = GranularSpreadStiffness * speedMul * speedMul;
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
            float now = Application.isPlaying ? Time.time : 0f;

            // 1. Topla: konum, hız, yol teğeti; halkada mı yolda mı
            int n = 0, ringCount = 0;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || !b.trackQueueDriven || b.state != BallState.InContainer || b.rb == null || !b.gameObject.activeInHierarchy)
                    continue;
                if (!b.trackQueuePosValid)
                {
                    Vector3 lp = ct.InverseTransformPoint(b.rb.position);
                    b.trackQueueLocalPos = new Vector2(lp.x, lp.y);
                    b.trackQueueLocalZ = lp.z;
                    b.trackQueuePosValid = true;
                }

                Vector2 p = b.trackQueueLocalPos;
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

                s_All[n] = b;
                s_Pos[n] = p;
                s_Prev[n] = p;
                s_Vel[n] = b.trackVel;
                s_GTang[n] = tang;
                s_GOnRing[n] = onRing;
                s_GOffset[n] = offset;
                n++;
            }
            s_RingCount = ringCount;
            s_Occupied = ringCount;
            bool canJoin = CanJoinBelt(ringCount);

            // 2. Sürüş: yol boyunca hedef hıza; önü boşsa biraz daha hızlı (boşluğa yetişir). Enine sönüm + itişme.
            BuildGranularGrid(n, fr, d, cols, rows);
            float aheadMin = d * 0.2f, aheadMax = d * 1.55f, aheadLat = d * 0.75f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = s_Pos[i];
                Vector2 t = s_GTang[i];
                Vector2 nrm = new Vector2(-t.y, t.x);

                bool blocked = false;
                int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - fr.xMin) / d), 0, cols - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - fr.yMin) / d), 0, rows - 1);
                for (int gy = Mathf.Max(0, cy - 2); gy <= Mathf.Min(rows - 1, cy + 2) && !blocked; gy++)
                {
                    for (int gx = Mathf.Max(0, cx - 2); gx <= Mathf.Min(cols - 1, cx + 2) && !blocked; gx++)
                    {
                        int j = s_CellHead[gy * cols + gx];
                        while (j != -1)
                        {
                            if (j != i)
                            {
                                Vector2 diff = s_Pos[j] - p;
                                float al = Vector2.Dot(diff, t);
                                if (al > aheadMin && al < aheadMax && Mathf.Abs(Vector2.Dot(diff, nrm)) < aheadLat) { blocked = true; break; }
                            }
                            j = s_Next[j];
                        }
                    }
                }

                float target = s_GOnRing[i] ? beltSpeed : maxSpeed * 0.6f;
                if (!blocked) target *= 1f + GranularCatchUp;

                Vector2 v = s_Vel[i];
                float along = Vector2.Dot(v, t);
                v += t * ((target - along) * driveGain * dt);
                float lat = Vector2.Dot(v, nrm);
                v -= nrm * (lat * Mathf.Min(1f, GranularLateralDamping * dt));

                // Kenara fazla yaklaşan topu kanal içine geri çek (orta bölgede serbest)
                if (s_GOnRing[i])
                {
                    // Viraj + arkadan itme kalabalığı dışa kaydırır; ölü bölgenin dışındaki topa kanal ortasına doğru ivme
                    float freeHalf = Mathf.Max(0f, track.innerHalfWidth - r) * 0.30f;
                    float off = s_GOffset[i];
                    float excess = Mathf.Abs(off) - freeHalf;
                    if (excess > 0f) v -= nrm * (Mathf.Sign(off) * excess * spreadStiff * dt);
                }

                // İtişme: her topa kendi fazında yavaş değişen küçük bir ivme (granül kalabalığın canlılığı)
                var b = s_All[i];
                float ph = (b.GetInstanceID() & 1023) * 0.0613f;
                Vector2 jit = new Vector2(Mathf.Sin(now * 6.1f + ph) + 0.5f * Mathf.Sin(now * 11.3f + ph * 2.1f),
                                          Mathf.Cos(now * 5.3f + ph * 1.7f) + 0.5f * Mathf.Cos(now * 9.7f + ph));
                v += jit * (GranularJitter * d * dt);

                if (v.sqrMagnitude > maxBallSpeed * maxBallSpeed) v = v.normalized * maxBallSpeed;
                s_Vel[i] = v;
                s_Pos[i] = p + v * dt;
                s_Corr[i] = Vector2.zero;
            }

            // 3. Çarpışmalar (konum tabanlı, karşılıklı), duvarlar, giriş yolu kapısı
            float contact = d * GranularContactRatio;
            float contactSq = contact * contact;
            float wallClear = r * 1.02f;
            for (int it = 0; it < GranularIterations; it++)
            {
                BuildGranularGrid(n, fr, d, cols, rows);
                for (int i = 0; i < n; i++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].x - fr.xMin) / d), 0, cols - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].y - fr.yMin) / d), 0, rows - 1);
                    for (int gy = Mathf.Max(0, cy - 1); gy <= Mathf.Min(rows - 1, cy + 1); gy++)
                    {
                        for (int gx = Mathf.Max(0, cx - 1); gx <= Mathf.Min(cols - 1, cx + 1); gx++)
                        {
                            int j = s_CellHead[gy * cols + gx];
                            while (j != -1)
                            {
                                if (j > i)
                                {
                                    Vector2 diff = s_Pos[i] - s_Pos[j];
                                    float dSq = diff.sqrMagnitude;
                                    if (dSq < contactSq)
                                    {
                                        float dist = Mathf.Sqrt(dSq);
                                        Vector2 nn = dist > 1e-5f ? diff / dist : new Vector2(0f, 1f);
                                        Vector2 push = nn * ((contact - dist) * 0.5f * 0.85f);
                                        s_Pos[i] += push; s_Corr[i] += push;
                                        s_Pos[j] -= push; s_Corr[j] -= push;

                                        if (it == 0)
                                        {
                                            Vector2 rel = s_Vel[i] - s_Vel[j];
                                            float vn = Vector2.Dot(rel, nn);
                                            if (vn < 0f)
                                            {
                                                float imp = -(1f + GranularRestitution) * vn * 0.5f;
                                                s_Vel[i] += nn * imp;
                                                s_Vel[j] -= nn * imp;
                                                Vector2 vt = rel - nn * vn;
                                                s_Vel[i] -= vt * (GranularFriction * 0.5f);
                                                s_Vel[j] += vt * (GranularFriction * 0.5f);
                                            }
                                        }
                                    }
                                }
                                j = s_Next[j];
                            }
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    track.SampleField(s_Pos[i], out float sdf, out _, out _, out Vector2 inward);
                    float over = sdf + wallClear;
                    if (over > 0f && sdf < d * 2f)
                    {
                        s_Pos[i] += inward * over;
                        s_Corr[i] += inward * over;
                        float vn = Vector2.Dot(s_Vel[i], inward);
                        if (vn < 0f) s_Vel[i] -= (1f + WallRestitution) * vn * inward;
                    }

                    // Halka kapasitesi doluyken yoldaki toplar halkanın şeridine giremez
                    if (!canJoin && !s_GOnRing[i] && loop.hasRoad)
                    {
                        Vector2 rel = s_Pos[i] - loop.roadJunction;
                        float al = Vector2.Dot(rel, loop.roadDir);
                        float lat = Mathf.Abs(rel.x * loop.roadDir.y - rel.y * loop.roadDir.x);
                        if (lat < loop.roadHalfW * 1.6f && al > -loop.roadHalfW && al < loop.roadGate)
                        {
                            s_Pos[i] += loop.roadDir * (loop.roadGate - al);
                            float vIn = Vector2.Dot(s_Vel[i], loop.roadDir);
                            if (vIn < 0f) s_Vel[i] -= loop.roadDir * vIn;
                        }
                    }
                }
            }

            // 4. Uygula: konum, hız, yuvarlanma; sert çarpmada kısa ezilme
            bool anyMoved = false;
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                Vector2 v = s_Vel[i];
                if (v.sqrMagnitude > maxBallSpeed * maxBallSpeed) v = v.normalized * maxBallSpeed;
                b.trackVel = v;
                b.trackQueueSpeed = v.magnitude;

                float hit = s_Corr[i].magnitude / dt;
                if (hit > 0.9f)
                {
                    Vector3 axis = ct.TransformDirection(new Vector3(s_Corr[i].x, s_Corr[i].y, 0f));
                    b.PlayTrackBump(axis, Mathf.Clamp(hit * 0.02f, 0.025f, 0.07f));
                }

                if (ApplyMove(b, ct, s_Prev[i], s_Pos[i], dt, now)) anyMoved = true;
                // Kilitlenme kontrolü için "hareket" sadece halkaya girmeyi bekleyen yoldaki toplar
                if (!s_GOnRing[i] && (s_Pos[i] - s_Prev[i]).sqrMagnitude > MinMoveSq * 4f) IsMoving = true;
            }
            RoadQueueCount = n - ringCount;

            // 5. Gizli kuyruk: yolun tepesi boşsa sıradaki top belirir; yol yoksa halkanın girişinde (kapasite varsa)
            if (s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
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
                    for (int i = 0; i < n && free; i++) if ((s_Pos[i] - spawn).sqrMagnitude < clearSq) free = false;
                    if (free && SpawnQueuedBall(container, spawn) != null)
                    {
                        s_NextRoadSpawn = Time.time + 0.06f / speedMul;
                        IsMoving = true;
                    }
                }
            }

            Array.Clear(s_All, 0, n);
            return anyMoved;
        }

        private static void BuildGranularGrid(int n, Rect fr, float d, int cols, int rows)
        {
            Array.Fill(s_CellHead, -1, 0, cols * rows);
            for (int i = 0; i < n; i++)
            {
                int ix = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].x - fr.xMin) / d), 0, cols - 1);
                int iy = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].y - fr.yMin) / d), 0, rows - 1);
                int cell = iy * cols + ix;
                s_Next[i] = s_CellHead[cell];
                s_CellHead[cell] = i;
            }
        }
    }
}
