// ============================================================
//  NdMcp / main.cs  (Next Design V3.x スクリプト拡張)
//
//  ★ このファイルは tools/build_main.py が生成する。直接編集しない。
//     編集対象: src/header.cs / src/server.cs（サーバー本体）
//               AgentReview/main.cs の Part 0 / 4 / 7 / 8（エクスポータ。転記元）
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
//    - 読み取り専用。モデルへの書き込み API は持たない
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
//  設定: %USERPROFILE%\.nd-mcp\config.ini（port= / exportDir=）
//  ログ: %USERPROFILE%\.nd-mcp\server.log
// ============================================================

using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
