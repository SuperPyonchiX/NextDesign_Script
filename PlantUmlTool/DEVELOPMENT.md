# PlantUmlTool 開発メモ

利用者向けの説明は [README.md](README.md) にある。ここには変更履歴、内部の仕組み、ビルドとテストの方法をまとめる。

## ビルドと配置

`PlantUmlTool.csproj` が `src/` の `.cs` を記載順にビルドして `PlantUmlTool.dll` を作る。using は csproj の `Using` 項目（global using）にまとめている。

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1   # 拡張をビルドして検査する（-Deploy で配置）
python PlantUmlTool/tests/run_class_sync_tests.py        # クラス図同期の純粋部テスト（tests/samples）
python Tools/Test-SequenceExport.py                      # シーケンス図出力のメッセージ処理（全拡張）
```

`manifest.json` を変更したら、配置する前に必ず検証を通す。マニフェストの誤りは Next Design 自体をエラーなしで起動不能にする。終了コード 0（WARN のみも可）で合格。

```
python <skills>/nextdesign-extension/scripts/validate_manifest.py PlantUmlTool --nd-version 3 --publish-dir work/publish/PlantUmlTool
```

DLL は Next Design の起動時にしか読み込まれない。差し替えるときは Next Design を終了してから配置する。開発環境の準備は [DLL 形式エクステンションの開発環境](../docs/dll-extension-setup.md)。

## ソースの構成

| ファイル | 内容 |
|---|---|
| `src/00-extension.cs` | エントリ `PlantUmlToolExtension`（IExtension） |
| `src/05-output-pane.cs` | 出力ウィンドウの表示（AgentReview / NdMcp は自前のものを使うのでビルドしない） |
| `src/10-sequence-export.cs` | シーケンス図の出力エンジン（SequenceImportProbe も使う） |
| `src/15-export-runner.cs` | 出力対象の収集とファイル書き出し（`DiagramEntry` / `ExportSettings` / `ExportRunner`） |
| `src/30-handlers.cs` | コマンドハンドラ（`PlantUmlToolExtension` の partial） |
| `src/40-class-export.cs` | クラス図の出力。本文は 60/61 の Snapshot + Writer で作る |
| `src/50-state-export.cs` | 状態遷移図の出力 |
| `src/60-class-sync.cs` | クラス図同期の純粋部（SDK 非依存。解析・書出し・差分計画・事前判定）。`tests/run_class_sync_tests.py` の対象 |
| `src/61-class-snapshot.cs` | クラス図の読取り（図 → 文書）。出力と同期の両方が使う |
| `src/62-class-sync-ui.cs` | 同期の結果表示と診断ファイル |
| `src/63-class-sync-runtime.cs` | クラス図同期の書込み・照合（SDK 依存） |
| `src/64-class-diagram-create.cs` | PlantUML から新しいクラス図を作る |
| `src/70-sequence-sync.cs` | シーケンス図同期の純粋部（文書・差分計画・事前判定・準備）。SequenceImportProbe のテストの対象 |
| `src/71-sequence-generate.cs` | PlantUML の解析と図の生成データ（`PumlPlan` / `PumlBuild` / `SequencePayload` / `SequenceJson`） |
| `src/72-sequence-sync-runtime.cs` | 図の読取り・反映・照合（SDK 依存） |
| `src/73-sequence-create.cs` | 型の解決（`PumlRuntime` / `SequenceTypeSource`）と Probe の旧取込 |
| `src/74-sequence-commands.cs` | リボンの入口（反映・新規作成） |
| `src/shims/metamap.cs` | 出力エンジンが使う `MetaMap.ModelOf` のシム。AgentReview / NdMcp がビルドする。PlantUmlTool はビルドしない（旧取り込みは SequenceImportProbe/src/40-legacy-import.cs） |

PlantUML 出力と同期の正本はここ。AgentReview・NdMcp・SequenceImportProbe・ClassImportProbe の csproj が `src/` のファイルを直接ビルドする（どれを使うかは [DLL 形式エクステンションの開発環境](../docs/dll-extension-setup.md) の「ソースの共有」）。出力や同期を直したら、使う拡張をすべてビルドし直す。

PlantUmlTool は機能だけを持つ。差分検証・診断表示・調査・一括検証のボタンは開発用の ClassImportProbe（クラス図）と SequenceImportProbe（シーケンス図）にある。

## 内部の仕組み

### 図の種類の判別

状態遷移図の EditorType はクラス図と同じ `ERDiagram`（実機確認済み）なので、シーケンス図 → 状態遷移図 → クラス図の順に判別する。状態遷移図の判定は次の順。

1. `StateEditorTypes` / `NonStateEditorTypes`（EditorType の明示指定。既定は空）
2. `ViewDefinitionName` の完全一致。既定は `ステートマシン図` / `状態遷移図` / `StateMachineDiagram` が状態遷移図、`クラス図` / `ClassDiagram` がクラス図
3. 図上ノードのメタクラス名（ClassName と全親クラス名）を `StateClassNames`（`Vertex` / `State` / `Pseudostate` / `HistoryState` 等の完全一致）と突き合わせ、状態系のノードがクラス系以上に多ければ状態遷移図

判定が外れたときは `src/50-state-export.cs` の `StatePlantUmlOptions` を直す。

| 症状 | 直す場所 |
|---|---|
| 状態遷移図がクラス図として出る | `StateViewDefinitionNames` にビュー定義名を追加、または `StateClassNames` に実際のメタクラス名を追加 |
| クラス図が状態遷移図として出る | `NonStateViewDefinitionNames` にビュー定義名を追加 |

### 状態遷移図の出力

拡張 API に状態遷移図専用のインタフェースは無い。`IDiagram` から取れるのはノードとコネクタだけなので、状態・擬似状態・遷移の意味はモデル側のメタクラス名とフィールドから取る。

対応表（`StatePlantUmlOptions`）の埋め方:

1. 状態遷移図を開いて ClassImportProbe の「クラス図調査」を実行する（任意の図で動く）。
2. 出力ウィンドウの `EditorType` / ノードのクラス名一覧 / コネクタの参照フィールドを確認する。
3. `StateKindMap`（メタクラス名 → state/initial/final/choice/…）、`TriggerFieldNames` / `GuardFieldNames` / `ActionFieldNames`（遷移ラベル）、`EntryFieldNames` / `ExitFieldNames` / `DoFieldNames`（内部アクション）に実名を写す。

対応表に無いメタクラスは state 扱いにして警告を出す。親の無い履歴擬似状態はステレオタイプ付き状態に退避して警告する。

### クラス図の出力

`ClassDiagram` というエディタ種別は存在しない。クラス図は EditorType が `ERDiagram`（プロジェクトによっては `TreeDiagram`）のエディタで、クラス・属性・操作・関連の意味はモデル側から取る。

2.2.0 から、出力は同期側の読取り＋書出し（`ClassDiagramSnapshot` + `ClassPumlWriter`）を使う。出力と比較が同じ経路なので、出力した直後のファイルを比較すると差分は 0 件になる。関連の矢印はフィールド名ごとの対応表（Related `-->`、SuperClasses `--|>` 等）で出す。オプション `IncludeTitle` / `Theme` / `EmitTimestamp` / `HideEmptyMembers` はヘッダに反映する。それ以外の `ClassPlantUmlOptions` はクラス図の出力には効かない（状態遷移図の出力は引き続き使う）。

`src/40-class-export.cs` の `ClassPlantUmlOptions`（プロファイル依存）:

| メンバ | 用途 |
|---|---|
| `KeywordMap` | メタクラス名 → `class` / `interface` / `enum` / `abstract class` … |
| `StereotypeMap` | メタクラス名 → `<<...>>`。空文字を入れるとステレオタイプを出さない |
| `MemberKindMap` | 子モデルのメタクラス名 → `attribute` / `operation` / `literal` / `skip` |
| `LinkMap` | 参照フィールド名 → 矢印 |
| `VisibilityMap` | 可視性の値 → `+` `-` `#` `~` |
| `TypeFieldNames` ほか | 型・多重度・可視値・既定値・引数・戻り値を探すフィールド名の候補 |

