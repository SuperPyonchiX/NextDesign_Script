ICommandParams.ToCollection メソッド
================================

名前空間: NextDesign.Desktop

説明​
-----------------------

パラメータの内容をオブジェクトコレクションに変換します。

戻り値​
--------------------------

*   ICollection

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

Ver.1.1 より、このプロパティの型が、IObjectCollection から、(C#) ICollection に変更されます。  
本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所を変更する必要があります。

次の例を参考に変更してください。

**変更前**

    IObjectCollection params = parameters.ToCollection();

**変更後**

    System.Collections.ICollection params = parameters.ToCollection();