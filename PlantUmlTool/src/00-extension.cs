// ============================================================
//  Next Design エクステンション : PlantUML 連携（出力 / 取り込み）
//  エントリポイント（DLL / Next Design V3.x）
//
//  PlantUmlTool.csproj が src/ の .cs を記載順にビルドして PlantUmlTool.dll を作る。
//  AgentReview・NdMcp・SequenceImportProbe も、PlantUML の出力と同期のファイルを
//  ここ（PlantUmlTool/src）から直接ビルドする。修正はここで行い、各拡張をビルドし直す。
//
//  構成:
//    00  エントリ          PlantUmlToolExtension（IExtension）
//    05  出力ペイン        OutputPane
//    10  出力エンジン      PlantUmlOptions / PlantUmlText / SeqEvent /
//                          OpenFragment / SequencePlantUmlExporter
//    15  出力実行部        DiagramEntry / ExportSettings / ExportRunner
//    20  取り込み（旧）    AST / PlantUmlSequenceParser / MetaMap / SequenceWriter など
//    30  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
//    40  クラス図出力      ClassPlantUmlOptions / ClassDiagramCollector /
//                          ClassPlantUmlExporter / ClassExportRunner
//    45  クラス図の診断    ClassProbe
//    50  状態遷移図出力    StatePlantUmlOptions / StateDiagramCollector /
//                          StatePlantUmlExporter / StateExportRunner
//    60〜64 クラス図同期   文書・読取り・画面・実行・新規作成
//
//  制約:
//    - using は PlantUmlTool.csproj の Using 項目（global using）にまとめる。各ファイルに書かない
//    - 変換エンジンは IApplication を引数で受け取る（ハンドラ以外から App に触らない）
//    - DLL は Next Design の起動時にしか読み込まれない。差し替えは終了してから行う
// ============================================================

public partial class PlantUmlToolExtension : IExtension
{
    public void Activate(IContext context)
    {
    }

    public void Deactivate(IContext context)
    {
    }
}
