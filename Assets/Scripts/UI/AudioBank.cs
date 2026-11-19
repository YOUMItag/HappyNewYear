using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 占位音效：全部用正弦/方波在运行时合成，不引入任何 wav 文件。
    ///
    /// 为什么程序化生成而不是找免费音效包：
    /// 1. M4 阶段先验证「有没有声音比声音好不好听更要紧」，合成音零成本；
    /// 2. 不引入外部资源 = 不引入授权问题，也不用担心包体；
    /// 3. 定美术风格时如果决定用真音效，删掉这个类换成 AudioClip 引用即可，调用点只有三处。
    ///
    /// 音量默认 0.35，比一般 BGM 低，避免短促的消除音糊成一片。
    /// </summary>
    public static class AudioBank
    {
        const int SampleRate = 44100;

        /// <summary>总音量 0~1。按 M 键可静音（AutoPlayer 里处理）。</summary>
        public static float Volume = 0.35f;
        public static bool Muted;

        static AudioSource _source;
        static AudioClip[] _clear;      // 下标 = 连锁轮次（1~4，超过用最后一个）
        static AudioClip _invalid;
        static AudioClip _shuffle;
        static bool _hasListener;

        public static void Init(GameObject host)
        {
            if (_source != null || host == null) return;

            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;      // 2D，不随位置衰减
            _source.volume = 1f;

            _clear = new[]
            {
                Tone("clear1", 660f, 0.16f, 0.55f),
                Tone("clear2", 740f, 0.17f, 0.55f),
                Tone("clear3", 830f, 0.18f, 0.55f),
                Tone("clear4", 930f, 0.20f, 0.55f)
            };
            _invalid = Square("invalid", 118f, 0.16f, 0.35f);
            _shuffle = Sweep("shuffle", 520f, 240f, 0.26f, 0.30f);

            // 场景里没有听者时无声。M2Setup 建的 Main Camera 只有 Camera 组件，
            // 所以这里补一个 —— AudioListener 挂在哪都行，不要求挂在相机上。
            _hasListener = Object.FindObjectOfType<AudioListener>() != null;
            if (!_hasListener)
            {
                host.AddComponent<AudioListener>();
                _hasListener = true;
                Debug.LogWarning("[AudioBank] 场景里没有 AudioListener，已在 " + host.name + " 上补建");
            }

            Debug.Log("[AudioBank] 音效就绪：clear1 峰值 " + Peak(_clear[0]).ToString("F2")
                + "，音量 " + Volume + (Muted ? "（已静音）" : ""));
        }

        static float Peak(AudioClip clip)
        {
            if (clip == null) return 0f;
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);

            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] < 0f ? -data[i] : data[i];
                if (v > peak) peak = v;
            }
            return peak;
        }

        public static void PlayClear(int round)
        {
            if (_clear == null || _clear.Length == 0) return;
            int index = round < 1 ? 0 : (round - 1);
            if (index >= _clear.Length) index = _clear.Length - 1;
            Play(_clear[index], 1f);
        }

        public static void PlayInvalid()
        {
            Play(_invalid, 1f);
        }

        public static void PlayShuffle()
        {
            Play(_shuffle, 1f);
        }

        /// <summary>按 M 键静音。</summary>
        public static void ToggleMute()
        {
            Muted = !Muted;
            Debug.Log("[AudioBank] " + (Muted ? "静音" : "恢复声音"));
        }

        static void Play(AudioClip clip, float pitch)
        {
            if (clip == null || _source == null || Muted || Volume <= 0.001f) return;
            if (!_hasListener) return;

            _source.pitch = pitch;
            _source.PlayOneShot(clip, Volume);
        }

        /// <summary>带泛音的短音：基频 + 二次谐音 + 快速衰减，听起来像「叮」。</summary>
        static AudioClip Tone(string name, float freq, float duration, float amplitude)
        {
            int count = (int)(SampleRate * duration);
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-14f * t);                   // 快速衰减
                float attack = Mathf.Clamp01(t * 400f);            // 掐掉起音爆音

                float value = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.75f
                            + Mathf.Sin(2f * Mathf.PI * freq * 2f * t) * 0.20f
                            + Mathf.Sin(2f * Mathf.PI * freq * 3f * t) * 0.05f;

                data[i] = value * env * attack * amplitude;
            }
            return Build(name, data);
        }

        /// <summary>方波低音：交换失败时的「咚」。</summary>
        static AudioClip Square(string name, float freq, float duration, float amplitude)
        {
            int count = (int)(SampleRate * duration);
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-11f * t);
                float attack = Mathf.Clamp01(t * 500f);
                float phase = (freq * t) % 1f;
                data[i] = (phase < 0.5f ? 1f : -1f) * env * attack * amplitude;
            }
            return Build(name, data);
        }

        /// <summary>扫频：洗牌时的「唰」。</summary>
        static AudioClip Sweep(string name, float fromHz, float toHz, float duration, float amplitude)
        {
            int count = (int)(SampleRate * duration);
            var data = new float[count];

            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float k = t / duration;
                float freq = Mathf.Lerp(fromHz, toHz, k);

                phase += freq / SampleRate;
                phase -= Mathf.Floor(phase);

                float env = Mathf.Sin(Mathf.PI * k);               // 中段最响
                data[i] = Mathf.Sin(2f * Mathf.PI * phase) * env * amplitude;
            }
            return Build(name, data);
        }

        static AudioClip Build(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
