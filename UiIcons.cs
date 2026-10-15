using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RestAgent
{
    public static class UiIcons
    {
        public static Path Create(FrameworkElement owner, string key, double size = 16)
        {
            var path = new Path { Data = (Geometry)owner.FindResource("Icon" + key), Width = size, Height = size, Stretch = Stretch.Uniform, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
            path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
            return path;
        }
        public static StackPanel Label(FrameworkElement owner, string icon, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(Create(owner, icon));
            panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); return panel;
        }
    }
    public sealed partial class Controller
    {
        void InitializeSidebarIcons()
        {
            foreach (var spec in new[] { new[] { "Import", "Import", "Импорт" }, new[] { "Export", "Export", "Экспорт" }, new[] { "NewRequest", "Request", "Запрос" }, new[] { "NewFolder", "Folder", "Каталог" }, new[] { "Settings", "Settings", "Настройки" } })
                Control<Button>(spec[0]).Content = UiIcons.Label(window, spec[1], spec[2]);
            Control<System.Windows.Controls.Primitives.ToggleButton>("HistoryNavigation").Content = UiIcons.Label(window, "History", "История");
            Control<System.Windows.Controls.Primitives.ToggleButton>("CollectionNavigation").Content = UiIcons.Label(window, "Collection", "Коллекция");
        }
    }
}
