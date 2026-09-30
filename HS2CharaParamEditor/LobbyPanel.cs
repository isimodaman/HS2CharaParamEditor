using System;
using Actor;
using UnityEngine;

namespace HS2CharaParamEditor
{
    /// <summary>
    /// ロビー待ち合わせ画面に出す IMGUI パネル。
    /// </summary>
    internal class LobbyPanel : MonoBehaviour
    {
        private const int MainWindowId = 5482311;
        private const int PopupWindowId = 5482312;
        private const string PanelTitle = "CharaParamEditor ver." + Plugin.Version;
        // 幅はステータス行（ラベル+スライダ+値 の2列）がいちばん広くなるように取ってある。
        //   44 + 78 + 32 + 4 + 44 + 78 + 32 = 312、コントロール間の余白を足して約 340。
        // WindowWidth はこれに枠の内側余白（左右 8 ずつ）と若干のマージンを足した値。
        // 他の行（メンタル・状態・最下段のボタン）はこれより狭くなるよう各幅を決めている。
        private const float WindowWidth = 364f;
        private const float LabelW = 44f;
        private const float DropW = 108f;
        private const float SliderW = 78f;
        private const float ValueW = 32f;
        private const float ToggleW = 48f;      // 状態の 固定 / 破綻 / 依存
        private const float BottomMargin = 2f;

        private const int DdPersonality = 0;
        private const int DdTrait = 1;
        private const int DdMind = 2;
        private const int DdHAttribute = 3;
        private const int DdState = 4;
        private const int DdLobbyEvent = 5;
        private const int DdCount = 6;

        internal static LobbyPanel Instance;

        private readonly ParamState _state = new ParamState();
        private int _slot;                       // パネルが今見ている枠（タブ）
        private int _lastGameSlot = int.MinValue;
        private EditTarget _target;
        private string _charaName = string.Empty;
        private bool _idleLogged;
        private bool _voiceDragged;             // 声質スライダを動かした。離した時に試聴する
        private bool _wasForeground;            // 前フレームでパネルが出ていたか

        private Rect _rect = new Rect(60f, 60f, WindowWidth, 300f);
        private string _message = string.Empty;
        private float _messageUntil;

        private int _openDropdown = -1;
        private int _mode;                       // 0 = 待ち合わせ / 1 = マップ選択
        private bool _modeKnown;
        private Rect _popupRect;
        private Vector2 _popupScroll;

        // 各ドロップダウンのボタン位置（ウィンドウ内座標）。
        // GetLastRect が正しい値を返すのは Repaint のときだけなので、毎フレーム控えておく。
        private readonly Rect[] _ddAnchors = new Rect[DdCount];

        private bool _secProfile = true;
        private bool _secMental = true;
        private bool _secStatus = true;
        private bool _secDesire = true;
        private bool _secState = true;
        private bool _secEvent = true;

        // 待ち合わせイベントフラグ。カードではなくセーブデータ側の状態。
        private OptionList _eventChoices = new OptionList();
        private int _eventCurrentId = LobbyEventFlag.None;
        private int _eventChoice = LobbyEventFlag.None;
        private bool _eventAvailable;
        private string _eventNote = string.Empty;

        private static Font _font;
        private static int _fontSize;

        private void Awake()
        {
            Instance = this;
            Plugin.Log.LogInfo("待ち合わせ画面にパネルを生成しました。");
            NameSources.EnsureBuilt();
            SliderRange.Refresh();
            RestorePosition();
        }

        private void OnDestroy()
        {
            VoicePreview.Stop();
            if (Instance == this)
                Instance = null;
        }

        internal static void NotifySelected(int entryNo)
        {
            if (Instance != null)
                Instance.SwitchSlot(entryNo);
        }

        /// <summary>ロビー側の画面モードが変わった。</summary>
        internal static void NotifyMode(int mode)
        {
            if (Instance == null)
                return;
            Instance._mode = mode;
            Instance._modeKnown = true;
            if (mode != 0)
                Instance._openDropdown = -1;
        }

