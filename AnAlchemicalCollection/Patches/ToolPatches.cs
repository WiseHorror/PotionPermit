using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CharacterIDEnum;
using GlobalEnum;
using HarmonyLib;
using InControl;
using InControl.NativeDeviceProfiles;
using Mono.Cecil;
using Sirenix.Utilities;
using TutorialEnum;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

namespace AnAlchemicalCollection;

[HarmonyPatch]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class ToolPatches
{
    private const string Plant = "PLANT";
    private const string Tree = "TREE";
    private const string Stone = "STONE";
    private const string Rock = "ROCK";
    private const string EnemyType = "ENEMY";
    private static List<ToolsData> ToolsDataList { get; set; }
    private static ToolsHUDUI ToolsHud { get; set; }
    private static int StaminaUsageCounter { get; set; }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterStatus), nameof(CharacterStatus.SetStatus))]
    public static void CharacterStatus_SetStatus(ref CharacterStatus __instance, ref BaseStatus _baseStatus, ref BaseStatus _curStatus, CharacterType charType)
    {
        if (!Plugin.HalveToolStaminaUsage.Value) return;
        if (charType != CharacterType.PLAYER) return;
        StaminaUsageCounter = 0;
        Plugin.L($"ResetStatus Called. Resetting _hitCounter to 0.");
    }


    private static void SetTool(WeaponTypeEnum type)
    {
        var tool = ToolsDataList.Find(a => a.WeaponType == type);
        PlayerCharacter.Instance.SetSelectedTools(tool);
        ToolsHud.toolIcon.SetSprite(tool.IconName);
        ToolsHud.ToolsHUDUpdate();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BattleCalculator), nameof(BattleCalculator.Calculator))]
    public static void BattleCalculator_Calculator(CharacterType typeC, PlayerCharacter player, UnityEngine.Object obj)
    {
        if (!Plugin.AutoChangeTool.Value) return;
        if (typeC != CharacterType.RESOURCES) return;

        var resource = (ResourcesObject)obj;
        Plugin.L($"BattleCalculator: ResourceID {resource.RESOURCES_ID}");

        if (resource.RESOURCES_ID.ToString().Contains(Plant))
        {
            SetTool(WeaponTypeEnum.SICKLE);
        }

        if (resource.RESOURCES_ID.ToString().Contains(Tree))
        {
            SetTool(WeaponTypeEnum.AXE);
        }

        if (resource.RESOURCES_ID.ToString().Contains(Stone) || resource.RESOURCES_ID.ToString().Contains(Rock))
        {
            SetTool(WeaponTypeEnum.HAMMER);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleCalculator), nameof(BattleCalculator.Calculator))]
    public static void BattleCalculator_Calculator_Postfix(CharacterType typeC, PlayerCharacter player, UnityEngine.Object obj)
    {
        if (!Plugin.LootManipulation.Value) return;

        if (typeC == CharacterType.ENEMY)
        {
            var enemy = (Enemy) obj;

            Plugin.L($"BattleCalculator: MonsterID {enemy.GetID} Health: {enemy.characterStatus.currentstatus.Health} / {enemy.characterStatus.GetBaseStatus.Health}");

            CollectItems_Dict newLoots = enemy.loots;

            Plugin.L($"BattleCalculator: MonsterID {enemy.GetID} loots count: {newLoots.Count}");
            if (newLoots.Count == 0) return;

            foreach (var loot in newLoots.ToList())
            {
                var key = loot.Key;
                var value = loot.Value;

                if (enemy.loots[key] >= Mathf.RoundToInt(Plugin.LootMultiplier.Value)) return;

                Plugin.L($"Added quantity {value * Mathf.RoundToInt(Plugin.LootMultiplier.Value)} of item {key.name} to loot table of monster {enemy.name}");
                enemy.loots[key] = value * Mathf.RoundToInt(Plugin.LootMultiplier.Value);
            }
        }
        if (typeC == CharacterType.RESOURCES)
        {
            var resource = (ResourcesObject) obj;

            Plugin.L($"BattleCalculator: ResourceID {resource.RESOURCES_ID} Health: {resource.currentstatus.Health} / {resource.baseStatus.Health}");

            CollectItems_Dict newLoots = resource.loots;

            Plugin.L($"BattleCalculator: ResourceID {resource.RESOURCES_ID} loots count: {newLoots.Count}");
            if (newLoots.Count == 0) return;

            foreach (var loot in newLoots.ToList())
            {
                var key = loot.Key;
                var value = loot.Value;

                if (resource.loots[key] >= Mathf.RoundToInt(Plugin.LootMultiplier.Value) && resource.currentstatus.Health != resource.baseStatus.Health) return;
                Plugin.L($"Added quantity {value * Mathf.RoundToInt(Plugin.LootMultiplier.Value)} of item {key.name} to loot table of resource {resource.name}");
                resource.loots[key] = value * Mathf.RoundToInt(Plugin.LootMultiplier.Value);
            }
        }

    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(ToolsHUDUI), nameof(ToolsHUDUI.Init))]
    public static void ToolsHUDUI_Init(ref ToolsHUDUI __instance)
    {
        ToolsDataList = __instance.toolsDataList;
        ToolsHud = __instance;
    }

    //half energy use if greater than 1
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterStatus), nameof(CharacterStatus.UseTools))]
    public static bool CharacterStatus_UseTools_Prefix(ref CharacterStatus __instance)
    {
        return !Plugin.HalveToolStaminaUsage.Value;
    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterStatus), nameof(CharacterStatus.UseTools))]
    public static void CharacterStatus_UseTools_Postfix(ref CharacterStatus __instance)
    {
        if (!Plugin.HalveToolStaminaUsage.Value) return;
        StaminaUsageCounter++;
        if (StaminaUsageCounter != 2) return;
        StaminaUsageCounter = 0;
        Plugin.L($"Take Energy! Counter: {StaminaUsageCounter}");
        var staminaLoss = -__instance.GetStatusTools().Stamina;
        var newStamina = __instance.currentstatus.Stamina + staminaLoss;

        if (newStamina > __instance.GetBaseStatus.Stamina)
        {
            var isStaminaLossNegative = staminaLoss < 0;
            staminaLoss = newStamina - __instance.GetBaseStatus.Stamina;
            staminaLoss = (isStaminaLossNegative ? (staminaLoss + staminaLoss) : (staminaLoss - staminaLoss));
            newStamina = __instance.GetBaseStatus.Stamina;
        }

        __instance.currentstatus.Stamina = newStamina;
        __instance.player.EnergySpeed = 100f;
        if (__instance.GetStaminaPercent <= 30f)
        {
            UIManager.TUTORIAL_UI.Call(TutorialID.STAMINA_SYSTEM);
        }

        if (UIManager.GAME_HUD != null)
        {
            UIManager.GAME_HUD.GetStaminaBarHUD.OnValueChange(staminaLoss);
        }
    }
}