using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.addons.mega_text;

namespace BetaArt;

internal static class BetaArtState
{
    internal static readonly ConditionalWeakTable<NInspectCardScreen, ScreenState> States = new();
    internal static readonly HashSet<string> BetaEnabled = new();
    // Suppress Toggled signal handler while programmatically setting IsTicked
    internal static bool SuppressToggled;

    private const string PrefsPath = "user://betaart_enabled.txt";

    internal static void LoadPrefs()
    {
        if (!Godot.FileAccess.FileExists(PrefsPath)) return;
        try
        {
            using var f = Godot.FileAccess.Open(PrefsPath, Godot.FileAccess.ModeFlags.Read);
            foreach (var line in f.GetAsText().Split('\n'))
            {
                var entry = line.Trim();
                // Guard: only load paths that look like real beta portrait paths.
                // Malformed entries (e.g. the card atlas root) can hang ResourceLoader on startup.
                if (!string.IsNullOrWhiteSpace(entry) && entry.Contains("/beta/"))
                    BetaEnabled.Add(entry);
            }
        }
        catch (Exception e) { GD.PrintErr($"[BetaArt] LoadPrefs failed: {e}"); }
    }

    internal static void SavePrefs()
    {
        try
        {
            using var f = Godot.FileAccess.Open(PrefsPath, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(string.Join("\n", BetaEnabled));
        }
        catch (Exception e) { GD.PrintErr($"[BetaArt] SavePrefs failed: {e}"); }
    }

    internal static readonly System.Reflection.FieldInfo CardsField =
        AccessTools.Field(typeof(NInspectCardScreen), "_cards");
    internal static readonly System.Reflection.FieldInfo IndexField =
        AccessTools.Field(typeof(NInspectCardScreen), "_index");
    internal static readonly System.Reflection.FieldInfo CardField =
        AccessTools.Field(typeof(NInspectCardScreen), "_card");
    internal static readonly System.Reflection.FieldInfo UpgradeTickboxField =
        AccessTools.Field(typeof(NInspectCardScreen), "_upgradeTickbox");
    internal static readonly System.Reflection.MethodInfo UpdateDisplayMethod =
        AccessTools.Method(typeof(NInspectCardScreen), "UpdateCardDisplay");

    internal static void SetOwnerRecursive(Node node, Node owner)
    {
        if (node != owner) node.Owner = owner;
        foreach (var child in node.GetChildren())
            SetOwnerRecursive(child, owner);
    }

    internal static readonly System.Reflection.FieldInfo TB_ImageContainer =
        AccessTools.Field(typeof(NTickbox), "_imageContainer");
    internal static readonly System.Reflection.FieldInfo TB_TickedImage =
        AccessTools.Field(typeof(NTickbox), "_tickedImage");
    internal static readonly System.Reflection.FieldInfo TB_NotTickedImage =
        AccessTools.Field(typeof(NTickbox), "_notTickedImage");
    internal static readonly System.Reflection.FieldInfo TB_BaseScale =
        AccessTools.Field(typeof(NTickbox), "_baseScale");
    internal static readonly System.Reflection.FieldInfo TB_Hsv =
        AccessTools.Field(typeof(NTickbox), "_hsv");

    internal static void SetBetaInteractable(ScreenState state, bool hasBeta, bool positioned)
    {
        if (state.BetaTickbox == null) return;
        var modulate = hasBeta ? Colors.White : new Color(1, 1, 1, 0.5f);
        var filter   = hasBeta ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        state.BetaTickbox.Visible     = positioned;
        state.BetaTickbox.Modulate    = modulate;
        state.BetaTickbox.MouseFilter = filter;
        if (state.BetaLabel != null)
        {
            state.BetaLabel.Visible     = positioned;
            state.BetaLabel.Modulate    = modulate;
            state.BetaLabel.MouseFilter = filter;
        }
    }

    internal static bool HasBetaArt(CardModel model) =>
        model.BetaPortraitPath?.Contains("/beta/") == true
        && ResourceLoader.Exists(model.BetaPortraitPath);

    internal static string GetCardKey(CardModel model) => model.BetaPortraitPath;

    internal static void ApplyBetaPortrait(NCard cardNode)
    {
        var model    = cardNode.Model;
        var betaPath = model.BetaPortraitPath;
        if (!HasBetaArt(model)) return;

        var betaTexture = ResourceLoader.Load<Texture2D>(betaPath, null, ResourceLoader.CacheMode.Reuse);
        if (betaTexture == null)
        {
            GD.PrintErr($"[BetaArt] Failed to load beta texture: {betaPath}");
            return;
        }

        bool isAncient   = model.Rarity == CardRarity.Ancient;
        var portraitRect = cardNode.GetNodeOrNull<TextureRect>(isAncient ? "%AncientPortrait" : "%Portrait");
        if (portraitRect != null)
            portraitRect.Texture = betaTexture;
    }

    internal static void RevertToNormalPortrait(NCard cardNode)
    {
        var model = cardNode.Model;
        var key   = GetCardKey(model);
        if (key == null) return;

        bool isAncient   = model.Rarity == CardRarity.Ancient;
        var portraitRect = cardNode.GetNodeOrNull<TextureRect>(isAncient ? "%AncientPortrait" : "%Portrait");
        if (portraitRect == null) return;

        var normalPath = key.Replace("/beta/", "/");
        var normalTex  = ResourceLoader.Load<Texture2D>(normalPath, null, ResourceLoader.CacheMode.Reuse);
        if (normalTex == null) return;
        portraitRect.Texture = normalTex;
    }

    internal static void RefreshMatchingCardsInScene(string betaPortraitPath, bool apply)
    {
        var root = (Engine.GetMainLoop() as SceneTree)?.Root;
        if (root == null) return;
        RefreshCardsUnder(root, betaPortraitPath, apply);
    }

    private static void RefreshCardsUnder(Node node, string path, bool apply)
    {
        if (node is NCard card && GodotObject.IsInstanceValid(card)
            && card.Model != null && BetaArtState.GetCardKey(card.Model) == path)
        {
            if (apply) ApplyBetaPortrait(card);
            else       RevertToNormalPortrait(card);
        }
        foreach (var child in node.GetChildren())
            RefreshCardsUnder(child, path, apply);
    }

    internal static void OnBetaToggled(NInspectCardScreen screen, bool pressed)
    {
        if (SuppressToggled) return;
        if (!States.TryGetValue(screen, out var state) || state.BetaTickbox == null)
            return;
        try
        {
            var cards = CardsField.GetValue(screen) as List<CardModel>;
            var index = (int)(IndexField.GetValue(screen) ?? 0);
            if (cards == null || index < 0 || index >= cards.Count) return;

            string key = GetCardKey(cards[index]);
            if (pressed) BetaEnabled.Add(key);
            else BetaEnabled.Remove(key);

            UpdateDisplayMethod.Invoke(screen, null);
            RefreshMatchingCardsInScene(key, pressed);
            SavePrefs();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] OnBetaToggled failed: {e}");
        }
    }
}

