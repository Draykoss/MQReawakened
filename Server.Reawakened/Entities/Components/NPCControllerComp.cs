﻿using A2m.Server;
using FollowCamDefines;
using Microsoft.Extensions.Logging;
using Server.Base.Core.Extensions;
using Server.Base.Logging;
using Server.Reawakened.Configs;
using Server.Reawakened.Entities.Enums;
using Server.Reawakened.Network.Extensions;
using Server.Reawakened.Players;
using Server.Reawakened.Players.Extensions;
using Server.Reawakened.Players.Models.Character;
using Server.Reawakened.Rooms.Models.Entities;
using Server.Reawakened.XMLs.Bundles;
using Server.Reawakened.XMLs.BundlesInternal;
using Server.Reawakened.XMLs.Enums;
using Server.Reawakened.XMLs.Models.Npcs;
using System.Text;
using static A2m.Server.QuestStatus;
using static NPCController;

namespace Server.Reawakened.Entities.Components;

public class NPCControllerComp : Component<NPCController>
{
    public FollowCamModes CameraMode => ComponentData.CameraMode;
    public FollowCamPriority CameraPriority => ComponentData.CameraPriority;
    public bool ShouldDisableNpcInteraction => ComponentData.ShouldDisableNPCInteraction;

    public ILogger<NPCControllerComp> Logger { get; set; }
    public ServerRConfig RConfig { get; set; }
    public FileLogger FileLogger { get; set; }

    public QuestCatalog QuestCatalog { get; set; }
    public ItemCatalog ItemCatalog { get; set; }
    public DialogDictionary Dialog { get; set; }
    public MiscTextDictionary MiscText { get; set; }

    public ServerRConfig Config { get; set; }

    public InternalVendor VendorCatalog { get; set; }
    public InternalDialog DialogCatalog { get; set; }
    public InternalDialogRewrite DialogRewrites { get; set; }

    public VendorInfo VendorInfo;
    public DialogInfo DialogInfo;

    public QuestDescription[] ValidatorQuests;
    public QuestDescription[] GiverQuests;

    public int NameId;
    public string NpcName;

    public NpcType NpcType;

    public override void InitializeComponent()
    {
        NameId = -1;
        NpcType = NpcType.Unknown;

        VendorInfo = VendorCatalog.GetVendorById(int.Parse(Id));

        if (VendorInfo != null)
        {
            NpcType = NpcType.Vendor;
            NameId = VendorInfo.NameId;
        }

        GiverQuests = [..
            QuestCatalog.GetQuestGiverById(int.Parse(Id))
            .Where(x => x.QuestGiverLevelId == Room.LevelInfo.LevelId)
            .OrderBy(x => x.Id)
        ];

        ValidatorQuests = [..
            QuestCatalog.GetQuestValidatorById(int.Parse(Id))
                .Where(x => x.ValidatorLevelId == Room.LevelInfo.LevelId)
                .OrderBy(x => x.Id)
        ];


        // Clean this part up. For now, this is to get progress on 2013 working.
        if (!Config.Is2014Client)
        {
            if (Id == Config.OnkoGoId.ToString())
            {
                Console.WriteLine("hi");
                GiverQuests = [..
                    QuestCatalog.GetQuestGiverByName("Onko")
                        .Where(x => x.QuestGiverLevelId == Room.LevelInfo.LevelId)
                        .OrderBy(x => x.Id)
                ];
            }
            if (GiverQuests.Length > 0)
            {
                GiverQuests = [..
                    QuestCatalog.GetQuestGiverByName(GiverQuests[0].QuestgGiverName)
                        .Where(x => x.QuestGiverLevelId == Room.LevelInfo.LevelId)
                        .OrderBy(x => x.Id)
                ];
            }
        }
        

        var questGiverNames = GiverQuests.Select(x => (int)x.GetField("_questGiverNameId"));
        var questValidatorNames = ValidatorQuests.Select(x => (int)x.GetField("_validatorNameId"));
        var allQuestNames = questGiverNames.Concat(questValidatorNames).Where(i => i > 0).ToArray();

        Console.WriteLine(ComponentData.NpcName);

       // GiverQuests2013 = [..
       //     QuestCatalog.GetQuestGiverById(int.Parse(Id))
       //         .Where(x => x.QuestGiverLevelId == Room.LevelInfo.LevelId)
       //         .OrderBy(x => x.Id)
       // ];

        //ValidatorQuests2013 = [..
        //    QuestCatalog.GetQuestValidatorById(int.Parse(Id))
        //        .Where(x => x.ValidatorLevelId == Room.LevelInfo.LevelId)
        //        .OrderBy(x => x.Id)
        //];

        if (allQuestNames.Length > 0)
        {
            NpcType = NpcType.Quest;
            NameId = (from item in allQuestNames
                      group item by item into g
                      orderby g.Count() descending
                      select g.Key)
                      .First();
        }

        DialogInfo = DialogCatalog.GetDialogById(Room.LevelInfo.LevelId, int.Parse(Id));

        if (DialogInfo != null && NpcType == NpcType.Unknown)
        {
            NpcType = NpcType.Dialog;
            NameId = DialogInfo.NameId;
        }

        if (NameId < 0)
        {
            Logger.LogWarning("No information found for NPC {Name} ({Id})", PrefabName, Id);
            return;
        }

        NpcName = MiscText.GetLocalizationTextById(NameId);
    }