| オプション | 既定 | 意味 |
|---|---|---|
| `EmitMembers` | `true` | 属性・操作を出す |
| `EmitPackages` | `true` | オーナーを `package` でまとめる |
| `EmitEmbedded` | `false` | 所有関連も線にする。属性の親子まで線になるので既定は off |
| `EmitRoleNames` | `true` | リンクのラベルにフィールド名を出す |
| `EmitMultiplicity` | `true` | 多重度を出す |
| `MergeBidirectional` | `true` | 双方向の関連を1本にまとめる |
| `EmitUnknownStereotype` | `true` | 対応表に無いメタクラス名もそのまま `<<...>>` に出す |

対応表に無いものは既定の扱い（メンバは属性、関連は `-->`）にして、出力ウィンドウに `[warn]` 行を出す。対応表を埋めるときは ClassImportProbe の「クラス図調査」で `EditorType`・ノードと子モデルの `ClassName` 一覧・コネクタの参照フィールドを確かめ、`KeywordMap` / `MemberKindMap` / `LinkMap` に写す。

### 一括出力のフォルダ

`<出力先>/<種別>/<図グループ>/…/<図の直接の親>/<図名>.puml` に書く。図グループは、図のメタクラスを所有フィールドの型として宣言している祖先のうちいちばん上のもの（判別できなければ直接の親だけ）。同じ親の下で名前が重なる別モデルのフォルダには ID の短いハッシュを付ける。`ExportSettings.GroupFolders=false` で従来の平置き。

