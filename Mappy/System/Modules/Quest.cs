﻿using System.Drawing;
using System.Linq;
using System.Numerics;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using KamiLib.AutomaticUserInterface;
using KamiLib.Caching;
using KamiLib.Utilities;
using KamiLib.Windows;
using Lumina.Excel.GeneratedSheets;
using Mappy.Abstracts;
using Mappy.Models;
using Mappy.Models.Enums;
using Mappy.Utility;

namespace Mappy.System.Modules;

[Category("ModuleColors")]
public interface IQuestColorConfig
{
    [ColorConfig("InProgressColor", 255, 69, 0, 45)]
    public Vector4 InProgressColor { get; set; }
    
    [ColorConfig("LeveQuestColor", 0, 133, 5, 97)]
    public Vector4 LeveQuestColor { get; set; }
}

[Category("ModuleConfig")]
public class QuestConfig : IModuleConfig, IIconConfig, ITooltipConfig, IQuestColorConfig
{
    public bool Enable { get; set; } = true;
    public int Layer { get; set; } = 11;
    public bool ShowIcon { get; set; } = true;
    public float IconScale { get; set; } = 0.50f;
    public bool ShowTooltip { get; set; } = true;
    public Vector4 TooltipColor { get; set; } = KnownColor.White.AsVector4();
    public Vector4 InProgressColor { get; set; } = KnownColor.OrangeRed.AsVector4() with { W = 0.33f };
    public Vector4 LeveQuestColor { get; set; } = new Vector4(0, 133, 5, 97) / 255.0f;
    
    [BoolConfig("HideUnacceptedQuests")]
    public bool HideUnacceptedQuests { get; set; } = false;

    [BoolConfig("HideAcceptedQuests")]
    public bool HideAcceptedQuests { get; set; } = false;

    [BoolConfig("HideLeveQuests")]
    public bool HideLeveQuests { get; set; } = false;
}

public unsafe class Quest : ModuleBase
{
    public override ModuleName ModuleName => ModuleName.QuestMarkers;
    public override IModuleConfig Configuration { get; protected set; } = new QuestConfig();

    public override void LoadForMap(MapData mapData) { }

    protected override bool ShouldDrawMarkers(Map map)
    {
        if (!GetConfig<QuestConfig>().ShowIcon) return false;
        
        return base.ShouldDrawMarkers(map);
    }

    protected override void DrawMarkers(Viewport viewport, Map map)
    {
        var config = GetConfig<QuestConfig>();

        if (!config.HideUnacceptedQuests) DrawUnacceptedQuests(map);
        if (!config.HideAcceptedQuests) DrawAcceptedQuests(viewport, map);
        if (!config.HideLeveQuests) DrawLeveQuests(viewport, map);
    }
    
    private void DrawLeveQuests(Viewport viewport, Map map)
    {
        var anyActive = false;
        
        foreach (var quest in QuestManager.Instance()->LeveQuestsSpan)
        {
            if (quest.LeveId is 0) continue;
            if (quest.Flags is 44) continue; // Complete
            
            var luminaData = LuminaCache<Leve>.Instance.GetRow(quest.LeveId)!;
            var level = LuminaCache<Level>.Instance.GetRow(luminaData.LevelStart.Row);
            if (level is null) continue;
            
            var journalGenre = LuminaCache<JournalGenre>.Instance.GetRow(luminaData.JournalGenre.Row)!;
            if (level.Map.Row != map.RowId) continue;
            
            DebugWindow.Print(quest.Flags + luminaData.Name.RawString);
            
            var icon = (uint) journalGenre.Icon;
            var tooltip = luminaData.Name.RawString;
            var ringColor = GetConfig<QuestConfig>().LeveQuestColor;
            var tooltipColor =  GetConfig<QuestConfig>().TooltipColor;
            var scale = GetConfig<QuestConfig>().IconScale;
            var showTooltip = GetConfig<QuestConfig>().ShowTooltip;

            DrawUtilities.DrawLevelObjective(level, icon, tooltip, ringColor, tooltipColor, viewport, map, showTooltip, scale, 50.0f);

            if (quest.Flags is not 32) continue; // InProgress
            
            anyActive = true;
            foreach (var activeLevel in MappySystem.QuestController.ActiveLevequestLevels)
            {
                var activeLevelData = LuminaCache<Level>.Instance.GetRow(activeLevel);
                if (activeLevelData is null) continue;

                DrawUtilities.DrawLevelObjective(activeLevelData, icon, tooltip, ringColor, tooltipColor, viewport, map, showTooltip, scale, 50.0f);
            }
        }

        if (!anyActive && MappySystem.QuestController.ActiveLevequestLevels.Count > 0)
        {
            MappySystem.QuestController.ActiveLevequestLevels.Clear();
        }
    }