internal class ScreenState
{
    public NTickbox? BetaTickbox;
    public Label? BetaLabel;
    public bool Positioned;
}

[HarmonyPatch(typeof(NInspectCardScreen), "Open",
    new[] { typeof(List<CardModel>), typeof(int), typeof(bool) })]
public static class NInspectCardScreen_Open_Patch
{
    public static void Postfix(NInspectCardScreen __instance)
    {
        try
        {
            var state = BetaArtState.States.GetOrCreateValue(__instance);

            if (state.BetaTickbox == null)
            {
                var srcTickbox = BetaArtState.UpgradeTickboxField?.GetValue(__instance) as NTickbox;
                var srcLabel   = __instance.GetNodeOrNull<MegaLabel>("%ShowUpgradeLabel");

                if (srcTickbox == null)
                {
                    GD.PrintErr("[BetaArt] Open: could not get _upgradeTickbox.");
                    return;
                }

                var betaTickbox = srcTickbox.Duplicate(15) as NTickbox;
                if (betaTickbox == null)
                {
                    GD.PrintErr("[BetaArt] Open: Duplicate returned null.");
                    return;
                }
                betaTickbox.Visible   = false;
                betaTickbox.FocusMode = Control.FocusModeEnum.None;
                RestoreUniqueNames(srcTickbox, betaTickbox);
                __instance.AddChild(betaTickbox);

                WireTickboxFields(srcTickbox, betaTickbox);
                HideDescendantLabels(betaTickbox);

                betaTickbox.IsTicked = false;
                betaTickbox.Toggled += (NTickbox tb) =>
                    BetaArtState.OnBetaToggled(__instance, tb.IsTicked);

                Label? betaLabel = new Label();
                betaLabel.Text = "Beta Art";
                betaLabel.Visible = false;
                betaLabel.MouseFilter = Control.MouseFilterEnum.Stop;
                __instance.AddChild(betaLabel);
                if (srcLabel != null)
                {
                    var font = srcLabel.GetThemeFont("font");
                    if (font != null) betaLabel.AddThemeFontOverride("font", font);
                    int fontSize = srcLabel.GetThemeFontSize("font_size");
                    if (fontSize > 0) betaLabel.AddThemeFontSizeOverride("font_size", fontSize);
                    var color = srcLabel.GetThemeColor("font_color");
                    betaLabel.AddThemeColorOverride("font_color", color);
                }
                var capturedTb  = betaTickbox;
                var capturedScr = __instance;
                betaLabel.GuiInput += (InputEvent e) => {
                    if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    {
                        bool next = !capturedTb.IsTicked;
                        BetaArtState.SuppressToggled = true;
                        capturedTb.IsTicked = next;
                        BetaArtState.SuppressToggled = false;
                        BetaArtState.OnBetaToggled(capturedScr, next);
                    }
                };

                state.BetaTickbox = betaTickbox;
                state.BetaLabel   = betaLabel;

                InitTickboxState(__instance, state);
                GD.Print("[BetaArt] Open: BetaTickbox created.");
            }

            if (state.Positioned)
                return;

            var upgradeControl = BetaArtState.UpgradeTickboxField?.GetValue(__instance) as Control;
            var upgradeLabel   = __instance.GetNodeOrNull<MegaLabel>("%ShowUpgradeLabel");
            if (upgradeControl == null) return;

            var tb2    = state.BetaTickbox;
            var label2 = state.BetaLabel;

            var capturedState = state;
            var capturedScr2  = __instance;
            Callable.From(() => Callable.From(() =>
            {
                try
                {
                    float rightEdge = upgradeControl.GlobalPosition.X + upgradeControl.Size.X;
                    if (upgradeLabel != null)
                        rightEdge = Mathf.Max(rightEdge,
                            upgradeLabel.GlobalPosition.X + upgradeLabel.Size.X);

                    float centerY  = upgradeControl.GlobalPosition.Y + upgradeControl.Size.Y * 0.5f;
                    float iconSize = upgradeControl.Size.Y;

                    float tbX = rightEdge + 24f;
                    tb2.GlobalPosition = new Vector2(tbX, centerY - iconSize * 0.5f);
                    tb2.Size = new Vector2(iconSize, iconSize);

                    if (label2 != null)
                    {
                        label2.GlobalPosition = new Vector2(
                            tbX + iconSize + 8f,
                            centerY - label2.Size.Y * 0.5f);
                    }

                    var cards2 = BetaArtState.CardsField.GetValue(capturedScr2) as List<CardModel>;
                    var index2 = (int)(BetaArtState.IndexField.GetValue(capturedScr2) ?? 0);
                    bool hasBeta2 = cards2 != null && index2 >= 0 && index2 < cards2.Count
                                    && BetaArtState.HasBetaArt(cards2[index2]);
                    capturedState.Positioned = true;
                    BetaArtState.SetBetaInteractable(capturedState, hasBeta2, true);
                    GD.Print($"[BetaArt] Positioned tb={tb2.GlobalPosition} size={tb2.Size}" +
                             (label2 != null ? $" label={label2.GlobalPosition} size={label2.Size} text='{label2.Text}'" : " no label"));
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[BetaArt] positioning failed: {e}");
                }
            }).CallDeferred()).CallDeferred();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] Open patch failed: {e}");
        }
    }

    private static void RestoreUniqueNames(Node src, Node dup)
    {
        int count = Math.Min(src.GetChildCount(), dup.GetChildCount());
        for (int i = 0; i < count; i++)
        {
            var srcChild = src.GetChild(i);
            var dupChild = dup.GetChild(i);
            if (srcChild.UniqueNameInOwner)
                dupChild.UniqueNameInOwner = true;
            RestoreUniqueNames(srcChild, dupChild);
        }
    }

    private static void WireTickboxFields(NTickbox src, NTickbox dup)
    {
        try
        {
            var srcIC  = BetaArtState.TB_ImageContainer.GetValue(src) as Control;
            var srcTI  = BetaArtState.TB_TickedImage.GetValue(src)    as Control;
            var srcNTI = BetaArtState.TB_NotTickedImage.GetValue(src)  as Control;
            if (srcIC == null) return;

            int icIdx = IndexOfChild(src, srcIC);
            if (icIdx < 0 || icIdx >= dup.GetChildCount()) return;
            var dupIC = dup.GetChild<Control>(icIdx);

            BetaArtState.TB_ImageContainer.SetValue(dup, dupIC);
            BetaArtState.TB_BaseScale.SetValue(dup, dupIC.Scale);

            var dupMat = dupIC.Material?.Duplicate() as ShaderMaterial;
            if (dupMat != null)
            {
                dupIC.Material = dupMat;
                BetaArtState.TB_Hsv.SetValue(dup, dupMat);
            }

            if (srcTI != null)
            {
                int tiIdx = IndexOfChild(srcIC, srcTI);
                if (tiIdx >= 0 && tiIdx < dupIC.GetChildCount())
                    BetaArtState.TB_TickedImage.SetValue(dup, dupIC.GetChild<Control>(tiIdx));
            }
            if (srcNTI != null)
            {
                int ntiIdx = IndexOfChild(srcIC, srcNTI);
                if (ntiIdx >= 0 && ntiIdx < dupIC.GetChildCount())
                    BetaArtState.TB_NotTickedImage.SetValue(dup, dupIC.GetChild<Control>(ntiIdx));
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] WireTickboxFields failed: {e}");
        }
    }

    private static int IndexOfChild(Node parent, Node child)
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
            if (parent.GetChild(i) == child) return i;
        return -1;
    }

    private static void HideDescendantLabels(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Label lbl)
                lbl.Visible = false;
            HideDescendantLabels(child);
        }
    }

    private static void InitTickboxState(NInspectCardScreen screen, ScreenState state)
    {
        if (state.BetaTickbox == null) return;
        try
        {
            var cards = BetaArtState.CardsField.GetValue(screen) as List<CardModel>;
            var index = (int)(BetaArtState.IndexField.GetValue(screen) ?? 0);
            if (cards == null || index < 0 || index >= cards.Count) return;

            var  model   = cards[index];
            bool hasBeta = BetaArtState.HasBetaArt(model);

            bool ticked = hasBeta && BetaArtState.BetaEnabled.Contains(BetaArtState.GetCardKey(model));
            BetaArtState.SuppressToggled = true;
            state.BetaTickbox.IsTicked = ticked;
            BetaArtState.SuppressToggled = false;
            // Visibility will be set by the deferred positioning callback.
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] InitTickboxState failed: {e}");
        }
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), "SetCard")]
public static class NInspectCardScreen_SetCard_Patch
{
    public static void Postfix(NInspectCardScreen __instance)
    {
        if (!BetaArtState.States.TryGetValue(__instance, out var state) || state.BetaTickbox == null)
            return;
        try
        {
            var cards = BetaArtState.CardsField.GetValue(__instance) as List<CardModel>;
            var index = (int)(BetaArtState.IndexField.GetValue(__instance) ?? 0);
            if (cards == null || index < 0 || index >= cards.Count) return;

            var  model   = cards[index];
            bool hasBeta = BetaArtState.HasBetaArt(model);

            GD.Print($"[BetaArt] SetCard: {model.BetaPortraitPath}, hasBeta={hasBeta}");

            BetaArtState.SetBetaInteractable(state, hasBeta, state.Positioned);

            bool ticked = hasBeta && BetaArtState.BetaEnabled.Contains(BetaArtState.GetCardKey(model));
            BetaArtState.SuppressToggled = true;
            state.BetaTickbox.IsTicked = ticked;
            BetaArtState.SuppressToggled = false;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] SetCard patch failed: {e}");
        }
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), "UpdateCardDisplay")]
public static class NInspectCardScreen_UpdateCardDisplay_Patch
{
    public static void Postfix(NInspectCardScreen __instance)
    {
        try
        {
            var cardNode = BetaArtState.CardField.GetValue(__instance) as NCard;
            if (cardNode?.Model == null) return;

            string key = BetaArtState.GetCardKey(cardNode.Model);
            if (BetaArtState.BetaEnabled.Contains(key))
                BetaArtState.ApplyBetaPortrait(cardNode);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] UpdateCardDisplay patch failed: {e}");
        }
    }
}

