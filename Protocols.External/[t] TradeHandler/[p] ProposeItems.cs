﻿using Server.Reawakened.Network.Extensions;
using Server.Reawakened.Network.Protocols;
using Server.Reawakened.Players.Helpers;
using Server.Reawakened.Players.Models.Temporary;

namespace Protocols.External._t__TradeHandler;

public class ProposeItems : ExternalProtocol
{
    public override string ProtocolName => "tp";

    public PlayerHandler PlayerHandler { get; set; }

    public override void Run(string[] message)
    {
        var itemsProposed = message[5];
        var bananas = int.Parse(message[6]);

        var tradeModel = Player.TempData.TradeModel;

        if (tradeModel == null)
            return;

        tradeModel.ItemsInTrade = TradeModel.ReverseProposeItems(itemsProposed);
        tradeModel.BananasInTrade = bananas;
        
        tradeModel.TradingPlayer?.SendXt("tp", itemsProposed, bananas);
    }
}