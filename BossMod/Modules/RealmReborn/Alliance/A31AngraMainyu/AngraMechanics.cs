namespace BossMod.RealmReborn.Alliance.A31AngraMainyu;

class DoubleVision(BossModule module) : Components.RaidwideCast(module, (uint)AID.DoubleVision)
{
    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        // Brand swap rule cross-checked against Cactbot's World of Darkness triggers.
        if (!Active)
            return;

        if (actor.FindStatus((uint)SID.BrandOfTheSullen) != null)
            hints.Add("Switch to red behind the boss!", false);
        else if (actor.FindStatus((uint)SID.BrandOfTheIreful) != null)
            hints.Add("Switch to white in front of the boss!", false);
    }
}

class MortalGaze(BossModule module) : Components.CastGazes(module, [(uint)AID.MortalGaze1, (uint)AID.MortalGaze2])
{
    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        if (actor.FindStatus((uint)SID.Doom) != null)
            hints.Add("Step onto a lit cleanse pad!");
    }
}
class Level100Flare(BossModule module) : Components.BaitAwayIcon(module, 12f, (uint)IconID.Level100Flare, (uint)AID.Level100Flare2, activationDelay: 4.7);
class Level150Death(BossModule module) : Components.BaitAwayIcon(module, 12f, (uint)IconID.Level150Death, (uint)AID.Level150Death2, activationDelay: 4.9);

