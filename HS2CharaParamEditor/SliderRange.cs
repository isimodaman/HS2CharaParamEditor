using System;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// スライダの上下限。声質は SliderUnlocker の設定に追随させる。
    /// </summary>
    internal static class SliderRange
    {
        private const string SliderUnlockerGuid = "com.bepis.bepinex.sliderunlocker";

        /// <summary>声質の表示範囲。UI 値であり、内部値はこれを 1/100 したもの。</summary>
        internal static float VoiceMin = 0f;
        internal static float VoiceMax = 100f;

        /// <summary>ステータスの範囲。上限解除がオンなら広がる。</summary>
        internal static float StatMin { get { return Plugin.CfgUnlockLimits.Value ? -100f : 0f; } }
        internal static float StatMax { get { return Plugin.CfgUnlockLimits.Value ? 200f : 100f; } }

        internal static void Refresh()
        {
            VoiceMin = 0f;
            VoiceMax = 100f;

            try
            {
                BepInEx.PluginInfo info;
                if (!Chainloader.PluginInfos.TryGetValue(SliderUnlockerGuid, out info) || info.Instance == null)
                {
                    Plugin.Log.LogInfo("SliderUnlocker が見つからないため、声質の範囲は 0-100 とします。");
                    return;
                }

                ConfigFile config = info.Instance.Config;
                float min, max;
                if (TryReadInt(config, "Slider Limits", "Minimum slider value", out min))
                    VoiceMin = min;
                if (TryReadInt(config, "Slider Limits", "Maximum slider value", out max))
                    VoiceMax = max;

                if (VoiceMax <= VoiceMin)
                {
                    Plugin.Log.LogWarning("SliderUnlocker の値が不正のため、声質の範囲を 0-100 に戻します。");
                    VoiceMin = 0f;
                    VoiceMax = 100f;
                }

                Plugin.Log.LogInfo("声質の範囲を " + VoiceMin + " - " + VoiceMax + " に設定しました。");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("SliderUnlocker の設定を読めませんでした: " + ex.Message);
                VoiceMin = 0f;
                VoiceMax = 100f;
            }
        }

        private static bool TryReadInt(ConfigFile config, string section, string key, out float result)
        {
            result = 0f;
            try
            {
                ConfigEntryBase entry = config[new ConfigDefinition(section, key)];
                if (entry == null)
                    return false;
                result = Convert.ToSingle(entry.BoxedValue);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
