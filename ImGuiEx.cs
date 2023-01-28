﻿using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Interface.Internal.Notifications;
using Lumina.Excel;

namespace ImGuiNET;

public static partial class ImGuiEx
{
    public static void SetItemTooltip(string s, ImGuiHoveredFlags flags = ImGuiHoveredFlags.None)
    {
        if (ImGui.IsItemHovered(flags))
            ImGui.SetTooltip(s);
    }

    public static bool IsItemDoubleClicked(ImGuiMouseButton button = ImGuiMouseButton.Left, ImGuiHoveredFlags flags = ImGuiHoveredFlags.None) =>
        ImGui.IsMouseDoubleClicked(button) && ImGui.IsItemHovered(flags);

    public static bool IsItemReleased(ImGuiMouseButton button = ImGuiMouseButton.Left, ImGuiHoveredFlags flags = ImGuiHoveredFlags.None) =>
        ImGui.IsMouseReleased(button) && ImGui.IsItemHovered(flags);

    // Why is this not a basic feature of ImGui...
    private static readonly Stack<float> fontScaleStack = new();
    private static float curScale = 1;
    public static void PushFontScale(float scale)
    {
        fontScaleStack.Push(curScale);
        curScale = scale;
        ImGui.SetWindowFontScale(curScale);
    }

    public static void PopFontScale()
    {
        curScale = fontScaleStack.Pop();
        ImGui.SetWindowFontScale(curScale);
    }

    public static void PushFontSize(float size) => PushFontScale(size / ImGui.GetFont().FontSize);

    public static void PopFontSize() => PopFontScale();

    public static float GetFontScale() => curScale;

    public static float GetFontSize() => curScale * ImGui.GetFont().FontSize;

    public static void ClampWindowPosToViewport()
    {
        var viewport = ImGui.GetWindowViewport();
        if (ImGui.IsWindowAppearing() || viewport.ID != ImGuiHelpers.MainViewport.ID) return;

        var pos = viewport.Pos;
        ClampWindowPos(pos, pos + viewport.Size);
    }

    public static void ClampWindowPos(Vector2 max) => ClampWindowPos(Vector2.Zero, max);

