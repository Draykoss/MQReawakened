﻿using Microsoft.Extensions.Logging;
using Server.Base.Accounts.Models;
using Server.Base.Core.Extensions;
using Server.Base.Network;
using Server.Reawakened.Configs;
using Server.Reawakened.Entities;
using Server.Reawakened.Levels.Enums;
using Server.Reawakened.Levels.Extensions;
using Server.Reawakened.Levels.Models.Entities;
using Server.Reawakened.Levels.Services;
using Server.Reawakened.Network.Extensions;
using Server.Reawakened.Network.Helpers;
using Server.Reawakened.Players;
using Server.Reawakened.Players.Extensions;
using WorldGraphDefines;

namespace Server.Reawakened.Levels;

public class Level
{
    private readonly ILogger<LevelHandler> _logger;
    private readonly ServerConfig _serverConfig;

    private readonly HashSet<int> _gameObjectIds;
    public readonly Dictionary<int, NetState> Clients;

    private readonly LevelHandler _levelHandler;

    public LevelInfo LevelInfo { get; set; }
    public LevelPlanes LevelPlaneHandler { get; set; }
    public LevelEntities LevelEntityHandler { get; set; }

    public long TimeOffset { get; set; }

    public long Time => Convert.ToInt64(Math.Floor((GetTime.GetCurrentUnixMilliseconds() - TimeOffset) / 1000.0));

    public Level(LevelInfo levelInfo, LevelPlanes levelPlaneHandler, ServerConfig serverConfig,
        LevelHandler levelHandler, ReflectionUtils reflection, IServiceProvider services, ILogger<LevelHandler> logger)
    {
        _serverConfig = serverConfig;
        _levelHandler = levelHandler;
        _logger = logger;
        Clients = new Dictionary<int, NetState>();
        _gameObjectIds = new HashSet<int>();

        LevelInfo = levelInfo;
        LevelPlaneHandler = levelPlaneHandler;
        TimeOffset = GetTime.GetCurrentUnixMilliseconds();

        LevelEntityHandler = new LevelEntities(this, _levelHandler, reflection, services, _logger);

        if (levelPlaneHandler.Planes == null)
            return;

        foreach (var gameObjectId in levelPlaneHandler.Planes.Values
                     .Select(x => x.GameObjects.Values)
                     .SelectMany(x => x)
                     .Select(x => x.ObjectInfo.ObjectId)
                )
            _gameObjectIds.Add(gameObjectId);
    }

    public void AddClient(NetState newClient, out JoinReason reason)
    {
        var playerId = -1;

        if (Clients.Count > _serverConfig.PlayerCap)
        {
            reason = JoinReason.Full;
        }
        else
        {
            playerId = 1;

            while (_gameObjectIds.Contains(playerId))
                playerId++;

            Clients.Add(playerId, newClient);
            _gameObjectIds.Add(playerId);
            reason = JoinReason.Accepted;
        }

        newClient.Get<Player>().PlayerId = playerId;

        if (reason == JoinReason.Accepted)
        {
            var newPlayer = newClient.Get<Player>();

            if (LevelInfo.LevelId == -1)
                return;

            // JOIN CONDITION
            newClient.SendXml("joinOK", $"<pid id='{newPlayer.PlayerId}' /><uLs />");

            if (LevelInfo.LevelId == 0)
                return;

            // USER ENTER
            var newAccount = newClient.Get<Account>();

            foreach (var currentClient in Clients.Values)
            {
                var currentPlayer = currentClient.Get<Player>();
                var currentAccount = currentClient.Get<Account>();

                var areDifferentClients = currentPlayer.UserInfo.UserId != newPlayer.UserInfo.UserId;

                newClient.SendUserEnterData(currentPlayer, currentAccount);

                if (areDifferentClients)
                    currentClient.SendUserEnterData(newPlayer, newAccount);
            }
        }
        else
        {
            newClient.SendXml("joinKO", $"<error>{reason.GetJoinReasonError()}</error>");
        }
    }

