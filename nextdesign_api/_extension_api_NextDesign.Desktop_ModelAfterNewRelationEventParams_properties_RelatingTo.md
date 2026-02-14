ModelAfterNewRelationEventParams.RelatingTo プロパティ
=================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

新しい関連先

    IModel RelatingTo { get; }

型​
--------------------

*   [IModel](_extension_api_NextDesign.Core_IModel.md)

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

Ver.1.1 より、このプロパティで取得できるモデルが、以下のように変更されました。

*   変更前：UI上の操作終点に対応するモデル
*   変更後：UI上でのユーザ操作に関わらず、関連クラスのTarget側クラスに対応するモデル

本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所を変更してください。