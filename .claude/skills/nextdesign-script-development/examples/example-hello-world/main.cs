using System;
using NextDesign.Core;
using NextDesign.Desktop;

public void SayHello(ICommandContext context, ICommandParams parameters)
{
    try
    {
        UI.ShowInformationDialog("Hello, Next Design!", "HelloWorld");
        Output.WriteLine("HelloWorld", "Hello Worldダイアログを表示しました。");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
