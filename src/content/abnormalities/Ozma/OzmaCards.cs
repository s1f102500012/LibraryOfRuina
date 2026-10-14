using System;
using System.Threading.Tasks;
using LibraryOfRuina.framework.cards;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using OzmaMonster = LibraryOfRuina.content.abnormalities.Ozma.Ozma;

namespace LibraryOfRuina.content.abnormalities.Ozma;

[CardPool(typeof(TokenCardPool))]
public sealed class OzmaReturnItToMeCard : CardModel
{
    public const int Damage = 5;
    public const int Hits = 2;
    public const int Block = 5;

    private CardModel? _originalRuntimeCard;

    public OzmaReturnItToMeCard()
        : base(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy, shouldShowInCardLibrary: false)
    {
    }

    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override string PortraitPath =>
        ImageHelper.GetImagePath("packed/card_portraits/colorless/ozma_return_it_to_me_card.png");

    public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(Damage, ValueProp.Move),
        new DynamicVar("Hits", Hits),
        new BlockVar(Block, ValueProp.Move)
    ];

    // 归还后原卡记录为 null；战斗结束时 CardCmd.Transform 会跳过替换，
    // 此时仍留在牌堆中的替身卡不写出空值。
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public SerializableCard? OriginalCard { get; private set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool RestoreAfterCurrentPlay { get; private set; }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        // 复制的“还给我”通过原卡快照独立归还，避免与来源牌重复使用同一个战斗实例。
        _originalRuntimeCard = null;
        RestoreAfterCurrentPlay = false;
    }

    public void SetOriginal(CardModel original)
    {
        OriginalCard = original.ToSerializable();
        _originalRuntimeCard = original;
    }

    public async Task<bool> RestoreOriginal()
    {
        Player? owner = Owner;
        CombatStateLike? combatState = owner?.Creature.CombatState;
        if (OriginalCard == null || owner == null || combatState == null)
        {
            return false;
        }

        if (Pile?.Type == PileType.Play)
        {
            if (RestoreAfterCurrentPlay)
            {
                return false;
            }

            RestoreAfterCurrentPlay = true;
            return true;
        }

        CardModel? original;
        // Transform 会直接把替换卡插入牌堆；只有同一玩家尚未入堆的原卡才能复用。
        if (_originalRuntimeCard != null
            && _originalRuntimeCard.Owner == owner
            && _originalRuntimeCard.Pile == null
            && combatState.ContainsCard(_originalRuntimeCard))
        {
            original = _originalRuntimeCard;
            original.HasBeenRemovedFromState = false;
        }
        else
        {
            original = CreateRestoredCombatCard(owner, OriginalCard);
        }

        if (original == null)
        {
            return false;
        }

        OriginalCard = null;
        _originalRuntimeCard = null;
        if (Pile != null)
        {
            await CardCmd.Transform(this, original, CardPreviewStyle.None);
        }
        else
        {
            await CardPileCmd.Add(original, PileType.Discard);
        }

        return true;
    }

    public override async Task AfterCardChangedPiles(
        CardModel card,
        PileType oldPileType,
        AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card != this
            || !RestoreAfterCurrentPlay
            || oldPileType != PileType.Play
            || Pile?.Type == PileType.Play)
        {
            return;
        }

        RestoreAfterCurrentPlay = false;
        if (!await RestoreOriginal())
        {
            return;
        }

        OzmaMonster? boss = OzmaEncounterHelper.FindBoss(Owner.Creature.CombatState);
        if (boss != null)
        {
            await boss.FinalizeForgottenRestoreStun();
        }
    }

    internal static CardModel? CreateRestoredCombatCard(Player owner, SerializableCard original)
    {
        CombatStateLike? combatState = owner.Creature.CombatState;
        if (combatState == null)
        {
            return null;
        }

        CardModel restored = FromSerializable(original);
        combatState.AddCard(restored, owner);
        return restored;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCardCompat(this, cardPlay)
            .Targeting(target)
            .WithHitCount(DynamicVars["Hits"].IntValue)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Move, cardPlay);
    }
}

[CardPool(typeof(TokenCardPool))]
public sealed class OzmaForgetCard() : CardModel(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
{
    // 遗忘：打出后恢复最大生命的百分比
    public const int HealPercent = 50;

    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override string PortraitPath =>
        ImageHelper.GetImagePath("packed/card_portraits/colorless/ozma_forget_card.png");

    public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
    [
        CardKeyword.Retain,
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("HealPercent", HealPercent)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        decimal heal = Math.Ceiling(Owner.Creature.MaxHp * (HealPercent / 100m));
        await CreatureCmd.Heal(Owner.Creature, heal);
        PlayerCmd.EndTurn(Owner, canBackOut: false);
    }
}

public abstract class OzmaPageChoiceCardBase : PageChoiceCard<OzmaPageMode>
{
    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar(OzmaPageRelic.CostIncrease),
        new DynamicVar("DamageMultiplier", OzmaPageRelic.DamageMultiplier),
        new DynamicVar("Heal", OzmaPageRelic.LifePowderHeal)
    ];
}

[CardPool(typeof(TokenCardPool))]
public sealed class OzmaOldPowerChoiceCard : OzmaPageChoiceCardBase
{
    public override OzmaPageMode PageMode => OzmaPageMode.OldPower;

    protected override string PortraitFileName => "ozma_old_power_choice_card.png";
}

[CardPool(typeof(TokenCardPool))]
public sealed class OzmaForgetChoiceCard : OzmaPageChoiceCardBase
{
    public override OzmaPageMode PageMode => OzmaPageMode.Forget;

    protected override string PortraitFileName => "ozma_forget_choice_card.png";

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromCard<OzmaForgetCard>()
    ];
}

[CardPool(typeof(TokenCardPool))]
public sealed class OzmaLifePowderChoiceCard : OzmaPageChoiceCardBase
{
    public override OzmaPageMode PageMode => OzmaPageMode.LifePowder;

    protected override string PortraitFileName => "ozma_life_powder_choice_card.png";
}
