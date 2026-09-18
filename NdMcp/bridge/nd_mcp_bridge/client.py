"""NdMcp 拡張（Next Design 内の HTTP サーバー）への薄いクライアント。標準ライブラリのみ。"""
from __future__ import annotations

import json
import os
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_URL = "http://127.0.0.1:3560"

NOT_RUNNING_HINT = (
    "Next Design 側のサーバーに接続できません。"
    "Next Design でプロジェクトを開き、リボン「NdMcp」タブの「サーバー開始」を押してください。"
    "（ポートを変えている場合は環境変数 ND_MCP_URL を合わせてください）"
)


class NdError(Exception):
    """NdMcp からのエラー応答、または接続失敗。"""


class NdClient:
    def __init__(self, base_url: str | None = None, timeout: float = 120.0):
        self.base_url = (base_url or os.environ.get("ND_MCP_URL") or DEFAULT_URL).rstrip("/")
        self.timeout = timeout

    def get(self, path: str, params: dict | None = None, timeout: float | None = None) -> dict:
        query = {k: v for k, v in (params or {}).items() if v not in (None, "")}
        url = self.base_url + path
        if query:
            url += "?" + urllib.parse.urlencode(query, encoding="utf-8")
        try:
            with urllib.request.urlopen(url, timeout=timeout or self.timeout) as res:
                return json.loads(res.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            try:
                message = json.loads(body).get("error", body)
            except ValueError:
                message = body
            raise NdError(f"NdMcp エラー (HTTP {e.code}): {message}") from None
        except urllib.error.URLError as e:
            raise NdError(f"{NOT_RUNNING_HINT} [{self.base_url}: {e.reason}]") from None
        except TimeoutError:
            raise NdError(f"NdMcp が応答しません（{self.base_url}）。Next Design がダイアログ等で止まっていないか確認してください") from None
