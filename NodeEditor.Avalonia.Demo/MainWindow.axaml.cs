using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NodeEditor.Avalonia;

namespace NodeEditor.Avalonia.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RegisterDemoNodes(Editor.Manager);
        Editor.CreateNode("SendMessage", 80, 120);
        Editor.CreateNode("Print", 520, 200);
    }

    /// <summary>
    /// 注册演示节点：传名称、标题、标题色、输入端点名、输出端点名，不会立刻创建控件。
    /// </summary>
    private static void RegisterDemoNodes(NodeManager manager)
    {
        manager.Register(
            "SendMessage",
            "Send Message",
            Color.Parse("#8B1E1E"),
            ["Target", "Content"],
            ["Next"]);
        manager.Register(
            "Print",
            "Print",
            Color.Parse("#1E4D8B"),
            ["Exec", "Text"],
            ["Next"]);
        manager.Register(
            "Branch",
            "Branch",
            Color.Parse("#2E7D32"),
            ["Exec", "Condition"],
            ["True", "False"]);
    }

    /// <summary>
    /// 导出当前画布节点列表为 JSON，显示在顶部提示栏。
    /// </summary>
    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var json = Editor.ExportJson();
        HintText.Text = json;
        Console.WriteLine(json);
    }
}
