using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// ロビーの待ち合わせ画面で、選択中のキャラのパラメータを日本語UIで編集する。
    /// 既存の FemaleParameterInfo(FPI) の上位互換。
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KKAPI.KoikatuAPI.GUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.isimodaman.hs2.charaparamediter";
        public const string PluginName = "HS2CharaParamEditor";
        public const string Version = "0.10.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> CfgVisible;
        internal static ConfigEntry<KeyboardShortcut> CfgToggleKey;
        internal static ConfigEntry<bool> CfgSaveCard;
        internal static ConfigEntry<bool> CfgBackupOnSave;
        internal static ConfigEntry<bool> CfgUnlockLimits;
        internal static ConfigEntry<string> CfgMergedPersonalities;
        internal static ConfigEntry<string> CfgFontName;
        internal static ConfigEntry<int> CfgFontSize;
        internal static ConfigEntry<float> CfgWindowX;
        internal static ConfigEntry<float> CfgWindowY;

        private void Awake()
        {
            Log = Logger;

            CfgVisible = Config.Bind(
                "表示", "パネルを表示する", true,
                "待ち合わせ画面に入ったときパネルを開きます。");

            CfgToggleKey = Config.Bind(
                "表示", "パネル開閉キー", new KeyboardShortcut(KeyCode.F6),
                "パネルの表示と非表示を切り替えるキーです。");

            CfgFontName = Config.Bind(
                "表示", "フォント名", "Yu Gothic UI",
                "日本語が表示されない場合は Meiryo や MS Gothic を試してください。");

            CfgFontSize = Config.Bind(
                "表示", "フォントサイズ", 12,
                new ConfigDescription("パネルの文字サイズです。", new AcceptableValueRange<int>(9, 20)));

            CfgSaveCard = Config.Bind(
                "保存", "カードにも書き戻す", true,
                "適用時にキャラカード(.png)へも保存します。オフにするとゲーム内の状態だけが変わります。");

            CfgBackupOnSave = Config.Bind(
                "保存", "バックアップを作る", true,
                "カードへの初回保存時に .bak を同じフォルダに残します。");

            CfgUnlockLimits = Config.Bind(
                "保存", "上限解除", false,
                "スライダの入力範囲を -100 から 200 に広げます。ゲームの想定外の値になるため通常は無効のままにしてください。");

            CfgMergedPersonalities = Config.Bind(
                "表示", "選択肢から外す性格", "フュル,シトリー",
                "バニラのキャラメイクに出てこない性格をカンマ区切りで指定します。" +
                "フュルはクール、シトリーは几帳面に統合されているため既定で外しています。");

            CfgWindowX = Config.Bind("内部", "ウィンドウ位置X", -1f, "ドラッグすると自動で更新されます。");
            CfgWindowY = Config.Bind("内部", "ウィンドウ位置Y", -1f, "ドラッグすると自動で更新されます。");

            try
            {
                new Harmony(GUID).PatchAll(typeof(LobbyHooks));
                Log.LogInfo(PluginName + " v" + Version + " loaded.");
            }
            catch (Exception ex)
            {
                Log.LogError("Harmony パッチの適用に失敗しました: " + ex);
            }
        }
    }
}
