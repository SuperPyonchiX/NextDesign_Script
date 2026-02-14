IViewDefinitions.RegisterGetTextStyleCallback(IElementDef,TextTypes,Func<IShape, TextTypes, string, IModel, IStyleProperty, object>,StyleAttributes) メソッド
=========================================================================================================================================================

名前空間: NextDesign.Core

説明​
-----------------------

スタイル属性を指定して、指定したエディタ要素定義より生成されるエディタ要素の指定されたテキストのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、RegisterGetStyleCallbackのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された

引数​
-----------------------

名前

型

説明

elementDef

IElementDef

エディタ要素定義

type

TextTypes

テキストの種類

getter

Func<IShape, TextTypes, string, IModel, IStyleProperty, object>

スタイル値取得のコールバック関数  
  
**コールバック関数仕様**  
object get\_TextStyleFunction(IShape shape, TextTypes type, string textPath, IModel model, IStyleProperty target)  
  
\- 引数  
\- shape : IShape ・・・スタイル適用対象のラベル/テキストを所有するシェイプが引き渡されます  
\- type : TextTypes ・・・対象テキストの種類（列挙）が引き渡されます  
\- textPath : string ・・・テキストのビュー定義でマッピングされたパス文字列（フィールド名）が引き渡されます  
\- model : IModel ・・・タイル適用対象のエディタ要素が参照するモデルが引き渡されます  
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

キーが重複

ExtensionDuplicationException

コールバック登録キーが重複する場合

注釈​
-----------------------

ラベル数が11件以上定義されている場合は、11件目以降のラベルにはスタイル値取得のコールバック関数を適用できません。