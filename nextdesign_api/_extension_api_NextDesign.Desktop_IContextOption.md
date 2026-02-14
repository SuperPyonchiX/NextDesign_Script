IContextOption インタフェース
======================

名前空間: NextDesign.Desktop

説明​
-----------------------

コンテキストが有効な期間におけるオプション定義情報です。  
各種API呼び出し結果（APIの振る舞い）の変更や、APIの振る舞いに影響する一時的な設定情報を記録します。

所属エリア​
--------------------------------

名前

説明

[グローバル](_extension_api_overview_global.md)

エクステンションの実行環境や実行状態にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[EditorAccessMode](_extension_api_NextDesign.Desktop_IContextOption_properties_EditorAccessMode.md)

コンテキストが有効な期間における、エディタ情報の取得・検索APIの振る舞い  
エディタ情報を取得・検索するAPIの動作を変更できます。

[PlModelAccessMode](_extension_api_NextDesign.Desktop_IContextOption_properties_PlModelAccessMode.md)

コンテキストが有効な期間における、モデル情報の取得・検索APIの振る舞い  
プロダクトラインがサポートされているプロジェクトにおいて、モデル情報を取得・検索する際に、プロダクトを評価するか否かを指定することができます。

[SpecifiedProduct](_extension_api_NextDesign.Desktop_IContextOption_properties_SpecifiedProduct.md)

プロダクト指定  
PlModelAccessMode が SpecifiedProduct の場合に評価対象とするプロダクトを指定します。  
なお、プロダクトに null が指定された場合は、150%モデルが指定されたものとして評価されます。