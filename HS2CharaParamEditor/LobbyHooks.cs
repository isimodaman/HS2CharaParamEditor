using HarmonyLib;
using HS2;
using System;
using static HS2CharaParamEditor.ConfigLanguage.Localize;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// ロビーへの差し込み。パネルの生成と、選択キャラの追従。
    /// </summary>
    internal static class LobbyHooks
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Manager.LobbySceneManager), nameof(Manager.LobbySceneManager.Start))]
        private static void LobbySceneManagerStart(Manager.LobbySceneManager __instance)
        {
            try
            {
                if (__instance == null)
                    return;
                if (__instance.GetComponent<LobbyPanel>() == null)
                    __instance.gameObject.AddComponent<LobbyPanel>();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(LC("パネルの生成に失敗しました: ") + ex);
            }
        }

        /// <summary>
        /// バニラ右側のパラメータ表示が差し替わったときに呼ばれる。
        /// 一覧をクリックして見ている対象が変わるとここを通るので、
        /// パネルの追従にはこの信号を使う。
        ///
        /// オーバーロードが2つあるので型で指定する。
        /// GameCharaFileInfo 版が一覧のカード、ChaFileControl 版が枠の実体。
        /// 引数は (_info, _eventNo, _entryNo) なので __2 が枠番号。
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(LobbyParameterUI), nameof(LobbyParameterUI.SetParameter),
            typeof(GameLoadCharaFileSystem.GameCharaFileInfo), typeof(int), typeof(int))]
        private static void SetParameterByInfo(int __2)
        {
            LobbyPanel.NotifyShown(__2);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(LobbyParameterUI), nameof(LobbyParameterUI.SetParameter),
            typeof(AIChara.ChaFileControl), typeof(int), typeof(int))]
        private static void SetParameterByFile(int __2)
        {
            LobbyPanel.NotifyShown(__2);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(LobbySelectUI), nameof(LobbySelectUI.SetEntryCharaNo))]
        private static void SetEntryCharaNo(int __0)
        {
            LobbyPanel.NotifySelected(__0);
        }

        /// <summary>
        /// 待ち合わせとマップ選択の切り替え。
        /// 「Hを開始する」で 1、マップ選択の「戻る」で 0 が渡される。
        /// バニラ右側のパラメータパネルが消えるのと同じタイミングなので、
        /// こちらのパネルもそれに追随させる。
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(Manager.LobbySceneManager), nameof(Manager.LobbySceneManager.SetModeCanvasGroup))]
        private static void SetModeCanvasGroup(int __0)
        {
            LobbyPanel.NotifyMode(__0);
        }
    }

    /// <summary>イベント番号がこの環境に存在するかを見る。書き込み前の確認に使う。</summary>
    internal static class EventTable
    {
        internal static bool Exists(int eventNo)
        {
            try
            {
                if (!Manager.Game.IsInstance())
                    return false;
                Manager.Game game = Manager.Game.Instance;
                if (game == null || game.infoEventContentDic == null)
                    return false;
                return game.infoEventContentDic.ContainsKey(eventNo);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
