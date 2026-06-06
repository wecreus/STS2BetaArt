using System;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;

namespace BetaArt;

internal class EpochScreenState
{
    public NTickbox? BetaTickbox;
    public Label? BetaLabel;
    public bool Positioned;
}

internal static class EpochBetaArtState
{
    internal static readonly ConditionalWeakTable<NEpochInspectScreen, EpochScreenState> States = new();

    private static readonly System.Reflection.FieldInfo PortraitField =
        AccessTools.Field(typeof(NEpochInspectScreen), "_portrait");
    private static readonly System.Reflection.FieldInfo EpochField =
        AccessTools.Field(typeof(NEpochInspectScreen), "_epoch");
    private static readonly System.Reflection.FieldInfo PlaceholderLabelField =
        AccessTools.Field(typeof(NEpochInspectScreen), "_placeholderLabel");
    private static readonly System.Reflection.FieldInfo CloseButtonField =
        AccessTools.Field(typeof(NEpochInspectScreen), "_closeButton");

    private const string TickboxScenePath = "res://scenes/ui/tickbox.tscn";

    internal static bool HasEpochBetaArt(EpochModel model) =>
        !model.IsArtPlaceholder &&
        ResourceLoader.Exists(GetEpochBetaPath(model));

    internal static string GetEpochBetaPath(EpochModel model) =>
        $"res://images/timeline/epoch_portraits/placeholder/{model.Id.ToLowerInvariant()}.png";

    internal static string GetEpochKey(EpochModel model) => $"epoch:{model.Id}";

    internal static void OnEpochBetaToggled(NEpochInspectScreen screen, bool pressed)
    {
        if (!States.TryGetValue(screen, out var state) || state.BetaTickbox == null) return;
        try
        {
            var model = EpochField.GetValue(screen) as EpochModel;
            if (model == null) return;

            string key = GetEpochKey(model);
            if (pressed) BetaArtState.BetaEnabled.Add(key);
            else         BetaArtState.BetaEnabled.Remove(key);

            ApplyEpochPortrait(screen, model);
            BetaArtState.SavePrefs();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] OnEpochBetaToggled failed: {e}");
        }
    }

    internal static void ApplyEpochPortrait(NEpochInspectScreen screen, EpochModel model)
    {
        var portrait = PortraitField.GetValue(screen) as TextureRect;
        if (portrait == null) return;

        string key = GetEpochKey(model);
        if (BetaArtState.BetaEnabled.Contains(key))
        {
            var betaTex = ResourceLoader.Load<Texture2D>(GetEpochBetaPath(model), null, ResourceLoader.CacheMode.Reuse);
            if (betaTex != null) portrait.Texture = betaTex;
        }
        else
        {
            portrait.Texture = model.RealPortrait;
        }
    }

    internal static void SyncTickboxForEpoch(EpochScreenState state, EpochModel model)
    {
        if (state.BetaTickbox == null) return;

        bool hasBeta = HasEpochBetaArt(model);
        bool isBetaOn = hasBeta && BetaArtState.BetaEnabled.Contains(GetEpochKey(model));

        state.BetaTickbox.Visible = hasBeta;
        if (state.BetaLabel != null) state.BetaLabel.Visible = hasBeta;

        if (hasBeta) state.BetaTickbox.Enable();
        else         state.BetaTickbox.Disable();

        state.BetaTickbox.IsTicked = isBetaOn;
    }

    internal static NTickbox? CreateTickbox(Node parent, out Label? label)
    {
        label = null;
        try
        {
            var scene = ResourceLoader.Load<PackedScene>(TickboxScenePath);
            if (scene == null)
            {
                GD.PrintErr($"[BetaArt] Could not load tickbox scene: {TickboxScenePath}");
                return null;
            }

            var tb = scene.Instantiate() as NTickbox;
            if (tb == null)
            {
                GD.PrintErr("[BetaArt] Tickbox scene did not produce an NTickbox.");
                return null;
            }

            tb.Visible   = false;
            tb.FocusMode = Control.FocusModeEnum.None;
            parent.AddChild(tb);
            tb.IsTicked = false;

            var lbl = new Label();
            lbl.Text        = "Beta Art";
            lbl.Visible     = false;
            lbl.MouseFilter = Control.MouseFilterEnum.Stop;
            parent.AddChild(lbl);

            var placeholderLabel = PlaceholderLabelField.GetValue(parent) as MegaLabel;
            if (placeholderLabel != null)
            {
                var font = placeholderLabel.GetThemeFont("font");
                if (font != null) lbl.AddThemeFontOverride("font", font);
                int fontSize = placeholderLabel.GetThemeFontSize("font_size");
                if (fontSize > 0) lbl.AddThemeFontSizeOverride("font_size", fontSize);
                var color = placeholderLabel.GetThemeColor("font_color");
                lbl.AddThemeColorOverride("font_color", color);
            }

            label = lbl;
            GD.Print("[BetaArt] Epoch: BetaTickbox created.");
            return tb;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] CreateTickbox failed: {e}");
            return null;
        }
    }

    internal static void PositionTickbox(NEpochInspectScreen screen, EpochScreenState state)
    {
        var tb2    = state.BetaTickbox;
        var label2 = state.BetaLabel;
        if (tb2 == null) return;

        Callable.From(() => Callable.From(() =>
        {
            try
            {
                var closeButton = CloseButtonField.GetValue(screen) as Control;
                if (closeButton == null) return;

                // Position to the left of the close button, centred on same Y
                float iconSize = closeButton.Size.Y;
                float tbY      = closeButton.GlobalPosition.Y + (closeButton.Size.Y - iconSize) * 0.5f;
                float tbX      = closeButton.GlobalPosition.X - iconSize - 24f;

                tb2.GlobalPosition = new Vector2(tbX, tbY);
                tb2.Size           = new Vector2(iconSize, iconSize);

                if (label2 != null)
                {
                    label2.GlobalPosition = new Vector2(
                        tbX - label2.Size.X - 8f,
                        tbY + (iconSize - label2.Size.Y) * 0.5f);
                }

                state.Positioned = true;
                GD.Print($"[BetaArt] Epoch: positioned tb={tb2.GlobalPosition} size={tb2.Size}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[BetaArt] Epoch positioning failed: {e}");
            }
        }).CallDeferred()).CallDeferred();
    }
}

