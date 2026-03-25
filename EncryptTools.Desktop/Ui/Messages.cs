using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;

namespace EncryptTools.Desktop.Ui;

internal static class Messages
{
    public static async Task ShowAsync(Window owner, string title, string text)
    {
        var w = new Window
        {
            Title = title,
            Width = 440,
            MinHeight = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var ok = new Button { Content = "确定", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => w.Close();
        w.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                ok
            }
        };
        await w.ShowDialog(owner);
    }
}
