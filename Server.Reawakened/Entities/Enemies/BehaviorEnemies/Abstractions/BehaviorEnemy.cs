﻿using A2m.Server;
using Microsoft.Extensions.DependencyInjection;
using Server.Reawakened.Entities.Components;
using Server.Reawakened.Entities.Enemies.BehaviorEnemies.BehaviourTypes;
using Server.Reawakened.Entities.Enemies.BehaviorEnemies.Extensions;
using Server.Reawakened.Entities.Interfaces;
using Server.Reawakened.Entities.Projectiles;
using Server.Reawakened.Players;
using Server.Reawakened.Players.Extensions;
using Server.Reawakened.Rooms;
using Server.Reawakened.Rooms.Extensions;
using Server.Reawakened.Rooms.Models.Planes;
using Server.Reawakened.Rooms.Services;
using Server.Reawakened.XMLs.Models.Enemy.Enums;
using UnityEngine;

namespace Server.Reawakened.Entities.Enemies.BehaviorEnemies.Abstractions;

public abstract class BehaviorEnemy(Room room, string entityId, string prefabName, EnemyControllerComp enemyController, IServiceProvider services) : Enemy(room, entityId, prefabName, enemyController, services), IDestructible
{
    public AIStatsGlobalComp Global;
    public AIStatsGenericComp Generic;
    public AIBaseBehavior AiBehavior;

    public float BehaviorEndTime;

    public StateTypes OffensiveBehavior;

    public float HealthModifier;
    public float ScaleModifier;
    public float ResistanceModifier;

    public override void Initialize()
    {
        base.Initialize();

        // Set up base config that all behaviours have
        BehaviorEndTime = 0;
        MinBehaviorTime = BehaviorModel.GlobalProperties.MinBehaviorTime;
        OffensiveBehavior = BehaviorModel.GlobalProperties.OffensiveBehavior;

        var classCopier = Services.GetRequiredService<ClassCopier>();

        //External Component Info
        Global = Room.GetEntityFromId<AIStatsGlobalComp>(Id);
        Generic = Room.GetEntityFromId<AIStatsGenericComp>(Id);
        GlobalProperties = BehaviorModel.GlobalProperties.GenerateGlobalPropertiesFromModel(classCopier, Global);

        //AIProcessData assignment, used for AI_Behavior
        AiData = new AIProcessData
        {
            Intern_SpawnPosX = Position.x,
            Intern_SpawnPosY = Position.y,
            Intern_SpawnPosZ = Position.z,
            Sync_PosX = Position.x,
            Sync_PosY = Position.y,
            SyncInit_Dir = 1,
            SyncInit_ProgressRatio = Generic.Patrol_InitialProgressRatio
        };

        AiData.SetStats(GlobalProperties);

        AiData.services = new AIServices
        {
            _shoot = new IShoot(),
            _bomber = new IBomber(),
            _scan = new IScan()
        };

        // Address magic numbers when we get to adding enemy effect mods
        SendAiInit(1, 1, 1);
    }

    public override void CheckForSpawner()
    {
        base.CheckForSpawner();

        Global = LinkedSpawner.Global;
        Generic = LinkedSpawner.Generic;
        Status = LinkedSpawner.Status;
    }

    public override void Update()
    {
        base.Update();

        // Commented lines are behaviors that have not been added yet
        // AIBehavior_Spike
        // AIBehavior_Projectile
        // AIBehavior_Acting

        switch (AiBehavior)
        {
            case AIBehaviorLookAround:
                HandleLookAround();
                break;
            case AIBehaviorPatrol:
                HandlePatrol();
                break;
            case AIBehaviorAggro:
                HandleAggro();
                break;
            case AIBehaviorComeBack:
                HandleComeBack();
                break;
            case AIBehaviorShooting:
                HandleShooting();
                break;
            case AIBehaviorBomber:
                HandleBomber();
                break;
            case AIBehaviorGrenadier:
                HandleGrenadier();
                break;
            case AIBehaviorStomper:
                HandleStomper();
                break;
            case AIBehaviorStinger:
                HandleStinger();
                break;
        }

        Position = new Vector3(AiData.Sync_PosX, AiData.Sync_PosY, Position.z);
        Hitbox.Position = new Vector3(AiData.Sync_PosX, AiData.Sync_PosY - (EnemyController.Scale.Y < 0 ? Hitbox.ColliderBox.Height : 0), Position.z);
    }

