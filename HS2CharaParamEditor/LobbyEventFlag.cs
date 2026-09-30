using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// 待ち合わせの強制イベントフラグ。
    ///
    /// ゲーム側の経路は次のとおり（IL から確定）。
    ///
    ///   [Hを開始する] 押下
    ///       LobbyMapSelectUI.InitList( eventNos[ heroineRommListIdx[0] ] )
    ///           番号 != -1 → 選択可能マップを
    ///                        infoEventContentDic[番号].meetingLocationMaps に差し替える。
    ///                        マップ一覧の表示自体は変わらず、該当以外が
    ///                        選択不可（グレーアウト）になる。
    ///           番号 == -1 → 通常のマップ集合
    ///   [マップ決定]
    ///       Game.eventNo = eventNos[ heroineRommListIdx[0] ]
    ///           -1 のときだけ GlobalHS2Calc.GetGeneralEventNo による通常抽選に回る
    ///
    /// つまり実際に効くのは LobbySceneManager.eventNos[部屋リスト番号] であり、
    /// 部屋リスト番号はゲームが LoadChara の中で
    /// SaveData.FindInRoomListIndex(Path.GetFileNameWithoutExtension(カード名)) を呼んで
    /// heroineRommListIdx[枠] に入れている。
    /// こちらで名前から引き直さず、その計算済みの値をそのまま使う。
    ///
    /// Manager.Game.tableLobbyEvents / tableDesireCharas は
    /// ロビー開始時に eventNos を作る元であり、この場面では読まれない。
    /// 整合のために書けるときだけ書く（失敗しても機能は成立する）。
    ///
    /// なおこの状態はセーブデータ側の実行時状態で、カードには保存されない。
    /// Manager.Game.CharaEventShuffle() が作り直すため、
    /// 効くのは「今開いている待ち合わせ」に対してである。
    /// </summary>
    internal static class LobbyEventFlag
    {
        /// <summary>
        /// イベント番号と行き先マップの対応（v0.10.0.0 で実機のテーブルから確定）。
        ///
        ///   28 入浴中     → マップ 101 風呂
        ///   29 シャワー中 → マップ 100 シャワー
        ///   30 トイレ中   → マップ 103 和式トイレ
        ///   31 トイレ中   → マップ 102 洋式トイレ
        ///   32 睡眠中     → マップ 3,5,7,10,16
        ///
        /// 28/29 と 30/31 は**通常版と自慰版ではない**。
        /// 入浴とシャワー、和式と洋式という行き先の違いである。
        /// v0.9.x まではこれを取り違えており、29 と 31 を選択肢から外していた。
        ///
        /// 各イベントの meetingLocationMaps は1件しか持たないので、
        /// 番号を立てた時点で行き先のマップが確定する。
        /// 汚れ・尿意が満ちたときの抽選は、この対から1つをランダムに選んでいる。
        /// </summary>
        internal const int None = -1;
        internal const int Bath = 28;         // 入浴
        internal const int Shower = 29;       // シャワー
        internal const int ToiletJp = 30;     // 和式トイレ
        internal const int ToiletWest = 31;   // 洋式トイレ
        internal const int Sleep = 32;        // 睡眠

        /// <summary>マップ選択に効くのは1人目の枠だけ。</summary>
        internal const int EffectiveSlot = 0;

        internal static string Describe(int eventID)
        {
            switch (eventID)
            {
                case Bath: return "風呂（入浴）";
                case Shower: return "シャワー";
                case ToiletJp: return "トイレ（和式）";
                case ToiletWest: return "トイレ（洋式）";
                case Sleep: return "睡眠";
            }
            return eventID < 0 ? "なし" : "その他 (" + eventID + ")";
        }

        /// <summary>
        /// プルダウンの選択肢。値はイベント番号そのもの。
        /// 24(初H) や 16(脱走) などゲームが別途入れる番号が立っているときは、
        /// 先頭に「そのまま」を出して温存する（選び直さない限り書き換わらない）。
        /// </summary>
        internal static OptionList BuildChoices(int currentID)
        {
            List<int> values = new List<int>();
            List<string> labels = new List<string>();

            if (currentID >= 0 && !IsKnown(currentID))
            {
                values.Add(currentID);
                labels.Add("そのまま (" + currentID + ")");
            }

            values.Add(None); labels.Add("なし");
            values.Add(Bath); labels.Add("風呂（入浴）");
            values.Add(Shower); labels.Add("シャワー");
            values.Add(ToiletJp); labels.Add("トイレ（和式）");
            values.Add(ToiletWest); labels.Add("トイレ（洋式）");
            values.Add(Sleep); labels.Add("睡眠");

            OptionList list = new OptionList();
            list.Values = values.ToArray();
            list.Labels = labels.ToArray();
            list.FromGame = false;
            return list;
        }

        private static bool IsKnown(int eventID)
        {
            return eventID == Bath || eventID == Shower
                || eventID == ToiletJp || eventID == ToiletWest || eventID == Sleep;
        }

        // ------------------------------------------------------------------

        /// <summary>書き込み先。RoomIndex が eventNos の添字で、これが本体。</summary>
        internal struct Target
        {
            internal int RoomIndex;
            internal string Key;      // tableLobbyEvents のキー。取れないこともある
            internal string Reason;   // 解決できなかったときの理由

            internal bool Valid { get { return RoomIndex >= 0; } }
        }

        private static List<string> RoomList()
        {
            try
            {
                if (!Manager.Game.IsInstance())
                    return null;
                Manager.Game game = Manager.Game.Instance;
                if (game == null)
                    return null;
                SaveData save = game.saveData;
                if (save == null || save.roomList == null)
                    return null;
                int group = save.selectGroup;
                if (group < 0 || group >= save.roomList.Length)
                    return null;
                return save.roomList[group];
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>ゲームが部屋リストの照合に使うのと同じ形（拡張子なしのファイル名）。</summary>
        private static string NameOf(EditTarget target)
        {
            if (target == null)
                return null;
            string path = target.CardPath;
            if (string.IsNullOrEmpty(path) && target.Info != null)
                path = target.Info.FileName;
            if (string.IsNullOrEmpty(path) && target.File != null)
                path = target.File.charaFileName;
            if (string.IsNullOrEmpty(path))
                return null;
            try { return Path.GetFileNameWithoutExtension(path); }
            catch (Exception) { return path; }
        }

        internal static Target Resolve(int slot, EditTarget target)
        {
            Target result = new Target();
            result.RoomIndex = -1;
            result.Key = null;
            result.Reason = null;

            Manager.LobbySceneManager mgr = TargetResolver.Manager_;
            if (mgr == null)
            {
                result.Reason = "ロビーが未初期化です。";
                return result;
            }

            // 1. ゲームが LoadChara で計算済みの部屋番号。枠が確定していればこれが正解。
            try
            {
                int[] idxs = mgr.heroineRommListIdx;
                if (idxs != null && slot >= 0 && slot < idxs.Length)
                    result.RoomIndex = idxs[slot];
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("heroineRommListIdx を参照できません: " + ex.Message);
            }

            string name = NameOf(target);

            // 2. ゲーム本体と同じ手順で名前から引く。
            if (result.RoomIndex < 0 && !string.IsNullOrEmpty(name))
            {
                try { result.RoomIndex = SaveData.FindInRoomListIndex(name); }
                catch (Exception ex) { Plugin.Log.LogWarning("FindInRoomListIndex が使えません: " + ex.Message); }
            }

            List<string> room = RoomList();

            // 3. 大文字小文字や拡張子の差に備えた保険。
            if (result.RoomIndex < 0 && room != null && !string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < room.Count; i++)
                {
                    if (string.IsNullOrEmpty(room[i]))
                        continue;
                    string entry;
                    try { entry = Path.GetFileNameWithoutExtension(room[i]); }
                    catch (Exception) { entry = room[i]; }
                    if (string.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.RoomIndex = i;
                        break;
                    }
                }
            }

            if (room != null && result.RoomIndex >= 0 && result.RoomIndex < room.Count)
                result.Key = room[result.RoomIndex];
            else if (!string.IsNullOrEmpty(name))
                result.Key = name;

            if (!result.Valid)
            {
                result.Reason = "枠が未確定です。キャラを枠に入れてから設定してください。";
                DumpRoomList(name, room);
            }

            return result;
        }

        private static void DumpRoomList(string name, List<string> room)
        {
            try
            {
                string entries = room == null
                    ? "(部屋リストを参照できません)"
                    : string.Join(" / ", room.ToArray());
                Plugin.Log.LogInfo("部屋番号を解決できません。探した名前=" + (name ?? "(なし)")
                                   + " / 部屋リスト=" + entries);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------

        /// <summary>今のイベント番号。実際に参照されるのは eventNos なのでそこを読む。</summary>
        internal static int Get(int slot, EditTarget target)
        {
            Target t = Resolve(slot, target);
            return Get(t);
        }

        internal static int Get(Target t)
        {
            if (!t.Valid)
                return None;
            try
            {
                Manager.LobbySceneManager mgr = TargetResolver.Manager_;
                if (mgr != null && mgr.eventNos != null && t.RoomIndex < mgr.eventNos.Length)
                    return mgr.eventNos[t.RoomIndex];
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("eventNos を読めません: " + ex.Message);
            }
            return None;
        }

        /// <summary>
        /// イベント番号を書き込む。本命は eventNos。
        /// tableLobbyEvents と tableDesireCharas は整合のための副次的な更新で、
        /// 失敗しても待ち合わせからの誘導自体は成立する。
        /// </summary>
        internal static bool Set(int slot, EditTarget target, int eventID, out string message)
        {
            Target t = Resolve(slot, target);
            if (!t.Valid)
            {
                message = "待ち合わせイベント: " + (t.Reason ?? "設定先を特定できません。");
                return false;
            }

            if (eventID >= 0 && !EventTable.Exists(eventID))
            {
                message = "待ち合わせイベント: イベント " + eventID + " がこの環境にありません。";
                return false;
            }

            Manager.LobbySceneManager mgr = TargetResolver.Manager_;
            if (mgr == null || mgr.eventNos == null)
            {
                message = "待ち合わせイベント: eventNos が未初期化です。";
                return false;
            }
            if (t.RoomIndex >= mgr.eventNos.Length)
            {
                message = "待ち合わせイベント: 部屋番号 " + t.RoomIndex + " が範囲外です。";
                return false;
            }

            mgr.eventNos[t.RoomIndex] = eventID;
            SyncTables(t.Key, eventID);

            Plugin.Log.LogInfo("待ち合わせイベントを " + Describe(eventID) + " に設定しました"
                               + "（枠 " + (slot + 1) + " / 部屋 " + t.RoomIndex
                               + " / キー " + (t.Key ?? "(なし)") + "）。");

            message = slot == EffectiveSlot
                ? "待ち合わせイベントを " + Describe(eventID) + " にしました。"
                : "待ち合わせイベントを " + Describe(eventID) + " にしました（マップ選択に効くのは1人目です）。";
            return true;
        }

        /// <summary>ロビー開始時に eventNos を作る元の表。整合のために合わせておく。</summary>
        private static void SyncTables(string key, int eventID)
        {
            if (string.IsNullOrEmpty(key))
                return;
            try
            {
                if (!Manager.Game.IsInstance())
                    return;
                Manager.Game game = Manager.Game.Instance;
                if (game == null)
                    return;
                SaveData save = game.saveData;
                if (save == null)
                    return;
                int group = save.selectGroup;

                Dictionary<string, Manager.Game.EventCharaInfo>[] table = game.tableLobbyEvents;
                if (table != null && group >= 0 && group < table.Length && table[group] != null)
                {
                    Dictionary<string, Manager.Game.EventCharaInfo> dic = table[group];
                    if (eventID < 0)
                    {
                        dic.Remove(key);
                    }
                    else
                    {
                        Manager.Game.EventCharaInfo info;
                        if (!dic.TryGetValue(key, out info) || info == null)
                        {
                            // ゲーム側の生成手順と同じ。fileName と eventID だけを持たせる。
                            info = new Manager.Game.EventCharaInfo();
                            info.fileName = key;
                            dic[key] = info;
                        }
                        info.eventID = eventID;
                    }
                }

                if (game.tableDesireCharas != null)
                {
                    game.tableDesireCharas.Remove(key);
                    if (eventID >= 0)
                    {
                        IReadOnlyList<int> ids = Manager.Game.DesireEventIDs;
                        if (ids != null && ids.Contains(eventID))
                            game.tableDesireCharas.Add(key, eventID);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("イベント表の同期に失敗（動作自体には影響しません）: " + ex.Message);
            }
        }
    }
}
