onFieldChanged イベント
===================

説明​
-----------------------

フィールド値の変更後に通知されるイベントです。

APIイベント引数型​
-----------------------------------------------

*   [ModelFieldChangedEventParams](_extension_api_NextDesign.Desktop_ModelFieldChangedEventParams.md)

注釈​
-----------------------

コピーペースト操作の場合にもフィールド値変更後イベントが通知されますが、通知されたタイミングで一部のAPIが正しく情報を取得できない場合があります。  
フィールド値変更後イベント処理では、該当するAPIを呼び出さない処理だけを利用してください。  
代わりにモデル編集イベント(onModelEdited)を使うことを検討してください。

*   IModelのメンバ
    *   IModel Owner {get;}
    *   string ModelPath {get;}
    *   bool IsEditable {get;}
    *   IModelUnit ModelUnit {get;}
    *   IModelCollection GetOwners():
    *   IModel FindOwnerByClass(string, bool);
    *   IRelationship GetOwnerRelationship();
    *   IField GetOwnerField();
    *   void MoveTo(IModel, string, string, int);
*   IRelationshipのメンバ
    *   IModel Source {get;}
    *   IModel Target {get;}