    public virtual void DetectPlayers(StateTypes type)
    {
        foreach (var player in Room.Players.Values)
        {
            if (PlayerInRange(player.TempData.Position, GlobalProperties.Global_DetectionLimitedByPatrolLine))
            {
                AiData.Sync_TargetPosX = player.TempData.Position.X;
                AiData.Sync_TargetPosY = player.TempData.Position.Y;

                ChangeBehavior(type, player.TempData.Position.X, player.TempData.Position.Y, Generic.Patrol_ForceDirectionX);
            }
        }
    }

    public bool PlayerInRange(Vector3Model pos, bool limitedByPatrolLine) =>
        AiData.Sync_PosX - (AiData.Intern_Dir < 0 ? GlobalProperties.Global_FrontDetectionRangeX : GlobalProperties.Global_BackDetectionRangeX) < pos.X &&
            pos.X < AiData.Sync_PosX + (AiData.Intern_Dir < 0 ? GlobalProperties.Global_BackDetectionRangeX : GlobalProperties.Global_FrontDetectionRangeX) &&
            AiData.Sync_PosY - GlobalProperties.Global_FrontDetectionRangeDownY < pos.Y && pos.Y < AiData.Sync_PosY + GlobalProperties.Global_FrontDetectionRangeUpY &&
            Position.z < pos.Z + 1 && Position.z > pos.Z - 1 &&
            (!limitedByPatrolLine || pos.X > AiData.Intern_MinPointX - 1.5 && pos.X < AiData.Intern_MaxPointX + 1.5);

    public void ResetBehaviorTime(float behaviorEndTime)
    {
        if (behaviorEndTime < MinBehaviorTime)
            behaviorEndTime = MinBehaviorTime;

        BehaviorEndTime = Room.Time + behaviorEndTime;
    }

    public virtual void HandleLookAround() {
        DetectPlayers(OffensiveBehavior);

        if (Room.Time >= BehaviorEndTime)
        {
            ChangeBehavior(StateTypes.Patrol, Position.x, Position.y, Generic.Patrol_ForceDirectionX);
            AiBehavior.MustDoComeback(AiData);
        }
    }

    public virtual void HandlePatrol()
    {
        AiBehavior.Update(ref AiData, Room.Time);

        if (Room.Time >= BehaviorEndTime)
            DetectPlayers(OffensiveBehavior);
    }

    public virtual void HandleComeBack() {
        if (!AiBehavior.Update(ref AiData, Room.Time))
            ChangeBehavior(StateTypes.Patrol, Position.x, Position.y, Generic.Patrol_ForceDirectionX);
    }

    public virtual void HandleAggro() {
        if (!AiBehavior.Update(ref AiData, Room.Time))
            ChangeBehavior(StateTypes.LookAround, AiData.Sync_TargetPosX, AiData.Sync_TargetPosY, AiData.Intern_Dir);
    }

