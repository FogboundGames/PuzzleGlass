using DG.Tweening;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Bir blok dolup tepsiden çıktığında kutlama: kısa bir parlama halkası, bloğun renginde parçacık patlaması
    /// ve mobilde kısa bir titreşim. Kendi kendine yeten statik yardımcı; sahnede kurulum gerektirmez.
    /// Malzeme olarak sprite'ların varsayılan malzemesi kullanılır (build'de her zaman bulunur).
    /// </summary>
    public static class BlockClearCelebration
    {
        /// <summary>Titreşim açık mı? (PlayerPrefs "PG_Haptics", varsayılan açık)</summary>
        public static bool HapticsEnabled
        {
            get => PlayerPrefs.GetInt("PG_Haptics", 1) == 1;
            set { PlayerPrefs.SetInt("PG_Haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Sprite'ların varsayılan malzemesi (iz/parçacık için; build'de her zaman bulunur).</summary>
        public static Material SpriteMaterial { get { EnsureResources(); return s_SpriteMat; } }

        private static Sprite s_SoftDot;
        private static Sprite s_CrispRing;
        private static Material s_SpriteMat;
        private static ParticleSystem s_Burst;

        public static void Play(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.6f);
            PlayFlash(pos, color, size);
            PlayRing(pos, color, size, duration: 0.28f, endScale: 2.15f);
            PlayBurst(pos, color, size, count: 22);
            Vibrate(30);
        }

        /// <summary>
        /// Compact, crisp ASMR seal burst played immediately when a piece finishes filling and latches shut.
        /// </summary>
        public static void PlaySealRingPop(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.6f);
            PlayRing(pos, color, size * 0.92f, duration: 0.22f, endScale: 1.85f);
            PlayBurst(pos, color, size * 0.85f, count: 10);
            Vibrate(18);
        }

        /// <summary>
        /// Clean ASMR implosion pop: one crisp colored ring and tight pastel/white sparkles with zero screen clutter.
        /// </summary>
        public static void PlayImplosionPop(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.55f);
            PlayRing(pos, color, size * 0.82f, duration: 0.20f, endScale: 1.55f);
            PlayBurst(pos, color, size * 0.78f, count: 12);
            Vibrate(22);
        }

        /// <summary>
        /// Subtle thin colored contact ring played right around a gray socket when a ball snaps in.
        /// </summary>
        public static void PlaySocketSeatRing(Vector3 worldPos, Color color, float radius = 0.24f, float alpha = 0.82f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            if (s_CrispRing == null) return;

            var go = new GameObject("SocketSeatRing");
            go.transform.position = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.14f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s_CrispRing;
            sr.sortingOrder = 492;

            Color c = Color.Lerp(color, Color.white, 0.55f);
            c.a = Mathf.Clamp01(alpha);
            sr.color = c;

            float startScale = Mathf.Max(0.10f, radius * 1.15f);
            float endScale = Mathf.Max(0.22f, radius * 2.55f);
            go.transform.localScale = Vector3.one * startScale;

            Sequence seq = DOTween.Sequence();
            seq.Join(go.transform.DOScale(endScale, 0.17f).SetEase(Ease.OutCubic));
            seq.Join(sr.DOFade(0f, 0.17f).SetEase(Ease.InQuad));
            seq.OnComplete(() => { if (go != null) Object.Destroy(go); });
            seq.SetLink(go);
        }

        // ---------- Parlama ----------

        private static void PlayFlash(Vector3 pos, Color color, float size)
        {
            var go = new GameObject("BlockClearFlash");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s_SoftDot;
            sr.sortingOrder = 500;
            Color c = Color.Lerp(color, Color.white, 0.55f);
            c.a = 0.95f;
            sr.color = c;

            float start = 0.35f * size, end = 1.9f * size;
            go.transform.localScale = Vector3.one * start;
            Sequence seq = DOTween.Sequence();
            seq.Join(go.transform.DOScale(end, 0.32f).SetEase(Ease.OutCubic));
            seq.Join(sr.DOFade(0f, 0.32f).SetEase(Ease.InQuad));
            seq.OnComplete(() => { if (go != null) Object.Destroy(go); });
            seq.SetLink(go);
        }

        private static void PlayRing(Vector3 pos, Color color, float size, float duration, float endScale)
        {
            if (s_CrispRing == null) return;
            var go = new GameObject("BlockSealRing");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s_CrispRing;
            sr.sortingOrder = 502;
            Color c = Color.Lerp(color, Color.white, 0.68f);
            c.a = 0.92f;
            sr.color = c;

            go.transform.localScale = Vector3.one * (0.42f * size);
            Sequence seq = DOTween.Sequence();
            seq.Join(go.transform.DOScale(endScale * size, duration).SetEase(Ease.OutCubic));
            seq.Join(sr.DOFade(0f, duration).SetEase(Ease.InQuad));
            seq.OnComplete(() => { if (go != null) Object.Destroy(go); });
            seq.SetLink(go);
        }

        // ---------- Parçacıklar ----------

        private static void PlayBurst(Vector3 pos, Color color, float size, int count)
        {
            if (s_Burst == null) s_Burst = CreateBurstSystem();
            s_Burst.transform.position = pos;

            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true
            };
            for (int i = 0; i < count; i++)
            {
                float t = Random.value;
                emit.startColor = t < 0.25f ? Color.white : Color.Lerp(color, Color.white, t * 0.35f);
                emit.startSize = Random.Range(0.06f, 0.15f) * size;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float spd = Random.Range(1.4f, 3.2f) * size;
                emit.velocity = new Vector3(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + 0.9f, 0f);
                emit.startLifetime = Random.Range(0.35f, 0.6f);
                s_Burst.Emit(emit, 1);
            }
        }

        private static ParticleSystem CreateBurstSystem()
        {
            var go = new GameObject("BlockClearBurst");
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.55f;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.18f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = 0.6f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = s_SpriteMat;
            r.sortingOrder = 501;
            if (s_SoftDot != null) r.material.mainTexture = s_SoftDot.texture;
            return ps;
        }

        // ---------- Kaynaklar ----------

        private static void EnsureResources()
        {
            if (s_SoftDot == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // yumuşak kenar
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_SoftDot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
            if (s_CrispRing == null)
            {
                const int n = 96;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float band = Mathf.Exp(-Mathf.Pow((d - 0.80f) / 0.085f, 2f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(band) * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_CrispRing = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
            if (s_SpriteMat == null)
            {
                // Sprite'ın varsayılan malzemesi (Sprites-Default / URP 2D sprite) her build'de bulunur
                var tmp = new GameObject("tmp");
                s_SpriteMat = tmp.AddComponent<SpriteRenderer>().sharedMaterial;
                if (Application.isPlaying)
                    Object.Destroy(tmp);
                else
                    Object.DestroyImmediate(tmp);
            }
        }

        // ---------- Titreşim ----------

        /// <summary>Kısa titreşim. Android'de milisaniye ayarlı; iOS'ta sistem titreşimi.</summary>
        public static void Vibrate(long milliseconds)
        {
            if (!HapticsEnabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null) return;
                    int sdk;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                    if (sdk >= 26)
                    {
                        using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, -1))
                            vibrator.Call("vibrate", effect);
                    }
                    else
                    {
                        vibrator.Call("vibrate", milliseconds);
                    }
                }
            }
            catch (System.Exception)
            {
                Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }
}
