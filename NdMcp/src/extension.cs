// ============================================================
//  NdMcp  (Next Design V3.x の DLL 拡張)
//
//  NdMcp.csproj が次を記載順にビルドして NdMcp.dll を作る。
//    src/extension.cs（このファイル）/ src/server.cs（サーバー本体）/ src/classsync.cs（クラス図同期の窓口）
//    AgentReview/src の 01 / 05 / 08（共通ヘルパ・Markdown 出力・共有部品）
//    PlantUmlTool/src の 10 / 15 / 40 / 50（PlantUML 出力）と 60 / 61 / 63（クラス図同期）
//  共有部品の修正はそれぞれの正本（AgentReview/src、PlantUmlTool/src）で行う。
//  using は NdMcp.csproj の Using 項目（global using）にまとめる。
//
//  Next Design のモデルを MCP（Model Context Protocol）クライアントから
//  読めるようにするための、Next Design 側のサーバー。
//
//    Claude Code ── stdio(MCP) ── Python ブリッジ(bridge/) ── HTTP ── この拡張
//
//  構成:
//    - リボン「NdMcp」タブの「サーバー開始」で 127.0.0.1:3560 に HttpListener を立てる
//      （ハンドラは UI スレッドで同期実行されるため、受付だけ仕掛けて即 return する）
//    - リクエストはスレッドプールで受け、ND API を触る処理は
//      SynchronizationContext.Send() で UI スレッドへ戻してから実行する
//    - 応答は JSON（手書きの Json ライタ。GET + クエリ文字列のみなので JSON パーサは不要）
//    - モデル読み出しは読み取り専用。書き込みは /class-sync/trial と /class-sync/apply だけ
//
//  API（すべて GET。path= はモデルパス、id= はモデル ID。両方空ならプロジェクト）:
//    /ping                        生存確認（ND API 非依存）
//    /thread                      スレッド診断
//    /project                     プロジェクト概要と直下モデル
//    /tree?path=&id=&depth=2      モデルツリー
//    /model?path=&id=             モデル詳細（全フィールド）
//    /search?q=&metaclass=&limit= 名前の部分一致検索
//    /markdown?path=&id=          サブツリーを design.md 形式の Markdown で返す
//    /export?path=&id=&out=       design.md + diagrams\*.puml + _index.md をフォルダへ書き出す
//
//  クラス図同期（PlantUmlTool の同期本体を転記。この API だけがモデルへ書き込む）:
//    GET  /class-sync/editors?path=&id=          モデルに紐づく図の一覧
//    GET  /class-sync/current?path=&id=&editor=  クラス図を PlantUML（PlantUmlTool 互換の書式）で返す
//    POST /class-sync/preview  {path|id, editor?, plantuml|file}  比較のみ
//    POST /class-sync/trial    {path|id, editor?, plantuml|file}  一時適用して照合し、必ず取り消す
//    POST /class-sync/apply    {path|id, editor?, plantuml|file}  確定する（Undo 可）
//
//  設定: %USERPROFILE%\.nd-mcp\config.ini（port= / exportDir=）
//  ログ: %USERPROFILE%\.nd-mcp\server.log
// ============================================================

public partial class NdMcpExtension : IExtension
{
    public void Activate(IContext context)
    {
    }

    public void Deactivate(IContext context)
    {
    }
}