    public override object[] GetInitData(Player player) => NameId <= 0 ? [] : [NameId.ToString()];

    public override void RunSyncedEvent(SyncEvent syncEvent, Player player)
    {
        if (syncEvent.Type == SyncEvent.EventType.Trigger)
        {
            var tEvent = new Trigger_SyncEvent(syncEvent);

            if (tEvent.Activate)
                TalkToNpc(player);
            else
                Logger.LogDebug("[INACTIVE NPC TRIGGERED] [{Name} ({Id})]", Name, Id);
        }
        else
            Logger.LogDebug("[UNKNOWN NPC EVENT] [{Type}] [{Name} ({Id})]", syncEvent.Type.ToString().ToUpperInvariant(), Name, Id);
    }

    public void TalkToNpc(Player player)
    {
        player.CheckObjective(ObjectiveEnum.Talkto, Id, PrefabName, 1);

        player.CheckAchievement(AchConditionType.Talkto, PrefabName, Logger);

        switch (NpcType)
        {
            case NpcType.Vendor:
                player.SendXt("nv", VendorInfo.ToString(player));
                break;
            case NpcType.Quest:
                switch (GetQuestStatus(player))
                {
                    case NPCStatus.QuestAvailable:
                        StartNewQuest(player);
                        Logger.LogDebug("[AVALIABLE QUEST] [{Name} ({Id})]", Name, Id);
                        break;
                    case NPCStatus.QuestInProgress:
                        SendQuestProgress(player);
                        Logger.LogDebug("[IN PROGRESS QUEST] [{Name} ({Id})]", Name, Id);
                        break;
                    case NPCStatus.QuestCompleted:
                        ValidateQuest(player);
                        Logger.LogDebug("[COMPLETED QUEST] [{Name} ({Id})]", Name, Id);
                        break;
                    case NPCStatus.QuestUnavailable:
                        SendDialog(player);
                        Logger.LogDebug("[DIALOG QUEST] [{Name} ({Id})]", Name, Id);
                        break;
                    default:
                        break;
                }

                player.CheckObjective(ObjectiveEnum.Talkto, Id, PrefabName, 1);

                var newStatus = GetQuestStatus(player);

                if (newStatus is NPCStatus.QuestCompleted or NPCStatus.QuestAvailable)
                    TalkToNpc(player);

                break;
            case NpcType.Dialog:
                SendDialog(player);
                Logger.LogDebug("[DIALOG] [{Name} ({Id})]", Name, Id);
                break;
            default:
                Logger.LogDebug("[UNKNOWN NPC INTERACTION] [{Name} ({Id})]", Name, Id);
                break;
        }
    }

