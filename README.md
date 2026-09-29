# HS2CharaParamEditor

Honey Select 2 Libido DX 向けの BepInEx プラグイン。
**ロビーの待ち合わせ画面**で、選択中のキャラのパラメータを日本語UIで編集する。

既存プラグイン **FemaleParameterInfo (FPI)** と同じ場所・同じ役割で、項目を増やし表記を日本語にしたもの。

> A BepInEx plugin for Honey Select 2 Libido DX. It adds a Japanese-language panel to the lobby
> (待ち合わせ) screen for editing the selected heroine's parameters — personality, voice pitch,
> traits, status values, desires, and the forced lobby-event flag. UI and documentation are in Japanese.

---

## 必要なもの

| | |
| --- | --- |
| ゲーム | Honey Select 2 **Libido DX** |
| プラグイン基盤 | BepInEx 5.4.x |
| 依存 | HS2API (KKAPI) |

**FemaleParameterInfo (FPI) とは同時に使えない。** 同じ場所に同じ役割のパネルが二重に出て、
同じ `gameinfo2` を奪い合う。どちらか一方を無効にすること。

## インストール

`HS2CharaParamEditor.dll` を `BepInEx\plugins\` に置く。

設定ファイルは初回起動時に `BepInEx\config\com.isimodaman.hs2.charaparamediter.cfg` として作られる。
ゲーム内では ConfigurationManager から変更できる。

---

## 使い方

1. ロビーの待ち合わせ画面に入る
2. キャラを選ぶとパネルが出る。選び直せば内容もそのキャラに切り替わる
3. 値を変えて **適用** を押す

キャラが決まるまでパネルは出ない。一覧でカーソルを合わせた時点で対象が決まる。
パネルはマップ選択画面と ADV 再生中には自動で引っ込み、待ち合わせに戻ると元の状態で出てくる。
`F6` で手動の表示・非表示を切り替えられる。

**適用** で行われる処理は次の通り。

1. 値をキャラに書き込む
2. 状態を手で変えていた場合、`lockNowState` を自動で立てる（立てないと次の手順で上書きされる）
3. `GlobalHS2Calc.CalcState()` で状態を再計算する
4. `Heroine.cachedVoiceNo` をクリアする（性格を変えたときボイスを追随させるため）
5. 「カードにも保存」が有効ならカード(.png)へ書き戻す
6. ロビーのバニラ表示を更新する
7. 待ち合わせイベントを変えていた場合、ゲーム内のイベント表へ書き込む

---

## 編集できる項目

| 群 | 項目 |
| --- | --- |
| プロフィール | 名前 / 性格 / 声質 |
| メンタル | 特性 / 心情 / H属性 |
| ステータス | 好感度 / 従順度 / 悦び / 嫌悪 / 依存 / 破綻 |
| 欲求 | 汚れ / 疲労 / 尿意 / 性欲 |
| 状態 | 状態（嫌悪・依存・壊れ等）と 状態 / 破綻 / 依存 の各ロック |
| 待ち合わせイベント | なし / 風呂 / トイレ / 睡眠 ※セーブデータ側の状態 |

選択肢の日本語名は、プラグイン側に名称表を持たず**ゲームが実行時に持っているテーブルから引いている**。
翻訳のずれや取りこぼしが起きない。

- 性格 … `Manager.Voice.infoTable` の `Param.Get(languageInt)`
- 特性 / 心情 / H属性 / 状態 … `Manager.Game.infoTraitTable` ほか

性格はバニラと同じ絞り込みを行う。キャラメイクの `CustomControl.Initialize` が
`Param.No >= 0` で絞ってから選択肢にしているので、負番号のエントリは出さない。
加えて、バニラで統合されている性格を Config で除外できる（既定は フュル・シトリー）。

なお**心情はバニラのロビー画面には表示されない**。このパネルでのみ見える。

---

## サンプルボイスの試聴

**性格を変えたときと、声質スライダを離したときに、その設定のサンプルボイスが自動で鳴る。**
バニラのキャラメイクと同じ挙動で、適用する前に実際の声を確認できる。

- ドラッグ中は鳴らさない。離した時点で1回だけ鳴る
- 音源は性格ごとに2本あり、バニラと同じくランダムに選ばれる
- パネルが引っ込むとき（F6・マップ選択・ADV）に停止する

ピッチは `ChaFileParameter2.voicePitch` にゲーム自身で計算させている。
換算式をこちらで持たないので、上限解除で範囲外へ振ったときの挙動もゲームの実装どおりになる。

**ピッチの可動幅は 0.94〜1.06 しかない**（`ChaFileDefine.VoicePitchMin` / `VoicePitchMax`）。
声質スライダを端から端まで振っても音程の変化がわずかなのは、そういう仕様である。

---

## 待ち合わせイベントフラグ

「Hを開始する」→ マップ選択画面が**強制的に風呂／トイレしか選べない状態**になり、
そのマップを選ぶと特殊Hシーンに入る —— この状態を作るフラグを直接読み書きできる。

### 仕組み

```
[Hを開始する] 押下
    LobbyMapSelectUI.InitList( eventNos[heroineRommListIdx[0]] )
        番号 != -1 → 選択可能マップを
                     infoEventContentDic[番号].meetingLocationMaps に差し替える
        番号 == -1 → 通常のマップ集合

[マップ決定]
    game.eventNo = eventNos[heroineRommListIdx[0]]
    if (game.eventNo == -1)
        game.eventNo = GlobalHS2Calc.GetGeneralEventNo(gameinfo2, mapNo)
