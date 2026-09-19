# シーケンス生成実験 0.1.0

Next Design V3.1.9で、ライフライン2本と同期メッセージ1本の新しい図を生成するためのC#スクリプト拡張。実機での生成は未確認。問い合わせを前提にせず、生成結果と診断を使って実装を調整する。

既存の `ModelUpdateProbe` とは別フォルダで配置する。メッセージ変更用の拡張はそのまま利用できる。

## 配置と操作

1. `git pull` 後、`SequenceImportProbe` フォルダを他の拡張と同じ `extensions` フォルダへコピーする。ZIPの展開やビルドは不要。
2. Next Designを再起動する。
3. **コピーしたプロジェクト**を開く。異なるライフライン間の同期メッセージがある既存シーケンス図をメインエディタに表示する。
4. 「シーケンス生成実験」タブの「最小図を生成」を押す。コピーであることの確認後、ログの保存先を選ぶ。
5. `A → B : probe()` を作成する確認画面で「はい」を選ぶ。JSONやIDの入力は不要。
6. 成功したら、元の図と同じ親モデルの配下に追加された `SEQ_PROBE_...` を開く。結果画面と新しい図のスクショを渡す。

停止したら結果画面を渡す。原因の説明が足りない場合は「診断表示」で詳細を開ける。詳細にはプロファイルの属性名やIDが含まれる場合がある。ファイルを編集する必要はない。

失敗後は保存せずに検証用コピーを開き直してから再実行する。取消APIが正常終了しても、プロジェクト全体の復元が確認できたとは扱わない。

## 何を試すか

- 選択中の図から、相互作用・フレーム・ライフライン・実行仕様・メッセージの型IDとビュー定義IDを取得する。
- 標準構造関連のIDと `MessageSort=Sync` が現在のプロファイルにあることを検査する。
- 新しいモデル7件、構造関連10件、エディタ1件とシェイプ6件のJSONを自動生成する。既存の業務モデルを参照先に含めない。
- `BeginUndoTransaction(false)` の中で `IProject.ImportUnitFromJson` を1回呼ぶ。
- 戻り値が成功で、警告・エラーがなく、モデル数・名前・送受信先・シェイプ数の読戻しが一致した場合のみ確定する。それ以外は取消を試みる。

この実験は新規図の作成のみ。任意のPlantUMLファイルの取り込み、既存図の更新、複合フラグメントは次の段階で扱う。APIによる読戻し一致と、図の正常表示・保存後の再読込・Undo/Redoは別々に確認する。

## 仮説と根拠

入力構造は[公式サンプルのプロジェクト](https://github.com/denso-create/NextDesign-Samples/blob/main/ndcli-extensions/ModelValidation/project/先進運転システムソフト開発.nproj)の保存形式を参照した。サンプルの業務データ・固有IDは同梱せず、生成する名前は `A`、`B`、`probe()` の固定値とする。

JSON形式で保存されたプロジェクトでは、先頭の `SchemaVersion` を利用する。取得できない保存形式では公式サンプルに見られた `13.0` を仮の値として使い、その判断をログに残す。保存形式の互換性、既定のシェイプスタイル、インポート時の整合性検査は実機で切り分ける。[API調査記録](../docs/sequence-import-api-research.md)も参照。

## 記録

保存先に `sequence_日時_ID` フォルダを作り、次を保存する。機密情報を含む可能性があるため会社PC内で扱う。

| ファイル | 内容 |
|---|---|
| `input.json` | 実際に渡す新規モデル・関連・シェイプ |
| `before.txt` | スキーマの選び方・SDK・親と見本のID |
| `checked.txt` | 確定直前の照合結果。照合完了時のみ |
| `result.txt` | 最終結果・API診断・例外・取消の結果 |

## 開発側の検査

```text
python SequenceImportProbe/tests/run_tests.py
python SequenceImportProbe/tests/run_tests.py --sdk-root work/sequence-api-research
python <skills>/nextdesign-script-extension/scripts/validate_manifest.py SequenceImportProbe --nd-version 3
```

1つ目は生成JSONのID独立性、所有構造、送受信、シェイプ、座標、文字列エスケープと不正入力を検査する。2つ目はさらに公式 `NextDesign.Core / Desktop 3.1.3.30714` と `.NET 6` 参照アセンブリで配布スクリプト全体をコンパイルする。SDKは開発PCの作業フォルダにのみ配置する。

これらの検査ではNext Designを起動しない。インポートの成功、失敗時の復元、実機の表示は保証しない。
