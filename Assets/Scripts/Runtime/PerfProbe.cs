using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PuzzleGlass
{
    /// <summary>
    /// GEÇİCİ performans ölçüm aracı. Sadece Android'de, uygulama adb ile "perf" parametresiyle açılırsa çalışır:
    ///   adb shell am start -n .../com.unity3d.player.UnityPlayerGameActivity -e perf "rs=0.8;hide=SoftBackdrop,GlassChamberLit"
    /// rs: URP render scale, hide: shader adında bu parçaları geçen renderer'lar kapatılır.
    /// Her 2 saniyede logcat'e "PERFPROBE" etiketiyle FPS yazar. Normal açılışta hiçbir şey yapmaz.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        private string[] _hide = new string[0];
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
                yield return wait;
            }
        }

        private void Update()
        {
            _frames++;
            _time += Time.unscaledDeltaTime;
            if (_time >= 2f)
            {
                Debug.Log($"PERFPROBE fps={_frames / _time:F1}");
                _frames = 0;
                _time = 0f;
            }
        }
    }
}
