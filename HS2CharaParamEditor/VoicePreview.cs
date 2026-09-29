using System;
using System.Collections.Generic;
using AIChara;
using UnityEngine;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// 性格と声質のサンプル再生。
    ///
    /// バニラのキャラメイク（CharaCustom.CvsO_Type）は、キャラ種のトグルと
    /// 声質スライダのコールバックから PlayVoice() を呼んでいる。
    /// ここでもそれに合わせ、性格を変えたときと声質を確定したときに鳴らす。
    ///
    /// 音源は Manager.Voice.infoTable の samplebundle / sampleasset を起点にする。
    /// ピッチは ChaFileParameter2.voicePitch にゲーム自身で計算させるので、
    /// 換算式をこちらで持たない。上限解除で範囲外へ振ったときの挙動も
    /// ゲームの実装どおりになる。
    ///
    /// 音源が2系統ある点に注意（v0.9.1.0 で判明）。
    ///
    ///   sound/data/systemse/voicesample/&lt;30|50&gt;.unity3d … hss_&lt;性格&gt;_03_00
    ///       テーブルが指しているのはこちら。**1性格につき1本しかない**
    ///   sound/data/custom/&lt;30|50&gt;.unity3d              … hss_&lt;性格&gt;_02_00 / _01
    ///       キャラメイクが2本からランダムに選んでいるのはこちら
    ///
    /// バニラと同じ「2本からランダム」にしたいので、テーブルの値から名前を
    /// 寄せて custom 側を優先し、空振りしたら systemse 側へ落とす。
    /// </summary>
    internal static class VoicePreview
    {
        /// <summary>キャラメイクの試聴セットは1性格につき2本。</summary>
        private const int SampleCount = 2;

        private static readonly System.Random Rng = new System.Random();

        /// <summary>実際に鳴らせた音源をログに出した性格。初回の1回だけ出す。</summary>
        private static readonly HashSet<int> Logged = new HashSet<int>();

        /// <summary>
        /// キャラメイクの2本セットが使えなかった性格。
        /// 以後はテーブルが指す音源だけを鳴らし、空振りを繰り返さない。
        /// </summary>
        private static readonly HashSet<int> NoCustomSet = new HashSet<int>();

        /// <summary>ピッチ換算用の使い捨て。実キャラには触らない。</summary>
        private static ChaFileParameter2 _pitchCalc;

        private static AudioSource _playing;

        /// <summary>指定の性格のサンプルを、指定の声質で鳴らす。</summary>
        internal static void Play(int personality, float voiceRate)
        {
            try
            {
                VoiceInfo.Param prm = Find(personality);
                if (prm == null || string.IsNullOrEmpty(prm.samplebundle) || string.IsNullOrEmpty(prm.sampleasset))
                    return;

                float pitch = PitchOf(voiceRate);
                Stop();

                // まずキャラメイクと同じ2本セット。駄目ならテーブルが指す音源。
                if (PlayCustomPair(personality, prm, pitch))
                    return;

                if (!PlayOne(personality, prm.samplebundle, prm.sampleasset, pitch))
                    Plugin.Log.LogWarning("試聴の再生元を取得できませんでした: "
                                          + prm.samplebundle + " / " + prm.sampleasset);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("サンプルボイスの再生に失敗しました: " + ex.Message);
            }
        }

        /// <summary>鳴っていれば止める。こちらで鳴らした音だけを対象にする。</summary>
        internal static void Stop()
        {
            try
            {
                if (_playing != null && _playing.isPlaying)
                    _playing.Stop();
            }
            catch (Exception)
            {
                // 再生元がゲーム側で片付けられている。無視してよい。
            }
            _playing = null;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// キャラメイクの2本セットから1本をランダムに鳴らす。
        /// 引いた番号が空振りしたらもう一方を試し、両方駄目なら
        /// この性格では以後このセットを使わない。
        /// </summary>
        private static bool PlayCustomPair(int personality, VoiceInfo.Param prm, float pitch)
        {
            if (NoCustomSet.Contains(personality))
                return false;

            string bundle = CustomBundle(prm.samplebundle);
            string baseName = CustomAssetBase(prm.sampleasset);
            if (bundle == null || baseName == null)
            {
                NoCustomSet.Add(personality);
                return false;
            }

            int first = Rng.Next(SampleCount);
            for (int i = 0; i < SampleCount; i++)
            {
                int index = (first + i) % SampleCount;
                if (PlayOne(personality, bundle, baseName + index.ToString("00"), pitch))
                    return true;
            }

            NoCustomSet.Add(personality);
            return false;
        }

        /// <summary>1本鳴らす。鳴らせたら true。</summary>
        private static bool PlayOne(int personality, string bundle, string asset, float pitch)
        {
            Manager.Voice.Loader loader = new Manager.Voice.Loader();
            loader.bundle = bundle;
            loader.asset = asset;
            loader.pitch = pitch;

            AudioSource src = Manager.Voice.OncePlay(loader);
            if (src == null)
                return false;

            _playing = src;
            if (Logged.Add(personality))
                Plugin.Log.LogInfo("試聴音源: 性格 " + personality + " → " + bundle + " / " + asset);
            return true;
        }

        /// <summary>
        /// テーブルのバンドル（sound/data/systemse/voicesample/NN.unity3d）を
        /// キャラメイクのバンドル（sound/data/custom/NN.unity3d）へ読み替える。
        /// 30 が基本、50 がアペンドディスク分。番号はテーブルの値をそのまま使う。
        /// </summary>
        private static string CustomBundle(string sampleBundle)
        {
            const string marker = "systemse/voicesample/";
            int at = sampleBundle.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                return null;
            return sampleBundle.Substring(0, at) + "custom/" + sampleBundle.Substring(at + marker.Length);
        }

        /// <summary>
        /// hss_&lt;性格2桁&gt;_03_&lt;通し2桁&gt; → hss_&lt;性格2桁&gt;_02_
        /// 通し番号は呼び出し側で付ける。規則から外れていたら null。
        /// </summary>
        private static string CustomAssetBase(string sampleAsset)
        {
            if (sampleAsset.Length < 12 || !sampleAsset.StartsWith("hss_", StringComparison.Ordinal))
                return null;
            if (sampleAsset.Substring(6, 4) != "_03_")
                return null;
            return sampleAsset.Substring(0, 6) + "_02_";
        }

        /// <summary>
        /// テーブルのキーが性格番号とは限らないので、Param.No で引く。
        /// 選択肢を作っている NameSources も Param.No を値にしている。
        /// </summary>
        private static VoiceInfo.Param Find(int personality)
        {
            IReadOnlyDictionary<int, VoiceInfo.Param> table = Manager.Voice.infoTable;
            if (table == null)
                return null;

            foreach (KeyValuePair<int, VoiceInfo.Param> kv in table)
            {
                VoiceInfo.Param prm = kv.Value;
                if (prm != null && prm.No == personality)
                    return prm;
            }
            return null;
        }

        /// <summary>
        /// voiceRate から実際のピッチを得る。換算式はゲームが持っているので、
        /// 使い捨ての ChaFileParameter2 に値を入れて計算させる。
        /// </summary>
        private static float PitchOf(float voiceRate)
        {
            try
            {
                if (_pitchCalc == null)
                    _pitchCalc = new ChaFileParameter2();

                _pitchCalc.voiceRate = voiceRate;
                return _pitchCalc.voicePitch;
            }
            catch (Exception)
            {
                // 取れないときだけ、ゲームの定数から直接引く。
                return Mathf.Lerp(ChaFileDefine.VoicePitchMin, ChaFileDefine.VoicePitchMax, voiceRate);
            }
        }
    }
}
