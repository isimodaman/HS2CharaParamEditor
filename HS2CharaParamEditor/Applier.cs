using System;
using System.IO;
using System.Reflection;
using Actor;
using AIChara;
using GameLoadCharaFileSystem;
using HS2;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// 適用処理。枠に Heroine がいる場合の手順は FPI の実装を解析して揃えてある。
    /// 未割り当ての場合はカードだけを書き換え、一覧の表示を更新する。
    /// </summary>
    internal static class Applier
    {
        private const byte SexFromCard = 255;

        private static readonly FieldInfo VoiceCacheField =
            typeof(Heroine).GetField("cachedVoiceNo", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static string DescribeRoster()
        {
            try
            {
                Manager.LobbySceneManager mgr = TargetResolver.Manager_;
                if (mgr == null)
                    return "ロビー未初期化";
                if (mgr.heroines == null)
                    return "heroines = null";

                int filled = 0;
                for (int i = 0; i < mgr.heroines.Length; i++)
                    if (mgr.heroines[i] != null)
                        filled++;
                return "枠 " + mgr.heroines.Length + " / 在席 " + filled
                       + " / ゲーム側の選択枠 " + TargetResolver.GameActiveSlot;
            }
            catch (Exception ex)
            {
                return "取得失敗: " + ex.Message;
            }
        }

        internal static bool Apply(EditTarget target, ParamState state, out string message)
        {
            message = string.Empty;

            if (target == null || target.File == null)
            {
                message = "編集対象がありません。";
                return false;
            }

            // 状態を手で変えたなら、CalcState に上書きされないようロックを立てる。
            if (state.StateEditedByHand && !state.LockNowState)
            {
                state.LockNowState = true;
                Plugin.Log.LogInfo("状態を手動変更したため、状態ロックを自動で有効にしました。");
            }

            state.ApplyTo(target.File);
            Plugin.Log.LogInfo("適用（" + (target.IsLive ? "実体+カード" : "カードのみ") + "）: " + state.Summary());

            // 状態の再計算。ロックが立っていれば手動値が保たれる。
            try
            {
                int personality = target.IsLive
                    ? target.Heroine.personality
                    : (target.File.parameter2 != null ? target.File.parameter2.personality : 0);
                GlobalHS2Calc.CalcState(target.File.gameinfo2, personality);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("CalcState の呼び出しに失敗しました: " + ex.Message);
            }

            if (target.IsLive)
                ClearVoiceCache(target.Heroine);

            bool cardSaved = false;
            if (Plugin.CfgSaveCard.Value)
                cardSaved = SaveCard(target);

            RefreshInfo(target);
            RefreshLobbyUI(target);

            message = cardSaved
                ? (target.IsLive ? "適用してカードにも保存しました。" : "カードに保存しました。")
                : "ゲーム内に適用しました。";
            return true;
        }

        private static bool SaveCard(EditTarget target)
        {
            try
            {
                string name = target.CardPath;
                if (string.IsNullOrEmpty(name))
                {
                    Plugin.Log.LogWarning("保存先のファイル名が空のため保存を見送りました。");
                    return false;
                }

                if (Plugin.CfgBackupOnSave.Value)
                    MakeBackup(target.File, name);

                bool ok = target.File.SaveCharaFile(name, SexFromCard, false);
                if (!ok)
                    Plugin.Log.LogWarning("カードの保存に失敗しました。");
                return ok;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("カード保存で例外: " + ex);
                return false;
            }
        }

        private static void MakeBackup(ChaFileControl file, string name)
        {
            try
            {
                string path = Path.IsPathRooted(name)
                    ? name
                    : file.ConvertCharaFilePath(name, SexFromCard, false);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                string backup = path + ".bak";
                if (File.Exists(backup))
                    return;

                File.Copy(path, backup);
                Plugin.Log.LogInfo("バックアップを作成しました: " + backup);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("バックアップの作成に失敗しました: " + ex.Message);
            }
        }

        /// <summary>
        /// Heroine.voiceNo は一度読むと cachedVoiceNo に保持され続けるため、
        /// 性格を変えたらここを消さないと古いボイスが鳴る。
        /// </summary>
        private static void ClearVoiceCache(Heroine heroine)
        {
            if (VoiceCacheField == null)
            {
                Plugin.Log.LogWarning("cachedVoiceNo が見つかりません。ボイスの更新が遅れる可能性があります。");
                return;
            }
            try { VoiceCacheField.SetValue(heroine, null); }
            catch (Exception ex) { Plugin.Log.LogWarning("ボイスキャッシュのクリアに失敗: " + ex.Message); }
        }

        /// <summary>一覧のサムネ横に出ている表示情報を更新する。</summary>
        private static void RefreshInfo(EditTarget target)
        {
            try
            {
                GameCharaFileInfo info = target.Info;
                if (info == null)
                    return;

                ChaFileParameter p = target.File.parameter;
                ChaFileParameter2 p2 = target.File.parameter2;
                ChaFileGameInfo2 g2 = target.File.gameinfo2;

                if (p != null)
                    info.name = p.fullname;
                if (p2 != null)
                {
                    info.trait = p2.trait;
                    info.hAttribute = p2.hAttribute;
                    info.personality = NameSources.Personality.LabelOfValue(p2.personality);
                }
                if (g2 != null)
                {
                    info.state = g2.nowState;
                    info.broken = g2.Broken;
                    info.dependence = g2.Dependence;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("一覧表示の更新に失敗: " + ex.Message);
            }
        }

        /// <summary>バニラ側の表示を更新する。</summary>
        private static void RefreshLobbyUI(EditTarget target)
        {
            try
            {
                Manager.LobbySceneManager mgr = TargetResolver.Manager_;
                if (mgr == null)
                    return;

                if (mgr.ParameterUI != null)
                {
                    if (target.IsLive)
                        mgr.ParameterUI.SetParameter(target.File, -1, TargetResolver.GameActiveSlot);
                    else if (target.Info != null)
                        mgr.ParameterUI.SetParameter(target.Info, -1, TargetResolver.GameActiveSlot);
                }

                if (target.IsLive)
                    mgr.SetCharaAnimationAndPosition();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ロビー表示の更新に失敗しました: " + ex.Message);
            }
        }
    }
}
