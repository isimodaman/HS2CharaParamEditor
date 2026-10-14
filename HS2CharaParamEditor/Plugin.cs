using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using static HS2CharaParamEditor.ConfigLanguage.Localize;
using KeyboardShortcut = BepInEx.Configuration.KeyboardShortcut;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// ロビーの待ち合わせ画面で、選択中のキャラのパラメータを日本語UIで編集する。
    /// 既存の FemaleParameterInfo(FPI) の上位互換。
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("HoneySelect2"), BepInProcess("HoneySelect2VR")]
    [BepInDependency(KKAPI.KoikatuAPI.GUID)]
    public class Plugin : BasePlugin
    {
        public const string GUID = "com.isimodaman.hs2.charaparamediter";
        public const string PluginName = "HS2CharaParamEditor";
        public const string Version = "0.12.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> CfgVisible;
        internal static ConfigEntry<KeyboardShortcut> CfgToggleKey;
        internal static ConfigEntry<bool> CfgBackupOnSave;
        internal static ConfigEntry<bool> CfgUnlockLimits;
        internal static ConfigEntry<string> CfgMergedPersonalities;
        internal static ConfigEntry<string> CfgFontName;
        internal static ConfigEntry<int> CfgFontSize;
        internal static ConfigEntry<float> CfgWindowScale;
        internal static ConfigEntry<Vector2> CfgWindowPosition;
        internal static ConfigEntry<float> CfgWindowOpacity;

        [SuppressMessage("CodeQuality", "IDE0051")]
        private void Awake()
        {
            Log = Logger;

            ConfigLanguage.Bind(this, "Localization", "Language", "Requires to restart the game, if change this.");
            ConfigLanguage.SettingChanged += OnLanguageChanged;

            CfgVisible = ConfigBind(
                "表示", "パネルを表示する", true,
                "待ち合わせ画面に入ったときパネルを開きます。");

            CfgToggleKey = ConfigBind(
                "表示", "パネル開閉キー", new KeyboardShortcut(KeyCode.F6),
                "パネルの表示と非表示を切り替えるキーです。");

            CfgFontName = ConfigBind(
                "表示", "フォント名", LC("Yu Gothic UI"),
                "日本語が表示されない場合は Meiryo や MS Gothic を試してください。");
            CfgFontName.SettingChanged += OnFontChanged;

            CfgFontSize = ConfigBind(
                "表示", "フォントサイズ", 12,
                "パネルの文字サイズです。", new AcceptableValueRange<int>(9, 20));
            CfgFontSize.SettingChanged += OnFontChanged;

            CfgBackupOnSave = ConfigBind(
                "保存", "バックアップを作る", true,
                "カードへの初回保存時に .bak を同じフォルダに残します。");

            CfgUnlockLimits = ConfigBind(
                "保存", "上限解除", false,
                "スライダの入力範囲を -100 から 200 に広げます。ゲームの想定外の値になるため通常は無効のままにしてください。");

            CfgMergedPersonalities = ConfigBind(
                "表示", "選択肢から外す性格", LC("フュル,シトリー"),
                "バニラのキャラメイクに出てこない性格をカンマ区切りで指定します。" +
                "フュルはクール、シトリーは几帳面に統合されているため既定で外しています。");

            CfgWindowScale = ConfigBind("ウィンドウ", "拡縮", 1f, null, new AcceptableValueRange<float>(0.5f, 2f));
            CfgWindowPosition = ConfigBind("ウィンドウ", "位置", new Vector2(-1f, -1f));
            CfgWindowOpacity = ConfigBind("ウィンドウ", "不透明度", 1f, null, new AcceptableValueRange<float>(0.5f, 1f));

            try
            {
                Harmony harmony = new Harmony(GUID);

                // 本体のパッチを先に当てる。
                harmony.PatchAll(typeof(LobbyHooks));

                // ConfigurationManager の項目名を翻訳するパッチ。
                // CM を導入していない環境では型を解決できず例外になるので、
                // ここだけ切り離して握りつぶす。本体の動作には影響しない。
                try
                {
                    harmony.PatchAll(typeof(BasePlugin));
                }
                catch (Exception)
                {
                    Log.LogInfo("ConfigurationManager が見つかりません。設定画面の項目名は翻訳されません。");
                }
                Log.LogInfo(PluginName + " v" + Version + " loaded.");
            }
            catch (Exception ex)
            {
                Log.LogError(LC("Harmony パッチの適用に失敗しました: ") + ex);
            }
        }

        private static void OnLanguageChanged(object sender, EventArgs e)
        {
            NameSources.Rebuild();
            if (LobbyPanel.Instance)
                LobbyPanel.Instance.BuildChoices();
            LobbyPanel.Initialized = false;
        }

        private static void OnFontChanged(object sender, EventArgs e)
        {
            LobbyPanel.DestroyFont();
        }
    }
}
