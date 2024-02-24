using System;
using System.Globalization;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("CustomizeSightDistanceTitle", "CustomizeSightDistanceDescription", ModuleCategories.Interface)]
public unsafe class CustomizeSightDistance : IDailyModule
{
    public bool Initialized { get; set; }
    public bool WithConfigUI => true;

    private static float ConfigMaxDistance = 80;

    public void Init()
    {
        Service.Config.AddConfig(this, "MaxDistance", ConfigMaxDistance);
        ConfigMaxDistance = Service.Config.GetConfig<float>(this, "MaxDistance");

        Service.Log.Debug($"{CameraManager.Instance->GetActiveCamera()->MaxDistance}");
        CameraManager.Instance->GetActiveCamera()->MaxDistance = ConfigMaxDistance;

        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    public void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("CustomizeSightDistance-MaxDistanceInput")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat("###MaxDistanceInput", ref ConfigMaxDistance, 0, 0,
                             ConfigMaxDistance.ToString(CultureInfo.InvariantCulture),
                             ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigMaxDistance = Math.Clamp(ConfigMaxDistance, 1, 100);

            Service.Config.UpdateConfig(this, "MaxDistance", ConfigMaxDistance);
            CameraManager.Instance->GetActiveCamera()->MaxDistance = ConfigMaxDistance;
        }
    }

    public void OverlayUI() { }

    private void OnZoneChanged(object? sender, ushort e)
    {
        CameraManager.Instance->GetActiveCamera()->MaxDistance = ConfigMaxDistance;
    }

    public void Uninit()
    {
        CameraManager.Instance->GetActiveCamera()->MaxDistance = 25;
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
    }
}
