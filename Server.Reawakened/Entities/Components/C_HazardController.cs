﻿using A2m.Server;
using Microsoft.Extensions.Logging;
using Server.Reawakened.Players;
using Server.Reawakened.Rooms.Extensions;
using Server.Reawakened.Rooms.Models.Entities;

namespace Server.Reawakened.Entities.Components;

public class C_HazardController : Component<HazardController>
{
    public string HurtEffect => ComponentData.HurtEffect;
    public float HurtLength => ComponentData.HurtLenght;
    public float InitialDamageDelay => ComponentData.InitialDamageDelay;
    public float DamageDelay => ComponentData.DamageDelay;
    public bool DeathPlane => ComponentData.DeathPlane;
    public string NullifyingEffect => ComponentData.NullifyingEffect;
    public bool HitOnlyVisible => ComponentData.HitOnlyVisible;
    public float InitialProgressRatio => ComponentData.InitialProgressRatio;
    public float ActiveDuration => ComponentData.ActiveDuration;
    public float DeactivationDuration => ComponentData.DeactivationDuration;
    public float HealthRatioDamage => ComponentData.HealthRatioDamage;
    public int HurtSelfOnDamage => ComponentData.HurtSelfOnDamage;

    public ILogger<C_HazardController> Logger { get; set; }

    public override object[] GetInitData(Player player) => new object[] { 0 };

    public override void NotifyCollision(NotifyCollision_SyncEvent notifyCollisionEvent, Player player)
    {
        if (HurtEffect == "NoEffect")
            return;

        var character = player.Character;

        Enum.TryParse(HurtEffect, true, out ItemEffectType effectType);

        if (effectType == default)
        {
            Logger.LogWarning("No hazard type found for {Type}. Returning...", HurtEffect);
            return;
        }

        var statusEffect = new StatusEffect_SyncEvent(player.GameObjectId.ToString(), Room.Time, (int)effectType,
            0, Convert.ToInt32(HurtLength), true, Entity.GameObject.ObjectInfo.PrefabName, false);

        Room.SendSyncEvent(statusEffect);

        Logger.LogTrace("Triggered status effect for {Character} of {HurtType}", character.Data.CharacterName,
            effectType);

        switch (effectType)
        {
            default:
                SendComponentMethodUnknown("unran-hazards", "Failed Hazard Event", "Hazard Type Switch",
                    $"Effect Type: {effectType}");
                break;
        }
    }
}