    private void DrawUnacceptedQuests(Map map)
    {
        foreach (var (_ ,(mapIcon, level, questId, flags)) in MappySystem.QuestController.AllowedQuests)
        {
            if(flags is 6) continue;
            
            var levelInfo = LuminaCache<Level>.Instance.GetRow(level)!;
            if(levelInfo.Map.Row != map.RowId) continue;
            
            var position = Position.GetObjectPosition(new Vector2(levelInfo.X, levelInfo.Z), map);
            var showTooltip = GetConfig<QuestConfig>().ShowTooltip;
            var tooltipColor = GetConfig<QuestConfig>().TooltipColor;
            var scale = flags is 1 ? GetConfig<QuestConfig>().IconScale / 2.0f : GetConfig<QuestConfig>().IconScale;
            
            if (showTooltip)
            {
                switch (questId)
                {
                    case > 0xB0000 and < 0xC0000 when LuminaCache<CustomTalk>.Instance.GetRow(questId) is { MainOption.RawString: var mainOption, SubOption.RawString: var subOption }: // CustomTalk
                        var customTalkTooltip = mainOption.IsNullOrEmpty() ? subOption : mainOption;
                        DrawUtilities.DrawIcon(mapIcon, position, scale);
                        DrawUtilities.DrawTooltip(customTalkTooltip, tooltipColor, mapIcon);
                        break;

                    case > 0x230000 and < 0x240000 when LuminaCache<TripleTriad>.Instance.GetRow(questId) is { } triadInfo: // Triple Triad
                        DrawUtilities.DrawIcon(mapIcon, position, scale);
                        DrawTriadTooltip(triadInfo, tooltipColor, mapIcon);
                        break;
                    
                    case > 0x10000 and < 0x20000 when LuminaCache<CustomQuestSheet>.Instance.GetRow(questId) is { Name.RawString: var name, ClassJobLevel0: var classJobLevel } && !name.IsNullOrEmpty(): // Quest
                        DrawUtilities.DrawIcon(mapIcon, position, scale);
                        DrawUtilities.DrawTooltip($"Lv. {classJobLevel} {name}", tooltipColor, mapIcon);
                        break;
                    
                    case > 0x60000 and < 0x70000 when LuminaCache<GuildleveAssignment>.Instance.GetRow(questId) is { Type.RawString: var leveTooltip}: // Levequest Icon (vendor)
                        DrawUtilities.DrawIcon(mapIcon, position, scale);
                        DrawUtilities.DrawTooltip(leveTooltip, tooltipColor, mapIcon);
                        break;
                    
                    case > 0x170000 and < 0x180000:
                        DrawUtilities.DrawIcon(mapIcon, position, scale);
                        // 0x170001 - "Guildhests"
                        break;
                
                    default:
                        #if DEBUG
                        DebugWindow.Print($"0x{questId:X} - {questId} :: {mapIcon}");
                        #endif
                        break;
                }
            }
        }
    }

    private void DrawAcceptedQuests(Viewport viewport, Map map)
    {
        foreach (var quest in QuestManager.Instance()->NormalQuestsSpan)
        {
            if (quest.QuestId is 0) continue;

            foreach (var level in QuestHelpers.GetActiveLevelsForQuest(quest, map.RowId))
            {
                var luminaQuest = LuminaCache<CustomQuestSheet>.Instance.GetRow(quest.QuestId + 65536u)!;
                var journalIcon = luminaQuest.JournalGenre.Value?.Icon;
                if (journalIcon is null) continue;

                var icon = (uint) journalIcon;
                var tooltip = luminaQuest.Name.RawString;
                var ringColor = GetConfig<QuestConfig>().InProgressColor;
                var tooltipColor = GetConfig<QuestConfig>().TooltipColor;
                var scale = GetConfig<QuestConfig>().IconScale;
                var showTooltip = GetConfig<QuestConfig>().ShowTooltip;

                DrawUtilities.DrawLevelObjective(level, icon, tooltip, ringColor, tooltipColor, viewport, map, showTooltip, scale);
            }
        }
    }

    private void DrawTriadTooltip(TripleTriad triadInfo, Vector4 tooltipColor, uint mapIcon)
    {
        var tripleTriadMatchString = LuminaCache<Addon>.Instance.GetRow(9224)!.Text.RawString;
        var cardRewards = triadInfo.ItemPossibleReward
            .Where(reward => reward.Row is not 0)
            .Select(reward => reward.Value)
            .OfType<Item>()
            .Select(item => item.Name.RawString);
        
        DrawUtilities.DrawMultiTooltip(tripleTriadMatchString, string.Join("\n", cardRewards), tooltipColor, mapIcon);
    }
}