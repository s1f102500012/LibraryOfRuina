using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.powers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.BigBird;

public sealed class BigBirdEverBurningLampPassivePower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "BIG_BIRD_EVER_BURNING_LAMP_PASSIVE_POWER";

    public override PowerType Type => PowerType.None;

    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Turns", BigBird.CycleLength)
    ];
}

public sealed class EyeballBirdSleepyEyesPassivePower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "EYEBALL_BIRD_SLEEPY_EYES_PASSIVE_POWER";

    public override PowerType Type => PowerType.None;

    public override PowerStackType StackType => PowerStackType.Single;
}

public sealed class EyeballBirdFollowPassivePower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "EYEBALL_BIRD_FOLLOW_PASSIVE_POWER";

    public override PowerType Type => PowerType.None;

    public override PowerStackType StackType => PowerStackType.Single;
}

public sealed class BigBirdCharmedPower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "BIG_BIRD_CHARMED_POWER";

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromCard<SoulSnareStatusCard>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Damage", BigBird.CharmBreakUnblockedDamage),
        new DynamicVar("Turns", BigBird.CharmTurns)
    ];

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner
            || result.UnblockedDamage < BigBird.CharmBreakUnblockedDamage
            || !IsOtherPlayerDealer(dealer, Owner))
        {
            return;
        }

        await PowerCmd.Remove(this);
        BigBirdFilterOverlay.RefreshForCombat(Owner.CombatState);
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        BigBirdFilterOverlay.RefreshForCombat(oldOwner.CombatState);
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        BigBirdFilterOverlay.Clear();
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!TurnParticipants.IsPlayerTurnFor(Owner, side, participants) || Owner.IsDead)
        {
            return;
        }

        if (Amount > 1)
        {
            await PowerCmdCompat.ModifyAmount(choiceContext, this, -1, Owner, null, silent: true);
            return;
        }

        await PowerCmd.Remove(this);
        BigBirdFilterOverlay.RefreshForCombat(Owner.CombatState);
    }

    public override async Task BeforeHandDraw(
        Player player,
        PlayerChoiceContext choiceContext,
        CombatStateLike combatState)
    {
        if (player.Creature != Owner || Owner.IsDead)
        {
            return;
        }

        await CardPileCmdCompat.AddToCombatAndPreview<SoulSnareStatusCard>(
            Owner,
            PileType.Hand,
            1,
            addedByPlayer: false,
            CardPilePosition.Top);
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card.Owner?.Creature != Owner)
        {
            return true;
        }

        // 勾魂只限制普通牌，状态牌与诅咒牌继续遵循各自的出牌规则。
        if (card.Type is CardType.Status or CardType.Curse)
        {
            return true;
        }

        bool handHasSoulSnare = HandContains<SoulSnareStatusCard>(card.Owner);
        if (!handHasSoulSnare)
        {
            return true;
        }

        if (HandContains<BirdLullabyCard>(card.Owner))
        {
            return card is BirdLullabyCard;
        }

        return false;
    }

    public override async Task BeforeFlush(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature != Owner || Owner.IsDead)
        {
            return;
        }

        Creature? boss = BigBirdEncounterHelper.FindBoss(Owner.CombatState);
        if (boss == null)
        {
            return;
        }

        foreach (SoulSnareStatusCard card in CardPile.GetCards(player, PileType.Hand)
            .OfType<SoulSnareStatusCard>()
            .ToArray())
        {
            await CardCmd.AutoPlay(choiceContext, card, boss);
        }
    }

    private static bool IsOtherPlayerDealer(Creature? dealer, Creature owner)
    {
        Player? dealerPlayer = dealer?.Player ?? dealer?.PetOwner;
        Player? ownerPlayer = owner.Player ?? owner.PetOwner;
        return dealerPlayer != null && ownerPlayer != null && dealerPlayer != ownerPlayer;
    }

    private static bool HandContains<TCard>(Player player)
        where TCard : CardModel
    {
        return CardPile.GetCards(player, PileType.Hand).OfType<TCard>().Any();
    }
}

public sealed class BigBirdPatrolPower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "BIG_BIRD_PATROL_POWER";

    public override PowerType Type => PowerType.None;

    public override PowerStackType StackType => PowerStackType.Counter;

    public uint? SourceCombatId { get; private set; }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Turns", 1)
    ];

    public void SetSource(Creature source)
    {
        SourceCombatId = source.CombatId;
    }

    public Creature? ResolveSource()
    {
        return Owner.CombatState?.Creatures.FirstOrDefault(creature =>
            creature.CombatId == SourceCombatId && creature.IsAlive);
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!TurnParticipants.IsPlayerTurnFor(Owner, side, participants))
        {
            return;
        }

        if (Amount > 1)
        {
            await PowerCmdCompat.ModifyAmount(choiceContext, this, -1, Owner, null, silent: true);
            return;
        }

        await PowerCmd.Remove(this);
    }
}

public sealed class BigBirdSleepPower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "BIG_BIRD_SLEEP_POWER";

    public override PowerType Type => PowerType.None;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Turns", BigBird.SleepTurns)
    ];

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || Owner.IsDead)
        {
            return;
        }
        
        if (Amount > 1)
        {
            await PowerCmdCompat.ModifyAmount(choiceContext, this, -1, Owner, null, silent: true);
            if (Amount == 1 && Owner.Monster is BigBird bigBird)
            {
                await bigBird.EnterSleepConfusion();
            }
            return;
        }

        await PowerCmd.Remove(this);
    }

    public override async Task AfterRemoved(Creature oldOwner)
    {
        if (!oldOwner.IsDead && oldOwner.Monster is BigBird)
        {
            await CreatureCmd.TriggerAnim(oldOwner, "Idle", 0f);
        }
    }
}

public sealed class BigBirdEnergySealPower : LibraryOfRuinaPowerModel
{
    protected override string LegacyPowerId => "BIG_BIRD_ENERGY_SEAL_POWER";

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar("Energy", BigBirdPageRelic.WatchfulEyeEnergyPenalty)
    ];

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player || !participants.Contains(Owner))
        {
            return;
        }

        await PowerCmd.Remove(this);
    }
}
