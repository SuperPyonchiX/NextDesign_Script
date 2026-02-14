IModel.MoveTo メソッド
==================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスを指定したモデルの子要素となるように移動します。  
移動先の親要素、およびフィールドが、現在の親要素、およびフィールドと同一の場合は、指定フィールドにおける要素の順序を変更します。  
なお、移動先フィールドの多重度上限制約が違反しても例外はスローされません。

引数​
-----------------------

名前

型

説明

newOwner

IModel

移動先（新しい親モデル）  
nullを指定することはできません。

fieldName

string

フィールド名  
null、または空文字列は指定できません。

direction

string

移動先における追加方向  
\- "first" : 先頭  
\- "last" : 末尾  
\- "before" : 前  
\- "after" : 後

index

int

追加基準位置  
先頭位置を0とするインデックスを指定します。  
direction の指定が "first"の場合は、インデックスの値に関係なく先頭に移動します。  
direction の指定が"last"の場合は、インデックスの値に関係なく末尾に移動します。  
direction の指定が "before"の場合は、指定された位置に、"after"の場合は、指定された位置の次の位置にインスタンスを移動します。

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

newOwner に null を指定した場合  
fieldName に null、または空文字列 を指定した場合  
direction に既定の文字列以外の文字列が指定された場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドが移動先のインスタンスのメタクラスで見つからない場合

インデックス範囲不正

ExtensionOutOfRangeException

index に 負数が指定された場合  
または、index に移動先の該当フィールドの要素数以上の値が指定された場合

指定フィールド不正

ExtensionInvalidTypeException

指定されたフィールドが所有フィールドでない、または、指定されたフィールドのデータ型がこのインスタンスのメタクラスと互換しない

不正操作

ExtensionInvalidOperationException

自身が削除済みモデル、もしくは一時プロキシの場合  
フィールド名に操作不可のフィールドが指定された場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
無効なフィールドが指定された場合

循環参照

ExtensionCircularReferenceException

newOwnerに、このメソッドの呼び出し対象自身またはその子孫を指定した場合

無効なモデルを指定した

ExtensionInvalidModelException

移動先のモデルに削除されたモデル、もしくは一時プロキシが指定された場合  
自身がプロジェクト、もしくはユニットにおける基点であり、親要素が読み込まれていないモデルの場合