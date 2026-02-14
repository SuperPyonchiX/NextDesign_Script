IParagraph インタフェース
==================

名前空間: NextDesign.Desktop.DocumentGenerator.Word

説明​
-----------------------

段落の書式を制御します。

所属エリア​
--------------------------------

名前

説明

[ドキュメント生成](_extension_api_overview_documents.md)

ドキュメント生成にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Alignment](_extension_api_NextDesign.Desktop_IParagraph_properties_Alignment.md)

段落のテキスト配置を取得または設定します。

[CharacterUnitFirstLineIndent](_extension_api_NextDesign.Desktop_IParagraph_properties_CharacterUnitFirstLineIndent.md)

最初の行またはぶら下げインデントの値（文字数）を取得または設定します。  
  
正の値を使用して最初の行のインデントを設定し、負の値を使用してぶら下げインデントを設定します。

[CharacterUnitLeftIndent](_extension_api_NextDesign.Desktop_IParagraph_properties_CharacterUnitLeftIndent.md)

段落の左インデント（文字数）を取得または設定します。

[FirstLineIndent](_extension_api_NextDesign.Desktop_IParagraph_properties_FirstLineIndent.md)

最初の行またはぶら下げインデントの値（ポイント単位）を取得または設定します。  
  
正の値を使用して最初の行のインデントを設定し、負の値を使用してぶら下げインデントを設定します。

[HangingPunctuation](_extension_api_NextDesign.Desktop_IParagraph_properties_HangingPunctuation.md)

現在の段落で句読点が有効になっているかどうかを取得または設定します。

[IsHeading](_extension_api_NextDesign.Desktop_IParagraph_properties_IsHeading.md)

段落スタイルが組み込みの見出しスタイルの1つかどうかを取得します。

[IsListItem](_extension_api_NextDesign.Desktop_IParagraph_properties_IsListItem.md)

段落が箇条書きまたは番号付きリストの項目かどうかを取得します。

[KeepTogether](_extension_api_NextDesign.Desktop_IParagraph_properties_KeepTogether.md)

段落内のすべての行を同じページに残すかどうかを取得または設定します。

[KeepWithNext](_extension_api_NextDesign.Desktop_IParagraph_properties_KeepWithNext.md)

段落を後続の段落と同じページに残すかどうかを取得または設定します。

[LeftIndent](_extension_api_NextDesign.Desktop_IParagraph_properties_LeftIndent.md)

段落の左インデント（ポイント単位）を取得または設定します。

[LineSpacing](_extension_api_NextDesign.Desktop_IParagraph_properties_LineSpacing.md)

段落の行間隔（ポイント単位）を取得または設定します。

[LineUnitAfter](_extension_api_NextDesign.Desktop_IParagraph_properties_LineUnitAfter.md)

段落の後の間隔（グリッド線）の量を取得または設定します。

[LineUnitBefore](_extension_api_NextDesign.Desktop_IParagraph_properties_LineUnitBefore.md)

段落の前の間隔（グリッド線）の量を取得または設定します。

[NoSpaceBetweenParagraphsOfSameStyle](_extension_api_NextDesign.Desktop_IParagraph_properties_NoSpaceBetweenParagraphsOfSameStyle.md)

SpaceBefore と SpaceAfter が同じスタイルの段落間で無視するかどうかを取得または設定します。

[OutlineLevel](_extension_api_NextDesign.Desktop_IParagraph_properties_OutlineLevel.md)

ドキュメント内の段落のアウトラインレベルを取得または設定します。

[PageBreakBefore](_extension_api_NextDesign.Desktop_IParagraph_properties_PageBreakBefore.md)

段落の前に改ページをするかどうかを取得または設定します。

[RightIndent](_extension_api_NextDesign.Desktop_IParagraph_properties_RightIndent.md)

段落の右インデント（ポイント単位）を取得または設定します。

[SpaceAfter](_extension_api_NextDesign.Desktop_IParagraph_properties_SpaceAfter.md)

段落の後の間隔の量（ポイント単位）を取得または設定します。

[SpaceAfterAuto](_extension_api_NextDesign.Desktop_IParagraph_properties_SpaceAfterAuto.md)

段落の後の間隔の量を自動的に設定するかを取得または設定します。

[SpaceBefore](_extension_api_NextDesign.Desktop_IParagraph_properties_SpaceBefore.md)

段落の前の間隔の量（ポイント単位）を取得または設定します。

[StyleName](_extension_api_NextDesign.Desktop_IParagraph_properties_StyleName.md)

段落スタイルの名前を取得または設定します。

[SuppressLineNumbers](_extension_api_NextDesign.Desktop_IParagraph_properties_SuppressLineNumbers.md)

現在の段落の行を、親セクションで適用される行番号から除外するかどうかを取得または設定します。

[WordWrap](_extension_api_NextDesign.Desktop_IParagraph_properties_WordWrap.md)

ラテン語のテキストを単語単位で折り返すかどうかを取得または設定します。  
falseを設定した場合、単語の途中にあるラテン語のテキストを現在の段落で折り返すことができます。

メソッド​
-----------------------------

名前

説明

[ClearFormatting](_extension_api_NextDesign.Desktop_IParagraph_methods_ClearFormatting.md)

デフォルトの段落フォーマットにリセットします。

[RestoreAlignment](_extension_api_NextDesign.Desktop_IParagraph_methods_RestoreAlignment.md)

段落のテキスト配置を復元します。

[SetBodyStyleByOutlineLevel](_extension_api_NextDesign.Desktop_IParagraph_methods_SetBodyStyleByOutlineLevel.md)

アウトラインレベルに応じた本文のスタイルを設定します。

[SetHeadingStyleByOutlineLevel](_extension_api_NextDesign.Desktop_IParagraph_methods_SetHeadingStyleByOutlineLevel.md)

アウトラインレベルに応じた見出しのスタイルを設定します。