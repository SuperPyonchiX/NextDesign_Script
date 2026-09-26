// ============================================================
//  AgentReview / Claude Code・Codex による設計レビュー支援
//  エントリポイント（DLL / Next Design V3.x）
//
//  AgentReview.csproj が src/ の .cs と、PlantUmlTool/src の PlantUML 出力部を
//  記載順にビルドして AgentReview.dll を作る。PlantUML 出力の修正は PlantUmlTool/src で行う。
//
//    役割分担:
//      - 本拡張 : 設計情報のエクスポート / エージェント向け指示書の生成 /
//                 ターミナルでのエージェント起動 / 結果ファイルの表示
//      - 対話   : ターミナル上の claude / codex 本来の UI に委ねる
//        （V3.x の拡張 UI では対話画面を作れず、コマンドは UI スレッド
//          同期実行のため、CLI の完了を待つと Next Design が固まる）
//    Next Design のモデルへの書き戻しは行わない（V3.x は読み取り専用
//    要素が多いため。修正は review/ 配下への提案ファイル出力まで）。
//
//    ファイル構成:
//      00  エントリ           AgentReviewExtension（IExtension）
//      01  Part 0 共通ヘルパ   AgentText / OutputPane（NdMcp も使う）
//      02  Part 1 設定         AgentConfig（%USERPROFILE%\.nd-agent-review\config.ini）
//      03  Part 2 セッション   SessionInfo / SessionLocator
//      04  Part 3 ワークスペース WorkspaceBuilder（フォルダ・指示書・session.ini）
//      05  Part 4 Markdown出力 MarkdownExportOptions / MarkdownExporter / HtmlToMarkdown（NdMcp も使う）
//      06  Part 5 プロセス起動 TerminalLauncher / CliProbe
//      07  Part 6 コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
//      08  NdMcp と共有する部品 ReviewSnapshot.Cell / ChangeRecord / DesignArtifactWriter
//      PlantUmlTool/src の 10 / 15 / 40 / 50 / 60 / 61 と shims/metamap.cs（PlantUML 出力）
//
//    using は AgentReview.csproj の Using 項目（global using）にまとめる。
//    skills/ は拡張フォルダに同梱する（csproj が出力へコピーする）。
// ============================================================

public partial class AgentReviewExtension : IExtension
{
    public void Activate(IContext context)
    {
    }

    public void Deactivate(IContext context)
    {
    }
}
