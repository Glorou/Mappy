﻿using System.Drawing;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using KamiLib.Classes;
using KamiLib.CommandManager;
using KamiLib.Extensions;
using KamiLib.Window;
using Lumina.Excel.GeneratedSheets;
using Mappy.Classes;
using Mappy.Controllers;
using Mappy.Data;

namespace Mappy.Windows;

public class MapWindow : Window {
    public Vector2 MapDrawOffset { get; private set; }
    public bool IsMapHovered { get; private set; }
    public bool ProcessingCommand { get; set; }

    private bool isMapItemHovered;
    private bool isDragStarted;
    private Vector2 lastWindowSize;

    public MapWindow() : base("Mappy Map Window", new Vector2(400.0f, 250.0f)) {
        DisableWindowSounds = true;
        RegisterCommands();

        // Mirroring behavior doesn't let the close button work, so, remove it.
        ShowCloseButton = false;
    }

    public override bool DrawConditions()
        => IntegrationsController.ShouldShowMap();

    public override unsafe void PreOpenCheck() {
        IsOpen = AgentMap.Instance()->IsAgentActive();

        if (System.SystemConfig.KeepOpen) IsOpen = true;
        if (Service.ClientState is { IsLoggedIn: false } or { IsPvP: true }) IsOpen = false;
    }
    
    public override unsafe void OnOpen() {
        if (!AgentMap.Instance()->IsAgentActive()) {
            AgentMap.Instance()->Show();
        }
        
        YeetVanillaMap();

        if (ProcessingCommand) {
            ProcessingCommand = false;
            System.SystemConfig.FollowPlayer = false;
            return;
        }
        
        if (System.SystemConfig.FollowOnOpen) {
            System.IntegrationsController.OpenOccupiedMap();
            System.SystemConfig.FollowPlayer = true;
        }

        switch (System.SystemConfig.CenterOnOpen) {
            case CenterTarget.Player when Service.ClientState.LocalPlayer is {} localPlayer:
                System.MapRenderer.CenterOnGameObject(localPlayer);
                break;

            case CenterTarget.Map:
                System.SystemConfig.FollowPlayer = false;
                System.MapRenderer.DrawOffset = Vector2.Zero;
                break;

            case CenterTarget.Disabled:
            default:
                break;
        }
    }