    public void SendNpcInfo(Player player)
    {
        var npcStatus = NPCStatus.Unknown;
        var descriptionId = 0;

        switch (NpcType)
        {
            case NpcType.Vendor:
                npcStatus = VendorInfo.VendorType;
                descriptionId = VendorInfo.DescriptionId;
                break;
            case NpcType.Quest:
                npcStatus = GetQuestStatus(player);
                break;
            case NpcType.Dialog:
                npcStatus = NPCStatus.Dialog;
                descriptionId = DialogInfo.DescriptionId;
                break;
            default:
                break;
        }

        player.SendXt("nt", Id, (int)npcStatus, descriptionId);
    }

    public void SendDialog(Player player)
    {
        if (DialogInfo == null)
        {
            Logger.LogError("[DIALOG] [{NpcName} ({Id})] No dialog catalog found for NPC",
                NpcName, Id);
            return;
        }

        var dialog = DialogInfo.Dialog
            .Where(d => d.Key <= player.Character.Data.GlobalLevel)
            .OrderBy(d => d.Key)
            .Select(d => d.Value)
            .FirstOrDefault();

        if (dialog == null)
        {
            Logger.LogError("[DIALOG] [{NpcName} ({Id})] No dialog found for user of level {Level}",
                NpcName, Id, player.Character.Data.GlobalLevel);
            return;
        }

        player.NetState.SendXt("nd", Id, NameId, dialog);
    }

    public NPCStatus GetQuestStatus(Player player)
    {
        foreach (var validatorQuest in ValidatorQuests)
        {
            var questStatus = GetQuestType(player, validatorQuest.Id);

            if (questStatus == NPCStatus.QuestCompleted)
            {
                Logger.LogTrace("[{QuestId}] [COMPLETED QUEST] Quest from {NpcName} ({Id}) has been validated",
                    validatorQuest.Id, NpcName, Id);

                return NPCStatus.QuestCompleted;
            }
        }

        foreach (var givenQuest in GiverQuests)
        {
            var questStatus = GetQuestType(player, givenQuest.Id);

            if (questStatus == NPCStatus.QuestInProgress)
            {
                Logger.LogTrace("[{QuestId}] [QUEST IN PROGRESS] Quest from {NpcName} ({Id}) is in progress",
                    givenQuest.Id, NpcName, Id);

                return NPCStatus.QuestInProgress;
            }

            if (questStatus == NPCStatus.QuestAvailable)
            {
                Logger.LogTrace("[{Quest} ({QuestId})] [AVAILABLE QUEST] Availale from {NpcName} ({Id})",
                    givenQuest.Name, givenQuest.Id, NpcName, Id);

                return NPCStatus.QuestAvailable;
            }
        }

        return NPCStatus.QuestUnavailable;
    }