    public virtual void HandleShooting()
    {
        if (AiData.Intern_FireProjectile)
        {
            var pos = new Vector3Model { X = Position.x + AiData.Intern_Dir * GlobalProperties.Global_ShootOffsetX, Y = Position.y + GlobalProperties.Global_ShootOffsetY, Z = Position.z };

            var rand = new System.Random();
            var projectileId = Math.Abs(rand.Next()).ToString();

            while (Room.GameObjectIds.Contains(projectileId))
                projectileId = Math.Abs(rand.Next()).ToString();

            // Magic numbers here are temporary
            Room.SendSyncEvent(
                AISyncEventHelper.AILaunchItem(Id, Room.Time, pos.X, pos.Y, pos.Z,
                    (float)Math.Cos(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed,
                    (float)Math.Sin(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed, 3, int.Parse(projectileId), 0
                )
            );

            AiData.Intern_FireProjectile = false;

            var prj = new AIProjectile(
                Room, Id, projectileId, pos, (float)Math.Cos(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed,
                (float)Math.Sin(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed, 3, Room.Enemies[Id].EnemyController.TimerThread,
                GameFlow.StatisticData.GetValue(ItemEffectType.AbilityPower, WorldStatisticsGroup.Enemy, Level),
                EnemyController.ComponentData.EnemyEffectType, ServerRConfig, ItemCatalog
            );

            Room.AddProjectile(prj);
        }

        if (!AiBehavior.Update(ref AiData, Room.Time))
            ChangeBehavior(StateTypes.LookAround, AiData.Sync_TargetPosX, AiData.Sync_TargetPosY, AiData.Intern_Dir);
    }

    public virtual void HandleBomber()
    {
        if (Room.Time >= BehaviorEndTime)
            base.Damage(EnemyController.MaxHealth, null);
    }

    public virtual void HandleGrenadier()
    {
        if (AiData.Intern_FireProjectile)
        {
            // Magic numbers here are temporary
            Room.SendSyncEvent(AISyncEventHelper.AILaunchItem(Id, Room.Time,
                Position.x + AiData.Intern_Dir * GlobalProperties.Global_ShootOffsetX,
                Position.y + GlobalProperties.Global_ShootOffsetY,
                Position.z,
                (float)Math.Cos(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed,
                (float)Math.Sin(AiData.Intern_FireAngle) * AiData.Intern_FireSpeed,
                3, 0, 1)
            );

            AiData.Intern_FireProjectile = false;
        }

        if (Room.Time >= BehaviorEndTime)
            ChangeBehavior(StateTypes.LookAround, Position.x, Position.y, AiData.Intern_Dir);
    }

    public void HandleStomper()
    {
        if (Room.Time >= BehaviorEndTime)
            ChangeBehavior(StateTypes.Patrol, Position.x, Position.y, Generic.Patrol_ForceDirectionX);
    }

    public void HandleStinger()
    {
        if (!AiBehavior.Update(ref AiData, Room.Time))
            ChangeBehavior(StateTypes.Patrol, Position.x, Position.y, AiData.Intern_Dir);
    }

    public override void Damage(int damage, Player player)
    {
        base.Damage(damage, player);

        if (AiBehavior is not AIBehaviorShooting)
        {
            AiData.Sync_TargetPosX = player.TempData.Position.X;
            AiData.Sync_TargetPosY = player.TempData.Position.Y;

            ChangeBehavior(OffensiveBehavior, player.TempData.Position.X, player.TempData.Position.Y, Generic.Patrol_ForceDirectionX);

            ResetBehaviorTime(MinBehaviorTime);
        }
    }

    public void ChangeBehavior(StateTypes behaviourType, float x, float y, int direction)
    {
        var behaviour = BehaviorModel.BehaviorData[behaviourType];
        var index = BehaviorModel.IndexOf(behaviourType);

        AiBehavior = behaviour.CreateBaseBehaviour(Global, Generic);

        var args = behaviour.GetStartArgs(this);

        AiBehavior.Start(ref AiData, Room.Time, args);

        Room.SendSyncEvent(AISyncEventHelper.AIDo(Id, Room.Time, Position.x, Position.y, 1.0f, index, args, x, y, direction, false));

        ResetBehaviorTime(AiBehavior.ResetTime);
    }

    public void SendAiInit(float healthMod, float sclMod, float resMod)
    {
        HealthModifier = healthMod;
        ScaleModifier = sclMod;
        ResistanceModifier = resMod;

        Room.SendSyncEvent(
            AISyncEventHelper.AIInit(
                Id, Room.Time, Position.x, Position.y, Position.z, Position.x, Position.y,
                Generic.Patrol_InitialProgressRatio, Health, MaxHealth, HealthModifier, ScaleModifier, ResistanceModifier,
                Status.Stars, Level, GlobalProperties, BehaviorModel.BehaviorData, Global, Generic
            )
        );

        ChangeBehavior(StateTypes.Patrol, Position.x, Position.y, Generic.Patrol_ForceDirectionX);
    }

    public override void SendAiData(Player player)
    {
        player.SendSyncEventToPlayer(
            AISyncEventHelper.AIInit(
                Id, Room.Time, AiData.Sync_PosX, AiData.Sync_PosY, AiData.Sync_PosZ, AiData.Intern_SpawnPosX, AiData.Intern_SpawnPosY,
                AiBehavior.GetBehaviorRatio(ref AiData, Room.Time), Health, MaxHealth, HealthModifier, ScaleModifier, ResistanceModifier,
                Status.Stars, Level, GlobalProperties, BehaviorModel.BehaviorData, Global, Generic
            )
        );

        player.SendSyncEventToPlayer(AISyncEventHelper.AIDo(Id, Room.Time, AiData.Sync_PosX, AiData.Sync_PosY, 1.0f, GetIndexOfCurrentBehavior(), AiBehavior.GetInitArgs(), AiData.Sync_TargetPosX, AiData.Sync_TargetPosY, AiData.Intern_Dir, false));
    }

    public int GetIndexOfCurrentBehavior() => BehaviorModel.IndexOf(AiBehavior.GetBehavior());
}
