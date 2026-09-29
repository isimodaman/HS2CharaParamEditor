using System;
using HarmonyLib;
using HS2;
using UnityEngine;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// ロビーへの差し込み。パネルの生成と、選択キャラの追従。
    /// </summary>
    internal static class LobbyHooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Manager.LobbySceneManager), "Start")]
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
                Plugin.Log.LogError("パネルの生成に失敗しました: " + ex);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LobbySelectUI), "ItemSelectAction")]
        private static void ItemSelectAction(int __0)
        {
            LobbyPanel.NotifySelected(__0);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LobbySelectUI), "SetEntryCharaNo")]
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
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Manager.LobbySceneManager), "SetModeCanvasGroup")]
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
