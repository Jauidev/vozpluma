using System.Windows;

namespace VoiceAgent;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Run(new MainWindow());
    }
}
