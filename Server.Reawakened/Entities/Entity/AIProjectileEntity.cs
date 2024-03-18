﻿using Server.Base.Timers.Services;
using Server.Reawakened.Rooms;
using Server.Reawakened.Rooms.Extensions;
using Server.Reawakened.Rooms.Models.Entities.ColliderType;
using Server.Reawakened.Rooms.Models.Planes;

namespace Server.Reawakened.Entities.Entity;
public class AIProjectileEntity : TicklyEntity
{
    private readonly string _ownerId;
    private readonly Room _room;

    public AIProjectileEntity(Room room, string ownerId, string projectileId, Vector3Model position, float speedX, float speedY, float lifeTime, bool isGrenade, TimerThread timerThread)
    {
        // Initialize projectile location info
        _room = room;
        _ownerId = ownerId;

        ProjectileID = projectileId;
        Position = position;
        PrjPlane = Position.Z > 10 ? "Plane1" : "Plane0";
        SpawnPosition = new Vector3Model { X = Position.X, Y = Position.Y, Z = Position.Z };

        // Initialize projectile info
        SpeedX = speedX;
        SpeedY = speedY;
        StartTime = _room.Time;
        LifeTime = StartTime + lifeTime;
        IsGrenade = isGrenade;

        // Send all information to room
        Collider = new AIProjectileCollider(ProjectileID, room, Position, 0.5f, 0.5f, PrjPlane, LifeTime, timerThread);
    }

    public override void Update()
    {
        if (SpeedX != 0 || SpeedY != 0)
        {
            Position.X = SpawnPosition.X + (_room.Time - StartTime) * SpeedX;
            Collider.Position.x = Position.X;

            Position.Y = SpawnPosition.Y + (_room.Time - StartTime) * SpeedY;
            Collider.Position.y = Position.Y;
        }

        var Collisions = Collider.IsColliding(true);

        if (Collisions.Length > 0)
            foreach (var collision in Collisions)
                Hit(collision);

        if (LifeTime <= _room.Time)
            Hit("-1");
    }

    public override void Hit(string hitGoID)
    {
        //Logger.LogInformation("Projectile with ID {args1} destroyed at position ({args2}, {args3}, {args4})", ProjectileID, Position.X, Position.Y, Position.Z);
        var hit = new ProjectileHit_SyncEvent(new SyncEvent(_ownerId, SyncEvent.EventType.ProjectileHit, _room.Time));

        hit.EventDataList.Add(int.Parse(ProjectileID));
        hit.EventDataList.Add(hitGoID);
        hit.EventDataList.Add(0);
        hit.EventDataList.Add(Position.X);
        hit.EventDataList.Add(Position.Y);

        _room.SendSyncEvent(hit);
        _room.Projectiles.Remove(ProjectileID);
    }
}
