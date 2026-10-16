using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RestAgent
{
    // Встроенные регрессионные проверки запускаются ключом --self-test.
    // Рабочая коллекция из data не используется: контроллер получает временное хранилище.
    public static partial class SelfTest
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static int Run()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "RestAgentTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var report = new List<string>();
            try
            {
                string json = "{\"name\":\"Привет\",\"escape\":\"a\\\"b\",\"array\":[1,true,null],\"number\":1.2500}";
                Check(Data.Serializer().Serialize(Data.Serializer().DeserializeObject(json)) == Data.Serializer().Serialize(Data.Serializer().DeserializeObject(Data.Pretty(json))), "JSON changed");
                Check(Data.Pretty(json).Contains("1.2500"), "Number precision changed");
                bool invalid = false; try { Data.Pretty("{invalid"); } catch { invalid = true; } Check(invalid, "Invalid JSON accepted");
                report.Add("PASS JSON formatting, escaping, numeric representation, invalid JSON");
                TestReadableUnicode(report);
                var workspace = new Workspace();
                workspace.Folders.Add(new Folder { Id = "a", Name = "API" }); workspace.Folders.Add(new Folder { Id = "b", ParentId = "a", Name = "Заказы" });
                workspace.Requests.Add(new Request { FolderId = "b", Name = "Тест" });
                string path = Path.Combine(temporary, "workspace.json"); Data.Write(path, workspace); Data.Write(path, workspace);
                Check(Data.Read(path).Requests[0].FolderId == "b" && File.Exists(path + ".bak"), "Persistence failed");
                workspace.Folders[0].ParentId = "b"; invalid = false; try { Data.Validate(workspace); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Cycle accepted");
                report.Add("PASS nested collections, Unicode persistence, backup, cycle rejection");
                var request = new Request { Url = "http://localhost/test?existing=1#section" };
                request.Queries.Add(new Pair { Name = "search", Value = "a& b/тест" }); request.Queries.Add(new Pair { Name = "disabled", Value = "secret", Enabled = false });
                Uri uri = Data.BuildUri(request); Check(uri.Query.Contains("existing=1&search=a%26%20b%2F") && !uri.Query.Contains("secret") && uri.Fragment == "#section", "URL encoding failed");
                report.Add("PASS query encoding, existing query, disabled fields, URL fragment");
                var app = new Application();
                System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Window window;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) window = (Window)XamlReader.Load(stream);
                var controller = new Controller(window, Path.Combine(temporary, "ui-workspace.json"));
                foreach (var name in new[] { "Send", "Library", "Body", "Queries", "Headers", "ResponseBody", "Settings" }) Check(window.FindName(name) != null, "Missing UI control " + name);
                using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico"))
                {
                    var decoder = new IconBitmapDecoder(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); window.Icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First(); Check(decoder.Frames.Count == 7 && ((BitmapSource)window.Icon).PixelWidth == 256, "Missing icon sizes or wrong frame");
                }
                var tabs = (TabControl)window.FindName("RequestTabs");
                Check((string)((TabItem)tabs.Items[0]).Header == "Заголовки" && (string)((TabItem)tabs.Items[1]).Header == "Параметры" && (string)((TabItem)tabs.Items[2]).Header == "Тело запроса", "Request tabs out of order");
                window.Opacity = 0; window.Show(); window.UpdateLayout(); TestEditors(controller, window, report); TestHeaderCatalog(controller, window, report); TestQueryUrl(controller, window, report); TestLibrary(controller, window, report); TestNavigation(controller, window, report); TestCodeEditors(controller, window, report); TestJsonGroups(controller, window, report); TestThemes(controller, window, report);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1280, 860)); content.Arrange(new Rect(0, 0, 1280, 860)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1280, 860, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview.png"))) encoder.Save(output);
                report.Add("PASS XAML loading, control bindings, offscreen layout/render");
                TestRequest(controller, window, "GET", 200, UnicodeResponse(), report);
                TestRequest(controller, window, "POST", 401, "{\"error\":\"invalid_credentials\"}", report);
                TestRequest(controller, window, "HEAD", 200, "{\"head\":true}", report);
                TestRequest(controller, window, "GET", 401, "{\"error\":\"denied\"}", report);
                TestRequest(controller, window, "POST", 200, "{\"empty\":true}", report, false);
                TestRequest(controller, window, "DELETE", 200, "{\"deleted\":true}", report, true);
                TestRequest(controller, window, "GET", 201, "{\"created\":true}", report);
                TestRequest(controller, window, "GET", 302, "<html><body>Redirect</body></html>", report, false, "text/html");
                TestRequest(controller, window, "GET", 500, "{\"error\":\"server_error\"}", report);
                TestRequest(controller, window, "GET", 200, "<orders><order id=\"1\"><name>Привет</name></order></orders>", report, false, "application/xml");
                TestInterrupted(controller, window, false, report);
                TestInterrupted(controller, window, true, report);
                var saved = Data.Read(Path.Combine(temporary, "ui-workspace.json")); Check(saved.History.Count == 10 && saved.History[0].Method == "GET", "History failed");
                report.Add("PASS response history persistence");
                window.Close(); app.Shutdown();
                report.Add("ALL TESTS PASSED");
                File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt"), report);
                return 0;
            }
            catch (Exception ex)
            {
                report.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt"), report); return 1;
            }
            finally
            {
                foreach (var file in Directory.GetFiles(temporary)) File.Delete(file);
                Directory.Delete(temporary);
            }
        }
        static string UnicodeResponse()
        {
            return @"{""name"":""\u041d\u0430\u0431\u043e\u0440\u044b \u0434\u043b\u044f \u0440\u043e\u0437\u0435\u0442\u043a\u0430, \u0435\u043f\u0456\u0446\u0435\u043d\u0442\u0440, \u0442\u043c \u0434\u0438\u043b\u0435\u0440"",""name_multilang"":{""ru"":""\u041d\u0430\u0431\u043e\u0440\u044b \u0434\u043b\u044f \u0440\u043e\u0437\u0435\u0442\u043a\u0430, \u0435\u043f\u0456\u0446\u0435\u043d\u0442\u0440, \u0442\u043c \u0434\u0438\u043b\u0435\u0440"",""uk"":""\u041d\u0430\u0431\u043e\u0440\u0438 \u0434\u043b\u044f \u0440\u043e\u0437\u0435\u0442\u043a\u0430, \u0441\u0435\u043f\u0456\u0446\u0435\u043d\u0442\u0440, \u0442\u043c \u0434\u0438\u043b\u0435\u0440""}}";
        }
        // Проверяем и читаемость Unicode, и сохранность JSON: слеши, кавычки,
        // управляющие символы и суррогатные пары не должны менять смысл строки.
        static void TestReadableUnicode(List<string> report)
        {
            string pretty = Data.Pretty(UnicodeResponse(), true);
            Check(pretty.Contains("Наборы для розетка, епіцентр, тм дилер") && pretty.Contains("Набори для розетка, сепіцентр, тм дилер"), "Russian/Ukrainian Unicode escapes not readable");
            Check(!pretty.Contains("\\u041d"), "Unicode response still escaped");
            Check(Data.Serializer().Serialize(Data.Serializer().DeserializeObject(pretty)) == Data.Serializer().Serialize(Data.Serializer().DeserializeObject(UnicodeResponse())), "Unicode conversion changed values");
            string edge = @"{""\u006bey"":""\u0041\u00e9\u4e2d"",""emoji"":""\ud83d\uDE00"",""literal"":""\\u041d \\\\u0041 \\uZZZZ"",""slash"":""\u005Cu041d"",""quote"":""\u0022text\u0022"",""controls"":""\u0000\u000A\u0009\u001f\u0085\u2028\u2029"",""high"":""\uD800"",""low"":""\uDC00"",""number"":1.2500,""exponent"":-0.12500e+06}";
            string readable = Data.Pretty(edge, true);
            Check(readable.Contains("\"key\"") && readable.Contains("Aé中") && readable.Contains(char.ConvertFromUtf32(0x1F600)), "Escaped keys, mixed-case hex or surrogate pair failed");
            Check(readable.Contains(@"\\u041d") && readable.Contains(@"\u005Cu041d") && readable.Contains(@"\u0022text\u0022"), "Literal slash-u or quote escape corrupted");
            Check(readable.Contains(@"\u000A") && readable.Contains(@"\uD800") && readable.Contains(@"\uDC00"), "Control/lone surrogate escape lost");
            Check(readable.Contains("1.2500") && readable.Contains("-0.12500e+06"), "Unicode formatting changed number representation");
            Check(Data.Serializer().Serialize(Data.Serializer().DeserializeObject(readable)) == Data.Serializer().Serialize(Data.Serializer().DeserializeObject(edge)), "Unicode edge-case values changed");
            Check(Data.Pretty(readable, true) == readable, "Unicode formatting decoded twice");
            var utf8 = new UTF8Encoding(false, true); Check(utf8.GetString(utf8.GetBytes(readable)) == readable, "Readable JSON cannot be saved as UTF-8");
            Check(Data.Pretty(UnicodeResponse()).Contains(@"\u041d"), "Storage/default formatter unexpectedly decoded Unicode");
            report.Add("PASS readable Unicode JSON: user Russian/Ukrainian sample, keys, mixed-case hex, emoji pairs, literal backslashes, quotes/controls/lone surrogates, numeric precision, idempotence, strict UTF-8");
        }
        static T FindVisual<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindVisual<T>(VisualTreeHelper.GetChild(root, i)); if (found != null) return found; }
            return null;
        }
        static void TestEditors(Controller controller, Window window, List<string> report)
        {
            var request = new Request();
            request.Headers.Add(new Pair { Name = "Accept", Value = "application/json" });
            request.Queries.Add(new Pair { Name = "page", Value = "1" });
            var load = typeof(Controller).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic);
            var content = (FrameworkElement)window.Content;
            foreach (string name in new[] { "Queries", "Headers" })
            {
                load.Invoke(controller, new object[] { request });
                var grid = (DataGrid)window.FindName(name);
                var tab = grid.Parent;
                while (!(tab is TabItem)) tab = ((FrameworkElement)tab).Parent;
                ((TabItem)tab).IsSelected = true;
                foreach (int width in new[] { 1000, 1280 })
                {
                    content.Measure(new Size(width, 760)); content.Arrange(new Rect(0, 0, width, 760)); content.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); content.UpdateLayout();
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    Check(row != null, "Editor row missing");
                    var pair = (Pair)grid.Items[0];
                    var combo = FindVisual<ComboBox>(row);
                    if (name == "Headers") Check(combo != null && combo.ActualHeight >= 26 && combo.ActualHeight <= row.ActualHeight, "Editor clipped");
                    Check(grid.Columns[1].ActualWidth + grid.Columns[2].ActualWidth <= grid.ActualWidth, "Columns overflow");
                    if (name == "Headers")
                    {
                        var nameBox = FindVisual<TextBox>(row); nameBox.Text = "X-Custom-Header";
                        nameBox.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Check(pair.Name == nameBox.Text, "Custom header name lost");
                        nameBox.Text = "Content-Type"; window.UpdateLayout();
                        combo.SelectedIndex = 0; combo.GetBindingExpression(ComboBox.TextProperty).UpdateSource();
                        Check(pair.Value == "application/json", "Header value selection lost");
                        combo.Text = "application/custom+json"; combo.GetBindingExpression(ComboBox.TextProperty).UpdateSource();
                        Check(pair.Value == "application/custom+json", "Custom value lost");
                        nameBox.Text = "Authorization"; window.UpdateLayout(); Check(pair.Value == "application/custom+json", "Name change overwrote value");
                    }
                    else
                    {
                        var nameBox = FindVisual<TextBox>(row); nameBox.Text = "custom_query"; nameBox.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Check(pair.Name == nameBox.Text, "Custom query name lost");
                        var valueBox = FindVisual<TextBox>(grid.Columns[2].GetCellContent(row)); valueBox.Text = "a& b+тест"; valueBox.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Check(pair.Value == valueBox.Text, "Query value lost");
                    }
                    pair.Value = "edited value"; pair.Enabled = false;
                    Check(((TextBlock)window.FindName("RequestTitle")).Text.EndsWith(" \u2022"), "Pair edit not marked dirty");
                    var captured = (Request)typeof(Controller).GetMethod("Capture", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                    var saved = name == "Headers" ? captured.Headers[0] : captured.Queries[0];
                    Check(saved.Value == "edited value" && !saved.Enabled && saved.Name == pair.Name, "Editor capture failed");
                }
            }
            report.Add("PASS header/query editors at 1000/1280: layout, custom names, list selection, dirty tracking, capture");
        }
        // Вызываем закрытые действия контроллера для регрессионных сценариев,
        // не расширяя публичный API приложения ради самотеста.
        static object Call(Controller controller, string method, params object[] args)
        { return typeof(Controller).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, args); }
        static void TestQueryUrl(Controller controller, Window window, List<string> report)
        {
            var request = new Request { Url = "https://example.com/orders?page=1&existing=x#details", Name = "Orders" };
            request.Queries.Add(new Pair { Name = "page", Value = "2" });
            request.Queries.Add(new Pair { Name = "createdAt", Value = "2026-10-09 00:00:00" });
            request.Queries.Add(new Pair { Name = "createdTo", Value = "2026-10-09 23:59:59" });
            request.Queries.Add(new Pair { Name = "status", Value = "new", Enabled = false });
            request.Queries.Add(new Pair { Name = "uzdStatus", Value = "1" });
            request.Queries.Add(new Pair { Name = "tag[]", Value = "disabled tag", Enabled = false });
            var built = Data.BuildUri(request);
            Check(!built.Query.Contains("page=1") && built.Query.Contains("page=2") && built.Query.Contains("existing=x"), "Existing query duplicate/merge failed");
            Check(built.Query.Contains("createdAt=2026-10-09%2000%3A00%3A00") && built.Query.Contains("createdTo=2026-10-09%2023%3A59%3A59") && !built.Query.Contains("status=new"), "Date/disabled encoding failed");
            Call(controller, "Load", request);
            var url = (TextBox)window.FindName("Url"); var grid = (DataGrid)window.FindName("Queries");
            Check(url.Text == built.AbsoluteUri && !((TextBlock)window.FindName("RequestTitle")).Text.EndsWith("\u2022"), "Loading did not show full URL or made request dirty");
            var page = grid.Items.Cast<Pair>().First(p => p.Name == "page"); page.Value = "3";
            Check(url.Text.Contains("page=3") && !url.Text.Contains("page=2"), "Table-to-URL sync failed");
            page.Enabled = false; Check(!url.Text.Contains("page="), "Disabled query remained in URL");
            url.Text = "https://example.com/orders?page=4&search=a%2Bb+%26+%D1%82%D0%B5%D1%81%D1%82&tag%5B%5D=one&tag%5B%5D=two#details";
            Check(grid.Items.Cast<Pair>().First(p => p.Name == "page").Value == "4" && grid.Items.Cast<Pair>().First(p => p.Name == "page").Enabled, "URL-to-table sync failed");
            Check(grid.Items.Cast<Pair>().First(p => p.Name == "search").Value == "a+b & тест" && grid.Items.Cast<Pair>().Count(p => p.Name == "tag[]" && p.Enabled) == 2, "URL decoding/repeated keys lost");
            Check(grid.Items.Cast<Pair>().Any(p => p.Name == "status" && !p.Enabled), "Pasting URL lost disabled row");
            var captured = (Request)Call(controller, "Capture"); var once = Data.BuildUri(captured);
            Call(controller, "Load", captured); Check(Data.BuildUri((Request)Call(controller, "Capture")).AbsoluteUri == once.AbsoluteUri, "Save/load duplicated or double-encoded query");
            Check(once.Query.Contains("search=a%2Bb%20%26%20%D1%82%D0%B5%D1%81%D1%82") && !once.Query.Contains("%252B") && once.Fragment == "#details", "Query double-encoded or fragment lost");
            grid.Items.Cast<Pair>().First(p => p.Name == "search").Value = "100% / a=b";
            Check(url.Text.Contains("search=100%25%20%2F%20a%3Db"), "Percent/equal/slash encoding failed");
            url.Text = "invalid URL"; bool invalid = false; try { Data.BuildUri((Request)Call(controller, "Capture")); } catch (ArgumentException) { invalid = true; } Check(invalid, "Invalid URL reused previous address");
            Call(controller, "Load", request);
            Exception failure = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                Window dialog = null; foreach (Window candidate in Application.Current.Windows) if (candidate != window) dialog = candidate;
                try
                {
                    Check(dialog != null, "Parameter dialog missing"); dialog.Opacity = 0;
                    ((TextBox)dialog.FindName("QuerySearch")).Text = "createdTo";
                    var list = (ListBox)dialog.FindName("QueryOptions"); list.SelectedItem = list.Items.Cast<HeaderInfo>().First(h => h.Name == "createdTo");
                    var value = (ComboBox)dialog.FindName("QueryValue"); value.Text = "2026-10-10 23:59:59";
                    RenderDialog(dialog, "parameter-picker-preview.png");
                    ((Button)dialog.FindName("QueryConfirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { failure = ex; if (dialog != null && dialog.IsVisible) dialog.DialogResult = false; }
            }));
            Call(controller, "ChooseQuery", grid.Items.Cast<Pair>().First(p => p.Name == "createdTo")); if (failure != null) throw failure;
            Check(url.Text.Contains("createdTo=2026-10-10%2023%3A59%3A59"), "Catalog value not synced to URL");
            ((Button)window.FindName("AddEmptyQuery")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0); ((TabItem)((TabControl)window.FindName("RequestTabs")).Items[1]).IsSelected = true; grid.ScrollIntoView(grid.Items[0]); window.UpdateLayout();
            row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0); var removedName = ((Pair)grid.Items[0]).Name;
            FindVisual<Button>(grid.Columns[3].GetCellContent(row)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!grid.Items.Cast<Pair>().Any(p => p.Name == removedName), "Query row delete failed");
            var showcase = new Request { Url = "https://example.com/orders", Name = "Orders", Queries = request.Queries.Where(p => p.Name == "createdAt" || p.Name == "createdTo" || p.Name == "status" || p.Name == "uzdStatus").ToList() };
            Call(controller, "Load", showcase); window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            foreach (int width in new[] { 1000, 1280, 1920 })
            {
                var content = (FrameworkElement)window.Content; content.Measure(new Size(width, 860)); content.Arrange(new Rect(0, 0, width, 860)); content.UpdateLayout();
                foreach (string name in new[] { "Save", "Send", "AddQuery", "AddEmptyQuery", "Settings", "RenameItem" })
                { var button = (Button)window.FindName(name); Check(button.ActualHeight <= 30 && button.ActualWidth < 160, "Oversized button " + name); }
            }
            var preview = (FrameworkElement)window.Content; preview.Measure(new Size(1280, 860)); preview.Arrange(new Rect(0, 0, 1280, 860)); preview.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth), (int)Math.Ceiling(preview.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parameters-preview.png"))) encoder.Save(output);
            report.Add("PASS queries: full URL/table sync, dates, disabled/repeated fields, merge without duplicates, UTF-8/+/%/arrays, save/load, picker edit, row delete, compact buttons at 1000/1280/1920");
        }
        static void TestHeaderCatalog(Controller controller, Window window, List<string> report)
        {
            Check(HeaderCatalog.Names.Length > 200 && System.Linq.Enumerable.Distinct(HeaderCatalog.Names, StringComparer.OrdinalIgnoreCase).Count() == HeaderCatalog.Names.Length, "Incomplete or duplicate registry");
            Check(System.Linq.Enumerable.Contains(HeaderCatalog.Names, "Content-Digest") && System.Linq.Enumerable.Contains(HeaderCatalog.Names, "X-API-Key"), "Registry/custom headers missing");
            Call(controller, "Load", new Request());
            Exception failure = null;
            Action<Action<Window>> queue = action => Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                Window dialog = null; foreach (Window candidate in Application.Current.Windows) if (candidate != window) dialog = candidate;
                try { Check(dialog != null, "Catalog dialog missing"); dialog.Opacity = 0; dialog.UpdateLayout(); action(dialog); }
                catch (Exception ex) { failure = ex; if (dialog != null && dialog.IsVisible) dialog.DialogResult = false; }
            }));
            queue(delegate(Window dialog)
            {
                var search = (TextBox)dialog.FindName("HeaderSearch"); var list = (ListBox)dialog.FindName("HeaderOptions");
                Check(list.Items.Count > 30, "Popular headers incomplete");
                search.Text = "Content-Digest"; Check(list.Items.Count < 5 && list.Items.Cast<HeaderInfo>().Any(h => h.Name == "Content-Digest"), "Search did not include full registry");
                search.Text = "Content-Type"; list.SelectedIndex = 0;
                var name = (TextBox)dialog.FindName("HeaderName"); var value = (ComboBox)dialog.FindName("HeaderValue");
                Check(name.Text == "Content-Type" && value.Items.Count >= 8, "Name/value suggestions failed");
                value.Text = "application/custom+json";
                search.Clear(); list.SelectedItem = list.Items.Cast<HeaderInfo>().First(h => h.Name == "Content-Type");
                RenderDialog(dialog, "header-picker-preview.png");
                ((Button)dialog.FindName("HeaderConfirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Call(controller, "ChooseHeader", (object)null); if (failure != null) throw failure;
            var captured = (Request)Call(controller, "Capture"); Check(captured.Headers.Count == 1 && captured.Headers[0].Value == "application/custom+json", "Catalog header not captured");
            queue(delegate(Window dialog)
            {
                var name = (TextBox)dialog.FindName("HeaderName"); var value = (ComboBox)dialog.FindName("HeaderValue"); var ok = (Button)dialog.FindName("HeaderConfirm");
                Check(value.Text == "application/custom+json", "Editing lost current value");
                name.Text = "bad header"; ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(dialog.IsVisible, "Invalid header name accepted");
                name.Text = "X-My-Custom-Header"; Check(value.Text == "application/custom+json", "Changing name overwrote value"); value.Text = "custom value";
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            var pair = (Pair)((DataGrid)window.FindName("Headers")).Items[0]; Call(controller, "ChooseHeader", pair); if (failure != null) throw failure;
            Check(pair.Name == "X-My-Custom-Header" && pair.Value == "custom value", "Editing existing row failed");
            queue(delegate(Window dialog) { dialog.DialogResult = false; });
            Call(controller, "ChooseHeader", (object)null); if (failure != null) throw failure;
            Check(((Request)Call(controller, "Capture")).Headers.Count == 1, "Cancel added a header");
            ((Button)window.FindName("AddEmptyHeader")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((DataGrid)window.FindName("Headers")).Items.Count == 2, "Blank header row not added");
            window.UpdateLayout(); var grid = (DataGrid)window.FindName("Headers");
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1); grid.ScrollIntoView(grid.Items[1]); window.UpdateLayout(); row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
            var buttons = System.Linq.Enumerable.OfType<Button>(FindVisual<Grid>(grid.Columns[1].GetCellContent(row)).Children);
            Check(System.Linq.Enumerable.Any(buttons, b => b.Name == "ChooseHeaderName"), "Row catalog button missing");
            var remove = FindVisual<Button>(grid.Columns[3].GetCellContent(row)); remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(grid.Items.Count == 1, "Row delete failed");
            report.Add("PASS header catalog: full registry, search, popular/custom names, value suggestions, name validation, add/edit/cancel, blank row, row deletion; 7 icon sizes; tab order");
        }
        static IEnumerable<TreeViewItem> Nodes(ItemCollection items)
        { foreach (TreeViewItem item in items) { yield return item; foreach (var child in Nodes(item.Items)) yield return child; } }
        static void RenderDialog(Window dialog, string name)
        {
            dialog.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); dialog.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            var panel = (FrameworkElement)dialog.Content; int width = (int)Math.Ceiling(panel.ActualWidth + 48), height = (int)Math.Ceiling(panel.ActualHeight + 48);
            var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) { context.DrawRectangle(dialog.Background, null, new Rect(0, 0, width, height)); context.DrawRectangle(new VisualBrush(panel), null, new Rect(24, 24, panel.ActualWidth, panel.ActualHeight)); }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(output);
        }
        static void TestLibrary(Controller controller, Window window, List<string> report)
        {
            var fixture = new Workspace();
            fixture.Folders.Add(new Folder { Id = "api", Name = "Marketplace API" });
            fixture.Folders.Add(new Folder { Id = "orders", ParentId = "api", Name = "Orders" });
            fixture.Folders.Add(new Folder { Id = "shared", Name = "Shared" });
            fixture.Requests.Add(new Request { Id = "get", FolderId = "orders", Name = "List orders", Url = "https://example.com/orders" });
            fixture.Requests.Add(new Request { Id = "post", FolderId = "orders", Name = "Create order", Method = "POST", Url = "https://example.com/orders" });
            fixture.Requests.Add(new Request { Id = "health", Name = "Health check", Url = "https://example.com/health" });
            fixture.Requests[0].Headers.Add(new Pair { Name = "Accept", Value = "application/json" });
            fixture.Requests[0].Headers.Add(new Pair { Name = "Content-Type", Value = "application/json" });
            fixture.Requests[0].Headers.Add(new Pair { Name = "X-API-Key", Value = "demo-key" });
            fixture.Requests[0].Queries.Add(new Pair { Name = "createdAt", Value = "2026-10-09 00:00:00" });
            fixture.Requests[0].Queries.Add(new Pair { Name = "createdTo", Value = "2026-10-09 23:59:59" });
            fixture.Requests[0].Queries.Add(new Pair { Name = "status", Value = "new", Enabled = false });
            fixture.Requests[0].Queries.Add(new Pair { Name = "uzdStatus", Value = "1" });
            Call(controller, "Persist", fixture); Call(controller, "Load", fixture.Requests[0]); Call(controller, "RefreshLibrary");
            var tree = (TreeView)window.FindName("Library");
            Func<string, TreeViewItem> find = id => System.Linq.Enumerable.FirstOrDefault(Nodes(tree.Items), i => i.Tag is Request && ((Request)i.Tag).Id == id || i.Tag is Folder && ((Folder)i.Tag).Id == id);
            find("get").IsSelected = true;
            ((CodeEditor)window.FindName("Body")).Text = "unsaved editor text";
            Call(controller, "RefreshLibrary");
            Check(((Request)((TreeViewItem)tree.SelectedItem).Tag).Id == "get", "Selection lost on refresh");
            Check(((CodeEditor)window.FindName("Body")).Text == "unsaved editor text", "Refresh overwrote unsaved editor");
            Call(controller, "RenameNodeCore", fixture.Requests[0], "Orders list");
            Check(((TextBlock)window.FindName("RequestTitle")).Text == "Orders list \u2022", "Rename overwrote dirty state");
            Call(controller, "RenameNode", ((TreeViewItem)tree.SelectedItem).Tag);
            var renameInput = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<TextBox>(((Grid)((TreeViewItem)tree.SelectedItem).Header).Children));
            renameInput.Text = "Inline name";
            renameInput.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Check(((TextBlock)window.FindName("RequestTitle")).Text == "Inline name \u2022", "Inline rename did not commit");
            Call(controller, "RenameNode", ((TreeViewItem)tree.SelectedItem).Tag);
            renameInput = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<TextBox>(((Grid)((TreeViewItem)tree.SelectedItem).Header).Children));
            renameInput.Text = "Discard this name";
            renameInput.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Check(((TextBlock)window.FindName("RequestTitle")).Text == "Inline name \u2022", "Escape committed rename");
            Check(!(bool)Call(controller, "CanMove", fixture.Folders[0], "orders"), "Move into descendant allowed");
            Check(!(bool)Call(controller, "CanMove", fixture.Folders[0], "api"), "Move into self allowed");
            Call(controller, "MoveNode", fixture.Requests[0], (object)null);
            Check(((Request)Call(controller, "Capture")).FolderId == null, "Current moved folder stale");
            Check((bool)Call(controller, "SaveRequestCore"), "Existing save failed");
            Call(controller, "DuplicateNode", ((TreeViewItem)tree.SelectedItem).Tag);
            var copy = (Request)Call(controller, "Capture"); Check(copy.Id != "get" && copy.Name.Contains("копия"), "Duplicate failed");
            find("api").IsExpanded = false; Call(controller, "RefreshLibrary"); Check(!find("api").IsExpanded, "Collapsed folder reset");
            ((TextBox)window.FindName("Search")).Text = "Create order"; Check(find("post") != null && find("health") == null && find("api").IsExpanded, "Search lost ancestor or did not filter");
            ((TextBox)window.FindName("Search")).Text = "Orders"; Check(find("post") != null, "Folder search hid descendants");
            ((TextBox)window.FindName("Search")).Text = "nothing-matches"; Check(((TextBlock)window.FindName("LibraryEmpty")).Visibility == Visibility.Visible, "Empty search feedback missing");
            ((TextBox)window.FindName("Search")).Clear(); Check(!find("api").IsExpanded, "Search changed expansion");
            Call(controller, "DeleteNodeCore", fixture.Folders[0]); Check(find("api") == null && find("post") == null && find("health") != null, "Recursive deletion failed");
            Call(controller, "Load", new Request());
            Exception dialogError = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                Window dialog = null;
                foreach (Window candidate in Application.Current.Windows) if (candidate != window) dialog = candidate;
                try
                {
                    Check(dialog != null, "Save dialog missing"); dialog.Opacity = 0; dialog.UpdateLayout();
                    var panel = (StackPanel)dialog.Content;
                    var input = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<TextBox>(panel.Children)); input.Text = "Saved from dialog";
                    var folders = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<ComboBox>(panel.Children));
                    Check(folders.SelectedItem != null, "Folder selector missing");
                    folders.SelectedItem = System.Linq.Enumerable.First(System.Linq.Enumerable.Cast<Folder>(folders.Items), f => f.Id == "shared");
                    var buttons = (StackPanel)panel.Children[panel.Children.Count - 1]; var ok = (Button)buttons.Children[0];
                    Check(ok.ActualHeight >= 24 && ok.ActualHeight <= 32 && input.ActualHeight >= 26, "Dialog controls are not compact/styled");
                    dialog.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
                    int dialogWidth = (int)Math.Ceiling(panel.ActualWidth + 44), dialogHeight = (int)Math.Ceiling(panel.ActualHeight + 44);
                    var drawing = new DrawingVisual();
                    using (var context = drawing.RenderOpen()) { context.DrawRectangle(dialog.Background, null, new Rect(0, 0, dialogWidth, dialogHeight)); context.DrawRectangle(new VisualBrush(panel), null, new Rect(22, 22, panel.ActualWidth, panel.ActualHeight)); }
                    var dialogBitmap = new RenderTargetBitmap(dialogWidth, dialogHeight, 96, 96, PixelFormats.Pbgra32); dialogBitmap.Render(drawing);
                    var dialogEncoder = new PngBitmapEncoder(); dialogEncoder.Frames.Add(BitmapFrame.Create(dialogBitmap));
                    using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save-preview.png"))) dialogEncoder.Save(output);
                    ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { dialogError = ex; if (dialog != null) dialog.DialogResult = false; }
            }));
            bool savedFromDialog = (bool)Call(controller, "SaveRequestCore"); if (dialogError != null) throw dialogError; Check(savedFromDialog, "New save failed");
            Check(((Request)Call(controller, "Capture")).Name == "Saved from dialog", "New saved name lost");
            Check(((Request)Call(controller, "Capture")).FolderId == "shared", "Save folder selection lost");
            report.Add("PASS library: selection, unsaved edits, rename, move/cycle guard, duplicate, search, expansion, recursive delete, styled save dialog, save without repeated prompt");
            Call(controller, "Persist", fixture); Call(controller, "Load", fixture.Requests[0]);
            ((TextBox)window.FindName("Search")).Clear();
            Call(controller, "RevealFolder", "orders"); Call(controller, "RefreshLibrary"); find("get").IsSelected = true;
            window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
        }
        static System.Windows.Input.KeyEventArgs PreviewKey(Window window, UIElement target, System.Windows.Input.Key key)
        {
            var e = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e); return e;
        }
        static DragEventArgs PreviewDrag(Window window, UIElement target, IDataObject data, RoutedEvent routed)
        {
            window.UpdateLayout();
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var e = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, window.FindName("Library"), new Point(40, 60) });
            e.RoutedEvent = routed; target.RaiseEvent(e); return e;
        }
        static void TestNavigation(Controller controller, Window window, List<string> report)
        {
            string path = (string)typeof(Controller).GetField("dataPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var original = Data.Read(path); var tree = (TreeView)window.FindName("Library");
            Func<string, TreeViewItem> find = id => Nodes(tree.Items).FirstOrDefault(i => i.Tag is Request && ((Request)i.Tag).Id == id || i.Tag is Folder && ((Folder)i.Tag).Id == id);
            window.Width = 1280; window.UpdateLayout();
            var column = (ColumnDefinition)window.FindName("SidebarColumn"); column.Width = new GridLength(390); window.UpdateLayout();
            ((System.Windows.Controls.GridSplitter)window.FindName("SidebarSplitter")).RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(110, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            Check(Math.Abs(column.ActualWidth - 390) < 1, "Sidebar resizing failed");
            column.Width = new GridLength(420);
            ((System.Windows.Controls.GridSplitter)window.FindName("SidebarSplitter")).RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(30, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            window.UpdateLayout(); Check(Math.Abs(column.ActualWidth - 420) < 1, "Drag completion read stale sidebar layout");
            column.Width = new GridLength(390);
            ((System.Windows.Controls.GridSplitter)window.FindName("SidebarSplitter")).RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-30, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent }); window.UpdateLayout();
            string unsaved = "unsaved body must survive navigation"; ((CodeEditor)window.FindName("Body")).Text = unsaved;
            ((Button)window.FindName("ToggleSidebar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            Check(column.ActualWidth == 0 && ((Border)window.FindName("Sidebar")).Visibility == Visibility.Collapsed, "Sidebar did not hide");
            Check(((CodeEditor)window.FindName("Body")).Text == unsaved, "Hiding lost editor text");
            Window restored; using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) restored = (Window)XamlReader.Load(stream);
            new Controller(restored, path); Check(((Border)restored.FindName("Sidebar")).Visibility == Visibility.Collapsed, "Hidden sidebar setting not restored"); restored.Close();
            Call(controller, "ToggleSidebar"); window.UpdateLayout(); Check(Math.Abs(column.ActualWidth - 390) < 1, "Sidebar width not restored");
            foreach (int width in new[] { 1000, 1280, 1920 })
            {
                window.Width = width; window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); window.UpdateLayout(); Check(column.ActualWidth >= 220 && column.ActualWidth <= 520, "Sidebar limits failed");
                Check(((FrameworkElement)((Grid)window.FindName("WorkspaceLayout")).Children[2]).ActualWidth >= 660, "Resizing squeezed request editor");
                var grid = (DataGrid)window.FindName("Queries"); Check(grid.Columns.Sum(c => c.ActualWidth) <= grid.ActualWidth + 2, "Parameter columns overflow after sidebar resize");
            }
            window.Width = 1280; window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); window.UpdateLayout();
            Call(controller, "BeginTitleRename"); var title = (TextBox)window.FindName("RequestTitleEditor"); title.Text = "  Header name  "; PreviewKey(window, title, System.Windows.Input.Key.Enter);
            Check(((Request)Call(controller, "Capture")).Name == "Header name", "Header rename failed");
            Check(Data.Read(path).Requests.First(r => r.Id == "get").Name == "Header name", "Header rename not persisted");
            Check(((CodeEditor)window.FindName("Body")).Text == unsaved && ((TextBlock)window.FindName("RequestTitle")).Text.EndsWith(" •"), "Header rename lost unsaved body/dirty state");
            Check(Data.Read(path).Requests.First(r => r.Id == "get").Body != unsaved, "Rename saved unsaved request body");
            Call(controller, "BeginTitleRename"); title.Text = "discard"; PreviewKey(window, title, System.Windows.Input.Key.Escape);
            Check(((Request)Call(controller, "Capture")).Name == "Header name", "Escape saved header rename");
            Call(controller, "BeginTitleRename"); title.Text = " "; PreviewKey(window, title, System.Windows.Input.Key.Enter);
            Check(((Request)Call(controller, "Capture")).Name == "Header name", "Empty header name accepted");
            foreach (string id in new[] { "get", "orders" })
            {
                var item = find(id); item.IsSelected = true; Call(controller, "RenameNode", item.Tag);
                var input = ((Grid)item.Header).Children.OfType<TextBox>().Single(); input.Focus(); input.SelectAll();
                var key = PreviewKey(window, input, System.Windows.Input.Key.Delete);
                Check(!key.Handled, "Tree stole Delete during inline rename");
                System.Windows.Documents.EditingCommands.Delete.Execute(null, input); Check(input.Text == "", "Delete did not edit rename text");
                PreviewKey(window, input, System.Windows.Input.Key.Escape);
                Check(Data.Read(path).Requests.Count == original.Requests.Count && Data.Read(path).Folders.Count == original.Folders.Count, "Delete removed a renamed node");
            }
            find("get").IsSelected = true; window.UpdateLayout();
            var data = (DataObject)Call(controller, "LibraryDragData", find("get").Tag);
            var destination = find("shared"); var over = PreviewDrag(window, (UIElement)destination.Header, data, DragDrop.PreviewDragOverEvent);
            Check(over.Effects == DragDropEffects.Move && ((Border)destination.Template.FindName("Row", destination)).BorderThickness.Left == 1, "Drag-over did not accept/highlight destination");
            var drop = PreviewDrag(window, (UIElement)destination.Header, data, DragDrop.PreviewDropEvent);
            Check(drop.Effects == DragDropEffects.Move && ((Request)Call(controller, "Capture")).FolderId == "shared", "Request drag/drop failed");
            Check(((CodeEditor)window.FindName("Body")).Text == unsaved, "Drag/drop lost unsaved editor");
            data = (DataObject)Call(controller, "LibraryDragData", find("orders").Tag);
            PreviewDrag(window, (UIElement)find("shared").Header, data, DragDrop.PreviewDropEvent);
            Check(Data.Read(path).Folders.First(f => f.Id == "orders").ParentId == "shared", "Folder drag/drop failed");
            data = (DataObject)Call(controller, "LibraryDragData", find("shared").Tag);
            over = PreviewDrag(window, (UIElement)find("orders").Header, data, DragDrop.PreviewDragOverEvent);
            drop = PreviewDrag(window, (UIElement)find("orders").Header, data, DragDrop.PreviewDropEvent);
            Check(over.Effects == DragDropEffects.None && drop.Effects == DragDropEffects.None, "Folder cycle accepted by drag/drop");
            data = (DataObject)Call(controller, "LibraryDragData", find("get").Tag);
            PreviewDrag(window, (UIElement)find("post").Header, data, DragDrop.PreviewDropEvent);
            Check(((Request)Call(controller, "Capture")).FolderId == "orders", "Drop onto request did not use containing folder");
            var root = (TreeViewItem)tree.Items[0]; data = (DataObject)Call(controller, "LibraryDragData", find("orders").Tag);
            PreviewDrag(window, (UIElement)root.Header, data, DragDrop.PreviewDropEvent);
            Check(Data.Read(path).Folders.First(f => f.Id == "orders").ParentId == null, "Drop into root failed");
            root = (TreeViewItem)tree.Items[0]; over = PreviewDrag(window, (UIElement)root.Header, new DataObject(DataFormats.Text, "external data"), DragDrop.PreviewDragOverEvent);
            Check(over.Effects == DragDropEffects.None, "External drag data accepted");
            over = PreviewDrag(window, (UIElement)root.Header, new DataObject("RestAgent.LibraryNode", "another-session|r:get"), DragDrop.PreviewDragOverEvent);
            Check(over.Effects == DragDropEffects.None, "Another window's drag data accepted");

            var menu = find("get").ContextMenu; menu.PlacementTarget = tree; menu.IsOpen = true;
            window.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            Check(menu.Style == window.FindResource("LibraryContextMenu"), "Context menu style missing");
            foreach (var entry in menu.Items.OfType<MenuItem>()) Check(entry.Style == window.FindResource("LibraryMenuItem") && entry.ActualHeight >= 26, "Context item style/layout missing");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "context-menu-preview.png"))) encoder.Save(output);
            var submenu = menu.Items.OfType<MenuItem>().First(i => i.HasItems); submenu.IsSubmenuOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            var popup = (System.Windows.Controls.Primitives.Popup)submenu.Template.FindName("PART_Popup", submenu);
            Check(popup.IsOpen && ((FrameworkElement)popup.Child).ActualWidth >= 220, "Styled submenu did not open"); submenu.IsSubmenuOpen = false; menu.IsOpen = false;
            Call(controller, "BeginTitleRename"); title.Text = "Редактирование названия"; window.UpdateLayout(); RenderDialog(window, "navigation-preview.png"); Call(controller, "FinishTitleRename", false);
            Call(controller, "ToggleSidebar"); window.UpdateLayout(); RenderDialog(window, "sidebar-hidden-preview.png"); Call(controller, "ToggleSidebar");
            Call(controller, "Load", new Request()); Call(controller, "BeginTitleRename"); title.Text = "New request name"; PreviewKey(window, title, System.Windows.Input.Key.Enter);
            Check(((Request)Call(controller, "Capture")).Name == "New request name", "Unsaved header rename failed");
            Check(Data.Read(path).Requests.Count == original.Requests.Count, "Header rename created a saved request");
            Call(controller, "Persist", original); Call(controller, "Load", original.Requests.First(r => r.Id == "get")); Call(controller, "RefreshLibrary");
            column.Width = new GridLength(280); window.UpdateLayout(); ((System.Windows.Controls.GridSplitter)window.FindName("SidebarSplitter")).RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            report.Add("PASS navigation: sidebar resize/hide/restore and responsive limits, persisted header rename/Enter/Escape/empty name, Delete editing request/folder text, routed drag/drop request/folder/root/cycle/external data, unsaved editor preserved, styled context menu/submenu");
        }
        static void TestCodeEditors(Controller controller, Window window, List<string> report)
        {
            var outgoing = (CodeEditor)window.FindName("Body"); var response = (CodeEditor)window.FindName("ResponseBody");
            string source = "{\"orderId\":1.2500,\"customer\":\"Привет \\\" } [\",\"items\":[{\"enabled\":true,\"quantity\":2},null]}";
            var request = new Request { Name = "Подсветка и блоки", Method = "POST", Body = source };
            var mime = new Pair { Name = "Content-Type", Value = "application/json" }; request.Headers.Add(mime);
            Call(controller, "Load", request); outgoing.RefreshSyntax();
            Check(outgoing.SyntaxLanguage == "JSON" && !outgoing.IsReadOnly && response.IsReadOnly, "Editor modes/read-only failed");
            outgoing.FormatText(); string pretty = outgoing.Text;
            Check(pretty.Contains("1.2500") && outgoing.FoldingCount == 3, "JSON number/string-safe nested folding failed");
            Check(((Request)Call(controller, "Capture")).Body == pretty, "Highlighted request not captured");
            outgoing.Undo(); Check(outgoing.Text == source, "Formatting destroyed undo");
            response.Text = pretty; response.SetContentType("application/problem+json");
            using (var highlighter = new ICSharpCode.AvalonEdit.Highlighting.DocumentHighlighter(response.Document, response.SyntaxHighlighting))
            {
                var colors = new HashSet<string>();
                for (int line = 1; line <= response.Document.LineCount; line++)
                    foreach (var section in highlighter.HighlightLine(line).Sections) colors.Add(section.Color.Name);
                Check(colors.Contains("Key") && colors.Contains("String") && colors.Contains("Number") && colors.Contains("Literal"), "Missing JSON token colors: " + string.Join(",", colors));
            }
            ((Button)window.FindName("CollapseResponse")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(response.AllCollapsed && response.Text == pretty, "Collapse changed full response text");
            Check((string)Call(controller, "ResponseText") == pretty, "Copy/save source lost collapsed content");
            ((Button)window.FindName("ExpandResponse")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!response.AllCollapsed && response.Text == pretty, "Expand changed response");
            var tags = (System.Collections.ObjectModel.ObservableCollection<Pair>)((DataGrid)window.FindName("Headers")).ItemsSource;
            tags[0].Value = "application/xml"; outgoing.Text = "<orders><order id=\"1\"><name>Привет</name></order></orders>"; outgoing.RefreshSyntax();
            Check(outgoing.SyntaxLanguage == "XML", "Changed request Content-Type ignored");
            outgoing.FormatText(); response.Text = outgoing.Text; response.SetContentType("application/xml");
            Check(response.SyntaxHighlighting != null && response.FoldingCount >= 2, "XML highlighting/folding failed");
            response.FoldAll(true); Check(response.AllCollapsed, "XML collapse failed"); response.FoldAll(false);
            string mixed = "<p><b>a</b> <i>b</i></p>";
            Check(CodeEditor.PrettyXml(mixed).Contains("</b> <i>"), "XML mixed-content whitespace lost");
            response.Text = "<broken>\n<item>"; response.RefreshSyntax(); // Partial XML remains readable.
            response.Text = "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///not-a-real-file'>]><a>&x;</a>"; response.RefreshSyntax();
            foreach (string language in new[] { "HTML", "JavaScript", "CSS", "SQL" })
            {
                response.SetMode(language); Check(response.SyntaxHighlighting != null, "Missing highlighting " + language);
            }
            response.SetMode("Текст"); Check(response.SyntaxHighlighting == null && response.FoldingCount == 0, "Plain text mode kept highlighting/folds");
            response.SetMode("Авто"); response.SetContentType("text/plain"); response.Text = "Обычный текст"; response.RefreshSyntax();
            Check(response.SyntaxLanguage == "Текст" && !((Button)window.FindName("CollapseResponse")).IsEnabled, "Plain response has enabled folding");
            response.Text = "{\n\"large\":\"" + new string('x', 2 * 1024 * 1024) + "\"\n}"; response.SetContentType("application/json");
            Check(response.FoldingCount == 0, "Large response folding scan not limited");

            Call(controller, "Load", request); outgoing.FormatText();
            ((TabControl)window.FindName("RequestTabs")).SelectedIndex = 2;
            response.Text = pretty; response.SetContentType("application/json");
            Call(controller, "SetResponseStatus", "200 OK", Controller.StatusColor(200));
            ((TextBlock)window.FindName("ResponseMeta")).Text = "107 мс  •  JSON UTF-8";
            ((TextBlock)window.FindName("RequestAddress")).Text = "POST https://example.com/orders";
            ((TextBlock)window.FindName("RequestAddress")).Visibility = Visibility.Visible;
            foreach (int width in new[] { 1000, 1280, 1920 })
            {
                window.Width = width; window.UpdateLayout();
                foreach (string name in new[] { "ResponseTop", "ResponseBottom", "CollapseResponse", "ExpandResponse", "GroupResponse", "FormatResponse", "CopyResponse", "SaveResponse" })
                {
                    var button = (Button)window.FindName(name); var panel = (FrameworkElement)button.Parent;
                    var origin = button.TranslatePoint(new Point(), panel);
                    Check(button.ActualHeight <= 30 && origin.X >= 0 && origin.X + button.ActualWidth <= panel.ActualWidth + 1, "Response toolbar clipped at " + width);
                }
            }
            window.Width = 1280; window.UpdateLayout(); RenderDialog(window, "code-preview.png");
            response.FoldAll(true); window.UpdateLayout(); RenderDialog(window, "code-collapsed-preview.png"); response.FoldAll(false);
            Call(controller, "SetResponseStatus", "500 Internal Server Error", Controller.StatusColor(500));
            window.UpdateLayout(); RenderDialog(window, "code-error-preview.png");
            Call(controller, "Load", new Request());
            report.Add("PASS code editors: JSON token colors, XML/HTML/JS/CSS/SQL, Content-Type changes, nested folds/string escaping, full copy/save source with collapsed content, request capture/undo, XML mixed content/invalid input, large response, toolbar at 1000/1280/1920");
        }
        static List<Button> GroupArrows(CodeEditor editor)
        {
            editor.TextArea.TextView.EnsureVisualLines();
            return editor.TextArea.TextView.VisualLines.SelectMany(line => line.Elements)
                .OfType<ICSharpCode.AvalonEdit.Rendering.InlineObjectElement>()
                .SelectMany(element => ((Panel)element.Element).Children.OfType<Button>()).ToList();
        }
        static void TestJsonGroups(Controller controller, Window window, List<string> report)
        {
            var response = (CodeEditor)window.FindName("ResponseBody");
            var manager = (ICSharpCode.AvalonEdit.Folding.FoldingManager)typeof(CodeEditor).GetField("folding", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(response);
            string[] orders = Enumerable.Range(0, 8).Select(index => "{\"id\":" + (16628764 + index) + ",\"date_created\":\"2016-05-17T07:41:21.298558+00:00\",\"date_modified\":\"2016-06-13T06:42:00+00:00\",\"name\":\"Заказ  Набори\",\"number\":1.2500,\"customer\":{\"name\":\"Привет\"},\"items\":[{\"sku\":\"A-1\"}]}" ).ToArray();
            string json = Data.Pretty("{\"orders\":[" + string.Join(",", orders) + "],\"total\":8,\"empty\":{},\"empty_array\":[]}", true);
            Call(controller, "Load", new Request { Name = "Группы JSON • Заказы", Url = "https://example.com/orders" });
            ((TabControl)window.FindName("RequestTabs")).SelectedIndex = 2;
            ((CodeEditor)window.FindName("Body")).Text = "";
            response.Text = json; response.SetMode("Авто"); response.SetContentType("application/json");
            Check(response.CanGroupJson && ((Button)window.FindName("GroupResponse")).IsEnabled, "JSON group view unavailable for orders");
            ((Button)window.FindName("GroupResponse")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!manager.AllFoldings.First().IsFolded && !manager.AllFoldings.ElementAt(1).IsFolded, "Grouping hid root or orders array");
            Check(manager.AllFoldings.Count(section => section.IsFolded && section.Title.StartsWith("\"id\":")) == 8, "Orders not grouped individually");
            Check(manager.AllFoldings.First(section => section.Title.StartsWith("\"id\":")).Title.Contains("\"date_created\""), "Group preview lost order fields");
            Check(manager.AllFoldings.First(section => section.Title.StartsWith("\"id\":")).Title.Contains("Заказ  Набори"), "Preview changed whitespace inside a string");
            Check(response.Text == json && (string)Call(controller, "ResponseText") == json, "Grouping changed copied/saved JSON");
            Call(controller, "SetResponseStatus", "200 OK", Controller.StatusColor(200));
            ((TextBlock)window.FindName("ResponseMeta")).Text = "JSON • 8 заказов";
            ((TextBlock)window.FindName("RequestAddress")).Text = "GET https://example.com/orders";
            window.UpdateLayout(); RenderDialog(window, "json-groups-preview.png");
            var arrows = GroupArrows(response);
            Check(arrows.Count >= 4, "Inline JSON arrows not rendered");
            var orderArrow = arrows.First(button => ((ICSharpCode.AvalonEdit.Folding.FoldingSection)button.Tag).Title.StartsWith("\"id\": 16628764"));
            var order = (ICSharpCode.AvalonEdit.Folding.FoldingSection)orderArrow.Tag;
            orderArrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            Check(!order.IsFolded && manager.AllFoldings.Count(section => section.IsFolded && section.Title.StartsWith("\"id\":")) == 7, "Arrow did not expand one order independently");
            RenderDialog(window, "json-group-expanded-preview.png");
            GroupArrows(response).First(button => button.Tag == order).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(order.IsFolded && response.Text == json, "Arrow did not collapse order or changed text");
            response.RefreshSyntax(); Check(order.IsFolded, "Syntax refresh reset group expansion");
            response.FoldAll(true); window.UpdateLayout(); Check(GroupArrows(response).Count == 1, "Collapsed root kept child arrows visible");
            GroupArrows(response).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!manager.AllFoldings.First().IsFolded, "Root arrow failed to expand");
            response.FoldAll(false); Check(manager.AllFoldings.All(section => !section.IsFolded), "Expand all left groups collapsed");
            response.Text = "[{\"text\":\"a\\\" } [ b\",\"n\":1.2500},{\"id\":2},null,{},[]]"; response.RefreshSyntax();
            Check(response.FoldingCount == 3 && response.CanGroupJson, "Single-line/escaped strings/empty groups failed");
            response.GroupJson(); Check(manager.AllFoldings.Count(section => section.IsFolded) == 2, "Root array grouping failed");
            response.Text = "{\"value\":\"" + new string('x', 10000) + "\"}"; response.RefreshSyntax();
            Check(manager.AllFoldings.Single().Title.Length <= 243 && manager.AllFoldings.Single().Title.EndsWith(" …"), "Long preview not bounded");
            string emojiPreview = CodeEditor.CompactJsonPreview(new string('x', 239) + char.ConvertFromUtf32(0x1F600), 0, 241);
            Check(!emojiPreview.Contains("\ud83d"), "Truncated preview left a lone surrogate");
            response.SetMode("Текст"); window.UpdateLayout(); Check(GroupArrows(response).Count == 0 && !((Button)window.FindName("GroupResponse")).IsEnabled, "Text mode retained group UI");
            response.SetMode("Авто"); response.SetContentType("application/json"); response.Text = new string(' ', 2 * 1024 * 1024) + "[{}]"; response.RefreshSyntax();
            Check(!response.CanGroupJson && !((Button)window.FindName("GroupResponse")).IsEnabled, "Large JSON enabled grouping");
            response.Text = json; response.RefreshSyntax(); response.GroupJson();
            report.Add("PASS JSON groups: inline arrow expand/collapse, orders previews and independent groups, root/nested arrays, empty and one-line values, escaped braces, string whitespace/numeric precision, bounded preview, full copy/save source, refresh persistence, plain/large document limits");
        }
        // Временный localhost-сервер проверяет фактически переданные URL, заголовки и тело,
        // затем возвращает заданный ответ. Доступ к внешним API для этого теста не нужен.
        static void TestRequest(Controller controller, Window window, string method, int status, string body, List<string> report, bool? withBody = null, string contentType = "application/json")
        {
            bool sendBody = withBody ?? method == "POST";
            string outgoingBody = sendBody ? "{\"test\":true}" : "";
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(delegate
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 5000;
                    var bytes = new List<byte>(); int next;
                    while ((next = stream.ReadByte()) >= 0)
                    {
                        bytes.Add((byte)next); int n = bytes.Count;
                        if (n >= 4 && bytes[n-4] == 13 && bytes[n-3] == 10 && bytes[n-2] == 13 && bytes[n-1] == 10) break;
                    }
                    string received = Encoding.UTF8.GetString(bytes.ToArray());
                    string expectedTarget = "/api?existing=1&search=a%26%20b&createdAt=2026-10-09%2000%3A00%3A00&createdTo=2026-10-09%2023%3A59%3A59&uzdStatus=1&page=2&tag%5B%5D=one&tag%5B%5D=two";
                    Check(received.StartsWith(method + " " + expectedTarget + " "), "Incorrect request line: " + received);
                    Check(received.Contains("X-API-Key: test-key"), "Header absent");
                    Check(received.IndexOf("Content-Type: application/json", StringComparison.OrdinalIgnoreCase) >= 0, "Content-Type lost");
                    Check(received.IndexOf("Content-Language: uk", StringComparison.OrdinalIgnoreCase) >= 0, "Content-Language lost");
                    int length = 0;
                    foreach (string line in received.Split(new[] { "\r\n" }, StringSplitOptions.None)) if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                    var payload = new byte[length]; int offset = 0;
                    while (offset < length) { int count = stream.Read(payload, offset, length - offset); if (count == 0) break; offset += count; }
                    Check(Encoding.UTF8.GetString(payload) == outgoingBody, "Unexpected request payload");
                    if (method == "GET" || method == "HEAD") Check(length == 0 && received.IndexOf("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) < 0, "Body framing attached to GET/HEAD");
                    byte[] responseBody = Encoding.UTF8.GetBytes(body);
                    byte[] responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + (status == 200 ? " OK" : " Unauthorized") + "\r\nContent-Type: " + contentType + "; charset=utf-8\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(responseHeaders, 0, responseHeaders.Length); if (method != "HEAD") stream.Write(responseBody, 0, responseBody.Length);
                }
            });
            try
            {
                var request = new Request { Method = method, Url = "http://127.0.0.1:" + port + "/api?page=1&existing=1", Body = outgoingBody, Timeout = 5 };
                request.Headers.Add(new Pair { Name = "X-API-Key", Value = "test-key" });
                request.Headers.Add(new Pair { Name = "Content-Type", Value = "application/json" });
                request.Headers.Add(new Pair { Name = "Content-Language", Value = "uk" });
                request.Queries.Add(new Pair { Name = "search", Value = "a& b" });
                request.Queries.Add(new Pair { Name = "createdAt", Value = "2026-10-09 00:00:00" });
                request.Queries.Add(new Pair { Name = "createdTo", Value = "2026-10-09 23:59:59" });
                request.Queries.Add(new Pair { Name = "status", Value = "new", Enabled = false });
                request.Queries.Add(new Pair { Name = "uzdStatus", Value = "1" });
                request.Queries.Add(new Pair { Name = "page", Value = "2" });
                request.Queries.Add(new Pair { Name = "tag[]", Value = "one" });
                request.Queries.Add(new Pair { Name = "tag[]", Value = "two" });
                typeof(Controller).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { request });
                var task = (Task)typeof(Controller).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                timer.Tick += delegate { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                Check(task.IsCompleted, "Request timed out in test"); task.GetAwaiter().GetResult();
                Check(((TextBlock)window.FindName("RequestAddress")).Text == method + " " + ((TextBox)window.FindName("Url")).Text, "Visible address differs from sent address");
                Check(((TextBlock)window.FindName("ResponseStatus")).Text.StartsWith(status.ToString()), "Unexpected status: " + ((CodeEditor)window.FindName("ResponseBody")).Text);
                if (method == "HEAD") Check(((CodeEditor)window.FindName("ResponseBody")).Text == "", "HEAD response body must be empty");
                else if (contentType.Contains("json")) Check(Data.Serializer().Serialize(Data.Serializer().DeserializeObject(((CodeEditor)window.FindName("ResponseBody")).Text)) == Data.Serializer().Serialize(Data.Serializer().DeserializeObject(body)), "Response body mismatch");
                if (body == UnicodeResponse())
                {
                    string displayed = ((CodeEditor)window.FindName("ResponseBody")).Text;
                    Check(displayed.Contains("Наборы") && displayed.Contains("Набори") && !displayed.Contains(@"\u041d"), "HTTP response Unicode not decoded in editor");
                    Check((string)Call(controller, "ResponseText") == displayed, "Copy/save source differs from readable response");
                    window.UpdateLayout(); RenderDialog(window, "unicode-response-preview.png");
                    report.Add("PASS HTTP escaped Unicode: readable response editor and full copy/save source");
                }
                if (method != "HEAD" && contentType.Contains("xml"))
                {
                    var expectedXml = new System.Xml.XmlDocument(); expectedXml.LoadXml(body);
                    var actualXml = new System.Xml.XmlDocument(); actualXml.LoadXml(((CodeEditor)window.FindName("ResponseBody")).Text);
                    Check(actualXml.OuterXml == expectedXml.OuterXml, "XML response changed");
                }
                if (contentType == "text/html") Check(((CodeEditor)window.FindName("ResponseBody")).Text == body, "HTML response changed");
                Color color = ((SolidColorBrush)((Border)window.FindName("ResponseStatusBand")).Background).Color;
                Check(status >= 400 ? color.R > color.G : status < 300 ? color.G > color.R : color.R > color.G && color.G > color.B, "Wrong status background color");
                Check(((TextBlock)window.FindName("ResponseStatus")).Foreground == Brushes.White, "Status contrast missing");
                Check(((CodeEditor)window.FindName("ResponseBody")).SyntaxLanguage == CodeEditor.Detect(body, contentType), "Response Content-Type detection failed");
                Check(server.Wait(6000), "Server did not finish");
                report.Add("PASS HTTP " + method + ": wire URL dates/status/arrays/merge, visible address, Content-Type/Content-Language, " + (sendBody ? "payload" : "no request body") + ", " + status + " response");
            }
            finally { listener.Stop(); }
        }
        static void TestInterrupted(Controller controller, Window window, bool cancel, List<string> report)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(delegate
            {
                using (var client = listener.AcceptTcpClient())
                {
                    System.Threading.Thread.Sleep(1600);
                }
            });
            try
            {
                var request = new Request { Url = "http://127.0.0.1:" + port + "/wait", Timeout = cancel ? 5 : 1 };
                typeof(Controller).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { request });
                var task = (Task)typeof(Controller).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                var frame = new DispatcherFrame(); var started = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; bool cancelled = false;
                timer.Tick += delegate
                {
                    if (cancel && !cancelled && (DateTime.UtcNow - started).TotalMilliseconds > 150)
                    {
                        cancelled = true;
                        var source = (System.Threading.CancellationTokenSource)typeof(Controller).GetField("cancellation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller); source.Cancel();
                    }
                    if (task.IsCompleted || (DateTime.UtcNow - started).TotalSeconds > 8) frame.Continue = false;
                };
                timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Check(task.IsCompleted, "Interruption failed"); task.GetAwaiter().GetResult();
                Check(((TextBlock)window.FindName("ResponseStatus")).Text == (cancel ? "Запрос отменён" : "Истёк таймаут"), "Unexpected interruption status");
                var interruptionColor = ((SolidColorBrush)((Border)window.FindName("ResponseStatusBand")).Background).Color;
                Check(cancel ? interruptionColor.B > interruptionColor.R : interruptionColor.R > interruptionColor.G, "Interruption kept previous success background");
                Check(((Button)window.FindName("Send")).IsEnabled && !((Button)window.FindName("Cancel")).IsEnabled, "Buttons not restored");
                Check(server.Wait(5000), "Slow server failed"); report.Add("PASS HTTP " + (cancel ? "cancellation" : "timeout") + " and UI recovery");
            }
            finally { listener.Stop(); }
        }
    }
}