```

マップ一覧の表示自体は変わらない。`Init` の第2引数（選択可能なマップIDの配列）が差し替わることで、
**該当以外のマップが選択不可（「このマップで開始する」がグレーアウト）**になる。
バニラでトイレ・風呂が普段選べないのはこのためで、フラグが立つと逆にそこだけが選べる状態になる。

`-1` のときだけ通常イベントの抽選に回る。つまり
**フラグが立っていれば、欲求値の状態に関係なくその特殊Hシーンが確定で発生する。**

参照されるのは `heroineRommListIdx[0]` —— **1人目のフラグだけ**である。

### 番号

| プルダウン | 番号 |
| --- | --- |
| なし | -1 |
| 風呂 | 28 |
| トイレ | 30 |
| 睡眠 | 32 |

自慰版（風呂 29 / トイレ 31）はこちらからは設定しない。
汚れ・尿意が満ちていればバニラの抽選がこの番号を立てることがあり、
そのときは「現在」に **風呂（自慰）** のように表示したうえで、
プルダウンの先頭に **「そのまま (29)」** を出して温存する。
24（初H）や 16（脱走）など、ゲーム側が別途入れる番号も同じ扱い。
いずれも選び直さない限り書き換えない。

### 有効範囲

この表は `Manager.Game.CharaEventShuffle()` が作り直す。
呼び出し元は**タイトルからの復帰**と **ADV から待ち合わせへ戻るところ**の二か所。
したがってここでの変更は**今開いている待ち合わせに対して効く**。
一度イベント／Hシーンを挟むと、そのときの欲求値で抽選し直される。

抽選側の条件はこうなっている（いずれも H経験1回以上、状態が 5・6 でないことが前提）。

```
汚れ   >= 100   → 28 と 29 が候補に入る
尿意   >= 100   → 30 と 31 が候補に入る
疲労   >= 80    → 32 が候補に入る（疲労100なら100%、それ未満は30%）
候補が1つ以上あれば、その中から抽選で1つ
```

### 注意

- **枠が確定していること（キャラが1人目に入っていること）が前提。**
  一覧を眺めているだけの状態では設定できない
- **これはセーブデータ側の状態であり、カード(.png)には保存されない。**
  「カードにも保存」のオン・オフとは無関係に、適用時にゲーム内へ書き込む

---

## Config

| キー | 既定 | 内容 |
| --- | --- | --- |
| パネルを表示する | true | 待ち合わせ画面でパネルを開く |
| パネル開閉キー | F6 | 表示・非表示の切り替え |
| 選択肢から外す性格 | フュル,シトリー | 性格プルダウンから除外する名前（カンマ区切り） |
| フォント名 | Yu Gothic UI | 日本語が出ない場合は Meiryo / MS Gothic |
| フォントサイズ | 12 | 9〜20 |
| カードにも書き戻す | true | 適用時にカード(.png)へも保存する |
| バックアップを作る | true | 初回保存時に `<カード名>.png.bak` を残す |
| 上限解除 | false | スライダの範囲を -100〜200 に広げる |
| ウィンドウ位置X / Y | -1 | ドラッグすると自動更新 |

声質の範囲は SliderUnlocker の設定に追随する（未導入なら 0〜100）。

**上限解除**はゲームの想定外の値を書き込めるようにするもので、通常は無効のままにしておくこと。

---

## 既知の注意点

- **カードを書き換える。** 「カードにも保存」が有効なとき、対象のカード(.png)を上書きする。
  初回保存時に `.bak` を残すが、大事なカードは別途バックアップを取っておくこと
- **性格変更後のボイス** — `cachedVoiceNo` はクリアしているが、他にキャッシュが残っている可能性がある
- パネルのレイアウトはフォントサイズ 12 前提で幅を詰めてある。
  Config でフォントを大きくすると文字が収まらないことがある

ログは `BepInEx\LogOutput.log` の `[Info : HS2CharaParamEditor]` 以下に出る。
不具合を報告するときは、この行を添えてもらえると追いやすい。

---

## ビルド

Visual Studio 2022 で `HS2CharaParamEditor.sln` を開いてビルドする。

**ゲームの場所は `HS2CharaParamEditor.csproj` の `<GameDir>` で指定している。**
既定は `E:\illusion\HoneySelect 2` なので、環境に合わせてここ1箇所を書き換えること。
ビルド後、`$(GameDir)\BepInEx\plugins` へ自動でコピーされる。

| 項目 | 値 |
| --- | --- |
| ターゲット | .NET Framework 4.6 / Library |
| LangVersion | 7.3 |
| 文字コード | UTF-8 **BOM なし**（csproj に `<CodePage>65001</CodePage>`） |

参照アセンブリはすべてゲームと BepInEx に同梱されているものを使う。
リポジトリには含めていない。

| アセンブリ | 用途 |
| --- | --- |
| BepInEx.dll / 0Harmony.dll | プラグイン基盤とパッチ |
| HS2API.dll | `IMGUIUtils.EatInputInRect` |
| Assembly-CSharp.dll / -firstpass.dll | ゲーム本体の型 |
| IL.dll | `Singleton<T>`。`Manager.Game.Instance` 等に必要 |
| UnityEngine(.CoreModule / .IMGUIModule) | MonoBehaviour と IMGUI |
| UnityEngine.TextRenderingModule.dll | `Font.CreateDynamicFontFromOSFont` |
| UnityEngine.AudioModule.dll | `Manager.Voice.OncePlay` の戻り値 |

---

## 作者

isimodaman（いしもだマン）

## ライセンス

MIT License. [LICENSE](LICENSE) を参照。

## 謝辞

- パラメータの対応関係を調べる際に **HS2CharEdit** を参照した（コードの流用はしていない）
- **FemaleParameterInfo** が、この機能をロビーに置くという発想の元になっている
