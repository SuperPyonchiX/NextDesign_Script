// ============================================================
//  Next Design エクステンション : PlantUML 連携（出力 / 取り込み）
//  エントリポイント（C# スクリプト / Next Design V3.x）
//
//  ★ このファイルは tools/build_main.py が src/*.cs をファイル名順に連結して生成する。
//     直接編集しない。編集対象は src/ 配下（構成の各 Part がそのままファイルになっている）。
//
//  構成:
//    Part 0  出力エンジン  PlantUmlOptions / PlantUmlText / SeqEvent /
//                          OpenFragment / SequencePlantUmlExporter
//    Part 0  出力実行部    DiagramEntry / ExportSettings / ExportRunner
//    Part 1  解析層        AST / PlantUmlSequenceParser
//    Part 2  適用層 (1)    MetaMap（メタモデルの自動判別）
//    Part 3  適用層 (2)    平坦化 / 既存索引 / 突き合わせ / 差分プラン
//    Part 4  適用層 (3)    ActivationResolver / WriteResult / SequenceWriter
//    Part 5  実行層        MetaProbe / ImportRunner
//    Part 6  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
//    Part 7  クラス図出力  ClassPlantUmlOptions / ClassDiagramCollector /
//                          ClassPlantUmlExporter / ClassExportRunner / ClassProbe
//    Part 8  状態遷移図出力 StatePlantUmlOptions / StateDiagramCollector /
//                          StatePlantUmlExporter / StateExportRunner
//
//  制約:
//    - main に指定できるファイルは1つだけ。src/ を分割して書き、生成物を配置する
//    - 変換エンジンはグローバルオブジェクト（App / UI / Output）に触らない。
//      クラス内からは参照できないため、IApplication を引数で受け取る
//    - デバッガは使えない。Output.WriteLine が唯一の手がかりになる
//    - 変更は Next Design を再起動するまで反映されない
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
