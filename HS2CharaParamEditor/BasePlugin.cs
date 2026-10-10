using BepInEx;
using BepInEx.Configuration;
using ConfigurationManager;
using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static ConfigurationManager.ConfigurationManager;
using static HS2CharaParamEditor.ConfigLanguage.Localize;

namespace HS2CharaParamEditor
{
    public class BasePlugin : BaseUnityPlugin
    {
        private int configOrder = int.MaxValue;

        public static BasePlugin Instance { get; private set; }

        public BasePlugin() =>
            Instance = this;

        internal ConfigEntry<T> ConfigBind<T>(string section, string key, T defaultValue, string description = null, AcceptableValueBase acceptableValues = null) =>
            Config.Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description ?? "", acceptableValues, new ConfigurationManagerAttributes { Order = configOrder-- }));

        [SuppressMessage("CodeQuality", "IDE0051")]
        [HarmonyTranspiler, HarmonyPatch(typeof(ConfigurationManager.ConfigurationManager), "DrawSinglePlugin", typeof(PluginSettingsData))]
        private static IEnumerable<CodeInstruction> ConfigurationManager_DrawSinglePlugin_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodBase method && method.Name == "DrawCategoryHeader")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, typeof(BasePlugin).GetMethod(nameof(TranslateCategoryName), BindingFlags.Static | BindingFlags.NonPublic));
                }
                yield return instruction;
            }
        }

        private static string TranslateCategoryName(string categoryName, PluginSettingsData plugin) =>
            plugin.Info.GUID == Plugin.GUID
                ? LC(categoryName)
                : categoryName;

        [SuppressMessage("CodeQuality", "IDE0051")]
        [HarmonyTranspiler, HarmonyPatch(typeof(ConfigurationManager.ConfigurationManager), "DrawSettingName", typeof(SettingEntryBase))]
        private static IEnumerable<CodeInstruction> ConfigurationManager_DrawSettingName_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is MethodBase method && method.ReflectedType == typeof(GUIContent))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, typeof(BasePlugin).GetMethod(nameof(TranslateSettingName), BindingFlags.Static | BindingFlags.NonPublic));
                }
            }
        }

        private static GUIContent TranslateSettingName(GUIContent content, SettingEntryBase setting)
        {
            if (setting.PluginInstance == Instance)
            {
                content.text = LC(content.text);
                content.tooltip = LC(content.tooltip);
            }
            return content;
        }
    }
}
