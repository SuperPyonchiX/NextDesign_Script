IViewDefinitions.RegisterGetCompartmentItemTextStyleCallback(IElementDef,string,Func<INode, int, string, IModel, IStyleProperty, object>,StyleAttributes) メソッド
==============================================================================================================================================================

名前空間: NextDesign.Core

説明​
-----------------------

スタイル属性を指定して、指定したエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・登録したコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された

引数​
-----------------------

名前

型

説明

elementDef

IElementDef

エディタ要素定義

areaPath

string

コンパートメント区画のパス

getter

Func<INode, int, string, IModel, IStyleProperty, object>

スタイル値取得のコールバック関数  
  
**コールバック関数仕様**  
object object get\_ItemTextStyleFunction(INode node, int areaIndex, string areaPath, IModel itemModel, IStyleProperty target)  
  
\- 引数  
\- node : INode ・・・スタイル適用対象のコンパートメントアイテムを所有するシェイプが引き渡されます  
\- areaIndex : int ・・・スタイル適用対象のコンパートメントアイテムが属する区画インデックスが引き渡されます  
\- areaPath : string ・・・スタイル適用対象のコンパートメントアイテムが属する区画定義に設定されているパス文字列（フィールド名）が引き渡されます  
\- itemModel : IModel ・・・スタイル適用対象のコンパートメントアイテムが参照するモデルが引き渡されます  
\- target : IStyleProperty ・・・対象スタイル属性情報が引き渡されます  
\- 返値  
\- object ・・・ Getの結果として扱うオブジェクトを返します  
\- コールバック関数が指定された対象スタイル属性の型に合致するオブジェクトでなければなりません  
\- 型に合致しないオブジェクトを戻した場合、特に例外とはならず`IStyleProperty.CurrentValue`の値に自動変換されます  
\- 例外の扱い  
\- コールバック関数の実装で例外をスローした場合、その例外は捕捉されて、コールバックの呼び出し結果として`IStyleProperty.CurrentValue`の値が使用されます  

properties

StyleAttributes

スタイル属性  
（複数のスタイル属性名が指定された場合、それぞれについてコールバック登録行う）

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

elementDef に null を指定した場合  
areaPath に null、または空文字を指定した場合

キーが重複

ExtensionDuplicationException

コールバック登録キーが重複する場合