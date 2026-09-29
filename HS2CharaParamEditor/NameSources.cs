using System;
using System.Collections.Generic;

namespace HS2CharaParamEditor
{
    internal class OptionList
    {
        internal int[] Values = new int[0];
        internal string[] Labels = new string[0];
        internal bool FromGame;

        internal int Count { get { return Values.Length; } }

        internal int IndexOfValue(int value)
        {
            for (int i = 0; i < Values.Length; i++)
                if (Values[i] == value)
                    return i;
            return -1;
        }

        internal string LabelOfValue(int value)
        {
            int i = IndexOfValue(value);
            return i >= 0 ? Labels[i] : value + " (未定義)";
        }
    }

    /// <summary>
    /// 選択肢の日本語名。すべてゲームが実行時に持っているテーブルから引く。
    /// 性格はキャラメイク限定の CustomBase ではなく、
    /// ロビーでも使える Manager.Voice.infoTable から取る。
    /// </summary>
    internal static class NameSources
    {
        internal static OptionList Personality = new OptionList();
        internal static OptionList Trait = new OptionList();
        internal static OptionList Mind = new OptionList();
        internal static OptionList HAttribute = new OptionList();
        internal static OptionList NowState = new OptionList();

        private static bool _built;

        internal static void EnsureBuilt()
        {
            if (_built)
                return;
            Rebuild();
        }

        internal static void Rebuild()
        {
            Personality = BuildPersonality();
            Trait = Build("特性", SafeTable("infoTraitTable"));
            Mind = Build("心情", SafeTable("infoMindTable"));
            HAttribute = Build("H属性", SafeTable("infoHAttributeTable"));
            NowState = Build("状態", SafeTable("infoStateTable"));
            _built = true;
        }

        private static IEnumerable<KeyValuePair<int, string>> SafeTable(string which)
        {
            try
            {
                switch (which)
                {
                    case "infoTraitTable": return Manager.Game.infoTraitTable;
                    case "infoMindTable": return Manager.Game.infoMindTable;
                    case "infoHAttributeTable": return Manager.Game.infoHAttributeTable;
                    case "infoStateTable": return Manager.Game.infoStateTable;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(which + " を取得できません: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 性格の選択肢。バニラのキャラメイクは CustomControl.Initialize で
        /// Manager.Voice.infoTable を Param.No >= 0 で絞ってから dictPersonality に入れている。
        /// 負番号のエントリ（フュル・シトリー等）は選択肢に出さないのが本来の仕様。
        /// 表示名も Param.Get(languageInt) を使い、言語設定に追随させる。
        /// </summary>
        private static OptionList BuildPersonality()
        {
            List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>();
            int dropped = 0;

            try
            {
                var table = Manager.Voice.infoTable;
                if (table != null)
                {
                    int lang = 0;
                    try { lang = Manager.GameSystem.Instance.languageInt; }
                    catch (Exception) { }

                    foreach (var kv in table)
                    {
                        VoiceInfo.Param prm = kv.Value;
                        if (prm == null)
                            continue;

                        // バニラと同じ絞り込み。
                        if (prm.No < 0)
                        {
                            dropped++;
                            continue;
                        }

                        string label = null;
                        try { label = prm.Get(lang); }
                        catch (Exception) { }
                        if (string.IsNullOrEmpty(label))
                            label = prm.Personality;

                        if (IsMergedAway(label))
                        {
                            dropped++;
                            continue;
                        }

                        list.Add(new KeyValuePair<int, string>(prm.No, label));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Manager.Voice.infoTable を取得できません: " + ex.Message);
            }

            if (dropped > 0)
                Plugin.Log.LogInfo("性格の選択肢から " + dropped + " 件を除外しました（バニラ非表示分）。");

            return Build("性格", list.Count > 0 ? list : null);
        }

        /// <summary>
        /// バニラで統合されている性格。フュルはクール、シトリーは几帳面に相当するため、
        /// 選択肢には出さない。Config で書き換えられる。
        /// </summary>
        private static bool IsMergedAway(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            string cfg = Plugin.CfgMergedPersonalities.Value;
            if (string.IsNullOrEmpty(cfg))
                return false;

            string[] parts = cfg.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), label.Trim(), StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static OptionList Build(string label, IEnumerable<KeyValuePair<int, string>> table)
        {
            OptionList list = new OptionList();
            List<int> values = new List<int>();
            List<string> labels = new List<string>();

            if (table != null)
            {
                foreach (KeyValuePair<int, string> kv in table)
                {
                    values.Add(kv.Key);
                    labels.Add(string.IsNullOrEmpty(kv.Value) ? kv.Key.ToString() : kv.Value);
                }
            }

            if (values.Count == 0)
            {
                Plugin.Log.LogWarning(label + " の名称テーブルが空のため、番号のみの選択肢にします。");
                for (int i = 0; i < 32; i++)
                {
                    values.Add(i);
                    labels.Add(i.ToString());
                }
                list.FromGame = false;
            }
            else
            {
                list.FromGame = true;
            }

            int[] order = new int[values.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, delegate(int a, int b) { return values[a].CompareTo(values[b]); });

            list.Values = new int[order.Length];
            list.Labels = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                list.Values[i] = values[order[i]];
                list.Labels[i] = labels[order[i]];
            }

            Plugin.Log.LogInfo(label + " の選択肢を " + list.Count + " 件読み込みました"
                               + (list.FromGame ? "" : "（フォールバック）") + "。");
            return list;
        }
    }
}
