using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.Modules;

[ModuleDescription("FastObjectInteractTitle", "FastObjectInteractDescription", ModuleCategories.界面优化)]
public unsafe partial class FastObjectInteract : DailyModuleBase
{
    private sealed record ObjectWaitSelected(nint GameObject, string Name, ObjectKind Kind, float Distance)
    {
        public bool Equals(ObjectWaitSelected? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return GameObject == other.GameObject;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((nint)GameObject);
        }
    }

    private class Config : ModuleConfiguration
    {
        public int MaxDisplayAmount = 5;
        public bool AllowClickToTarget;
        public bool WindowInvisibleWhenInteract = true;
        public float FontScale = 1f;
        public readonly HashSet<ObjectKind> SelectedKinds = [ObjectKind.EventNpc, ObjectKind.EventObj, ObjectKind.Treasure,             ObjectKind.Aetheryte, ObjectKind.GatheringPoint];
        public readonly HashSet<string> BlacklistKeys = [];
        public float MinButtonWidth = 300f;
        public bool OnlyDisplayInViewRange;
        public bool LockWindow;
    }

    private static readonly Dictionary<ObjectKind, string> ObjectKindLoc = new()
    {
        { ObjectKind.BattleNpc, "战斗类 NPC (不建议)" },
        { ObjectKind.EventNpc, "一般类 NPC" },
        { ObjectKind.EventObj, "事件物体 (绝大多数要交互的都属于此类)" },
        { ObjectKind.Treasure, "宝箱" },
        { ObjectKind.Aetheryte, "以太之光" },
        { ObjectKind.GatheringPoint, "采集点" },
        { ObjectKind.MountType, "坐骑 (不建议)" },
        { ObjectKind.Companion, "宠物 (不建议)" },
        { ObjectKind.Retainer, "雇员" },
        { ObjectKind.Area, "地图传送相关" },
        { ObjectKind.Housing, "家具庭具" },
        { ObjectKind.CardStand, "固定类物体 (如无人岛采集点等)" },
        { ObjectKind.Ornament, "时尚配饰 (不建议)" }
    };
    private static Dictionary<uint, string>? ENpcTitles;
    private static HashSet<uint>? ImportantENPC;
    private const string ENPCTiltleText = "[{0}] {1}";

    private static Config ModuleConfig = null!;

    private static string BlacklistKeyInput = string.Empty;
    private static float WindowWidth;

    private static readonly List<ObjectWaitSelected> tempObjects = new(596);
    private static TargetSystem* targetSystem;
    private static readonly Dictionary<nint, ObjectWaitSelected> ObjectsWaitSelected = [];

    private static string AethernetShardName = string.Empty;
    private static bool IsInInstancedArea;
    private static int InstancedAreaAmount = 3;

    public override void Init()
    {
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };

        ModuleConfig = LoadConfig<Config>() ?? new();

        ENpcTitles ??= LuminaCache.Get<ENpcResident>()
                                  .Where(x => x.Unknown10 && !string.IsNullOrWhiteSpace(x.Title.RawString))
                                  .ToDictionary(x => x.RowId, x => x.Title.RawString);
        ImportantENPC ??= LuminaCache.Get<ENpcResident>()
                                     .Where(x => x.Unknown10)
                                     .Select(x => x.RowId)
                                     .ToHashSet();

        AethernetShardName = LuminaCache.GetRow<EObjName>(2000151).Singular.RawString;

        targetSystem = TargetSystem.Instance();

        Overlay ??= new Overlay(this, $"Daily Routines {Service.Lang.GetText("FastObjectInteractTitle")}");
        Overlay.Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
                        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse;

        if (ModuleConfig.LockWindow) Overlay.Flags |= ImGuiWindowFlags.NoMove;
        else Overlay.Flags &= ~ImGuiWindowFlags.NoMove;

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Service.FrameworkManager.Register(OnUpdate);

