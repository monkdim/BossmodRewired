namespace BossMod.Stormblood.Ultimate.UCOB;

abstract class LiquidHellBase(BossModule module) : Components.VoidzoneAtCastTarget(module, 6f, (uint)AID.LiquidHell, m => m.Enemies((uint)OID.VoidzoneLiquidHell).Where(z => z.EventState != 7), 1.3f)
{
    public void Reset() => NumCasts = 0;
}

sealed class LiquidHell(BossModule module) : LiquidHellBase(module);

sealed class P1LiquidHell(BossModule module) : LiquidHellBase(module)
{
    public override bool KeepOnPhaseChange => true;
}
