IModelEventDispatcher.RegisterPropertyChangedEventHandler メソッド
==============================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたオブザーバにおいて、指定されたモデルのプロパティ変更通知を受信するイベントハンドラを登録します。

引数​
-----------------------

名前

型

説明

observer

object

オブザーバ

modelId

string

監視対象モデルの識別子

handler

Action<IModel, PropertyChangedEventArgs>

イベントハンドラ  
  
**イベントハンドラ仕様**  
　シグネチャ void on\_modelpropertychanged(IModel sender, PropertyChangedEventArgs args);  
\-　引数  
　- sender : IModel・・・変更があったモデル  
　- args : PropertyChangedEventArgs・・・プロパティ変更イベントパラメータ

戻り値​
--------------------------

*   void