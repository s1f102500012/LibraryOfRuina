using System;
using LibraryOfRuina.framework.cards;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace LibraryOfRuina.content.abnormalities.SmilingBodies;

public abstract class SmilingBodiesPageChoiceCardBase : PageChoiceCard<SmilingBodiesPageMode>
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.ForEnergy(this),
        HoverTipFactory.FromPower<LibraryVulnerablePower>(),
        HoverTipFactory.FromPower<LibraryStrongPower>(),
        HoverTipFactory.FromPower<LibraryEndurancePower>(),
        HoverTipFactory.FromPower<LibraryQuicknessPower>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("ChaosDamage", SmilingBodiesPageRelic.CorpseLaughsChaosDamage),
        new DynamicVar("Vulnerable", SmilingBodiesPageRelic.CorpseLaughsVulnerable),
        new DynamicVar("HealPercent", SmilingBodiesPageRelic.AbsorptionHealPercent),
        new DynamicVar("HealUses", SmilingBodiesPageRelic.AbsorptionMaxHealsPerCombat),
        new DynamicVar("StrongHealPercent", SmilingBodiesPageRelic.AbsorptionHealPercentPerStrong),
        new DynamicVar("StrongPerThreshold", SmilingBodiesPageRelic.AbsorptionStrongPerHealThreshold),
        new DynamicVar("AllyBuffs", SmilingBodiesPageRelic.CorpseMountainBuffStacks),
        new DynamicVar("EnergyBonus", SmilingBodiesPageRelic.CorpseMountainEnergyBonusPerAlly),
        new DynamicVar("Cooldown", SmilingBodiesPageRelic.CorpseMountainCooldown),
        new DynamicVar("MaxHpLossPercent", SmilingBodiesPageRelic.CorpseMountainMaxHpLossPercent),
        new DynamicVar("LowHpThresholdPercent", SmilingBodiesPageRelic.CorpseLaughsLowHpThresholdPercent),
        new DynamicVar("LowHpThreshold", 0),
        new DynamicVar("HealAmount", 0),
        new DynamicVar("HealThreshold", 0),
        new DynamicVar("MaxHpLoss", 0)
    ];

    public override void AfterCreated()
    {
        base.AfterCreated();

        int ownerMaxHp = Owner.Creature.MaxHp;
        DynamicVars["LowHpThreshold"].BaseValue = ownerMaxHp > 0
            ? (int)(ownerMaxHp * SmilingBodiesPageRelic.CorpseLaughsLowHpThresholdPercent / 100m)
            : 0;
        DynamicVars["HealAmount"].BaseValue = ownerMaxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(ownerMaxHp * SmilingBodiesPageRelic.AbsorptionHealPercent / 100m))
            : 0;
        DynamicVars["HealThreshold"].BaseValue = ownerMaxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(ownerMaxHp * SmilingBodiesPageRelic.AbsorptionHealPercentPerStrong / 100m))
            : 0;
        DynamicVars["MaxHpLoss"].BaseValue = ownerMaxHp > 0
            ? Math.Max(1, (int)Math.Ceiling(ownerMaxHp * SmilingBodiesPageRelic.CorpseMountainMaxHpLossPercent / 100m))
            : 0;
    }
}

[CardPool(typeof(TokenCardPool))]
public sealed class SmilingBodiesCorpseLaughsChoiceCard : SmilingBodiesPageChoiceCardBase
{
    public override SmilingBodiesPageMode PageMode => SmilingBodiesPageMode.CorpseLaughs;

    protected override string PortraitFileName => "smiling_bodies_corpse_laughs_choice_card.png";
}

[CardPool(typeof(TokenCardPool))]
public sealed class SmilingBodiesCorpseAbsorptionChoiceCard : SmilingBodiesPageChoiceCardBase
{
    public override SmilingBodiesPageMode PageMode => SmilingBodiesPageMode.CorpseAbsorption;

    protected override string PortraitFileName => "smiling_bodies_corpse_absorption_choice_card.png";
}

[CardPool(typeof(TokenCardPool))]
public sealed class SmilingBodiesCorpseMountainChoiceCard : SmilingBodiesPageChoiceCardBase
{
    public override SmilingBodiesPageMode PageMode => SmilingBodiesPageMode.CorpseMountain;

    protected override string PortraitFileName => "smiling_bodies_corpse_mountain_choice_card.png";
}
