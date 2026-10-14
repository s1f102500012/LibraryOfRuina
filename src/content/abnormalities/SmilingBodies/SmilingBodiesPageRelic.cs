using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.relics;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.SmilingBodies;

public sealed class SmilingBodiesPageRelic : ModalPageRelic<SmilingBodiesPageMode>
{
    public const int CorpseLaughsChaosDamage = 8;
    public const int CorpseLaughsVulnerable = 1;
    public const int AbsorptionHealPercent = 2;
    public const int AbsorptionMaxHealsPerCombat = 2;
    public const int AbsorptionHealPercentPerStrong = 10; // 死尸吸收：本场战斗累计回复量每达到最大生命值的该百分比，回合开始时获得一次永久强壮。
    public const int AbsorptionStrongPerHealThreshold = 1; // 死尸吸收：每达到一次上述回复阈值获得的永久强壮层数。
    public const int CorpseMountainBuffStacks = 3;
    public const int CorpseMountainEnergyBonusPerAlly = 1;
    public const int CorpseMountainCooldown = 12;
    public const int CorpseMountainMaxHpLossPercent = 4;
    public const int CorpseLaughsLowHpThresholdPercent = 50;

    protected override string IconBaseName => "smiling_bodies_page_relic";

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasRightClick => Mode == SmilingBodiesPageMode.CorpseMountain;

    public override bool ShowCounter => CombatManager.Instance.IsInProgress
        && (Mode == SmilingBodiesPageMode.CorpseAbsorption
            || (Mode == SmilingBodiesPageMode.CorpseMountain && CorpseMountainCooldownRemaining > 0));

    public override int DisplayAmount => Mode switch
    {
        SmilingBodiesPageMode.CorpseAbsorption =>
            Math.Max(0, AbsorptionMaxHealsPerCombat - AbsorptionHealsUsed),
        SmilingBodiesPageMode.CorpseMountain =>
            Math.Max(0, CorpseMountainCooldownRemaining),
        _ => 0
    };

    public override bool IsAllowed(IRunState runState) =>
        AbnormalityPageRewardHelper.CanPageRelicAppear<SmilingBodiesPageRelic>(runState);

