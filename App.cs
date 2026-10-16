using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace RestAgent
{
    // Строка таблицы заголовков или параметров. Уведомления нужны и привязкам WPF,
    // и контроллеру: правка ячейки должна обновлять URL и признак несохранённых изменений.
    public class Pair : System.ComponentModel.INotifyPropertyChanged
    {
        bool enabled; string name; string value; bool changingSuggestions;
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public bool Enabled { get { return enabled; } set { if (enabled != value) { enabled = value; Changed("Enabled"); } } }
        // Обновление подсказок ComboBox может временно записать новое значение обратно.
        // Флаг сохраняет введённый пользователем текст при смене имени заголовка.
        public string Name { get { return name; } set { if (name != value) { name = value; Changed("Name"); changingSuggestions = true; try { Changed("ValueSuggestions"); } finally { changingSuggestions = false; } Changed("Value"); } } }
        public string Value { get { return this.value; } set { if (!changingSuggestions && this.value != value) { this.value = value; Changed("Value"); } } }
        void Changed(string property) { var handler = PropertyChanged; if (handler != null) handler(this, new System.ComponentModel.PropertyChangedEventArgs(property)); }
        public static readonly string[] KnownHeaders = HeaderCatalog.Names;
        public static readonly string[] KnownQueries = ParameterCatalog.Entries.Select(p => p.Name).ToArray();
        [ScriptIgnore] public string[] HeaderNames { get { return KnownHeaders; } }
        [ScriptIgnore] public string[] QueryNames { get { return KnownQueries; } }
        [ScriptIgnore] public string[] ValueSuggestions { get { return HeaderCatalog.Values(Name); } }
        public Pair() { Enabled = true; Name = ""; Value = ""; }
    }

    // Сохраняемая модель запроса; коллекция и история содержат её независимые копии.
    public class Request
    {
        public string Id { get; set; }
        public string FolderId { get; set; }
        public string Name { get; set; }
        public string Method { get; set; }
        public string Url { get; set; }
        public string Body { get; set; }
        public List<Pair> Headers { get; set; }
        public List<Pair> Queries { get; set; }
        public int Timeout { get; set; }
        public Request() { Id = Guid.NewGuid().ToString(); Name = "Новый запрос"; Method = "GET"; Url = "https://httpbin.org/get"; Body = ""; Headers = new List<Pair>(); Queries = new List<Pair>(); Timeout = 30; }
        public override string ToString() { return Method + "  " + Name; }
    }

    // Иерархия хранится плоским списком: ParentId ссылается на родителя, null означает корень.
    public class Folder
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Name { get; set; }
    }

    // Формат workspace.json. Версия проверяется при чтении, записи и импорте.
    public class Workspace
    {
        public int Version { get; set; }
        public List<Folder> Folders { get; set; }
        public List<Request> Requests { get; set; }
        public List<Request> History { get; set; }
        public Workspace() { Version = 1; Folders = new List<Folder>(); Requests = new List<Request>(); History = new List<Request>(); }
    }

    // Работа с форматом коллекций и URL без зависимости от элементов интерфейса.
    public static class Data
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 }; }
        // Глубокая копия позволяет сначала подготовить изменения, а затем сохранить их целиком.
        public static T Clone<T>(T value) { return Serializer().Deserialize<T>(Serializer().Serialize(value)); }
        // Форматируем исходный JSON посимвольно после проверки его корректности.
        // Это сохраняет запись чисел (например, 1.2500); readableUnicode включается для показа,
        // а при хранении коллекций действует прежнее экранирование сериализатора.
        public static string Pretty(string json, bool readableUnicode = false)
        {
            Serializer().DeserializeObject(json);
            var output = new StringBuilder();
            int level = 0; bool quoted = false; bool escaped = false;
            for (int index = 0; index < json.Length; index++)
            {
                char c = json[index];
                if (quoted)
                {
                    char unicode;
                    if (readableUnicode && !escaped && c == '\\' && ReadUnicodeEscape(json, index, out unicode))
                    {
                        char low;
                        if (char.IsHighSurrogate(unicode) && ReadUnicodeEscape(json, index + 6, out low) && char.IsLowSurrogate(low))
                        {
                            output.Append(unicode); output.Append(low); index += 11; continue;
                        }
                        // Кавычки, обратный слеш, управляющие символы и непарные суррогаты
                        // остаются экранированными, чтобы результат был корректным JSON.
                        // Текст с буквальным \\uXXXX не декодируется повторно.
                        if (!char.IsSurrogate(unicode) && !char.IsControl(unicode) && unicode != '"' && unicode != '\\' && unicode != '\u2028' && unicode != '\u2029')
                        {
                            output.Append(unicode); index += 5; continue;
                        }
                    }
                    output.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; output.Append(c); }
                else if (c == '{' || c == '[') { output.Append(c); output.AppendLine(); output.Append(' ', ++level * 2); }
                else if (c == '}' || c == ']') { output.AppendLine(); output.Append(' ', --level * 2); output.Append(c); }
                else if (c == ',') { output.Append(c); output.AppendLine(); output.Append(' ', level * 2); }
                else if (c == ':') output.Append(": ");
                else if (!char.IsWhiteSpace(c)) output.Append(c);
            }
            return output.ToString();
        }
        static bool ReadUnicodeEscape(string text, int index, out char value)
        {
            value = '\0';
            if (index + 5 >= text.Length || text[index] != '\\' || text[index + 1] != 'u') return false;
            int number = 0;
            for (int digit = index + 2; digit < index + 6; digit++)
            {
                char c = text[digit];
                int hex = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (hex < 0) return false;
                number = number * 16 + hex;
            }
            value = (char)number; return true;
        }
        // Проверяем не только поля, но и связи: дубли ID, отсутствующих родителей и циклы папок.
        // Некорректный импорт должен быть отклонён до изменения рабочей коллекции.
        public static void Validate(Workspace workspace)
        {
            if (workspace == null || workspace.Version != 1 || workspace.Folders == null || workspace.Requests == null || workspace.History == null)
                throw new InvalidDataException("Неподдерживаемый формат коллекции.");
            var folders = new Dictionary<string, Folder>();
            foreach (var folder in workspace.Folders)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.Id) || string.IsNullOrWhiteSpace(folder.Name) || folders.ContainsKey(folder.Id))
                    throw new InvalidDataException("Некорректная папка или повторяющийся идентификатор.");
                folders.Add(folder.Id, folder);
            }
            foreach (var folder in workspace.Folders)
            {
                var seen = new HashSet<string>(); var current = folder;
                while (current != null)
                {
                    if (!seen.Add(current.Id)) throw new InvalidDataException("В иерархии папок обнаружен цикл.");
                    if (string.IsNullOrEmpty(current.ParentId)) break;
                    if (!folders.TryGetValue(current.ParentId, out current)) throw new InvalidDataException("Родительская папка не найдена.");
                }
            }
            var ids = new HashSet<string>();
            foreach (var request in workspace.Requests.Concat(workspace.History))
            {
                if (request == null || request.Headers == null || request.Queries == null || request.Headers.Concat(request.Queries).Any(p => p == null))
                    throw new InvalidDataException("Некорректный запрос.");
                if (request.Timeout < 1 || request.Timeout > 600 || !new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }.Contains(request.Method))
                    throw new InvalidDataException("Некорректный метод или таймаут.");
            }
            foreach (var request in workspace.Requests)
            {
                if (string.IsNullOrWhiteSpace(request.Id) || !ids.Add(request.Id) || string.IsNullOrWhiteSpace(request.Name)) throw new InvalidDataException("Некорректный идентификатор или имя запроса.");
                if (!string.IsNullOrEmpty(request.FolderId) && !folders.ContainsKey(request.FolderId)) throw new InvalidDataException("Папка запроса не найдена.");
            }
        }
        // Сначала пишем временный файл. Замена готовым файлом сохраняет предыдущую версию в .bak.
        public static void Write(string path, Workspace workspace)
        {
            Validate(workspace);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Pretty(Serializer().Serialize(workspace)), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        public static Workspace Read(string path)
        {
            var workspace = Serializer().Deserialize<Workspace>(File.ReadAllText(path, Encoding.UTF8));
            Validate(workspace); return workspace;
        }
        public static Uri BuildUri(Request request)
        {
            Uri uri;
            if (!Uri.TryCreate((request.Url ?? "").Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("Укажите полный HTTP или HTTPS URL.");
            var builder = new UriBuilder(uri);
            // Таблица определяет значения своих имён, в том числе выключенных строк:
            // одноимённые поля исходного URL удаляем, чтобы не отправить старое значение.
            // Повторяющиеся строки таблицы сохраняем: они нужны для массивов и фильтров.
            var names = new HashSet<string>(request.Queries.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name.Trim()), StringComparer.Ordinal);
            var fields = uri.Query.TrimStart('?').Split('&').Where(p => p.Length > 0 && !names.Contains(DecodeQuery(p.Split('=')[0]))).ToList();
            fields.AddRange(request.Queries.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Name)).Select(p => Uri.EscapeDataString(p.Name.Trim()) + "=" + Uri.EscapeDataString(p.Value ?? "")));
            builder.Query = string.Join("&", fields); return builder.Uri;
        }
        // В query символ '+' означает пробел, а буквальный плюс должен приходить как %2B.
        static string DecodeQuery(string value) { return Uri.UnescapeDataString(value.Replace("+", " ")); }
        public static List<Pair> ParseQuery(Uri uri)
        {
            return uri.Query.TrimStart('?').Split('&').Where(p => p.Length > 0).Select(p =>
            {
                int equals = p.IndexOf('=');
                return new Pair { Name = DecodeQuery(equals < 0 ? p : p.Substring(0, equals)), Value = equals < 0 ? "" : DecodeQuery(p.Substring(equals + 1)) };
            }).ToList();
        }
        public static string WithoutQuery(Uri uri) { return new UriBuilder(uri) { Query = "" }.Uri.AbsoluteUri; }
    }

    // Связывает Main.xaml с моделями, хранилищем и HTTP. Навигация и настройки вынесены
    // в другие части partial-класса: Navigation.cs, Themes.cs и ThemeSettings.cs.
    public sealed partial class Controller
    {
        readonly Window window;
        readonly string dataPath;
        Workspace workspace = new Workspace();
        Request current = new Request { Headers = new List<Pair> { new Pair { Name = "Accept", Value = "application/json" } } };
        ObservableCollection<Pair> headers;
        ObservableCollection<Pair> queries;
        CancellationTokenSource cancellation;
        bool dirty;
        // Программная загрузка и синхронизация не должны помечать запрос изменённым
        // или вызывать бесконечную цепочку URL -> таблица -> URL.
        bool loading;
        bool synchronizingUrl;
        string baseUrl;
        bool readOnlyData;
        bool refreshingLibrary;
        string selectedLibraryKey;
        readonly Dictionary<string, bool> expandedFolders = new Dictionary<string, bool>();
        public Controller(Window window, string storagePath = null)
        {
            this.window = window; CodeEditor.Install(window);
            dataPath = storagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "workspace.json");
            try
            {
                if (File.Exists(dataPath)) workspace = Data.Read(dataPath);
            }
            catch (Exception ex)
            {
                // При ошибке чтения не перезаписываем исходный файл пустой коллекцией.
                readOnlyData = true;
                MessageBox.Show(window, "Не удалось загрузить данные: " + ex.Message + "\nИсходный файл сохранён. Экспортируйте новую коллекцию или восстановите workspace.json.bak.", "Rest Agent", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Bind("ClearSearch", delegate { Control<TextBox>("Search").Clear(); });
            Bind("RenameItem", delegate { var item = Control<TreeView>("Library").SelectedItem as TreeViewItem; if (item != null && item.Tag != null) RenameNode(item.Tag); });
            Bind("ItemActions", delegate { var item = Control<TreeView>("Library").SelectedItem as TreeViewItem; if (item != null && item.ContextMenu != null) { item.ContextMenu.PlacementTarget = Control<Button>("ItemActions"); item.ContextMenu.IsOpen = true; } });
            Control<TreeView>("Library").PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                // Delete в поле переименования удаляет символ, а не запрос или папку.
                if (IsTextEditing(e.OriginalSource as DependencyObject) || IsTextEditing(Keyboard.FocusedElement as DependencyObject)) return;
                var item = Control<TreeView>("Library").SelectedItem as TreeViewItem;
                if (item == null || item.Tag == null) return;
                if (e.Key == Key.F2) { e.Handled = true; Safe(delegate { RenameNode(item.Tag); }); }
                if (e.Key == Key.Delete) { e.Handled = true; Safe(delegate { DeleteNode(item.Tag); }); }
            };
            Bind("NewRequest", NewRequest);
            Bind("NewFolder", NewFolder);
            Bind("Save", SaveRequest);
            Bind("Send", async delegate { await Send(); });
            Bind("Cancel", delegate { if (cancellation != null) cancellation.Cancel(); });
            Bind("AddQuery", delegate { ChooseQuery(null); });
            Bind("AddEmptyQuery", delegate { AddPair("Queries", queries); });
            Bind("CopyUrl", delegate { Clipboard.SetText(Data.BuildUri(Capture()).AbsoluteUri); Notice("Полный URL скопирован"); });
            Bind("AddHeader", delegate { ChooseHeader(null); });
            Bind("AddEmptyHeader", delegate { AddPair("Headers", headers); });
            Control<DataGrid>("Headers").AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                var button = e.OriginalSource as Button; if (button == null) return;
                var pair = button.DataContext as Pair; if (pair == null) return;
                if (button.Name == "ChooseHeaderName") { e.Handled = true; Safe(delegate { ChooseHeader(pair); }); }
                if (button.Name == "DeleteHeaderRow") { e.Handled = true; pair.PropertyChanged -= PairChanged; headers.Remove(pair); MarkDirty(); }
            }));
            Control<DataGrid>("Queries").AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                var button = e.OriginalSource as Button; if (button == null) return;
                var pair = button.DataContext as Pair; if (pair == null) return;
                if (button.Name == "ChooseQueryName") { e.Handled = true; Safe(delegate { ChooseQuery(pair); }); }
                if (button.Name == "DeleteQueryRow") { e.Handled = true; pair.PropertyChanged -= PairChanged; queries.Remove(pair); UpdateUrl(); MarkDirty(); }
            }));
            Bind("RemoveQuery", delegate { RemovePair("Queries", queries); });
            Bind("RemoveHeader", delegate { RemovePair("Headers", headers); });
            Bind("Bearer", delegate
            {
                string token = Prompt("Авторизация", "Bearer token", "");
                if (token == null) return;
                var pair = headers.FirstOrDefault(p => string.Equals(p.Name, "Authorization", StringComparison.OrdinalIgnoreCase));
                if (pair != null) headers.Remove(pair);
                var authorization = new Pair { Name = "Authorization", Value = "Bearer " + token.Trim() }; authorization.PropertyChanged += PairChanged; headers.Add(authorization); MarkDirty();
            });
            Bind("FormatRequest", delegate { Format("Body"); });
            Bind("FormatResponse", delegate { Format("ResponseBody"); });
            Bind("CopyResponse", delegate { Clipboard.SetText(ResponseText()); });
            Bind("SaveResponse", SaveResponse);
            Bind("ResponseTop", delegate { Control<CodeEditor>("ResponseBody").ScrollToHome(); });
            Bind("ResponseBottom", delegate { Control<CodeEditor>("ResponseBody").ScrollToEnd(); });
            Bind("CollapseResponse", delegate { Control<CodeEditor>("ResponseBody").FoldAll(true); });
            Bind("ExpandResponse", delegate { Control<CodeEditor>("ResponseBody").FoldAll(false); });
            Bind("GroupResponse", delegate { Control<CodeEditor>("ResponseBody").GroupJson(); });
            ConfigureEditor("Body", "RequestSyntax", "RequestLanguage");
            ConfigureEditor("ResponseBody", "ResponseSyntax", "ResponseLanguage");
            Bind("Import", Import);
            Bind("Export", Export);
            Bind("Settings", Settings);
            Control<TextBox>("Search").TextChanged += delegate { RefreshLibrary(); };
            Control<TreeView>("Library").SelectedItemChanged += delegate(object sender, RoutedPropertyChangedEventArgs<object> e)
            {
                if (refreshingLibrary) return;
                var item = e.NewValue as TreeViewItem;
                if (item == null) { UpdateLibraryActions(); return; }
                var request = item.Tag as Request;
                if (request != null && request.Id != current.Id)
                {
                    if (!CanLeave()) { item.IsSelected = false; RestoreLibrarySelection(); return; }
                    // Сохранение в CanLeave может пересоздать дерево: заново ищем запрос по ID.
                    request = workspace.Requests.FirstOrDefault(r => r.Id == request.Id);
                    if (request == null) return;
                    Load(request);
                }
                selectedLibraryKey = NodeKey(item.Tag); RestoreLibrarySelection(); UpdateLibraryActions();
            };
            Control<ListBox>("History").SelectionChanged += delegate
            {
                if (refreshingLibrary) return;
                var request = Control<ListBox>("History").SelectedItem as Request;
                if (request != null && CanLeave()) { var copy = Data.Clone(request); copy.Id = Guid.NewGuid().ToString(); copy.FolderId = null; Load(copy); MarkDirty(); }
            };
            foreach (string name in new[] { "Url", "Timeout" }) Control<TextBox>(name).TextChanged += delegate { MarkDirty(); };
            Control<TextBox>("Url").TextChanged += delegate { UrlEdited(); };
            Control<CodeEditor>("Body").TextChanged += delegate { MarkDirty(); };
            Control<ComboBox>("Method").SelectionChanged += delegate { MarkDirty(); };
            foreach (string name in new[] { "Headers", "Queries" }) Control<DataGrid>(name).CellEditEnding += delegate { MarkDirty(); };
            window.PreviewKeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.S) { e.Handled = true; Safe(SaveRequest); }
                if (e.Key == Key.Enter) { e.Handled = true; await Send(); }
                if (e.Key == Key.B) { e.Handled = true; ToggleSidebar(); }
            };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!CanLeave()) { e.Cancel = true; return; }
                if (cancellation != null) cancellation.Cancel();
            };
            InitializeNavigation(); InitializeThemes(); InitializeSidebarIcons(); RefreshLibrary(); Load(current);
        }
        T Control<T>(string name) where T : FrameworkElement { return (T)window.FindName(name); }
        void Bind(string name, Action action) { Control<Button>(name).Click += delegate { Safe(action); }; }
        void Safe(Action action) { try { action(); } catch (Exception ex) { Error(ex.Message); } }
        void Error(string message) { MessageBox.Show(window, message, "Rest Agent", MessageBoxButton.OK, MessageBoxImage.Warning); }
        void Notice(string message) { Control<TextBlock>("Notice").Text = message; }
        void MarkDirty() { if (!loading && !synchronizingUrl) { dirty = true; Control<TextBlock>("RequestTitle").Text = current.Name + " •"; } }
        // Завершаем редактирование ячеек и названия до чтения значений из интерфейса.
        void Commit()
        {
            FinishTitleRename(true);
            foreach (string name in new[] { "Headers", "Queries" })
            {
                var grid = Control<DataGrid>(name); grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            Keyboard.ClearFocus();
        }
        // Снимок редактора для отправки/сохранения; исходная модель current здесь не меняется.
        Request Capture()
        {
            Commit();
            int timeout;
            if (!int.TryParse(Control<TextBox>("Timeout").Text, out timeout) || timeout < 1 || timeout > 600) throw new ArgumentException("Таймаут должен быть от 1 до 600 секунд.");
            var request = Data.Clone(current);
            request.Method = ((ComboBoxItem)Control<ComboBox>("Method").SelectedItem).Content.ToString();
            request.Url = baseUrl ?? Control<TextBox>("Url").Text.Trim(); request.Body = Control<CodeEditor>("Body").Text;
            request.Timeout = timeout; request.Headers = Data.Clone(headers.ToList()); request.Queries = Data.Clone(queries.ToList());
            return request;
        }
        // Редактор получает копию запроса. Параметры из готового URL объединяются с таблицей;
        // строки таблицы имеют приоритет, поэтому повторное открытие не создаёт дубликаты.
        void Load(Request request)
        {
            FinishTitleRename(false);
            loading = true; current = Data.Clone(request);
            Control<TextBox>("Url").Text = current.Url; Control<CodeEditor>("Body").Text = current.Body;
            Control<TextBox>("Timeout").Text = current.Timeout.ToString();
            var method = Control<ComboBox>("Method"); method.SelectedItem = method.Items.Cast<ComboBoxItem>().First(i => i.Content.ToString() == current.Method);
            if (headers != null) foreach (var pair in headers) pair.PropertyChanged -= PairChanged;
            if (queries != null) foreach (var pair in queries) pair.PropertyChanged -= PairChanged;
            headers = new ObservableCollection<Pair>(current.Headers); queries = new ObservableCollection<Pair>(current.Queries);
            Uri parsed;
            baseUrl = current.Url;
            if (Uri.TryCreate(current.Url, UriKind.Absolute, out parsed) && (parsed.Scheme == "http" || parsed.Scheme == "https"))
            {
                baseUrl = Data.WithoutQuery(parsed);
                var names = new HashSet<string>(queries.Select(p => (p.Name ?? "").Trim()), StringComparer.Ordinal);
                queries = new ObservableCollection<Pair>(Data.ParseQuery(parsed).Where(p => !names.Contains(p.Name)).Concat(queries));
            }
            queries.CollectionChanged += delegate { UpdateUrl(); };
            headers.CollectionChanged += delegate { UpdateRequestSyntax(); };
            foreach (var pair in headers.Concat(queries)) pair.PropertyChanged += PairChanged;
            Control<DataGrid>("Headers").ItemsSource = headers; Control<DataGrid>("Queries").ItemsSource = queries;
            Control<TextBlock>("RequestTitle").Text = current.Name; dirty = false; loading = false; UpdateUrl(false);
            Control<ComboBox>("RequestSyntax").SelectedIndex = 0; UpdateRequestSyntax();
        }
        // Единая проверка несохранённых правок при смене запроса, копировании и закрытии окна.
        bool CanLeave()
        {
            Commit();
            if (!dirty) return true;
            var answer = MessageBox.Show(window, "Сохранить изменения текущего запроса?", "Несохранённый запрос", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.No) return true;
            try { return SaveRequestCore(); } catch (Exception ex) { Error(ex.Message); return false; }
        }
        string SelectedFolder()
        {
            var item = Control<TreeView>("Library").SelectedItem as TreeViewItem;
            if (item == null) return null;
            var folder = item.Tag as Folder; if (folder != null) return folder.Id;
            var request = item.Tag as Request; return request == null ? null : request.FolderId;
        }
        void NewRequest()
        {
            string folderId = SelectedFolder();
            if (!CanLeave()) return;
            Load(new Request { FolderId = folderId, Headers = new List<Pair> { new Pair { Name = "Accept", Value = "application/json" } } });
        }
        void NewFolder()
        {
            string name = Prompt("Новая папка", "Название папки", "Новая коллекция"); if (string.IsNullOrWhiteSpace(name)) return;
            var folder = new Folder { Id = Guid.NewGuid().ToString(), ParentId = SelectedFolder(), Name = name.Trim() };
            var next = Data.Clone(workspace); next.Folders.Add(folder);
            Persist(next); selectedLibraryKey = NodeKey(folder); RevealFolder(folder.ParentId); RefreshLibrary();
        }
        void SaveRequest() { SaveRequestCore(); }
        bool SaveRequestCore()
        {
            var request = Capture();
            if (!workspace.Requests.Any(r => r.Id == request.Id))
            {
                string name = Prompt("Сохранить запрос", "Название запроса", current.Name, request);
                if (string.IsNullOrWhiteSpace(name)) return false;
                request.Name = name.Trim();
            }
            var next = Data.Clone(workspace); next.Requests.RemoveAll(r => r.Id == request.Id); next.Requests.Add(request);
            Persist(next); Load(request); selectedLibraryKey = NodeKey(request); RevealFolder(request.FolderId); RefreshLibrary(); Notice("Запрос сохранён"); return true;
        }
        // Меняем коллекцию в памяти только после успешной записи на диск.
        void Persist(Workspace next)
        {
            if (readOnlyData) throw new IOException("Исходный файл данных повреждён. Восстановите его из резервной копии и перезапустите приложение либо экспортируйте коллекцию.");
            Directory.CreateDirectory(Path.GetDirectoryName(dataPath)); Data.Write(dataPath, next); workspace = next;
        }
        // Устойчивый ключ переживает пересоздание TreeViewItem; префикс различает папку и запрос.
        static string NodeKey(object node)
        {
            var folder = node as Folder; var request = node as Request;
            return folder != null ? "f:" + folder.Id : request != null ? "r:" + request.Id : "root";
        }
        IEnumerable<TreeViewItem> LibraryNodes(ItemCollection items)
        {
            foreach (TreeViewItem item in items) { yield return item; foreach (var child in LibraryNodes(item.Items)) yield return child; }
        }
        void RestoreLibrarySelection()
        {
            bool previous = refreshingLibrary; refreshingLibrary = true;
            try { var item = LibraryNodes(Control<TreeView>("Library").Items).FirstOrDefault(i => NodeKey(i.Tag) == selectedLibraryKey); if (item != null) item.IsSelected = true; }
            finally { refreshingLibrary = previous; }
            UpdateLibraryActions();
        }
        void UpdateLibraryActions()
        {
            var item = Control<TreeView>("Library").SelectedItem as TreeViewItem;
            Control<Button>("RenameItem").IsEnabled = item != null && item.Tag != null;
            Control<Button>("ItemActions").IsEnabled = item != null && item.ContextMenu != null;
        }
        void RevealFolder(string id)
        {
            while (id != null) { expandedFolders[id] = true; var folder = workspace.Folders.FirstOrDefault(f => f.Id == id); id = folder == null ? null : folder.ParentId; }
            expandedFolders["root"] = true;
        }
        // Перестраиваем дерево и историю с учётом поиска. Флаг блокирует обработчики выбора,
        // чтобы обновление представления само по себе не заменяло текущий редактор.
        void RefreshLibrary()
        {
            refreshingLibrary = true;
            try
            {
                var tree = Control<TreeView>("Library"); tree.Items.Clear();
                string search = Control<TextBox>("Search").Text.Trim();
                var root = CreateNode(null, "Все запросы", workspace.Requests.Count.ToString());
                root.IsExpanded = search.Length > 0 || !expandedFolders.ContainsKey("root") || expandedFolders["root"];
                TrackExpansion(root, "root"); AddNodes(root.Items, null, search); tree.Items.Add(root);
                var empty = Control<TextBlock>("LibraryEmpty");
                empty.Text = search.Length > 0 ? "Ничего не найдено.\nИзмените или очистите поиск." : "Пока нет сохранённых запросов.\nСоздайте каталог или сохраните запрос.";
                empty.Visibility = root.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                // Восстанавливаем выделение истории без повторного открытия её снимка.
                var history = Control<ListBox>("History"); var selected = history.SelectedItem as Request;
                history.ItemsSource = workspace.History.Where(r => search.Length == 0 || (r.Name + " " + r.Method + " " + r.Url).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); if (selected != null) history.SelectedItem = selected;
                var historyEmpty = Control<TextBlock>("HistoryEmpty"); historyEmpty.Visibility = history.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                historyEmpty.Text = search.Length == 0 ? "История появится после первого запроса." : "В истории нет совпадений. Измените или очистите поиск.";
                RestoreLibrarySelection();
            }
            finally { refreshingLibrary = false; }
            UpdateLibraryActions();
        }
        void TrackExpansion(TreeViewItem item, string key)
        {
            item.Expanded += delegate(object sender, RoutedEventArgs e) { if (e.OriginalSource == item && Control<TextBox>("Search").Text.Length == 0) expandedFolders[key] = true; };
            item.Collapsed += delegate(object sender, RoutedEventArgs e) { if (e.OriginalSource == item && Control<TextBox>("Search").Text.Length == 0) expandedFolders[key] = false; };
        }
        TreeViewItem CreateNode(object node, string name, string badge)
        {
            var item = new TreeViewItem { Tag = node, Style = (Style)window.FindResource("LibraryItem") };
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool folder = !(node is Request);
            FrameworkElement label;
            if (folder) { label = UiIcons.Create(window, node == null ? "Collection" : "Folder", 16); label.Width = 24; label.Margin = new Thickness(0, 0, 5, 0); }
            else
            {
                var method = new StackPanel { Orientation = Orientation.Horizontal, Width = 58 };
                method.Children.Add(UiIcons.Create(window, "Request", 12));
                method.Children.Add(new TextBlock { Text = badge, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(badge == "GET" ? Color.FromRgb(128, 210, 173) : badge == "DELETE" ? Color.FromRgb(238, 155, 153) : Color.FromRgb(236, 195, 123)) }); label = method;
            }
            header.Children.Add(label);
            var title = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }; Grid.SetColumn(title, 1); header.Children.Add(title);
            if (node == null) { var count = new TextBlock { Text = badge, VerticalAlignment = VerticalAlignment.Center }; count.SetResourceReference(TextBlock.ForegroundProperty, "SidebarMutedBrush"); Grid.SetColumn(count, 2); header.Children.Add(count); }
            else
            {
                AttachMenu(item);
                var actions = new Button { Content = UiIcons.Create(window, "More", 13), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0), Background = Brushes.Transparent, ToolTip = "Действия", Focusable = false };
                actions.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SidebarMutedBrush");
                actions.Click += delegate { item.ContextMenu.PlacementTarget = actions; item.ContextMenu.IsOpen = true; };
                Grid.SetColumn(actions, 2); header.Children.Add(actions);
            }
            item.Header = header; item.ToolTip = node is Request ? name + "\n" + ((Request)node).Url : name; return item;
        }
        void AddNodes(ItemCollection items, string parent, string search)
        {
            foreach (var folder in workspace.Folders.Where(f => string.IsNullOrEmpty(parent) ? string.IsNullOrEmpty(f.ParentId) : f.ParentId == parent).OrderBy(f => f.Name))
            {
                bool match = search.Length == 0 || folder.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                var item = CreateNode(folder, folder.Name, "");
                AddNodes(item.Items, folder.Id, match ? "" : search);
                if (match || item.Items.Count > 0)
                {
                    item.IsExpanded = search.Length > 0 || !expandedFolders.ContainsKey(folder.Id) || expandedFolders[folder.Id];
                    TrackExpansion(item, folder.Id); items.Add(item);
                }
            }
            foreach (var request in workspace.Requests.Where(r => (string.IsNullOrEmpty(parent) ? string.IsNullOrEmpty(r.FolderId) : r.FolderId == parent) && (search.Length == 0 || (r.Name + " " + r.Method + " " + r.Url).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(r => r.Name))
                items.Add(CreateNode(request, request.Name, request.Method));
        }
        void RenameNode(object node)
        {
            var item = LibraryNodes(Control<TreeView>("Library").Items).FirstOrDefault(i => NodeKey(i.Tag) == NodeKey(node)); if (item == null) return;
            var header = (Grid)item.Header;
            var title = header.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1);
            if (header.Children.OfType<TextBox>().Any()) return;
            var input = new TextBox { Text = title.Text, Style = (Style)window.FindResource("EditorInput"), Padding = new Thickness(4, 3, 4, 3), MinWidth = 50, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(input, 1); title.Visibility = Visibility.Collapsed; header.Children.Add(input);
            bool finished = false;
            Action<bool> finish = delegate(bool save)
            {
                if (finished) return; finished = true;
                header.Children.Remove(input); title.Visibility = Visibility.Visible;
                if (save && !string.IsNullOrWhiteSpace(input.Text)) Safe(delegate { RenameNodeCore(node, input.Text.Trim()); });
            };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Escape) { e.Handled = true; finish(e.Key == Key.Enter); } };
            input.LostKeyboardFocus += delegate { finish(true); };
            input.Focus(); input.SelectAll();
        }
        void RenameNodeCore(object node, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Название не может быть пустым.");
            name = name.Trim();
            var folder = node as Folder; var request = node as Request; var next = Data.Clone(workspace);
            if (folder != null) next.Folders.First(f => f.Id == folder.Id).Name = name;
            else next.Requests.First(r => r.Id == request.Id).Name = name;
            Persist(next);
            if (request != null && current.Id == request.Id) { current.Name = name; Control<TextBlock>("RequestTitle").Text = name + (dirty ? " •" : ""); }
            RefreshLibrary();
        }
        // Идём от папки назначения к корню: если встретим переносимую папку, возникнет цикл.
        bool CanMove(object node, string targetId)
        {
            if (node == null || !NodeExists(node)) return false;
            var folder = node as Folder;
            while (targetId != null)
            {
                if (folder != null && folder.Id == targetId) return false;
                var target = workspace.Folders.FirstOrDefault(f => f.Id == targetId); if (target == null) return false; targetId = target.ParentId;
            }
            return true;
        }
        void MoveNode(object node, string targetId)
        {
            if (!CanMove(node, targetId)) throw new InvalidDataException("Нельзя переместить папку внутрь себя или её вложенной папки.");
            var next = Data.Clone(workspace); var folder = node as Folder; var request = node as Request;
            if (folder != null) next.Folders.First(f => f.Id == folder.Id).ParentId = targetId;
            else next.Requests.First(r => r.Id == request.Id).FolderId = targetId;
            Persist(next); if (request != null && current.Id == request.Id) current.FolderId = targetId;
            RevealFolder(targetId); RefreshLibrary();
            Notice("Элемент перемещён: " + (targetId == null ? "Все запросы" : FolderPath(workspace.Folders.First(f => f.Id == targetId))));
        }
        void DuplicateNode(Request request)
        {
            if (!CanLeave()) return;
            request = workspace.Requests.First(r => r.Id == request.Id);
            var copy = Data.Clone(request); copy.Id = Guid.NewGuid().ToString(); copy.Name += " — копия";
            var next = Data.Clone(workspace); next.Requests.Add(copy); Persist(next);
            Load(copy); selectedLibraryKey = NodeKey(copy); RevealFolder(copy.FolderId); RefreshLibrary(); Notice("Создана копия сохранённого запроса");
        }
        void DeleteNode(object node)
        {
            var folder = node as Folder; var request = node as Request;
            string name = folder != null ? folder.Name : request.Name;
            if (MessageBox.Show(window, "Удалить «" + name + "»" + (folder != null ? " и всё содержимое папки?" : "?"), "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            DeleteNodeCore(node);
        }
        // Для папки сначала собираем всех потомков, затем удаляем их вместе с запросами.
        void DeleteNodeCore(object node)
        {
            var next = Data.Clone(workspace); var folder = node as Folder; var request = node as Request;
            var removed = new HashSet<string>();
            if (folder != null)
            {
                removed.Add(folder.Id); bool changed;
                do { changed = false; foreach (var f in next.Folders) if (f.ParentId != null && removed.Contains(f.ParentId) && removed.Add(f.Id)) changed = true; } while (changed);
                next.Folders.RemoveAll(f => removed.Contains(f.Id)); next.Requests.RemoveAll(r => r.FolderId != null && removed.Contains(r.FolderId));
            }
            else next.Requests.RemoveAll(r => r.Id == request.Id);
            bool currentWasDeleted = workspace.Requests.Any(r => r.Id == current.Id) && next.Requests.All(r => r.Id != current.Id);
            Persist(next); if (removed.Contains(current.FolderId ?? "")) { current.FolderId = null; MarkDirty(); }
            if (currentWasDeleted) { current.Id = Guid.NewGuid().ToString(); MarkDirty(); }
            selectedLibraryKey = folder != null && folder.ParentId != null ? "f:" + folder.ParentId : "root"; RefreshLibrary();
        }
        void AttachMenu(TreeViewItem item)
        {
            var menu = new ContextMenu { Style = (Style)window.FindResource("LibraryContextMenu") };
            var rename = LibraryMenuItem("Переименовать", "Rename", "F2"); rename.Click += delegate { Safe(delegate { RenameNode(item.Tag); }); };
            var request = item.Tag as Request;
            if (request != null) { var duplicate = LibraryMenuItem("Создать копию", "Copy", ""); duplicate.Click += delegate { Safe(delegate { DuplicateNode(request); }); }; menu.Items.Add(duplicate); }
            var move = LibraryMenuItem("Переместить в папку", "Move", "");
            var destinations = new List<Folder> { new Folder { Name = "Все запросы" } }; destinations.AddRange(workspace.Folders.OrderBy(f => FolderPath(f)));
            foreach (var destination in destinations)
            {
                if (!CanDropNode(item.Tag, destination.Id)) continue;
                var target = destination; var entry = LibraryMenuItem(target.Id == null ? "Все запросы" : FolderPath(target), "Folder", "");
                entry.Click += delegate { Safe(delegate { MoveNode(item.Tag, target.Id); }); }; move.Items.Add(entry);
            }
            move.IsEnabled = move.Items.Count > 0; menu.Items.Add(move); menu.Items.Add(rename); menu.Items.Add(new Separator { Style = (Style)window.FindResource("LibraryMenuSeparator") });
            var delete = LibraryMenuItem("Удалить", "Delete", "Del"); delete.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "DangerBrush"); delete.Click += delegate { Safe(delegate { DeleteNode(item.Tag); }); }; menu.Items.Add(delete); item.ContextMenu = menu;
        }
        string FolderPath(Folder folder)
        {
            var names = new List<string>();
            while (folder != null) { names.Add(folder.Name); folder = workspace.Folders.FirstOrDefault(f => f.Id == folder.ParentId); }
            names.Reverse(); return string.Join(" / ", names);
        }
        void PairChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (queries != null && queries.Contains((Pair)sender)) UpdateUrl();
            if (headers != null && headers.Contains((Pair)sender)) UpdateRequestSyntax();
            MarkDirty();
        }
        // Направление «таблица -> адрес»: выключенные параметры остаются в таблице,
        // но отсутствуют в URL. Неполный адрес разрешён во время ввода и проверяется при отправке.
        void UpdateUrl(bool markDirty = true)
        {
            if (synchronizingUrl || loading || queries == null) return;
            try
            {
                var uri = Data.BuildUri(new Request { Url = baseUrl, Queries = queries.ToList() });
                synchronizingUrl = true;
                if (Control<TextBox>("Url").Text != uri.AbsoluteUri) Control<TextBox>("Url").Text = uri.AbsoluteUri;
            }
            catch (ArgumentException) { /* Keep an incomplete URL editable; Send validates it. */ }
            finally { synchronizingUrl = false; }
            if (markDirty) MarkDirty();
        }
        // Направление «адрес -> таблица»: сопоставляем повторяющиеся имена по одному,
        // сохраняя выключенные и ещё не заполненные строки, которых нет в новом адресе.
        void UrlEdited()
        {
            if (synchronizingUrl || loading || queries == null) return;
            baseUrl = Control<TextBox>("Url").Text.Trim();
            Uri uri;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;
            synchronizingUrl = true;
            try
            {
                baseUrl = Data.WithoutQuery(uri);
                var incoming = Data.ParseQuery(uri); var previous = queries.ToList(); var unused = new List<Pair>(previous); var next = new List<Pair>();
                foreach (var field in incoming)
                {
                    var pair = unused.FirstOrDefault(p => p.Enabled && (p.Name ?? "").Trim() == field.Name) ?? unused.FirstOrDefault(p => (p.Name ?? "").Trim() == field.Name);
                    if (pair == null) { pair = field; pair.PropertyChanged += PairChanged; }
                    else { unused.Remove(pair); pair.Value = field.Value; pair.Enabled = true; }
                    next.Add(pair);
                }
                next.AddRange(unused.Where(p => !p.Enabled || string.IsNullOrWhiteSpace(p.Name)));
                foreach (var pair in previous.Except(next)) pair.PropertyChanged -= PairChanged;
                queries.Clear(); foreach (var pair in next) queries.Add(pair);
            }
            finally { synchronizingUrl = false; }
        }
        void ChooseQuery(Pair existing)
        {
            Commit();
            var names = queries.Select(p => p.Name).Concat(workspace.Requests.SelectMany(r => r.Queries).Select(p => p.Name));
            var pair = HeaderCatalog.Pick(window, names, existing, true); if (pair == null) return;
            if (existing == null) { pair.PropertyChanged += PairChanged; queries.Add(pair); }
            else { existing.Name = pair.Name; existing.Value = pair.Value; pair = existing; }
            var grid = Control<DataGrid>("Queries"); grid.SelectedItem = pair; grid.ScrollIntoView(pair); UpdateUrl(); MarkDirty();
        }
        void ChooseHeader(Pair existing)
        {
            Commit();
            var names = headers.Select(p => p.Name).Concat(workspace.Requests.SelectMany(r => r.Headers).Select(p => p.Name));
            var pair = HeaderCatalog.Pick(window, names, existing); if (pair == null) return;
            if (existing == null) { pair.PropertyChanged += PairChanged; headers.Add(pair); }
            else { existing.Name = pair.Name; existing.Value = pair.Value; pair = existing; }
            var grid = Control<DataGrid>("Headers"); grid.SelectedItem = pair; grid.ScrollIntoView(pair); MarkDirty();
        }
        void AddPair(string name, ObservableCollection<Pair> list)
        {
            Commit(); var pair = new Pair(); pair.PropertyChanged += PairChanged; list.Add(pair);
            var grid = Control<DataGrid>(name); grid.SelectedItem = pair; grid.ScrollIntoView(pair); MarkDirty();
        }
        void RemovePair(string name, ObservableCollection<Pair> list)
        {
            var pair = Control<DataGrid>(name).SelectedItem as Pair; if (pair != null) { pair.PropertyChanged -= PairChanged; list.Remove(pair); MarkDirty(); }
        }
        // В один момент выполняется один запрос. await возвращает управление WPF на время сети;
        // finally восстанавливает кнопки при успехе, ошибке, отмене и таймауте.
        async Task Send()
        {
            if (cancellation != null) return;
            CancellationTokenSource source = null;
            try
            {
                Request request = Capture(); Uri uri = Data.BuildUri(request);
                Control<TextBlock>("RequestAddress").Text = request.Method + " " + uri.AbsoluteUri;
                Control<TextBlock>("RequestAddress").Visibility = Visibility.Visible;
                source = new CancellationTokenSource(); cancellation = source;
                Control<Button>("Send").IsEnabled = false; Control<Button>("Cancel").IsEnabled = true;
                Control<CodeEditor>("ResponseBody").Clear(); Control<TextBox>("ResponseHeaders").Clear();
                SetResponseStatus("Отправка…", "#586E85");
                Control<ComboBox>("ResponseSyntax").SelectedIndex = 0; Control<CodeEditor>("ResponseBody").SetContentType(""); Control<TextBlock>("ResponseMeta").Text = "";
                var timer = Stopwatch.StartNew();
                using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, UseCookies = false, AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(request.Timeout) })
                using (var message = new HttpRequestMessage(new HttpMethod(request.Method), uri))
                {
                    // GET/HEAD используют отдельный транспорт: в .NET Framework добавление
                    // HttpContent даже ради Content-Type приводит к попытке отправить тело.
                    bool bodyless = request.Method == "GET" || request.Method == "HEAD";
                    if (!string.IsNullOrEmpty(request.Body))
                    {
                        if (request.Method == "GET" || request.Method == "HEAD") throw new ArgumentException("Для GET и HEAD оставьте тело запроса пустым.");
                        message.Content = new StringContent(request.Body, Encoding.UTF8); message.Content.Headers.Remove("Content-Type");
                    }
                    foreach (var pair in request.Headers.Where(p => !bodyless && p.Enabled && !string.IsNullOrWhiteSpace(p.Name)))
                    {
                        if ((pair.Value ?? "").IndexOfAny(new[] { '\r', '\n' }) >= 0 || pair.Name.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                            throw new ArgumentException("Заголовки не должны содержать переносы строк.");
                        if (!message.Headers.TryAddWithoutValidation(pair.Name.Trim(), pair.Value ?? ""))
                        {
                            if (message.Content == null) message.Content = new ByteArrayContent(new byte[0]);
                            if (!message.Content.Headers.TryAddWithoutValidation(pair.Name.Trim(), pair.Value ?? "")) throw new ArgumentException("Некорректный заголовок: " + pair.Name);
                        }
                    }
                    client.MaxResponseContentBufferSize = 16 * 1024 * 1024;
                    using (var response = bodyless ? await BodylessTransport.Send(request, uri, source.Token) : await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, source.Token))
                    {
                        string text = await response.Content.ReadAsStringAsync(); timer.Stop();
                        Control<CodeEditor>("ResponseBody").Text = text;
                        Control<TextBox>("ResponseHeaders").Text = response.Headers.ToString() + response.Content.Headers.ToString();
                        int status = (int)response.StatusCode;
                        SetResponseStatus(status + " " + response.ReasonPhrase, StatusColor(status));
                        Control<CodeEditor>("ResponseBody").SetContentType(response.Content.Headers.ContentType == null ? "" : response.Content.Headers.ContentType.MediaType);
                        Control<TextBlock>("ResponseMeta").Text = timer.ElapsedMilliseconds + " мс  •  " + Encoding.UTF8.GetByteCount(text).ToString("N0") + " байт UTF-8";
                        try { if (Control<CodeEditor>("ResponseBody").SyntaxLanguage == "JSON" || Control<CodeEditor>("ResponseBody").SyntaxLanguage == "XML") Control<CodeEditor>("ResponseBody").FormatText(); } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.Xml.XmlException) { }
                        // История хранит отправленный снимок, а не ссылку на редактируемый запрос.
                        // Ошибка её записи не должна скрывать уже полученный ответ.
                        var next = Data.Clone(workspace); next.History.Insert(0, request); next.History = next.History.Take(100).ToList();
                        try { Persist(next); RefreshLibrary(); Notice("Ответ получен. История сохранена."); } catch (Exception ex) { Notice("Ответ получен, история не сохранена: " + ex.Message); }
                    }
                }
            }
            catch (OperationCanceledException) { bool cancelled = source != null && source.IsCancellationRequested; SetResponseStatus(cancelled ? "Запрос отменён" : "Истёк таймаут", cancelled ? "#586E85" : "#D94B57"); }
            catch (Exception ex) { SetResponseStatus("Ошибка запроса", "#D94B57"); Control<CodeEditor>("ResponseBody").SetContentType("text/plain"); Control<CodeEditor>("ResponseBody").Text = ex.Message; Notice("Проверьте URL, заголовки и подключение к сети."); }
            finally
            {
                cancellation = null; if (source != null) source.Dispose();
                Control<Button>("Send").IsEnabled = true; Control<Button>("Cancel").IsEnabled = false;
            }
        }
        void Format(string name)
        {
            var box = Control<CodeEditor>(name); box.FormatText(); Notice(box.SyntaxLanguage + " отформатирован");
        }
        void ConfigureEditor(string name, string selector, string label)
        {
            var editor = Control<CodeEditor>(name); var combo = Control<ComboBox>(selector);
            combo.ItemsSource = CodeEditor.Modes;
            combo.SelectionChanged += delegate { editor.SetMode(combo.SelectedItem as string); };
            editor.SyntaxChanged += delegate
            {
                Control<TextBlock>(label).Text = editor.SyntaxLanguage;
                if (name == "ResponseBody")
                {
                    Control<Button>("CollapseResponse").IsEnabled = editor.FoldingCount > 0;
                    Control<Button>("ExpandResponse").IsEnabled = editor.FoldingCount > 0;
                    Control<Button>("GroupResponse").IsEnabled = editor.CanGroupJson;
                }
            };
            combo.SelectedIndex = 0;
        }
        void UpdateRequestSyntax()
        {
            if (headers == null) return;
            var header = headers.FirstOrDefault(p => p.Enabled && string.Equals((p.Name ?? "").Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase));
            Control<CodeEditor>("Body").SetContentType(header == null ? "" : header.Value);
        }
        public static string StatusColor(int status)
        {
            return status >= 400 ? "#D94B57" : status >= 300 ? "#B97813" : status >= 200 ? "#15AA8F" : "#3E79B1";
        }
        void SetResponseStatus(string text, string background)
        {
            Control<TextBlock>("ResponseStatus").Text = text;
            Control<Border>("ResponseStatusBand").Background = (Brush)new BrushConverter().ConvertFromString(background);
            Control<TextBlock>("ResponseStatus").Foreground = Brushes.White;
            Control<TextBlock>("ResponseMeta").Foreground = Brushes.White;
            Control<TextBlock>("RequestAddress").Foreground = Brushes.White;
        }
        // Берём полный текст документа: свёрнутые блоки и краткие превью не обрезают копию/файл.
        string ResponseText() { return Control<CodeEditor>("ResponseBody").Text; }
        void SaveResponse()
        {
            var dialog = new SaveFileDialog { FileName = "response." + (Control<CodeEditor>("ResponseBody").SyntaxLanguage == "JSON" ? "json" : Control<CodeEditor>("ResponseBody").SyntaxLanguage == "XML" ? "xml" : Control<CodeEditor>("ResponseBody").SyntaxLanguage == "HTML" ? "html" : "txt"), Filter = "Ответ|*.json;*.xml;*.html;*.txt|Все файлы|*.*" };
            if (dialog.ShowDialog(window) == true) File.WriteAllText(dialog.FileName, ResponseText(), new UTF8Encoding(false));
        }
        void Export()
        {
            var dialog = new SaveFileDialog { FileName = "rest-agent-collection.json", Filter = "Коллекция JSON|*.json" };
            if (dialog.ShowDialog(window) == true) { Data.Write(dialog.FileName, workspace); Notice("Коллекция экспортирована"); }
        }
        void Import()
        {
            var dialog = new OpenFileDialog { Filter = "Коллекция JSON|*.json" }; if (dialog.ShowDialog(window) != true) return;
            var imported = Data.Read(dialog.FileName);
            // Новые ID предотвращают конфликты при повторном импорте той же коллекции.
            // Ссылки на родителей и папки запросов переводим через одну таблицу соответствий.
            var next = Data.Clone(workspace); var mapping = imported.Folders.ToDictionary(f => f.Id, f => Guid.NewGuid().ToString());
            foreach (var folder in imported.Folders) { string old = folder.Id; folder.Id = mapping[old]; folder.ParentId = string.IsNullOrEmpty(folder.ParentId) ? null : mapping[folder.ParentId]; next.Folders.Add(folder); }
            foreach (var request in imported.Requests) { request.Id = Guid.NewGuid().ToString(); request.FolderId = string.IsNullOrEmpty(request.FolderId) ? null : mapping[request.FolderId]; next.Requests.Add(request); }
            Persist(next); RefreshLibrary(); Notice("Коллекция импортирована: " + imported.Requests.Count + " запросов");
        }
        void Settings() { CreateSettingsDialog().ShowDialog(); }
        string Prompt(string title, string label, string value, Request destination = null)
        {
            var dialog = new Window { Title = title, Owner = window, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = window.Background, Resources = window.Resources, FontFamily = window.FontFamily, FontSize = window.FontSize, Foreground = window.Foreground, Icon = window.Icon, ShowInTaskbar = false };
            var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = panel;
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 10) });
            var input = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(input);
            ComboBox folders = null;
            if (destination != null)
            {
                panel.Children.Add(new TextBlock { Text = "Папка", Margin = new Thickness(0, 0, 0, 8) });
                var choices = new List<Folder> { new Folder { Name = "Все запросы" } }; choices.AddRange(workspace.Folders.OrderBy(f => FolderPath(f)).Select(f => new Folder { Id = f.Id, Name = FolderPath(f) }));
                folders = new ComboBox { Style = (Style)window.FindResource("ValueInput"), IsEditable = true, IsReadOnly = true, ItemsSource = choices, DisplayMemberPath = "Name", SelectedItem = choices.FirstOrDefault(f => f.Id == destination.FolderId) ?? choices[0], Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(folders);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Сохранить", IsDefault = true }; var cancel = new Button { Content = "Отмена", IsCancel = true };
            ok.Click += delegate { if (!string.IsNullOrWhiteSpace(input.Text)) { if (destination != null) destination.FolderId = ((Folder)folders.SelectedItem).Id; dialog.DialogResult = true; } };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            return dialog.ShowDialog() == true ? input.Text : null;
        }
    }

    // Точка входа: зависимости загружаются из ресурсов EXE до создания контроллера и редакторов.
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            EmbeddedDependencies.Initialize();
            if (args.Contains("--self-test")) return SelfTest.Run();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var app = new Application();
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "Rest Agent"); e.Handled = true; };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))
            {
                var window = (Window)XamlReader.Load(stream);
                using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico"))
                {
                    var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(icon, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    window.Icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                }
                new Controller(window);
                return app.Run(window);
            }
        }
    }
}
