using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace RestAgent
{
    public static partial class SelfTest
    {
        static void ThemeDialog(Controller controller, Action<Window> action)
        {
            var dialog = (Window)Call(controller, "CreateSettingsDialog"); dialog.Opacity = 0;
            Exception failure = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                try { dialog.UpdateLayout(); action(dialog); }
                catch (Exception ex) { failure = ex; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }));
            dialog.ShowDialog(); if (failure != null) throw failure;
        }
        static void PickTheme(Window dialog, string id)
        {
            var choices = (ComboBox)dialog.FindName("ThemeChoices"); choices.SelectedItem = choices.Items.Cast<AppTheme>().First(t => t.Id == id);
        }
        static void ClickSetting(Window dialog, string id) { ((Button)dialog.FindName(id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        static void ThemeColor(Window dialog, string key, string color) { ((TextBox)dialog.FindName("ThemeColor" + key)).Text = color; }
        static Color BrushColor(object brush) { return ((SolidColorBrush)brush).Color; }
        static void TestThemes(Controller controller, Window window, List<string> report)
        {
            var themes = (ThemeService)typeof(Controller).GetField("themes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            string dataPath = (string)typeof(Controller).GetField("dataPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            string path = Path.Combine(Path.GetDirectoryName(dataPath), "themes.json");
            string workspaceBefore = File.ReadAllText(dataPath);
            var original = Data.Clone(themes.Current);
            var tabs = (TabControl)window.FindName("LibraryTabs");
            Check(tabs.Items.Count == 2 && ((TabItem)tabs.Items[0]).Header.ToString() == "История" && ((TabItem)tabs.Items[1]).Header.ToString() == "Коллекция", "Sidebar tabs/order missing");
            foreach (string name in new[] { "HistoryNavigation", "CollectionNavigation" })
            {
                var button = (RadioButton)window.FindName(name); var label = button.Content as StackPanel;
                Check(label != null && label.Children[0] is System.Windows.Shapes.Path, "Tab vector icon missing");
                typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, null);
                Check(button.IsChecked == true && tabs.SelectedIndex == (name == "HistoryNavigation" ? 0 : 1), "Tab switch/selection failed");
                typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, null);
                Check(button.IsChecked == true && tabs.SelectedIndex >= 0, "Active tab click hid sidebar content");
            }
            var workspace = (Workspace)typeof(Controller).GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var historyBefore = workspace.History;
            workspace.History = new List<Request> { new Request { Name = "History alpha", Url = "https://example.com/alpha" }, new Request { Name = "History beta", Url = "https://example.com/beta" } };
            ((TextBox)window.FindName("Search")).Text = "alpha";
            Check(((ListBox)window.FindName("History")).Items.Count == 1, "Shared search did not filter history");
            ((TextBox)window.FindName("Search")).Text = "no-history-match";
            Check(((TextBlock)window.FindName("HistoryEmpty")).Visibility == Visibility.Visible, "History empty-search feedback missing");
            ((TextBox)window.FindName("Search")).Clear(); workspace.History = historyBefore; Call(controller, "RefreshLibrary");
            foreach (string name in new[] { "Import", "Export", "NewRequest", "NewFolder", "Settings" })
            {
                var button = (Button)window.FindName(name); var label = button.Content as StackPanel;
                Check(label != null && label.Children[0] is System.Windows.Shapes.Path && !((TextBlock)label.Children[1]).Text.Contains("+"), "Footer vector icon missing");
            }
            var sidebarColumn = (ColumnDefinition)window.FindName("SidebarColumn");
            sidebarColumn.Width = new GridLength(220); window.UpdateLayout();
            foreach (string name in new[] { "Import", "Export", "NewRequest", "NewFolder", "Settings", "HistoryNavigation", "CollectionNavigation" })
            {
                var control = (Control)window.FindName(name); var position = control.TranslatePoint(new Point(), (Border)window.FindName("Sidebar"));
                Check(position.X >= 0 && position.X + control.ActualWidth <= 221 && control.ActualHeight <= 46, "Sidebar action clipped at minimum width");
            }
            sidebarColumn.Width = new GridLength(280); window.UpdateLayout();
            var tree = (TreeView)window.FindName("Library"); var menu = Nodes(tree.Items).First(i => i.Tag is Request).ContextMenu;
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                Check(item.Icon is System.Windows.Shapes.Path, "Context action vector icon missing");
                foreach (var child in item.Items.OfType<MenuItem>()) Check(child.Icon is System.Windows.Shapes.Path, "Submenu vector icon missing");
            }
            Call(controller, "Load", new Request { Name = "Темы оформления", Method = "POST", Url = "https://example.com/orders", Body = Data.Pretty("{\"name\":\"Привет, світе\",\"enabled\":true,\"quantity\":1.2500}", true), Headers = new List<Pair> { new Pair { Name = "Content-Type", Value = "application/json" } } });
            var body = (CodeEditor)window.FindName("Body"); var response = (CodeEditor)window.FindName("ResponseBody");
            string bodyBefore = body.Text, responseBefore = response.Text; int caret = body.CaretOffset;
            var manager = (ICSharpCode.AvalonEdit.Folding.FoldingManager)typeof(CodeEditor).GetField("folding", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(response);
            int collapsed = manager.AllFoldings.Count(f => f.IsFolded);
            foreach (var template in ThemeService.Templates())
            {
                themes.Preview(template); window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
                Check(BrushColor(window.Background) == (Color)ColorConverter.ConvertFromString(template.Background) && BrushColor(((Border)window.FindName("Sidebar")).Background) == (Color)ColorConverter.ConvertFromString(template.Sidebar), "Theme background did not update");
                Check(BrushColor(body.Background) == (Color)ColorConverter.ConvertFromString(template.Input) && BrushColor(response.Foreground) == (Color)ColorConverter.ConvertFromString(template.Text), "Code editor theme not applied");
                Check(body.Text == bodyBefore && response.Text == responseBefore && body.CaretOffset == caret && manager.AllFoldings.Count(f => f.IsFolded) == collapsed, "Theme changed request/response/folding/caret");
                Check(!((TextBlock)window.FindName("RequestTitle")).Text.EndsWith(" •"), "Theme marked request dirty");
                Check(BrushColor(((Border)window.FindName("ResponseStatusBand")).Background).G > BrushColor(((Border)window.FindName("ResponseStatusBand")).Background).R, "Theme changed semantic success status");
                if (template.Id == "graphite")
                {
                    RenderDialog(window, "theme-graphite-preview.png");
                    double background = ThemeService.Luminance(BrushColor(body.Background));
                    foreach (var ink in body.TextArea.TextView.VisualLines.SelectMany(l => l.Elements).Select(e => e.TextRunProperties.ForegroundBrush as SolidColorBrush).Where(b => b != null))
                    {
                        double foreground = ThemeService.Luminance(ink.Color);
                        Check((Math.Max(background, foreground) + 0.05) / (Math.Min(background, foreground) + 0.05) >= 4.4, "Unreadable dark syntax ink: " + ink.Color);
                    }
                }
                if (template.Id == "ocean") RenderDialog(window, "theme-ocean-preview.png");
            }
            themes.Preview(original);
            ThemeDialog(controller, delegate(Window dialog)
            {
                PickTheme(dialog, "graphite"); RenderDialog(dialog, "settings-themes-preview.png");
                Check(!((Button)dialog.FindName("ThemeDelete")).IsEnabled, "Built-in theme deletion enabled");
                ThemeColor(dialog, "Accent", "#XYZ123"); ClickSetting(dialog, "ApplySettings");
                Check(dialog.IsVisible && !string.IsNullOrEmpty(((TextBlock)dialog.FindName("ThemeError")).Text), "Invalid color accepted");
                ThemeColor(dialog, "Accent", "#237B91"); Check(BrushColor(window.FindResource("AccentBrush")).ToString() == "#FF237B91", "Live color preview failed");
            });
            Check(themes.Current.Id == original.Id && !File.Exists(path), "Cancel saved theme preview");
            ThemeDialog(controller, delegate(Window dialog)
            {
                PickTheme(dialog, "ocean"); ClickSetting(dialog, "ThemeCreate");
                ((TextBox)dialog.FindName("ThemeName")).Text = "Моя проверка";
                ThemeColor(dialog, "Background", "#123456"); ThemeColor(dialog, "Surface", "#192A36"); ThemeColor(dialog, "Input", "#102030"); ThemeColor(dialog, "Text", "#EAF4FF"); ThemeColor(dialog, "Muted", "#A6BACD");
                ClickSetting(dialog, "ThemeSave");
                Check(((ComboBox)dialog.FindName("ThemeChoices")).Items.Cast<AppTheme>().Any(t => t.Name == "Моя проверка"), "Custom theme not added");
                ClickSetting(dialog, "ApplySettings"); Check(dialog.DialogResult == true, "Custom theme apply failed: " + ((TextBlock)dialog.FindName("ThemeError")).Text);
            });
            var saved = Data.Serializer().Deserialize<ThemeStore>(File.ReadAllText(path));
            Check(saved.CustomThemes.Count == 1 && saved.CustomThemes[0].Name == "Моя проверка" && saved.ActiveThemeId == saved.CustomThemes[0].Id, "Custom theme persistence failed");
            Check(BrushColor(window.Background).ToString() == "#FF123456" && body.Text == bodyBefore && response.Text == responseBefore, "Custom theme lost color/text");
            RenderDialog(window, "theme-custom-preview.png");
            Window restored; using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) restored = (Window)XamlReader.Load(stream);
            new Controller(restored, dataPath); Check(BrushColor(restored.Background).ToString() == "#FF123456", "Saved theme not restored on startup"); restored.Close();
            string persisted = File.ReadAllText(path);
            ThemeDialog(controller, delegate(Window dialog) { ClickSetting(dialog, "ThemeDelete"); });
            Check(File.ReadAllText(path) == persisted && themes.Current.Id == saved.ActiveThemeId, "Cancel persisted theme deletion");
            ThemeDialog(controller, delegate(Window dialog) { ClickSetting(dialog, "ThemeDelete"); ClickSetting(dialog, "ApplySettings"); });
            Check(themes.Snapshot().CustomThemes.Count == 0 && themes.Current.Id == "light", "Custom theme deletion failed");
            Check(File.ReadAllText(dataPath) == workspaceBefore, "Theme changes modified collection data");
            Color invalid; Check(!ThemeService.TryColor("#12345", out invalid) && !ThemeService.TryColor("red", out invalid) && !ThemeService.TryColor("#12345678", out invalid), "Malformed color accepted");
            Check(ThemeService.Templates().First(t => t.Id == "ocean").Background == "#EAF2F5", "Editing mutated built-in template");
            themes.Preview(original); window.UpdateLayout(); RenderDialog(window, "sidebar-redesign-preview.png");
            report.Add("PASS themes/sidebar: top icon tabs and repeat selection, five footer icons and menu/submenu vectors, four palettes, live HEX preview/validation, create/save/reload/delete custom theme, cancel rollback, unchanged documents/caret/folds/status/collection");
        }
    }
}