        /// <summary>
        /// 待ち合わせ画面が前面にあるか。
        /// マップ選択へ遷移すると SetModeCanvasGroup(1) が、
        /// 「戻る」で SetModeCanvasGroup(0) が走る。
        ///
        /// 通知を受け取る前は待ち合わせと見なしてよい。
        /// マップ選択へは「Hを開始する」を押すしか到達経路が無く、
        /// そのハンドラ自身が上の呼び出しを行うためである。
        /// </summary>
        private bool IsLobbyForeground()
        {
            try
            {
                Manager.LobbySceneManager mgr = TargetResolver.Manager_;
                if (mgr == null)
                    return false;

                if (mgr.IsADVShow)
                    return false;

                if (_modeKnown && _mode != 0)
                    return false;
            }
            catch (Exception)
            {
                // ロビー初期化中。次のフレームで拾う。
            }
            return true;
        }

        /// <summary>タブを切り替える。ゲーム側で枠が切り替わったときにも呼ぶ。</summary>
        private void SwitchSlot(int slot)
        {
            if (slot < 0)
                return;
            _slot = slot;
            LoadSlot();
        }

        private void LoadSlot()
        {
            try
            {
                _target = TargetResolver.Resolve(_slot);
                if (_target == null || _target.File == null)
                {
                    _charaName = string.Empty;
                    _state.Loaded = false;
                    _eventAvailable = false;
                    _openDropdown = -1;

                    // この状態ではパネルを出さないので、原因はログに残す。
                    // 枠が埋まるまで 20 フレームごとにここへ来るため、入ったときだけ書く。
                    if (!_idleLogged)
                    {
                        _idleLogged = true;
                        Plugin.Log.LogInfo("枠 " + (_slot + 1) + " が未割り当てのため、パネルを表示しません。（"
                                           + Applier.DescribeRoster() + "）");
                    }
                    return;
                }

                _state.LoadFrom(_target.File);
                _charaName = _target.DisplayName;
                _idleLogged = false;
                ReadEventFlag();
               
                _openDropdown = -1;
                Plugin.Log.LogInfo("枠 " + (_slot + 1) + " → " + _charaName
                                   + "（" + (_target.IsLive ? "実体" : "カード") + "）");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("枠の読み込みに失敗しました: " + ex.Message);
            }
        }

        /// <summary>今の待ち合わせイベントフラグを読み直す。</summary>
        private void ReadEventFlag()
        {
            try
            {
                LobbyEventFlag.Target t = LobbyEventFlag.Resolve(_slot, _target);
                _eventAvailable = t.Valid;
                _eventCurrentId = LobbyEventFlag.Get(t);
                _eventChoice = _eventCurrentId;
                _eventChoices = LobbyEventFlag.BuildChoices(_eventCurrentId);
                _eventNote = t.Valid
                    ? (_slot == LobbyEventFlag.EffectiveSlot
                        ? string.Empty
                        : "マップ選択に効くのは1人目のフラグです。")
                    : (t.Reason ?? "設定先を特定できません。");
            }
            catch (Exception ex)
            {
                _eventAvailable = false;
                _eventCurrentId = LobbyEventFlag.None;
                _eventChoice = LobbyEventFlag.None;
                _eventChoices = LobbyEventFlag.BuildChoices(LobbyEventFlag.None);
                _eventNote = "取得に失敗: " + ex.Message;
            }
        }

        private void Update()
        {
            FollowGameSlot();

            if (Plugin.CfgToggleKey.Value.IsDown())
                Plugin.CfgVisible.Value = !Plugin.CfgVisible.Value;

            // パネルが引っ込むときは試聴も止める。
            // F6・マップ選択・ADV のどれでもここを通る。
            bool foreground = Plugin.CfgVisible.Value && IsLobbyForeground();
            if (_wasForeground && !foreground)
                VoicePreview.Stop();
            _wasForeground = foreground;

            if (Input.GetMouseButtonUp(0))
                PersistPosition();
        }

        /// <summary>
        /// ゲーム側が選ばせている枠が変わったら、タブを自動で追従させる。
        /// 二人目を選び始めたらタブ2がアクティブになる。
        /// </summary>
        private void FollowGameSlot()
        {
            try
            {
                int game = TargetResolver.GameActiveSlot;
                if (game < 0 || game == _lastGameSlot)
                {
                    // 同じ枠でも、一覧のハイライトが動けば対象が変わるので拾い直す。
                    if (_target == null && Time.frameCount % 20 == 0)
                        LoadSlot();
                    return;
                }
                _lastGameSlot = game;
                SwitchSlot(game);
            }
            catch (Exception)
            {
                // ロビー初期化中は参照が揃っていない。次のフレームで拾う。
            }
        }