    public static void ClampWindowPos(Vector2 min, Vector2 max)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var x = Math.Min(Math.Max(pos.X, min.X), max.X - size.X);
        var y = Math.Min(Math.Max(pos.Y, min.Y), max.Y - size.Y);
        ImGui.SetWindowPos(new Vector2(x, y));
    }

    public static bool IsWindowInMainViewport() => ImGui.GetWindowViewport().ID == ImGuiHelpers.MainViewport.ID;

    public static bool ShouldDrawInViewport() => IsWindowInMainViewport() || Util.IsWindowFocused;

    public static void ShouldDrawInViewport(out bool b) => b = ShouldDrawInViewport();

    // Helper function for displaying / hiding windows outside of the main viewport when the game isn't focused, returns the bool to allow using it in if statements to reduce code
    public static bool SetBoolOnGameFocus(ref bool b)
    {
        if (!b)
            b = Util.IsWindowFocused;
        return b;
    }

    public static string GetClipboardTextOrDefault(string def = "")
    {
        try { return ImGui.GetClipboardText(); }
        catch { return def; }
    }

    private static bool sliderEnabled = false;
    private static bool sliderVertical = false;
    private static float sliderInterval = 0;
    private static int lastHitInterval = 0;
    private static Action<bool, bool, bool> sliderAction;
    public static void SetupSlider(bool vertical, float interval, Action<bool, bool, bool> action)
    {
        sliderEnabled = true;
        sliderVertical = vertical;
        sliderInterval = interval;
        lastHitInterval = 0;
        sliderAction = action;
    }

    public static void DoSlider()
    {
        if (!sliderEnabled) return;

        // You can blame ImGui for this
        var popupOpen = !ImGui.IsPopupOpen("_SLIDER") && ImGui.IsPopupOpen(null, ImGuiPopupFlags.AnyPopup);
        if (!popupOpen)
        {
            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(new Vector2(-100));
            ImGui.OpenPopup("_SLIDER", ImGuiPopupFlags.NoOpenOverItems);
            if (!ImGui.BeginPopup("_SLIDER")) return;
        }

        var drag = sliderVertical ? ImGui.GetMouseDragDelta().Y : ImGui.GetMouseDragDelta().X;
        var dragInterval = (int)(drag / sliderInterval);
        var hit = false;
        var increment = false;
        if (dragInterval > lastHitInterval)
        {
            hit = true;
            increment = true;
        }
        else if (dragInterval < lastHitInterval)
            hit = true;

        var closing = !ImGui.IsMouseDown(ImGuiMouseButton.Left);

        if (lastHitInterval != dragInterval)
        {
            while (lastHitInterval != dragInterval)
            {
                lastHitInterval += increment ? 1 : -1;
                sliderAction(hit, increment, closing && lastHitInterval == dragInterval);
            }
        }
        else
            sliderAction(false, false, closing);

        if (closing)
            sliderEnabled = false;

        if (!popupOpen)
            ImGui.EndPopup();
    }

    // ?????????
    public static void PushClipRectFullScreen() => ImGui.GetWindowDrawList().PushClipRectFullScreen();

    public static void TextCopyable(string text)
    {
        ImGui.TextUnformatted(text);

        if (!ImGui.IsItemHovered()) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
            ImGui.SetClipboardText(text);
    }

    public static void TextCopyable(Vector4 color, string text)
    {
        ImGui.TextColored(color, text);

        if (!ImGui.IsItemHovered()) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
            ImGui.SetClipboardText(text);
    }

    public static Vector2 RotateVector(Vector2 v, float a)
    {
        var aCos = (float)Math.Cos(a);
        var aSin = (float)Math.Sin(a);
        return RotateVector(v, aCos, aSin);
    }

    public static Vector2 RotateVector(Vector2 v, float aCos, float aSin) => new(v.X * aCos - v.Y * aSin, v.X * aSin + v.Y * aCos);

    private static string search = string.Empty;
    private static HashSet<ExcelRow> filtered;
    public static bool ExcelSheetCombo<T>(string id, out T selected, Func<ExcelSheet<T>, string> getPreview, ImGuiComboFlags flags, Func<T, string, bool> searchPredicate, Func<T, bool> selectableDrawing) where T : ExcelRow
    {
        var sheet = DalamudApi.DataManager.GetExcelSheet<T>();
        return ExcelSheetCombo(id, out selected, getPreview(sheet), flags, sheet, searchPredicate, selectableDrawing);
    }

    public static bool ExcelSheetCombo<T>(string id, out T selected, string preview, ImGuiComboFlags flags, ExcelSheet<T> sheet, Func<T, string, bool> searchPredicate, Func<T, bool> drawRow) where T : ExcelRow
    {
        selected = null;
        if (!ImGui.BeginCombo(id, preview, flags)) return false;

        if (ImGui.IsWindowAppearing() && ImGui.IsWindowFocused() && !ImGui.IsAnyItemActive())
        {
            search = string.Empty;
            filtered = null;
            ImGui.SetKeyboardFocusHere(0);
        }

        if (ImGui.InputText("##ExcelSheetComboSearch", ref search, 128))
            filtered = null;

        filtered ??= sheet.Where(s => searchPredicate(s, search)).Select(s => (ExcelRow)s).ToHashSet();

        var i = 0;
        foreach (var row in filtered.Cast<T>())
        {
            ImGui.PushID(i++);
            if (drawRow(row))
                selected = row;
            ImGui.PopID();

            if (selected == null) continue;
            ImGui.EndCombo();
            return true;
        }

        ImGui.EndCombo();
        return false;
    }

    public static bool FontButton(string label, ImFontPtr font)
    {
        ImGui.PushFont(font);
        var ret = ImGui.Button(label);
        ImGui.PopFont();
        return ret;
    }

    public static bool FontButton(string label, ImFontPtr font, Vector2 size)
    {
        ImGui.PushFont(font);
        var ret = ImGui.Button(label, size);
        ImGui.PopFont();
        return ret;
    }

    public class HeaderIconOptions
    {
        public int Position { get; init; } = 1;
        public Vector2 Offset { get; init; } = Vector2.Zero;
        public ImGuiMouseButton MouseButton { get; init; } = ImGuiMouseButton.Left;
        public string Tooltip { get; init; } = string.Empty;
        public uint Color { get; init; } = 0xFFFFFFFF;
        public bool ToastTooltipOnClick { get; init; } = false;
        public ImGuiMouseButton ToastTooltipOnClickButton { get; init; } = ImGuiMouseButton.Left;
    }

    public static bool AddHeaderIcon(string id, FontAwesomeIcon icon, HeaderIconOptions options)
    {
        if (ImGui.IsWindowCollapsed()) return false;

        var scale = ImGuiHelpers.GlobalScale;
        var prevCursorPos = ImGui.GetCursorPos();
        var buttonSize = new Vector2(20 * scale);
        var buttonPos = new Vector2(ImGui.GetWindowWidth() - buttonSize.X - 17 * options.Position * scale - ImGui.GetStyle().FramePadding.X * 2, 0) + options.Offset;
        ImGui.SetCursorPos(buttonPos);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRectFullScreen();

        var pressed = false;
        ImGui.InvisibleButton(id, buttonSize);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var halfSize = ImGui.GetItemRectSize() / 2;
        var center = itemMin + halfSize;
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
        {
            if (!string.IsNullOrEmpty(options.Tooltip))
                ImGui.SetTooltip(options.Tooltip);
            ImGui.GetWindowDrawList().AddCircleFilled(center, halfSize.X, ImGui.GetColorU32(ImGui.IsMouseDown(ImGuiMouseButton.Left) ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered));
            if (ImGui.IsMouseReleased(options.MouseButton))
                pressed = true;
            if (options.ToastTooltipOnClick && ImGui.IsMouseReleased(options.ToastTooltipOnClickButton))
                DalamudApi.PluginInterface.UiBuilder.AddNotification(options.Tooltip!, null, NotificationType.Info);
        }

        ImGui.SetCursorPos(buttonPos);
        ImGui.PushFont(UiBuilder.IconFont);
        var iconString = icon.ToIconString();
        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), itemMin + halfSize - ImGui.CalcTextSize(iconString) / 2 + Vector2.One, options.Color, iconString);
        ImGui.PopFont();

        ImGui.PopClipRect();
        ImGui.SetCursorPos(prevCursorPos);

        return pressed;
    }

    public static void AddDonationHeader(int position, string link = @"https://ko-fi.com/unknownx7")
    {
        if (AddHeaderIcon("_DONATE", FontAwesomeIcon.Heart, new HeaderIconOptions { Position = position, Color = 0xFF3030D0, MouseButton = ImGuiMouseButton.Right, Tooltip = $"Right click to go to {link}", ToastTooltipOnClick = true }))
            Util.StartProcess(link);
    }
}