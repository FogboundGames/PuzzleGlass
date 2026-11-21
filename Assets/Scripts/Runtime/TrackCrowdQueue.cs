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
    public static partial class TrackCrowdQueue
    {
        /// <summary>Bantın dönme hızı (cam yerel birimi / sn, yol ortasında).</summary>
        public static float BeltSpeed = 1.18f;
        /// <summary>Banda katılmamış (kuyruktaki) topların en yüksek yürüme hızı.</summary>
        public static float MaxSpeed = 2.45f;
        /// <summary>Durmuş bir kuyruk topunun tekrar tam hıza çıkma ivmesi.</summary>
        public static float Acceleration = 12.5f;
        /// <summary>İki top merkezi arasındaki en küçük mesafe (çap oranı).</summary>
        public static float SpacingRatio = 0.985f;

        /// <summary>Kuyruk topları hâlâ yürüyor ya da banda yerleşiyor mu? (Bantın kendi dönüşü sayılmaz.)</summary>
        public static bool IsMoving { get; private set; }

        private const float MinMoveSq = 0.0004f * 0.0004f;
        private const int ResolveIterations = 3;
        // Pist fiziği: gerçek çarpışma yön değişimi, komşu itme kuvveti, ağırlık/momentum, sıkışma, kontrollü sekme ve tutarlı yerçekimi.
        /// <summary>Teğetsel yay sertliği (kanal boyunca akış takibi; topların ağırlık ve momentum taşıması için esnek tutulur).</summary>
        public static float SpringStiffness = 72f;
        /// <summary>Enine (kanal genişliği boyunca) esnek yuva rehberi; düşük tutularak çarpışma yön değişimi, sıkışma ve yerçekimine izin verir.</summary>
        public static float LateralSpringStiffness = 5.5f;
        /// <summary>Sönüm oranı (1 = kritik; 0.25 = toplar aniden durmaz, momentumla hareketini doğal tamamlar ve hafif seker).</summary>
        public static float SpringDampingRatio = 0.25f;
        /// <summary>Bu mesafenin altında (çap oranı) toplar birbirine fiziksel olarak değer, sıkışır ve seker.</summary>
        public static float ContactRatio = 0.992f;
        /// <summary>Hareket eden topun temas öncesi çevresindeki topları hafifçe itmeye başladığı etki yarıçapı (çap oranı).</summary>
        public static float PushRadiusRatio = 1.12f;
        /// <summary>Çarpışmanın sertliği (0..1): üst üste binmenin ne kadarı bir adımda çözülür.</summary>
        public static float ContactStiffness = 0.88f;
        /// <summary>Toplar arası çarpışma esnekliği / kontrollü sekme katsayısı (Newtonian restitution).</summary>
        public static float BallRestitution = 0.42f;
        /// <summary>Cam duvar çarpması kontrollü sekme katsayısı.</summary>
        public static float WallRestitution = 0.28f;
        /// <summary>Oyunun dikey perspektifine uygun tutarlı aşağı (-Y) yerçekimi ivmesi.</summary>
        public static float TrackGravity = 6.2f;
        /// <summary>Top havuzu canlılığı için hafif salınım/itişme kuvveti.</summary>
        public static float AgitationStrength = 1.55f;
        private const float MaxBallSpeed = 4.2f;
        private const float FastForwardBallSpeedMultiplier = 2.0f;

        private static float GetActiveSpeedMultiplier()
        {
            return (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsEndgameFastForward)
                ? FastForwardBallSpeedMultiplier
                : 1.0f;
        }

        // ==================== Bant (döngü) verisi ====================

        private sealed class BeltLoop
        {
            public GlassTrackLayout.CompiledTrack source;
            public int lanes;
            public Vector2[] centerPts;     // eşit yay uzunluklu kapalı merkez eğrisi
            public Vector2[] centerTang;    // merkez eğrisi saat yönü birim teğetleri
            public Vector2[] centerNorm;    // merkez eğrisi enine birim normalleri (içten dışa)
            public float[] laneOffsets;     // her şeridin merkez çizgisine dik ofseti
            public Vector2[][] lanePts;     // her şerit için düzgün aralıklı kapalı eğri
            public float[] laneLen;
            public int[] laneRows;
            public int[] slotBase;
            public int totalSlots;
            public int rowsAround;          // tüm şeritlerde ortak enine kesit sıra sayısı
            public float centerLen;
            public float phase;             // 0..1, bütün şeritler aynı oranda döner (renk blokları dağılmaz)
            public BallPhysicsController[] occupant;
            // Granül akış: cam yerel düzleminde her hücre için en yakın halka orta çizgisi noktası (yol teğeti için)
            public int[] nearLookup;
            public Rect nearRect;
            public float nearCell;
            public int nearCols, nearRows;
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

        /// <summary>Top bantta ve şu an fiziksel olarak çıkışın önünden mi geçiyor?</summary>
        public static bool IsAtExit(BallPhysicsController ball)
        {
            // Katman kilidi: daha dış katmanda top varken içteki katmanın topu çıkıştan ayrılamaz
            if (!IsLayerOpen(ball)) return false;
            var loop = s_Loop;
            if (UseGranularFlow || UseRigidbodyPhysics) return IsAtExitGranular(loop, ball);
            if (loop == null || ball == null || ball.trackLoopSlot < 0 || ball.trackLoopSlot >= loop.totalSlots) return false;
            SlotToLaneRow(loop, ball.trackLoopSlot, out int lane, out int row);
            float u = GetSlotU(loop, lane, row);
            float distAlong = Mathf.Min(u, 1f - u) * loop.centerLen;
            float d = loop.source.ballDiameter;
            if (distAlong < d * 1.45f)
            {
                if (!ball.trackQueuePosValid) return true;
                float maxExitDist = loop.source.innerHalfWidth + d * 1.85f;
                if ((ball.trackQueueLocalPos - loop.source.exitPoint).sqrMagnitude <= maxExitDist * maxExitDist)
                    return true;
            }
            if (ball.trackQueuePosValid)
            {
                Vector2 rel = ball.trackQueueLocalPos - loop.source.exitPoint;
                if (Mathf.Abs(rel.x) <= d * 1.35f && Mathf.Abs(rel.y) <= loop.source.innerHalfWidth + d * 0.85f)
                    return true;
            }
            return false;
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
            center = ResampleClosed(center, d * 0.14f, out float centerLen);
            if (center.Count < 8 || centerLen < d * 6f) return null;

            int lanes = Mathf.Max(1, track.lanesAcross);
            // Petek (honeycomb) top havuzu dizilimi için enine şerit aralığı (~0.866 * d, kanal genişliğine uyumlu)
            float maxChannelSpan = Mathf.Max(0f, track.innerHalfWidth * 2f - d * 1.12f);
            float idealHoneycombSpan = d * 0.87f * Mathf.Max(0, lanes - 1);
            float minChannelSpan = Mathf.Min(maxChannelSpan, d * 0.76f * Mathf.Max(0, lanes - 1));
            float usableSpan = lanes > 1 ? Mathf.Clamp(idealHoneycombSpan, minChannelSpan, Mathf.Max(minChannelSpan, maxChannelSpan)) : 0f;
            float laneSpacing = lanes > 1 ? usableSpan / (lanes - 1) : 0f;

            int n = center.Count;
            Vector2[] cPts = center.ToArray();
            Vector2[] cTang = new Vector2[n];
            Vector2[] cNorm = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 tang = (cPts[(i + 1) % n] - cPts[(i - 1 + n) % n]).normalized;
                if (tang.sqrMagnitude < 1e-6f) tang = Vector2.left;
                cTang[i] = tang;
                cNorm[i] = new Vector2(-tang.y, tang.x);
            }

            // Tüm şeritler aynı merkez kesit sıra sayısını (rowsAround) paylaşır:
            // böylece iç ve dış şeritler virajlarda birbirinden kopmaz, renk grupları ve ön/arka uçlar tam hizalı kalır.
            int rowsAround = Mathf.Max(4, Mathf.FloorToInt(centerLen / (d * 1.02f)));

            var loop = new BeltLoop
            {
                source = track,
                lanes = lanes,
                centerPts = cPts,
                centerTang = cTang,
                centerNorm = cNorm,
                laneOffsets = new float[lanes],
                lanePts = new Vector2[lanes][],
                laneLen = new float[lanes],
                laneRows = new int[lanes],
                slotBase = new int[lanes],
                rowsAround = rowsAround,
                centerLen = centerLen
            };

            int total = 0;
            for (int l = 0; l < lanes; l++)
            {
                float off = (l - (lanes - 1) * 0.5f) * laneSpacing;
                loop.laneOffsets[l] = off;
                var lane = new Vector2[n];
                float len = 0f;
                for (int i = 0; i < n; i++)
                {
                    lane[i] = cPts[i] + cNorm[i] * off;
                    if (i > 0) len += Vector2.Distance(lane[i - 1], lane[i]);
                }
                len += Vector2.Distance(lane[n - 1], lane[0]);
                loop.lanePts[l] = lane;
                loop.laneLen[l] = len;
                loop.laneRows[l] = rowsAround;
                loop.slotBase[l] = total;
                total += rowsAround;
            }
            loop.totalSlots = total;
            loop.occupant = new BallPhysicsController[total];

            // Gizli kuyruk girişi: yolun en üst noktası (U'da kol tepesi, ovalde üst orta)
            int topIdx = 0;
            for (int i = 1; i < n; i++)
                if (cPts[i].y > cPts[topIdx].y + 1e-4f || (Mathf.Abs(cPts[i].y - cPts[topIdx].y) <= 1e-4f && cPts[i].x < cPts[topIdx].x))
                    topIdx = i;
            loop.entryU = (float)topIdx / n;
            return loop;
        }

        /// <summary>
        /// Şerit ve satır için [0, 1) aralığında merkez eğri parametresi.
        /// Tek sayılı şeritler yarım satır (-0.45) petek kaydırmalıdır; böylece toplar yan yana kare ızgara yerine
        /// gerçek top havuzu gibi çapraz boşluklara (bal집/petek düzeni) oturur.
        /// </summary>
        private static float GetSlotU(BeltLoop loop, int lane, int row)
        {
            int rows = Mathf.Max(1, loop.laneRows[lane]);
            float honeycombStagger = ((lane & 1) != 0) ? -0.45f : 0f;
            return Frac(((float)row + honeycombStagger) / rows + loop.phase);
        }

        /// <summary>
        /// Halkalı pist için başlangıç top yerleşimini bant yuvalarından üretir (faz 0). Böylece level açılırken
        /// toplar tam bant yuvalarında doğar: şeritler petek düzeninde hizalı başlar, iç boşluk ve renk kayması olmaz.
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
                int rows = loop.laneRows[l];
                for (int row = 0; row < rows; row++)
                {
                    // Aynı satırdaki tüm şeritler aynı kesit mesafesini paylaşır ki renk blokları tüm kanal genişliğini eşit doldursun
                    float uCrossSection = (float)row / rows;
                    slots.Add(new GlassTrackLayout.TrackSlot
                    {
                        localPos = SafeSpawnPosition(track, SlotPosition(loop, l, row) + ((UseGranularFlow || UseRigidbodyPhysics) ? GranularSpawnJitter(l, row, track.ballDiameter) : Vector2.zero)),
                        branchId = track.closedLoopBranch,
                        rowIndex = row,
                        laneIndex = l,
                        distToExit = Frac(1f - uCrossSection) * loop.centerLen
                    });
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// Doğma noktası duvara fazla yakınsa kanal içine çeker: gerçek fizikte duvar collider'ına gömülü doğan top
        /// iç içe geçme çözülürken duvarın dışına itilebiliyordu.
        /// </summary>
        private static Vector2 SafeSpawnPosition(GlassTrackLayout.CompiledTrack track, Vector2 p)
        {
            float clear = track.ballDiameter * 0.56f;
            for (int i = 0; i < 3; i++)
            {
                track.SampleField(p, out float sdf, out _, out _, out Vector2 inward);
                float over = sdf + clear;
                if (over <= 0f) break;
                p += inward * over;
            }
            return p;
        }

        private static Vector2 SlotPosition(BeltLoop loop, int lane, int row)
        {
            return SlotPositionAndTangent(loop, lane, row, out _);
        }

        private static Vector2 SlotPositionAndTangent(BeltLoop loop, int lane, int row, out Vector2 tangent)
        {
            float u = GetSlotU(loop, lane, row);
            var cPts = loop.centerPts;
            var cTang = loop.centerTang;
            var cNorm = loop.centerNorm;
            int len = cPts.Length;
            float f = u * len;
            int i0 = Mathf.FloorToInt(f) % len;
            if (i0 < 0) i0 += len;
            int i1 = (i0 + 1) % len;
            float t = f - Mathf.Floor(f);

            Vector2 centerPos = Vector2.Lerp(cPts[i0], cPts[i1], t);
            tangent = Vector2.Lerp(cTang[i0], cTang[i1], t);
            if (tangent.sqrMagnitude > 1e-6f) tangent.Normalize();
            else tangent = Vector2.left;

            Vector2 normal = Vector2.Lerp(cNorm[i0], cNorm[i1], t);
            if (normal.sqrMagnitude > 1e-6f) normal.Normalize();
            else normal = new Vector2(-tangent.y, tangent.x);

            float off = loop.laneOffsets != null && lane < loop.laneOffsets.Length ? loop.laneOffsets[lane] : 0f;
            // Yatay/eğrisel kısımlarda yerçekimi etkisiyle toplar kanalın alt duvarına doğal biçimde yaslanır
            float d = loop.source != null ? loop.source.ballDiameter : 0.16f;
            float gravSag = Mathf.Clamp(-normal.y * d * 0.12f, -d * 0.12f, d * 0.12f);
            return centerPos + normal * (off + gravSag);
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
        private static Vector2[] s_SlotTang = new Vector2[256];
        private static BallPhysicsController[] s_All = new BallPhysicsController[256];
        private static Vector2[] s_Prev = new Vector2[256];
        private static Vector2[] s_Corr = new Vector2[256];
        private static Vector2[] s_Vel = new Vector2[256];
        private static int[] s_CellHead = new int[0];
        private static int s_CachedFrontRow = -1;
        private static GlassTrackLayout.CompiledTrack s_FrontTrackRef;

        /// <summary>
        /// Pisti bir fizik adımı ilerletir. Bir top bile hareket ettiyse true döner.
        /// </summary>
        public static bool Step(ShapedGlassContainer container, List<BallPhysicsController> activeBalls, float dt)
        {
            IsMoving = false;
            if (container == null || activeBalls == null || dt <= 0f) return false;
            RefreshLayerLock();
            if (container.compiledTrack == null) container.RebuildTrackCompiledData();
            var track = container.compiledTrack;
            if (track == null) return false;

            Transform ct = container.transform;
            EnsureCapacity(activeBalls.Count);
            var loop = GetLoop(container);
            float speedMul = GetActiveSpeedMultiplier();
            if (UseRigidbodyPhysics && loop != null) return StepRigidbody(container, activeBalls, dt, loop, track, ct, speedMul);
            if (UseGranularFlow && loop != null) return StepGranular(container, activeBalls, dt, loop, track, ct, speedMul);

            float beltSpeed = BeltSpeed * speedMul;
            float maxSpeed = MaxSpeed * speedMul;
            float maxAccel = Acceleration * speedMul;
            float maxBallSpeed = MaxBallSpeed * speedMul;

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
            float nowTime = Application.isPlaying ? Time.time : 0f;

            // 1. Bantı döndür, geçersiz yuvaları boşalt, yuva konumlarını hesapla
            if (loop != null)
            {
                if (s_FrontTrackRef != track)
                {
                    s_FrontTrackRef = track;
                    s_CachedFrontRow = -1;
                }

                // Bant sadece üzerinde top varken döner; boşken faz 0'a döner ki yeni doğan toplar tam yuvalarına otursun
                bool anyOnBelt = false;
                for (int s = 0; s < loop.totalSlots && !anyOnBelt; s++)
                {
                    var occ = loop.occupant[s];
                    if (occ != null && occ.trackQueueDriven && occ.state == BallState.InContainer && occ.trackLoopSlot == s && occ.gameObject.activeInHierarchy)
                        anyOnBelt = true;
                }

                if (!anyOnBelt) s_CachedFrontRow = -1;
                loop.phase = anyOnBelt ? Frac(loop.phase + beltSpeed * dt / Mathf.Max(0.01f, loop.centerLen)) : 0f;

                if (s_SlotPos.Length < loop.totalSlots)
                {
                    int cap = Mathf.NextPowerOfTwo(loop.totalSlots);
                    s_SlotPos = new Vector2[cap];
                    s_SlotTang = new Vector2[cap];
                }
                for (int l = 0; l < loop.lanes; l++)
                {
                    for (int row = 0; row < loop.laneRows[l]; row++)
                    {
                        int slot = loop.slotBase[l] + row;
                        s_SlotPos[slot] = SlotPositionAndTangent(loop, l, row, out s_SlotTang[slot]);
                        var occ = loop.occupant[slot];
                        if (occ != null && (!occ.trackQueueDriven || occ.state != BallState.InContainer || occ.trackLoopSlot != slot || !occ.gameObject.activeInHierarchy))
                            loop.occupant[slot] = null;
                    }
                }

                // Level başında halka yuvalarında doğmuş topları, kuyruk akışı onları kaydırmadan doğrudan kendi yuvalarına bağla
                if (!anyOnBelt)
                {
                    BindInitialRingBalls(loop, ct, activeBalls, d);
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

            // 2. Pistteki topları topla:
            //    Anizotropik (teğetsel + yumuşak enine) yay-sönümleyici, tutarlı aşağı (-Y) yerçekimi ve ağırlık/momentum
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
                    Vector2 tang = s_SlotTang[b.trackLoopSlot];
                    Vector2 norm = new Vector2(-tang.y, tang.x);
                    Vector2 toTarget = target - p;
                    float gapDist = toTarget.magnitude;

                    // Teğetsel (kanal akışı yönü) ve enine (kanal genişliği yönü) bileşenleri ayır:
                    // Enine yay çok yumuşak (LateralSpringStiffness) tutularak topların çarpışmada doğal yön değiştirmesine,
                    // daralan yerlerde birbirini sıkıştırıp boşluklara yerleşmesine ve yerçekimiyle alt duvara oturmasına izin verilir.
                    float errTang = Vector2.Dot(toTarget, tang);
                    float errNorm = Vector2.Dot(toTarget, norm);

                    // Eğim ve yerçekimi etkisi: yokuş aşağı boşluğa inen top hızlanır, yokuş yukarı çıkan top ağırlığını hissettirir
                    float slopeFactor = 1f;
                    if (gapDist > d * 0.10f)
                    {
                        float dirY = toTarget.y / gapDist;
                        slopeFactor = dirY < 0f
                            ? (1f + (-dirY) * 0.85f)   // yokuş aşağı yerçekimi ivmelenmesi
                            : (1f - dirY * 0.25f);     // yokuş yukarı ağırlık direnci
                    }

                    float kTang = SpringStiffness * slopeFactor;
                    float cTang = 2f * Mathf.Sqrt(kTang) * SpringDampingRatio;
                    float kNorm = LateralSpringStiffness;
                    float cNorm = 2f * Mathf.Sqrt(kNorm) * 0.34f;

                    Vector2 beltVel = tang * beltSpeed;
                    Vector2 relVel = b.trackVel - beltVel;
                    float relVelTang = Vector2.Dot(relVel, tang);
                    float relVelNorm = Vector2.Dot(relVel, norm);

                    Vector2 springAccel = tang * (kTang * errTang - cTang * relVelTang)
                                        + norm * (kNorm * errNorm - cNorm * relVelNorm);

                    // Oyunun perspektifine uygun tutarlı aşağı (-Y) yerçekimi:
                    // 1) Enine (kanal genişliği boyunca) sürekli aşağı doğru çekim -> toplar alt duvara ve alttaki topların üstüne oturur.
                    // 2) Boşluk varsa veya kanal aşağı eğimliyse aşağı doğru akış ivmesi.
                    Vector2 downGrav = new Vector2(0f, -TrackGravity);
                    float latGravComp = Vector2.Dot(downGrav, norm);
                    Vector2 gravAccel = norm * (latGravComp * 0.68f);
                    if (gapDist > d * 0.14f && toTarget.y < -0.001f)
                    {
                        float fallRatio = Mathf.Clamp01(gapDist / (d * 0.75f));
                        gravAccel += new Vector2(0f, -TrackGravity * (0.45f + 0.65f * fallRatio));
                    }
                    else if (tang.y < -0.05f)
                    {
                        gravAccel += tang * ((-tang.y) * TrackGravity * 0.24f);
                    }

                    int idHash = b.GetInstanceID();
                    float p1 = (idHash & 1023) * 0.019f;
                    float p2 = p1 * 1.63f;
                    float crowdAgScale = (s_Occupied <= 3) ? 0.12f : (s_Occupied <= 8 ? 0.45f : 1.0f);
                    Vector2 agitate = new Vector2(
                        Mathf.Sin(nowTime * 5.2f + p1) + 0.6f * Mathf.Cos(nowTime * 7.9f - p2),
                        Mathf.Cos(nowTime * 4.7f + p2) + 0.6f * Mathf.Sin(nowTime * 7.3f + p1)) * (AgitationStrength * crowdAgScale * d * 3.8f);

                    Vector2 accel = springAccel + gravAccel + agitate;
                    Vector2 v = b.trackVel + accel * dt;
                    if (v.sqrMagnitude > maxBallSpeed * maxBallSpeed) v = v.normalized * maxBallSpeed;
                    b.trackVel = v;

                    Vector2 q = p + v * dt;
                    if (Mathf.Abs(errTang) > d * 0.55f) settling = true;
                    InsertIntoGrid(b, q, fr, d, cols, rows, n);
                    s_Pos[n] = q;
                    s_Prev[n] = p;
                    s_Vel[n] = v;
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
                s_Vel[n] = b.trackVel;
                s_Flow[n] = flow;
                s_Keys[n] = exitDist;
                n++;
            }

            // 3. Banda katılmamış toplar (açık kanal veya giriş yolu):
            //    Ağırlık, momentum, tutarlı aşağı (-Y) yerçekimi ve çarpışma yön değişimini koruyarak akar.
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
            float maxStep = maxSpeed * 1.4f * dt;
            for (int k = 0; k < q0; k++)
            {
                int i = s_Order[k];
                var b = s_Balls[i];
                Vector2 p = s_Pos[i];
                Vector2 flow = s_Flow[i];

                // Tutarlı aşağı yerçekimi + kanal akış ivmesi (aniden durmaz, mevcut momentum s_Vel üzerinden devam eder)
                float downhillBoost = flow.y < 0f ? (1f + (-flow.y) * 0.55f) : 0.88f;
                float targetSpd = maxSpeed * downhillBoost;
                Vector2 gravBiasedFlow = (flow + new Vector2(0f, -0.32f)).normalized;
                Vector2 desiredVel = gravBiasedFlow * targetSpd;
                Vector2 vel = Vector2.MoveTowards(s_Vel[i], desiredVel, maxAccel * downhillBoost * dt);
                vel.y -= TrackGravity * 0.35f * dt;
                if (vel.sqrMagnitude > maxBallSpeed * maxBallSpeed) vel = vel.normalized * maxBallSpeed;

                Vector2 q = p + vel * dt;

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
                                    float pen = minDist - dist;
                                    q += dir * (pen * 0.72f);

                                    // Hareket eden kuyruk topu çarptığı topu hafifçe iter ve kendisi doğal açıyla yön değiştirir
                                    float vn = Vector2.Dot(vel - s_Vel[j], dir);
                                    if (vn < 0f)
                                    {
                                        Vector2 pushImp = dir * (-vn * 0.38f);
                                        vel += pushImp * (1f + BallRestitution * 0.5f);
                                        s_Vel[j] -= pushImp * 0.55f;
                                    }
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
                        float vn = Vector2.Dot(vel, inward);
                        if (vn < 0f) vel -= (1f + WallRestitution) * vn * inward;
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

                // Momentum koruma: aniden sıfırlamak yerine gerçek hızla yumuşak geçiş yap
                Vector2 actualStepVel = delta / dt;
                vel = Vector2.Lerp(vel, actualStepVel, 0.48f);
                b.trackVel = vel;
                b.trackQueueSpeed = vel.magnitude;

                if (delta != Vector2.zero) settling = true;
                s_Pos[i] = q;
                s_Vel[i] = vel;
                InsertIntoGrid(b, q, fr, d, cols, rows, i);

                // Önünden boş bir bant yuvası geçiyorsa (ve bantta yer varsa) banda katıl
                if (loop != null && CanJoinBelt(s_Occupied) && TryJoinBelt(loop, b, q, d)) s_Occupied++;
            }
            RoadQueueCount = q0;

            // Çıkışa çekilmekte olan (AttractingToExit) toplar kanaldan çıkarken yanındaki topları hafifçe iter
            ApplyExitingBallsPush(ct, activeBalls, n, d);

            // 4. Fizik: gerçek 2B küre çarpışması (yön değiştirme, komşu itme kuvveti, dar alanda sıkışma/boşluğa yerleşme ve kontrollü sekme)
            SolveContacts(track, loop, n, d, fr, cols, rows);
            float inertiaDecay = Mathf.Exp(-1.15f * dt);
            for (int i = 0; i < n; i++)
            {
                var b = s_All[i];
                if (b == null) continue;
                Vector2 p = s_Prev[i];
                Vector2 q = s_Pos[i];

                // Çarpışma sonrası hız vektörünü (s_Vel) koru ve sıkışma kaynaklı yer değiştirmeyi momentuma ekle:
                // böylece toplar aniden durmaz, sekme ve yön değişimini doğal ağırlıkla tamamlar.
                Vector2 squeezeVel = s_Corr[i] * (0.34f / dt);
                Vector2 v = (s_Vel[i] + squeezeVel) * inertiaDecay;
                if (v.sqrMagnitude > maxBallSpeed * maxBallSpeed) v = v.normalized * maxBallSpeed;
                b.trackVel = v;

                float hit = s_Corr[i].magnitude / dt;
                if (hit > 0.15f)
                {
                    Vector3 axis = ct.TransformDirection(new Vector3(s_Corr[i].x, s_Corr[i].y, 0f));
                    b.PlayTrackBump(axis, Mathf.Clamp(hit * 0.044f, 0.032f, 0.095f));
                }
                if (ApplyMove(b, ct, p, q, dt, nowTime)) anyMoved = true;
            }

            // Giriş yolunun tepesi boşsa kuyruktaki sıradaki top orada belirir ve aşağı akar
            if (loop != null && loop.hasRoad && s_Pending.Count > 0 && Application.isPlaying && Time.time >= s_NextRoadSpawn)
            {
                float clearSq = (d * 1.05f) * (d * 1.05f);
                bool topFree = true;
                for (int i = 0; i < n && topFree; i++) if ((s_Pos[i] - loop.roadTop).sqrMagnitude < clearSq) topFree = false;
                if (topFree && SpawnQueuedBall(container, loop.roadTop) != null)
                {
                    s_NextRoadSpawn = Time.time + 0.06f / speedMul;
                    settling = true;
                }
            }

            Array.Clear(s_Balls, 0, n);
            Array.Clear(s_All, 0, n);
            IsMoving = settling;
            return anyMoved;
        }

        /// <summary>
        /// Çıkış ağzına doğru çekilen (AttractingToExit) bir top hareket ederken yakınındaki topları hafifçe yana/yukarı iter.
        /// </summary>
        private static void ApplyExitingBallsPush(Transform ct, List<BallPhysicsController> activeBalls, int n, float d)
        {
            if (n <= 0) return;
            float pushRad = d * 1.12f;
            float pushRadSq = pushRad * pushRad;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var ex = activeBalls[i];
                if (ex == null || ex.state != BallState.AttractingToExit || !ex.gameObject.activeInHierarchy) continue;
                Vector2 exLocal = ct.InverseTransformPoint(ex.transform.position);
                for (int j = 0; j < n; j++)
                {
                    if (s_All[j] == null) continue;
                    Vector2 diff = s_Pos[j] - exLocal;
                    float dSq = diff.sqrMagnitude;
                    if (dSq > 1e-6f && dSq < pushRadSq)
                    {
                        float dist = Mathf.Sqrt(dSq);
                        Vector2 dir = diff / dist;
                        float strength = (1f - dist / pushRad);
                        // Yana doğru hafif açılma itmesi
                        Vector2 lateralDir = new Vector2(dir.x, Mathf.Max(-0.15f, dir.y * 0.35f));
                        if (lateralDir.sqrMagnitude > 1e-4f) lateralDir.Normalize();
                        s_Vel[j] += lateralDir * (0.38f * strength);
                    }
                }
            }
        }

        /// <summary>
        /// Level başında halka üzerinde doğan topları en yakın boş bant yuvalarına birebir oturtur.
        /// Böylece ilk karede kuyruk akışı topları kaydırıp şeritler arası yuva çalmasına yol açmaz.
        /// </summary>
        private static void BindInitialRingBalls(BeltLoop loop, Transform ct, List<BallPhysicsController> activeBalls, float d)
        {
            float maxSnapSq = (d * 0.52f) * (d * 0.52f);
            int occupied = 0;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || !b.trackQueueDriven || b.state != BallState.InContainer || b.rb == null || !b.gameObject.activeInHierarchy)
                    continue;
                if (b.trackLoopSlot >= 0) continue;
                if (!CanJoinBelt(occupied)) break;

                if (!b.trackQueuePosValid)
                {
                    Vector3 lp = ct.InverseTransformPoint(b.rb.position);
                    b.trackQueueLocalPos = new Vector2(lp.x, lp.y);
                    b.trackQueueLocalZ = lp.z;
                    b.trackQueuePosValid = true;
                }

                Vector2 p = b.trackQueueLocalPos;
                float bestSq = maxSnapSq;
                int bestSlot = -1;
                for (int s = 0; s < loop.totalSlots; s++)
                {
                    if (loop.occupant[s] != null) continue;
                    float dSq = (s_SlotPos[s] - p).sqrMagnitude;
                    if (dSq < bestSq)
                    {
                        bestSq = dSq;
                        bestSlot = s;
                    }
                }
                if (bestSlot >= 0)
                {
                    loop.occupant[bestSlot] = b;
                    b.trackLoopSlot = bestSlot;
                    b.trackQueueSpeed = 0f;
                    occupied++;
                }
            }
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
            s_CachedFrontRow = -1;
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
            if (UseGranularFlow || UseRigidbodyPhysics) return CanJoinBelt(s_RingCount);
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
                float u = GetSlotU(loop, l, row);
                float du = Mathf.Abs(u - loop.entryU);
                du = Mathf.Min(du, 1f - du);
                if (du * loop.centerLen > d * 0.55f) continue;

                var ball = SpawnQueuedBall(container, SlotPosition(loop, l, row));
                if (ball == null) return;
                loop.occupant[slot] = ball;
                ball.trackLoopSlot = slot;
                added++;
                BallCollectionController.Instance?.MarkBallFieldDirty();
            }
        }

        private static bool IsCrossSectionRowOccupied(BeltLoop loop, int row)
        {
            for (int l = 0; l < loop.lanes; l++)
            {
                if (loop.occupant[loop.slotBase[l] + row] != null) return true;
            }
            return false;
        }

        private static bool HasAnyBallBehindInLane(BeltLoop loop, int lane, int startStep, int frontRow, int R)
        {
            int bas = loop.slotBase[lane];
            for (int st = startStep; st < R; st++)
            {
                int r = ((frontRow - st) % R + R) % R;
                if (loop.occupant[bas + r] != null) return true;
            }
            return false;
        }

        private static bool IsBallLongitudinallySettled(BallPhysicsController b, int slot, float maxTangDist, float maxTotalDistSq)
        {
            if (b == null || !b.trackQueuePosValid) return false;
            Vector2 diff = s_SlotPos[slot] - b.trackQueueLocalPos;
            if (diff.sqrMagnitude > maxTotalDistSq) return false;
            float tangDist = Mathf.Abs(Vector2.Dot(diff, s_SlotTang[slot]));
            return tangDist <= maxTangDist;
        }

        private static bool IsAheadSlotPhysicallyClear(BeltLoop loop, int targetLane, int targetRow, float maxAheadTangDist)
        {
            int R = loop.rowsAround;
            int aheadRow = (targetRow + 1) % R;
            int aheadSlot = loop.slotBase[targetLane] + aheadRow;
            var aheadBall = loop.occupant[aheadSlot];
            if (aheadBall == null || !aheadBall.trackQueuePosValid) return true;
            Vector2 diff = s_SlotPos[aheadSlot] - aheadBall.trackQueueLocalPos;
            return Mathf.Abs(Vector2.Dot(diff, s_SlotTang[aheadSlot])) <= maxAheadTangDist;
        }

        /// <summary>
        /// Çarpışma, sıkışma veya yerçekimi etkisiyle yan şerit tarafına savrulan topların yuva atamasını
        /// yeni fiziksel konumlarına uyarlar. Böylece çarpışıp yön değiştiren top eski şeridine lastik gibi
        /// geri çekilmez, yön değiştirdiği yeni boşluğa doğal biçimde yerleşir.
        /// </summary>
        private static void AdaptLateralLaneAssignments(BeltLoop loop, float d)
        {
            int R = loop.rowsAround;
            int L = loop.lanes;
            if (L < 2) return;
            float switchMarginSq = (d * 0.14f) * (d * 0.14f);

            for (int r = 0; r < R; r++)
            {
                for (int l = 0; l < L - 1; l++)
                {
                    int s0 = loop.slotBase[l] + r;
                    int s1 = loop.slotBase[l + 1] + r;
                    var b0 = loop.occupant[s0];
                    var b1 = loop.occupant[s1];

                    if (b0 != null && b1 == null && b0.trackQueuePosValid)
                    {
                        float d0 = (b0.trackQueueLocalPos - s_SlotPos[s0]).sqrMagnitude;
                        float d1 = (b0.trackQueueLocalPos - s_SlotPos[s1]).sqrMagnitude;
                        if (d1 + switchMarginSq < d0)
                        {
                            loop.occupant[s0] = null;
                            loop.occupant[s1] = b0;
                            b0.trackLoopSlot = s1;
                        }
                    }
                    else if (b1 != null && b0 == null && b1.trackQueuePosValid)
                    {
                        float d1 = (b1.trackQueueLocalPos - s_SlotPos[s1]).sqrMagnitude;
                        float d0 = (b1.trackQueueLocalPos - s_SlotPos[s0]).sqrMagnitude;
                        if (d0 + switchMarginSq < d1)
                        {
                            loop.occupant[s1] = null;
                            loop.occupant[s0] = b1;
                            b1.trackLoopSlot = s0;
                        }
                    }
                    else if (b0 != null && b1 != null && b0.trackQueuePosValid && b1.trackQueuePosValid && b0.colorType == b1.colorType)
                    {
                        float curCost = (b0.trackQueueLocalPos - s_SlotPos[s0]).sqrMagnitude + (b1.trackQueueLocalPos - s_SlotPos[s1]).sqrMagnitude;
                        float swapCost = (b0.trackQueueLocalPos - s_SlotPos[s1]).sqrMagnitude + (b1.trackQueueLocalPos - s_SlotPos[s0]).sqrMagnitude;
                        if (swapCost + switchMarginSq < curCost)
                        {
                            loop.occupant[s0] = b1;
                            loop.occupant[s1] = b0;
                            b1.trackLoopSlot = s0;
                            b0.trackLoopSlot = s1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 2B Petek Top Havuzu Boşluk Kapatma ve Kümeleme:
        /// - Tüm şeritler tek bir ortak kalabalık ön kesitini (s_CachedFrontRow) paylaşır; böylece çıkıştan top
        ///   toplandığında şeritler birbirinden kopmaz ve iç/dış şerit farklı yönlere ayrılmaz.
        /// - Öndeki top harekete geçip fiziksel olarak yer açtıkça arkadaki top harekete geçer (domino dalga yayılımı).
        /// - Bir şeridin önünde veya kuyruğunda tek sıra halinde taşan toplar, yan şeritteki çapraz boş petek
        ///   yuvalarına yuvarlanarak tüm kanal genişliğini gerçek bir top havuzu gibi dengeli doldurur.
        /// </summary>
        private static void CloseBeltGaps(BeltLoop loop, float d)
        {
            int R = loop.rowsAround;
            int L = loop.lanes;
            if (R < 3) return;

            int totalOccupied = 0;
            for (int s = 0; s < loop.totalSlots; s++)
            {
                if (loop.occupant[s] != null) totalOccupied++;
            }
            if (totalOccupied <= 0 || totalOccupied >= loop.totalSlots)
            {
                if (totalOccupied >= loop.totalSlots) s_CachedFrontRow = -1;
                return;
            }

            // Çarpışma veya yerçekimi ile yan şeride kayan topların yuva atamasını doğal konumlarına uyarla
            if (totalOccupied > 2)
            {
                AdaptLateralLaneAssignments(loop, d);
            }

            // 1. Ortak ön kesit satırını (s_CachedFrontRow) belirle veya koru
            if (s_CachedFrontRow < 0 || s_CachedFrontRow >= R)
            {
                int bestFront = -1;
                int bestScore = -1;
                for (int r = 0; r < R; r++)
                {
                    if (!IsCrossSectionRowOccupied(loop, r)) continue;
                    int score = 0;
                    for (int k = 1; k < R; k++)
                    {
                        int nr = (r + k) % R;
                        int occInRow = 0;
                        for (int l = 0; l < L; l++)
                        {
                            if (loop.occupant[loop.slotBase[l] + nr] != null) occInRow++;
                        }
                        if (occInRow == 0) score += 10;
                        else if (occInRow < L && k == 1) score += 2;
                        else break;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestFront = r;
                    }
                }
                s_CachedFrontRow = bestFront >= 0 ? bestFront : 0;
            }
            else
            {
                // En ön satırdaki tüm toplar toplandıysa ön sınırı bir arka dolu kesite çek
                int guard = 0;
                while (!IsCrossSectionRowOccupied(loop, s_CachedFrontRow) && guard < R)
                {
                    s_CachedFrontRow = (s_CachedFrontRow - 1 + R) % R;
                    guard++;
                }
            }

            int frontRow = s_CachedFrontRow;
            float maxTangSettled = d * 0.58f;
            float maxTotalSettledSq = (d * 1.28f) * (d * 1.28f);
            float dominoTangClear = d * 0.76f;

            // 2. Ön Uç Dengeleme (Front Leveling):
            // Yalnızca hemen arkasında kalabalık varken tek başına taşan ön topu yan cebin arkasına oturt;
            // tek/son kalan topu boşluğa doğru geriye yürütme (sonsuz geri sekme döngüsünü önler).
            if (L > 1 && totalOccupied > 1)
            {
                int prevFrontRow = (frontRow - 1 + R) % R;
                if (IsCrossSectionRowOccupied(loop, prevFrontRow))
                {
                    for (int l = 0; l < L; l++)
                    {
                        int frontSlot = loop.slotBase[l] + frontRow;
                        var fb = loop.occupant[frontSlot];
                        if (!IsBallLongitudinallySettled(fb, frontSlot, maxTangSettled, maxTotalSettledSq)) continue;

                        for (int dl = -1; dl <= 1; dl += 2)
                        {
                            int adjL = l + dl;
                            if (adjL < 0 || adjL >= L) continue;
                            int adjFrontSlot = loop.slotBase[adjL] + frontRow;
                            int adjPrevSlot = loop.slotBase[adjL] + prevFrontRow;
                            if (loop.occupant[adjFrontSlot] == null && loop.occupant[adjPrevSlot] == null)
                            {
                                loop.occupant[frontSlot] = null;
                                loop.occupant[adjPrevSlot] = fb;
                                fb.trackLoopSlot = adjPrevSlot;
                                break;
                            }
                        }
                    }
                    if (!IsCrossSectionRowOccupied(loop, frontRow))
                    {
                        int guard = 0;
                        while (!IsCrossSectionRowOccupied(loop, s_CachedFrontRow) && guard < R)
                        {
                            s_CachedFrontRow = (s_CachedFrontRow - 1 + R) % R;
                            guard++;
                        }
                        frontRow = s_CachedFrontRow;
                    }
                }
            }

            // 3. Önden arkaya dalgalı ilerleme + Kuyrukta çapraz şerit doldurma (top havuzu toplanması)
            for (int step = 1; step < R; step++)
            {
                int row = ((frontRow - step) % R + R) % R;
                int nextRow = (row + 1) % R;

                for (int l = 0; l < L; l++)
                {
                    int slot = loop.slotBase[l] + row;
                    var b = loop.occupant[slot];
                    if (!IsBallLongitudinallySettled(b, slot, maxTangSettled, maxTotalSettledSq)) continue;

                    // Öncelik 1: Kendi şeridinde bir ön yuva boşsa ve öndeki top fiziksel olarak yer açtıysa düz ilerle
                    int sameLaneNext = loop.slotBase[l] + nextRow;
                    if (loop.occupant[sameLaneNext] == null)
                    {
                        if (step == 1 || IsAheadSlotPhysicallyClear(loop, l, nextRow, dominoTangClear))
                        {
                            loop.occupant[slot] = null;
                            loop.occupant[sameLaneNext] = b;
                            b.trackLoopSlot = sameLaneNext;
                        }
                        continue;
                    }

                    // Öncelik 2: Kendi önü doluysa ama komşu şeridin ön çapraz yuvası boşsa VE komşu şeritte arkadan gelen
                    // başka top kalmadıysa (kuyruk tek şeritte uzadıysa), çapraz petek yuvasına yuvarlanıp kümeyi topla!
                    if (L > 1)
                    {
                        for (int dl = -1; dl <= 1; dl += 2)
                        {
                            int adjL = l + dl;
                            if (adjL < 0 || adjL >= L) continue;
                            int adjNextSlot = loop.slotBase[adjL] + nextRow;
                            if (loop.occupant[adjNextSlot] != null) continue;
                            if (HasAnyBallBehindInLane(loop, adjL, step, frontRow, R)) continue;
                            if (step > 1 && !IsAheadSlotPhysicallyClear(loop, adjL, nextRow, dominoTangClear)) continue;

                            loop.occupant[slot] = null;
                            loop.occupant[adjNextSlot] = b;
                            b.trackLoopSlot = adjNextSlot;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Toplar arası gerçek 2B fiziksel etkileşim:
        /// 1) İtme kuvveti: hareket eden bir top yaklaşırken çevresindeki topları hafifçe iter.
        /// 2) Gerçek çarpışma ve yön değiştirme: çarpışan toplar temas açısına göre doğal yön değiştirir.
        /// 3) Sıkışma: daralan alanlarda toplar birbirini sıkıştırır ve kama etkisiyle boşluklara doğal yerleşir.
        /// 4) Sekme: çarpışmalarda ve cam duvarlarda hafif, kontrollü sekmeler oluşur.
        /// </summary>
        private static void SolveContacts(GlassTrackLayout.CompiledTrack track, BeltLoop loop, int n, float d, Rect fr, int cols, int rows)
        {
            float contact = d * ContactRatio;
            float contactSq = contact * contact;
            float pushDist = d * PushRadiusRatio;
            float pushDistSq = pushDist * pushDist;
            float wallClear = d * 0.5f * 1.02f;
            for (int i = 0; i < n; i++) s_Corr[i] = Vector2.zero;

            for (int iter = 0; iter < 4; iter++)
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

                                    // 1. Temas öncesi yakınlık itmesi: hareket eden top komşularını hafifçe iter
                                    if (iter == 0 && dSq >= contactSq && dSq < pushDistSq)
                                    {
                                        float dist = Mathf.Sqrt(dSq);
                                        Vector2 dir = dist > 1e-5f ? diff / dist : new Vector2(0f, 1f);
                                        Vector2 relVel = s_Vel[i] - s_Vel[j];
                                        float vn = Vector2.Dot(relVel, dir);
                                        if (vn < -0.04f)
                                        {
                                            float proximity = 1f - (dist - contact) / Mathf.Max(0.001f, pushDist - contact);
                                            float spdI = s_Vel[i].magnitude;
                                            float spdJ = s_Vel[j].magnitude;
                                            float weightToJ = Mathf.Lerp(0.5f, 0.78f, (spdI - spdJ) / (spdI + spdJ + 0.15f));
                                            float weightToI = 1f - weightToJ;
                                            Vector2 gentlePush = dir * (-vn * 0.24f * proximity);
                                            s_Vel[i] += gentlePush * weightToI;
                                            s_Vel[j] -= gentlePush * weightToJ;
                                        }
                                    }
                                    // 2. Doğrudan temas, sıkışma (boşluklara kama yerleşimi), yön değiştirme ve kontrollü sekme
                                    else if (dSq < contactSq)
                                    {
                                        float dist = Mathf.Sqrt(dSq);
                                        Vector2 dir = dist > 1e-5f ? diff / dist : new Vector2(0f, 1f);
                                        float pen = contact - dist;

                                        // Hız/momentum ağırlıklı itme: hızlı hareket eden top duran/yavaş topu daha fazla iter
                                        float spdI = s_Vel[i].magnitude;
                                        float spdJ = s_Vel[j].magnitude;
                                        float wJ = Mathf.Lerp(0.5f, 0.72f, (spdI - spdJ) / (spdI + spdJ + 0.12f));
                                        float wI = 1f - wJ;

                                        Vector2 push = dir * (pen * ContactStiffness);
                                        s_Pos[i] += push * wI; s_Corr[i] += push * wI;
                                        s_Pos[j] -= push * wJ; s_Corr[j] -= push * wJ;

                                        // Sıkışma (Wedging): iki top birbirini sıkıştırdığında yanlardaki boşluklara ve
                                        // yerçekimi (-Y) yönüne doğru kama etkisiyle doğal biçimde kayıp yerleşir
                                        Vector2 tang = new Vector2(-dir.y, dir.x);
                                        if (tang.y > 0f) tang = -tang; // yerçekimi (-Y) yönündeki boşluğu tercih et
                                        float wedgeDisp = pen * 0.18f * ContactStiffness;
                                        float sideSign = ((i + j) & 1) == 0 ? 1f : -1f;
                                        Vector2 wedgeVec = tang * (wedgeDisp * (Mathf.Abs(tang.y) > 0.25f ? 1f : sideSign));
                                        s_Pos[i] += wedgeVec * 0.5f;
                                        s_Pos[j] -= wedgeVec * 0.5f;

                                        // İlk iterasyonda: gerçek çarpışma yön değişimi + hafif ve kontrollü sekme
                                        if (iter == 0)
                                        {
                                            Vector2 relVel = s_Vel[i] - s_Vel[j];
                                            float vn = Vector2.Dot(relVel, dir);
                                            if (vn < -0.015f)
                                            {
                                                // Kontrollü sekme: çok düşük hızlarda yumuşak, çarpışmalarda net ama aşırıya kaçmayan sekme
                                                float effRestitution = BallRestitution * Mathf.Clamp01((-vn - 0.015f) / 0.28f);
                                                float bounceMag = Mathf.Min(-vn * effRestitution, 0.95f);
                                                float totalNormalDelta = -vn + bounceMag;

                                                Vector2 normalImp = dir * totalNormalDelta;
                                                // Çarpışma açısına göre doğal yön değiştirme (teğetsel sapma + petek boşluğuna yönelme)
                                                float vt = Vector2.Dot(relVel, tang);
                                                float deflectMag = Mathf.Clamp(totalNormalDelta * 0.24f, 0f, 0.55f);
                                                Vector2 deflectDir = Mathf.Abs(vt) > 0.03f ? (tang * Mathf.Sign(vt)) : (tang * sideSign);
                                                Vector2 totalImp = normalImp + deflectDir * deflectMag;

                                                s_Vel[i] += totalImp * wI;
                                                s_Vel[j] -= totalImp * wJ;
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
                    var b = s_All[i];
                    if (b == null) continue;
                    track.SampleField(s_Pos[i], out float sdf, out _, out _, out Vector2 inward);
                    float over = sdf + wallClear;
                    if (over > 0f && sdf <= 0.05f)
                    {
                        Vector2 push = inward * over;
                        s_Pos[i] += push; s_Corr[i] += push;

                        float vn = Vector2.Dot(s_Vel[i], inward);
                        if (vn < 0f)
                        {
                            // Duvarla çarpışmada hafif kontrollü sekme + duvar eğrisi boyunca doğal yön değiştirerek kayma
                            float effWallRest = WallRestitution * Mathf.Clamp01((-vn - 0.02f) / 0.30f);
                            s_Vel[i] -= (1f + effWallRest) * vn * inward;
                        }
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

        private static bool ApplyMove(BallPhysicsController b, Transform ct, Vector2 p, Vector2 q, float dt, float nowTime)
        {
            Vector2 delta = q - p;
            bool movedPos = delta.sqrMagnitude >= MinMoveSq;
            if (movedPos)
            {
                b.trackQueueLocalPos = q;
                b.rb.MovePosition(ct.TransformPoint(new Vector3(q.x, q.y, b.trackQueueLocalZ)));
            }

            // 3B fiziksel yuvarlanma + top havuzu canlı dönüşü
            Vector3 worldDelta = ct.TransformVector(new Vector3(delta.x, delta.y, 0f));
            float dist = worldDelta.magnitude;
            int idHash = b.GetInstanceID();
            float phase = (idHash & 1023) * 0.024f;
            Vector3 ambientSpinAxis = new Vector3(
                Mathf.Cos(nowTime * 3.1f + phase),
                Mathf.Sin(nowTime * 3.6f + phase * 1.4f),
                Mathf.Sin(nowTime * 2.5f - phase) * 0.5f).normalized;
            float ambientDeg = 46f * dt;

            if (dist > 1e-5f)
            {
                Vector3 rollAxis = Vector3.Cross(Vector3.back, worldDelta / dist);
                float rollDeg = (dist / Mathf.Max(0.02f, b.ballRadius)) * Mathf.Rad2Deg * 1.25f;
                Quaternion rot = Quaternion.AngleAxis(rollDeg, rollAxis) * Quaternion.AngleAxis(ambientDeg, ambientSpinAxis) * b.rb.rotation;
                b.rb.MoveRotation(rot);
            }
            else if (Application.isPlaying)
            {
                b.rb.MoveRotation(Quaternion.AngleAxis(ambientDeg * 0.65f, ambientSpinAxis) * b.rb.rotation);
            }
            return movedPos;
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
            s_Vel = new Vector2[cap];
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
