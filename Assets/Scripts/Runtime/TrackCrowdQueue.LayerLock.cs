using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Katman kilidi (PuzzleGlassLevelData.trackLayerLock): halka pistte her top doğduğu şeride göre dış (0), orta (1)
    /// ya da iç (2) katmandadır. Camda hâlâ top bulunan en dış katman "açık" katmandır; ondan içteki katmanların topları
    /// çıkıştan ayrılamaz. Açık katmanın son topu da gidince sıradaki katman açılır. (Toplar görsel olarak değişmez.)
    /// Çıkış kuralı (IsAtExit) ve kilitlenme kontrolü (BallCollectionController) aynı bilgiyi buradan okur.
    /// </summary>
    public static partial class TrackCrowdQueue
    {
        private static bool s_LayerLock;
        private static int s_OpenLayer = int.MaxValue;
        private static int s_LayerFrame = -1;

        /// <summary>Katman bandından taşan topu geri iten yay sertliği (1/sn²). Enine yerçekimini rahatça yenmeli.</summary>
        /// (Level_38 testi: 170'te dış şeride kayan kilitli top 29, 600'de 5; yaysız 78.)
        public static float LayerKeepStiffness = 600f;
        /// <summary>Bant dışındayken enine hızı söndürme kazancı (1/sn): geri itilen top bandın kenarında sekmesin.</summary>
        public static float LayerKeepDamping = 20f;

        // Fizik adımında her top için adadan dışa enine konum ve yön (Rigidbody adımı doldurur)
        private static float[] s_LayerOutOff = new float[256];
        private static Vector2[] s_LayerOutDir = new Vector2[256];

        /// <summary>Sert duvarın bant dış kenarından ötedeki toleransı (top çapı oranı): normal itişmede tetiklenmesin.</summary>
        public static float LayerWallTolerance = 0.15f;
        /// <summary>Teşhis: sert duvarın topu geri aldığı toplam sayı (level başında sıfırlanır).</summary>
        public static int LayerWallClamps { get; private set; }
        /// <summary>Teşhis: duvarın geri aldığı toplam / en büyük enine mesafe (top çapı cinsinden).</summary>
        public static float LayerWallCorrectionSum { get; private set; }
        public static float LayerWallCorrectionMax { get; private set; }
        /// <summary>Teşhis: hatalı ölçüm şüphesiyle atlanan (yarım çaptan büyük) düzeltme sayısı.</summary>
        public static int LayerWallSkipped { get; private set; }

        /// <summary>
        /// Sert duvar: kilitli katmandaki (açık katmandan içteki) top, bandının dış kenarını (+tolerans) aşarsa enine
        /// duvara geri alınır ve dışa doğru hızı silinir. Sadece dış taraf sınırlanır (içe hareket serbest); kanal boyunca
        /// ilerleme korunur. Düzeltme yapıldıysa true döner ve p güncellenir.
        /// </summary>
        private static bool ClampToLayerWall(BallPhysicsController b, ref Vector2 p, float outOffset, Vector2 outDir,
            GlassTrackLayout.CompiledTrack track, float d, Transform ct, float localZ)
        {
            if (!s_LayerLock || b == null || b.trackLayer < 0 || b.trackLayer <= OpenLayer) return false;
            float half = Mathf.Max(1e-4f, track.innerHalfWidth - d * 0.56f);
            float edge = half * 0.33f;
            float pad = d * 0.08f;
            // Bandın dış kenarı: orta için +edge-pad, iç için -edge-pad
            float bandOuter = b.trackLayer == 1 ? (edge - pad) : (-edge - pad);
            float wall = bandOuter + d * LayerWallTolerance;
            if (outOffset <= wall) return false;
            // Güvenlik: gerçek taşma adım adım büyür ve duvar onu erken yakalar. Tek adımda yarım çaptan büyük taşma
            // ancak hatalı ölçümden gelir (ör. en yakın orta çizgi noktası, birbirine yakın iki kolun karşı tarafından
            // seçildiyse); o durumda topu ışınlama, yaya bırak.
            if (outOffset - wall > d * 0.5f) { LayerWallSkipped++; return false; }

            p += outDir * (wall - outOffset);
            b.rb.position = ct.TransformPoint(new Vector3(p.x, p.y, localZ));
            Vector3 vLocal = ct.InverseTransformDirection(b.rb.linearVelocity);
            Vector2 v2 = new Vector2(vLocal.x, vLocal.y);
            float vLat = Vector2.Dot(v2, outDir);
            if (vLat > 0f)
            {
                v2 -= outDir * vLat;
                b.rb.linearVelocity = ct.TransformDirection(new Vector3(v2.x, v2.y, vLocal.z));
            }
            LayerWallClamps++;
            float corr = (outOffset - wall) / Mathf.Max(1e-4f, d);
            LayerWallCorrectionSum += corr;
            if (corr > LayerWallCorrectionMax) LayerWallCorrectionMax = corr;
            return true;
        }

        /// <summary>
        /// Topun kendi katman bandında kalması için enine ivme. Bantlar ComputeRingLayer ile aynı: kanal yarı genişliği üçe
        /// bölünür (dış: +0.33..+1, orta: -0.33..+0.33, iç: -1..-0.33). Bant içindeyken ivme yok; taşınca yay + sönüm.
        /// </summary>
        private static Vector2 LayerBandAccel(int layer, float outOffset, Vector2 outDir, Vector2 vel,
            GlassTrackLayout.CompiledTrack track, float d)
        {
            float half = Mathf.Max(1e-4f, track.innerHalfWidth - d * 0.56f);
            float edge = half * 0.33f;
            // Bant kenarına küçük pay: top bandın sınırına yaslanınca komşu bantla karışmasın
            float pad = d * 0.08f;
            float lo, hi;
            if (layer == 0) { lo = edge + pad; hi = float.MaxValue; }
            else if (layer == 1) { lo = -edge + pad; hi = edge - pad; }
            else { lo = float.MinValue; hi = -edge - pad; }
            if (lo > hi) { float mid = (lo + hi) * 0.5f; lo = hi = mid; }

            float target = Mathf.Clamp(outOffset, lo, hi);
            float err = target - outOffset;
            if (Mathf.Abs(err) < 1e-5f) return Vector2.zero;
            float latVel = Vector2.Dot(vel, outDir);
            return outDir * (err * LayerKeepStiffness - latVel * LayerKeepDamping);
        }

        public static bool LayerLockActive => s_LayerLock;

        /// <summary>Şu an açık olan katman (camda top kalmadıysa int.MaxValue).</summary>
        public static int OpenLayer
        {
            get
            {
                RefreshLayerLock();
                return s_OpenLayer;
            }
        }

        /// <summary>Level yüklenirken toplar doğduktan sonra çağrılır.</summary>
        public static void SetLayerLock(bool active)
        {
            s_LayerLock = active;
            s_OpenLayer = int.MaxValue;
            s_LayerFrame = -1;
            LayerWallClamps = 0;
            LayerWallCorrectionSum = 0f;
            LayerWallCorrectionMax = 0f;
            LayerWallSkipped = 0;
            if (active) RefreshLayerLock(force: true);
        }

        /// <summary>Top katmanı açık mı? Kilit kapalıysa ya da topun katmanı yoksa her zaman açıktır.</summary>
        public static bool IsLayerOpen(BallPhysicsController ball)
        {
            if (!s_LayerLock || ball == null || ball.trackLayer < 0) return true;
            RefreshLayerLock();
            return ball.trackLayer <= s_OpenLayer;
        }

        /// <summary>Açık katmanı yeniden hesaplar (karede bir kez).</summary>
        public static void RefreshLayerLock(bool force = false)
        {
            if (!s_LayerLock) return;
            if (!force && s_LayerFrame == Time.frameCount) return;
            s_LayerFrame = Time.frameCount;

            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return;
            var balls = pool.ActiveBalls;

            int open = int.MaxValue;
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b == null || b.trackLayer < 0 || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                if (b.trackLayer < open) open = b.trackLayer;
            }

            s_OpenLayer = open;
        }
    }
}
