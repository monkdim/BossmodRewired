namespace BossMod.Stormblood.Ultimate.UCOB;

sealed class P3SeventhUmbralEra(BossModule module) : Components.GenericKnockback(module, (uint)AID.SeventhUmbralEra)
{
    private readonly DateTime _activation = module.WorldState.FutureTime(5.3d);

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        return new Knockback[1] { new(Arena.Center, 11f, _activation, ignoreImmunes: true) };
    }
}

sealed class P3CalamitousFlame(BossModule module) : Components.CastCounter(module, (uint)AID.CalamitousFlame);
sealed class P3CalamitousBlaze(BossModule module) : Components.CastCounter(module, (uint)AID.CalamitousBlaze);
