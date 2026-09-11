using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NodeEditor.Avalonia;

namespace NodeEditor.Avalonia.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RegisterDemoNodes(Editor.Manager);
        Editor.CreateNode("Int", 80, 40);
        Editor.CreateNode("String", 80, 200);
        Editor.CreateNode("Bool", 80, 360);
        Editor.CreateNode("SendMessage", 420, 80);
        Editor.CreateNode("Print", 420, 280);
    }

    /// <summary>
    /// 注册演示节点：传名称、标题、标题色、输入端点名、输出端点名，不会立刻创建控件。
    /// </summary>
    private static void RegisterDemoNodes(NodeManager manager)
    {
        manager.RegisterCommonDataSources();
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
    /// 把当前画布导出成 JSON 文件。
    /// </summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export JSON",
            SuggestedFileName = "nodes.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType]
        });
        if (file == null)
            return;
        var json = Editor.ExportJson();
        var path = file.TryGetLocalPath();
        if (path != null)
            await File.WriteAllTextAsync(path, json);
        else
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
        }

        HintText.Text = "已导出 " + (path ?? file.Name);
    }

    /// <summary>
    /// 选择 JSON 文件并加载到当前节点视图，会替换画布上已有节点。
    /// </summary>
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import JSON",
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType]
        });
        if (files.Count == 0)
            return;
        string json;
        var path = files[0].TryGetLocalPath();
        if (path != null)
            json = await File.ReadAllTextAsync(path);
        else
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync();
        }

        Editor.ImportJson(json);
        HintText.Text = "已导入 " + (path ?? files[0].Name);
    }

    private static FilePickerFileType JsonFileType => new("JSON")
    {
        Patterns = ["*.json"]
    };
}
