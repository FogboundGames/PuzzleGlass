using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Pist (TrackChannel) camının kalabalık sürücüsü. Toplar fizikle çalkalanmaz.
    ///
    /// Döngülü pist (ortada ada boşluğu olan, iki kol + köprü):
    ///  - Boşluğun etrafındaki yol kapalı bir "yürüyen bant"tır. Toplar şerit şerit, sıra sıra dizilir ve bant
    ///    saat yönünde sürekli döner (sol kol yukarı, köprü üstü, sağ kol aşağı, alttaki çıkış).
    ///  - Çıkışın önünden geçen toplardan rengi aktif bir bloğa uyanlar ayrılır; uymayanlar tura devam eder.
    ///  - Ayrılan topun yeri bantta boş yuva olarak döner; döngü dışındaki (giriş kolu / kuyruk) toplar
    ///    önlerinden boş yuva geçince sırayla banda katılır.
    ///
    /// Döngüsüz pist (U-kanal, S-kanal): toplar sıralı bir kuyruk gibi çıkışa yürür; öndeki önceliklidir.
    /// Toplar kinematik Rigidbody'dir; konumlarını MovePosition ile bu sınıf belirler.
    /// </summary>
    public static class TrackCrowdQueue
    {
        /// <summary>Bantın dönme hızı (cam yerel birimi / sn, yol ortasında).</summary>
        public static float BeltSpeed = 0.75f;
        /// <summary>Banda katılmamış (kuyruktaki) topların en yüksek yürüme hızı.</summary>
        public static float MaxSpeed = 1.7f;
        /// <summary>Durmuş bir kuyruk topunun tekrar tam hıza çıkma ivmesi.</summary>
        public static float Acceleration = 9f;
        /// <summary>İki top merkezi arasındaki en küçük mesafe (çap oranı).</summary>
        public static float SpacingRatio = 0.985f;

        /// <summary>Kuyruk topları hâlâ yürüyor ya da banda yerleşiyor mu? (Bantın kendi dönüşü sayılmaz.)</summary>
        public static bool IsMoving { get; private set; }

        private const float MinMoveSq = 0.0008f * 0.0008f;
        private const int ResolveIterations = 3;
        // Pist fiziği: top yuvasını yay-sönümleyici ile izler (hafif aşıp geri oturur), toplar birbirini iter.
        /// <summary>Yay sertliği (yuvaya çekim).</summary>
        public static float SpringStiffness = 220f;
        /// <summary>Sönüm oranı (1 = kritik; altı hafif sekme).</summary>
        public static float SpringDampingRatio = 0.42f;
        /// <summary>Bu mesafenin altında (çap oranı) toplar birbirini iter. Normal dizilimde değmesinler diye &lt; 1.</summary>
        public static float ContactRatio = 0.93f;
        /// <summary>Çarpışmanın sertliği (0..1): üst üste binmenin ne kadarı bir adımda çözülür.</summary>
        public static float ContactStiffness = 0.75f;
        private const float MaxBallSpeed = 4f;

        // ==================== Bant (döngü) verisi ====================

        private sealed class BeltLoop
        {
            public GlassTrackLayout.CompiledTrack source;
            public int lanes;
            public Vector2[][] lanePts;     // her şerit için düzgün aralıklı kapalı eğri
            public float[] laneLen;
            public int[] laneRows;
            public int[] slotBase;
            public int totalSlots;
            public float centerLen;
            public float phase;             // 0..1, bütün şeritler aynı oranda döner (renk blokları dağılmaz)
            public BallPhysicsController[] occupant;
            public float entryU;            // giriş yolu yoksa: gizli kuyruk girişi (yolun en üst noktası), bant oranı
            // Giriş yolu (gizli kuyruk): halkaya üstten bağlanan yol; toplar buradan aşağı akıp banda katılır
            public bool hasRoad;
            public Vector2 roadJunction;    // yolun halka orta çizgisine bağlandığı nokta
            public Vector2 roadDir;         // birleşimden yukarı (yolun tepesine) doğru birim yön
            public Vector2 roadTop;         // yolun tepesi: kuyruktaki sıradaki top burada belirir
            public float roadGate;          // bant doluyken toplar birleşime bu kadar yaklaşabilir (halka kenarı)
            public float roadHalfW;
        }

        private static BeltLoop s_Loop;
        private static GlassTrackLayout.CompiledTrack s_LoopFailedFor;

        /// <summary>Bu camın pistinde boşluğun etrafında dönen bir bant var mı?</summary>
        public static bool HasLoop(ShapedGlassContainer container)
        {
            return GetLoop(container) != null;
        }

        /// <summary>Top bantta ve şu an çıkışın önünden mi geçiyor?</summary>
        public static bool IsAtExit(BallPhysicsController ball)
        {
            var loop = s_Loop;
            if (loop == null || ball == null || ball.trackLoopSlot < 0 || ball.trackLoopSlot >= loop.totalSlots) return false;
            SlotToLaneRow(loop, ball.trackLoopSlot, out int lane, out int row);
            float u = Frac((float)row / loop.laneRows[lane] + loop.phase);
            float distAlong = Mathf.Min(u, 1f - u) * loop.laneLen[lane];
            float d = loop.source.ballDiameter;
            return distAlong < d * 0.85f;
        }

        private static BeltLoop GetLoop(ShapedGlassContainer container)
        {
            if (container == null || !container.IsTrackShape) return null;
            if (container.compiledTrack == null) container.RebuildTrackCompiledData();
            var track = container.compiledTrack;
            if (track == null) return null;
            if (s_Loop != null && s_Loop.source == track) return s_Loop;
            if (s_LoopFailedFor == track) return null;

            s_Loop = BuildLoop(track);
            if (s_Loop == null) s_LoopFailedFor = track;
            return s_Loop;
        }

        private static BeltLoop BuildLoop(GlassTrackLayout.CompiledTrack track)
        {
            // Pist kapalı bir halka olarak tanımlandıysa bant doğrudan o halkadır (cam ile şeritler birebir örtüşür)
            if (track.closedLoopBranch >= 0 && track.closedLoopBranch < track.polylines.Count)
            {
                var ring = new List<Vector2>(track.polylines[track.closedLoopBranch]);
                if (ring.Count > 1 && (ring[0] - ring[ring.Count - 1]).sqrMagnitude < 1e-6f) ring.RemoveAt(ring.Count - 1);
                if (SignedArea(ring) > 0f) ring.Reverse(1, ring.Count - 1);
                var built = BuildLoopFromCenter(track, ring, smooth: false);
                if (built != null)
                {
                    for (int i = 0; i < track.polylines.Count; i++)
                    {
                        if (i == track.closedLoopBranch || (i < track.polylineIsBridge.Count && track.polylineIsBridge[i])) continue;
                        var road = track.polylines[i];
                        if (road == null || road.Count < 2) continue;
                        built.hasRoad = true;
                        built.roadJunction = road[0];
                        built.roadTop = road[road.Count - 1];
                        built.roadDir = (built.roadTop - built.roadJunction).normalized;
                        built.roadHalfW = track.innerHalfWidth;
                        built.roadGate = track.innerHalfWidth + track.ballDiameter * 0.5f * 1.05f;
                        // Tepe noktası: top tamamen yolun içinde doğsun
                        built.roadTop -= built.roadDir * (track.ballDiameter * 0.6f);
                        break;
                    }
                }
                return built;
            }

            var arms = new List<List<Vector2>>();
            var bridges = new List<List<Vector2>>();
            for (int i = 0; i < track.polylines.Count; i++)
            {
                var poly = track.polylines[i];
                if (poly == null || poly.Count < 2) continue;
                bool isBridge = i < track.polylineIsBridge.Count && track.polylineIsBridge[i];
                if (isBridge) bridges.Add(poly); else arms.Add(poly);
            }
            if (arms.Count < 2 || bridges.Count == 0) return null;

            // Sol / sağ kol
            List<Vector2> left = arms[0], right = arms[1];
            if (MeanX(left) > MeanX(right)) { var t = left; left = right; right = t; }

            // Çıkışa en yakın köprü (alttaki ada boşluğunu çevreleyen)
            List<Vector2> bridge = bridges[0];
            for (int i = 1; i < bridges.Count; i++)
                if (MeanY(bridges[i]) < MeanY(bridge)) bridge = bridges[i];
            bridge = new List<Vector2>(bridge);
            if (bridge[0].x > bridge[bridge.Count - 1].x) bridge.Reverse();

            int iL = NearestIndex(left, bridge[0]);
            int iR = NearestIndex(right, bridge[bridge.Count - 1]);
            if (iL < 1 || iR < 1) return null;

            // Çıkış -> sol kol yukarı -> köprü -> sağ kol aşağı (saat yönü)
            var center = new List<Vector2>();
            for (int i = 0; i <= iL; i++) center.Add(left[i]);
            for (int i = 0; i < bridge.Count; i++) center.Add(bridge[i]);
            for (int i = iR; i >= 1; i--) center.Add(right[i]);
            if (center.Count < 4) return null;

            if (SignedArea(center) > 0f) center.Reverse(1, center.Count - 1);
            return BuildLoopFromCenter(track, center, smooth: true);
        }

        private static BeltLoop BuildLoopFromCenter(GlassTrackLayout.CompiledTrack track, List<Vector2> center, bool smooth)
        {
            if (center == null || center.Count < 4) return null;
            if (smooth) center = ChaikinClosed(center, 2);
            float d = Mathf.Max(0.05f, track.ballDiameter);
            center = ResampleClosed(center, d * 0.2f, out float centerLen);
            if (center.Count < 8 || centerLen < d * 6f) return null;

            int lanes = Mathf.Max(1, track.lanesAcross);
            float usableSpan = Mathf.Max(d * (lanes - 1), track.innerHalfWidth * 2f - d * 1.22f);
            float laneSpacing = lanes > 1 ? usableSpan / (lanes - 1) : 0f;

            var loop = new BeltLoop
            {
                source = track,
                lanes = lanes,
                lanePts = new Vector2[lanes][],
                laneLen = new float[lanes],
                laneRows = new int[lanes],
                slotBase = new int[lanes],
                centerLen = centerLen
            };

            int n = center.Count;
            int total = 0;
            for (int l = 0; l < lanes; l++)
            {
                float off = (l - (lanes - 1) * 0.5f) * laneSpacing;
                var lane = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    Vector2 tang = (center[(i + 1) % n] - center[(i - 1 + n) % n]).normalized;
                    Vector2 normal = new Vector2(-tang.y, tang.x);
                    lane.Add(center[i] + normal * off);
                }
                var res = ResampleClosed(lane, d * 0.1f, out float len);
                loop.lanePts[l] = res.ToArray();
                loop.laneLen[l] = len;
                loop.laneRows[l] = Mathf.Max(1, Mathf.FloorToInt(len / (d * 1.03f)));
                loop.slotBase[l] = total;
                total += loop.laneRows[l];
            }
            loop.totalSlots = total;
            loop.occupant = new BallPhysicsController[total];

            // Gizli kuyruk girişi: yolun en üst noktası (U'da kol tepesi, ovalde üst orta)
            int topIdx = 0;
            for (int i = 1; i < n; i++)
                if (center[i].y > center[topIdx].y + 1e-4f || (Mathf.Abs(center[i].y - center[topIdx].y) <= 1e-4f && center[i].x < center[topIdx].x))
                    topIdx = i;
            loop.entryU = (float)topIdx / n;
            return loop;
        }

        /// <summary>
        /// Halkalı pist için başlangıç top yerleşimini bant yuvalarından üretir (faz 0). Böylece level açılırken
        /// toplar tam bant yuvalarında doğar: şeritler hizalı başlar, iç boşluk ve renk kayması olmaz.
        /// distToExit = topun bant yönünde çıkışa kalan yolu (0: tam çıkışta).
        /// </summary>
        public static int AppendRingSlots(GlassTrackLayout.CompiledTrack track, List<GlassTrackLayout.TrackSlot> slots)
        {
            if (track == null || slots == null || track.closedLoopBranch < 0) return 0;
            var loop = BuildLoop(track);
            if (loop == null) return 0;
            int added = 0;
            for (int l = 0; l < loop.lanes; l++)
            {
                for (int row = 0; row < loop.laneRows[l]; row++)
                {
                    float u = (float)row / loop.laneRows[l];
                    slots.Add(new GlassTrackLayout.TrackSlot
                    {
                        localPos = SlotPosition(loop, l, row),
                        branchId = track.closedLoopBranch,
                        rowIndex = row,
                        laneIndex = l,
                        distToExit = Frac(1f - u) * loop.centerLen
                    });
                    added++;
                }
            }
            return added;
        }

        private static Vector2 SlotPosition(BeltLoop loop, int lane, int row)
        {
            float u = Frac((float)row / loop.laneRows[lane] + loop.phase);
            var pts = loop.lanePts[lane];
            float f = u * pts.Length;
            int i0 = Mathf.FloorToInt(f) % pts.Length;
            int i1 = (i0 + 1) % pts.Length;
            return Vector2.Lerp(pts[i0], pts[i1], f - Mathf.Floor(f));
        }

        private static void SlotToLaneRow(BeltLoop loop, int slot, out int lane, out int row)
        {
            lane = loop.lanes - 1;
            for (int l = 0; l < loop.lanes - 1; l++)
            {
                if (slot < loop.slotBase[l + 1]) { lane = l; break; }
            }
            row = slot - loop.slotBase[lane];
        }

        // ==================== Adım ====================

        private static BallPhysicsController[] s_Balls = new BallPhysicsController[256];
        private static Vector2[] s_Pos = new Vector2[256];
        private static Vector2[] s_Flow = new Vector2[256];
        private static float[] s_Keys = new float[256];
        private static int[] s_Order = new int[256];
        private static int[] s_Next = new int[256];
        private static Vector2[] s_SlotPos = new Vector2[256];
        private static BallPhysicsController[] s_All = new BallPhysicsController[256];
        private static Vector2[] s_Prev = new Vector2[256];
        private static Vector2[] s_Corr = new Vector2[256];
        private static int[] s_CellHead = new int[0];

        /// <summary>
        /// Pisti bir fizik adımı ilerletir. Bir top bile hareket ettiyse true döner.
        /// </summary>
        public static bool Step(ShapedGlassContainer container, List<BallPhysicsController> activeBalls, float dt)
        {
            IsMoving = false;
            if (container == null || activeBalls == null || dt <= 0f) return false;
            if (container.compiledTrack == null) container.RebuildTrackCompiledData();
            var track = container.compiledTrack;
            if (track == null) return false;

            Transform ct = container.transform;
            EnsureCapacity(activeBalls.Count);
            var loop = GetLoop(container);

            float d = Mathf.Max(0.05f, track.ballDiameter);
            float r = d * 0.5f;
            float minDist = d * SpacingRatio;
            float minDistSq = minDist * minDist;
            Rect fr = track.fieldRect;
            int cols = Mathf.Max(1, Mathf.CeilToInt(fr.width / d));
            int rows = Mathf.Max(1, Mathf.CeilToInt(fr.height / d));
            if (s_CellHead.Length < cols * rows) s_CellHead = new int[cols * rows];
            Array.Fill(s_CellHead, -1, 0, cols * rows);

            bool anyMoved = false;
            bool settling = false;

            // 1. Bantı döndür, geçersiz yuvaları boşalt, yuva konumlarını hesapla
            if (loop != null)
            {
                // Bant sadece üzerinde top varken döner; boşken faz 0'a döner ki yeni doğan toplar tam yuvalarına otursun
                bool anyOnBelt = false;
                for (int s = 0; s < loop.totalSlots && !anyOnBelt; s++) anyOnBelt = loop.occupant[s] != null;
                loop.phase = anyOnBelt ? Frac(loop.phase + BeltSpeed * dt / Mathf.Max(0.01f, loop.centerLen)) : 0f;
                if (s_SlotPos.Length < loop.totalSlots) s_SlotPos = new Vector2[Mathf.NextPowerOfTwo(loop.totalSlots)];
                for (int l = 0; l < loop.lanes; l++)
                {
                    for (int row = 0; row < loop.laneRows[l]; row++)
                    {
                        int slot = loop.slotBase[l] + row;
                        s_SlotPos[slot] = SlotPosition(loop, l, row);
                        var occ = loop.occupant[slot];
                        if (occ != null && (!occ.trackQueueDriven || occ.state != BallState.InContainer || occ.trackLoopSlot != slot || !occ.gameObject.activeInHierarchy))
                            loop.occupant[slot] = null;
                    }
                }
                CloseBeltGaps(loop, d);

                int occupied = 0;
                for (int s = 0; s < loop.totalSlots; s++) if (loop.occupant[s] != null) occupied++;
                s_Occupied = occupied;

                // Giriş yolu olmayan pistte (banda sığmamış toplar): eski usul girişten banda besleme
                if (!loop.hasRoad && s_Pending.Count > 0 && CanJoinBelt(occupied) && AllSeated(activeBalls))
                    FeedFromHiddenQueue(loop, container, d, s_BeltLimit > 0 ? s_BeltLimit - occupied : int.MaxValue);
                EnsureCapacity(activeBalls.Count); // yeni giren toplar için
            }

            // 2. Pistteki topları topla: bantta olanlar yuvalarına kayar (önce yerleşir, engel olur)
            int n = 0;
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

                if (loop != null && b.trackLoopSlot >= 0 && (b.trackLoopSlot >= loop.totalSlots || loop.occupant[b.trackLoopSlot] != b))
                    b.trackLoopSlot = -1;

                if (loop != null && b.trackLoopSlot >= 0)
                {
                    Vector2 p = b.trackQueueLocalPos;
                    Vector2 target = s_SlotPos[b.trackLoopSlot];
                    // Yay-sönümleyici: yuvaya doğru hızlanır, hafifçe aşıp geri oturur (yarı-örtük Euler)
                    float k = SpringStiffness;
                    float c = 2f * Mathf.Sqrt(k) * SpringDampingRatio;
                    Vector2 v = b.trackVel + (k * (target - p) - c * b.trackVel) * dt;
                    if (v.sqrMagnitude > MaxBallSpeed * MaxBallSpeed) v = v.normalized * MaxBallSpeed;
                    b.trackVel = v;
                    Vector2 q = p + v * dt;
                    if ((target - q).sqrMagnitude > (d * 0.6f) * (d * 0.6f)) settling = true;
                    InsertIntoGrid(b, q, fr, d, cols, rows, n);
                    s_Pos[n] = q;
                    s_Prev[n] = p;
                    s_All[n] = b;
                    s_Balls[n] = null; // yerleşmiş; kuyruk işleminde atlanır
                    n++;
                    continue;
                }

                Vector2 pp = b.trackQueueLocalPos;
                track.SampleField(pp, out _, out Vector2 flow, out float exitDist, out _);
                s_Balls[n] = b;
                s_All[n] = b;
                s_Pos[n] = pp;
                s_Prev[n] = pp;
                s_Flow[n] = flow;
                s_Keys[n] = exitDist;
                n++;
            }

            // 3. Banda katılmamış toplar: çıkışa en yakından başlayarak sıralı kuyruk
            int q0 = 0;
            for (int i = 0; i < n; i++)
            {
                if (s_Balls[i] == null) continue;
                s_Order[q0] = i;
                s_Keys[q0] = s_Keys[i];
                q0++;
            }
            if (q0 > 1) Array.Sort(s_Keys, s_Order, 0, q0);

            float wallClear = r * 1.02f;
            float maxStep = MaxSpeed * dt;
            for (int k = 0; k < q0; k++)
            {
                int i = s_Order[k];
                var b = s_Balls[i];
                Vector2 p = s_Pos[i];
                Vector2 flow = s_Flow[i];

                float v = Mathf.MoveTowards(b.trackQueueSpeed, MaxSpeed, Acceleration * dt);
                Vector2 q = p + flow * (v * dt);

                for (int it = 0; it < ResolveIterations; it++)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt((q.x - fr.xMin) / d), 0, cols - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt((q.y - fr.yMin) / d), 0, rows - 1);
                    for (int gy = Mathf.Max(0, cy - 1); gy <= Mathf.Min(rows - 1, cy + 1); gy++)
                    {
                        for (int gx = Mathf.Max(0, cx - 1); gx <= Mathf.Min(cols - 1, cx + 1); gx++)
                        {
                            int j = s_CellHead[gy * cols + gx];
                            while (j != -1)
                            {
                                Vector2 diff = q - s_Pos[j];
                                float dSq = diff.sqrMagnitude;
                                if (dSq < minDistSq)
                                {
                                    float dist = Mathf.Sqrt(dSq);
                                    Vector2 dir = dist > 1e-5f ? diff / dist : -flow;
                                    q += dir * (minDist - dist);
                                }
                                j = s_Next[j];
                            }
                        }
                    }

                    track.SampleField(q, out float sdf, out _, out _, out Vector2 inward);
                    float over = sdf + wallClear;
                    if (over > 0f)
                    {
                        if (sdf > 0f) over = Mathf.Min(over, maxStep * 2.5f);
                        q += inward * over;
                    }

                    // Giriş yolu kapısı: bant kapasitesi doluyken yoldaki toplar halkanın şeridine giremez
                    if (loop != null && loop.hasRoad && !CanJoinBelt(s_Occupied))
                    {
                        Vector2 rel = q - loop.roadJunction;
                        float al = Vector2.Dot(rel, loop.roadDir);
                        float lat = Mathf.Abs(rel.x * loop.roadDir.y - rel.y * loop.roadDir.x);
                        if (lat < loop.roadHalfW * 1.6f && al > -loop.roadHalfW && al < loop.roadGate)
                            q += loop.roadDir * (loop.roadGate - al);
                    }
                }

                Vector2 delta = q - p;
                if (delta.sqrMagnitude < MinMoveSq) { q = p; delta = Vector2.zero; }

                float along = Mathf.Max(0f, Vector2.Dot(delta, flow)) / dt;
                b.trackQueueSpeed = Mathf.Min(MaxSpeed, along);

                if (delta != Vector2.zero) settling = true;
                s_Pos[i] = q;
                InsertIntoGrid(b, q, fr, d, cols, rows, i);

                // Önünden boş bir bant yuvası geçiyorsa (ve bantta yer varsa) banda katıl
                if (loop != null && CanJoinBelt(s_Occupied) && TryJoinBelt(loop, b, q, d)) s_Occupied++;
            }
            RoadQueueCount = q0;

            // 4. Fizik: bütün toplar (bant + kuyruk) birbirini karşılıklı iter, duvarlar içeride tutar; sonra
            //    hız, gerçekte gidilen yoldan güncellenir (çarpışma hızı da etkiler) ve sert çarpmada top ezilir.
            SolveContacts(track, loop, n, d, fr, cols, rows);
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                if (b == null) continue;
                Vector2 p = s_Prev[i];
                Vector2 q = s_Pos[i];
                if (b.trackLoopSlot >= 0)
                {
                    Vector2 v = (q - p) / dt;
                    if (v.sqrMagnitude > MaxBallSpeed * MaxBallSpeed) v = v.normalized * MaxBallSpeed;
                    b.trackVel = v;
                }
                float hit = s_Corr[i].magnitude / dt;
                if (hit > 0.22f)
                {
                    Vector3 axis = ct.TransformDirection(new Vector3(s_Corr[i].x, s_Corr[i].y, 0f));
                    b.PlayTrackBump(axis, Mathf.Clamp(hit * 0.035f, 0.03f, 0.085f));
                }
                if (ApplyMove(b, ct, p, q)) anyMoved = true;
            }

            // Giriş yolunun tepesi boşsa kuyruktaki sıradaki top orada belirir ve aşağı akar
            if (loop != null && loop.hasRoad && s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
            {
                float clearSq = (d * 1.05f) * (d * 1.05f);
                bool topFree = true;
                for (int i = 0; i < n && topFree; i++) if ((s_Pos[i] - loop.roadTop).sqrMagnitude < clearSq) topFree = false;
                if (topFree && SpawnQueuedBall(container, loop.roadTop) != null)
                {
                    s_NextRoadSpawn = Time.time + 0.06f;
                    settling = true;
                }
            }

            Array.Clear(s_Balls, 0, n);
            Array.Clear(s_All, 0, n);
            IsMoving = settling;
            return anyMoved;
        }

        // ==================== Gizli kuyruk (zorluk) ====================

        private static readonly List<PuzzleColor> s_Pending = new List<PuzzleColor>();
        private static float s_PendingRadius = 0.08f;
        // Bant kapasite sınırı: level başında halkada olan top sayısı. Yoldaki/kuyruktaki toplar ancak bant bunun
        // altına düşünce (bir top toplanınca) banda girer. 0 veya altı: sınırsız.
        private static int s_BeltLimit = -1;
        private static int s_Occupied;
        private static float s_NextRoadSpawn;

        /// <summary>Henüz camda olmayan (giriş yolunun tepesinden gelecek) top sayısı.</summary>
        public static int PendingCount => s_Pending.Count;
        /// <summary>Giriş yolunda (bant dışında) sırasını bekleyen top sayısı (son adım).</summary>
        public static int RoadQueueCount { get; private set; }

        /// <summary>Level başında: halkaya sığmayan toplar ve bant kapasite sınırı.</summary>
        public static void SetPending(List<PuzzleColor> colors, float ballRadius, int beltLimit)
        {
            s_Pending.Clear();
            if (colors != null) s_Pending.AddRange(colors);
            s_PendingRadius = Mathf.Max(0.02f, ballRadius);
            s_BeltLimit = beltLimit > 0 ? beltLimit : -1;
            s_NextRoadSpawn = 0f;
            RoadQueueCount = 0;
        }

        public static void ClearPending()
        {
            s_Pending.Clear();
            s_BeltLimit = -1;
            RoadQueueCount = 0;
        }

        private static bool CanJoinBelt(int occupied) => s_BeltLimit <= 0 || occupied < s_BeltLimit;

        private static bool AllSeated(List<BallPhysicsController> balls)
        {
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                if (!b.trackQueueDriven || b.trackLoopSlot < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Bekleyen toplar banda girebilir mi? Bantta boş yuva olmalı ve bant, başlangıçtaki top sayısı
        /// (kapasite sınırı) kadar dolu olmamalı; dolu ise ancak bir top toplanıp yer açılınca girer.
        /// </summary>
        public static bool HasFreeBeltSlot(ShapedGlassContainer container)
        {
            var loop = GetLoop(container);
            if (loop == null) return false;
            int occupied = 0;
            bool anyEmpty = false;
            for (int s = 0; s < loop.totalSlots; s++)
            {
                if (loop.occupant[s] != null) occupied++;
                else anyEmpty = true;
            }
            return anyEmpty && CanJoinBelt(occupied);
        }

        /// <summary>Kuyruktaki sıradaki topu cam yerel konumunda doğurur (kinematik kuyruk topu olarak).</summary>
        private static BallPhysicsController SpawnQueuedBall(ShapedGlassContainer container, Vector2 localPos)
        {
            if (s_Pending.Count == 0 || BallObjectPool.Instance == null) return null;
            Vector3 world = container.transform.TransformPoint(new Vector3(localPos.x, localPos.y, -0.10f));
            var ball = BallObjectPool.Instance.SpawnBall(s_Pending[0], world, s_PendingRadius);
            if (ball == null) return null;
            s_Pending.RemoveAt(0);
            ball.ConfigureForTrackMode(true);
            ball.ReleaseToPhysics(); // pist modunda kinematik olup kuyruğa katılır
            BallCollectionController.Instance?.MarkBallFieldDirty();
            return ball;
        }

        /// <summary>
        /// Girişin önünden boş bir bant yuvası geçerken kuyruktaki sıradaki topu o yuvada doğurur.
        /// Kalabalığın kuyruğu girişi geçtikçe arkasındaki boş yuvalar sırayla dolar; böylece kuyruktaki
        /// renk sırası bantta da korunur.
        /// </summary>
        private static void FeedFromHiddenQueue(BeltLoop loop, ShapedGlassContainer container, float d, int maxToAdd)
        {
            if (s_Pending.Count == 0 || BallObjectPool.Instance == null || !Application.isPlaying) return;
            if (BallFeedTubeController.Instance != null && BallFeedTubeController.Instance.IsPouringOrSettling) return;

            int added = 0;
            for (int l = 0; l < loop.lanes && s_Pending.Count > 0 && added < maxToAdd; l++)
            {
                int rows = loop.laneRows[l];
                int row = Mathf.RoundToInt(Frac(loop.entryU - loop.phase) * rows) % rows;
                int slot = loop.slotBase[l] + row;
                if (loop.occupant[slot] != null) continue;
                float u = Frac((float)row / rows + loop.phase);
                float du = Mathf.Abs(u - loop.entryU);
                du = Mathf.Min(du, 1f - du);
                if (du * loop.laneLen[l] > d * 0.5f) continue;

                var ball = SpawnQueuedBall(container, SlotPosition(loop, l, row));
                if (ball == null) return;
                loop.occupant[slot] = ball;
                ball.trackLoopSlot = slot;
                added++;
                BallCollectionController.Instance?.MarkBallFieldDirty();
            }
        }

        /// <summary>
        /// Boşluk kapatma: her şeritte kalabalığın önü, önündeki en uzun boş aralığa bakan toptur; o bant hızında
        /// kalır. Diğer bütün toplar önlerindeki yuva boşsa bir sıra ilerler. Önden arkaya işlendiği için
        /// arkadaki bütün dizi aynı anda bir adım atar; böylece kalabalık tek, sıkışık bir kütle halinde kalır.
        /// </summary>
        private static void CloseBeltGaps(BeltLoop loop, float d)
        {
            // Bant sürekli döndüğü için top yuvasını hep biraz geriden izler; bu gecikmeyi "oturmuş" say
            float settledSq = (d * 0.65f) * (d * 0.65f);
            for (int l = 0; l < loop.lanes; l++)
            {
                int rowsInLane = loop.laneRows[l];
                int bas = loop.slotBase[l];
                if (rowsInLane < 3) continue;

                // Kalabalığın önü: arkasından (ileri yönde) gelen boş aralığı en uzun olan dolu yuva
                int front = -1, bestRun = -1, occupiedCount = 0;
                for (int row = 0; row < rowsInLane; row++)
                {
                    if (loop.occupant[bas + row] == null) continue;
                    occupiedCount++;
                    int run = 0;
                    while (run < rowsInLane - 1 && loop.occupant[bas + (row + 1 + run) % rowsInLane] == null) run++;
                    if (run > bestRun) { bestRun = run; front = row; }
                }
                if (front < 0 || occupiedCount == rowsInLane) continue;

                for (int step = 1; step < rowsInLane; step++)
                {
                    int row = ((front - step) % rowsInLane + rowsInLane) % rowsInLane;
                    int slot = bas + row;
                    var b = loop.occupant[slot];
                    if (b == null) continue;
                    int nextSlot = bas + (row + 1) % rowsInLane;
                    if (loop.occupant[nextSlot] != null) continue;
                    if ((b.trackQueueLocalPos - s_SlotPos[slot]).sqrMagnitude > settledSq) continue;

                    loop.occupant[slot] = null;
                    loop.occupant[nextSlot] = b;
                    b.trackLoopSlot = nextSlot;
                }
            }
        }

        /// <summary>
        /// Toplar arası karşılıklı itme (konum tabanlı, iki top da yarı yarıya kayar) + duvar sınırı + giriş yolu
        /// kapısı. Her topun bu adımda yediği toplam itme s_Corr'da tutulur (çarpışma sertliği / ezilme için).
        /// </summary>
        private static void SolveContacts(GlassTrackLayout.CompiledTrack track, BeltLoop loop, int n, float d, Rect fr, int cols, int rows)
        {
            float contact = d * ContactRatio;
            float contactSq = contact * contact;
            float wallClear = d * 0.5f * 1.02f;
            for (int i = 0; i < n; i++) s_Corr[i] = Vector2.zero;

            for (int iter = 0; iter < 2; iter++)
            {
                Array.Fill(s_CellHead, -1, 0, cols * rows);
                for (int i = 0; i < n; i++)
                {
                    if (s_All[i] == null) continue;
                    int ix = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].x - fr.xMin) / d), 0, cols - 1);
                    int iy = Mathf.Clamp(Mathf.FloorToInt((s_Pos[i].y - fr.yMin) / d), 0, rows - 1);
                    int cell = iy * cols + ix;
                    s_Next[i] = s_CellHead[cell];
                    s_CellHead[cell] = i;
                }

                for (int i = 0; i < n; i++)
                {
                    if (s_All[i] == null) continue;
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
                                        Vector2 dir = dist > 1e-5f ? diff / dist : new Vector2(0f, 1f);
                                        Vector2 push = dir * ((contact - dist) * 0.5f * ContactStiffness);
                                        s_Pos[i] += push; s_Corr[i] += push;
                                        s_Pos[j] -= push; s_Corr[j] -= push;
                                    }
                                }
                                j = s_Next[j];
                            }
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    var b = s_All[i];
                    if (b == null) continue;
                    track.SampleField(s_Pos[i], out float sdf, out _, out _, out Vector2 inward);
                    float over = sdf + wallClear;
                    if (over > 0f && sdf <= 0.05f)
                    {
                        Vector2 push = inward * over;
                        s_Pos[i] += push; s_Corr[i] += push;
                    }

                    // Bant doluyken kuyruk topları halkanın şeridine itilemez
                    if (b.trackLoopSlot < 0 && loop != null && loop.hasRoad && !CanJoinBelt(s_Occupied))
                    {
                        Vector2 rel = s_Pos[i] - loop.roadJunction;
                        float al = Vector2.Dot(rel, loop.roadDir);
                        float lat = Mathf.Abs(rel.x * loop.roadDir.y - rel.y * loop.roadDir.x);
                        if (lat < loop.roadHalfW * 1.6f && al > -loop.roadHalfW && al < loop.roadGate)
                            s_Pos[i] += loop.roadDir * (loop.roadGate - al);
                    }
                }
            }
        }

        private static bool TryJoinBelt(BeltLoop loop, BallPhysicsController b, Vector2 p, float d)
        {
            float bestSq = (d * 0.9f) * (d * 0.9f);
            int best = -1;
            for (int s = 0; s < loop.totalSlots; s++)
            {
                if (loop.occupant[s] != null) continue;
                float dSq = (s_SlotPos[s] - p).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = s; }
            }
            if (best < 0) return false;
            loop.occupant[best] = b;
            b.trackLoopSlot = best;
            b.trackQueueSpeed = 0f;
            return true;
        }

        private static bool ApplyMove(BallPhysicsController b, Transform ct, Vector2 p, Vector2 q)
        {
            Vector2 delta = q - p;
            if (delta.sqrMagnitude < MinMoveSq) return false;

            b.trackQueueLocalPos = q;
            b.rb.MovePosition(ct.TransformPoint(new Vector3(q.x, q.y, b.trackQueueLocalZ)));

            // Yuvarlanma: hareket yönüne dik eksende, kat edilen yol / yarıçap kadar dön
            Vector3 worldDelta = ct.TransformVector(new Vector3(delta.x, delta.y, 0f));
            float dist = worldDelta.magnitude;
            if (dist > 1e-5f)
            {
                Vector3 axis = Vector3.Cross(Vector3.back, worldDelta / dist);
                float deg = dist / Mathf.Max(0.02f, b.ballRadius) * Mathf.Rad2Deg;
                b.rb.MoveRotation(Quaternion.AngleAxis(deg, axis) * b.rb.rotation);
            }
            return true;
        }

        private static void InsertIntoGrid(BallPhysicsController b, Vector2 q, Rect fr, float d, int cols, int rows, int index)
        {
            int ix = Mathf.Clamp(Mathf.FloorToInt((q.x - fr.xMin) / d), 0, cols - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt((q.y - fr.yMin) / d), 0, rows - 1);
            int cell = iy * cols + ix;
            s_Next[index] = s_CellHead[cell];
            s_CellHead[cell] = index;
        }

        private static void EnsureCapacity(int count)
        {
            if (s_Balls.Length >= count) return;
            int cap = Mathf.NextPowerOfTwo(count);
            s_Balls = new BallPhysicsController[cap];
            s_Pos = new Vector2[cap];
            s_Flow = new Vector2[cap];
            s_Keys = new float[cap];
            s_Order = new int[cap];
            s_All = new BallPhysicsController[cap];
            s_Prev = new Vector2[cap];
            s_Corr = new Vector2[cap];
            s_Next = new int[cap];
        }

        // ==================== Geometri yardımcıları ====================

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float MeanX(List<Vector2> pts)
        {
            float s = 0f;
            for (int i = 0; i < pts.Count; i++) s += pts[i].x;
            return s / pts.Count;
        }

        private static float MeanY(List<Vector2> pts)
        {
            float s = 0f;
            for (int i = 0; i < pts.Count; i++) s += pts[i].y;
            return s / pts.Count;
        }

        private static int NearestIndex(List<Vector2> pts, Vector2 p)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float dSq = (pts[i] - p).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = i; }
            }
            return best;
        }

        private static float SignedArea(List<Vector2> pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p0 = pts[i], p1 = pts[(i + 1) % pts.Count];
                a += p0.x * p1.y - p1.x * p0.y;
            }
            return a * 0.5f;
        }

        private static List<Vector2> ChaikinClosed(List<Vector2> pts, int iterations)
        {
            var cur = pts;
            for (int it = 0; it < iterations; it++)
            {
                var next = new List<Vector2>(cur.Count * 2);
                for (int i = 0; i < cur.Count; i++)
                {
                    Vector2 a = cur[i], b = cur[(i + 1) % cur.Count];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                cur = next;
            }
            return cur;
        }

        private static List<Vector2> ResampleClosed(List<Vector2> pts, float spacing, out float totalLen)
        {
            int n = pts.Count;
            totalLen = 0f;
            for (int i = 0; i < n; i++) totalLen += Vector2.Distance(pts[i], pts[(i + 1) % n]);
            var res = new List<Vector2>();
            if (totalLen < 1e-4f) return res;

            int count = Mathf.Max(8, Mathf.RoundToInt(totalLen / Mathf.Max(0.005f, spacing)));
            float step = totalLen / count;
            int seg = 0;
            float segStart = 0f;
            float segLen = Vector2.Distance(pts[0], pts[1 % n]);
            for (int k = 0; k < count; k++)
            {
                float target = k * step;
                while (segStart + segLen < target && seg < n - 1)
                {
                    segStart += segLen;
                    seg++;
                    segLen = Vector2.Distance(pts[seg], pts[(seg + 1) % n]);
                }
                float t = segLen > 1e-6f ? Mathf.Clamp01((target - segStart) / segLen) : 0f;
                res.Add(Vector2.Lerp(pts[seg], pts[(seg + 1) % n], t));
            }
            return res;
        }
    }
}