    private NPCStatus GetQuestType(Player player, int questId)
    {
        var questData = QuestCatalog.GetQuestData(questId);

        if (player.Character.Data.CompletedQuests.Contains(questId))
        {
            Logger.LogTrace("[{QuestName} ({QuestId})] [ALREADY COMPLETED]", questData.Name, questData.Id);
            return NPCStatus.Unknown;
        }

        var currentQuest = player.Character.Data.QuestLog.FirstOrDefault(q => q.Id == questId);

        if (currentQuest != null)
        {
            if (currentQuest.QuestStatus == QuestState.IN_PROCESSING)
            {
                var canSendQuestComplete = true;

                foreach (var objective in currentQuest.Objectives.Values)
                {
                    if (objective.Completed)
                        continue;

                    if (objective.CountLeft > 1 ||
                        objective.ObjectiveType != ObjectiveEnum.Talkto ||
                        objective.GameObjectLevelId != Room.LevelInfo.LevelId ||
                        objective.GameObjectId.ToString() != Id
                    )
                    {
                        canSendQuestComplete = false;
                        break;
                    }
                    else
                    {
                        objective.Completed = true;
                        objective.CountLeft = 0;
                    }
                }

                if (canSendQuestComplete)
                    currentQuest.QuestStatus = QuestState.TO_BE_VALIDATED;
            }

            if (currentQuest.QuestStatus == QuestState.IN_PROCESSING)
            {
                Logger.LogTrace("[{QuestName} ({QuestId})] [ALREADY IN PROGRESS]", questData.Name, questData.Id);
                return NPCStatus.QuestInProgress;
            }
            else if (currentQuest.QuestStatus == QuestState.TO_BE_VALIDATED)
            {
                Logger.LogTrace("[{QuestName} ({QuestId})] [TO BE VALIDATED]", questData.Name, questData.Id);
                return NPCStatus.QuestCompleted;
            }
        }

        if (!QuestCatalog.QuestLineCatalogs.TryGetValue(questData.QuestLineId, out var questLine))
        {
            Logger.LogTrace("[{QuestName} ({QuestId})] [INVALID QUESTLINE] Quest with line {QuestLineId} could not be found",
                questData.Name, questData.Id, questData.QuestLineId);
            return NPCStatus.Unknown;
        }

        if (questLine.QuestType == QuestType.Daily || !questLine.ShowInJournal || QuestCatalog.GetQuestLineTotalQuestCount(questLine) == 0)
        {
            Logger.LogTrace("[{QuestName} ({QuestId})] [SKIPPED QUESTLINE] Quest with line {QuestLineId} was skipped as it does not meet valid preconditions",
                questData.Name, questData.Id, questData.QuestLineId);
            return NPCStatus.Unknown;
        }

        if (player.Character.Data.GlobalLevel < questData.LevelRequired)
        {
            Logger.LogTrace("[{QuestName} ({QuestId})] [SKIPPED QUEST] Level {Current} is less than required {Required}",
                questData.Name, questData.Id, player.Character.Data.GlobalLevel, questData.LevelRequired);
            return NPCStatus.Unknown;
        }

        /*
        var previousQuestOnQuestLine = QuestCatalog.GetPreviousQuestOnQuestLine(questLine, questData);

        if (previousQuestOnQuestLine != null)
        {
            if (player.Character.Data.CompletedQuests.Contains(previousQuestOnQuestLine.Id))
            {
                Logger.LogDebug("[{QuestName} ({QuestId})] [FOUND QUEST FROM NEXT IN LINE]", questData.Name, questData.Id);
                return NPCStatus.QuestAvailable;
            }
            else
            {
                Logger.LogTrace("[{QuestName} ({QuestId})] [CAN NOT START AS PREVIOUS QUEST] Quest {Name} {Id} has not begun",
                    questData.Name, questData.Id, previousQuestOnQuestLine.Name, previousQuestOnQuestLine.Id);
                return NPCStatus.Unknown;
            }
        }
        */

        var requiredQuests = QuestCatalog.GetListOfPreviousQuests(questData);

        var canStartQuest = false;

        foreach (var item in requiredQuests)
            if (player.Character.Data.CompletedQuests.Contains(item.Id))
            {
                canStartQuest = true;
                break;
            }

        if (requiredQuests.Count == 0 || canStartQuest)
        {
            Logger.LogDebug("[{QuestName} ({QuestId})] [FOUND QUEST THAT MEETS REQUIREMENTS]", questData.Name, questData.Id);
            return NPCStatus.QuestAvailable;
        }

        Logger.LogTrace(
            "[{QuestName} ({QuestId})] [DOES NOT MEET REQUIRED QUESTS] {Quests}",
            questData.Name, questData.Id,
            string.Join(", ", requiredQuests.Select(x => x.Name))
        );

        return NPCStatus.Unknown;
    }

    public void ValidateQuest(Player player)
    {
        foreach (var quest in ValidatorQuests)
        {
            var matchingQuest = player.Character.Data.QuestLog.FirstOrDefault(q => q.Id == quest.Id);

            if (matchingQuest == null || player.Character.Data.CompletedQuests.Contains(quest.Id))
                continue;

            if (matchingQuest.QuestStatus != QuestState.TO_BE_VALIDATED)
                continue;

            SendNpcDialog(player, matchingQuest, 1);

            var completedQuest = player.Character.Data.QuestLog.FirstOrDefault(x => x.Id == quest.Id);

            if (completedQuest != null)
            {
                player.Character.Data.QuestLog.Remove(completedQuest);
                player.NetState.SendXt("nq", completedQuest.Id);
                player.Character.Data.CompletedQuests.Add(completedQuest.Id);
                Logger.LogInformation("[{QuestName} ({QuestId})] [QUEST COMPLETED]", quest.Name, quest.Id);
            }

            break;
        }
    }

