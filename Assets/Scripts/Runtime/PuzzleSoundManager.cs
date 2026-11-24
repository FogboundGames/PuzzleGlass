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
        private const int PitchedVoiceCount = 8;

        private static readonly float[] PentatonicPitchScale =
        {
            1.000f, // C5
            1.125f, // D5
            1.250f, // E5
            1.500f, // G5
            1.667f, // A5
            2.000f, // C6
            2.250f, // D6
            2.500f  // E6
        };

        private static readonly float[] AsmrDropPitchScale =
        {
            0.891f,
            0.944f,
            1.000f,
            1.059f,
            1.122f,
            1.189f,
            1.260f,
            1.335f
        };

        [Header("Master Audio Settings")]
        [Range(0f, 1f)] public float masterVolume = 0.72f;
        [SerializeField] private bool isMuted = false;

        private AudioSource _sfxSource;
        private AudioSource[] _pitchedVoices;
        private int _nextVoiceIndex = 0;

        private AudioClip _clipBallPop;
        private AudioClip _clipTransferBead;
        private AudioClip _clipBallTap;
        private AudioClip _clipBallSoftSettle;
        private AudioClip _clipLidLatch;
        private AudioClip _clipGridSlide;
        private AudioClip _clipBlockSnap;
        private AudioClip _clipBoxComplete;
        private AudioClip _clipBlocked;
        private AudioClip _clipUiClick;
        private AudioClip _clipLevelClear;
        private AudioClip _clipLevelFail;
        private AudioClip _clipUnlock;
        private AudioClip _clipIceBreak;

        private float _lastPopTime = -10f;
        private float _lastTapTime = -10f;
        private float _tapSuppressedUntil = -10f;
        private float _lastSlideTime = -10f;
        private int _popChainCount = 0;
        private int _tapCascadeCount = 0;

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
            if (_sfxSource == null)
            {
                _sfxSource = GetComponent<AudioSource>();
                if (_sfxSource == null)
                {
                    _sfxSource = gameObject.AddComponent<AudioSource>();
                }
                _sfxSource.playOnAwake = false;
                _sfxSource.loop = false;
                _sfxSource.spatialBlend = 0f;
            }

            if (_pitchedVoices == null || _pitchedVoices.Length != PitchedVoiceCount)
            {
                _pitchedVoices = new AudioSource[PitchedVoiceCount];
                for (int i = 0; i < PitchedVoiceCount; i++)
                {
                    AudioSource voice = gameObject.AddComponent<AudioSource>();
                    voice.playOnAwake = false;
                    voice.loop = false;
                    voice.spatialBlend = 0f;
                    _pitchedVoices[i] = voice;
                }
            }
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

        public void SuppressBallTaps(float durationSeconds)
        {
            _tapSuppressedUntil = Mathf.Max(_tapSuppressedUntil, Time.unscaledTime + Mathf.Max(0f, durationSeconds));
        }

        public void PlayBallTap(float intensity01)
        {
            if (isMuted || !Application.isPlaying) return;
            float now = Time.unscaledTime;
            if (now < _tapSuppressedUntil) return;
            if (now - _lastTapTime < 0.032f) return;

            if (now - _lastTapTime > 0.34f)
            {
                _tapCascadeCount = 0;
            }
            else
            {
                _tapCascadeCount = (_tapCascadeCount + 1) % AsmrDropPitchScale.Length;
            }
            _lastTapTime = now;

            float k = Mathf.Clamp01(intensity01);
            float cascadePitch = AsmrDropPitchScale[_tapCascadeCount] * Random.Range(0.985f, 1.015f);

            if (k < 0.42f)
            {
                float softVol = Mathf.Lerp(0.11f, 0.23f, k / 0.42f);
                PlayClipWithPitch(_clipBallSoftSettle, softVol, cascadePitch * 0.96f);
            }
            else
            {
                float tapVol = Mathf.Lerp(0.15f, 0.32f, (k - 0.42f) / 0.58f);
                float bodyVol = Mathf.Lerp(0.09f, 0.17f, k);
                PlayClipWithPitch(_clipBallTap, tapVol, cascadePitch);
                PlayClipWithPitch(_clipBallSoftSettle, bodyVol, cascadePitch * 0.92f);
            }
        }

        public void PlayBallPop()
        {
            PlayBallTransfer(isFinalBallInPiece: false);
        }

        /// <summary>
        /// Layered ASMR ball transfer feedback: soft liquid bubble pop blended with a
        /// harmonic pentatonic marimba/acrylic bead note that ascends cleanly over a chain.
        /// </summary>
        public void PlayBallTransfer(bool isFinalBallInPiece)
        {
            if (isMuted || !Application.isPlaying) return;
            if (Time.unscaledTime - _lastPopTime < 0.036f) return;

            if (Time.unscaledTime - _lastPopTime > 0.52f)
            {
                _popChainCount = 0;
            }
            else
            {
                _popChainCount = Mathf.Min(_popChainCount + 1, PentatonicPitchScale.Length - 1);
            }
            _lastPopTime = Time.unscaledTime;

            float popPitch = 0.94f + _popChainCount * 0.038f;
            float musicalRatio = PentatonicPitchScale[_popChainCount];

            PlayClipWithPitch(_clipBallPop, 0.44f, popPitch);
            PlayClipWithPitch(_clipTransferBead, isFinalBallInPiece ? 0.38f : 0.30f, isFinalBallInPiece ? musicalRatio * 1.125f : musicalRatio);
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

        public void PlayLidLatch()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipLidLatch, 0.46f, Random.Range(0.98f, 1.04f));
        }

        public void PlayBoxCompleteChime()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipBoxComplete, 0.60f, 1.0f);
        }

        public void PlayBoxComplete()
        {
            if (isMuted || !Application.isPlaying) return;
            // Tactile acrylic latch click layered with a warm sparkling add9 chime chord
            PlayClipWithPitch(_clipLidLatch, 0.42f, Random.Range(0.98f, 1.03f));
            PlayClipWithPitch(_clipBoxComplete, 0.58f, 1.0f);
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

        public void PlayUnlock()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipUnlock, 0.62f, Random.Range(0.98f, 1.04f));
        }

        public void PlayIceBreak()
        {
            if (isMuted || !Application.isPlaying) return;
            PlayClipWithPitch(_clipIceBreak, 0.60f, Random.Range(0.96f, 1.06f));
        }

        private void PlayClipWithPitch(AudioClip clip, float volumeScale, float pitch)
        {
            if (clip == null) return;
            EnsureAudioSource();

            AudioSource target = _sfxSource;
            if (Mathf.Approximately(pitch, 1.0f) && _sfxSource != null)
            {
                target = _sfxSource;
                target.pitch = 1.0f;
            }
            else if (_pitchedVoices != null && _pitchedVoices.Length > 0)
            {
                target = _pitchedVoices[_nextVoiceIndex % _pitchedVoices.Length];
                _nextVoiceIndex++;
                if (target != null)
                {
                    target.pitch = pitch;
                }
            }
            else if (target != null)
            {
                target.pitch = pitch;
            }

            if (target == null) return;
            target.PlayOneShot(clip, Mathf.Clamp01(masterVolume * volumeScale));
        }

        private void BuildProceduralClips()
        {
            if (_clipBallPop != null) return;

            _clipBallPop        = CreateBubblePopClip("SFX_BallPop", 0.072f, 440f, 820f);
            _clipTransferBead   = CreateTransferBeadClip("SFX_TransferBead", 0.095f, 523.25f);
            _clipBallTap        = CreateBallTapClip("SFX_BallTap", 0.036f);
            _clipBallSoftSettle = CreateBallSoftSettleClip("SFX_BallSoftSettle", 0.042f);
            _clipLidLatch     = CreateLidLatchClip("SFX_LidLatch", 0.036f);
            _clipGridSlide    = CreateWoodTickClip("SFX_GridSlide", 0.036f, 240f, 145f);
            _clipBlockSnap    = CreateMarimbaSnapClip("SFX_BlockSnap", 0.065f, 320f, 470f);
            _clipBoxComplete  = CreateChimeChordClip("SFX_BoxComplete", 0.30f, new[] { 523.25f, 659.25f, 783.99f, 987.77f, 1174.66f }, 0.034f);
            _clipBlocked      = CreateWoodTickClip("SFX_Blocked", 0.095f, 155f, 92f);
            _clipUiClick      = CreateBubblePopClip("SFX_UiClick", 0.046f, 620f, 890f);
            _clipLevelClear   = CreateChimeChordClip("SFX_LevelClear", 0.62f, new[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.078f);
            _clipLevelFail    = CreateChimeChordClip("SFX_LevelFail", 0.36f, new[] { 329.63f, 261.63f }, 0.130f);
            _clipUnlock       = CreateChimeChordClip("SFX_Unlock", 0.28f, new[] { 659.25f, 987.77f, 1318.51f }, 0.035f);
            _clipIceBreak     = CreateChimeChordClip("SFX_IceBreak", 0.22f, new[] { 1046.50f, 1396.91f, 1760.00f }, 0.022f);
        }

        private static AudioClip CreateTransferBeadClip(string name, float duration, float baseFreq)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float sec = i / (float)SampleRate;
                float t = i / (float)count;
                float attack = Mathf.Min(1f, sec / 0.004f);
                float decay = Mathf.Exp(-t * 6.2f);
                float body = Mathf.Sin(2f * Mathf.PI * baseFreq * sec) * 0.72f
                           + Mathf.Sin(2f * Mathf.PI * (baseFreq * 2.0f) * sec) * 0.20f * Mathf.Exp(-t * 9f)
                           + Mathf.Sin(2f * Mathf.PI * (baseFreq * 3.0f) * sec) * 0.06f * Mathf.Exp(-t * 14f);
                data[i] = body * attack * decay;
            }
            return FinishClip(name, data);
        }

        private static AudioClip CreateBallTapClip(string name, float duration)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;
            for (int i = 0; i < count; i++)
            {
                float sec = i / (float)SampleRate;
                float t = i / (float)count;
                float attack = Mathf.Min(1f, sec / 0.0016f);
                float bodyDecay = Mathf.Exp(-t * 8.2f);
                float crispDecay = Mathf.Exp(-t * 15.5f);
                float bodyFreq = Mathf.Lerp(470f, 345f, Mathf.Pow(t, 0.45f));
                phase += 2.0 * Mathf.PI * bodyFreq / SampleRate;
                // Warm marble/acrylic core layered with delicate glass clink harmonic
                float tone = Mathf.Sin((float)phase) * 0.56f * bodyDecay
                           + Mathf.Sin(2f * Mathf.PI * 1380f * sec) * 0.28f * crispDecay
                           + Mathf.Sin(2f * Mathf.PI * 2280f * sec) * 0.12f * Mathf.Exp(-t * 21f);
                data[i] = tone * attack;
            }
            return FinishClip(name, data);
        }

        private static AudioClip CreateBallSoftSettleClip(string name, float duration)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;
            for (int i = 0; i < count; i++)
            {
                float sec = i / (float)SampleRate;
                float t = i / (float)count;
                float attack = Mathf.Min(1f, sec / 0.0028f);
                float decay = Mathf.Exp(-t * 7.8f);
                float freq = Mathf.Lerp(335f, 225f, Mathf.Pow(t, 0.55f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float tone = Mathf.Sin((float)phase) * 0.74f
                           + Mathf.Sin((float)(phase * 2.05)) * 0.20f * Mathf.Exp(-t * 11f);
                data[i] = tone * attack * decay;
            }
            return FinishClip(name, data);
        }

        private static AudioClip CreateLidLatchClip(string name, float duration)
        {
            int count = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[count];
            double phase = 0.0;
            for (int i = 0; i < count; i++)
            {
                float sec = i / (float)SampleRate;
                float t = i / (float)count;
                float attack = Mathf.Min(1f, sec / 0.0015f);
                float decay = Mathf.Exp(-t * 11.5f);
                float freq = Mathf.Lerp(960f, 410f, Mathf.Pow(t, 0.45f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float click = Mathf.Sin((float)phase) * 0.68f
                            + Mathf.Sin(2f * Mathf.PI * 1680f * sec) * 0.24f * Mathf.Exp(-t * 14f);
                data[i] = click * attack * decay;
            }
            return FinishClip(name, data);
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

        private static AudioClip FinishClip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
