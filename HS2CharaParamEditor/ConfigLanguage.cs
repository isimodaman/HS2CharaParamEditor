using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Xml;
using UnityEngine;

namespace HS2CharaParamEditor
{
    public static class ConfigLanguage
    {
        public const SystemLanguage DefaultLanguage = SystemLanguage.Japanese;

        private static readonly Dictionary<SystemLanguage, Dictionary<string, string>> lookup = new Dictionary<SystemLanguage, Dictionary<string, string>>();
        private static PropertyInfo configEntryValueProperty;
        private static EventInfo configEntrySettingChangedEvent;

        public static Type Type { get; private set; }
        public static object Instance { get; private set; }
        public static SystemLanguage DefaultValue { get; private set; }
        public static SystemLanguage Value
        {
            get => (SystemLanguage)configEntryValueProperty?.GetValue(Instance);
            set => configEntryValueProperty?.SetValue(Instance, Enum.ToObject(Type, value));
        }
        public static event EventHandler SettingChanged
        {
            add => configEntrySettingChangedEvent?.AddEventHandler(Instance,
                Delegate.CreateDelegate(configEntrySettingChangedEvent.EventHandlerType, value.Target, value.Method));
            remove => configEntrySettingChangedEvent?.RemoveEventHandler(Instance,
                Delegate.CreateDelegate(configEntrySettingChangedEvent.EventHandlerType, value.Target, value.Method));
        }

        static ConfigLanguage()
        {
            try
            {
                var xml = new XmlDocument();
                xml.Load(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".xml"));
                var root = xml.SelectSingleNode("HS2CharaParamEditor");
                if (root == null)
                    return;
                var localizes = root.SelectNodes("localize");
                if (localizes == null)
                    return;
                foreach (XmlNode localize in localizes)
                {
                    var languageText = localize.Attributes["language"]?.InnerText;
                    if (languageText == null || !Enum.TryParse<SystemLanguage>(languageText, true, out var language))
                        continue;
                    var table = new Dictionary<string, string>();
                    lookup[language] = table;
                    var nodes = localize.SelectNodes("replace");
                    if (nodes == null)
                        continue;
                    foreach (XmlNode node in nodes)
                    {
                        var target = node.Attributes["target"]?.InnerText;
                        if (target == null)
                            continue;
                        var value = node.Attributes["value"]?.InnerText;
                        if (value == null)
                            continue;
                        table[target] = value;
                    }
                }
            }
            catch
            {
            }
        }

        public static bool Bind(BasePlugin basePlugin, string section, string key, string description)
        {
            const string typeName = nameof(HS2CharaParamEditor) + "." + nameof(ConfigLanguage) + "+Language";

            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName();
            var assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            var fileName = Path.GetFileName(assembly.Location);
            var moduleName = Path.GetFileNameWithoutExtension(fileName);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(moduleName, fileName);
            var enumBuilder = moduleBuilder.DefineEnum(typeName, TypeAttributes.Public, typeof(int));
            var values = new[] { DefaultLanguage }
                .Concat(lookup.Keys)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            for (var i = 0; i < values.Length; i++)
                enumBuilder.DefineLiteral(values[i].ToString(), (int)values[i]);
            Type = enumBuilder.CreateType();
            var type = typeof(ConfigEntry<>).MakeGenericType(new[] { Type });
            configEntryValueProperty = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (configEntryValueProperty is null)
                return false;
            configEntrySettingChangedEvent = type.GetEvent("SettingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (configEntrySettingChangedEvent is null)
                return false;
            var language = Application.systemLanguage;
            if (!Enum.IsDefined(Type, (int)language))
                language = DefaultLanguage;
            DefaultValue = language;
            ParameterInfo[] parameters;
            var bindMethod = typeof(BasePlugin).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(method =>
                method.Name == nameof(BasePlugin.ConfigBind) &&
                (parameters = method.GetParameters()).Length == 5 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(string) &&
                parameters[2].ParameterType.IsGenericParameter &&
                parameters[3].ParameterType == typeof(string) &&
                parameters[4].ParameterType == typeof(AcceptableValueBase) &&
                method.ReturnType.IsGenericType
            ).MakeGenericMethod(new[] { Type });
            Instance = bindMethod.Invoke(basePlugin, new object[] { section, key, Enum.ToObject(Type, language), description, null });
            return Instance != null;
        }

        public static class Localize
        {
            public static string LC(string format, params object[] args)
            {
                if (!string.IsNullOrEmpty(format) &&
                    lookup.TryGetValue(Value, out var table) &&
                    table.TryGetValue(format, out var translatedText))
                {
                    format = translatedText;
                }
                return string.Format(format, args);
            }

            public static string LC(string[] formats, params object[] args)
            {
                string format = null;
                if (!formats.IsNullOrEmpty() &&
                    (format = formats.LastOrDefault(s => !string.IsNullOrEmpty(s))) != null &&
                    lookup.TryGetValue(Value, out var table))
                {
                    foreach (var untranslatedText in formats)
                    {
                        if (!string.IsNullOrEmpty(untranslatedText) &&
                            table.TryGetValue(untranslatedText, out var translatedText))
                        {
                            format = translatedText;
                            break;
                        }
                    }
                }
                return string.Format(format, args);
            }

            public static bool LC(string format, out string translatedText, params object[] args)
            {
                bool result = false;
                if (!string.IsNullOrEmpty(format) &&
                    lookup.TryGetValue(Value, out var table) &&
                    table.TryGetValue(format, out var text))
                {
                    result = true;
                    format = text;
                }
                translatedText = string.Format(format, args);
                return result;
            }
        }
    }
}
