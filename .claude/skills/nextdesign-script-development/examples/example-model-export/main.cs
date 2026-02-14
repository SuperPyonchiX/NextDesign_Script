using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NextDesign.Core;
using NextDesign.Desktop;

/// <summary>
/// 選択中モデルの子要素をCSVファイルにエクスポートする
/// </summary>
public void ExportToCsv(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null)
        {
            UI.ShowInformationDialog("モデルが選択されていません。", "ModelExporter");
            return;
        }

        // 保存先ファイルパスを選択
        var filePath = UI.ShowSaveFileDialog("CSVファイルの保存先を選択", "CSVファイル|*.csv");
        if (filePath == null) return;

        // 子要素を取得
        var children = model.GetAllChildren();
        var sb = new StringBuilder();
        sb.AppendLine("名前,クラス名,説明");

        var count = 0;
        foreach (var child in children)
        {
            var name = child.GetField("Name") as string ?? "";
            var className = child.ClassName ?? "";
            var description = child.GetField("Description") as string ?? "";

            // CSVフィールドをエスケープ
            sb.AppendLine($"\"{EscapeCsvField(name)}\",\"{EscapeCsvField(className)}\",\"{EscapeCsvField(description)}\"");
            count++;
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

        Output.WriteLine("ModelExporter", $"{count} 件の要素をエクスポートしました: {filePath}");
        UI.ShowInformationDialog($"{count} 件の要素をCSVファイルにエクスポートしました。", "ModelExporter");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}

/// <summary>
/// 選択中モデルの子要素数をクラス別に集計して出力ウィンドウに表示する
/// </summary>
public void ShowSummary(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null)
        {
            UI.ShowInformationDialog("モデルが選択されていません。", "ModelExporter");
            return;
        }

        var children = model.GetAllChildren();
        var classCounts = new Dictionary<string, int>();

        foreach (var child in children)
        {
            var className = child.ClassName ?? "(不明)";
            if (classCounts.ContainsKey(className))
            {
                classCounts[className]++;
            }
            else
            {
                classCounts[className] = 1;
            }
        }

        Output.Clear("ModelExporter");
        Output.WriteLine("ModelExporter", $"モデル「{model.GetField("Name")}」の子要素サマリー:");
        Output.WriteLine("ModelExporter", "--------------------------------------------");

        var total = 0;
        foreach (var pair in classCounts.OrderByDescending(p => p.Value))
        {
            Output.WriteLine("ModelExporter", $"  {pair.Key}: {pair.Value} 件");
            total += pair.Value;
        }

        Output.WriteLine("ModelExporter", "--------------------------------------------");
        Output.WriteLine("ModelExporter", $"  合計: {total} 件");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}

/// <summary>
/// CSVフィールド内のダブルクォートをエスケープする
/// </summary>
private string EscapeCsvField(string value)
{
    if (value == null) return "";
    return value.Replace("\"", "\"\"");
}
