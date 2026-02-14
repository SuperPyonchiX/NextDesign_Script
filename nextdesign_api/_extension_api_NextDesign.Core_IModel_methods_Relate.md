IModel.Relate メソッド
==================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定したフィールドの末尾で指定したモデルを関連づけて、追加した関連インスタンスを返します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

opposite

IModel

関連づけするモデル  
null は指定できません。

戻り値​
--------------------------

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

opposite に null を指定した場合  
fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合

不正操作

ExtensionInvalidOperationException

自身が削除済みモデル、一時プロキシの場合  
フィールド名に操作不可のフィールドが指定された場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
\- 所有フィールド  
無効なフィールドが指定された場合

制約違反

ExtensionIllegalFieldAccessException

関連づけするモデルが指定したフィールドのデータ型と互換しない場合  
関連づけにより、フィールドの多重度制約に違反する場合  
関連づけにより、フィールドのパス制約に違反する場合

無効なモデルを指定した

ExtensionInvalidModelException

関連づけするモデルに削除されたモデル、一時プロキシが指定された場合