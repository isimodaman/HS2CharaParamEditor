using System;
using System.Collections.Generic;
using Actor;
using AIChara;
using GameLoadCharaFileSystem;
using HarmonyLib;
using HS2;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// 編集対象。枠に Heroine が入っていればそれ、
    /// まだ一覧を見ているだけならカードファイルそのものが対象になる。
    /// </summary>
    internal class EditTarget
    {
        internal ChaFileControl File;
        internal Heroine Heroine;             // null なら枠未割り当て（カードのみ）
        internal GameCharaFileInfo Info;      // 一覧の表示情報。あれば適用後に更新する
        internal string CardPath;
        internal string DisplayName = string.Empty;

        internal bool IsLive { get { return Heroine != null; } }
    }

    /// <summary>
    /// 枠番号から編集対象を解決する。
    /// </summary>
    internal static class TargetResolver
    {
        private static readonly Dictionary<string, ChaFileControl> CardCache =
            new Dictionary<string, ChaFileControl>();

        /// <summary>LobbySelectUI.scrollCtrl は private なのでリフレクションで取る。</summary>
        private static object GetScrollCtrl(LobbySelectUI ui)
        {
            try
            {
                if (ui == null)
                    return null;
                return Traverse.Create(ui).Field("scrollCtrl").GetValue();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("scrollCtrl を取得できません: " + ex.Message);
                return null;
            }
        }

        internal static Manager.LobbySceneManager Manager_
        {
            get
            {
                try
                {
                    return Manager.LobbySceneManager.IsInstance() ? Manager.LobbySceneManager.Instance : null;
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>枠の数。通常は 2（1人目 / 2人目）。</summary>
        internal static int SlotCount
        {
            get
            {
                Manager.LobbySceneManager mgr = Manager_;
                if (mgr != null && mgr.heroines != null && mgr.heroines.Length > 0)
                    return mgr.heroines.Length;
                return 2;
            }
        }

        /// <summary>ゲーム側が今どちらの枠を選ばせているか。</summary>
        internal static int GameActiveSlot
        {
            get
            {
                try
                {
                    Manager.LobbySceneManager mgr = Manager_;
                    if (mgr != null && mgr.SelectUI != null)
                        return mgr.SelectUI.EntryCharaNo;
                }
                catch (Exception) { }
                return -1;
            }
        }

        /// <summary>
        /// 一覧で今見ているカード情報。枠に入れる前のキャラはここからしか取れない。
        ///
        /// scrollCtrl.selectInfo は「確定選択」しないと入らないため、
        /// 一覧を眺めているだけの状態では null になる。
        /// バニラ右パネルの LobbyParameterUI.gameCharainfo は
        /// SetParameter(GameCharaFileInfo,...) のたびに更新されるので、
        /// カーソルを合わせただけのキャラでもこちらなら取れる。
        /// </summary>
        private static GameCharaFileInfo GetBrowsingInfo()
        {
            Manager.LobbySceneManager mgr = Manager_;
            if (mgr == null)
                return null;

            // 1. バニラの情報パネルが今表示しているカード
            try
            {
                if (mgr.ParameterUI != null)
                {
                    GameCharaFileInfo shown = Traverse.Create(mgr.ParameterUI)
                                                      .Field("gameCharainfo")
                                                      .GetValue<GameCharaFileInfo>();
                    if (shown != null && !string.IsNullOrEmpty(shown.FullPath))
                        return shown;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("情報パネルの表示対象を取得できません: " + ex.Message);
            }

            // 2. スクロールリストの確定選択
            try
            {
                if (mgr.SelectUI == null)
                    return null;

                object ctrl = GetScrollCtrl(mgr.SelectUI);
                if (ctrl == null)
                    return null;

                object data = Traverse.Create(ctrl).Property("selectInfo").GetValue();
                if (data == null)
                    return null;

                return Traverse.Create(data).Field("info").GetValue<GameCharaFileInfo>();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("一覧の選択情報を取得できません: " + ex.Message);
                return null;
            }
        }

        internal static EditTarget Resolve(int slot)
        {
            Manager.LobbySceneManager mgr = Manager_;
            if (mgr == null)
                return null;

            // 1. 枠に割り当て済みならそれが本体
            try
            {
                if (mgr.heroines != null && slot >= 0 && slot < mgr.heroines.Length)
                {
                    Heroine h = mgr.heroines[slot];
                    if (h != null && h.chaFile != null)
                    {
                        return new EditTarget
                        {
                            File = h.chaFile,
                            Heroine = h,
                            Info = FindInfo(h.chaFile.charaFileName),
                            CardPath = h.chaFile.charaFileName,
                            DisplayName = h.chaFile.parameter != null ? h.chaFile.parameter.fullname : string.Empty
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("枠 " + slot + " の参照に失敗: " + ex.Message);
            }

            // 2. 未割り当てなら、その枠を選んでいる最中に限り一覧のハイライトを対象にする
            if (slot != GameActiveSlot)
                return null;

            GameCharaFileInfo info = GetBrowsingInfo();
            if (info == null || string.IsNullOrEmpty(info.FullPath))
                return null;

            ChaFileControl file = LoadCard(info.FullPath);
            if (file == null)
                return null;

            return new EditTarget
            {
                File = file,
                Heroine = null,
                Info = info,
                CardPath = info.FullPath,
                DisplayName = file.parameter != null ? file.parameter.fullname : (info.name ?? string.Empty)
            };
        }

        private static GameCharaFileInfo FindInfo(string charaFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(charaFileName))
                    return null;
                Manager.LobbySceneManager mgr = Manager_;
                if (mgr == null || mgr.SelectUI == null)
                    return null;

                object ctrl = GetScrollCtrl(mgr.SelectUI);
                if (ctrl == null)
                    return null;

                string key = System.IO.Path.GetFileNameWithoutExtension(charaFileName);
                object data = Traverse.Create(ctrl).Method("FindInfoByFileName", new object[] { key }).GetValue();
                if (data == null)
                    return null;
                return Traverse.Create(data).Field("info").GetValue<GameCharaFileInfo>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// カードを読む。範囲チェックは切る（SliderUnlocker 域の値が丸められるのを防ぐため）。
        /// PNG とステータスも含めて全部読むので、そのまま保存し直しても欠落しない。
        /// </summary>
        private static ChaFileControl LoadCard(string path)
        {
            ChaFileControl cached;
            if (CardCache.TryGetValue(path, out cached) && cached != null)
                return cached;

            try
            {
                ChaFileControl file = new ChaFileControl();
                file.skipRangeCheck = true;
                if (!file.LoadCharaFile(path, 255, false, false))
                {
                    Plugin.Log.LogWarning("カードを読めませんでした: " + path);
                    return null;
                }
                CardCache[path] = file;
                Plugin.Log.LogInfo("カードを読み込みました: " + path);
                return file;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("カード読み込みで例外: " + ex);
                return null;
            }
        }

        internal static void ClearCache()
        {
            CardCache.Clear();
        }
    }
}
