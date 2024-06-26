using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

namespace DailyRoutines.Modules;

[ModuleDescription("InstantReturnTitle", "InstantReturnDescription", ModuleCategories.系统)]
public class InstantReturn : DailyModuleBase
{
    private delegate byte ReturnDelegate(nint a1);

    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B 3D ?? ?? ?? ?? 48 8B D9 48 8D 0D", DetourName = nameof(ReturnDetour))]
    private static Hook<ReturnDelegate>? ReturnHook;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        ReturnHook?.Enable();
    }

    private static byte ReturnDetour(nint a1)
    {
        if (Service.ClientState.IsPvPExcludingDen)
            return ReturnHook.Original(a1);

        Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.InstantReturn, 0, 0, 0, 0);
        return 1;
    }
}
