using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BetaArt;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "BetaArt";

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll();
        BetaArtState.LoadPrefs();
    }
}