        // ------------------------------------------------------------------

        private void RestorePosition()
        {
            float x = Plugin.CfgWindowX.Value;
            float y = Plugin.CfgWindowY.Value;
            if (x < 0f || y < 0f || x > Screen.width - 40f || y > Screen.height - 40f)
            {
                x = Screen.width - WindowWidth - 24f;
                y = 60f;
            }
            _rect.x = x;
            _rect.y = y;
            _rect.width = WindowWidth;
        }

        private void PersistPosition()
        {
            if (!Plugin.CfgVisible.Value)
                return;
            if (Math.Abs(Plugin.CfgWindowX.Value - _rect.x) < 0.5f
                && Math.Abs(Plugin.CfgWindowY.Value - _rect.y) < 0.5f)
                return;
            Plugin.CfgWindowX.Value = _rect.x;
            Plugin.CfgWindowY.Value = _rect.y;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!Plugin.CfgVisible.Value)
                return;

            // マップ選択やADV中は引っ込める。Config の表示設定は触らないので、
            // 待ち合わせに戻れば元の状態で出てくる。
            if (!IsLobbyForeground())
                return;

            // キャラが決まるまではパネル自体を出さない。
            // フォントの差し替えもここより後なので、他プラグインの IMGUI には触れない。
            if (_target == null || _target.File == null || !_state.Loaded)
            {
                _openDropdown = -1;
                return;
            }

            EnsureFont();
            Font prev = GUI.skin.font;
            if (_font != null)
                GUI.skin.font = _font;

            CloseDropdownIfClickedOutside();

            _rect = GUILayout.Window(MainWindowId, _rect, DrawMain,
                                     PanelTitle, GUILayout.Width(WindowWidth));
            KKAPI.Utilities.IMGUIUtils.EatInputInRect(_rect);

            if (_openDropdown >= 0)
            {
                // パネルをドラッグしても離れないよう、毎フレーム置き直す。
                _popupRect = PlacePopup(_openDropdown, CurrentList());
                _popupRect = GUILayout.Window(PopupWindowId, _popupRect, DrawPopup, string.Empty,
                                              GUILayout.Width(_popupRect.width),
                                              GUILayout.Height(_popupRect.height));
                GUI.BringWindowToFront(PopupWindowId);
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(_popupRect);
            }

            // 声質スライダを離したら試聴する。ドラッグ中は鳴らさない。
            //
            // Input.GetMouseButtonUp では拾えない。KKAPI の EatInputInRect が
            // カーソルのある間ずっと Input.ResetInputAxes() を呼ぶので、
            // パネル上のマウス操作は Input 側に出てこない。
            // 掴んでいる間は hotControl がスライダを指しているため、そちらで見る。
            if (_voiceDragged && Event.current.type == EventType.Repaint && GUIUtility.hotControl == 0)
            {
                _voiceDragged = false;
                VoicePreview.Play(_state.Personality, _state.VoiceRate);
            }

            GUI.skin.font = prev;
        }

        // ------------------------------------------------------------------

        private void DrawMain(int id)
        {
            GUILayout.BeginVertical();

            DrawTabs();

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(_charaName) ? "(名前なし)" : _charaName);
            GUILayout.FlexibleSpace();
            GUILayout.Label(_target != null && _target.IsLive ? "実体" : "カード", GUILayout.Width(46f));
            GUILayout.EndHorizontal();

            if (Foldout("プロフィール", ref _secProfile))
            {
                Row();
                GUILayout.Label("名前", GUILayout.Width(LabelW));
                string name = GUILayout.TextField(_state.Name ?? string.Empty, 64);
                if (name != _state.Name) { _state.Name = name; }
                EndRow();

                Row();
                Dropdown("性格", DdPersonality, NameSources.Personality, _state.Personality);
                GUILayout.Space(4f);
                VoiceSlider();
                EndRow();
            }

            if (Foldout("メンタル", ref _secMental))
            {
                Row();
                Dropdown("特性", DdTrait, NameSources.Trait, _state.Trait);
                GUILayout.Space(4f);
                Dropdown("心情", DdMind, NameSources.Mind, _state.Mind);
                EndRow();

                Row();
                Dropdown("H属性", DdHAttribute, NameSources.HAttribute, _state.HAttribute);
                GUILayout.FlexibleSpace();
                EndRow();
            }

