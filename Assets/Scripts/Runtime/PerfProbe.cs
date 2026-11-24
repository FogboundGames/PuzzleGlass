using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PuzzleGlass
{
    /// <summary>
    /// GEÇİCİ performans ölçüm aracı. Sadece Android'de, uygulama adb ile "perf" parametresiyle açılırsa çalışır:
    ///   adb shell am start -n .../com.unity3d.player.UnityPlayerGameActivity -e perf "rs=0.8;hide=SoftBackdrop,GlassChamberLit"
    /// rs: URP render scale, hide: shader adında bu parçaları geçen renderer'lar kapatılır.
    /// si / svi: Physics solver / velocity iteration (mevcut ve yeni Rigidbody.lere uygulanır), fdt: Time.fixedDeltaTime,
    /// bake=0: cam pişirmeyi kapatır (GlassChamberBakeCache), mdt: Time.maximumDeltaTime, ffts: oyun sonu hızlanmasının timeScale'i (varsayılan 2).
    /// Her 2 saniyede logcat'e "PERFPROBE" etiketiyle FPS yazar. Normal açılışta hiçbir şey yapmaz.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        private string[] _hide = new string[0];
        private int _si = -1, _svi = -1;
        private int _frames;
        private float _time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string arg = ReadIntentExtra("perf");
            if (string.IsNullOrEmpty(arg)) return;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>().Configure(arg);
        }

        private static string ReadIntentExtra(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = act.Call<AndroidJavaObject>("getIntent"))
                {
                    return intent.Call<string>("getStringExtra", key);
                }
            }
            catch { return null; }
#else
            return null;
#endif
        }

        private void Configure(string arg)
        {
            foreach (var part in arg.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                if (kv[0] == "rs" && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rs))
                {
                    if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = rs;
                }
                else if (kv[0] == "hide") _hide = kv[1].Split(',');
                else if (kv[0] == "si" && int.TryParse(kv[1], out int si)) { Physics.defaultSolverIterations = si; _si = si; }
                else if (kv[0] == "svi" && int.TryParse(kv[1], out int svi)) { Physics.defaultSolverVelocityIterations = svi; _svi = svi; }
                else if (kv[0] == "fdt" && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fdt)) Time.fixedDeltaTime = fdt;
                else if (kv[0] == "bake") GlassChamberBakeCache.Enabled = kv[1] != "0";
                else if (kv[0] == "mdt"&& float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mdt)) Time.maximumDeltaTime = mdt;
                else if (kv[0] == "ffts" && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ffts)) PuzzleGlassGameManager.EndgameTimeScale = ffts;
            }
            Debug.Log("PERFPROBE config " + arg);
            StartCoroutine(HideLoop());
        }

        private IEnumerator HideLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                if (_hide.Length > 0)
                {
                    foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        var m = r.sharedMaterial;
                        if (m == null || m.shader == null) continue;
                        string n = m.shader.name;
                        for (int i = 0; i < _hide.Length; i++)
                            if (_hide[i].Length > 0 && n.Contains(_hide[i])) { r.enabled = false; break; }
                    }
                }
                if (_si > 0 || _svi > 0)
                {
                    // Havuzdaki toplar sahne yüklenirken oluşmuş olabilir: mevcut Rigidbody'lere de uygula
                    foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                    {
                        if (_si > 0) rb.solverIterations = _si;
                        if (_svi > 0) rb.solverVelocityIterations = _svi;
                    }
                }
                yield return wait;
            }
        }

        private int _fixedSteps, _cappedFrames;
        private float _gameTime;

        private void FixedUpdate() { _fixedSteps++; }

        private void Update()
        {
            _frames++;
            _time += Time.unscaledDeltaTime;
            _gameTime += Time.deltaTime;
            if (Time.deltaTime >= Time.maximumDeltaTime * 0.999f) _cappedFrames++;

            // Oyun sonu hızlanmasında (timeScale > 1) daha sık raporla
            float window = Time.timeScale > 1.01f ? 1f : 2f;
            if (_time >= window)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Debug.Log(string.Format(ci,
                    "PERFPROBE fps={0:F1} ts={1:F1} steps/frame={2:F2} capped={3:P0} gameSpeed={4:F2}",
                    _frames / _time, Time.timeScale, (float)_fixedSteps / _frames,
                    (float)_cappedFrames / _frames, _gameTime / _time));
                _frames = 0; _time = 0f; _gameTime = 0f; _fixedSteps = 0; _cappedFrames = 0;
            }
        }
    }
}
