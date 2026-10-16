using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace RestAgent
{
    // Диалог работает с копиями палитр: выбор и ввод цвета дают предпросмотр,
    // а постоянная запись выполняется только кнопкой «Применить».
    public sealed partial class Controller
    {
        static T SettingsControl<T>(Window dialog, string name, T control) where T : FrameworkElement
        { control.Name = name; dialog.RegisterName(name, control); return control; }
        Window CreateSettingsDialog()
        {
            // Снимок нужен для отката оформления при отмене или закрытии крестиком.
            var before = Data.Clone(themes.Current);
            var working = new ObservableCollection<AppTheme>(themes.AllThemes());
            bool loading = false, committed = false;
            var dialog = new Window { Title = "Настройки", Owner = window, Width = 800, Height = 690, MinWidth = 700, MinHeight = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, Resources = window.Resources, FontFamily = window.FontFamily, FontSize = window.FontSize, Icon = window.Icon, ShowInTaskbar = false };
            System.Windows.NameScope.SetNameScope(dialog, new NameScope());
            dialog.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AppBackgroundBrush"); dialog.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush");
            var root = new Grid { Margin = new Thickness(22) }; dialog.Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "Настройки", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
            var tabs = new TabControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch }; Grid.SetRow(tabs, 1); root.Children.Add(tabs);
            var appearance = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; tabs.Items.Add(new TabItem { Header = "Оформление", Content = appearance });
            var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); appearance.Children.Add(heading);
            var description = new TextBlock { Text = "Выберите готовую тему или создайте свою. Цвета — #RRGGBB.", FontSize = 12, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap }; description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); heading.Children.Add(description);
            var choiceRow = new Grid { Margin = new Thickness(0, 0, 0, 10) }; choiceRow.ColumnDefinitions.Add(new ColumnDefinition()); choiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); heading.Children.Add(choiceRow);
            var choices = SettingsControl(dialog, "ThemeChoices", new ComboBox { Style = (Style)window.FindResource("ValueInput"), IsEditable = true, IsReadOnly = true, ItemsSource = working, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 10, 0) }); choiceRow.Children.Add(choices);
            var create = SettingsControl(dialog, "ThemeCreate", new Button { Content = UiIcons.Label(window, "Copy", "Создать свою"), Margin = new Thickness(0) }); Grid.SetColumn(create, 1); choiceRow.Children.Add(create);
            var nameCaption = new TextBlock { Text = "Название темы", FontSize = 11, Margin = new Thickness(0, 0, 0, 5) }; nameCaption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); heading.Children.Add(nameCaption);
            var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 12) }; nameRow.ColumnDefinitions.Add(new ColumnDefinition()); nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); heading.Children.Add(nameRow);
            var name = SettingsControl(dialog, "ThemeName", new TextBox { Style = (Style)window.FindResource("EditorInput"), MaxLength = 80, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Название своей темы" }); nameRow.Children.Add(name);
            var saveTheme = SettingsControl(dialog, "ThemeSave", new Button { Content = "Сохранить тему" }); Grid.SetColumn(saveTheme, 1); nameRow.Children.Add(saveTheme);
            var delete = SettingsControl(dialog, "ThemeDelete", new Button { Content = UiIcons.Create(window, "Delete"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0), ToolTip = "Удалить свою тему" }); delete.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "DangerBrush"); Grid.SetColumn(delete, 2); nameRow.Children.Add(delete);
            var hint = new TextBlock { Text = "Изменения тем сохранятся после нажатия «Применить».", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); DockPanel.SetDock(hint, Dock.Bottom); appearance.Children.Add(hint);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; appearance.Children.Add(scroll);
            var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition()); scroll.Content = fields;
            var inputs = new TextBox[ThemeService.ColorKeys.Length]; var swatches = new Border[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                if (i % 2 == 0) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var row = new Grid { Margin = new Thickness(0, 0, i % 2 == 0 ? 18 : 0, 12) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); Grid.SetColumn(row, i % 2); Grid.SetRow(row, i / 2); fields.Children.Add(row);
                row.Children.Add(new TextBlock { Text = ThemeService.ColorLabels[i], FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
                inputs[i] = SettingsControl(dialog, "ThemeColor" + ThemeService.ColorKeys[i], new TextBox { Style = (Style)window.FindResource("EditorInput"), MaxLength = 7, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(5, 4, 5, 4), ToolTip = ThemeService.ColorLabels[i] + " — #RRGGBB" }); Grid.SetColumn(inputs[i], 1); row.Children.Add(inputs[i]);
                swatches[i] = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right }; swatches[i].SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); Grid.SetColumn(swatches[i], 2); row.Children.Add(swatches[i]);
            }
            var general = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; tabs.Items.Add(new TabItem { Header = "Общие", Content = general });
            bool startupBefore;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startupBefore = key != null && key.GetValue("RestAgentPortable") != null;
            var startup = new CheckBox { Content = "Запускать при входе в Windows", IsChecked = startupBefore, Margin = new Thickness(0, 0, 0, 18) }; general.Children.Add(startup);
            var location = new TextBlock { Text = "Данные: " + dataPath + "\nТемы сохраняются рядом с коллекцией в themes.json.\nПосле переноса приложения включите автозапуск заново.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; location.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); general.Children.Add(location);
            var error = SettingsControl(dialog, "ThemeError", new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), FontSize = 12 }); error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush"); Grid.SetRow(error, 2); root.Children.Add(error);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(footer, 3); root.Children.Add(footer);
            var apply = SettingsControl(dialog, "ApplySettings", new Button { Content = "Применить" }); apply.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush"); apply.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentTextBrush"); footer.Children.Add(apply);
            footer.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0) });

            Func<string, string> uniqueName = basis => { string candidate = basis; int suffix = 2; while (working.Any(t => string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = basis + " " + suffix++; return candidate; };
            // Собираем и проверяем черновик из полей, не изменяя выбранный шаблон.
            Func<AppTheme> readDraft = delegate
            {
                var selected = choices.SelectedItem as AppTheme;
                if (selected == null) throw new ArgumentException("Выберите тему.");
                var draft = Data.Clone(selected); draft.Name = name.Text.Trim();
                for (int i = 0; i < inputs.Length; i++) typeof(AppTheme).GetProperty(ThemeService.ColorKeys[i]).SetValue(draft, inputs[i].Text.Trim().ToUpperInvariant(), null);
                ThemeService.ValidateTheme(draft); return draft;
            };
            Action loadSelection = delegate
            {
                var selected = choices.SelectedItem as AppTheme; if (selected == null) return;
                loading = true;
                name.Text = selected.Name;
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].Text = (string)typeof(AppTheme).GetProperty(ThemeService.ColorKeys[i]).GetValue(selected, null);
                    swatches[i].Background = (Brush)new BrushConverter().ConvertFromString(inputs[i].Text); inputs[i].SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
                }
                delete.IsEnabled = selected.Id.StartsWith("custom-", StringComparison.Ordinal); error.Text = ""; loading = false; themes.Preview(selected);
            };
            Action preview = delegate
            {
                if (loading) return;
                bool valid = true;
                for (int i = 0; i < inputs.Length; i++)
                {
                    Color color;
                    if (ThemeService.TryColor(inputs[i].Text, out color)) { swatches[i].Background = new SolidColorBrush(color); inputs[i].SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush"); }
                    else { valid = false; inputs[i].SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "DangerBrush"); }
                }
                if (!valid) { error.Text = "Цвет должен содержать # и 6 шестнадцатеричных цифр, например #237B91."; return; }
                try { var draft = readDraft(); themes.Preview(draft); error.Text = ""; } catch (ArgumentException ex) { error.Text = ex.Message; }
            };
            // «Сохранить тему» обновляет рабочий список диалога. Для готового шаблона
            // создаём пользовательскую копию с новым ID, сохраняя сам шаблон неизменным.
            Func<bool> saveDraft = delegate
            {
                try
                {
                    var draft = readDraft(); var selected = (AppTheme)choices.SelectedItem;
                    if (working.Any(t => t.Id != selected.Id && string.Equals(t.Name, draft.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Тема с таким названием уже существует.");
                    if (!selected.Id.StartsWith("custom-", StringComparison.Ordinal))
                    {
                        if (string.Equals(draft.Name, selected.Name, StringComparison.OrdinalIgnoreCase)) draft.Name = uniqueName(selected.Name + " — моя");
                        draft.Id = "custom-" + Guid.NewGuid().ToString("N"); working.Add(draft);
                    }
                    else { int index = working.IndexOf(selected); loading = true; working[index] = draft; loading = false; }
                    choices.SelectedItem = draft; loadSelection(); return true;
                }
                catch (ArgumentException ex) { error.Text = ex.Message; return false; }
            };
            choices.SelectionChanged += delegate { if (!loading) loadSelection(); };
            for (int i = 0; i < inputs.Length; i++) inputs[i].TextChanged += delegate
            {
                if (!loading)
                {
                    var selected = choices.SelectedItem as AppTheme;
                    if (selected != null && !selected.Id.StartsWith("custom-", StringComparison.Ordinal) && name.Text == selected.Name) { loading = true; name.Text = uniqueName(selected.Name + " — моя"); loading = false; }
                }
                preview();
            };
            name.TextChanged += delegate { preview(); };
            create.Click += delegate
            {
                var selected = choices.SelectedItem as AppTheme; if (selected == null) return;
                var custom = Data.Clone(selected); custom.Id = "custom-" + Guid.NewGuid().ToString("N"); custom.Name = uniqueName("Моя тема"); working.Add(custom); choices.SelectedItem = custom; name.Focus(); name.SelectAll();
            };
            saveTheme.Click += delegate { saveDraft(); };
            delete.Click += delegate
            {
                var selected = choices.SelectedItem as AppTheme; if (selected == null || !selected.Id.StartsWith("custom-", StringComparison.Ordinal)) return;
                loading = true; working.Remove(selected); loading = false; choices.SelectedItem = working[0]; loadSelection();
            };
            apply.Click += delegate
            {
                try
                {
                    var draft = readDraft(); var selected = (AppTheme)choices.SelectedItem;
                    bool changed = draft.Name != selected.Name || ThemeService.ColorKeys.Any(k => !string.Equals((string)typeof(AppTheme).GetProperty(k).GetValue(draft, null), (string)typeof(AppTheme).GetProperty(k).GetValue(selected, null), StringComparison.OrdinalIgnoreCase));
                    if (changed && !saveDraft()) return;
                    selected = (AppTheme)choices.SelectedItem;
                    themes.Commit(new ThemeStore { ActiveThemeId = selected.Id, CustomThemes = working.Where(t => t.Id.StartsWith("custom-", StringComparison.Ordinal)).ToList() });
                    // Реестр автозапуска меняем только при подтверждении настроек
                    // и только если пользователь действительно переключил этот флажок.
                    if ((startup.IsChecked == true) != startupBefore)
                        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                        { if (startup.IsChecked == true) key.SetValue("RestAgentPortable", "\"" + Assembly.GetExecutingAssembly().Location + "\""); else key.DeleteValue("RestAgentPortable", false); }
                    committed = true; dialog.DialogResult = true; Notice("Настройки сохранены");
                }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            dialog.Closed += delegate { if (!committed) themes.Preview(before); };
            choices.SelectedItem = working.FirstOrDefault(t => t.Id == before.Id) ?? working[0];
            return dialog;
        }
    }
}