            if (Foldout("ステータス", ref _secStatus))
            {
                Row();
                _state.Favor = Stat("好感度", _state.Favor);
                GUILayout.Space(4f);
                _state.Aversion = Stat("嫌悪", _state.Aversion);
                EndRow();

                Row();
                _state.Slavery = Stat("従順度", _state.Slavery);
                GUILayout.Space(4f);
                _state.Dependence = Stat("依存", _state.Dependence);
                EndRow();

                Row();
                _state.Enjoyment = Stat("悦び", _state.Enjoyment);
                GUILayout.Space(4f);
                _state.Broken = Stat("破綻", _state.Broken);
                EndRow();
            }

            if (Foldout("欲求", ref _secDesire))
            {
                Row();
                _state.Dirty = Stat("汚れ", _state.Dirty);
                GUILayout.Space(4f);
                _state.Toilet = Stat("尿意", _state.Toilet);
                EndRow();

                Row();
                _state.Tiredness = Stat("疲労", _state.Tiredness);
                GUILayout.Space(4f);
                _state.Libido = Stat("性欲", _state.Libido);
                EndRow();
            }

            if (Foldout("状態", ref _secState))
            {
                Row();
                Dropdown("状態", DdState, NameSources.NowState, _state.NowState);
                GUILayout.Space(4f);
                bool lockState = GUILayout.Toggle(_state.LockNowState, "固定", GUILayout.Width(ToggleW));
                if (lockState != _state.LockNowState) { _state.LockNowState = lockState; }
                bool lb = GUILayout.Toggle(_state.LockBroken, "破綻", GUILayout.Width(ToggleW));
                if (lb != _state.LockBroken) { _state.LockBroken = lb; }
                bool ld = GUILayout.Toggle(_state.LockDependence, "依存", GUILayout.Width(ToggleW));
                if (ld != _state.LockDependence) { _state.LockDependence = ld; }
                EndRow();

                if (_state.StateEditedByHand && !_state.LockNowState)
                    GUILayout.Label("状態を手動変更しています。適用時に固定が自動で入ります。");
            }

            if (Foldout("待ち合わせイベント", ref _secEvent))
            {
                Row();
                GUILayout.Label("現在", GUILayout.Width(LabelW));
                GUILayout.Label(LobbyEventFlag.Describe(_eventCurrentId), GUILayout.Width(DropW));
                GUILayout.FlexibleSpace();
                EndRow();

                bool prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && _eventAvailable;

                Row();
                Dropdown("設定", DdLobbyEvent, _eventChoices, _eventChoice);
                GUILayout.FlexibleSpace();
                EndRow();

                GUI.enabled = prevEnabled;

                if (!string.IsNullOrEmpty(_eventNote))
                    GUILayout.Label(_eventNote);
            }

            DrawSeparator();

            Row();
            bool saveCard = GUILayout.Toggle(Plugin.CfgSaveCard.Value, "カードにも保存", GUILayout.Width(108f));
            if (saveCard != Plugin.CfgSaveCard.Value)
                Plugin.CfgSaveCard.Value = saveCard;

            // 既定オフの危険側の設定なので、オンのときだけ色を変える。
            Color pc = GUI.color;
            if (Plugin.CfgUnlockLimits.Value)
                GUI.color = new Color(1f, 0.75f, 0.3f);
            bool unlocked = GUILayout.Toggle(Plugin.CfgUnlockLimits.Value, "上限解除", GUILayout.Width(72f));
            GUI.color = pc;
            if (unlocked != Plugin.CfgUnlockLimits.Value)
                Plugin.CfgUnlockLimits.Value = unlocked;

            if (GUILayout.Button("読み直す", GUILayout.Width(66f)))
            {
                LoadSlot();
                SetMessage("現在の値を読み直しました。");
            }
            if (GUILayout.Button("適用", GUILayout.Width(56f)))
                DoApply();
            EndRow();

            // メッセージが無いときに空のラベルを置くと、その1行ぶん下端が伸びたままになる。
            if (!string.IsNullOrEmpty(_message) && Time.realtimeSinceStartup < _messageUntil)
                GUILayout.Label(_message);

