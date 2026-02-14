IResourceUtility インタフェース
========================

名前空間: NextDesign.Desktop

説明​
-----------------------

リソース操作を提供します。

所属エリア​
--------------------------------

名前

説明

[ユーティリティ](_extension_api_overview_utility.md)

汎用API群です。

メソッド​
-----------------------------

名前

説明

[GetIcon](_extension_api_NextDesign.Desktop_IResourceUtility_methods_GetIcon.md)

指定されたパスのアイコンを取得します

[GetObjectIcon](_extension_api_NextDesign.Desktop_IResourceUtility_methods_GetObjectIcon.md)

指定されたオブジェクトのアイコンを取得します。  
指定されたオブジェクトの種類により以下のアイコンを取得できます。  
\- IClass（および、その派生型） : プロファイルで定義したメタクラスのアイコン  
\- IField : フィールドの種類（プロパティ/リファレンス）のアイコン  
\- IEnum : 列挙のアイコン  
\- IEnumLiteral : プロファイルで定義した列挙リテラルのアイコン  
\- IEditorDef（および、その派生型） : エディタ定義の種類に対応する組込みアイコン  
\- IElementDef（および、その派生型） : エディタ要素定義が対応するメタクラスのアイコン  
\- IModel（および、その派生型） : モデルの型（メタクラス）のアイコン  
\- IRepresentation（および、その派生型） : ビュー要素が対応するモデルの型（メタクラス）のアイコン  
上記のいずれにも該当しない型が指定された場合は、null を返します。