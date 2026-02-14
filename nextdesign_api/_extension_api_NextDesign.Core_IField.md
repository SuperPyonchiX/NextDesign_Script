IField インタフェース
==============

名前空間: NextDesign.Core

説明​
-----------------------

フィールドへのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[INamedElement](_extension_api_NextDesign.Core_INamedElement.md)

名前付け可能要素を表します。

プロパティ​
--------------------------------

名前

説明

[Category](_extension_api_NextDesign.Core_IField_properties_Category.md)

フィールドのカテゴリ  
カテゴリが未指定の場合は、空文字列となります。

[DefaultValue](_extension_api_NextDesign.Core_IField_properties_DefaultValue.md)

デフォルト値

[IsDerivationSource](_extension_api_NextDesign.Core_IField_properties_IsDerivationSource.md)

導出元フィールドか

[IsDerivationTarget](_extension_api_NextDesign.Core_IField_properties_IsDerivationTarget.md)

導出先フィールドか

[IsEmbedded](_extension_api_NextDesign.Core_IField_properties_IsEmbedded.md)

所有フィールドか

[IsReference](_extension_api_NextDesign.Core_IField_properties_IsReference.md)

参照フィールドか

[LowerBound](_extension_api_NextDesign.Core_IField_properties_LowerBound.md)

多重度（下限）

[OwnerClass](_extension_api_NextDesign.Core_IField_properties_OwnerClass.md)

このフィールドを保持（宣言）しているクラス

[RelationshipClass](_extension_api_NextDesign.Core_IField_properties_RelationshipClass.md)

関連クラス  
フィールドがクラス型の場合のみ取得できます。  
クラス型でないフィールドの場合はnullとなります。

[Type](_extension_api_NextDesign.Core_IField_properties_Type.md)

フィールドの型名  
\- 真偽値型 : "Boolean"  
\- 整数型 : "Integer"  
\- 実数型 : "Double"  
\- 文字列型 : "String"  
\- リッチテキスト型 : "RichText"  
\- 列挙型 : 列挙の名前  
\- クラス型 : クラスの名前

[TypeClass](_extension_api_NextDesign.Core_IField_properties_TypeClass.md)

フィールドの型のクラス  
フィールドがクラス型の場合のみ取得できます。  
クラス型でないフィールドの場合はnullとなります。

[TypeEnum](_extension_api_NextDesign.Core_IField_properties_TypeEnum.md)

フィールドの型の列挙型  
フィールドが列挙型の場合のみ取得できます。  
列挙型でないフィールドの場合はnullとなります。

[UpperBound](_extension_api_NextDesign.Core_IField_properties_UpperBound.md)

多重度（上限）  
制限なしの場合は-1を指定します。