        OnZoneChanged(1);
    }

    private static void OnZoneChanged(ushort zone)
    {
        if (zone == 0 || zone == Service.ClientState.TerritoryType) return;

        InstancedAreaAmount = 3;
    }

    public override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-FontScale")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        ImGui.InputFloat("###FontScaleInput", ref ModuleConfig.FontScale, 0f, 0f,
                         ModuleConfig.FontScale.ToString(CultureInfo.InvariantCulture));
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.FontScale = Math.Max(0.1f, ModuleConfig.FontScale);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-MinButtonWidth")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        ImGui.InputFloat("###MinButtonWidthInput", ref ModuleConfig.MinButtonWidth, 0, 0,
                         ModuleConfig.MinButtonWidth.ToString(CultureInfo.InvariantCulture));
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.MinButtonWidth = Math.Max(1, ModuleConfig.MinButtonWidth);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-MaxDisplayAmount")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###MaxDisplayAmountInput", ref ModuleConfig.MaxDisplayAmount, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.MaxDisplayAmount = Math.Max(1, ModuleConfig.MaxDisplayAmount);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-SelectedObjectKinds")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###ObjectKindsSelection", Service.Lang.GetText("FastObjectInteract-SelectedObjectKindsAmount", ModuleConfig.SelectedKinds.Count), ImGuiComboFlags.HeightLarge))
        {
            foreach (var kind in ObjectKindLoc)
            {
                var state = ModuleConfig.SelectedKinds.Contains(kind.Key);
                if (ImGui.Checkbox(kind.Value, ref state))
                {
                    if (!ModuleConfig.SelectedKinds.Remove(kind.Key))
                        ModuleConfig.SelectedKinds.Add(kind.Key);

                    SaveConfig(ModuleConfig);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-BlacklistKeysList")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###BlacklistObjectsSelection", Service.Lang.GetText("FastObjectInteract-BlacklistKeysListAmount", ModuleConfig.BlacklistKeys.Count), ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("###BlacklistKeyInput",
                                    $"{Service.Lang.GetText("FastObjectInteract-BlacklistKeysListInputHelp")}",
                                    ref BlacklistKeyInput, 100);
            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("###BlacklistKeyInputAdd", FontAwesomeIcon.Plus,
                                   Service.Lang.GetText("FastObjectInteract-Add")))
            {
                if (!ModuleConfig.BlacklistKeys.Add(BlacklistKeyInput)) return;

                SaveConfig(ModuleConfig);
            }

            ImGui.Separator();

            foreach (var key in ModuleConfig.BlacklistKeys)
            {
                if (ImGuiOm.ButtonIcon(key, FontAwesomeIcon.TrashAlt, Service.Lang.GetText("FastObjectInteract-Remove")))
                {
                    ModuleConfig.BlacklistKeys.Remove(key);
                    SaveConfig(ModuleConfig);
                }

                ImGui.SameLine();
                ImGui.Text(key);
            }

            ImGui.EndCombo();
        }

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-WindowInvisibleWhenInteract"), 
                           ref ModuleConfig.WindowInvisibleWhenInteract))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-LockWindow"), ref ModuleConfig.LockWindow))
        {
            SaveConfig(ModuleConfig);

            if (ModuleConfig.LockWindow)
                Overlay.Flags |= ImGuiWindowFlags.NoMove;
            else
                Overlay.Flags &= ~ImGuiWindowFlags.NoMove;
        }

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-OnlyDisplayInViewRange"), ref ModuleConfig.OnlyDisplayInViewRange))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-AllowClickToTarget"), ref ModuleConfig.AllowClickToTarget))
            SaveConfig(ModuleConfig);
    }

    public override void OverlayUI()
    {
        PresetFont.Axis14.Push();
        var colors = ImGui.GetStyle().Colors;

        var aetheryteInstance = (GameObject*)nint.Zero;
        var aetheryteInstanceState = false;

        ImGui.BeginGroup();
        foreach (var kvp in ObjectsWaitSelected)
        {
            if (kvp.Value.GameObject == nint.Zero) continue;

            var interactState = CanInteract(kvp.Value.Kind, kvp.Value.Distance);
            aetheryteInstanceState = interactState;
            if (IsInInstancedArea && kvp.Value.Kind == ObjectKind.Aetheryte)
            {
                var gameObj = (GameObject*)kvp.Value.GameObject;
                if (Marshal.PtrToStringUTF8((nint)gameObj->Name) != AethernetShardName)
                    aetheryteInstance = gameObj;
            }

            if (ModuleConfig.AllowClickToTarget)
            {
                if (!interactState)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, colors[(int)ImGuiCol.HeaderActive]);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colors[(int)ImGuiCol.HeaderHovered]);
                }

                ButtonText(kvp.Key.ToString(), kvp.Value.Name);

                if (!interactState)
                {
                    ImGui.PopStyleColor(2);
                    ImGui.PopStyleVar();
                }

                if (ImGui.BeginPopupContextItem($"{kvp.Value.Name}"))
                {
                    if (ImGui.MenuItem(Service.Lang.GetText("FastObjectInteract-AddToBlacklist")))
                    {
                        if (!ModuleConfig.BlacklistKeys.Add(AddToBlacklistNameRegex().Replace(kvp.Value.Name, "").Trim()))
                            return;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && interactState)
                {
                    if (interactState) InteractWithObject((GameObject*)kvp.Value.GameObject, kvp.Value.Kind);
                }
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    TargetSystem.Instance()->Target = (GameObject*)kvp.Value.GameObject;
            }
            else
            {
                ImGui.BeginDisabled(!interactState);
                if (ButtonText(kvp.Key.ToString(), kvp.Value.Name) && interactState)
                    InteractWithObject((GameObject*)kvp.Value.GameObject, kvp.Value.Kind);
                ImGui.EndDisabled();

                if (ImGui.BeginPopupContextItem($"{kvp.Value.Name}"))
                {
                    if (ImGui.MenuItem(Service.Lang.GetText("FastObjectInteract-AddToBlacklist")))
                    {
                        if (!ModuleConfig.BlacklistKeys.Add(FastObjectInteractTitleRegex().Replace(kvp.Value.Name, "").Trim()))
                            return;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }
            }
        }
        ImGui.EndGroup();

        ImGui.SameLine();
        if (aetheryteInstance != null)
        {
            ImGui.BeginGroup();
            var instance = UIState.Instance()->AreaInstance;
            for (var i = 1; i < InstancedAreaAmount + 1; i++)
            {
                if (i == instance.Instance) continue;
                ImGui.BeginDisabled(!aetheryteInstanceState);
                if (ButtonText(Service.Lang.GetText("FastObjectInteract-InstanceAreaChange", i), Service.Lang.GetText("FastObjectInteract-InstanceAreaChange", i)) && aetheryteInstanceState)
                    ChangeInstanceZone(aetheryteInstance, i);
                ImGui.EndDisabled();
            }
            ImGui.EndGroup();

        }
        WindowWidth = Math.Max(ModuleConfig.MinButtonWidth, ImGui.GetItemRectSize().X);

        PresetFont.Axis14.Pop();
    }

    private void OnUpdate(IFramework framework)
    {
        if (!EzThrottler.Throttle("FastSelectObjects", 250)) return;

        Service.Framework.Run(() =>
        {
            var localPlayer = Service.ClientState.LocalPlayer;
            if (localPlayer == null)
            {
                ObjectsWaitSelected.Clear();
                WindowWidth = 0f;
                Overlay.IsOpen = false;
                return;
            }

            tempObjects.Clear();
            IsInInstancedArea = UIState.Instance()->AreaInstance.IsInstancedArea();

            foreach (var obj in Service.ObjectTable)
            {
                if (!obj.IsTargetable || obj.IsDead) continue;

                var objName = obj.Name.TextValue;
                if (ModuleConfig.BlacklistKeys.Contains(objName)) continue;

                var objKind = obj.ObjectKind;
                if (!ModuleConfig.SelectedKinds.Contains(objKind)) continue;

                var dataID = obj.DataId;
                if (objKind == ObjectKind.EventNpc && !ImportantENPC.Contains(dataID))
                {
                    if (!ImportantENPC.Contains(dataID)) continue;
                    if (ENpcTitles.TryGetValue(dataID, out var ENPCTitle))
                        objName = string.Format(ENPCTiltleText, ENPCTitle, obj.Name);
                }

                var gameObj = (GameObject*)obj.Address;
                if (ModuleConfig.OnlyDisplayInViewRange)
                    if (!targetSystem->IsObjectInViewRange(gameObj))
                        continue;

                var objDistance = Vector3.Distance(localPlayer.Position, obj.Position);
                if (objDistance > 15 || localPlayer.Position.Y - gameObj->Position.Y > 4) continue;

                if (tempObjects.Count > ModuleConfig.MaxDisplayAmount) break;
                tempObjects.Add(new ObjectWaitSelected((nint)gameObj, objName, objKind, objDistance));
            }

            tempObjects.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            ObjectsWaitSelected.Clear();
            foreach (var tempObj in tempObjects) ObjectsWaitSelected.Add(tempObj.GameObject, tempObj);

            if (Overlay == null) return;
            if (!IsWindowShouldBeOpen())
            {
                Overlay.IsOpen = false;
                WindowWidth = 0f;
            }
            else
                Overlay.IsOpen = true;
        }, FrameworkManager.CancelSource.Token);
    }

    private static void InteractWithObject(GameObject* obj, ObjectKind kind)
    {
        TargetSystem.Instance()->Target = obj;
        TargetSystem.Instance()->InteractWithObject(obj);
        if (kind is ObjectKind.EventObj) TargetSystem.Instance()->OpenObjectInteraction(obj);
    }

    private void ChangeInstanceZone(GameObject* obj, int zone)
    {
        TaskManager.Enqueue(() =>
        {
            TargetSystem.Instance()->Target = obj;
            TargetSystem.Instance()->InteractWithObject(obj);
        });

        TaskManager.Enqueue(() =>
        {
            if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) || 
                !IsAddonAndNodesReady(addon)) return false;

            return ClickHelper.SelectString("切换副本区");
        });

        TaskManager.Enqueue(() =>
        {
            if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) ||
                !IsAddonAndNodesReady(addon)) return false;

            if (!MemoryHelper.ReadSeStringNullTerminated((nint)addon->AtkValues[2].String).TextValue
                             .Contains("为了缓解服务器压力")) return false;

            InstancedAreaAmount = ((AddonSelectString*)addon)->PopupMenu.PopupMenu.EntryCount - 2;
            return Click.TrySendClick($"select_string{zone + 1}");
        });
    }

    private static bool IsWindowShouldBeOpen()
        => ObjectsWaitSelected.Count != 0 && (!ModuleConfig.WindowInvisibleWhenInteract || !IsOccupied());

    private static bool CanInteract(ObjectKind kind, float distance)
    {
        return kind switch
        {
            ObjectKind.EventObj => distance < 4.7999999,
            ObjectKind.EventNpc => distance < 6.9999999,
            ObjectKind.Aetheryte => distance < 11.0,
            ObjectKind.GatheringPoint => distance < 3.0,
            _ => distance < 6.0
        };
    }

    public static bool ButtonText(string id, string text)
    {
        ImGui.PushID(id);
        ImGui.SetWindowFontScale(ModuleConfig.FontScale);

        var textSize = ImGui.CalcTextSize(text);

        var cursorPos = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().FramePadding;
        var buttonWidth = Math.Max(WindowWidth, textSize.X + (padding.X * 2));
        var result = ImGui.Button(string.Empty, new Vector2(buttonWidth, textSize.Y + (padding.Y * 2)));

        ImGui.GetWindowDrawList()
             .AddText(new Vector2(cursorPos.X + ((buttonWidth - textSize.X) / 2), cursorPos.Y + padding.Y),
                      ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.SetWindowFontScale(1);
        ImGui.PopID();

        return result;
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        ObjectsWaitSelected.Clear();

        base.Uninit();
    }

    [GeneratedRegex("\\[.*?\\]")]
    private static partial Regex AddToBlacklistNameRegex();

    [GeneratedRegex("\\[.*?\\]")]
    private static partial Regex FastObjectInteractTitleRegex();
}
