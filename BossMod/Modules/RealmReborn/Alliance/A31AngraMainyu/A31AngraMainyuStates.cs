namespace BossMod.RealmReborn.Alliance.A31AngraMainyu;

class A31AngraMainyuStates : StateMachineBuilder
{
    public A31AngraMainyuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DoubleVision>()
            .ActivateOnEnter<MortalGaze>()
            .ActivateOnEnter<Level100Flare>()
            .ActivateOnEnter<Level150Death>();
    }
}
