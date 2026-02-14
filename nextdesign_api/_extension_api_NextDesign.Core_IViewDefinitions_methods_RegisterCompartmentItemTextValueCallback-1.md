IViewDefinitions.RegisterCompartmentItemTextValueCallback(IElementDef,string,Func<INode, int, string, IModel, string>,Action<INode, int, string, IModel, string>) メソッド
======================================================================================================================================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムの値取得/設定コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・登録した値取得のコールバック関数が呼び出される契機は以下です。  
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

Func<INode, int, string, IModel, string>

テキスト値取得のコールバック関数  
  
**コールバック関数仕様**  
string get\_TextValueFunction(IShape shape, TextTypes type, string textPath, IModel model)  
  
\- 引数  
\- node : INode ・・・スタイル適用対象のコンパートメントアイテムを所有するシェイプが引き渡されます  
\- areaIndex : int ・・・スタイル適用対象のコンパートメントアイテムが属する区画インデックスが引き渡されます  
\- areaPath : string ・・・スタイル適用対象のコンパートメントアイテムが属する区画定義に設定されているパス文字列（フィールド名）が引き渡されます  
\- itemModel : IModel ・・・スタイル適用対象のコンパートメントアイテムが参照するモデルが引き渡されます  
\- 返値  
\- string ・・・ Getの結果として扱う文字列を返します  
\- 例外の扱い  
\- コールバック関数の実装で例外をスローした場合、その例外は捕捉されて、textPathのフィールド値が使用されます  

setter

Action<INode, int, string, IModel, string>

テキスト値設定のコールバック関数  
  
**コールバック関数仕様**  
void set\_TextValueFunction(IShape shape, TextTypes type, string textPath, IModel model, string value)  
  
\- 引数  
\- node : INode ・・・スタイル適用対象のコンパートメントアイテムを所有するシェイプが引き渡されます  
\- areaIndex : int ・・・スタイル適用対象のコンパートメントアイテムが属する区画インデックスが引き渡されます  
\- areaPath : string ・・・スタイル適用対象のコンパートメントアイテムが属する区画定義に設定されているパス文字列（フィールド名）が引き渡されます  
\- itemModel : IModel ・・・スタイル適用対象のコンパートメントアイテムが参照するモデルが引き渡されます  
\- value : string ・・・ラベルのテキスト値として設定が要求されたテキストが引き渡されます  
\- 例外の扱い  
\- コールバック関数の実装で例外をスローした場合、テキストの設定は取り消されます  

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