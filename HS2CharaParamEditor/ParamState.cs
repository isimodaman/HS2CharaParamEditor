using System;
using AIChara;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// パネルが保持する編集中の値。適用ボタンを押すまでキャラには書き込まない。
    /// </summary>
    internal class ParamState
    {
        // プロフィール
        internal string Name = string.Empty;
        internal int Personality;
        internal float VoiceRate;          // 内部値。UI では x100 で見せる。

        // メンタル
        internal int Trait;
        internal int Mind;
        internal int HAttribute;

        // ステータス
        internal int Favor;
        internal int Slavery;
        internal int Enjoyment;
        internal int Aversion;
        internal int Dependence;
        internal int Broken;

        // 欲求
        internal int Dirty;
        internal int Tiredness;
        internal int Toilet;
        internal int Libido;

        // 状態
        internal int NowState;
        internal bool LockNowState;
        internal bool LockBroken;
        internal bool LockDependence;

        /// <summary>状態を手で変更したか。変更したらロックを立てないと CalcState に上書きされる。</summary>
        internal bool StateEditedByHand;

        internal bool Loaded;

        internal void LoadFrom(ChaFileControl file)
        {
            Loaded = false;
            StateEditedByHand = false;
            if (file == null)
                return;

            try
            {
                ChaFileParameter p = file.parameter;
                ChaFileParameter2 p2 = file.parameter2;
                ChaFileGameInfo2 g2 = file.gameinfo2;

                if (p != null)
                    Name = p.fullname ?? string.Empty;

                if (p2 != null)
                {
                    Personality = p2.personality;
                    VoiceRate = p2.voiceRate;
                    Trait = p2.trait;
                    Mind = p2.mind;
                    HAttribute = p2.hAttribute;
                }

                if (g2 != null)
                {
                    Favor = g2.Favor;
                    Slavery = g2.Slavery;
                    Enjoyment = g2.Enjoyment;
                    Aversion = g2.Aversion;
                    Dependence = g2.Dependence;
                    Broken = g2.Broken;

                    Dirty = g2.Dirty;
                    Tiredness = g2.Tiredness;
                    Toilet = g2.Toilet;
                    Libido = g2.Libido;

                    NowState = (int)g2.nowState;
                    LockNowState = g2.lockNowState;
                    LockBroken = g2.lockBroken;
                    LockDependence = g2.lockDependence;
                }

                Loaded = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("パラメータの読み取りに失敗しました: " + ex);
            }
        }

        internal void ApplyTo(ChaFileControl file)
        {
            if (file == null)
                return;

            ChaFileParameter p = file.parameter;
            ChaFileParameter2 p2 = file.parameter2;

            if (p != null)
                p.fullname = Name ?? string.Empty;

            if (p2 != null)
            {
                p2.personality = Personality;
                p2.voiceRate = VoiceRate;
                p2.trait = (byte)Trait;
                p2.mind = (byte)Mind;
                p2.hAttribute = (byte)HAttribute;
            }

            ApplyGameInfo2To(file.gameinfo2);
        }

        /// <summary>
        /// GameInfo2 だけを書き込む。イベント予測用の作業コピーにも使う。
        /// </summary>
        internal void ApplyGameInfo2To(ChaFileGameInfo2 g2)
        {
            if (g2 == null)
                return;

            g2.Favor = Favor;
            g2.Slavery = Slavery;
            g2.Enjoyment = Enjoyment;
            g2.Aversion = Aversion;
            g2.Dependence = Dependence;
            g2.Broken = Broken;

            g2.Dirty = Dirty;
            g2.Tiredness = Tiredness;
            g2.Toilet = Toilet;
            g2.Libido = Libido;

            g2.nowState = (ChaFileDefine.State)NowState;
            g2.nowDrawState = (ChaFileDefine.State)NowState;
            g2.lockNowState = LockNowState;
            g2.lockBroken = LockBroken;
            g2.lockDependence = LockDependence;
        }

        internal string Summary()
        {
            return string.Format(
                "name={0} personality={1} voice={2:0.###} trait={3} mind={4} hAttr={5} / " +
                "Favor={6} Slavery={7} Enjoy={8} Aversion={9} Depend={10} Broken={11} / " +
                "Dirty={12} Tired={13} Toilet={14} Libido={15} / state={16} lock={17}",
                Name, Personality, VoiceRate, Trait, Mind, HAttribute,
                Favor, Slavery, Enjoyment, Aversion, Dependence, Broken,
                Dirty, Tiredness, Toilet, Libido, NowState, LockNowState);
        }
    }
}
