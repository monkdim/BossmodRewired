namespace BossMod.Stormblood.Alliance.A33ThunderGod;

class HolySword(BossModule module) : Components.CastHints(module,
    [(uint)AID.TGHolySword1, (uint)AID.TGHolySword2, (uint)AID.TGHolySword3, (uint)AID.TGHolySword4, (uint)AID.TGHolySword5, (uint)AID.TGHolySword6],
    "")
{
    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        // Movement cues from Cactbot's Orbonne Monastery triggers.
        if (!Casters.Contains(Module.PrimaryActor) || Module.PrimaryActor.CastInfo is not { } cast)
            return;

        var instruction = (AID)cast.Action.ID switch
        {
            AID.TGHolySword1 => "Move left",
            AID.TGHolySword2 => "Move right",
            AID.TGHolySword3 => "Rotate right",
            AID.TGHolySword4 => "Rotate left",
            AID.TGHolySword5 => "Move out",
            AID.TGHolySword6 => "Move in",
            _ => null
        };
        if (instruction != null)
            hints.Add($"{instruction} ({cast.NPCRemainingTime:f1}s)");
    }
}

