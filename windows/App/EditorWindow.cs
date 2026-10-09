using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core;

namespace Compositor.Windows;

public sealed class EditorWindow : Window {
    private readonly EditorSession editor = new();
    private readonly UpdateService updater = new();
    private readonly Dictionary<string, Button> buttons = new();
    private readonly Dictionary<string, MenuItem> menuCommands = new();
    private readonly TextBlock projectTitle = new() { Text = "未命名", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock opacityText = new() { Text = "100%", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private StackPanel welcome = null!;
    private readonly Image canvasImage = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox layerList = new() { MinHeight = 180 };
    private readonly TextBlock status = new() { Text = "新建画布或导入一张图片，开始合成。", Margin = new Thickness(16, 9) };
    private readonly TextBlock updateText = new() { Text = "启动后检查更新", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock documentText = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 12) };
    private readonly TextBox nameBox = new() { PlaceholderText = "图层名称" };
    private readonly CheckBox visibleBox = new() { Content = "显示图层" };
    private readonly Slider opacitySlider = new() { Minimum = 0, Maximum = 100, Value = 100 };
    private readonly ComboBox blendBox = new() { ItemsSource = new[] { "正常", "正片叠底", "滤色", "叠加", "变暗", "变亮", "差值", "排除" }, SelectedIndex = 0 };
    private readonly NumericUpDown positionX = Number(-1_000_000, 1_000_000, 0);
    private readonly NumericUpDown positionY = Number(-1_000_000, 1_000_000, 0);
    private readonly string[] blendKeys = ["Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "Difference", "Exclusion"];
    private List<LayerData> shownLayers = [];
    private bool refreshing, busy, closingApproved, dragging;
    private Point pointerStart;
    private double layerStartX, layerStartY, zoom = 1;
    private Bitmap? preview;
    private readonly Dictionary<Guid, Bitmap> thumbnails = new();
    internal EditorWindow() {
        Title = "Compositor · Windows 中文版"; Width = 1280; Height = 820; MinWidth = 940; MinHeight = 620;
        Background = Gray("#242424"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 12;
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WindowsIcon"); Icon = new WindowIcon(iconStream!);
        layerList.ItemTemplate = new FuncDataTemplate<LayerData>((layer, _) => {
            if (layer == null) return new TextBlock();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,42,*"), Height = 52 };
            row.Children.Add(new TextBlock { Text = layer.Visible ? "◉" : "○", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });
            Control thumbnail;
            if (layer.Png is byte[] png) {
                if (!thumbnails.TryGetValue(layer.Id, out var bitmap)) { using var input = new MemoryStream(png); thumbnails[layer.Id] = bitmap = Bitmap.DecodeToWidth(input, 36); }
                thumbnail = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            } else thumbnail = new TextBlock { Text = layer.IsGroup ? "▱" : "□", FontSize = 24, VerticalAlignment = VerticalAlignment.Center };
            var frame = new Border { Child = thumbnail, Width = 36, Height = 36, BorderBrush = Gray("#555555"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(frame, 1); row.Children.Add(frame);
            var name = new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0) };
            Grid.SetColumn(name, 2); row.Children.Add(name); return row;
        });
        var root = new Grid { RowDefinitions = new RowDefinitions("24,42,42,*,30") };
        var menu = new Menu { Background = Gray("#292929"), FontSize = 12 };
        menu.ItemsSource = new[] {
            new MenuItem { Header = "文件", ItemsSource = new[] {
                MenuAction("新建", NewCanvas, "Ctrl+N"), MenuAction("打开", OpenProject, "Ctrl+O"), MenuAction("导入图片", ImportImage),
                MenuAction("保存", () => SaveProject(), "Ctrl+S"), MenuAction("另存为", () => SaveProject(true)), MenuAction("导出 PNG", Export) } },
            new MenuItem { Header = "编辑", ItemsSource = new[] {
                MenuAction("撤销", () => { editor.Undo(); Refresh(); return Task.CompletedTask; }, "Ctrl+Z"),
                MenuAction("重做", () => { editor.Redo(); Refresh(); return Task.CompletedTask; }, "Ctrl+Y") } },
            new MenuItem { Header = "帮助", ItemsSource = new[] {
                MenuAction("检查更新", CheckUpdate), MenuAction("下载更新", UpdateAction),
                InfoMenu("关于 Windows 中文版", "界面参照原版 Compositor。当前支持基础栅格图层、PNG/JPEG 和 .comp 基础子集；文字、蒙版、滤镜等工具仍待适配。") } }
        };
        root.Children.Add(menu);
        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,Auto"), Background = Gray("#2B2B2B") };
        var create = new Button { Content = "+", FontSize = 22, Width = 36, Height = 32, Padding = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(create, "新建画布 (Ctrl+N)"); create.Click += async (_, _) => await Dispatch("新建"); titlebar.Children.Add(create);
        var tab = new Border { Background = Gray("#363636"), CornerRadius = new CornerRadius(5), Padding = new Thickness(18, 6), Margin = new Thickness(16, 5), HorizontalAlignment = HorizontalAlignment.Center, Child = projectTitle };
        Grid.SetColumn(tab, 1); titlebar.Children.Add(tab);
        var zoomControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 4) };
        zoomControls.Children.Add(ActionButton("适应窗口", () => { Fit(); Refresh(); return Task.CompletedTask; }));
        var actual = new Button { Content = "100%", Padding = new Thickness(8, 3), MinHeight = 28 }; actual.Click += (_, _) => SetZoom(1); zoomControls.Children.Add(actual);
        zoomControls.Children.Add(ActionButton("缩小", () => { SetZoom(zoom / 1.25); return Task.CompletedTask; }, "−"));
        zoomControls.Children.Add(ActionButton("放大", () => { SetZoom(zoom * 1.25); return Task.CompletedTask; }, "+"));
        Grid.SetColumn(zoomControls, 2); titlebar.Children.Add(zoomControls); Grid.SetRow(titlebar, 1); root.Children.Add(titlebar);
        var transform = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center };
        transform.Children.Add(new TextBlock { Text = "移动", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        transform.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center }); positionX.Width = 90; positionX.Height = 28; transform.Children.Add(positionX);
        transform.Children.Add(new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center }); positionY.Width = 90; positionY.Height = 28; transform.Children.Add(positionY);
        transform.Children.Add(ActionButton("应用位置", () => { editor.MoveActive((double)(positionX.Value ?? 0), (double)(positionY.Value ?? 0)); Refresh(); return Task.CompletedTask; }));
        transform.Children.Add(new TextBlock { Text = "px", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        var transformBar = new Border { Name = "TransformBar", Height = 42, Background = Gray("#242424"), BorderBrush = Gray("#3C3C3C"), BorderThickness = new Thickness(0, 1, 0, 1), Child = transform };
        Grid.SetRow(transformBar, 2); root.Children.Add(transformBar);
        var workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,252") };
        var tools = new StackPanel { Spacing = 10, Margin = new Thickness(10, 16, 10, 12) };
        var move = ToolIcon("移动 (V)", "M9,1 L9,17 M1,9 L17,9 M9,1 L6,4 M9,1 L12,4 M9,17 L6,14 M9,17 L12,14 M1,9 L4,6 M1,9 L4,12 M17,9 L14,6 M17,9 L14,12", true);
        move.Background = Gray("#424242"); tools.Children.Add(move);
        foreach (var (name, path) in new[] {
            ("矩形选框", "M2,2 H16 V16 H2 Z"), ("套索", "M3,5 C1,2 17,0 17,7 C18,15 1,16 2,8 C3,5 7,10 4,17"),
            ("魔棒", "M3,16 L14,5 M10,2 V5 M15,0 V3 M17,5 H14"), ("裁剪", "M4,0 V14 H18 M0,4 H14 V18"),
            ("画笔", "M5,11 L13,1 L17,5 L8,14 M5,11 C0,10 4,14 1,17 C5,17 10,17 8,14"),
            ("修复", "M2,12 L12,2 C16,-1 20,3 17,6 L7,16 C3,20 -1,16 2,12 M6,8 L12,14"),
            ("仿制图章", "M5,10 H13 L12,6 C15,0 3,0 6,6 Z M2,12 H16 V16 H2 Z"),
            ("模糊", "M9,1 C9,1 2,9 2,12 C2,20 16,20 16,12 C16,9 9,1 9,1 Z"),
            ("渐变", "M1,3 H17 V15 H1 Z M5,3 V15 M8,3 V15 M11,3 V15"),
            ("形状", "M2,2 H16 V16 H2 Z"), ("文字", "M2,3 H16 M9,3 V17 M5,17 H13"),
            ("吸管", "M11,1 L17,7 M14,4 L4,14 L1,17 L4,17 L14,7"),
            ("抓手", "M4,9 V5 C4,3 6,3 6,5 V9 V2 C6,0 9,0 9,2 V8 V3 C9,1 12,1 12,3 V9 V5 C12,3 15,3 15,5 V12 C15,20 4,20 2,12 L0,9 C0,7 2,7 4,9")
        }) tools.Children.Add(ToolIcon(name + "（尚未适配）", path, false));
        var zoomTool = ToolIcon("放大", "M13,7 A6,6 0 1 1 1,7 A6,6 0 1 1 13,7 M12,12 L18,18 M4,7 H10 M7,4 V10", true);
        zoomTool.Click += (_, _) => SetZoom(zoom * 1.25); tools.Children.Add(zoomTool);
        var rail = new Border { Name = "ToolRail", Width = 56, BorderBrush = Gray("#3C3C3C"), BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = tools } };
        workspace.Children.Add(rail);
        var canvas = new Grid();
        canvas.Children.Add(new ScrollViewer { Content = canvasImage, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        welcome = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        welcome.Children.Add(new TextBlock { Text = "新建画布", FontSize = 24, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center });
        welcome.Children.Add(new TextBlock { Text = "创建画布或打开项目，开始合成。", Foreground = Brushes.Gray });
        var welcomeActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var key in new[] { "新建", "打开", "导入图片" }) { var button = new Button { Content = key }; button.Click += async (_, _) => await Dispatch(key); welcomeActions.Children.Add(button); }
        welcome.Children.Add(welcomeActions); canvas.Children.Add(welcome); Grid.SetColumn(canvas, 1); workspace.Children.Add(canvas);
        var panel = new Grid { RowDefinitions = new RowDefinitions("48,Auto,*,Auto,42"), Background = Gray("#292929") };
        panel.Children.Add(new Border { BorderBrush = Gray("#3C3C3C"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 0), Child = new TextBlock { Text = "图层", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } });
        var appearance = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        var blendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*") }; blendRow.Children.Add(new TextBlock { Text = "混合", VerticalAlignment = VerticalAlignment.Center }); blendBox.HorizontalAlignment = HorizontalAlignment.Stretch; blendBox.MinHeight = 28; Grid.SetColumn(blendBox, 1); blendRow.Children.Add(blendBox); appearance.Children.Add(blendRow);
        var opacityRow = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,42") }; opacityRow.Children.Add(new TextBlock { Text = "不透明度", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(opacitySlider, 1); opacityRow.Children.Add(opacitySlider); Grid.SetColumn(opacityText, 2); opacityRow.Children.Add(opacityText); appearance.Children.Add(opacityRow);
        Grid.SetRow(appearance, 1); panel.Children.Add(appearance); layerList.MinHeight = 0; layerList.Background = Brushes.Transparent; layerList.BorderThickness = new Thickness(0); Grid.SetRow(layerList, 2); panel.Children.Add(layerList);
        var details = new StackPanel { Spacing = 6, Margin = new Thickness(12, 8) }; details.Children.Add(nameBox); details.Children.Add(visibleBox); Grid.SetRow(details, 3); panel.Children.Add(details);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(ActionButton("上移", () => { editor.ReorderActive(1); Refresh(); return Task.CompletedTask; }, "↑"));
        footer.Children.Add(ActionButton("下移", () => { editor.ReorderActive(-1); Refresh(); return Task.CompletedTask; }, "↓"));
        footer.Children.Add(ActionButton("删除", () => { editor.DeleteActive(); Refresh(); return Task.CompletedTask; }, "删除")); Grid.SetRow(footer, 4); panel.Children.Add(footer);
        var side = new Border { Name = "LayersPanel", Width = 252, BorderBrush = Gray("#3C3C3C"), BorderThickness = new Thickness(1, 0, 0, 0), Child = panel };
        Grid.SetColumn(side, 2); workspace.Children.Add(side); Grid.SetRow(workspace, 3); root.Children.Add(workspace);
        var statusContents = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*,300"), Margin = new Thickness(18, 0) };
        documentText.FontSize = 11; documentText.Margin = new Thickness(0); documentText.VerticalAlignment = VerticalAlignment.Center; statusContents.Children.Add(documentText);
        status.Margin = new Thickness(6, 0); status.FontSize = 11; status.VerticalAlignment = VerticalAlignment.Center; status.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(status, 1); statusContents.Children.Add(status);
        updateText.FontSize = 11; updateText.Foreground = Brushes.Gray; updateText.TextWrapping = TextWrapping.NoWrap; updateText.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(updateText, 2); statusContents.Children.Add(updateText);
        var statusBar = new Border { Name = "StatusBar", Height = 30, BorderBrush = Gray("#3C3C3C"), BorderThickness = new Thickness(0, 1, 0, 0), Child = statusContents };
        Grid.SetRow(statusBar, 4); root.Children.Add(statusBar); Content = root;
        layerList.SelectionChanged += (_, _) => { if (!refreshing && layerList.SelectedIndex >= 0 && layerList.SelectedIndex < shownLayers.Count) { editor.Select(shownLayers[layerList.SelectedIndex].Id); Refresh(false); } };
        visibleBox.IsCheckedChanged += (_, _) => { if (!refreshing) { editor.SetVisible(visibleBox.IsChecked == true); Refresh(); } };
        opacitySlider.PointerCaptureLost += (_, _) => { if (!refreshing) { editor.SetOpacity(opacitySlider.Value / 100); Refresh(); } };
        opacitySlider.AddHandler(PointerReleasedEvent, (_, _) => { if (!refreshing) { editor.SetOpacity(opacitySlider.Value / 100); Refresh(); } }, RoutingStrategies.Bubble, true);
        opacitySlider.KeyUp += (_, _) => { if (!refreshing) { editor.SetOpacity(opacitySlider.Value / 100); Refresh(); } };
        blendBox.SelectionChanged += (_, _) => { if (!refreshing && blendBox.SelectedIndex >= 0) { editor.SetBlend(blendKeys[blendBox.SelectedIndex]); Refresh(); } };
        nameBox.LostFocus += (_, _) => { if (!refreshing) { editor.Rename(nameBox.Text ?? ""); Refresh(false); } };
        canvasImage.PointerPressed += (_, e) => { if (busy || editor.Active == null || editor.Active.IsGroup || !e.GetCurrentPoint(canvasImage).Properties.IsLeftButtonPressed) return; pointerStart = e.GetPosition(canvasImage); layerStartX = editor.Active.OriginX; layerStartY = editor.Active.OriginY; dragging = true; e.Pointer.Capture(canvasImage); };
        canvasImage.PointerMoved += (_, e) => { if (!dragging) return; var p = e.GetPosition(canvasImage); status.Text = $"移动到 X {layerStartX + (p.X - pointerStart.X) / zoom:0}，Y {layerStartY + (p.Y - pointerStart.Y) / zoom:0}；松开应用"; };
        canvasImage.PointerReleased += (_, e) => { if (!dragging) return; dragging = false; var p = e.GetPosition(canvasImage); editor.MoveActive(layerStartX + (p.X - pointerStart.X) / zoom, layerStartY + (p.Y - pointerStart.Y) / zoom); e.Pointer.Capture(null); Refresh(); };
        canvasImage.PointerCaptureLost += (_, _) => dragging = false;
        KeyDown += async (_, e) => {
            if (e.KeyModifiers != KeyModifiers.Control || busy || FocusManager?.GetFocusedElement() is TextBox or NumericUpDown) return;
            string? action = e.Key switch { Key.S => "保存", Key.Z => "撤销", Key.Y => "重做", Key.O => "打开", Key.N => "新建", _ => null };
            if (action != null && buttons[action].IsEnabled) { e.Handled = true; await Dispatch(action); }
        };
        Closing += (_, e) => { if (!closingApproved && editor.Dirty) { e.Cancel = true; _ = CloseWithSave(); } };
        Closed += (_, _) => { preview?.Dispose(); foreach (var bitmap in thumbnails.Values) bitmap.Dispose(); };
        Opened += async (_, _) => {
            Refresh();
            var args = Program.Arguments;
            if (args.Length >= 2 && args[0] == "--ui-gates") { await RunUiGates(args[1]); return; }
            if (args.Length >= 2 && args[0] is "--upgrade-gate" or "--upgrade-local-gate") { await RunUpgradeGate(args[1]); return; }
            await CheckUpdate();
        };
        actions = new() { ["新建"] = NewCanvas, ["打开"] = OpenProject, ["导入图片"] = ImportImage, ["保存"] = () => SaveProject(), ["另存为"] = () => SaveProject(true), ["导出 PNG"] = Export, ["撤销"] = () => { editor.Undo(); Refresh(); return Task.CompletedTask; }, ["重做"] = () => { editor.Redo(); Refresh(); return Task.CompletedTask; } };
    }
    private readonly Dictionary<string, Func<Task>> actions;
    private Task Dispatch(string key) => Run(actions[key]);
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Increment = 1, FormatString = "0", ShowButtonSpinner = false };
    private Button ActionButton(string name, Func<Task> action, string? label = null) {
        var button = new Button { Content = label ?? name, Padding = new Thickness(8, 3), MinHeight = 28 };
        ToolTip.SetTip(button, name); buttons.Add(name, button); button.Click += async (_, _) => await Run(action); return button;
    }
    private static SolidColorBrush Gray(string color) => new(Color.Parse(color));
    private MenuItem MenuAction(string name, Func<Task> action, string? gesture = null) {
        ActionButton(name, action);
        var item = new MenuItem { Header = name, InputGesture = gesture == null ? null : KeyGesture.Parse(gesture) };
        item.Click += async (_, _) => await Run(action); menuCommands.Add(name, item); return item;
    }
    private MenuItem InfoMenu(string name, string message) {
        var item = new MenuItem { Header = name }; item.Click += async (_, _) => await Confirm(name, message, "确定"); return item;
    }
    private static Button ToolIcon(string name, string geometry, bool enabled) {
        var icon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(geometry), Stroke = Brushes.LightGray, StrokeThickness = 1.4, Width = 18, Height = 18, Stretch = Stretch.Uniform };
        var button = new Button { Content = icon, Width = 36, Height = 36, Padding = new Thickness(9), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, IsEnabled = enabled, Opacity = enabled ? 1 : .28, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, name); Avalonia.Automation.AutomationProperties.SetName(button, name); return button;
    }
    private async Task Run(Func<Task> action) {
        if (busy) return;
        busy = true; Refresh(false);
        try { await action(); }
        catch (OperationException e) { status.Text = e.UserMessage; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException) { status.Text = "操作失败：" + e.Message; }
        finally { busy = false; Refresh(false); }
    }
    private void Refresh(bool render = true) {
        refreshing = true;
        try {
            var doc = editor.Document; var active = editor.Active;
            Title = (editor.Dirty ? "● " : "") + (editor.ProjectPath is string p ? Path.GetFileName(p) : "未命名") + " · Compositor Windows 中文版";
            documentText.Text = doc == null ? "准备就绪" : $"{zoom:P0}   {doc.Width} × {doc.Height} px   sRGB";
            projectTitle.Text = (editor.Dirty ? "● " : "") + (editor.ProjectPath is string project ? Path.GetFileName(project) : "未命名");
            welcome.IsVisible = doc == null; opacityText.Text = $"{(active?.Opacity ?? 1) * 100:0}%";
            shownLayers = doc?.Layers.AsEnumerable().Reverse().ToList() ?? [];
            foreach (var id in thumbnails.Keys.Where(id => shownLayers.All(l => l.Id != id)).ToArray()) { thumbnails[id].Dispose(); thumbnails.Remove(id); }
            layerList.ItemsSource = shownLayers;
            layerList.SelectedIndex = shownLayers.FindIndex(l => l.Id == active?.Id);
            nameBox.Text = active?.Name ?? ""; visibleBox.IsChecked = active?.Visible ?? true;
            opacitySlider.Value = (active?.Opacity ?? 1) * 100; blendBox.SelectedIndex = Array.IndexOf(blendKeys, active?.Blend ?? "Normal");
            positionX.Value = (decimal)(active?.OriginX ?? 0); positionY.Value = (decimal)(active?.OriginY ?? 0);
            nameBox.IsEnabled = visibleBox.IsEnabled = opacitySlider.IsEnabled = active != null && !busy;
            blendBox.IsEnabled = positionX.IsEnabled = positionY.IsEnabled = active != null && !active.IsGroup && !busy;
            foreach (var key in new[] { "保存", "另存为", "导出 PNG", "缩小", "放大", "适应窗口" }) buttons[key].IsEnabled = doc != null && !busy;
            foreach (var key in new[] { "新建", "打开", "导入图片", "检查更新" }) buttons[key].IsEnabled = !busy;
            foreach (var key in new[] { "应用位置", "上移", "下移", "删除" }) buttons[key].IsEnabled = active != null && !busy;
            buttons["撤销"].IsEnabled = editor.CanUndo && !busy; buttons["重做"].IsEnabled = editor.CanRedo && !busy;
            buttons["下载更新"].IsEnabled = updater.Available != null && !busy;
            foreach (var (key, item) in menuCommands) { item.IsEnabled = buttons[key].IsEnabled; item.Header = buttons[key].Content; item.IsVisible = key != "下载更新" || buttons[key].IsVisible; }
            ToolTip.SetTip(status, status.Text); ToolTip.SetTip(updateText, updateText.Text);
            if (render && doc != null) {
                using var stream = new MemoryStream(Raster.Render(doc, 1400, checkerboard: true));
                var next = new Bitmap(stream); canvasImage.Source = next; preview?.Dispose(); preview = next;
            }
            if (doc != null) { canvasImage.Width = doc.Width * zoom; canvasImage.Height = doc.Height * zoom; }
        } finally { refreshing = false; }
    }
    private void Fit() { if (editor.Document is { } doc) zoom = Math.Clamp(Math.Min((Math.Max(640, Width) - 370) / doc.Width, (Math.Max(480, Height) - 220) / doc.Height), .05, 4); }
    private void SetZoom(double value) { zoom = Math.Clamp(value, .05, 8); Refresh(false); }
    private async Task NewCanvas() {
        if (!await ConfirmSaved()) return;
        var width = Number(1, 30000, 1280); var height = Number(1, 30000, 720);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "画布宽度（像素）" }); panel.Children.Add(width);
        panel.Children.Add(new TextBlock { Text = "画布高度（像素）" }); panel.Children.Add(height);
        var window = Dialog("新建画布", panel); var ok = new Button { Content = "创建画布", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => window.Close(true); panel.Children.Add(ok);
        if (await window.ShowDialog<bool>(this)) { editor.New((int)(width.Value ?? 1280), (int)(height.Value ?? 720)); Fit(); Refresh(); status.Text = "画布已创建，可导入图片。"; }
    }
    private async Task OpenProject() {
        if (!await ConfirmSaved()) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 .comp 项目目录", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is string path) { editor.Open(path); Fit(); Refresh(); status.Text = "项目已打开。"; }
    }
    private async Task ImportImage() {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入 PNG/JPEG 图片", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not string path) return;
        var bytes = await File.ReadAllBytesAsync(path); var imported = Raster.Import(bytes);
        if (editor.Document == null) editor.New(imported.Width, imported.Height);
        editor.AddImage(bytes, Path.GetFileNameWithoutExtension(path)); Fit(); Refresh(); status.Text = "图片已添加为图层。";
    }
    private async Task<bool> SaveProject(bool asNew = false) {
        if (editor.Document == null) return false;
        var path = asNew ? null : editor.ProjectPath;
        if (path == null) {
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择保存项目的父目录", AllowMultiple = false });
            if (folder.FirstOrDefault()?.TryGetLocalPath() is not string parent) return false;
            var name = new TextBox { Text = "未命名.comp" }; var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "项目名（保存为 .comp 目录包）" }); panel.Children.Add(name);
            var dialog = Dialog("保存项目", panel); var ok = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right }; ok.Click += (_, _) => dialog.Close(true); panel.Children.Add(ok);
            if (!await dialog.ShowDialog<bool>(this)) return false;
            var filename = (name.Text ?? "").Trim();
            if (filename.Length == 0 || filename is "." or ".." || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new OperationException(ErrorCode.SaveFailed, "请输入有效项目名。");
            if (!filename.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) filename += ".comp";
            path = Path.Combine(parent, filename);
            if (Directory.Exists(path) && !await Confirm("覆盖项目", "此位置已有项目。确认覆盖吗？", "覆盖")) return false;
        }
        editor.Save(path); Refresh(); status.Text = "项目已保存：" + path; return true;
    }
    private async Task Export() {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出透明 PNG", SuggestedFileName = "合成图片.png", DefaultExtension = "png", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }] });
        if (file?.TryGetLocalPath() is string path) { await File.WriteAllBytesAsync(path, editor.ExportPng()); status.Text = "PNG 已导出；项目修改仍需单独保存。"; }
    }
    private static Window Dialog(string title, Control content) => new() { Title = title, Content = content, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
    private async Task<bool> Confirm(string title, string message, string accept) {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 }; panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var window = Dialog(title, panel); var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => window.Close(false); var ok = new Button { Content = accept }; ok.Click += (_, _) => window.Close(true); row.Children.Add(cancel); row.Children.Add(ok); panel.Children.Add(row);
        return await window.ShowDialog<bool>(this);
    }
    private async Task<bool> ConfirmSaved() {
        if (!editor.Dirty) return true;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 }; panel.Children.Add(new TextBlock { Text = "当前文档尚未保存。请选择保存、放弃修改或取消操作。", TextWrapping = TextWrapping.Wrap });
        var window = Dialog("有未保存修改", panel); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, value) in new[] { ("保存后继续", 1), ("放弃修改", 2), ("取消", 0) }) { var b = new Button { Content = label }; b.Click += (_, _) => window.Close(value); row.Children.Add(b); } panel.Children.Add(row);
        var result = await window.ShowDialog<int>(this); return result == 2 || result == 1 && await SaveProject();
    }
    private async Task CloseWithSave() { try { if (await ConfirmSaved()) { closingApproved = true; Close(); } } catch (OperationException e) { status.Text = e.UserMessage; } }
    private async Task CheckUpdate() {
        if (!updater.Manager.IsInstalled) { updateText.Text = "开发运行：安装 Setup 版本后可自动更新。"; return; }
        try {
            updateText.Text = "正在检查更新…"; await updater.Check();
            updateText.Text = updater.Available == null ? "当前已是最新 Windows 中文版。" : $"发现新版本 {updater.Available.TargetFullRelease.Version}，可下载后安装。";
            buttons["下载更新"].IsVisible = updater.Available != null; buttons["下载更新"].Content = "下载更新";
        } catch (OperationException e) { updateText.Text = e.UserMessage; buttons["下载更新"].IsVisible = false; }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException) { updateText.Text = Messages.For(ErrorCode.NetworkUnavailable); }
        Refresh(false);
    }
    private async Task UpdateAction() {
        if (updater.Ready) {
            if (editor.Dirty && !await SaveProject()) { updateText.Text = "更新已下载；请先保存当前项目，再安装。"; return; }
            if (!await Confirm("安装更新", "文档已保存。现在关闭软件、安装更新并重新启动吗？", "安装并重启")) return;
            await updater.Install();
        } else {
            updateText.Text = "正在下载并校验更新…";
            await updater.Download(p => Dispatcher.UIThread.Post(() => updateText.Text = $"正在下载并校验更新：{p}%"));
            updateText.Text = "更新已下载并通过签名与完整性校验。保存项目后可重启安装。"; buttons["下载更新"].Content = "安装并重启";
        }
    }
    private async Task RunUiGates(string directory) {
        var results = new List<object>(); var failed = 0;
        void Assert(string gate, bool condition) { results.Add(new { gate, passed = condition }); if (!condition) failed++; }
        try {
            Directory.CreateDirectory(directory);
            Control? Named(string name) => this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
            Assert("原版布局：左侧 56 像素工具栏", Named("ToolRail")?.Bounds.Width == 56);
            Assert("原版布局：右侧 252 像素图层面板", Named("LayersPanel")?.Bounds.Width == 252);
            Assert("原版布局：42 像素顶部变换栏", Named("TransformBar")?.Bounds.Height == 42);
            Assert("原版布局：30 像素底部状态栏", Named("StatusBar")?.Bounds.Height == 30);
            Assert("原版观感：中性灰与系统字体", Background is SolidColorBrush brush && brush.Color == Color.Parse("#242424") && FontSize == 12 && FontFamily.Name.StartsWith("Segoe UI"));
            Assert("空文档禁用保存与导出", !buttons["保存"].IsEnabled && !buttons["导出 PNG"].IsEnabled);
            editor.New(1280, 720); editor.AddImage(Raster.SolidPng(600, 400, 114, 81, 203), "紫色图层"); editor.MoveActive(120, 100); Fit(); Refresh();
            Assert("中文标题、图层与命令状态", Title?.Contains("中文版") == true && layerList.ItemCount == 1 && buttons["保存"].IsEnabled && buttons["撤销"].IsEnabled);
            await Dispatch("撤销"); Assert("实际撤销按钮路径", editor.Active!.OriginX == 0 && buttons["重做"].IsEnabled);
            Assert("菜单命令状态与界面一致", menuCommands["保存"].IsEnabled && menuCommands["重做"].IsEnabled);
            var unsavedId = editor.Document!.Id;
            var cancelSave = ConfirmSaved();
            await Task.Delay(100);
            var saveDialog = OwnedWindows.Single();
            var cancelButton = saveDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "取消");
            cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert("未保存弹窗取消保持项目", !await cancelSave && editor.Dirty && editor.Document.Id == unsavedId);
            var id = editor.Document!.Id;
            foreach (var code in Enum.GetValues<ErrorCode>()) { status.Text = Messages.For(code); Assert("中文错误显示 " + code, status.Text.Length > 5 && !status.Text.Contains("Exception")); }
            Assert("错误显示不替换文档", editor.Document!.Id == id);
            status.Text = "体验门：中文界面、图层、命令状态与错误显示已验证。";
            await Task.Delay(400);
            using var image = new RenderTargetBitmap(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height), new Vector(96, 96)); image.Render(this); image.Save(Path.Combine(directory, "中文界面.png"), PngBitmapEncoderOptions.Default);
        } catch (Exception e) { results.Add(new { gate = "体验运行", passed = false, detail = e.ToString() }); failed++; }
        await File.WriteAllTextAsync(Path.Combine(directory, "ui-gates.json"), JsonSerializer.Serialize(new { failed, gates = results }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failed == 0 ? 0 : 1; closingApproved = true; Close();
    }
    private async Task RunUpgradeGate(string directory) {
        Directory.CreateDirectory(directory);
        try {
            var project = Path.Combine(directory, "升级保留文档.comp");
            if (!Directory.Exists(project)) { editor.New(4, 4); editor.AddImage(Raster.SolidPng(2, 2, 255, 0, 0), "升级保留中文图层"); editor.Save(project); }
            else editor.Open(project);
            var pixel = Raster.Pixel(editor.ExportPng(), 0, 0);
            if (editor.Active?.Name != "升级保留中文图层" || pixel.R != 255 || pixel.A != 255 || editor.Dirty) throw new InvalidOperationException("升级后的项目与原始像素不一致");
            await updater.Check();
            var installed = updater.Manager.CurrentVersion?.ToString() ?? "uninstalled";
            await File.WriteAllTextAsync(Path.Combine(directory, "version-" + installed + ".json"), JsonSerializer.Serialize(new { installed, available = updater.Source.Version, documentVerified = true }));
            if (updater.Available != null) { await updater.Download(_ => { }); await updater.InstallForUpgradeGate(directory); }
            else { closingApproved = true; Close(); }
        } catch (Exception e) { await File.WriteAllTextAsync(Path.Combine(directory, "upgrade-failed.json"), JsonSerializer.Serialize(new { error = e.ToString() })); Environment.ExitCode = 1; closingApproved = true; Close(); }
    }
}
