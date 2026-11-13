using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Self-contained procedural audio manager that synthesizes crisp casual-puzzle SFX
    /// (marble pops, grid slide ticks, acrylic box seal latches, glass clinks, UI taps, level win chime)
    /// without external audio assets.
    /// </summary>
    public class PuzzleSoundManager : MonoBehaviour
    {
        public static PuzzleSoundManager Instance { get; private set; }

        private const string MutePrefKey = "PuzzleGlass_AudioMuted";
        private const int SampleRate = 44100;

        [Header("Master Audio Settings")]
        [Range(0f, 1f)] public float masterVolume = 0.72f;
        [SerializeField] private bool isMuted = false;

        private AudioSource _sfxSource;
        private AudioClip _clipBallPop;
        private AudioClip _clipGridSlide;
        private AudioClip _clipBlockSnap;
        private AudioClip _clipBoxComplete;
        private AudioClip _clipShakeGlass;
        private AudioClip _clipBlocked;
        private AudioClip _clipUiClick;
        private AudioClip _clipLevelClear;
        private AudioClip _clipLevelFail;

        private float _lastPopTime = -10f;
        private float _lastSlideTime = -10f;
        private int _popChainCount = 0;

        public bool IsMuted => isMuted;

        public static PuzzleSoundManager GetOrCreate()
        {
            if (Instance != null) return Instance;
            Instance = FindFirstObjectByType<PuzzleSoundManager>();
            if (Instance != null) return Instance;

            var gm = PuzzleGlassGameManager.Instance ?? FindFirstObjectByType<PuzzleGlassGameManager>();
            GameObject host = gm != null ? gm.gameObject : new GameObject("PuzzleSoundManager");
            return host.AddComponent<PuzzleSoundManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            EnsureAudioSource();
            BuildProceduralClips();
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureAudioSource()
        {
            if (_sfxSource != null) return;
            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
            }
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
        }

        public void SetMuted(bool muted)
        {
            isMuted = muted;
            PlayerPrefs.SetInt(MutePrefKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            if (!muted)
            {
                PlayUiClick();
            }
        }

        public bool ToggleMute()
        {
            SetMuted(!isMuted);
            return isMuted;
        }

        public void PlayBallPop()
        {
            if (isMuted || !Application.isPlaying) return;
            if (Time.unscaledTime - _lastPopTime < 0.032f) return;

            if (Time.unscaledTime - _lastPopTime > 0.45f)
            {
                _popChainCount = 0;
            }
            else
            {
                _popChainCount = Mathf.Min(_popChainCount + 1, 10);
            }
            _lastPopTime = Time.unscaledTime;

            float pitch = 0.94f + _popChainCount * 0.042f;
            PlayClipWithPitch(_clipBallPop, 0.56f, pitch);
        }

        public void PlayGridSlide()
        {
            if (isMuted || !Application.isPlaying) return;
            if (Time.unscaledTime - _lastSlideTime < 0.040f) return;
            _lastSlideTime = Time.unscaledTime;
            PlayClipWithPitch(_clipGridSlide, 0.36f, Random.Range(0.96f, 1.05f));
        }

        public void PlayBlockSnap()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipBlockSnap, 0.42f, Random.Range(0.98f, 1.03f));
        }

        public void PlayBoxComplete()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipBoxComplete, 0.62f, 1.0f);
        }

        public void PlayShakeGlass()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipShakeGlass, 0.58f, Random.Range(0.98f, 1.03f));
        }

        public void PlayBlocked()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipBlocked, 0.38f, 1.0f);
        }

        public void PlayUiClick()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipUiClick, 0.46f, 1.0f);
        }

        public void PlayLevelClear()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipLevelClear, 0.72f, 1.0f);
        }

        public void PlayLevelFail()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipLevelFail, 0.52f, 1.0f);
        }

        private void PlayClipWithPitch(AudioClip clip, float volumeScale, float pitch)
        {
            if (clip == null) return;
            EnsureAudioSource();
            if (_sfxSource == null) return;
            _sfxSource.pitch = pitch;
            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * volumeScale));
        }

        private void BuildProceduralClips()
        {
            if (_clipBallPop != null) return;

            _clipBallPop     = CreateBubblePopClip("SFX_BallPop", 0.075f, 460f, 840f);
            _clipGridSlide   = CreateWoodTickClip("SFX_GridSlide", 0.036f, 240f, 145f);
            _clipBlockSnap   = CreateMarimbaSnapClip("SFX_BlockSnap", 0.065f, 320f, 470f);
            _clipBoxComplete = CreateChimeChordClip("SFX_BoxComplete", 0.24f, new[] { 523.25f, 783.99f, 1046.50f }, 0.040f);
            _clipShakeGlass  = CreateGlassClinkClip("SFX_ShakeGlass", 0.28f);
            _clipBlocked     = CreateWoodTickClip("SFX_Blocked", 0.095f, 155f, 92f);
            _clipUiClick     = CreateBubblePopClip("SFX_UiClick", 0.046f, 620f, 890f);
            _clipLevelClear  = CreateChimeChordClip("SFX_LevelClear", 0.62f, new[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.078f);
            _clipLevelFail   = CreateChimeChordClip("SFX_LevelFail", 0.36f, new[] { 329.63f, 261.63f }, 0.130f);
        }

        private static AudioClip CreateBubblePopClip(string name, float duration, float fStart, float fEnd)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float env = Mathf.Sin(Mathf.Min(1f, t * 8f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - t, 1.8f);
                float freq = Mathf.Lerp(fStart, fEnd, Mathf.Pow(t, 0.65f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float tone = Mathf.Sin((float)phase) * 0.78f + Mathf.Sin((float)(phase * 2.0)) * 0.18f;
                data[i] = tone * env;
            }

            return FinishClip(name, data);
        }

        private static AudioClip CreateWoodTickClip(string name, float duration, float fStart, float fEnd)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float env = Mathf.Min(1f, t * 25f) * Mathf.Exp(-t * 7.5f);
                float freq = Mathf.Lerp(fStart, fEnd, t);
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float tone = Mathf.Sin((float)phase) * 0.85f + Mathf.Sin((float)(phase * 2.4)) * 0.15f;
                data[i] = tone * env;
            }

            return FinishClip(name, data);
        }

        private static AudioClip CreateMarimbaSnapClip(string name, float duration, float fStart, float fEnd)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float env = Mathf.Min(1f, t * 22f) * Mathf.Exp(-t * 5.2f);
                float freq = Mathf.Lerp(fStart, fEnd, Mathf.Min(1f, t * 3f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float tone = Mathf.Sin((float)phase) * 0.80f + Mathf.Sin((float)(phase * 2.0)) * 0.16f;
                data[i] = tone * env;
            }

            return FinishClip(name, data);
        }

        private static AudioClip CreateChimeChordClip(string name, float duration, float[] notes, float stepSeconds)
        {
            int count = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            float invNorm = 1f / Mathf.Max(1, notes.Length);

            for (int n = 0; n < notes.Length; n++)
            {
                int startSample = Mathf.RoundToInt(n * stepSeconds * SampleRate);
                float freq = notes[n];
                for (int i = startSample; i < count; i++)
                {
                    float localSec = (i - startSample) / (float)SampleRate;
                    float attack = Mathf.Min(1f, localSec / 0.008f);
                    float decay = Mathf.Exp(-localSec * 6.5f);
                    float env = attack * decay;
                    float angle = 2f * Mathf.PI * freq * localSec;
                    float val = Mathf.Sin(angle) * 0.72f + Mathf.Sin(angle * 2.0f) * 0.20f + Mathf.Sin(angle * 3.0f) * 0.06f;
                    data[i] += val * env * (0.55f + 0.45f * invNorm);
                }
            }

            // Soft peak limiter
            for (int i = 0; i < count; i++)
            {
                data[i] = Mathf.Clamp(data[i] * 0.65f, -0.95f, 0.95f);
            }

            return FinishClip(name, data);
        }

        private static AudioClip CreateGlassClinkClip(string name, float duration)
        {
            int count = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];

            float[] pingOffsets = { 0f, 0.065f, 0.125f };
            float[] pingFreqs = { 1380f, 1760f, 1540f };

            for (int p = 0; p < pingOffsets.Length; p++)
            {
                int startSample = Mathf.RoundToInt(pingOffsets[p] * SampleRate);
                float f = pingFreqs[p];
                for (int i = startSample; i < count; i++)
                {
                    float localSec = (i - startSample) / (float)SampleRate;
                    float attack = Mathf.Min(1f, localSec / 0.004f);
                    float decay = Mathf.Exp(-localSec * 14f);
                    float env = attack * decay;
                    float a = 2f * Mathf.PI * f * localSec;
                    float clink = Mathf.Sin(a) * 0.60f + Mathf.Sin(a * 1.52f) * 0.25f + Mathf.Sin(a * 2.38f) * 0.12f;
                    data[i] += clink * env * 0.42f;
                }
            }

            return FinishClip(name, data);
        }

        private static AudioClip FinishClip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
