using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace RestAgent
{
    // Одна подсказка каталога; этот же формат используется для примеров query-параметров.
    public sealed class HeaderInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Common { get; set; }
    }

    public static class HeaderCatalog
    {
        static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Accept", "Форматы ответа, которые принимает клиент" },
            { "Accept-Encoding", "Допустимые способы сжатия ответа" },
            { "Accept-Language", "Предпочтительные языки ответа" },
            { "Authorization", "Авторизация: Bearer token, Basic и другие схемы" },
            { "Content-Type", "Формат тела запроса, например application/json" },
            { "Content-Encoding", "Способ сжатия тела запроса" },
            { "Content-Language", "Язык содержимого" },
            { "Content-Length", "Размер тела в байтах; обычно рассчитывается автоматически" },
            { "Content-Disposition", "Название и способ передачи содержимого" },
            { "Cache-Control", "Правила кэширования" },
            { "Cookie", "Cookies запроса: name=value; name2=value2" },
            { "User-Agent", "Идентификатор клиента" },
            { "Host", "Имя сервера; обычно определяется из URL" },
            { "Origin", "Источник запроса" }, { "Referer", "Адрес страницы, отправившей запрос" },
            { "If-Match", "Выполнить запрос при совпадении ETag" },
            { "If-None-Match", "Выполнить запрос, если ETag изменился" },
            { "If-Modified-Since", "Проверить изменение после указанной даты" },
            { "If-Unmodified-Since", "Выполнить, если ресурс не менялся после даты" },
            { "If-Range", "Условное получение диапазона" },
            { "Range", "Диапазон байтов, например bytes=0-1023" },
            { "Prefer", "Предпочтения клиента при обработке запроса" },
            { "Pragma", "Совместимость с правилами кэширования HTTP/1.0" },
            { "Connection", "Параметры соединения" },
            { "Expect", "Ожидания клиента, например 100-continue" },
            { "Forwarded", "Сведения об исходном клиенте и прокси" },
            { "X-API-Key", "Ключ API; имя зависит от сервиса" },
            { "X-Auth-Token", "Токен авторизации сервиса" },
            { "X-Request-ID", "Идентификатор запроса для отслеживания" },
            { "X-Correlation-ID", "Идентификатор цепочки запросов" },
            { "X-CSRF-Token", "Токен защиты от CSRF" },
            { "X-XSRF-Token", "Токен защиты от XSRF" },
            { "X-Requested-With", "Тип клиента, например XMLHttpRequest" },
            { "X-Forwarded-For", "Исходный IP клиента за прокси" },
            { "X-Forwarded-Host", "Исходное имя сервера за прокси" },
            { "X-Forwarded-Proto", "Исходный протокол за прокси" },
            { "Idempotency-Key", "Ключ предотвращения повторного выполнения операции" },
            { "traceparent", "Контекст распределённой трассировки" },
            { "tracestate", "Дополнительные данные трассировки" },
            { "baggage", "Данные контекста распределённой трассировки" },
            { "Access-Control-Request-Method", "Метод предварительного CORS-запроса" },
            { "Access-Control-Request-Headers", "Имена заголовков предварительного CORS-запроса" }
        };
        static readonly string[] popular = { "Accept", "Authorization", "Content-Type", "X-API-Key", "Cookie", "User-Agent", "Accept-Language", "Accept-Encoding", "Cache-Control", "Origin", "Referer", "Range", "If-Match", "If-None-Match", "If-Modified-Since", "If-Unmodified-Since", "If-Range", "Content-Encoding", "Content-Language", "Content-Length", "Content-Disposition", "Host", "Connection", "Expect", "Pragma", "Prefer", "Forwarded", "X-Auth-Token", "X-Request-ID", "X-Correlation-ID", "X-CSRF-Token", "X-XSRF-Token", "X-Requested-With", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Idempotency-Key", "traceparent", "tracestate", "baggage", "Access-Control-Request-Method", "Access-Control-Request-Headers" };
        public static readonly HeaderInfo[] Entries = Load();
        public static string[] Names { get { return Entries.Select(h => h.Name).ToArray(); } }
        // Снимок реестра встроен в EXE и доступен без Интернета. Дополняем его частыми
        // нестандартными именами; словарь без учёта регистра убирает дубликаты заголовков.
        static HeaderInfo[] Load()
        {
            var entries = new Dictionary<string, HeaderInfo>(StringComparer.OrdinalIgnoreCase);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HttpFieldRegistry.csv"))
            using (var reader = new StreamReader(stream))
            {
                reader.ReadLine(); string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var fields = line.Split(','); if (fields.Length < 2 || !IsValidName(fields[0])) continue;
                    string name = fields[0], description;
                    if (!descriptions.TryGetValue(name, out description)) description = "Реестр IANA · " + (fields[1] == "permanent" ? "зарегистрированный" : fields[1] == "provisional" ? "предварительная регистрация" : fields[1] == "deprecated" ? "устаревший" : "исторический");
                    entries[name] = new HeaderInfo { Name = name, Description = description, Common = popular.Contains(name, StringComparer.OrdinalIgnoreCase) };
                }
            }
            foreach (var name in popular) if (!entries.ContainsKey(name)) entries[name] = new HeaderInfo { Name = name, Description = descriptions[name], Common = true };
            return entries.Values.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        // Имя HTTP-заголовка — непустой ASCII token; пробелы и переносы строк недопустимы.
        public static bool IsValidName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0);
        }
        public static string[] Values(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "accept": return new[] { "application/json", "application/xml", "text/plain", "text/html", "*/*" };
                case "content-type": return new[] { "application/json", "application/json; charset=utf-8", "application/x-www-form-urlencoded", "application/xml", "text/plain", "text/plain; charset=utf-8", "text/html", "application/octet-stream" };
                case "accept-encoding": return new[] { "gzip, deflate", "gzip", "deflate", "identity" };
                case "accept-language": return new[] { "uk-UA,uk;q=0.9,en;q=0.8", "en-US,en;q=0.9", "ru-RU,ru;q=0.9" };
                case "cache-control": return new[] { "no-cache", "no-store", "max-age=0", "max-age=3600" };
                case "connection": return new[] { "keep-alive", "close" };
                case "expect": return new[] { "100-continue" };
                case "pragma": return new[] { "no-cache" };
                case "range": return new[] { "bytes=0-1023", "bytes=0-", "bytes=-1024" };
                case "prefer": return new[] { "return=representation", "return=minimal", "respond-async" };
                case "x-requested-with": return new[] { "XMLHttpRequest" };
                default: return new string[0];
            }
        }
        // Общий диалог для заголовка и параметра. existing задаёт режим редактирования;
        // возвращаем новую строку только после подтверждения, null означает отмену.
        public static Pair Pick(Window owner, IEnumerable<string> customNames, Pair existing = null, bool query = false)
        {
            Pair result = null;
            var catalog = query ? ParameterCatalog.Entries : Entries;
            string prefix = query ? "Query" : "Header";
            var dialog = new Window { Title = query ? "Параметр адреса" : existing == null ? "Добавить заголовок" : "Выбрать заголовок", Owner = owner, Width = query ? 660 : 700, Height = query ? 560 : 680, MinWidth = 560, MinHeight = query ? 460 : 580, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background, Foreground = owner.Foreground, FontFamily = owner.FontFamily, FontSize = owner.FontSize, Resources = owner.Resources, Icon = owner.Icon, ShowInTaskbar = false };
            NameScope.SetNameScope(dialog, new NameScope());
            var panel = new Grid { Margin = new Thickness(24) }; dialog.Content = panel;
            for (int i = 0; i < 6; i++) panel.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            heading.Children.Add(new TextBlock { Text = query ? "Параметры адреса" : "Каталог заголовков", FontSize = 22, FontWeight = FontWeights.SemiBold });
            heading.Children.Add(new TextBlock { Text = query ? "Имена и значения зависят от API · параметры добавляются в URL" : "Поиск по имени и описанию · свой заголовок можно ввести ниже", Foreground = (Brush)owner.FindResource("MutedBrush"), FontSize = 11, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap }); panel.Children.Add(heading);
            var searchArea = new Grid { Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(searchArea, 1); panel.Children.Add(searchArea);
            var search = new TextBox { Name = prefix + "Search", ToolTip = "Поиск по имени или описанию", Style = (Style)owner.FindResource("EditorInput") }; dialog.RegisterName(search.Name, search); searchArea.Children.Add(search);
            var searchHint = new TextBlock { Text = query ? "Найти параметр…" : "Найти заголовок…", Foreground = (Brush)owner.FindResource("MutedBrush"), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; searchArea.Children.Add(searchHint);
            var categories = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(categories, 2); panel.Children.Add(categories);
            var common = new Button { Content = "Частые", Padding = new Thickness(10, 5, 10, 5) }; var all = new Button { Name = query ? "AllQueries" : "AllHeaders", Content = "Все · " + catalog.Length, Padding = common.Padding }; dialog.RegisterName(all.Name, all);
            var mine = new Button { Content = "Свои", Padding = common.Padding }; categories.Children.Add(common); categories.Children.Add(all); categories.Children.Add(mine);
            var list = new ListBox { Name = prefix + "Options", BorderThickness = new Thickness(0), Background = (Brush)owner.FindResource("SurfaceBrush"), HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemContainerStyle = (Style)owner.FindResource("CatalogItem") }; dialog.RegisterName(list.Name, list); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel><TextBlock Text='{Binding Name}' FontWeight='SemiBold' FontSize='13'/><TextBlock Text='{Binding Description}' Foreground='{DynamicResource MutedBrush}' FontSize='11' TextTrimming='CharacterEllipsis' Margin='0,3,0,0'/></StackPanel></DataTemplate>");
            var listArea = new Grid(); listArea.Children.Add(list); var empty = new TextBlock { Text = query ? "Не найдено. Введите своё имя параметра ниже." : "Не найдено. Введите своё имя заголовка ниже.", Foreground = (Brush)owner.FindResource("MutedBrush"), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false }; listArea.Children.Add(empty); Grid.SetRow(listArea, 3); panel.Children.Add(listArea);
            var form = new Grid { Margin = new Thickness(0, 16, 0, 16) }; form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(form, 4); panel.Children.Add(form);
            form.Children.Add(new TextBlock { Text = query ? "Имя параметра" : "Имя заголовка", Margin = new Thickness(0, 0, 0, 8) }); var valueLabel = new TextBlock { Text = "Значение", Margin = new Thickness(12, 0, 0, 8) }; Grid.SetColumn(valueLabel, 1); form.Children.Add(valueLabel);
            var nameInput = new TextBox { Name = prefix + "Name", Text = existing == null ? "" : existing.Name, Style = (Style)owner.FindResource("EditorInput") }; dialog.RegisterName(nameInput.Name, nameInput); Grid.SetRow(nameInput, 1); form.Children.Add(nameInput);
            var valueInput = new ComboBox { Name = prefix + "Value", IsEditable = true, IsTextSearchEnabled = false, Text = existing == null ? "" : existing.Value, Margin = new Thickness(12, 0, 0, 0), Style = (Style)owner.FindResource("ValueInput") }; dialog.RegisterName(valueInput.Name, valueInput); Grid.SetRow(valueInput, 1); Grid.SetColumn(valueInput, 1); form.Children.Add(valueInput);
            var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) }; Grid.SetRow(error, 2); Grid.SetColumnSpan(error, 2); form.Children.Add(error);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(footer, 5); panel.Children.Add(footer);
            var ok = new Button { Name = prefix + "Confirm", Content = existing == null ? "Добавить" : "Применить", IsDefault = true, Background = (Brush)owner.FindResource("AccentBrush"), Foreground = (Brush)owner.FindResource("AccentTextBrush") }; dialog.RegisterName(ok.Name, ok); footer.Children.Add(ok); footer.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0) });
            var custom = customNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(query ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).Select(n => new HeaderInfo { Name = n, Description = "Имя из ваших запросов" }).OrderBy(h => h.Name).ToArray();
            string category = "common";
            Action refresh = delegate
            {
                string term = search.Text.Trim();
                IEnumerable<HeaderInfo> source = category == "mine" ? custom : category == "all" || term.Length > 0 ? catalog.Concat(custom.Where(c => !catalog.Any(e => e.Name.Equals(c.Name, StringComparison.Ordinal)))) : catalog.Where(h => h.Common);
                var options = source.Where(h => (h.Name + " " + h.Description).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                list.ItemsSource = options; empty.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var button in new[] { common, all, mine }) { bool selected = button == common && category == "common" || button == all && category == "all" || button == mine && category == "mine"; button.Background = (Brush)owner.FindResource(selected ? "AccentBrush" : "ButtonBrush"); button.Foreground = (Brush)owner.FindResource(selected ? "AccentTextBrush" : "TextBrush"); }
            };
            all.ToolTip = query ? "Распространённые примеры; точные имена уточняйте в документации API" : "Реестр IANA: поля запросов и ответов, включая устаревшие";
            common.Click += delegate { category = "common"; refresh(); }; all.Click += delegate { category = "all"; refresh(); }; mine.Click += delegate { category = "mine"; refresh(); }; search.TextChanged += delegate { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; refresh(); };
            nameInput.TextChanged += delegate { string text = valueInput.Text; valueInput.ItemsSource = query ? new string[0] : Values(nameInput.Text); valueInput.Text = text; error.Text = ""; };
            list.SelectionChanged += delegate { var info = list.SelectedItem as HeaderInfo; if (info != null) { nameInput.Text = info.Name; list.ScrollIntoView(info); } };
            list.MouseDoubleClick += delegate { if (list.SelectedItem != null) valueInput.Focus(); };
            ok.Click += delegate
            {
                string name = nameInput.Text.Trim();
                if (query ? string.IsNullOrWhiteSpace(name) : !IsValidName(name)) { error.Text = query ? "Введите имя параметра." : "Введите имя без пробелов, двоеточия и переносов строк."; nameInput.Focus(); return; }
                result = new Pair { Name = name, Value = valueInput.Text, Enabled = existing == null || existing.Enabled }; dialog.DialogResult = true;
            };
            valueInput.ItemsSource = query ? new string[0] : Values(nameInput.Text); refresh(); dialog.Loaded += delegate { search.Focus(); };
            return dialog.ShowDialog() == true ? result : null;
        }
    }
}