    protected override IEnumerable<IHoverTip> ExtraHoverTips => Mode switch
    {
        SmilingBodiesPageMode.CorpseLaughs =>
        [
            HoverTipFactory.FromPower<LibraryVulnerablePower>()
        ],
        SmilingBodiesPageMode.CorpseAbsorption =>
        [
            HoverTipFactory.FromPower<LibraryStrongPower>()
        ],
        SmilingBodiesPageMode.CorpseMountain =>
        [
            HoverTipFactory.ForEnergy(this),
            HoverTipFactory.FromPower<LibraryStrongPower>(),
            HoverTipFactory.FromPower<LibraryEndurancePower>(),
            HoverTipFactory.FromPower<LibraryQuicknessPower>()
        ],
        _ => []
    };

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Mode", (int)SmilingBodiesPageMode.None),
        new DynamicVar("ChaosDamage", CorpseLaughsChaosDamage),
        new DynamicVar("Vulnerable", CorpseLaughsVulnerable),
        new DynamicVar("HealPercent", AbsorptionHealPercent),
        new DynamicVar("HealUses", AbsorptionMaxHealsPerCombat),
        new DynamicVar("StrongHealPercent", AbsorptionHealPercentPerStrong),
        new DynamicVar("StrongPerThreshold", AbsorptionStrongPerHealThreshold),
        new DynamicVar("RemainingHeals", AbsorptionMaxHealsPerCombat),
        new DynamicVar("AllyBuffs", CorpseMountainBuffStacks),
        new DynamicVar("EnergyBonus", CorpseMountainEnergyBonusPerAlly),
        new DynamicVar("Cooldown", CorpseMountainCooldown),
        new DynamicVar("MaxHpLossPercent", CorpseMountainMaxHpLossPercent),
        new DynamicVar("LowHpThreshold", 0),
        new DynamicVar("LowHpThresholdPercent", CorpseLaughsLowHpThresholdPercent),
        new DynamicVar("HealAmount", 0),
        new DynamicVar("HealThreshold", 0),
        new DynamicVar("MaxHpLoss", 0),
        new DynamicVar("CooldownRemaining", 0)
    ];

    [SavedProperty]
    public SmilingBodiesPageMode Mode { get; private set; }

    protected override SmilingBodiesPageMode SelectedMode
    {
        get => Mode;
        set => Mode = value;
    }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int AbsorptionHealsUsed { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int AbsorptionCumulativeHealPercent { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int AbsorptionGrantedThisCombat { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int AbsorptionPermanentStrong { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CorpseMountainCooldownRemaining { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CorpseMountainStrongStacks { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CorpseMountainEnduranceStacks { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CorpseMountainQuicknessStacks { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int CorpseMountainEnergyBonus { get; private set; }

    /// <summary>
    /// 吃队友时捕获的战斗对象身份。刻意不做 SavedProperty（避免联机 wire schema 变更）：
    /// 每场战斗都会创建新的 CombatState 实例，旧引用永远无法匹配后续战斗，
    /// 因此能量上限加成在战斗结束后必然失效，不依赖边界钩子的重置时机。
    /// </summary>
    private CombatStateLike? _corpseMountainEnergyCombat;

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int[]? CorpseMountainSavedPlayerIndexes { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int[]? CorpseMountainSavedHps { get; private set; }

    public override Task BeforeCombatStart()
    {
        EnsureValidModeOrFallback(nameof(BeforeCombatStart));
        ResetCorpseMountainCombatState();

        ResetAbsorptionCombatState();

        Log.Info("[LibraryOfRuina.SmilingBodies] BeforeCombatStart mode="
            + Mode
            + " permStrong=" + AbsorptionPermanentStrong
            + " used=" + AbsorptionHealsUsed
            + " cumPct=" + AbsorptionCumulativeHealPercent
            + " granted=" + AbsorptionGrantedThisCombat
            + " strongStacks=" + CorpseMountainStrongStacks
            + " enduranceStacks=" + CorpseMountainEnduranceStacks
            + " quicknessStacks=" + CorpseMountainQuicknessStacks
            + " energyBonus=" + CorpseMountainEnergyBonus);

        UpdateModeUiState();
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner)
        {
            return;
        }

        Log.Info("[LibraryOfRuina.SmilingBodies] AfterPlayerTurnStart player="
            + player.NetId
            + " mode=" + Mode
            + " cumPct=" + AbsorptionCumulativeHealPercent
            + " granted=" + AbsorptionGrantedThisCombat
            + " cooldown=" + CorpseMountainCooldownRemaining);

        if (CorpseMountainCooldownRemaining > 0)
        {
            CorpseMountainCooldownRemaining--;
        }

        if (Mode == SmilingBodiesPageMode.CorpseAbsorption)
        {
            await GrantAbsorptionStrength(choiceContext);
        }

        UpdateModeUiState();
    }

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        // 遗物钩子对场上每一次伤害都会调用，过滤在后面，所以只在 Debug 下记录。
        if (LorLog.IsDebugEnabled)
        {
            LorLog.Debug("[LibraryOfRuina.SmilingBodies] AfterDamageGiven mode="
                + Mode
                + " dealer=" + (dealer?.Name ?? "null")
                + " target=" + target.Name
                + " killed=" + result.WasTargetKilled
                + " targetSide=" + target.Side
                + " ownerAlive=" + (Owner?.Creature?.IsAlive ?? false)
                + " ownerSource=" + (Owner?.Creature == null ? "noOwner" : IsOwnerDamageSource(dealer, cardSource).ToString())
                + " used=" + AbsorptionHealsUsed);
        }

        if (Mode != SmilingBodiesPageMode.CorpseAbsorption
            || Owner?.Creature == null
            || !result.WasTargetKilled
            || target.Side == Owner.Creature.Side
            || AllyTurnRegistry.IsFriendlyAlly(target)
            || !IsOwnerDamageSource(dealer, cardSource)
            || AbsorptionHealsUsed >= AbsorptionMaxHealsPerCombat)
        {
            return;
        }

        Creature owner = Owner.Creature;
        if (!owner.IsAlive || owner.MaxHp <= 0)
        {
            return;
        }

        int healAmount = Math.Max(1, (int)Math.Ceiling(owner.MaxHp * AbsorptionHealPercent / 100m));
        int actualHeal = Math.Max(0, Math.Min(healAmount, owner.MaxHp - owner.CurrentHp));
        Flash();
        await CreatureCmd.Heal(owner, healAmount);
        AbsorptionHealsUsed++;

        Log.Info("[LibraryOfRuina.SmilingBodies] AbsorptionHeal heal="
            + healAmount
            + " actual=" + actualHeal
            + " cumPct=" + AbsorptionCumulativeHealPercent
            + " used=" + AbsorptionHealsUsed);

        UpdateModeUiState();
    }

    internal void OnHealReceived(Creature creature, int healed)
    {
        if (Mode != SmilingBodiesPageMode.CorpseAbsorption
            || Owner?.Creature == null
            || creature != Owner.Creature
            || healed <= 0
            || creature.MaxHp <= 0)
        {
            return;
        }

        AbsorptionCumulativeHealPercent += healed * 100 / creature.MaxHp;
        Log.Info("[LibraryOfRuina.SmilingBodies] HealReceived healed="
            + healed
            + " cumPct=" + AbsorptionCumulativeHealPercent);
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (Mode != SmilingBodiesPageMode.CorpseLaughs
            || Owner?.Creature == null
            || target != Owner.Creature
            || result.UnblockedDamage <= 0m
            || !Owner.Creature.IsAlive
            || Owner.Creature.CurrentHp * 100m >= Owner.Creature.MaxHp * CorpseLaughsLowHpThresholdPercent)
        {
            return;
        }

        IReadOnlyList<Creature> enemies = AllyTurnRegistry
            .FilterPlayerEnemyTargets(Owner.Creature.CombatState?.Enemies)
            .Where(static enemy => enemy.IsAlive)
            .ToArray();

        if (enemies.Count == 0)
        {
            return;
        }

        Flash(enemies);
        await LibraryCreatureCmd.ChaoDamage(
            choiceContext,
            enemies,
            CorpseLaughsChaosDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner.Creature,
            null,
            null);

        foreach (Creature enemy in enemies)
        {
            await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                enemy,
                CorpseLaughsVulnerable,
                turns: -1,
                Owner.Creature,
                null);
        }
    }

    public override async Task AfterSideTurnEndLate(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        await MaintainCorpseMountainQuickness(choiceContext);
    }

    public override bool CanHandleRightClickLocal(LibraryRightClickContext context) =>
        CanUseCorpseMountain();

    public override bool CanExecuteRightClick(LibraryRightClickExecutionContext context) =>
        CanUseCorpseMountain();

    public override async Task OnRightClick(LibraryRightClickExecutionContext context)
    {
        if (!CanUseCorpseMountain() || Owner?.Creature?.CombatState == null)
        {
            return;
        }

        CombatStateLike combatState = Owner.Creature.CombatState;
        IReadOnlyList<Player> players = combatState.Players;
        Player[] allies = players
            .Where(player =>
                player.Creature != null
                && player.Creature != Owner.Creature
                && player.Creature.IsAlive)
            .ToArray();

        if (allies.Length == 0)
        {
            return;
        }

        CorpseMountainSavedPlayerIndexes = allies
            .Select(ally => IndexOfPlayer(players, ally))
            .ToArray();
        CorpseMountainSavedHps = allies
            .Select(ally => Math.Max(0, ally.Creature.CurrentHp))
            .ToArray();

        foreach (Player ally in allies)
        {
            await CreatureCmd.Kill(ally.Creature, force: true);
        }

        int stacks = allies.Length * CorpseMountainBuffStacks;
        Creature ownerCreature = Owner.Creature;
        CorpseMountainStrongStacks += stacks;
        CorpseMountainEnduranceStacks += stacks;
        CorpseMountainQuicknessStacks += stacks;
        CorpseMountainEnergyBonus += allies.Length * CorpseMountainEnergyBonusPerAlly;
        _corpseMountainEnergyCombat = ownerCreature.CombatState;
        CorpseMountainCooldownRemaining = CorpseMountainCooldown;
        int maxHpLoss = Math.Max(
            1,
            (int)Math.Ceiling(ownerCreature.MaxHp * CorpseMountainMaxHpLossPercent / 100m));

        Flash();
        await LibraryPowerCmd.Apply<LibraryStrongPower>(ownerCreature, stacks, turns: -1, ownerCreature, null);
        await LibraryPowerCmd.Apply<LibraryEndurancePower>(ownerCreature, stacks, turns: -1, ownerCreature, null);
        await MaintainCorpseMountainQuickness(context.ChoiceContext);
        await CreatureCmd.LoseMaxHp(context.ChoiceContext, ownerCreature, maxHpLoss, isFromCard: false);
        Log.Info("[LibraryOfRuina.SmilingBodies] CorpseMountain maxHpLoss="
            + maxHpLoss
            + " newMaxHp=" + ownerCreature.MaxHp);
        UpdateModeUiState();
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        if (Mode != SmilingBodiesPageMode.CorpseMountain
            || player != Owner
            || CorpseMountainEnergyBonus <= 0
            || _corpseMountainEnergyCombat == null
            || !ReferenceEquals(player.Creature?.CombatState, _corpseMountainEnergyCombat))
        {
            return amount;
        }

        return amount + CorpseMountainEnergyBonus;
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        await RestoreCorpseMountainPlayers(room);
        ResetCorpseMountainCombatState();

        ResetAbsorptionCombatState();

        UpdateModeUiState();
    }

    private void ResetAbsorptionCombatState()
    {
        AbsorptionHealsUsed = 0;
        AbsorptionCumulativeHealPercent = 0;
        AbsorptionGrantedThisCombat = 0;
        // 死尸吸收的永久强壮仅限当前战斗，同时清理旧存档中的累计层数。
        AbsorptionPermanentStrong = 0;
    }

    private void ResetCorpseMountainCombatState()
    {
        CorpseMountainCooldownRemaining = 0;
        CorpseMountainStrongStacks = 0;
        CorpseMountainEnduranceStacks = 0;
        CorpseMountainQuicknessStacks = 0;
        CorpseMountainEnergyBonus = 0;
        _corpseMountainEnergyCombat = null;
        CorpseMountainSavedPlayerIndexes = null;
        CorpseMountainSavedHps = null;
    }

    private async Task GrantAbsorptionStrength(PlayerChoiceContext choiceContext)
    {
        Creature? owner = Owner?.Creature;
        if (owner == null || !owner.IsAlive || AbsorptionCumulativeHealPercent <= 0)
        {
            Log.Info("[LibraryOfRuina.SmilingBodies] GrantAbsorptionStrength skip ownerAlive="
                + (owner?.IsAlive ?? false)
                + " cumPct=" + AbsorptionCumulativeHealPercent);
            return;
        }

        int targetGranted = AbsorptionCumulativeHealPercent / AbsorptionHealPercentPerStrong
            * AbsorptionStrongPerHealThreshold;
        int toGrant = targetGranted - AbsorptionGrantedThisCombat;
        if (toGrant <= 0)
        {
            Log.Info("[LibraryOfRuina.SmilingBodies] GrantAbsorptionStrength noGrant targetGranted="
                + targetGranted
                + " alreadyGranted=" + AbsorptionGrantedThisCombat);
            return;
        }

        Flash();
        LibraryStrongPower? strong = await LibraryPowerCmd.Apply<LibraryStrongPower>(
            owner,
            toGrant,
            turns: -1,
            owner,
            null);
        AbsorptionPermanentStrong += toGrant;
        AbsorptionGrantedThisCombat += toGrant;
        Log.Info("[LibraryOfRuina.SmilingBodies] GrantAbsorptionStrength granted="
            + toGrant
            + " powerAmount=" + (strong?.Amount ?? -1)
            + " permStrong=" + AbsorptionPermanentStrong
            + " cumPct=" + AbsorptionCumulativeHealPercent);
    }

    private async Task MaintainCorpseMountainQuickness(PlayerChoiceContext choiceContext)
    {
        Creature? owner = Owner?.Creature;
        if (Mode != SmilingBodiesPageMode.CorpseMountain
            || CorpseMountainQuicknessStacks <= 0
            || owner == null
            || !owner.IsAlive
            || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        LibraryQuicknessPower? quickness = owner.GetPower<LibraryQuicknessPower>();
        if (quickness == null)
        {
            await PowerCmdCompat.Apply<LibraryQuicknessPower>(
                choiceContext,
                owner,
                CorpseMountainQuicknessStacks,
                owner,
                null,
                silent: true);
            return;
        }

        int amountDelta = CorpseMountainQuicknessStacks - quickness.Amount;
        if (amountDelta != 0)
        {
            await PowerCmdCompat.ModifyAmount(
                choiceContext,
                quickness,
                amountDelta,
                owner,
                null,
                silent: true);
        }
    }

    private async Task RestoreCorpseMountainPlayers(CombatRoom room)
    {
        int[]? indexes = CorpseMountainSavedPlayerIndexes;
        int[]? hps = CorpseMountainSavedHps;
        CorpseMountainSavedPlayerIndexes = null;
        CorpseMountainSavedHps = null;

        if (indexes == null
            || hps == null
            || indexes.Length == 0
            || room.CombatState == null)
        {
            return;
        }

        IReadOnlyList<Player> players = room.CombatState.Players;
        for (int i = 0; i < indexes.Length && i < hps.Length; i++)
        {
            int index = indexes[i];
            if (index < 0 || index >= players.Count || hps[i] <= 0)
            {
                continue;
            }

            Creature creature = players[index].Creature;
            if (creature != null)
            {
                await CreatureCmd.SetCurrentHp(creature, hps[i]);
            }
        }
    }

    private bool CanUseCorpseMountain()
    {
        if (Mode != SmilingBodiesPageMode.CorpseMountain
            || Owner?.Creature is not { IsAlive: true }
            || !CombatManager.Instance.IsInProgress
            || CorpseMountainCooldownRemaining > 0)
        {
            return false;
        }

        CombatStateLike? combatState = Owner.Creature.CombatState;
        return combatState != null
            && combatState.Players.Any(player =>
                player.Creature != null
                && player.Creature != Owner.Creature
                && player.Creature.IsAlive);
    }

    private bool IsOwnerDamageSource(Creature? dealer, CardModel? cardSource)
    {
        if (dealer == null || Owner.Creature == null)
        {
            return false;
        }

        if (dealer != Owner.Creature && dealer != Owner.Osty)
        {
            return false;
        }

        return cardSource == null || cardSource.Owner == Owner;
    }

    private static int IndexOfPlayer(IReadOnlyList<Player> players, Player player)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (ReferenceEquals(players[i], player))
            {
                return i;
            }
        }

        return -1;
    }

    protected override IReadOnlyList<CardModel> CreateModeChoiceCards()
    {
        return
        [
            Owner.RunState.CreateCard<SmilingBodiesCorpseLaughsChoiceCard>(Owner),
            Owner.RunState.CreateCard<SmilingBodiesCorpseAbsorptionChoiceCard>(Owner),
            Owner.RunState.CreateCard<SmilingBodiesCorpseMountainChoiceCard>(Owner)
        ];
    }

    protected override void ResetStateOnFallback()
    {
        AbsorptionHealsUsed = 0;
        AbsorptionCumulativeHealPercent = 0;
        AbsorptionGrantedThisCombat = 0;
        CorpseMountainCooldownRemaining = 0;
    }

    protected override void UpdateModeUiState()
    {
        DynamicVars["Mode"].BaseValue = (int)Mode;
        DynamicVars["RemainingHeals"].BaseValue =
            Math.Max(0, AbsorptionMaxHealsPerCombat - AbsorptionHealsUsed);
        DynamicVars["CooldownRemaining"].BaseValue = Math.Max(0, CorpseMountainCooldownRemaining);
        int maxHp = Owner?.Creature?.MaxHp ?? 0;
        DynamicVars["LowHpThreshold"].BaseValue = maxHp > 0
            ? maxHp * CorpseLaughsLowHpThresholdPercent / 100m
            : 0;
        DynamicVars["HealAmount"].BaseValue = maxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(maxHp * AbsorptionHealPercent / 100m))
            : 0;
        DynamicVars["HealThreshold"].BaseValue = maxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(maxHp * AbsorptionHealPercentPerStrong / 100m))
            : 0;
        DynamicVars["MaxHpLoss"].BaseValue = maxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(maxHp * CorpseMountainMaxHpLossPercent / 100m))
            : 0;
        Status = Mode switch
        {
            SmilingBodiesPageMode.None => RelicStatus.Normal,
            SmilingBodiesPageMode.CorpseMountain when CorpseMountainCooldownRemaining > 0 => RelicStatus.Disabled,
            SmilingBodiesPageMode.CorpseMountain when CombatManager.Instance.IsInProgress => RelicStatus.Active,
            SmilingBodiesPageMode.CorpseAbsorption when CombatManager.Instance.IsInProgress => RelicStatus.Active,
            _ => RelicStatus.Normal
        };
        InvokeDisplayAmountChanged();
    }
}

public enum SmilingBodiesPageMode
{
    None = 0,
    CorpseLaughs = 1,
    CorpseAbsorption = 2,
    CorpseMountain = 3
}
