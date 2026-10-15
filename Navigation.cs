using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace RestAgent
{
    public sealed class ViewSettings
    {
        public double SidebarWidth { get; set; }
        public bool SidebarHidden { get; set; }
        public ViewSettings() { SidebarWidth = 280; }
    }

    public sealed partial class Controller
    {
        const string LibraryDragFormat = "RestAgent.LibraryNode";
        readonly string dragSession = Guid.NewGuid().ToString("N");
        readonly ViewSettings viewSettings = new ViewSettings();
        bool titleRenaming;
        object dragCandidate;
        Point dragOrigin;
        TreeViewItem dropHighlight;
        DispatcherTimer expandDropTimer;
        DateTime lastDragScroll;

        static T Ancestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                var match = node as T; if (match != null) return match;
                node = node is Visual || node is Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
        static T Descendant<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i); var match = child as T;
                if (match != null) return match;
                match = Descendant<T>(child); if (match != null) return match;
            }
            return null;
        }
        static bool IsTextEditing(DependencyObject source)
        {
            return Ancestor<TextBoxBase>(source) != null || Ancestor<PasswordBox>(source) != null;
        }
        void InitializeNavigation()
        {
            Bind("ToggleSidebar", ToggleSidebar);
            Bind("RenameTitle", BeginTitleRename);
            Control<TextBlock>("RequestTitle").MouseLeftButtonDown += delegate { BeginTitleRename(); };
            var title = Control<TextBox>("RequestTitleEditor");
            title.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Escape) { e.Handled = true; FinishTitleRename(e.Key == Key.Enter); }
            };
            title.LostKeyboardFocus += delegate { FinishTitleRename(true); };
            string settingsPath = Path.Combine(Path.GetDirectoryName(dataPath), "view-settings.json");
            try
            {
                if (File.Exists(settingsPath))
                {
                    var saved = Data.Serializer().Deserialize<ViewSettings>(File.ReadAllText(settingsPath));
                    if (saved != null && !double.IsNaN(saved.SidebarWidth) && !double.IsInfinity(saved.SidebarWidth))
                    {
                        viewSettings.SidebarWidth = Math.Max(220, Math.Min(520, saved.SidebarWidth)); viewSettings.SidebarHidden = saved.SidebarHidden;
                    }
                }
            }
            catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            ApplySidebar(); Control<Grid>("WorkspaceLayout").SizeChanged += delegate { ApplySidebar(); };
            Control<GridSplitter>("SidebarSplitter").DragCompleted += delegate
            {
                var column = (ColumnDefinition)window.FindName("SidebarColumn");
                viewSettings.SidebarWidth = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
                ApplySidebar(); SaveViewSettings();
            };
            InitializeLibraryDrag();
            Control<Grid>("TitleRow").SizeChanged += delegate { Control<TextBlock>("RequestTitle").MaxWidth = Math.Max(20, Control<Grid>("TitleRow").ActualWidth - 38); };
        }
        void ApplySidebar()
        {
            var sidebar = (ColumnDefinition)window.FindName("SidebarColumn");
            var splitter = (ColumnDefinition)window.FindName("SplitterColumn");
            double width = Control<Grid>("WorkspaceLayout").ActualWidth;
            if (width <= 0) width = window.Width - 16;
            sidebar.MinWidth = viewSettings.SidebarHidden ? 0 : 220;
            sidebar.MaxWidth = Math.Max(220, Math.Min(520, width - 706));
            sidebar.Width = new GridLength(viewSettings.SidebarHidden ? 0 : Math.Min(viewSettings.SidebarWidth, sidebar.MaxWidth));
            splitter.Width = new GridLength(viewSettings.SidebarHidden ? 0 : 6);
            Control<Border>("Sidebar").Visibility = viewSettings.SidebarHidden ? Visibility.Collapsed : Visibility.Visible;
            Control<GridSplitter>("SidebarSplitter").Visibility = viewSettings.SidebarHidden ? Visibility.Collapsed : Visibility.Visible;
            Control<Button>("ToggleSidebar").ToolTip = (viewSettings.SidebarHidden ? "Показать" : "Скрыть") + " панель коллекций (Ctrl+B)";
        }
        void ToggleSidebar()
        {
            viewSettings.SidebarHidden = !viewSettings.SidebarHidden; ApplySidebar(); SaveViewSettings();
        }
        void SaveViewSettings()
        {
            string path = Path.Combine(Path.GetDirectoryName(dataPath), "view-settings.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path + ".tmp", Data.Serializer().Serialize(viewSettings));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch (IOException ex) { Notice("Не удалось сохранить размер панели: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Notice("Не удалось сохранить размер панели: " + ex.Message); }
        }
        void BeginTitleRename()
        {
            if (titleRenaming) return; titleRenaming = true;
            var input = Control<TextBox>("RequestTitleEditor"); input.Text = current.Name;
            Control<TextBlock>("RequestTitle").Visibility = Visibility.Collapsed; Control<Button>("RenameTitle").Visibility = Visibility.Collapsed;
            input.Visibility = Visibility.Visible; input.Focus(); input.SelectAll();
        }
        void FinishTitleRename(bool save)
        {
            if (!titleRenaming) return; titleRenaming = false;
            var input = Control<TextBox>("RequestTitleEditor"); string name = input.Text.Trim();
            input.Visibility = Visibility.Collapsed; Control<TextBlock>("RequestTitle").Visibility = Visibility.Visible; Control<Button>("RenameTitle").Visibility = Visibility.Visible;
            if (!save || name == current.Name) return;
            if (name.Length == 0) { Notice("Название не может быть пустым. Сохранено прежнее имя."); return; }
            Safe(delegate
            {
                if (workspace.Requests.Any(r => r.Id == current.Id)) RenameNodeCore(current, name);
                else { current.Name = name; MarkDirty(); }
            });
        }
        MenuItem LibraryMenuItem(string name, string icon, string shortcut)
        {
            return new MenuItem { Header = name, Icon = UiIcons.Create(window, icon), InputGestureText = shortcut, Style = (Style)window.FindResource("LibraryMenuItem") };
        }
        object NodeByKey(string key)
        {
            if (key == null) return null;
            if (key.StartsWith("r:", StringComparison.Ordinal)) return workspace.Requests.FirstOrDefault(r => r.Id == key.Substring(2));
            if (key.StartsWith("f:", StringComparison.Ordinal)) return workspace.Folders.FirstOrDefault(f => f.Id == key.Substring(2));
            return null;
        }
        bool NodeExists(object node) { return NodeByKey(NodeKey(node)) != null; }
        bool CanDropNode(object node, string targetId)
        {
            if (!CanMove(node, targetId)) return false;
            var saved = NodeByKey(NodeKey(node)); var folder = saved as Folder; var request = saved as Request;
            return folder != null ? folder.ParentId != targetId : request != null && request.FolderId != targetId;
        }
        DataObject LibraryDragData(object node)
        {
            var data = new DataObject(); data.SetData(LibraryDragFormat, dragSession + "|" + NodeKey(node)); return data;
        }
        object DragNode(IDataObject data)
        {
            if (!data.GetDataPresent(LibraryDragFormat, false)) return null;
            var payload = data.GetData(LibraryDragFormat, false) as string;
            string prefix = dragSession + "|";
            return payload == null || !payload.StartsWith(prefix, StringComparison.Ordinal) ? null : NodeByKey(payload.Substring(prefix.Length));
        }
        string DropFolder(TreeViewItem item)
        {
            var folder = item == null ? null : item.Tag as Folder; var request = item == null ? null : item.Tag as Request;
            return folder != null ? folder.Id : request != null ? request.FolderId : null;
        }
        void HighlightDrop(TreeViewItem item)
        {
            if (item == dropHighlight) return;
            if (dropHighlight != null)
            {
                var old = dropHighlight.Template.FindName("Row", dropHighlight) as Border;
                if (old != null) { old.ClearValue(Border.BorderBrushProperty); old.ClearValue(Border.BorderThicknessProperty); }
            }
            expandDropTimer.Stop(); dropHighlight = item;
            if (item == null) return;
            var row = item.Template.FindName("Row", item) as Border;
            if (row != null) { row.BorderBrush = new SolidColorBrush(Color.FromRgb(118, 201, 190)); row.BorderThickness = new Thickness(1); }
            if (!item.IsExpanded && item.HasItems) expandDropTimer.Start();
        }
        bool OverHeader(TreeViewItem item, Point point)
        {
            var row = item.Template.FindName("Row", item) as Border;
            return row != null && new Rect(row.TranslatePoint(new Point(), item), row.RenderSize).Contains(point);
        }
        void InitializeLibraryDrag()
        {
            var tree = Control<TreeView>("Library"); tree.AllowDrop = true;
            expandDropTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            expandDropTimer.Tick += delegate { expandDropTimer.Stop(); if (dropHighlight != null) dropHighlight.IsExpanded = true; };
            tree.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                dragCandidate = null; var source = e.OriginalSource as DependencyObject;
                if (IsTextEditing(source) || Ancestor<ButtonBase>(source) != null) return;
                var item = Ancestor<TreeViewItem>(source);
                if (item == null || item.Tag == null || !OverHeader(item, e.GetPosition(item))) return;
                // Select on mouse-up, allowing another item to move without replacing an unsaved editor.
                dragCandidate = item.Tag; dragOrigin = e.GetPosition(tree); e.Handled = true;
            };
            tree.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (dragCandidate == null) return; string key = NodeKey(dragCandidate); dragCandidate = null;
                var item = LibraryNodes(tree.Items).FirstOrDefault(i => NodeKey(i.Tag) == key);
                if (item != null) { item.Focus(); item.IsSelected = true; } e.Handled = true;
            };
            tree.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { dragCandidate = null; return; }
                if (dragCandidate == null) return; Point point = e.GetPosition(tree);
                if (Math.Abs(point.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                object node = dragCandidate; dragCandidate = null;
                try { DragDrop.DoDragDrop(tree, LibraryDragData(node), DragDropEffects.Move); }
                finally { HighlightDrop(null); }
            };
            tree.PreviewDragOver += LibraryDragOver;
            tree.PreviewDrop += LibraryDrop;
            tree.DragLeave += delegate(object sender, DragEventArgs e)
            {
                Point point = e.GetPosition(tree); if (!new Rect(tree.RenderSize).Contains(point)) HighlightDrop(null);
            };
        }
        TreeViewItem DropTarget(DragEventArgs e)
        {
            var tree = Control<TreeView>("Library");
            var item = Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item == null) item = Ancestor<TreeViewItem>(tree.InputHitTest(e.GetPosition(tree)) as DependencyObject);
            string id = DropFolder(item);
            return LibraryNodes(tree.Items).FirstOrDefault(i => id == null ? i.Tag == null : i.Tag is Folder && ((Folder)i.Tag).Id == id);
        }
        void LibraryDragOver(object sender, DragEventArgs e)
        {
            var node = DragNode(e.Data); var target = DropTarget(e); string id = DropFolder(target);
            bool accepted = (e.AllowedEffects & DragDropEffects.Move) != 0 && node != null && CanDropNode(node, id);
            e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
            HighlightDrop(accepted ? target : null);
            if ((DateTime.UtcNow - lastDragScroll).TotalMilliseconds < 80) return;
            var tree = Control<TreeView>("Library"); Point point = e.GetPosition(tree); var scroll = Descendant<ScrollViewer>(tree);
            if (node != null && scroll != null) { if (point.Y < 24) scroll.LineUp(); else if (point.Y > tree.ActualHeight - 24) scroll.LineDown(); lastDragScroll = DateTime.UtcNow; }
        }
        void LibraryDrop(object sender, DragEventArgs e)
        {
            var node = DragNode(e.Data); string id = DropFolder(DropTarget(e)); HighlightDrop(null);
            e.Effects = DragDropEffects.None; e.Handled = true;
            if ((e.AllowedEffects & DragDropEffects.Move) == 0 || node == null || !CanDropNode(node, id)) return;
            Safe(delegate { MoveNode(node, id); e.Effects = DragDropEffects.Move; });
        }
    }
}
