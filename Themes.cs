using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RestAgent
{
    // Осветляем цвета подсветки на тёмном фоне до контраста 4.5:1, если это возможно.
    // Меняется только отрисовка: текст и общие определения языков остаются прежними.
    sealed class ThemeInkTransformer : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
    {
        readonly CodeEditor editor;
        public ThemeInkTransformer(CodeEditor editor) { this.editor = editor; }
        protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        {
            var background = editor.Background as SolidColorBrush; var text = editor.Foreground as SolidColorBrush;
            if (background == null || text == null || ThemeService.Luminance(background.Color) > 0.25) return;
            double bg = ThemeService.Luminance(background.Color);
            ChangeLinePart(line.Offset, line.EndOffset, delegate(ICSharpCode.AvalonEdit.Rendering.VisualLineElement element)
            {
                var ink = element.TextRunProperties.ForegroundBrush as SolidColorBrush;
                if (ink == null || ink.Color == text.Color) return;
                Color candidate = ink.Color;
                for (double amount = 0; amount <= 1; amount += 0.1)
                {
                    candidate = ThemeService.Mix(ink.Color, Colors.White, amount);
                    double luminance = ThemeService.Luminance(candidate);
                    if ((Math.Max(luminance, bg) + 0.05) / (Math.Min(luminance, bg) + 0.05) >= 4.5) break;
                }
                element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(candidate));
            });
        }
    }
    // Палитра хранится строками #RRGGBB: её удобно редактировать и сохранять в JSON.
    public sealed class AppTheme
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Background { get; set; }
        public string Surface { get; set; }
        public string Input { get; set; }
        public string Text { get; set; }
        public string Muted { get; set; }
        public string Border { get; set; }
        public string Accent { get; set; }
        public string Sidebar { get; set; }
        public string SidebarText { get; set; }
        public string SidebarMuted { get; set; }
        public string SidebarHover { get; set; }
        public string SidebarSelected { get; set; }
        public AppTheme()
        {
            Background = "#F1F3F6"; Surface = "#FFFFFF"; Input = "#FFFFFF"; Text = "#263347"; Muted = "#7A8798"; Border = "#DCE2EA"; Accent = "#3F6D83";
            Sidebar = "#20242C"; SidebarText = "#F2F4F8"; SidebarMuted = "#A0A8B7"; SidebarHover = "#2A303B"; SidebarSelected = "#394251";
        }
        public override string ToString() { return Name; }
    }
    // В файле хранятся выбранная тема и пользовательские палитры; шаблоны задаются кодом.
    public sealed class ThemeStore
    {
        public int Version { get; set; }
        public string ActiveThemeId { get; set; }
        public List<AppTheme> CustomThemes { get; set; }
        public ThemeStore() { Version = 1; ActiveThemeId = "light"; CustomThemes = new List<AppTheme>(); }
    }
    // Разделяет временный предпросмотр (Preview) и запись подтверждённых настроек (Commit).
    public sealed class ThemeService
    {
        public static readonly string[] ColorKeys = { "Background", "Surface", "Input", "Text", "Muted", "Border", "Accent", "Sidebar", "SidebarText", "SidebarMuted", "SidebarHover", "SidebarSelected" };
        public static readonly string[] ColorLabels = { "Основной фон", "Панели и меню", "Поля и редактор кода", "Основной текст", "Вторичный текст", "Разделители", "Акцент", "Фон левой панели", "Текст левой панели", "Вторичный текст панели", "Наведение в дереве", "Выделение в дереве" };
        readonly Window window;
        readonly string path;
        ThemeStore store = new ThemeStore();
        public AppTheme Current { get; private set; }
        public string LoadError { get; private set; }
        public event EventHandler Applied;
        public ThemeService(Window window, string path)
        {
            this.window = window; this.path = path;
            try
            {
                if (File.Exists(path))
                {
                    var saved = Data.Serializer().Deserialize<ThemeStore>(File.ReadAllText(path));
                    ValidateStore(saved); store = saved;
                }
            }
            catch (Exception ex) { LoadError = "Не удалось прочитать файл тем. Исходный файл сохранён: " + ex.Message; }
            Preview(AllThemes().FirstOrDefault(t => t.Id == store.ActiveThemeId) ?? Templates()[0]);
        }
        // Каждый вызов создаёт новые объекты: редактор не может изменить исходные шаблоны.
        public static List<AppTheme> Templates()
        {
            return new List<AppTheme> {
                new AppTheme { Id = "light", Name = "Светлая" },
                new AppTheme { Id = "graphite", Name = "Графит", Background = "#191C22", Surface = "#222730", Input = "#1D222A", Text = "#E8EDF5", Muted = "#A1ADBE", Border = "#39414F", Accent = "#4EAAC7", Sidebar = "#17191F", SidebarText = "#F0F3F8", SidebarMuted = "#A0A9B9", SidebarHover = "#282D36", SidebarSelected = "#3A4658" },
                new AppTheme { Id = "ocean", Name = "Океан", Background = "#EAF2F5", Surface = "#F9FDFF", Input = "#FFFFFF", Text = "#193747", Muted = "#607E8E", Border = "#C8DBE4", Accent = "#137A99", Sidebar = "#132F3D", SidebarText = "#E5F5FC", SidebarMuted = "#9EC3D3", SidebarHover = "#1E4355", SidebarSelected = "#2B6074" },
                new AppTheme { Id = "forest", Name = "Лес", Background = "#EDF2EF", Surface = "#FBFDFC", Input = "#FFFFFF", Text = "#253D33", Muted = "#6C8176", Border = "#CFDED5", Accent = "#367B61", Sidebar = "#1B2E26", SidebarText = "#ECF5EF", SidebarMuted = "#A9C3B3", SidebarHover = "#294337", SidebarSelected = "#375A47" }
            };
        }
        public List<AppTheme> AllThemes() { return Templates().Concat(Data.Clone(store.CustomThemes)).ToList(); }
        public ThemeStore Snapshot() { return Data.Clone(store); }
        public static bool TryColor(string text, out Color color)
        {
            color = Colors.Transparent; text = (text ?? "").Trim();
            if (text.Length != 7 || text[0] != '#') return false;
            uint value;
            if (!uint.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
            color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value); return true;
        }
        public static void ValidateTheme(AppTheme theme)
        {
            if (theme == null || string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Length > 80) throw new ArgumentException("Укажите название темы (до 80 символов).");
            for (int i = 0; i < ColorKeys.Length; i++)
            {
                Color color; if (!TryColor((string)typeof(AppTheme).GetProperty(ColorKeys[i]).GetValue(theme, null), out color)) throw new ArgumentException(ColorLabels[i] + ": укажите цвет в формате #RRGGBB.");
            }
        }
        static void ValidateStore(ThemeStore data)
        {
            if (data == null || data.Version != 1 || data.CustomThemes == null || data.CustomThemes.Count > 100) throw new ArgumentException("Неподдерживаемый файл тем.");
            var ids = new HashSet<string>(); var names = new HashSet<string>(Templates().Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var theme in data.CustomThemes)
            {
                ValidateTheme(theme);
                if (!theme.Id.StartsWith("custom-", StringComparison.Ordinal) || !ids.Add(theme.Id) || !names.Add(theme.Name)) throw new ArgumentException("Повторяющееся название или идентификатор темы.");
            }
            if (!Templates().Concat(data.CustomThemes).Any(t => t.Id == data.ActiveThemeId)) throw new ArgumentException("Активная тема не найдена.");
        }
        // После ошибки чтения не затираем повреждённый файл; исправные данные заменяем
        // через .tmp с резервной копией. Состояние в памяти обновляется после записи.
        public void Commit(ThemeStore next)
        {
            ValidateStore(next);
            if (LoadError != null) throw new IOException(LoadError);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".tmp", Data.Pretty(Data.Serializer().Serialize(next)), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak"); else File.Move(path + ".tmp", path);
            store = Data.Clone(next); Preview(AllThemes().First(t => t.Id == store.ActiveThemeId));
        }
        static Color Read(string hex) { Color value; TryColor(hex, out value); return value; }
        public static Color Mix(Color first, Color second, double amount)
        { return Color.FromRgb((byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount)); }
        // Относительная яркость в линейном sRGB используется для выбора контрастного текста.
        public static double Luminance(Color c)
        {
            Func<byte, double> channel = b => b / 255.0 <= 0.04045 ? b / 3294.6 : Math.Pow((b / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * channel(c.R) + 0.7152 * channel(c.G) + 0.0722 * channel(c.B);
        }
        // Заменяем кисти в ресурсах окна: DynamicResource обновляет уже созданные элементы.
        // Предпросмотр не пишет на диск; событие Applied обновляет подсветку редакторов.
        public void Preview(AppTheme theme)
        {
            ValidateTheme(theme); Current = Data.Clone(theme);
            Color surface = Read(theme.Surface), text = Read(theme.Text), accent = Read(theme.Accent), sidebar = Read(theme.Sidebar);
            var colors = new Dictionary<string, Color> {
                { "AppBackgroundBrush", Read(theme.Background) }, { "SurfaceBrush", surface }, { "InputBrush", Read(theme.Input) }, { "TextBrush", text }, { "MutedBrush", Read(theme.Muted) }, { "BorderBrush", Read(theme.Border) },
                { "AccentBrush", accent }, { "AccentTextBrush", Luminance(accent) > 0.4 ? Colors.Black : Colors.White },
                { "ButtonBrush", Mix(surface, text, 0.075) }, { "HoverBrush", Mix(surface, accent, 0.15) }, { "HeaderBrush", Mix(surface, text, 0.04) }, { "AlternateBrush", Mix(surface, text, 0.02) },
                { "SidebarBrush", sidebar }, { "SidebarTextBrush", Read(theme.SidebarText) }, { "SidebarMutedBrush", Read(theme.SidebarMuted) }, { "SidebarHoverBrush", Read(theme.SidebarHover) }, { "SidebarSelectedBrush", Read(theme.SidebarSelected) },
                { "SidebarBorderBrush", Mix(sidebar, Read(theme.SidebarText), 0.16) }, { "DangerBrush", Luminance(surface) < 0.2 ? Read("#FF8796") : Read("#C04150") }
            };
            foreach (var pair in colors) { var brush = new SolidColorBrush(pair.Value); brush.Freeze(); window.Resources[pair.Key] = brush; }
            var handler = Applied; if (handler != null) handler(this, EventArgs.Empty);
        }
    }
    public sealed partial class Controller
    {
        ThemeService themes;
        void InitializeThemes()
        {
            themes = new ThemeService(window, Path.Combine(Path.GetDirectoryName(dataPath), "themes.json"));
            themes.Applied += delegate { Control<CodeEditor>("Body").RefreshTheme(); Control<CodeEditor>("ResponseBody").RefreshTheme(); };
            Control<CodeEditor>("Body").RefreshTheme(); Control<CodeEditor>("ResponseBody").RefreshTheme();
            if (themes.LoadError != null) Notice(themes.LoadError);
        }
    }
}
