using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Platform.Storage;
using System.Text;
using TexturePlugin.Helpers;
using TexturePlugin.ViewModels;
using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;

namespace TexturePlugin;

public class EditTextureOption : IUavPluginOption
{
    public string Name => "Edit Texture2D";
    public string Description => "Edits Texture2D settings";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (mode != UavPluginMode.Export)
            return false;

        var texTypeId = (int)AssetClassID.Texture2D;
        return selection.All(a => a.TypeId == texTypeId);
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        var editTextureVm = new EditTextureViewModel(workspace, selection);

        // FIX: передаём функцию открытия диалога — без async лямбды, просто метод
        editTextureVm.SetOpenFileDialogFunc(() => ShowLoadTextureDialog(funcs));

        var result = await funcs.ShowDialog(editTextureVm);
        if (!result.HasValue)
            return false;

        var editTexSettings = result.Value;

        var errorBuilder = new StringBuilder();
        foreach (var asset in selection)
        {
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";

            var baseField = TextureHelper.GetByteArrayTexture(workspace, asset);
            if (baseField == null)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: failed to read");
                continue;
            }

            var tex = TextureFile.ReadTextureFile(baseField);

            var needToReencode = false;
            if (editTexSettings.TextureFormat is not null)
                needToReencode |= tex.m_TextureFormat != (int)editTexSettings.TextureFormat;

            byte[]? texOrigDecBytes = null;
            if (needToReencode)
            {
                var texOrigEncBytes = tex.FillPictureData(asset.FileInstance);
                if (texOrigEncBytes is null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to decode for reencoding");
                    continue;
                }
                texOrigDecBytes = tex.DecodeTextureRaw(texOrigEncBytes, true);
            }

            if (editTexSettings.Name is not null)
                tex.m_Name = editTexSettings.Name;
            if (editTexSettings.TextureFormat is not null)
                tex.m_TextureFormat = (int)editTexSettings.TextureFormat.Value;
            if (editTexSettings.IsReadable is not null)
                tex.m_IsReadable = editTexSettings.IsReadable.Value;
            if (editTexSettings.FilterMode is not null)
                tex.m_TextureSettings.m_FilterMode = (int)editTexSettings.FilterMode.Value;
            if (editTexSettings.Filtering is not null)
                tex.m_TextureSettings.m_Aniso = editTexSettings.Filtering.Value;
            if (editTexSettings.MipBias is not null)
                tex.m_TextureSettings.m_MipBias = editTexSettings.MipBias.Value;
            if (editTexSettings.WrapModeU is not null)
                tex.m_TextureSettings.m_WrapU = (int)editTexSettings.WrapModeU.Value;
            if (editTexSettings.WrapModeV is not null)
                tex.m_TextureSettings.m_WrapV = (int)editTexSettings.WrapModeV.Value;
            if (editTexSettings.LightMapFormat is not null)
                tex.m_LightmapFormat = editTexSettings.LightMapFormat.Value;
            if (editTexSettings.ColorSpace is not null)
                tex.m_ColorSpace = (int)editTexSettings.ColorSpace.Value;

            // FIX: импорт новой текстуры если пользователь выбрал файл
            if (editTexSettings.NewTexturePath is not null && File.Exists(editTexSettings.NewTexturePath))
            {
                try
                {
                    tex.m_MipCount = 1;
                    tex.m_MipMap = false;
                    tex.EncodeTextureImage(editTexSettings.NewTexturePath);
                }
                catch (Exception e)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to import new texture: {e}");
                }
            }
            else if (needToReencode && texOrigDecBytes is not null)
            {
                try
                {
                    tex.m_MipCount = 1;
                    tex.m_MipMap = false;
                    tex.EncodeTextureRaw(texOrigDecBytes, tex.m_Width, tex.m_Height, 3, true);
                }
                catch (Exception e)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to import: {e}");
                }
            }

            tex.WriteTo(baseField);
            asset.UpdateAssetDataAndRow(workspace, baseField);
        }

        if (errorBuilder.Length > 0)
        {
            string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            string firstLinesStr = string.Join('\n', firstLines);
            await funcs.ShowMessageDialog("Error", firstLinesStr);
        }

        return true;
    }

    // FIX: открываем диалог и берём первый выбранный файл из массива результатов
    private static async Task<string?> ShowLoadTextureDialog(IUavPluginFunctions funcs)
    {
        var files = await funcs.ShowOpenFileDialog(new FilePickerOpenOptions()
        {
            Title = "Open texture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg", "*.tga"]
                },
                new FilePickerFileType("PNG file") { Patterns = ["*.png"] },
                new FilePickerFileType("BMP file") { Patterns = ["*.bmp"] },
                new FilePickerFileType("JPG file") { Patterns = ["*.jpg", "*.jpeg"] },
                new FilePickerFileType("TGA file") { Patterns = ["*.tga"] },
                FilePickerFileTypes.All,
            ]
        });

        // ShowOpenFileDialog возвращает string[] — берём первый файл
        return files?.FirstOrDefault();
    }
}