            GUILayout.Space(BottomMargin);
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 20f));
        }

        // ------------------------------------------------------------------

        /// <summary>1人目 / 2人目 のタブ。ゲーム側の選択に追従するが、手動でも切り替えられる。</summary>
        private void DrawTabs()
        {
            int count = TargetResolver.SlotCount;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < count; i++)
            {
                bool active = i == _slot;
                Color pc = GUI.backgroundColor;
                if (active)
                    GUI.backgroundColor = new Color(0.55f, 0.85f, 0.75f);
                if (GUILayout.Button((i + 1) + "人目", GUILayout.Height(21f)) && !active)
                    SwitchSlot(i);
                GUI.backgroundColor = pc;
            }
            GUILayout.EndHorizontal();
        }

        private static void Row() { GUILayout.BeginHorizontal(); }
        private static void EndRow() { GUILayout.EndHorizontal(); }

        private static bool Foldout(string title, ref bool open)
        {
            GUILayout.Space(2f);
            if (GUILayout.Button((open ? "▼ " : "▶ ") + title, GUILayout.Height(19f)))
                open = !open;
            return open;
        }

        private static void DrawSeparator()
        {
            GUILayout.Space(2f);
            GUILayout.Box(GUIContent.none, GUILayout.Height(1f), GUILayout.ExpandWidth(true));
            GUILayout.Space(2f);
        }

        private void Dropdown(string label, int dropdownId, OptionList list, int value)
        {
            GUILayout.Label(label, GUILayout.Width(LabelW));
            string text = list != null && list.Count > 0 ? list.LabelOfValue(value) : value.ToString();
            bool clicked = GUILayout.Button(text, GUILayout.Width(DropW));

            // GetLastRect がボタンの実座標を返すのは Repaint のときだけで、
            // 押された瞬間（MouseUp）に呼ぶとダミー矩形が返る。毎フレーム控えておく。
            if (Event.current != null && Event.current.type == EventType.Repaint
                && dropdownId >= 0 && dropdownId < DdCount)
                _ddAnchors[dropdownId] = GUILayoutUtility.GetLastRect();

            if (!clicked)
                return;

            if (_openDropdown == dropdownId)
            {
                _openDropdown = -1;
                return;
            }

            _openDropdown = dropdownId;
            _popupScroll = Vector2.zero;
        }

        /// <summary>
        /// 一覧を、押したボタンのすぐ下に出す。
        /// パネルからはみ出す場合は、下に出せなければボタンの上へ回し、
        /// それでも収まらなければパネル内へ寄せる。
        /// </summary>
        private Rect PlacePopup(int dropdownId, OptionList list)
        {
            const float margin = 4f;

            float w = DropW + 24f;
            float h = PopupHeight(list, _rect.height - margin * 2f);

            Rect anchor = dropdownId >= 0 && dropdownId < DdCount
                ? _ddAnchors[dropdownId]
                : new Rect(0f, 0f, DropW, 20f);

            float minX = _rect.x + margin;
            float maxX = Mathf.Max(minX, _rect.xMax - w - margin);
            float x = Mathf.Clamp(_rect.x + anchor.x, minX, maxX);

            float minY = _rect.y + margin;
            float maxY = Mathf.Max(minY, _rect.yMax - h - margin);
            float y = _rect.y + anchor.yMax;                 // 既定はボタンの直下
            if (y > maxY)
            {
                float above = _rect.y + anchor.y - h;        // 入らなければボタンの上
                y = above >= minY ? above : Mathf.Clamp(y, minY, maxY);
            }

            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// 一覧の高さ。全項目が出る高さを基本とし、パネルに収まらないときだけそこで頭打ちにする。
        /// 1項目の高さはフォントサイズ（Config で 9〜20）によって変わるので、
        /// 決め打ちの値ではなく実際のスタイルから測る。
        /// </summary>
        private static float PopupHeight(OptionList list, float available)
        {
            float cap = Mathf.Max(48f, available);

            int count = list != null ? list.Count : 0;
            if (count <= 0)
                return Mathf.Min(56f, cap);

            GUIStyle button = GUI.skin.button;

            // 縦に積むと隣り合うマージンは重なるので、行の間隔は片側ぶんで数える。
            float gap = Mathf.Max(button.margin.top, button.margin.bottom);
            float row = button.CalcHeight(new GUIContent("Ay"), DropW) + gap;

            // ウィンドウの枠（空タイトルでも上端は確保される）と、下端のマージン。
            float chrome = GUI.skin.window.padding.vertical + gap + 4f;

            return Mathf.Min(count * row + chrome, cap);
        }

        private void VoiceSlider()
        {
            GUILayout.Label("声質", GUILayout.Width(38f));
            float shown = Mathf.Clamp(_state.VoiceRate * 100f, SliderRange.VoiceMin, SliderRange.VoiceMax);
            float next = GUILayout.HorizontalSlider(shown, SliderRange.VoiceMin, SliderRange.VoiceMax,
                                                    GUILayout.Width(SliderW));
            if (Mathf.Abs(next - shown) > 0.001f)
            {
                _state.VoiceRate = Mathf.Round(next) / 100f;
                _voiceDragged = true;
            }
            GUILayout.Label(Mathf.RoundToInt(next).ToString(), GUILayout.Width(ValueW));
        }

        private int Stat(string label, int value)
        {
            GUILayout.Label(label, GUILayout.Width(LabelW));
            float min = SliderRange.StatMin;
            float max = SliderRange.StatMax;
            float v = Mathf.Clamp(value, min, max);
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW));
            int result = Mathf.RoundToInt(v);
            GUILayout.Label(result.ToString(), GUILayout.Width(ValueW));
            return result;
        }

        private void DrawPopup(int id)
        {
            OptionList list = CurrentList();
            if (list == null || list.Count == 0)
            {
                GUILayout.Label("選択肢がありません");
                return;
            }

            _popupScroll = GUILayout.BeginScrollView(_popupScroll);
            for (int i = 0; i < list.Count; i++)
            {
                if (GUILayout.Button(list.Labels[i]))
                {
                    SetCurrentValue(list.Values[i]);
                    _openDropdown = -1;
                   
                }
            }
            GUILayout.EndScrollView();
        }

        private void CloseDropdownIfClickedOutside()
        {
            if (_openDropdown < 0)
                return;
            if (Event.current == null || Event.current.type != EventType.MouseDown)
                return;
            Vector2 m = Event.current.mousePosition;
            if (!_popupRect.Contains(m) && !_rect.Contains(m))
                _openDropdown = -1;
        }

        private OptionList CurrentList()
        {
            switch (_openDropdown)
            {
                case DdPersonality: return NameSources.Personality;
                case DdTrait: return NameSources.Trait;
                case DdMind: return NameSources.Mind;
                case DdHAttribute: return NameSources.HAttribute;
                case DdState: return NameSources.NowState;
                case DdLobbyEvent: return _eventChoices;
                default: return null;
            }
        }

        private void SetCurrentValue(int value)
        {
            switch (_openDropdown)
            {
                case DdPersonality:
                    _state.Personality = value;
                    VoicePreview.Play(value, _state.VoiceRate);
                    break;
                case DdTrait: _state.Trait = value; break;
                case DdMind: _state.Mind = value; break;
                case DdHAttribute: _state.HAttribute = value; break;
                case DdState:
                    if (_state.NowState != value)
                    {
                        _state.NowState = value;
                        _state.StateEditedByHand = true;
                    }
                    break;
                case DdLobbyEvent:
                    _eventChoice = value;
                    break;
            }
        }

        private void DoApply()
        {
            string message;
            Applier.Apply(_target, _state, out message);

            // イベントフラグはセーブデータ側の状態なので、カードの適用とは別に書き込む。
            if (_eventAvailable)
            {
                int want = _eventChoice;
                if (want != _eventCurrentId)
                {
                    string eventMessage;
                    LobbyEventFlag.Set(_slot, _target, want, out eventMessage);
                    message = message + " " + eventMessage;
                    ReadEventFlag();
                }
            }

            SetMessage(message);
        }

        private void SetMessage(string message)
        {
            _message = message ?? string.Empty;
            _messageUntil = Time.realtimeSinceStartup + 6f;
        }

        private static void EnsureFont()
        {
            int size = Plugin.CfgFontSize.Value;
            if (_font != null && _fontSize == size)
                return;

            string[] candidates = { Plugin.CfgFontName.Value, "Yu Gothic UI", "Meiryo", "MS Gothic", "Arial" };
            foreach (string name in candidates)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                try
                {
                    Font f = Font.CreateDynamicFontFromOSFont(name, size);
                    if (f != null)
                    {
                        _font = f;
                        _fontSize = size;
                        return;
                    }
                }
                catch (Exception) { }
            }
            _fontSize = size;
        }
    }
}
