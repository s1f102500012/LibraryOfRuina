using System.Threading.Tasks;
using LibraryOfRuina.core.compat;
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
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.BigBird;

[CardPool(typeof(StatusCardPool))]
public sealed class SoulSnareStatusCard() : CardModel(3, CardType.Status, CardRarity.Status, TargetType.AnyEnemy)
{
    public const int Damage = 1;

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override string PortraitPath =>
        ImageHelper.GetImagePath("packed/card_portraits/status/soul_snare_status_card.png");

    public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
    [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Damage", Damage)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    ];

    // public override bool ShouldAllowTargeting(Creature target)
    // {
    //     if (Owner?.Creature?.GetPower<BigBirdCharmedPower>() == null)
    //     {
    //         return true;
    //     }
    //     return target.Monster is BigBird;
    // }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? boss = BigBirdEncounterHelper.FindBoss(Owner.Creature.CombatState);
        if (boss == null)
        {
            return;
        }

        await CreatureCmdCompat.Damage(
            choiceContext,
            boss,
            Damage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner.Creature,
            this,
            cardPlay);
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (card is SoulSnareStatusCard)
        {
            SaveManager.Instance.MarkCardAsSeen(card);
        }

        return Task.CompletedTask;
    }
}

[CardPool(typeof(StatusCardPool))]
public sealed class BirdLullabyCard() : CardModel(2, CardType.Skill, CardRarity.Status, TargetType.AnyEnemy)
{
    public const int Damage = 1;
    public const int CostIncrease = 1; // 鸟之摇篮曲：每次打出后本场战斗费用的增加量。

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override string PortraitPath =>
        ImageHelper.GetImagePath("packed/card_portraits/status/bird_lullaby_card.png");

    public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
    [
        CardKeyword.Retain
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Damage", Damage),
        new EnergyVar("CostIncrease", CostIncrease)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        HoverTipFactory.FromPower<BigBirdSleepPower>()
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        await CreatureCmdCompat.Damage(
            choiceContext,
            target,
            Damage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner.Creature,
            this,
            cardPlay);

        if (target.Monster is BigBird bigBird)
        {
            if (target.Block > 0m)
            {
                await GameApi.LoseBlock(choiceContext, target, target.Block, Owner.Creature);
            }

            await PowerCmdCompat.SetAmount<BigBirdSleepPower>(choiceContext, target, BigBird.SleepTurns, Owner.Creature, this);
            await BigBirdEncounterHelper.ClearAllCharmedPlayers(choiceContext, target.CombatState);
            bigBird.ForceRefreshMoveState();
            // if (target is LibraryCreature libraryTarget && libraryTarget.HasChaoResistance)
            // {
            //     await LibraryCreatureCmd.SetCurrentChaoValue(libraryTarget, 0m);
            // }
        }

        EnergyCost.AddThisCombat(CostIncrease);
        InvokeEnergyCostChanged();
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (card is BirdLullabyCard)
        {
            SaveManager.Instance.MarkCardAsSeen(card);
        }

        return Task.CompletedTask;
    }
}

public abstract class BigBirdPageChoiceCardBase : PageChoiceCard<BigBirdPageMode>
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.ForEnergy(this),
        HoverTipFactory.FromPower<LibraryStrongPower>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar("Energy", BigBirdPageRelic.WatchfulEyeEnergyPenalty),
        new DynamicVar("WatchfulCooldown", BigBirdPageRelic.WatchfulEyeCooldown),
        new DynamicVar("LampCooldown", BigBirdPageRelic.EverBurningLampCooldown),
        new DynamicVar("Strong", BigBirdPageRelic.SalvationStrong),
        new DynamicVar("Damage", BigBirdPageRelic.SalvationDamage)
    ];
}

[CardPool(typeof(TokenCardPool))]
public sealed class BigBirdWatchfulEyeChoiceCard : BigBirdPageChoiceCardBase
{
    public override BigBirdPageMode PageMode => BigBirdPageMode.WatchfulEye;

    protected override string PortraitFileName => "big_bird_watchful_eye_choice_card.png";
}

[CardPool(typeof(TokenCardPool))]
public sealed class BigBirdEverBurningLampChoiceCard : BigBirdPageChoiceCardBase
{
    public override BigBirdPageMode PageMode => BigBirdPageMode.EverBurningLamp;

    protected override string PortraitFileName => "big_bird_ever_burning_lamp_choice_card.png";
}

[CardPool(typeof(TokenCardPool))]
public sealed class BigBirdSalvationChoiceCard : BigBirdPageChoiceCardBase
{
    public override BigBirdPageMode PageMode => BigBirdPageMode.Salvation;

    protected override string PortraitFileName => "big_bird_salvation_choice_card.png";
}