    protected override void DrawContents() {
        UpdateStyle();
        UpdateSizePosition();
        IsMapHovered = WindowBounds.IsBoundedBy(ImGui.GetMousePos(), ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionMax());
        isMapItemHovered = false;
        
        MapDrawOffset = ImGui.GetCursorScreenPos();
        using var fade = ImRaii.PushStyle(ImGuiStyleVar.Alpha, System.SystemConfig.FadePercent,  ShouldFade());
        using (var renderChild = ImRaii.Child("render_child", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar)) {
            if (!renderChild) return;
            if (!System.SystemConfig.AcceptedSpoilerWarning) {
                using (ImRaii.PushColor(ImGuiCol.Text, KnownColor.Orange.Vector())) {
                    const string warningLine1 = "Warning, Mappy does not protect you from spoilers and will show everything.";
                    const string warningLine2 = "Do not use Mappy if you are not comfortable with this.";

                    ImGui.SetCursorPos(ImGui.GetContentRegionAvail() / 2.0f - (ImGui.CalcTextSize(warningLine1) * 2.0f) with { X = 0.0f });
                    ImGuiHelpers.CenteredText(warningLine1);
                    ImGuiHelpers.CenteredText(warningLine2);
                }
                
                ImGuiHelpers.ScaledDummy(30.0f);
                ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X / 3.0f);
                using (ImRaii.Disabled(!(ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl))) {
                    if (ImGui.Button("I understand", new Vector2(ImGui.GetContentRegionAvail().X / 2.0f, 23.0f * ImGuiHelpers.GlobalScale))) {
                        System.SystemConfig.AcceptedSpoilerWarning = true;
                        System.SystemConfig.Save();
                    }
                    
                    using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1.0f)) {
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                            ImGui.SetTooltip("Hold Shift + Control while clicking activate button");
                        }
                    }
                }

                return;
            }
            
            if (renderChild) {
                System.MapRenderer.Draw();
                ImGui.SetCursorPos(Vector2.Zero);
                
                DrawToolbar();
                isMapItemHovered |= ImGui.IsItemHovered();
                
                DrawCoordinateBar();
                isMapItemHovered |= ImGui.IsItemHovered();
            }
        }
        isMapItemHovered |= ImGui.IsItemHovered();
        
        // Process Inputs
        ProcessInputs();
    }

    private void ProcessInputs() {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
            ImGui.OpenPopup("Mappy_Context_Menu");
        }
        else {
            if (isMapItemHovered) {
                if (System.SystemConfig.EnableShiftDragMove && ImGui.GetIO().KeyShift) {
                    Flags &= ~ImGuiWindowFlags.NoMove;
                }
                else {
                    ProcessMouseScroll();
                    ProcessMapDragStart();
                    Flags |= ImGuiWindowFlags.NoMove;
                }
            }
            
            ProcessMapDragDragging();
            ProcessMapDragEnd();
        }

        // Draw Context Menu
        DrawGeneralContextMenu();
    }
    
    private unsafe void UpdateStyle() {
        if (System.SystemConfig.HideWindowFrame) {
            Flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
        }
        else {
            Flags &= ~(ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground);
        }

        if (System.SystemConfig.LockWindow) {
            Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        }
        else {
            Flags &= ~(ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);
        }

        RespectCloseHotkey = !System.SystemConfig.IgnoreEscapeKey;

        if (RespectCloseHotkey && Service.KeyState[VirtualKey.ESCAPE] && IsFocused) {
            AgentMap.Instance()->Hide();
        }
        
        YeetVanillaMap();
        
        if (System.SystemConfig.FollowPlayer && Service.ClientState is { LocalPlayer: {} localPlayer}) {
            System.MapRenderer.CenterOnGameObject(localPlayer);
        }
        
        if (System.SystemConfig.LockCenterOnMap) {
            System.SystemConfig.FollowPlayer = false;
            System.MapRenderer.DrawOffset = Vector2.Zero;
        }
    }

    private bool ShouldShowToolbar() {
        if (isDragStarted) return false;
        if (System.SystemConfig.ShowToolbarOnHover && IsMapHovered) return true;
        if (System.SystemConfig.AlwaysShowToolbar) return true;

        return false;
    }
    
    private unsafe void DrawToolbar() {
        var toolbarSize = new Vector2(ImGui.GetContentRegionMax().X, 33.0f * ImGuiHelpers.GlobalScale);
        
        if (!ShouldShowToolbar()) return;
        using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero with { W = System.SystemConfig.ToolbarFade });
        using var toolbarChild = ImRaii.Child("toolbar_child", toolbarSize);
        if (!toolbarChild) return;
        
        ImGui.SetCursorPos(new Vector2(5.0f, 5.0f));
        
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.ArrowUp, "up", "Open Parent Map")) {
            var valueArgs = new AtkValue {
                Type = ValueType.Int, 
                Int = 5,
            };

            var returnValue = new AtkValue();
            AgentMap.Instance()->ReceiveEvent(&returnValue, &valueArgs, 1, 0);
        }
        
        ImGui.SameLine();
        
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.LayerGroup, "layers", "Show Map Layers")) {
            ImGui.OpenPopup("Mappy_Show_Layers");
        }

        DrawLayersContextMenu();
        
        ImGui.SameLine();
        
        using (var _ = ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int) ImGuiCol.ButtonActive], System.SystemConfig.FollowPlayer)) {
            if (MappyGuiTweaks.IconButton(FontAwesomeIcon.LocationArrow, "follow", "Toggle Follow Player")) {
                System.SystemConfig.FollowPlayer = !System.SystemConfig.FollowPlayer;
        
                if (System.SystemConfig.FollowPlayer) {
                    System.IntegrationsController.OpenOccupiedMap();
                }
            }
        }
        
        ImGui.SameLine();
        
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.ArrowsToCircle, "centerPlayer", "Center on Player") && Service.ClientState.LocalPlayer is not null) {
            // Don't center on player if we are already following the player.
            if (!System.SystemConfig.FollowPlayer) {
                System.IntegrationsController.OpenOccupiedMap();
                System.MapRenderer.CenterOnGameObject(Service.ClientState.LocalPlayer);
            }
        }
        
        ImGui.SameLine();
        
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.MapMarked, "centerMap", "Center on Map")) {
            System.SystemConfig.FollowPlayer = false;
            System.MapRenderer.DrawOffset = Vector2.Zero;
        }
        
        ImGui.SameLine();
        
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.Search, "search", "Search for Map")) {
            System.WindowManager.AddWindow(new MapSelectionWindow {
                SingleSelectionCallback = selection => {
                    if (selection?.Map != null) {
                        if (AgentMap.Instance()->SelectedMapId != selection.Map.RowId) {
                            System.IntegrationsController.OpenMap(selection.Map.RowId);
                        }

                        if (selection.MarkerLocation is {} location) {
                            System.SystemConfig.FollowPlayer = false;
                            System.MapRenderer.DrawOffset = -location + DrawHelpers.GetMapCenterOffsetVector();
                        }
                    }
                },
            }, WindowFlags.OpenImmediately | WindowFlags.RequireLoggedIn);
        }
        
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 25.0f * ImGuiHelpers.GlobalScale - ImGui.GetStyle().ItemSpacing.X);
        if (MappyGuiTweaks.IconButton(FontAwesomeIcon.Cog, "settings", "Open Settings")) {
            System.ConfigWindow.UnCollapseOrShow();
            ImGui.SetWindowFocus(System.ConfigWindow.WindowName);
        }
    }
    
    private unsafe void DrawCoordinateBar() {
        if (!System.SystemConfig.ShowCoordinateBar) return;
        
        var coordinateBarSize = new Vector2(ImGui.GetContentRegionMax().X, 20.0f * ImGuiHelpers.GlobalScale);
        ImGui.SetCursorPos(ImGui.GetContentRegionMax() - coordinateBarSize);
        
        using var childBackgroundStyle = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero with { W = System.SystemConfig.CoordinateBarFade });
        using var coordinateChild = ImRaii.Child("coordinate_child", coordinateBarSize);
        if (!coordinateChild) return;

        var offsetX = -AgentMap.Instance()->SelectedOffsetX;
        var offsetY = -AgentMap.Instance()->SelectedOffsetY;
        var scale = AgentMap.Instance()->SelectedMapSizeFactor;

        var characterMapPosition = MapUtil.WorldToMap(Service.ClientState.LocalPlayer?.Position ?? Vector3.Zero, offsetX, offsetY, 0, (uint)scale);
        var characterPosition = $"Character  {characterMapPosition.X:F1}  {characterMapPosition.Y:F1}";
        
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2.0f * ImGuiHelpers.GlobalScale);

        var characterStringSize = ImGui.CalcTextSize(characterPosition);
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X / 3.0f - characterStringSize.X / 2.0f);

        if (AgentMap.Instance()->SelectedMapId == AgentMap.Instance()->CurrentMapId) {
            ImGui.TextColored(System.SystemConfig.CoordinateTextColor, characterPosition);
        }

        if (IsMapHovered) {
            var cursorPosition = ImGui.GetMousePos() - MapDrawOffset;
            cursorPosition -= System.MapRenderer.DrawPosition;
            cursorPosition /= System.MapRenderer.Scale;
            cursorPosition -= new Vector2(1024.0f, 1024.0f);
            cursorPosition -= new Vector2(offsetX, offsetY);
            cursorPosition /= AgentMap.Instance()->SelectedMapSizeFactorFloat;
 
            var cursorMapPosition = MapUtil.WorldToMap(new Vector3(cursorPosition.X, 0.0f, cursorPosition.Y), offsetX, offsetY, 0, (uint)scale);
            var cursorPositionString = $"Cursor  {cursorMapPosition.X:F1}  {cursorMapPosition.Y:F1}";
            var cursorStringSize = ImGui.CalcTextSize(characterPosition);
            ImGui.SameLine(ImGui.GetContentRegionMax().X * 2.0f / 3.0f - cursorStringSize.X / 2.0f);
            ImGui.TextColored(System.SystemConfig.CoordinateTextColor, cursorPositionString);
        }
    }

    private void UpdateSizePosition() {
        var systemConfig = System.SystemConfig;
        var windowPosition = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        if (!IsFocused) {
            if (windowPosition != systemConfig.WindowPosition) {
                ImGui.SetWindowPos(systemConfig.WindowPosition);
            }

            if (windowSize != systemConfig.WindowSize) {
                ImGui.SetWindowSize(systemConfig.WindowSize);
            }
        }
        else { // If focused
            if (systemConfig.WindowPosition != windowPosition) {
                systemConfig.WindowPosition = windowPosition;
                systemConfig.Save();
            }

            if (systemConfig.WindowSize != windowSize) {
                systemConfig.WindowSize = windowSize;
                systemConfig.Save();
            }
        }
    }

    private unsafe void DrawGeneralContextMenu() {
        using var contextMenu = ImRaii.ContextPopup("Mappy_Context_Menu");
        if (!contextMenu) return;
        
        if (ImGui.MenuItem("Place Flag")) {
            var cursorPosition = ImGui.GetMousePosOnOpeningCurrentPopup(); // Get initial cursor position (screen relative)
            var mapChildOffset = MapDrawOffset; // Get the screen position we started drawing the map at
            var mapDrawOffset = System.MapRenderer.DrawPosition; // Get the map texture top left offset vector
            var textureClickLocation = (cursorPosition - mapChildOffset - mapDrawOffset) / System.MapRenderer.Scale; // Math
            var result = textureClickLocation - new Vector2(1024.0f, 1024.0f); // One of our vectors made the map centered, undo it.
            var scaledResult = result / DrawHelpers.GetMapScaleFactor() + DrawHelpers.GetRawMapOffsetVector(); // Apply offset x/y and scalefactor
                
            AgentMap.Instance()->IsFlagMarkerSet = 0;
            AgentMap.Instance()->SetFlagMapMarker(AgentMap.Instance()->SelectedTerritoryId, AgentMap.Instance()->SelectedMapId, scaledResult.X, scaledResult.Y);
            AgentChatLog.Instance()->InsertTextCommandParam(1048, false);
        }
        
        if (ImGui.MenuItem("Remove Flag", AgentMap.Instance()->IsFlagMarkerSet is not 0)) {
            AgentMap.Instance()->IsFlagMarkerSet = 0;
        }

        ImGuiHelpers.ScaledDummy(5.0f);

        if (ImGui.MenuItem("Center on Player", Service.ClientState.LocalPlayer is not null) && Service.ClientState.LocalPlayer is not null) {
            System.IntegrationsController.OpenOccupiedMap();
            System.MapRenderer.CenterOnGameObject(Service.ClientState.LocalPlayer);
        }
        
        if (ImGui.MenuItem("Center on Map")) {
            System.SystemConfig.FollowPlayer = false;
            System.MapRenderer.DrawOffset = Vector2.Zero;
        }

        ImGuiHelpers.ScaledDummy(5.0f);
        
        if (ImGui.MenuItem("Lock Zoom", "", ref System.SystemConfig.ZoomLocked)) {
            System.SystemConfig.Save();
        }
        
        ImGuiHelpers.ScaledDummy(5.0f);
        
        if (ImGui.MenuItem("Open Quest List", System.WindowManager.GetWindow<QuestListWindow>() is null))  {
            System.WindowManager.AddWindow(new QuestListWindow(), WindowFlags.OpenImmediately | WindowFlags.RequireLoggedIn);
        }

        if (ImGui.MenuItem("Open Fate List", System.WindowManager.GetWindow<FateListWindow>() is null)) {
            System.WindowManager.AddWindow(new FateListWindow(), WindowFlags.OpenImmediately | WindowFlags.RequireLoggedIn);
        }
    }
        
    private unsafe void DrawLayersContextMenu() {
        using var contextMenu = ImRaii.Popup("Mappy_Show_Layers");
        if (!contextMenu) return;

        var currentMap = Service.DataManager.GetExcelSheet<Map>()!.GetRow(AgentMap.Instance()->SelectedMapId);
        
        var layers = Service.DataManager.GetExcelSheet<Map>()!
            .Where(eachMap => eachMap.PlaceName.Row == currentMap!.PlaceName.Row)
            .Where(eachMap => eachMap.MapIndex != 0)
            .OrderBy(eachMap => eachMap.MapIndex)
            .ToList();

        if (layers.Count is 0) {
            ImGui.Text("No layers for this map");
        }
        
        foreach (var layer in layers) {
            if (ImGui.MenuItem(layer.PlaceNameSub.Value?.Name ?? "Unable to Parse Name", "", AgentMap.Instance()->SelectedMapId == layer.RowId)) {
                System.IntegrationsController.OpenMap(layer.RowId);
                System.SystemConfig.FollowPlayer = false;
                System.MapRenderer.DrawOffset = Vector2.Zero;
            }
        }
    }
    
    public override void OnClose() {
        UnYeetVanillaMap();
        
        System.SystemConfig.Save();
    }

    private static void ProcessMouseScroll() {
        if (System.SystemConfig.ZoomLocked) return;
        if (ImGui.GetIO().MouseWheel is 0) return;
        
        if (System.SystemConfig.UseLinearZoom) {
            System.MapRenderer.Scale += System.SystemConfig.ZoomSpeed * ImGui.GetIO().MouseWheel;
        }
        else {
            System.MapRenderer.Scale *= 1.0f + System.SystemConfig.ZoomSpeed * ImGui.GetIO().MouseWheel;
        }
    }
    
    private void ProcessMapDragDragging() {
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && isDragStarted) {
            System.MapRenderer.DrawOffset += ImGui.GetMouseDragDelta() / System.MapRenderer.Scale;
            ImGui.ResetMouseDragDelta();
        }
    }
    
    private void ProcessMapDragEnd() {
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
            isDragStarted = false;
        }
    }
    
    private void ProcessMapDragStart() {
        // Don't allow a drag to start if the window size is changing
        if (ImGui.GetWindowSize() == lastWindowSize) {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !isDragStarted) {
                isDragStarted = true;
                System.SystemConfig.FollowPlayer = false;
            }
        } else {
            lastWindowSize = ImGui.GetWindowSize();
            isDragStarted = false;
        }
    }
    
    private unsafe bool ShouldFade() 
        => System.SystemConfig.FadeMode.HasFlag(FadeMode.Always) ||
           System.SystemConfig.FadeMode.HasFlag(FadeMode.WhenFocused) && IsFocused ||
           System.SystemConfig.FadeMode.HasFlag(FadeMode.WhenMoving) && AgentMap.Instance()->IsPlayerMoving is not 0 ||
           System.SystemConfig.FadeMode.HasFlag(FadeMode.WhenUnFocused) && !IsFocused;

    private unsafe void YeetVanillaMap() {
        var addon = Service.GameGui.GetAddonByName<AddonAreaMap>("AreaMap");
        if (addon is null || addon->RootNode is null) return;
        
        addon->RootNode->SetPositionFloat(-9001.0f, -9001.0f);
        addon->RootNode->ToggleVisibility(false);
    }
    
    private unsafe void UnYeetVanillaMap() {
        var addon = Service.GameGui.GetAddonByName<AddonAreaMap>("AreaMap");
        if (addon is null || addon->RootNode is null) return;
        
        AgentMap.Instance()->Hide();
        addon->RootNode->SetPositionFloat(addon->X, addon->Y);
        addon->RootNode->ToggleVisibility(false);
        Service.Framework.RunOnTick(() => addon->RootNode->ToggleVisibility(true), delayTicks: 10);
    }
    
    
    private void RegisterCommands() {
        System.CommandManager.RegisterCommand(new ToggleCommandHandler {
            UseShowHideText = true,
            BaseActivationPath = "/map",
            EnableDelegate = _ => System.MapWindow.UnCollapseOrShow(),
            DisableDelegate = _ => System.MapWindow.Close(),
            ToggleDelegate = _ => System.MapWindow.UnCollapseOrToggle(),
        });
        
        System.CommandManager.RegisterCommand(new CommandHandler {
            ActivationPath = "/map/follow",
            Delegate = _ => {
                System.SystemConfig.FollowPlayer = true;
                System.SystemConfig.Save();
            },
        });
        
        System.CommandManager.RegisterCommand(new CommandHandler {
            ActivationPath = "/map/unfollow",
            Delegate = _ => {
                System.SystemConfig.FollowPlayer = false;
                System.SystemConfig.Save();
            },
        });
        
        System.CommandManager.RegisterCommand(new ToggleCommandHandler {
            BaseActivationPath = "/autofollow",
            EnableDelegate = _ => {
                System.SystemConfig.FollowOnOpen = true;
                System.SystemConfig.Save();
            },
            DisableDelegate = _ => {
                System.SystemConfig.FollowOnOpen = false;
                System.SystemConfig.Save();
            },
            ToggleDelegate = _ => {
                System.SystemConfig.FollowOnOpen = !System.SystemConfig.FollowOnOpen;
                System.SystemConfig.Save();
            },
        });
        
        System.CommandManager.RegisterCommand(new ToggleCommandHandler {
            BaseActivationPath = "/keepopen",
            EnableDelegate = _ => {
                System.SystemConfig.KeepOpen = true;
                System.SystemConfig.Save();
            },
            DisableDelegate = _ => {
                System.SystemConfig.KeepOpen = false;
                System.SystemConfig.Save();
            },
            ToggleDelegate = _ => {
                System.SystemConfig.KeepOpen = !System.SystemConfig.KeepOpen;
                System.SystemConfig.Save();
            },
        });
        
        System.CommandManager.RegisterCommand(new CommandHandler {
            ActivationPath = "/center/player",
            Delegate = _ => {
                if (Service.ClientState.LocalPlayer is { } localPlayer) {
                    System.MapRenderer.CenterOnGameObject(localPlayer);
                }
            },
        });
        
        System.CommandManager.RegisterCommand(new CommandHandler {
            ActivationPath = "/center/map",
            Delegate = _ => {
                System.SystemConfig.FollowPlayer = false;
                System.MapRenderer.DrawOffset = Vector2.Zero;
            },
        });
        
        // Easter Egg, don't recommend executing this command.
        System.CommandManager.RegisterCommand(new CommandHandler {
            ActivationPath = "/pyon",
            Hidden = true,
            Delegate = _ => {
                foreach (var index in Enumerable.Range(0, 20)) {
                    Service.Framework.RunOnTick(Toggle, delayTicks: 20 * index);
                }
            },
        });
    }
}