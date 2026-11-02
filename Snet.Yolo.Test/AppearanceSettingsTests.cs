using SkiaSharp;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>品牌持久化及图片输入边界；使用独立目录，不修改运行站点配置。</summary>
public sealed class AppearanceSettingsTests
{
    [Fact]
    public void Save_RestartRestoresBrandAndDefaults_AndInvalidSaveKeepsPreviousValue()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-appearance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "appearance.json");
            var store = new AppearanceSettingsStore(path);
            var changed = 0;
            store.Changed += () => changed++;
            var settings = new AppearanceSettings { Title = "工厂视觉", Description = "生产检测", Language = "en-US", Theme = "dark", LogoDataUrl = AppearanceSettingsStore.CreateLogoDataUrl(Image(32, 32)) };
            store.Save(settings);

            Assert.Equal(settings, new AppearanceSettingsStore(path).Current);
            Assert.Equal(1, changed);
            Assert.Throws<ArgumentException>(() => store.Save(settings with { Title = " " }));
            Assert.Throws<ArgumentException>(() => store.Save(settings with { Language = "unsupported" }));
            Assert.Throws<ArgumentException>(() => store.Save(settings with { Theme = "invalid" }));
            Assert.Throws<InvalidDataException>(() => store.Save(settings with { LogoDataUrl = "javascript:alert(1)" }));
            Assert.Equal(settings, new AppearanceSettingsStore(path).Current);
            Assert.Equal(1, changed);
            store.Save(settings with { LogoDataUrl = null });
            Assert.Equal("/favicon.png", new AppearanceSettingsStore(path).Current.LogoUrl);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Logo_AcceptsActualSupportedFormatsAndReencodesAsPng(SKEncodedImageFormat format)
    {
        var logo = AppearanceSettingsStore.CreateLogoDataUrl(Image(32, 24, format));
        Assert.StartsWith("data:image/png;base64,", logo);
        using var bitmap = SKBitmap.Decode(Convert.FromBase64String(logo.Split(',')[1]));
        Assert.Equal(32, bitmap.Width);
        Assert.Equal(24, bitmap.Height);
    }

    [Fact]
    public void Logo_RejectsMalformedTooLargeAndOversizedDimensions()
    {
        Assert.Throws<InvalidDataException>(() => AppearanceSettingsStore.CreateLogoDataUrl([]));
        Assert.Throws<InvalidDataException>(() => AppearanceSettingsStore.CreateLogoDataUrl("<svg onload='alert(1)'/>"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => AppearanceSettingsStore.CreateLogoDataUrl(new byte[AppearanceSettingsStore.MaximumLogoBytes + 1]));
        Assert.Throws<InvalidDataException>(() => AppearanceSettingsStore.CreateLogoDataUrl(Image(2049, 1)));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("{\"LogoDataUrl\":\"invalid url\"}")]
    [InlineData("{\"LogoDataUrl\":\"data:image/png;base64,invalid\"}")]
    public void CorruptConfiguration_FallsBackToBuiltInBrand(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, content);
            Assert.Equal(new AppearanceSettings(), new AppearanceSettingsStore(path).Current);
        }
        finally { File.Delete(path); }
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(format, 90);
        return encoded.ToArray();
    }
}
