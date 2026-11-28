using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Yapışık top kümeleri. Tasarımcının Cam Stüdyosu'nda çizdiği şekil (gülen yüz, kalp, harf…) banda
    /// tek parça olarak yerleşir ve <b>bozulmadan</b> döner: kümedeki hiçbir top toplanamaz, yuvası değişmez,
    /// aralarına başka top giremez.
    ///
    /// Bant (şerit x enine sıra) ızgarasıdır ve bütün şeritler aynı oranda döner, bu yüzden yuvaları sabit
    /// kalan topların birbirine göre konumu hiç değişmez — şekil kendiliğinden korunur. Yuva kilidi
    /// TrackCrowdQueue tarafında <c>IsStuckInCluster</c> ile uygulanır.
    ///
    /// Çözülme koşulu: seçilen renkte yeterli sayıda kutu tamamlanınca yapışkanlık kalkar ve toplar normal
    /// bant topu olur. Oyuncunun kararı bu yüzden "önce hangi kutuyu bitirmeliyim" olur.
    /// </summary>
    public static class BallClusters
    {
        private sealed class RuntimeCluster
        {
            public BallClusterShape shape;
            public bool assigned;
            public bool dissolved;
            /// <summary>Kümenin halka üzerindeki fazı (0..1); her fizik adımında bant hızıyla ilerler.</summary>
            public float phase;
            public int completedBoxes;
            public readonly List<BallPhysicsController> members = new List<BallPhysicsController>();
        }

        private static List<BallClusterShape> s_Config;
        private static int s_ConfigHash;
        private static readonly List<RuntimeCluster> s_Clusters = new List<RuntimeCluster>(2);

        public static int ActiveClusterCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < s_Clusters.Count; i++) if (!s_Clusters[i].dissolved) n++;
                return n;
            }
        }

        public static void Invalidate()
        {
            for (int i = 0; i < s_Clusters.Count; i++) ReleaseMembers(s_Clusters[i]);
            s_Clusters.Clear();
            s_Config = null;
            s_ConfigHash = 0;
        }

        // ---------------------------------------------------------------- kurallar

        /// <summary>Kümenin halka fazı (0..1). Burst adımı bunu bant hızıyla ilerletir.</summary>
        internal static bool TryGetClusterPhase(int id, out float phase)
        {
            phase = 0f;
            if (id < 0 || id >= s_Clusters.Count) return false;
            var rc = s_Clusters[id];
            if (!rc.assigned || rc.dissolved) return false;
            phase = rc.phase;
            return true;
        }

        internal static void SetClusterPhase(int id, float phase)
        {
            if (id < 0 || id >= s_Clusters.Count) return;
            s_Clusters[id].phase = phase;
        }

        /// <summary>Top banttan ayrılabilir mi? Yapışık kümedeki toplar çözülene kadar ayrılamaz.</summary>
        public static bool IsReleased(BallPhysicsController ball)
        {
            return ball != null && !ball.IsStuckInCluster;
        }

        /// <summary>Bir kutu tamamlandı: koşulu sağlayan kümeler çözülür.</summary>
        public static void NotifyBoxCompleted(PuzzleColor boxColor)
        {
            for (int i = 0; i < s_Clusters.Count; i++)
            {
                var rc = s_Clusters[i];
                if (rc.dissolved || rc.shape == null) continue;
                if (rc.shape.unlockBoxColor != boxColor) continue;

                rc.completedBoxes++;
                if (rc.completedBoxes < Mathf.Max(1, rc.shape.unlockAfterBoxes)) continue;

                Dissolve(rc);
            }
        }

        private static void Dissolve(RuntimeCluster rc)
        {
            rc.dissolved = true;
            ReleaseMembers(rc);
        }

        private static void ReleaseMembers(RuntimeCluster rc)
        {
            for (int i = 0; i < rc.members.Count; i++)
            {
                var b = rc.members[i];
                if (b == null) continue;
                b.clusterId = -1;
                b.clusterCell = -1;
                // Çözülünce top hafifçe sarsılsın: oyuncu yapışkanlığın kalktığını görsün
                b.PlayTrackBump(Vector3.up, 0.45f);
            }
            rc.members.Clear();
        }

        // ---------------------------------------------------------------- adım

        public static void Step(ShapedGlassContainer container, List<BallPhysicsController> balls)
        {
            if (container == null || balls == null) return;

            EnsureConfig(container);
            if (s_Clusters.Count == 0) return;
            if (!TrackCrowdQueue.TryGetRingInfo(container, out float ballDiameter, out float innerHalfWidth)) return;

            for (int i = 0; i < s_Clusters.Count; i++)
            {
                var rc = s_Clusters[i];
                if (rc.dissolved) continue;
                if (!rc.assigned) TryAssign(container, rc, i, balls, ballDiameter, innerHalfWidth);
                else PruneMembers(rc);
            }
        }

        private static int ComputeHash(List<BallClusterShape> cfg)
        {
            int h = 23;
            if (cfg == null) return h;
            for (int i = 0; i < cfg.Count; i++)
            {
                var c = cfg[i];
                if (c == null) continue;
                h = h * 31 + (c.enabled ? 1 : 0);
                h = h * 31 + c.cols * 17 + c.rows;
                h = h * 31 + c.positionAlongBelt.GetHashCode();
                h = h * 31 + (int)c.unlockBoxColor * 13 + c.unlockAfterBoxes;
                if (c.cells != null)
                    for (int k = 0; k < c.cells.Count; k++) h = h * 31 + c.cells[k];
            }
            return h;
        }

        private static void EnsureConfig(ShapedGlassContainer container)
        {
            var cfg = container.ballClusters;
            int hash = ComputeHash(cfg);
            if (s_Config == cfg && s_ConfigHash == hash) return;

            for (int i = 0; i < s_Clusters.Count; i++) ReleaseMembers(s_Clusters[i]);
            s_Clusters.Clear();
            s_Config = cfg;
            s_ConfigHash = hash;
            if (cfg == null) return;

            for (int i = 0; i < cfg.Count; i++)
            {
                var c = cfg[i];
                if (c == null || !c.enabled) continue;
                c.EnsureCellBuffer();
                if (c.FilledCount <= 0) continue;
                s_Clusters.Add(new RuntimeCluster { shape = c });
            }
        }

        /// <summary>
        /// Şekli banda oturtur. Halka orta çizgisinde seçilen çapadan hücre hedefleri hesaplanır; her hedef
        /// için o renkten, hedefe en yakın serbest top seçilir ve oraya oturtulur. Yeni top eklenmez,
        /// sadece bandın içindeki toplar yer değiştirir — level'in renk dengesi bozulmaz.
        /// </summary>
        private static void TryAssign(
            ShapedGlassContainer container, RuntimeCluster rc, int clusterIndex,
            List<BallPhysicsController> balls, float ballDiameter, float innerHalfWidth)
        {
            var sh = rc.shape;
            if (!TrackCrowdQueue.TryGetRingPointAt(container, Mathf.Clamp01(sh.positionAlongBelt),
                                                   out Vector2 anchor, out Vector2 tang, out Vector2 nrm))
                return;

            // Bantta yeterince top dönmeye başlayana kadar bekle
            int onRing = 0;
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b != null && b.gameObject.activeInHierarchy && b.state == BallState.InContainer && b.trackLoopSlot >= 0)
                    onRing++;
            }
            if (onRing < sh.FilledCount * 2) return;

            float d = Mathf.Max(0.02f, ballDiameter);
            float acrossMax = Mathf.Max(0f, innerHalfWidth - d * 0.5f);
            float colCenter = (sh.cols - 1) * 0.5f;
            float rowCenter = (sh.rows - 1) * 0.5f;

            s_Taken.Clear();
            rc.members.Clear();

            for (int r = 0; r < sh.rows; r++)
            {
                for (int c = 0; c < sh.cols; c++)
                {
                    int v = sh.GetCell(c, r);
                    if (v < 0) continue;
                    var want = (PuzzleColor)v;

                    float across = Mathf.Clamp((c - colCenter) * d, -acrossMax, acrossMax);
                    float along = (rowCenter - r) * d;
                    Vector2 target = anchor + nrm * across + tang * along;

                    var pick = FindNearestFreeBall(balls, want, target, true);
                    if (pick == null) pick = FindNearestFreeBall(balls, want, target, false);
                    if (pick == null) continue;

                    s_Taken.Add(pick);
                    pick.clusterId = clusterIndex;
                    pick.clusterCell = r * sh.cols + c;
                    pick.clusterOffset = new Vector2(across, along);
                    // Şekil anında otursun: ilk karede yerine ışınlanır, sonrası rijit tutulur
                    pick.trackQueueLocalPos = target;
                    pick.trackQueuePosValid = true;
                    pick.trackVel = Vector2.zero;
                    rc.members.Add(pick);
                }
            }

            // Hiç top bulunamadıysa kümeyi kurmayı sonraki adıma bırak
            if (rc.members.Count < Mathf.Max(2, sh.FilledCount / 2))
            {
                for (int i = 0; i < rc.members.Count; i++)
                {
                    rc.members[i].clusterId = -1;
                    rc.members[i].clusterCell = -1;
                }
                rc.members.Clear();
                return;
            }
            rc.phase = Mathf.Repeat(sh.positionAlongBelt, 1f);
            rc.assigned = true;
        }

        private static readonly HashSet<BallPhysicsController> s_Taken = new HashSet<BallPhysicsController>();

        /// <summary>Hedefe en yakın, kümeye alınmamış, bantta dönen top. matchColor=false ise rengi önemsenmez.</summary>
        private static BallPhysicsController FindNearestFreeBall(
            List<BallPhysicsController> balls, PuzzleColor want, Vector2 target, bool matchColor)
        {
            BallPhysicsController best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (b.state != BallState.InContainer || b.trackLoopSlot < 0) continue;
                if (b.IsStuckInCluster || s_Taken.Contains(b)) continue;
                if (matchColor && b.colorType != want) continue;
                if (!b.trackQueuePosValid) continue;

                float sq = (b.trackQueueLocalPos - target).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = b; }
            }
            return best;
        }

        /// <summary>
        /// Havuza dönmüş / banttan çıkmış üyeleri ayıklar. Küme topları çözülmeden toplanamadığı için üye
        /// kaybı yalnızca level açılışında (toplar yeniden kurulurken) olur: o yüzden küme "çözüldü" sayılmaz,
        /// <b>yeniden kurulmak üzere</b> serbest bırakılır. Çözülme sadece koşul sağlandığında olur.
        /// </summary>
        private static void PruneMembers(RuntimeCluster rc)
        {
            for (int i = rc.members.Count - 1; i >= 0; i--)
            {
                var b = rc.members[i];
                if (b == null || !b.gameObject.activeInHierarchy || b.state != BallState.InContainer)
                {
                    if (b != null) { b.clusterId = -1; b.clusterCell = -1; }
                    rc.members.RemoveAt(i);
                }
            }

            if (rc.members.Count >= 2) return;

            for (int i = 0; i < rc.members.Count; i++)
            {
                var b = rc.members[i];
                if (b != null) { b.clusterId = -1; b.clusterCell = -1; }
            }
            rc.members.Clear();
            rc.assigned = false;
        }

    }
}
