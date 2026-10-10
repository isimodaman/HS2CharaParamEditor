using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using static HS2CharaParamEditor.ConfigLanguage.Localize;

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
        //   39 + 89 + 32 + 4 + 39 + 89 + 32 = 324、コントロール間の余白を足して約 344。
        // WindowWidth はこれに枠の内側余白（左右 8 ずつ）と若干のマージンを足した値。
        // 他の行（メンタル・状態・最下段のボタン）はこれより狭くなるよう各幅を決めている。
        private const float DefaultWindowWidth = 364f;
        private const float DefaultWindowHeight = 562f;
        private const float DefaultLabelW = 39f;
        private const float DefaultDropW = 125f;
        private const float DefaultSliderW = 89f;
        private const float ValueW = 32f;
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
        private int _slot;                      // パネルが今見ている枠（タブ）
        private int _lastGameSlot = int.MinValue;
        private EditTarget _target;
        private string _charaName = string.Empty;
        private bool _idleLogged;
        private bool _voiceDragged;             // 声質スライダを動かした。離した時に試聴する
        private bool _wasForeground;            // 前フレームでパネルが出ていたか

        private Rect _rect = new Rect(0f, 0f, DefaultWindowWidth, DefaultWindowHeight);
        private string _message = string.Empty;
        private float _messageUntil;

        private int _openDropdown = -1;
        private int _mode;                      // 0 = 待ち合わせ / 1 = マップ選択
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

        internal static bool Initialized;
        private static float _labelW = DefaultLabelW;
        private static float _dropW = DefaultDropW;
        private static float _sliderW = DefaultSliderW;
        private static float _reloadApplyW = 66f;
        private static GUIStyle _sliderStyle;
        private static GUIStyle _valueStyle;
        private static GUIStyle _dropdownWindowStyle;
        private static GUIStyle _dropdownButtonStyle;
        private static bool _dropdownVisible;
        private static Font _font;
        private static int _fontSize;

        [SuppressMessage("CodeQuality", "IDE0051")]
        private void Awake()
        {
            Instance = this;
            Plugin.Log.LogInfo(LC("待ち合わせ画面にパネルを生成しました。"));
            NameSources.EnsureBuilt();
            SliderRange.Refresh();
        }

        [SuppressMessage("CodeQuality", "IDE0051")]
        private void OnDestroy()
        {
            VoicePreview.Stop();
            if (Instance == this)
                Instance = null;
            DestroyDropdown();
            DestroyFont();
        }

        internal static void DestroyFont()
        {
            Destroy(_font);
            _font = null;
        }

        internal void DestroyDropdown()
        {
            _dropdownVisible = false;
            _openDropdown = -1;
            if (_dropdownWindowStyle == null)
                return;
            Destroy(_dropdownWindowStyle.normal.background);
            _dropdownWindowStyle.normal.background = null;
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
                Instance.DestroyDropdown();
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
            LoadSlot(slot);
        }

        private bool LoadSlot(int? slot = null, bool logging = true)
        {
            int newSlot = slot ?? _slot;
            try
            {
                _target = TargetResolver.Resolve(newSlot);
                if (_target != null && _target.File != null)
                {
                    _state.LoadFrom(_target.File);
                    _charaName = _target.DisplayName;
                    _idleLogged = false;
                    ReadEventFlag();

                    DestroyDropdown();
                    if (logging)
                        Plugin.Log.LogInfo(LC("枠 {0} → {1}（{2}）",
                            newSlot + 1,
                            _charaName,
                            LC(_target.IsLive ? "実体" : "カード")));

                    // set slot if success
                    _slot = newSlot;

                    return true;
                }

                _charaName = string.Empty;
                _state.Loaded = false;
                _eventAvailable = false;
                DestroyDropdown();

                // この状態ではパネルを出さないので、原因はログに残す。
                // 枠が埋まるまで 20 フレームごとにここへ来るため、入ったときだけ書く。
                if (!_idleLogged)
                {
                    _idleLogged = true;
                    if (logging)
                        Plugin.Log.LogInfo(LC("枠 {0} が未割り当てのため、パネルを表示しません。（{1}）",
                            newSlot + 1,
                            Applier.DescribeRoster()));
                }
            }
            catch (Exception ex)
            {
                if (logging)
                    Plugin.Log.LogWarning(LC("枠の読み込みに失敗しました: ") + ex.Message);
            }

            // reset if failed
            if (_slot != newSlot)
                LoadSlot(_slot, false);
            return false;
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
                        : LC("マップ選択に効くのは1人目のフラグです。"))
                    : (t.Reason ?? LC("設定先を特定できません。"));
            }
            catch (Exception ex)
            {
                _eventAvailable = false;
                _eventCurrentId = LobbyEventFlag.None;
                _eventChoice = LobbyEventFlag.None;
                _eventChoices = LobbyEventFlag.BuildChoices(LobbyEventFlag.None);
                _eventNote = LC("取得に失敗: ") + ex.Message;
            }
        }

        internal void BuildChoices()
        {
            _eventChoices = LobbyEventFlag.BuildChoices(_eventCurrentId);
        }

        [SuppressMessage("CodeQuality", "IDE0051")]
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

        [SuppressMessage("CodeQuality", "IDE0051")]
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
                DestroyDropdown();
                return;
            }

            if (!Initialized)
            {
                GUIStyle style = GUI.skin.label;
                string[] labels = {
                    "名前", "性格", "特性", "心情", "H属性", "好感度", "嫌悪", "従順度",
                    "依存", "悦び", "破綻", "汚れ", "尿意", "疲労", "性欲", "状態",
                    "現在", "設定" };
                _labelW = labels.Max(text => style.CalcSize(new GUIContent(LC(text))).x);

                style = GUI.skin.button;
                IEnumerable<string> dropdowns =
                    NameSources.Personality.Labels.Concat(
                    NameSources.Trait.Labels).Concat(
                    NameSources.Mind.Labels).Concat(
                    NameSources.HAttribute.Labels).Concat(
                    NameSources.NowState.Labels).Concat(
                    _eventChoices.Labels);
                _dropW = Math.Max(DefaultDropW, dropdowns.Max(text => style.CalcSize(new GUIContent(text)).x));

                _sliderW = _dropW - (DefaultDropW - DefaultSliderW);
                _rect.width = (_labelW + _dropW) * 2f + (DefaultWindowWidth - (DefaultLabelW + DefaultDropW) * 2f);

                string[] reloadApply = { "読み直す", "適用" };
                _reloadApplyW = reloadApply.Max(text => style.CalcSize(new GUIContent(LC(text))).x) + 12f;

                _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider) { margin = new RectOffset(4, 4, 9, 9) };
                _valueStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter };
                RectOffset offsetZero = new RectOffset();
                _dropdownWindowStyle = new GUIStyle(GUI.skin.window) { normal = new GUIStyleState { background = null }, margin = offsetZero, padding = offsetZero };
                _dropdownButtonStyle = new GUIStyle(GUI.skin.button) { margin = offsetZero };

                Initialized = true;
            }

            Matrix4x4 matrix = GUI.matrix;
            float scale = Plugin.CfgWindowScale.Value;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            try
            {
                EnsureFont();
                Font font = GUI.skin.font;
                if (_font != null)
                    GUI.skin.font = _font;
                try
                {
                    CloseDropdownIfClickedOutside();

                    // load window position
                    _rect.position = Plugin.CfgWindowPosition.Value;
                    bool initializedPosition = _rect.x < 0 || _rect.y < 0;
                    if (initializedPosition)
                    {
                        // calculate window size at the out of screen
                        _rect.x = Screen.width / scale;
                        _rect.y = Screen.height / scale;
                    }

                    // draw window
                    GUI.DrawTexture(_rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0f, 0f, 0f, (Plugin.CfgWindowOpacity.Value - 0.5f) * 2f), 0, 9);
                    _rect = GUILayout.Window(MainWindowId, _rect, DrawMain, PanelTitle);
                    KKAPI.Utilities.IMGUIUtils.EatInputInRect(_rect);

                    // save window position
                    if (!initializedPosition)
                    {
                        // move onto the screen
                        _rect.x = Mathf.Clamp(_rect.x, 0, Screen.width / scale - _rect.width);
                        _rect.y = Mathf.Clamp(_rect.y, 0, Screen.height / scale - _rect.height);
                        Plugin.CfgWindowPosition.Value = _rect.position;
                    }
                    else if (Event.current.type == EventType.Repaint)
                    {
                        // set initial position
                        _rect.x = Math.Max((Screen.width - 24f) / scale - _rect.width, 0);
                        _rect.y = Math.Max((Screen.height - 24f) / scale - _rect.height, 0);
                        Plugin.CfgWindowPosition.Value = _rect.position;
                    }

                    if (_dropdownVisible)
                    {
                        // パネルをドラッグしても離れないよう、毎フレーム置き直す。
                        _popupRect = PlacePopup(_openDropdown, CurrentList());
                        if (!_dropdownWindowStyle.normal.background)
                        {
                            // it is destroy by "ForceReleaseCursor"of "HS2 Mouse Stuck Fix" plugin.
                            Texture2D background = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                            background.SetPixel(0, 0, new Color(0f, 0f, 0f, Plugin.CfgWindowOpacity.Value));
                            background.Apply();
                            _dropdownWindowStyle.normal.background = background;
                        }
                        _popupRect = GUI.ModalWindow(PopupWindowId, _popupRect, DrawPopup, GUIContent.none, _dropdownWindowStyle);
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
                }
                finally
                {
                    GUI.skin.font = font;
                }
            }
            finally
            {
                GUI.matrix = matrix;
            }
        }

        // ------------------------------------------------------------------

        private void DrawMain(int id)
        {
            DrawCloseButton();

            GUILayout.BeginVertical();

            DrawTabs();

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(_charaName) ? LC("(名前なし)") : _charaName);
            GUILayout.FlexibleSpace();
            GUILayout.Label(LC(_target != null && _target.IsLive ? "実体" : "カード"));
            GUILayout.EndHorizontal();

            if (Foldout(LC("プロフィール"), ref _secProfile))
            {
                Row();
                GUILayout.Label(LC("名前"), GUILayout.Width(_labelW));
                string name = GUILayout.TextField(_state.Name ?? string.Empty, 64);
                if (name != _state.Name) { _state.Name = name; }
                EndRow();

                Row();
                Dropdown(LC("性格"), DdPersonality, NameSources.Personality, _state.Personality);
                GUILayout.Space(4f);
                VoiceSlider();
                EndRow();
            }

            if (Foldout(LC("メンタル"), ref _secMental))
            {
                Row();
                Dropdown(LC("特性"), DdTrait, NameSources.Trait, _state.Trait);
                GUILayout.Space(4f);
                Dropdown(LC("心情"), DdMind, NameSources.Mind, _state.Mind);
                EndRow();

                Row();
                Dropdown(LC("H属性"), DdHAttribute, NameSources.HAttribute, _state.HAttribute);
                GUILayout.FlexibleSpace();
                EndRow();
            }

            if (Foldout(LC("ステータス"), ref _secStatus))
            {
                Row();
                _state.Favor = Stat(LC("好感度"), _state.Favor);
                GUILayout.Space(4f);
                _state.Aversion = Stat(LC("嫌悪"), _state.Aversion);
                EndRow();

                Row();
                _state.Slavery = Stat(LC("従順度"), _state.Slavery);
                GUILayout.Space(4f);
                _state.Dependence = Stat(LC("依存"), _state.Dependence);
                EndRow();

                Row();
                _state.Enjoyment = Stat(LC("悦び"), _state.Enjoyment);
                GUILayout.Space(4f);
                _state.Broken = Stat(LC("破綻"), _state.Broken);
                EndRow();
            }

            if (Foldout(LC("欲求"), ref _secDesire))
            {
                Row();
                _state.Dirty = Stat(LC("汚れ"), _state.Dirty);
                GUILayout.Space(4f);
                _state.Toilet = Stat(LC("尿意"), _state.Toilet);
                EndRow();

                Row();
                _state.Tiredness = Stat(LC("疲労"), _state.Tiredness);
                GUILayout.Space(4f);
                _state.Libido = Stat(LC("性欲"), _state.Libido);
                EndRow();
            }

            if (Foldout(LC("状態"), ref _secState))
            {
                Row();
                Dropdown(LC("状態"), DdState, NameSources.NowState, _state.NowState);
                GUILayout.Space(4f);
                bool lockState = GUILayout.Toggle(_state.LockNowState, LC("固定"));
                if (lockState != _state.LockNowState) { _state.LockNowState = lockState; }
                bool lb = GUILayout.Toggle(_state.LockBroken, LC("破綻"));
                if (lb != _state.LockBroken) { _state.LockBroken = lb; }
                bool ld = GUILayout.Toggle(_state.LockDependence, LC("依存"));
                if (ld != _state.LockDependence) { _state.LockDependence = ld; }
                EndRow();

                if (_state.StateEditedByHand && !_state.LockNowState)
                    GUILayout.Label(LC("状態を手動変更しています。適用時に固定が自動で入ります。"));
            }

            if (Foldout(LC("待ち合わせイベント"), ref _secEvent))
            {
                Row();
                GUILayout.Label(LC("現在"), GUILayout.Width(_labelW));
                GUILayout.Label(LobbyEventFlag.Describe(_eventCurrentId), GUILayout.Width(_dropW));
                GUILayout.FlexibleSpace();
                EndRow();

                bool prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && _eventAvailable;

                Row();
                Dropdown(LC("設定"), DdLobbyEvent, _eventChoices, _eventChoice);
                GUILayout.FlexibleSpace();
                EndRow();

                GUI.enabled = prevEnabled;

                if (!string.IsNullOrEmpty(_eventNote))
                    GUILayout.Label(_eventNote);
            }

            DrawSeparator();

            Row();
            bool saveCard = GUILayout.Toggle(Plugin.CfgSaveCard.Value, LC("カードにも保存"));
            if (saveCard != Plugin.CfgSaveCard.Value)
                Plugin.CfgSaveCard.Value = saveCard;

            // 既定オフの危険側の設定なので、オンのときだけ色を変える。
            Color pc = GUI.color;
            if (Plugin.CfgUnlockLimits.Value)
                GUI.color = new Color(1f, 0.75f, 0.3f);
            bool unlocked = GUILayout.Toggle(Plugin.CfgUnlockLimits.Value, LC("上限解除"));
            GUI.color = pc;
            if (unlocked != Plugin.CfgUnlockLimits.Value)
                Plugin.CfgUnlockLimits.Value = unlocked;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(LC("読み直す"), GUILayout.Width(_reloadApplyW)))
            {
                LoadSlot();
                SetMessage(LC("現在の値を読み直しました。"));
            }
            if (GUILayout.Button(LC("適用"), GUILayout.Width(_reloadApplyW)))
                DoApply();
            EndRow();

            // メッセージが無いときに空のラベルを置くと、その1行ぶん下端が伸びたままになる。
            if (!string.IsNullOrEmpty(_message) && Time.realtimeSinceStartup < _messageUntil)
                GUILayout.Label(_message);

            GUILayout.Space(BottomMargin);
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, _rect.width, 20f));
        }

        // ------------------------------------------------------------------

        private void DrawCloseButton()
        {
            Color oldColor = GUI.color;
            try
            {
                GUI.color = Color.red;
                if (GUI.Button(new Rect(_rect.width - 19f, 2f, 15f, 15f), GUIContent.none))
                {
                    Plugin.CfgVisible.Value = false;
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

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
                if (GUILayout.Button(LC((i + 1) + "人目", out string text) ? text : LC("{0}人目", i + 1), GUILayout.Height(21f)) && !active)
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
            GUILayout.Label(label, GUILayout.Width(_labelW));
            string text = list != null && list.Count > 0 ? list.LabelOfValue(value) : value.ToString();
            bool clicked = GUILayout.Button(text, GUILayout.Width(_dropW));

            // GetLastRect がボタンの実座標を返すのは Repaint のときだけで、
            // 押された瞬間（MouseUp）に呼ぶとダミー矩形が返る。毎フレーム控えておく。
            if (Event.current != null && Event.current.type == EventType.Repaint
                && dropdownId >= 0 && dropdownId < DdCount)
                _ddAnchors[dropdownId] = GUILayoutUtility.GetLastRect();

            if (!clicked)
                return;

            if (_openDropdown == dropdownId)
            {
                DestroyDropdown();
                return;
            }

            _openDropdown = dropdownId;
            _dropdownVisible = true;
            _popupScroll = Vector2.zero;
        }

        /// <summary>
        /// 一覧を、押したボタンのすぐ下に出す。
        /// パネルからはみ出す場合は、下に出せなければボタンの上へ回し、
        /// それでも収まらなければパネル内へ寄せる。
        /// </summary>
        private Rect PlacePopup(int dropdownId, OptionList list)
        {
            float w = _dropW;
            float h = Mathf.Max(48f, _rect.height);

            int count = list != null ? list.Count : 0;
            if (count > 0)
            {
                float row = _dropdownButtonStyle.CalcHeight(new GUIContent("Ay"), w) + 2f;
                float calcHeight = row * count;
                if (calcHeight > h ||
                    list.Labels.Any(label => _dropdownButtonStyle.CalcSize(new GUIContent(label)).x > w) &&
                    (calcHeight += GUI.skin.horizontalScrollbar.fixedHeight) > h)
                {
                    w += GUI.skin.verticalScrollbar.fixedWidth;
                }
                else
                {
                    h = calcHeight;
                }
            }

            Rect anchor = dropdownId >= 0 && dropdownId < DdCount
                ? _ddAnchors[dropdownId]
                : new Rect(0f, 0f, w, 20f);

            float minX = _rect.x;
            float maxX = Mathf.Max(minX, _rect.xMax - w);
            float x = Mathf.Clamp(_rect.x + anchor.x, minX, maxX);

            float minY = _rect.y;
            float maxY = Mathf.Max(minY, _rect.yMax - h);
            float y = _rect.y + anchor.yMax;                 // 既定はボタンの直下
            if (y > maxY)
            {
                float above = _rect.y + anchor.y - h;        // 入らなければボタンの上
                y = above >= minY ? above : Mathf.Clamp(y, minY, maxY);
            }

            return new Rect(x, y, w, h);
        }

        private void VoiceSlider()
        {
            GUILayout.Label(LC("声質"), GUILayout.Width(_labelW));
            float min = SliderRange.VoiceMin;
            float max = SliderRange.VoiceMax;
            float shown = Mathf.Clamp(_state.VoiceRate * 100f, min, max);
            float next = GUILayout.HorizontalSlider(shown, min, max, _sliderStyle, GUI.skin.horizontalSliderThumb, GUILayout.Width(_sliderW));
            if (Mathf.Abs(next - shown) > 0.001f)
            {
                _state.VoiceRate = Mathf.Round(next) / 100f;
                _voiceDragged = true;
            }
            int value = Mathf.RoundToInt(shown);
            string text = GUILayout.TextField(value.ToString(), _valueStyle, GUILayout.Width(ValueW));
            if (int.TryParse(text, out int newValue) && newValue != value && newValue >= min && newValue <= max)
            {
                _state.VoiceRate = newValue / 100f;
                _voiceDragged = true;
            }
        }

        private int Stat(string label, int value)
        {
            GUILayout.Label(label, GUILayout.Width(_labelW));
            float min = SliderRange.StatMin;
            float max = SliderRange.StatMax;
            float v = Mathf.Clamp(value, min, max);
            v = GUILayout.HorizontalSlider(v, min, max, _sliderStyle, GUI.skin.horizontalSliderThumb, GUILayout.Width(_sliderW));
            int result = Mathf.RoundToInt(v);
            string text = GUILayout.TextField(result.ToString(), _valueStyle, GUILayout.Width(ValueW));
            if (int.TryParse(text, out int newValue) && newValue != result && newValue >= min && newValue <= max)
                result = newValue;
            return result;
        }

        private void DrawPopup(int id)
        {
            OptionList list = CurrentList();
            if (list == null || list.Count == 0)
            {
                GUILayout.Label(LC("選択肢がありません"));
                return;
            }

            _popupScroll = GUILayout.BeginScrollView(_popupScroll);
            int selected = list.IndexOfValue(GetCurrentValue());
            int index = GUILayout.SelectionGrid(selected, list.Labels, 1, _dropdownButtonStyle);
            if (index != selected)
            {
                SetCurrentValue(list.Values[index]);
                DestroyDropdown();
            }
            GUILayout.EndScrollView();
        }

        private void CloseDropdownIfClickedOutside()
        {
            if (!_dropdownVisible || !Input.GetMouseButtonDown(0))
                return;
            Vector2 m = Input.mousePosition;
            m.y = Screen.height - m.y;
            if (_popupRect.Contains(m))
                return;
            int dropdownId = _openDropdown;
            DestroyDropdown();
            if (dropdownId >= 0 && dropdownId < DdCount && _ddAnchors[dropdownId].Contains(m - _rect.position))
                _openDropdown = dropdownId;
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

        private int GetCurrentValue()
        {
            switch (_openDropdown)
            {
                case DdPersonality: return _state.Personality;
                case DdTrait: return _state.Trait;
                case DdMind: return _state.Mind;
                case DdHAttribute: return _state.HAttribute;
                case DdState: return _state.NowState;
                case DdLobbyEvent: return _eventChoice;
                default: return -1;
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