    private void SendQuestProgress(Player player)
    {
        var quests = GiverQuests.Concat(ValidatorQuests);

        foreach (var quest in quests)
        {
            var matchingQuest = player.Character.Data.QuestLog.FirstOrDefault(q => q.Id == quest.Id);

            if (matchingQuest == null || player.Character.Data.CompletedQuests.Contains(quest.Id))
                continue;

            SendNpcDialog(player, matchingQuest, 2);

            break;
        }
    }

    public void StartNewQuest(Player player)
    {
        foreach (var givenQuest in GiverQuests)
        {
            if (GetQuestType(player, givenQuest.Id) == NPCStatus.QuestAvailable)
            {
                var quest = player.AddQuest(givenQuest, true);

                SendNpcDialog(player, quest, 0);

                Logger.LogTrace("[{QuestName} ({QuestId})] [ADD QUEST] Added by {Name}", givenQuest.Name, givenQuest.Id, NpcName);

                var rewardIds = (Dictionary<int, int>)givenQuest.GetField("_rewardItemsIds");
                var unknownRewards = rewardIds.Where(x => !ItemCatalog.Items.ContainsKey(x.Key));

                if (unknownRewards.Any())
                {
                    var sb = new StringBuilder();

                    foreach (var reward in unknownRewards)
                        sb.AppendLine($"Reward Id {reward.Key}, Count {reward.Value}");

                    FileLogger.WriteGenericLog<NPCController>("unknown-rewards", $"[Unkwown Quest {givenQuest.Id} Rewards]", sb.ToString(),
                        LoggerType.Error);
                }

                quest.QuestStatus = QuestState.IN_PROCESSING;

                player.UpdateNpcsInLevel(givenQuest);

                Logger.LogInformation("[{QuestName} ({QuestId})] [QUEST STARTED]", givenQuest.Name, quest.Id);

                return;
            }
        }
    }

    public void SendNpcDialog(Player player, QuestStatusModel questStatus, int dialogNumber)
    {
        var quest = QuestCatalog.GetQuestData(questStatus.Id);
        var questName = quest.Name;

        if (quest.ValidatorGoId != quest.QuestGiverGoId && quest.ValidatorGoId.ToString() == Id)
            questName += "validator";

        if (DialogRewrites.Rewrites.TryGetValue(questName, out var rewrittenName))
            questName = rewrittenName;

        if (!Dialog.QuestDialog.TryGetValue(questName, out var questDialog))
        {
            Logger.LogError("[{QuestName}] [UNKNOWN QUEST DIALOG]", questName);
            return;
        }

        if (questDialog.Count < dialogNumber)
        {
            Logger.LogError("[{QuestName}] [UNKNOWN DIALOG ID {DialogId}]", questName, dialogNumber);
            return;
        }

        var dialog = questDialog[dialogNumber];

        if (!Dialog.DialogDict.TryGetValue(dialog.DialogId, out var dialogXml))
        {
            Logger.LogError("[{QuestName}] [UNKNOWN XML DIALOG ID {DialogId}]", questName, dialog.DialogId);
            return;
        }

        var conversation = dialogXml.FirstOrDefault(x => x.ConversationId == dialog.ConversationId);

        if (conversation == null)
        {
            Logger.LogError("[{QuestName}] [UNKNOWN XML CONVERSATION ID {DialogId}]", questName, dialog.ConversationId);
            return;
        }

        if (conversation.Lines.All(x => x.TextId == 0))
        {
            SendDialog(player);
            return;
        }

        player.NetState.SendXt("nl", questStatus, Id, NameId, dialog);
    }
}
