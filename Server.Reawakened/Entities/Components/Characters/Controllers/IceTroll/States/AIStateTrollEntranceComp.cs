﻿namespace Server.Reawakened.Entities.Components.Characters.Controllers.IceTroll.States;
public class AIStateTrollEntranceComp : BaseAIState<AIStateTrollEntrance>
{
    public float DelayBeforeEntranceDuration => ComponentData.DelayBeforeEntranceDuration;
    public float EntranceDuration => ComponentData.EntranceDuration;
    public float IntroDuration => ComponentData.IntroDuration;
}