// Patch on the public async Open method.
// The portrait is set synchronously before the first await, so our postfix
// runs after the texture is already assigned — we can safely override it.
[HarmonyPatch(typeof(NEpochInspectScreen), "Open",
    new[] { typeof(NEpochSlot), typeof(EpochModel), typeof(bool) })]
public static class NEpochInspectScreen_Open_Patch
{
    public static void Postfix(NEpochInspectScreen __instance, EpochModel epoch)
    {
        try
        {
            var state = EpochBetaArtState.States.GetOrCreateValue(__instance);

            if (state.BetaTickbox == null)
            {
                var tb = EpochBetaArtState.CreateTickbox(__instance, out var lbl);
                if (tb == null) return;

                state.BetaTickbox = tb;
                state.BetaLabel   = lbl;

                var capturedTb  = tb;
                var capturedScr = __instance;
                tb.Toggled += (NTickbox tickbox) =>
                    EpochBetaArtState.OnEpochBetaToggled(capturedScr, tickbox.IsTicked);

                if (lbl != null)
                {
                    lbl.GuiInput += (InputEvent e) => {
                        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                        {
                            bool next = !capturedTb.IsTicked;
                            capturedTb.IsTicked = next;
                            EpochBetaArtState.OnEpochBetaToggled(capturedScr, next);
                        }
                    };
                }

                EpochBetaArtState.PositionTickbox(__instance, state);
            }

            EpochBetaArtState.SyncTickboxForEpoch(state, epoch);

            // Override portrait if beta is toggled on
            if (BetaArtState.BetaEnabled.Contains(EpochBetaArtState.GetEpochKey(epoch)))
                EpochBetaArtState.ApplyEpochPortrait(__instance, epoch);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] Epoch Open patch failed: {e}");
        }
    }
}

// Patch the private OpenViaPaginator method that is called when the player
// navigates between chapters within the same epoch inspect screen.
[HarmonyPatch(typeof(NEpochInspectScreen), "OpenViaPaginator")]
public static class NEpochInspectScreen_OpenViaPaginator_Patch
{
    public static void Postfix(NEpochInspectScreen __instance, EpochModel epoch)
    {
        try
        {
            if (!EpochBetaArtState.States.TryGetValue(__instance, out var state)
                || state.BetaTickbox == null)
                return;

            EpochBetaArtState.SyncTickboxForEpoch(state, epoch);

            if (BetaArtState.BetaEnabled.Contains(EpochBetaArtState.GetEpochKey(epoch)))
                EpochBetaArtState.ApplyEpochPortrait(__instance, epoch);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] Epoch OpenViaPaginator patch failed: {e}");
        }
    }
}
