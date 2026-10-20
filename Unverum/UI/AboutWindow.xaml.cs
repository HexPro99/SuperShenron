using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Unverum
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            try
            {
                VersionText.Text = $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}";
            }
            catch (Exception)
            {
                VersionText.Text = String.Empty;
            }
        }
        private void Link_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (String.IsNullOrEmpty(url))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't open {url} ({ex.Message})", LoggerType.Error);
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
