namespace Snet.Yolo.Tasks.Services;

/// <summary>按用户保存尚未点击设置页保存的品牌图片草稿；上传完成不直接修改站点品牌。</summary>
public sealed class BrandLogoDraftStore
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "transfers", "logos");
    /// <summary>读取当前用户的上传草稿。</summary>
    public string? Read(string owner)
    {
        var path = Path.Combine(_root, UserStoragePath.Segment(owner) + ".txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
    /// <summary>原子写入已验证的图片草稿。</summary>
    public void Write(string owner, string data)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, UserStoragePath.Segment(owner) + ".txt");
        File.WriteAllText(path + ".tmp", data); File.Move(path + ".tmp", path, true);
    }
    /// <summary>保存或恢复默认后丢弃上传草稿。</summary>
    public void Clear(string owner) => File.Delete(Path.Combine(_root, UserStoragePath.Segment(owner) + ".txt"));
}