    public void SendCharacterInfo(Player newPlayer, NetState newClient)
    {
        // WHERE TO SPAWN
        var character = newPlayer.GetCurrentCharacter();

        BaseSyncedEntity spawnLocation = null;

        var spawnPoints = LevelEntityHandler.GetEntities<SpawnPointEntity>();
        var portals = LevelEntityHandler.GetEntities<PortalControllerEntity>();

        if (character.PortalId != 0)
            if (portals.TryGetValue(character.PortalId, out var portal))
                spawnLocation = portal;

        if (spawnLocation == null)
            if (spawnPoints.TryGetValue(character.SpawnPoint, out var spawnPoint))
                spawnLocation = spawnPoint;

        if (spawnLocation == null)
        {
            var spawnPoint = spawnPoints.Values.FirstOrDefault(s => s.Index == character.SpawnPoint);
            if (spawnPoint != null)
                spawnLocation = spawnPoint;
        }

        var defaultSpawn = spawnPoints.Values.MinBy(p => p.Index);

        if (defaultSpawn != null)
            spawnLocation ??= defaultSpawn;
        else
            throw new InvalidDataException($"Could not find default spawn point in {LevelInfo.LevelId}, as there are none initialized!");

        character.Data.SpawnPositionX = spawnLocation.Position.X + spawnLocation.Scale.X / 2;
        character.Data.SpawnPositionY = spawnLocation.Position.Y + spawnLocation.Scale.Y / 2;
        character.Data.SpawnOnBackPlane = spawnLocation.Position.Z > 1;

        _logger.LogDebug("Spawning {CharacterName} at object '{Object}' (portal '{Portal}' spawn '{SpawnPoint}') at '{NewLevel}'.",
            character.Data.CharacterName,
            spawnLocation.Id != 0 ? spawnLocation.Id : "DEFAULT",
            character.PortalId != 0 ? character.PortalId : "DEFAULT",
            character.SpawnPoint != 0 ? character.SpawnPoint : "DEFAULT",
            character.Level
        );

        _logger.LogDebug("Position of spawn: {Position}", spawnLocation.Position);

        // CHARACTER DATA

        foreach (var currentClient in Clients.Values)
        {
            var currentPlayer = currentClient.Get<Player>();

            var areDifferentClients = currentPlayer.UserInfo.UserId != newPlayer.UserInfo.UserId;

            newClient.SendCharacterInfoData(currentPlayer,
                areDifferentClients ? CharacterInfoType.Lite : CharacterInfoType.Portals, LevelInfo);

            if (areDifferentClients)
                currentClient.SendCharacterInfoData(newPlayer, CharacterInfoType.Lite, LevelInfo);
        }
    }
    
    public void DumpPlayersToLobby()
    {
        foreach (var playerId in Clients.Keys)
            DumpPlayerToLobby(playerId);
    }

    public void DumpPlayerToLobby(int playerId)
    {
        var client = Clients[playerId];
        client.Get<Player>().JoinLevel(client, _levelHandler.GetLevelFromId(-1), out _);
        RemoveClient(playerId);
    }

    public void RemoveClient(int playerId)
    {
        Clients.Remove(playerId);
        _gameObjectIds.Remove(playerId);

        if (Clients.Count == 0 && LevelInfo.LevelId > 0)
            _levelHandler.RemoveLevel(LevelInfo.LevelId);
    }

    public void SendSyncEvent(SyncEvent syncEvent, Player sentPlayer = null)
    {
        foreach (
            var client in
            from client in Clients.Values
            let receivedPlayer = client.Get<Player>()
            where sentPlayer == null || receivedPlayer.UserInfo.UserId != sentPlayer.UserInfo.UserId
            select client
        )
            client.SendSyncEventToPlayer(syncEvent);
    }
}