[HarmonyPatch(typeof(NTickbox), "ConnectSignals")]
public static class NTickbox_ConnectSignals_Patch
{
    public static void Prefix(NTickbox __instance)
    {
        if (!__instance.IsInsideTree()) return;
        if (__instance.GetChildCount() == 0 || __instance.GetChild(0).Owner != null) return;
        var parent = __instance.GetParent();
        if (parent != null)
            BetaArtState.SetOwnerRecursive(__instance, parent);
    }
}

[HarmonyPatch(typeof(NCard), "UpdateVisuals")]
public static class NCard_UpdateVisuals_Patch
{
    public static void Postfix(NCard __instance)
    {
        if (__instance.Model == null) return;
        var key = BetaArtState.GetCardKey(__instance.Model);
        if (key == null) return;
        try
        {
            if (!__instance.IsInsideTree())
            {
                // Card is being set up before entering the scene tree (common during deck layout).
                // Defer ResourceLoader calls to TreeEntered so they don't fire during the game's
                // async startup loading, which can deadlock the resource loader on second launch.
                if (BetaArtState.BetaEnabled.Contains(key))
                {
                    var capturedCard = __instance;
                    var capturedKey  = key;
                    void Handler()
                    {
                        capturedCard.TreeEntered -= Handler;
                        if (!GodotObject.IsInstanceValid(capturedCard) || capturedCard.Model == null) return;
                        if (BetaArtState.GetCardKey(capturedCard.Model) == capturedKey
                            && BetaArtState.BetaEnabled.Contains(capturedKey))
                            BetaArtState.ApplyBetaPortrait(capturedCard);
                    }
                    __instance.TreeEntered += Handler;
                }
                return;
            }

            if (BetaArtState.BetaEnabled.Contains(key))
            {
                BetaArtState.ApplyBetaPortrait(__instance);
            }
            else
            {
                // Portrait is still showing a beta texture but beta is disabled for this card —
                // revert it. Catches deck cards that RefreshMatchingCardsInScene couldn't reach.
                bool isAncient   = __instance.Model.Rarity == CardRarity.Ancient;
                var portraitRect = __instance.GetNodeOrNull<TextureRect>(isAncient ? "%AncientPortrait" : "%Portrait");
                if (portraitRect?.Texture?.ResourcePath?.Contains("/beta/") == true)
                    BetaArtState.RevertToNormalPortrait(__instance);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BetaArt] NCard.UpdateVisuals patch failed: {e}");
        }
    }
}
