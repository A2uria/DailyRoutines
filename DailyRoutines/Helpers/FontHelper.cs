using System;
using DailyRoutines.Managers;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using ImGuiNET;

namespace DailyRoutines.Helpers;

public class FontHelper
{
    public static IFontAtlas FontAtlas => fontAtlas.Value;
    public static IFontHandle Axis96 => axis96.Value;
    public static IFontHandle Axis12 => axis12.Value;
    public static IFontHandle Axis14 => axis14.Value;
    public static IFontHandle Axis18 => axis18.Value;
    public static IFontHandle Icon => Service.PluginInterface.UiBuilder.IconFontHandle;

    private static IFontHandle ConstructFontHandle(GameFontFamilyAndSize fontInfo)
        => FontAtlas.NewGameFontHandle(new GameFontStyle(fontInfo));

    public static unsafe ImFontPtr GetFont(float size, float scale = 0)
    {
        var style = new GameFontStyle(GameFontStyle.GetRecommendedFamilyAndSize(GameFontFamily.Axis, size));
        var handle = FontAtlas.NewGameFontHandle(style);

        try
        {
            var font = handle.Lock().ImFont;

            if ((nint)font.NativePtr == nint.Zero)
            {
                return ImGui.GetFont();
            }

            font.Scale = scale == 0 ? size / font.FontSize : scale;
            return font;
        }
        catch
        {
            return ImGui.GetFont();
        }
    }

    #region Lazy

    private static Lazy<IFontAtlas> fontAtlas =
        new(() => Service.PluginInterface.UiBuilder.CreateFontAtlas(FontAtlasAutoRebuildMode.OnNewFrame));

    private static Lazy<IFontHandle> axis96 = new(() => ConstructFontHandle(GameFontFamilyAndSize.Axis96));

    private static Lazy<IFontHandle> axis12 = new(() => ConstructFontHandle(GameFontFamilyAndSize.Axis12));

    private static Lazy<IFontHandle> axis14 = new(() => ConstructFontHandle(GameFontFamilyAndSize.Axis14));

    private static Lazy<IFontHandle> axis18 = new(() => ConstructFontHandle(GameFontFamilyAndSize.Axis18));

    #endregion
}