### シーケンス図の反映・新規作成

- 本体（`src/70〜73`）は SequenceImportProbe 0.12.1 で実機検証したもの（全図チェック 680/684、一括検証 97/97）。
- 反映は図の写しを SDK から組み立て、照合が一致したときだけ確定し、一致しなければ元に戻す。
- 未保存のまま更新したあとの Ctrl+Z は、図を最後に保存した状態の図形に戻す（製品のエディタ取込の Undo の挙動）。MCP など呼び出し元が `UpdateWithoutSaving` を立てた場合は確認を出さない。
- 操作に結び付いたメッセージは、図のラベルが操作から作られる（`EndProcess : void` など）。入力がモデルの名前ならそのまま（変更なし）と読み、名前を変えた場合はラベルの文字だけ読み戻しを受け入れる。
- シーケンスのビュー定義は、要素に Lifeline・Message・ExecutionSpecification を持つもので見分ける。
- 診断ファイルは `%LOCALAPPDATA%\NextDesign.SequenceSync\reports`。

### クラス図の反映・新規作成

- 扱える差分・停止条件・各版の実機手順は [docs/class-sync-history.md](../docs/class-sync-history.md)。
- 関連を足すときは、線の表示のため更新前の図の Editor JSON を退避する。そのため保存済みで未保存の変更がないプロジェクトが要る（C220）。
- 新規作成は見本の図を使わない。図の欄とメタクラスはグループの既存クラス図から、無ければプロファイルのエディタ定義から決める。箱の形はビュー定義から取る（`FindElementDefByClass`）。
- 作ったばかりのクラス図は、エディタに開くまで AddNodeShape を拒否する（ClassImportProbe 0.8.2 の調査）。図のモデルを作って確定し、開いてから箱を置く。
- 途中で1回プロジェクトを保存する。失敗・中止したときは作成した図と仮に作ったクラスを削除する。
- 作成中だけ効く分岐: 渡された線の雛形で関連を表示する、雛形が無いときは線を非表示のまま進める、空のクラスへメンバを足すとき同じ package の同じメタクラスのクラスからメンバのメタクラスを借りる、同じクラスの隣に複数足すとき重ならないよう下へずらす。
- 名前に括弧を含む操作は、「後ろに何も無いか ` : 戻り値` だけが続く」最初の括弧を引数とし、その前を名前として読む。
- 診断ファイルは `%LOCALAPPDATA%\NextDesign.ClassSync\reports`（モデル名・ID を含むので共有しない）。NdMcp の `/class-sync/*` も同じ本体を使う。

