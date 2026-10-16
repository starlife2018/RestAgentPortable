using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace RestAgent
{
    // Библиотека редактора встроена в EXE: рядом с portable-приложением не нужна отдельная DLL.
    static class EmbeddedDependencies
    {
        static Assembly editorAssembly;
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                return new AssemblyName(e.Name).Name == "ICSharpCode.AvalonEdit" ? editorAssembly : null;
            };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ICSharpCode.AvalonEdit.dll"))
            using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); editorAssembly = Assembly.Load(buffer.ToArray()); }
        }
    }

    // Общий редактор тела запроса и ответа: язык, подсветка, форматирование и сворачивание.
    public sealed class CodeEditor : TextEditor
    {
        readonly DispatcherTimer refreshTimer;
        readonly FoldingManager folding;
        readonly ThemeInkTransformer themeInk;
        readonly HashSet<int> jsonArrayItems = new HashSet<int>();
        string mode = "Авто", contentType = "";
        public string SyntaxLanguage { get; private set; }
        public event EventHandler SyntaxChanged;
        public int FoldingCount { get { return folding.AllFoldings.Count(); } }
        public bool AllCollapsed { get { return FoldingCount > 0 && folding.AllFoldings.All(f => f.IsFolded); } }
        public bool CanGroupJson { get { return SyntaxLanguage == "JSON" && jsonArrayItems.Count > 0; } }
        static IHighlightingDefinition jsonHighlighting;
        public static readonly string[] Modes = { "Авто", "JSON", "XML", "HTML", "JavaScript", "CSS", "SQL", "Текст" };

        public CodeEditor()
        {
            FontFamily = new FontFamily("Consolas"); FontSize = 13;
            SetResourceReference(BackgroundProperty, "InputBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
            ShowLineNumbers = true; SetResourceReference(LineNumbersForegroundProperty, "MutedBrush");
            themeInk = new ThemeInkTransformer(this); TextArea.TextView.LineTransformers.Add(themeInk);
            Padding = new Thickness(6); BorderThickness = new Thickness(0);
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Options.IndentationSize = 2; Options.ConvertTabsToSpaces = true;
            folding = FoldingManager.Install(TextArea);
            TextArea.TextView.ElementGenerators.Insert(0, new JsonGroupGenerator(this, folding));
            // Ждём короткую паузу во вводе, чтобы не пересчитывать блоки при каждом символе.
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            refreshTimer.Tick += delegate { RefreshSyntax(); };
            TextChanged += delegate { refreshTimer.Stop(); refreshTimer.Start(); };
            Unloaded += delegate { refreshTimer.Stop(); };
            Loaded += delegate { RefreshSyntax(); };
        }

        // Заменяем TextBox-заглушки из XAML, сохраняя имена для window.FindName в контроллере.
        public static void Install(Window window)
        {
            foreach (string name in new[] { "Body", "ResponseBody" })
            {
                var old = (TextBox)window.FindName(name);
                var editor = new CodeEditor { Name = name, IsReadOnly = old.IsReadOnly, Margin = old.Margin };
                var panel = old.Parent as Panel;
                if (panel != null)
                {
                    int index = panel.Children.IndexOf(old); panel.Children.RemoveAt(index); panel.Children.Insert(index, editor);
                    Grid.SetRow(editor, Grid.GetRow(old)); Grid.SetColumn(editor, Grid.GetColumn(old));
                }
                else ((ContentControl)old.Parent).Content = editor;
                window.UnregisterName(name); window.RegisterName(name, editor);
            }
        }
        public void SetMode(string value) { mode = value ?? "Авто"; RefreshSyntax(); }
        public void RefreshTheme()
        {
            TextArea.TextView.LineTransformers.Remove(themeInk); TextArea.TextView.LineTransformers.Add(themeInk);
            TextArea.TextView.Redraw();
        }
        public void SetContentType(string value) { contentType = value ?? ""; RefreshSyntax(); }
        // Сначала доверяем Content-Type; если он не распознан, проверяем начало текста.
        public static string Detect(string text, string mime)
        {
            mime = (mime ?? "").Split(';')[0].Trim().ToLowerInvariant();
            if (mime.Contains("json")) return "JSON";
            if (mime.Contains("html")) return "HTML";
            if (mime.Contains("xml")) return "XML";
            if (mime.Contains("javascript") || mime.Contains("ecmascript")) return "JavaScript";
            if (mime == "text/css") return "CSS";
            if (mime.Contains("sql")) return "SQL";
            string start = (text ?? "").TrimStart();
            if (start.StartsWith("{") || start.StartsWith("[")) return "JSON";
            if (start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "HTML";
            if (start.StartsWith("<")) return "XML";
            return "Текст";
        }
        static IHighlightingDefinition JsonHighlighting()
        {
            if (jsonHighlighting == null)
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Json.xshd"))
                using (var reader = XmlReader.Create(stream)) jsonHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            return jsonHighlighting;
        }
        public void RefreshSyntax()
        {
            refreshTimer.Stop(); SyntaxLanguage = mode == "Авто" ? Detect(Text, contentType) : mode;
            SyntaxHighlighting = SyntaxLanguage == "JSON" ? JsonHighlighting() : SyntaxLanguage == "Текст" ? null : HighlightingManager.Instance.GetDefinition(SyntaxLanguage == "SQL" ? "TSQL" : SyntaxLanguage);
            RefreshTheme();
            // Подсветка отрисовывается по видимым строкам, а поиск блоков читает весь документ.
            // Для текста больше 2 МиБ отключаем этот поиск, чтобы не задерживать интерфейс.
            var folds = new List<NewFolding>();
            if (Document.TextLength <= 2 * 1024 * 1024)
            {
                if (SyntaxLanguage == "XML" || SyntaxLanguage == "HTML")
                {
                    int error;
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersFromEntities = 1024 };
                    using (var reader = XmlReader.Create(Document.CreateReader(), settings))
                        folds.AddRange(new XmlFoldingStrategy().CreateNewFoldings(Document, reader, out error));
                }
                else if (SyntaxLanguage == "JSON" || SyntaxLanguage == "JavaScript" || SyntaxLanguage == "CSS") folds = BraceFoldings(Text, SyntaxLanguage == "JSON");
            }
            jsonArrayItems.Clear();
            if (SyntaxLanguage == "JSON")
            {
                // Запоминаем блоки непосредственно внутри массивов: кнопка «Группы»
                // свернёт эти элементы независимо, оставив массив и внешний объект раскрытыми.
                var parents = new Stack<NewFolding>();
                foreach (var section in folds)
                {
                    while (parents.Count > 0 && parents.Peek().EndOffset <= section.StartOffset) parents.Pop();
                    if (parents.Count > 0 && Document.GetCharAt(parents.Peek().StartOffset - 1) == '[') jsonArrayItems.Add(section.StartOffset);
                    parents.Push(section);
                }
            }
            folding.UpdateFoldings(folds, -1);
            foreach (var margin in TextArea.LeftMargins.OfType<FoldingMargin>()) margin.Visibility = SyntaxLanguage == "JSON" ? Visibility.Collapsed : Visibility.Visible;
            TextArea.TextView.Redraw();
            var changed = SyntaxChanged; if (changed != null) changed(this, EventArgs.Empty);
        }

        // Скобки внутри строк и комментариев не задают границы блока.
        // Стек хранит позиции открывающих скобок, чтобы находить вложенные пары за один проход.
        public static List<NewFolding> BraceFoldings(string text, bool json = false)
        {
            var result = new List<NewFolding>(); var stack = new Stack<int>();
            char quote = '\0'; bool escaped = false, lineComment = false, blockComment = false;
            int line = 0; var lines = new Dictionary<int, int>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i], next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '\n') { line++; lineComment = false; }
                if (lineComment) continue;
                if (blockComment) { if (c == '*' && next == '/') { blockComment = false; i++; } continue; }
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'' || c == '`') { quote = c; continue; }
                if (c == '/' && next == '/') { lineComment = true; i++; continue; }
                if (c == '/' && next == '*') { blockComment = true; i++; continue; }
                if (c == '{' || c == '[') { stack.Push(i); lines[i] = line; }
                else if ((c == '}' || c == ']') && stack.Count > 0)
                {
                    int start = stack.Peek(); if ((text[start] == '{') != (c == '}')) continue;
                    stack.Pop();
                    if (json)
                    {
                        string preview = CompactJsonPreview(text, start + 1, i);
                        if (preview.Length > 0) result.Add(new NewFolding(start + 1, i) { Name = preview });
                    }
                    else if (line > lines[start]) result.Add(new NewFolding(start + 1, i) { Name = " … " });
                }
            }
            result.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset)); return result;
        }
        public void FoldAll(bool collapse)
        {
            RefreshSyntax(); foreach (var section in folding.AllFoldings) section.IsFolded = collapse;
        }
        public void GroupJson()
        {
            RefreshSyntax();
            if (!CanGroupJson) return;
            foreach (var section in folding.AllFoldings) section.IsFolded = jsonArrayItems.Contains(section.StartOffset);
            ScrollToHome();
        }
        // Превью ограничено по длине и объёму сканирования даже для огромной строки JSON.
        // Убираем лишние пробелы только вне строк; исходный документ остаётся неизменным.
        internal static string CompactJsonPreview(string text, int start, int end)
        {
            const int limit = 240, scanLimit = 1024;
            var preview = new StringBuilder(); bool quoted = false, escaped = false, space = false;
            int i = start;
            for (; i < end && i - start < scanLimit && preview.Length < limit; i++)
            {
                char c = text[i];
                if (!quoted && char.IsWhiteSpace(c)) { space = preview.Length > 0; continue; }
                if (space) { preview.Append(' '); space = false; }
                preview.Append(c);
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
            }
            if (i < end)
            {
                // Не оставляем половину пары UTF-16 при обрезке эмодзи или другого символа.
                if (preview.Length > 0 && char.IsHighSurrogate(preview[preview.Length - 1])) preview.Length--;
                preview.Append(" …");
            }
            return preview.ToString();
        }

        // Добавляем стрелку и превью на уровне отрисовки, не заменяя текст документа.
        // Штатный механизм AvalonEdit продолжает учитывать скрытые строки и позиции курсора.
        sealed class JsonGroupGenerator : VisualLineElementGenerator
        {
            readonly CodeEditor editor;
            readonly FoldingManager manager;
            static readonly ControlTemplate arrowTemplate = CreateArrowTemplate();
            public JsonGroupGenerator(CodeEditor editor, FoldingManager manager) { this.editor = editor; this.manager = manager; }
            public override int GetFirstInterestedOffset(int startOffset)
            {
                if (editor.SyntaxLanguage != "JSON") return -1;
                var section = manager.GetNextFolding(startOffset + 1);
                return section == null ? -1 : section.StartOffset - 1;
            }
            public override VisualLineElement ConstructElement(int offset)
            {
                var section = manager.GetFoldingsAt(offset + 1).FirstOrDefault();
                if (section == null || offset >= editor.Document.TextLength) return null;
                char bracket = editor.Document.GetCharAt(offset);
                if (bracket != '{' && bracket != '[') return null;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var arrow = new Button
                {
                    Width = 16, Height = 16, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(0),
                    Style = new Style(typeof(Button)), Template = arrowTemplate, Cursor = Cursors.Hand, Focusable = false,
                    ToolTip = section.IsFolded ? "Развернуть группу" : "Свернуть группу", Tag = section,
                    Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(section.IsFolded ? "M 0,0 L 0,8 L 5,4 Z" : "M 0,0 L 8,0 L 4,5 Z"), Fill = (Brush)editor.FindResource("MutedBrush") }
                };
                System.Windows.Automation.AutomationProperties.SetName(arrow, (section.IsFolded ? "Развернуть" : "Свернуть") + " группу JSON");
                arrow.Click += delegate { section.IsFolded = !section.IsFolded; };
                row.Children.Add(arrow);
                row.Children.Add(new TextBlock { Text = bracket.ToString(), FontFamily = editor.FontFamily, FontSize = editor.FontSize, Foreground = (Brush)editor.FindResource("MutedBrush") });
                if (section.IsFolded)
                {
                    row.Children.Add(new TextBlock { Text = section.Title, FontFamily = editor.FontFamily, FontSize = editor.FontSize, Foreground = (Brush)editor.FindResource("MutedBrush"), Margin = new Thickness(2, 0, 2, 0) });
                    row.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) { section.IsFolded = false; e.Handled = true; } };
                }
                return new InlineObjectElement(section.IsFolded ? section.EndOffset - offset : 1, row);
            }
            static ControlTemplate CreateArrowTemplate()
            {
                var border = new FrameworkElementFactory(typeof(Border), "Frame");
                border.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
                var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
                template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Frame") } });
                return template;
            }
        }
        public static string PrettyXml(string text)
        {
            // Сохраняем значимые пробелы и смешанное содержимое XML.
            // DTD и загрузка внешних сущностей запрещены: форматирование не обращается к сети.
            var document = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.None })) document.Save(writer);
            string declaration = text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) ? text.TrimStart().Substring(0, text.TrimStart().IndexOf("?>", StringComparison.Ordinal) + 2) + Environment.NewLine : "";
            return declaration + output;
        }
        public void FormatText()
        {
            RefreshSyntax(); string formatted;
            if (SyntaxLanguage == "JSON") formatted = Data.Pretty(Text, true);
            else if (SyntaxLanguage == "XML") formatted = PrettyXml(Text);
            else throw new ArgumentException("Форматирование доступно для JSON и XML. Выберите нужный формат над текстом.");
            // Замена через Document сохраняет возможность отменить форматирование в редакторе.
            Document.Replace(0, Document.TextLength, formatted); RefreshSyntax();
        }
    }
}
