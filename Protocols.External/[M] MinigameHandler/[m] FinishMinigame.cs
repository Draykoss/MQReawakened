﻿using Microsoft.Extensions.Logging;
using Server.Reawakened.Entities.AbstractComponents;
using Server.Reawakened.Network.Extensions;
using Server.Reawakened.Network.Protocols;
using Server.Reawakened.Players;
using Server.Reawakened.Players.Extensions;
using Server.Reawakened.Players.Helpers;
using Server.Reawakened.Players.Models.Arenas;
using Server.Reawakened.XMLs.BundlesInternal;

namespace Protocols.External._M__MinigameHandler;

public class FinishedMinigame : ExternalProtocol
{
    public override string ProtocolName => "Mm";

    public InternalLoot LootCatalog { get; set; }
    public ILogger<FinishedMinigame> Logger { get; set; }

    public override void Run(string[] message)
    {
        var arenaObjectId = message[5];
        var finishedRaceTime = float.Parse(message[6]);

        Logger.LogInformation("Minigame with ID ({minigameId}) has completed.", arenaObjectId);

        foreach (var player in Player.Room.Players.Values)
            player.SendXt("Mt", arenaObjectId, Player.CharacterId, finishedRaceTime);

        if (Player.Character.BestMinigameTimes.TryGetValue(Player.Room.LevelInfo.Name, out var time))
        {
            if (finishedRaceTime < time)
            {
                Player.Character.BestMinigameTimes[Player.Room.LevelInfo.Name] = finishedRaceTime;
                Player.SendXt("Ms", Player.Room.LevelInfo.InGameName);
            }
        }

        else
        {
            Player.Character.BestMinigameTimes.Add(Player.Room.LevelInfo.Name, finishedRaceTime);
            Player.SendXt("Ms", Player.Room.LevelInfo.InGameName);
        }

        var trigger = Player.Room.GetEntityFromId<ITriggerComp>(arenaObjectId);

        if (trigger == null)
        {
            Logger.LogError("Cannot find statue with ID: {ID}", arenaObjectId);
            return;
        }

        trigger.RemovePhysicalInteractor(Player.GameObjectId);

        if (trigger.GetPhysicalInteractorCount() <= 0)
        {
            var players = Player.Room.Players;
            foreach (var player in players.Values)
                FinishMinigame(player, arenaObjectId, players.Count);

            trigger.RunTrigger(Player);
            trigger.ResetTrigger();
        }
    }

    public void FinishMinigame(Player player, string minigameId, int membersInRoom)
    {
        player.SendSyncEventToPlayer(new TriggerUpdate_SyncEvent(minigameId, player.Room.Time, membersInRoom));

        var rdmBananaReward = new Random().Next(7, 11 * player.Character.Data.GlobalLevel);
        var xpReward = player.Character.Data.ReputationForNextLevel / 30;

        var lootedItems = ArenaModel.GrantLootedItems(LootCatalog, player.Room.LevelInfo.LevelId, minigameId);
        var lootableItems = ArenaModel.GrantLootableItems(LootCatalog, player.Room.LevelInfo.LevelId, minigameId);

        var sb = new SeparatedStringBuilder('<');

        sb.Append(membersInRoom.ToString());
        sb.Append(rdmBananaReward.ToString());
        sb.Append(xpReward.ToString());
        sb.Append(lootedItems.ToString());
        sb.Append(lootableItems.ToString());

        player.SendXt("Mp", minigameId, sb.ToString());

        player.SendCashUpdate();
    }
}