### シーケンス図の旧取り込み

ソース内の旧パーサー・ライターは SequenceImportProbe に移した。「V3 ではスクリプトによる作成が不可能」という以前の説明は根拠を確認できなかったため撤回した。公式 SDK には JSON インポート API がある。入力仕様とサポート範囲は [API 調査記録](../docs/sequence-import-api-research.md)。

## 変更履歴

- **3.3.6**: 一括出力の `_index.md` のリンク先を、日本語のまま読める `<…>` 形式にした（`#` と `%` だけ符号化）。AgentReview 0.17.2 と同じ書き方。
- **3.3.5**: NdMcp 0.4.0（シーケンス図同期 API）向けに、直前の同期結果の報告と、ダイアログを出さない `SequenceDiagramCreator.Create` を外から使えるようにした。リボンの動作は変わらない（コミット 4a4f264。旧 README に記載がなかったので追記）。
- **3.3.4**: 未保存のプロジェクトでシーケンス図を更新するとき、先に保存するかを聞くようにした。
- **3.3.2**: 新しいクラス図は、開いてから箱を置くようにした（作ったばかりの図は箱の追加を拒否するため）。実機で作成に成功（2026-09-26）。
- **3.3.1**: 「PlantUMLを反映」を「PlantUMLで更新」に改名。
- **3.3.0**: 反映と新規作成のボタンを、シーケンス図・クラス図で1つずつにまとめた。図の種類は開いているもの・選んでいるモデルから決める。
- **3.2.8**: 名前に括弧を含む操作を属性と読み違えて止まる不具合を修正。
- **3.2.3**: 反映の確定後に、利用者の直前の編集を Undo してしまう不具合を修正（Probe 由来の自己確認を外した）。
- **3.2.0**: 一括出力を種別・図グループのフォルダに分け、出力先の選択を1回にした。
- **3.1.1**: 操作に結び付いたメッセージが、無編集でも名前の変更と判定される不具合を修正。
- **3.1.0**: シーケンス図の反映・新規作成を追加。診断・調査のボタンを開発用拡張へ移した。
- **3.0.0**: スクリプト（main.cs）から DLL に移行。機能は 2.4.4 と同じ。`10-sequence-export.cs` の後半を `15-export-runner.cs` に分けた。
- **2.4.0〜2.4.4**: 見本の図なしで PlantUML から新しいクラス図を作れるようにした。出力した .puml の package 表記・経路をそのまま受け付ける。2.3.x の「開いている図を見本にする方式」は見本が無い場合に使えないため廃止。
- **2.2.1**: 「試行して戻す」を削除（「PlantUMLを反映」も照合して不一致なら取り消すため）。
- **2.2.0**: ClassImportProbe 0.7.2 で実機検証したクラス図の同期を統合。クラス図の出力を同期側の読取りに切り替え、操作の戻り値と属性の多重度が出るようになった。実機確認（2026-09-22）済み。
- **2.1.3**: 2.1.2 の自由 Note 出力を取り消し、近傍ライフラインへの `note over` に戻した。
- **2.1.1**: シーケンス図で破棄の後に余分な `activate` / `deactivate` が出る不具合を修正。
- 旧 `PlantUmlExport` の後継。
