﻿// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2017 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using FullSerializer;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Questing.Actions;
using DaggerfallWorkshop.Game.Serialization;

namespace DaggerfallWorkshop.Game.Questing
{
    /// <summary>
    /// Hosts quests and manages their execution during play.
    /// Quests are instantiated from a source text template.
    /// It's possible to have the same quest multiple times (e.g. same fetch quest from two different mage guildhalls).
    /// Running quests can perform actions in the world (e.g. spawn enemies and play sounds).
    /// Or they can provide data to external systems like the NPC dialog interface (e.g. 'tell me about' and 'rumors').
    /// Quest support is considered to be in very early prototype stages and may change at any time.
    /// 
    /// Notes:
    ///  * Quests are not serialized at this time.
    ///  * Some data, such as reserved sites, need to be serialized from QuestMachine.
    /// </summary>
    public class QuestMachine : MonoBehaviour
    {
        #region Fields

        // Public constants
        public const string questPersonTag = "QuestPerson";
        public const string questFoeTag = "QuestFoe";
        public const string questItemTag = "QuestItem";

        const float startupDelay = 0f;          // How long quest machine will wait before running active quests
        const float ticksPerSecond = 10;        // How often quest machine will tick quest logic per second

        // Folder names constants
        const string questSourceFolderName = "Quests";
        const string questTablesFolderName = "Tables";

        // Table constants
        const string globalVarsTableFilename = "Quests-GlobalVars";
        const string staticMessagesTableFilename = "Quests-StaticMessages";
        const string placesTableFilename = "Quests-Places";
        const string soundsTableFilename = "Quests-Sounds";
        const string itemsTableFileName = "Quests-Items";
        const string factionsTableFileName = "Quests-Factions";
        const string foesTableFileName = "Quests-Foes";

        // Data tables
        Table globalVarsTable;
        Table staticMessagesTable;
        Table placesTable;
        Table soundsTable;
        Table itemsTable;
        Table factionsTable;
        Table foesTable;

        List<IQuestAction> actionTemplates = new List<IQuestAction>();
        Dictionary<ulong, Quest> quests = new Dictionary<ulong, Quest>();
        List<SiteLink> siteLinks = new List<SiteLink>();
        List<Quest> questsToTombstone = new List<Quest>();
        List<Quest> questsToRemove = new List<Quest>();
        List<Quest> questsToInvoke = new List<Quest>();

        bool waitingForStartup = true;
        float startupTimer = 0;
        float updateTimer = 0;

        StaticNPC lastNPCClicked;

        #endregion

        #region Properties

        /// <summary>
        /// Gets count of all quests running at this time.
        /// </summary>
        public int QuestCount
        {
            get { return quests.Count; }
        }

        /// <summary>
        /// Gets Quests source folder in StreamingAssets.
        /// </summary>
        public string QuestSourceFolder
        {
            get { return Path.Combine(Application.streamingAssetsPath, questSourceFolderName); }
        }

        /// <summary>
        /// Gets Tables source folder in StreamingAssets.
        /// TODO: This folder isn't ultimately exclusive to quests. Find a more generic spot later, e.g. GameManager.
        /// </summary>
        public string TablesSourceFolder
        {
            get { return Path.Combine(Application.streamingAssetsPath, questTablesFolderName); }
        }

        /// <summary>
        /// Gets the global variables data table.
        /// </summary>
        public Table GlobalVarsTable
        {
            get { return globalVarsTable; }
        }

        /// <summary>
        /// Gets the static message names data table.
        /// </summary>
        public Table StaticMessagesTable
        {
            get { return staticMessagesTable; }
        }

        /// <summary>
        /// Gets the places data table.
        /// </summary>
        public Table PlacesTable
        {
            get { return placesTable; }
        }

        /// <summary>
        /// Gets the sounds data table.
        /// </summary>
        public Table SoundsTable
        {
            get { return soundsTable; }
        }

        /// <summary>
        /// Gets the items data table.
        /// </summary>
        public Table ItemsTable
        {
            get { return itemsTable; }
        }

        /// <summary>
        /// Gets the factions data table.
        /// </summary>
        public Table FactionsTable
        {
            get { return factionsTable; }
        }

        /// <summary>
        /// Gets the foes data table.
        /// </summary>
        public Table FoesTable
        {
            get { return foesTable; }
        }

        /// <summary>
        /// Gets or sets StaticNPC last clicked by player.
        /// </summary>
        public StaticNPC LastNPCClicked
        {
            get { return lastNPCClicked; }
            set { SetLastNPCClicked(value); }
        }

        /// <summary>
        /// Returns true if debug mode enabled.
        /// This causes original quest source line to be stored and serialized with quests.
        /// Always enabled at this stage of development.
        /// </summary>
        public bool IsDebugModeEnabled
        {
            get { return true; }
        }

        #endregion

        #region Enums

        /// <summary>
        /// Fixed quest message constants.
        /// </summary>
        public enum QuestMessages
        {
            QuestorOffer = 1000,
            RefuseQuest = 1001,
            AcceptQuest = 1002,
            QuestFail = 1003,
            QuestComplete = 1004,
            RumorsDuringQuest = 1005,
            RumorsPostFailure = 1006,
            RumorsPostSuccess = 1007,
            QuestorPostSuccess = 1008,
            QuestorPostFailure = 1009,
        }

        #endregion

        #region Unity

        void Awake()
        {
            SetupSingleton();

            globalVarsTable = new Table(Instance.GetTableSourceText(globalVarsTableFilename));
            staticMessagesTable = new Table(Instance.GetTableSourceText(staticMessagesTableFilename));
            placesTable = new Table(Instance.GetTableSourceText(placesTableFilename));
            soundsTable = new Table(Instance.GetTableSourceText(soundsTableFilename));
            itemsTable = new Table(Instance.GetTableSourceText(itemsTableFileName));
            factionsTable = new Table(Instance.GetTableSourceText(factionsTableFileName));
            foesTable = new Table(Instance.GetTableSourceText(foesTableFileName));
        }

        void Start()
        {
            RegisterActionTemplates();
        }

        private void Update()
        {
            // Handle startup delay
            if (waitingForStartup)
            {
                startupTimer += Time.deltaTime;
                if (startupTimer < startupDelay)
                    return;
                waitingForStartup = false;
            }

            // Do not tick while HUD fading
            // This is to prevent quest popups or other actions while player
            // moving between interior/exterior
            if (DaggerfallUI.Instance.FadeInProgress)
                return;

            // Increment update timer
            updateTimer += Time.deltaTime;
            if (updateTimer < (1f / ticksPerSecond))
                return;

            // Tick quest machine
            Tick();

            // Reset update timer
            updateTimer = 0;
        }

        #endregion

        #region Action Methods

        /// <summary>
        /// All actions must be registered here so they can be evaluated and factoried at runtime.
        /// If an action pattern match cannot be found that action will just be ignored by quest system.
        /// The goal is to add incremental action support over time until 100% compatibility is reached.
        /// </summary>
        void RegisterActionTemplates()
        {
            // Register example actions
            //RegisterAction(new JuggleAction(null));

            // Register trigger conditions
            RegisterAction(new WhenNpcIsAvailable(null));
            RegisterAction(new WhenReputeWith(null));
            RegisterAction(new WhenTask(null));
            RegisterAction(new ClickedNpc(null));
            RegisterAction(new ClickedItem(null));
            RegisterAction(new LevelCompleted(null));
            RegisterAction(new InjuredFoe(null));
            RegisterAction(new KilledFoe(null));
            RegisterAction(new TotingItemAndClickedNpc(null));
            RegisterAction(new DailyFrom(null));

            // Register default actions
            RegisterAction(new EndQuest(null));
            RegisterAction(new Prompt(null));
            RegisterAction(new Say(null));
            RegisterAction(new PlaySound(null));
            RegisterAction(new StartTask(null));
            RegisterAction(new ClearTask(null));
            RegisterAction(new LogMessage(null));
            RegisterAction(new PickOneOf(null));
            RegisterAction(new RemoveLogMessage(null));
            RegisterAction(new PlayVideo(null));
            RegisterAction(new PcAt(null));
            RegisterAction(new CreateNpcAt(null));
            RegisterAction(new PlaceNpc(null));
            RegisterAction(new PlaceItem(null));
            RegisterAction(new GivePc(null));
            RegisterAction(new GiveItem(null));
            RegisterAction(new StartStopTimer(null));
            RegisterAction(new CreateFoe(null));
            RegisterAction(new PlaceFoe(null));
            RegisterAction(new HideNpc(null));
            RegisterAction(new RestoreNpc(null));
            RegisterAction(new AddFace(null));
            RegisterAction(new DropFace(null));
            RegisterAction(new GetItem(null));
            RegisterAction(new StartQuest(null));
            RegisterAction(new UnsetTask(null));

            // Stubs - these actions are not complete yet
            // Just setting up so certains quests compile for now
            RegisterAction(new ChangeReputeWith(null));
            RegisterAction(new DialogLink(null));
            RegisterAction(new AddDialog(null));
            RegisterAction(new RevealLocation(null));
            RegisterAction(new ReputeExceedsDo(null));
            RegisterAction(new MuteNpc(null));
            RegisterAction(new AddAsQuestor(null));
            RegisterAction(new DropAsQuestor(null));
            RegisterAction(new RevealLocation(null));
            RegisterAction(new LegalRepute(null));

            // Raise event for custom actions to be registered
            RaiseOnRegisterCustomerActionsEvent();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Tick quest machine.
        /// </summary>
        public void Tick()
        {
            // Invoke scheduled quests
            foreach (Quest quest in questsToInvoke)
            {
                if (quest != null)
                {
                    InstantiateQuest(quest);
                    RaiseOnQuestStartedEvent(quest);
                }
            }
            questsToInvoke.Clear();

            // Update quests
            questsToTombstone.Clear();
            questsToRemove.Clear();
            foreach (Quest quest in quests.Values)
            {
                // Tick active quests
                if (!quest.QuestComplete)
                    quest.Update();

                // Schedule completed quests for tombstoning
                if (quest.QuestComplete && !quest.QuestTombstoned)
                    questsToTombstone.Add(quest);

                // Expire tombstoned quests after 1 in-game week
                if (quest.QuestTombstoned)
                {
                    if (DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - quest.QuestTombstoneTime.ToSeconds() > DaggerfallDateTime.SecondsPerWeek)
                        questsToRemove.Add(quest);
                }
            }

            // Tombstone completed quests after update
            foreach (Quest quest in questsToTombstone)
            {
                TombstoneQuest(quest);
            }

            // Remove expired quests
            foreach (Quest quest in questsToRemove)
            {
                RemoveQuest(quest);
            }

            // Fire tick event
            RaiseOnTickEvent();
        }

        /// <summary>
        /// Resets operating state - clears all quests, sitelinks, debuggers, etc.
        /// Quests will not be disposed or tombstoned they will just be dropped for garbage collector.
        /// </summary>
        public void ClearState()
        {
            // Clear state
            quests.Clear();
            siteLinks.Clear();
            questsToTombstone.Clear();
            questsToRemove.Clear();
            questsToInvoke.Clear();
            lastNPCClicked = null;

            // Clear debugger state
            DaggerfallUI.Instance.DaggerfallHUD.PlaceMarker.ClearSiteTargets();
        }

        /// <summary>
        /// Register a new action in the quest engine.
        /// </summary>
        /// <param name="actionTemplate">IQuestAction template.</param>
        public void RegisterAction(IQuestAction actionTemplate)
        {
            actionTemplates.Add(actionTemplate);
        }

        /// <summary>
        /// Attempts to load quest source text from StreamingAssets/Quests.
        /// </summary>
        /// <param name="questName">Quest filename. Extension .txt is optional.</param>
        /// <returns>Array of lines in quest text, or empty array.</returns>
        public string[] GetQuestSourceText(string questName)
        {
            string[] source = new string[0];

            // Append extension if not present
            if (!questName.EndsWith(".txt"))
                questName += ".txt";

            // Attempt to load quest source file
            string path = Path.Combine(QuestSourceFolder, questName);
            if (!File.Exists(path))
            {
                Debug.LogErrorFormat("Quest filename path {0} not found.", path);
            }
            else
            {
                source = File.ReadAllLines(path);
            }

            return source;
        }

        /// <summary>
        /// Attempts to load table text from StreamingAssets/Tables.
        /// TODO: Tables are ultimately not exclusive to quests. Relocate this later.
        /// </summary>
        /// <param name="tableName">Table filename. Extension .txt is optional.</param>
        /// <returns>Array of lines in table text, or empty array.</returns>
        public string[] GetTableSourceText(string tableName)
        {
            string[] table = new string[0];

            // Append extension if not present
            if (!tableName.EndsWith(".txt"))
                tableName += ".txt";

            // Attempt to load quest source file
            string path = Path.Combine(TablesSourceFolder, tableName);
            if (!File.Exists(path))
            {
                Debug.LogErrorFormat("Table filename path {0} not found.", path);
            }
            else
            {
                table = File.ReadAllLines(path);
            }

            return table;
        }

        /// <summary>
        /// Returns a list of all active log messages from all active quests
        /// </summary>
        /// <returns>List of log messages.</returns>
        public List<Message> GetAllQuestLogMessages()
        {
            List<Message> questMessages = new List<Message>();

            foreach (var quest in quests.Values)
            {
                Quest.LogEntry[] logEntries = quest.GetLogMessages();
                if (logEntries == null || logEntries.Length == 0)
                    continue;

                foreach (var logEntry in logEntries)
                {
                    var message = quest.GetMessage(logEntry.messageID);
                    if (message != null)
                        questMessages.Add(message);
                }
            }

            return questMessages;
        }

        /// <summary>
        /// Parses a new quest from name.
        /// Quest will attempt to load from QuestSourceFolder property path.
        /// </summary>
        /// <param name="questName">Name of quest filename. Extensions .txt is optional.</param>
        /// <returns>Quest object if successfully parsed, otherwise null.</returns>
        public Quest ParseQuest(string questName, StaticNPC questorNPC = null)
        {
            Debug.LogFormat("Parsing quest {0}", questName);

            string[] source = GetQuestSourceText(questName);
            if (source == null || source.Length == 0)
                throw new Exception(string.Format("Could not load quest '{0}' or source file is empty/invalid.", questName));

            return ParseQuest(source);
        }

        /// <summary>
        /// Instantiate a new quest from source text array.
        /// </summary>
        /// <param name="questSource">Array of lines from quuest source file.</param>
        /// <returns>Quest.</returns>
        public Quest ParseQuest(string[] questSource)
        {
            // Parse quest
            Parser parser = new Parser();
            Quest quest = parser.Parse(questSource);

            return quest;
        }

        /// <summary>
        /// Parse and instantiate a quest from quest name.
        /// </summary>
        /// <param name="questName">Quest name.</param>
        /// <returns>Quest.</returns>
        public Quest InstantiateQuest(string questName)
        {
            Quest quest = ParseQuest(questName);
            if (quest != null)
            {
                InstantiateQuest(quest);
                return quest;
            }

            return null;
        }

        /// <summary>
        /// Instantiate quest from a parsed quest object.
        /// </summary>
        /// <param name="quest">Quest.</param>
        public void InstantiateQuest(Quest quest)
        {
            quests.Add(quest.UID, quest);
            RaiseOnQuestStartedEvent(quest);
        }

        /// <summary>
        /// Schedules quest to start on next tick.
        /// </summary>
        /// <param name="quest">Quest.</param>
        public void ScheduleQuest(Quest quest)
        {
            questsToInvoke.Add(quest);
        }

        /// <summary>
        /// Find registered action template based on source line.
        /// </summary>
        /// <param name="source">Action source line.</param>
        /// <returns>IQuestAction template.</returns>
        public IQuestAction GetActionTemplate(string source)
        {
            // Brute force check every registered action for now
            // Would like a more elegant way of accomplishing this
            foreach (IQuestAction action in actionTemplates)
            {
                if (action.Test(source).Success)
                    return action;
            }

            // No pattern match found
            return null;
        }

        /// <summary>
        /// Get all Place site details for all active quests.
        /// </summary>
        /// <returns>SiteDetails[] array.</returns>
        public SiteDetails[] GetAllActiveQuestSites()
        {
            List<SiteDetails> sites = new List<SiteDetails>();

            foreach (var kvp in quests)
            {
                Quest quest = kvp.Value;
                if (!quest.QuestComplete)
                {
                    QuestResource[] foundResources = quest.GetAllResources(typeof(Place));
                    foreach (QuestResource resource in foundResources)
                    {
                        sites.Add((resource as Place).SiteDetails);
                    }
                }
            }

            return sites.ToArray();
        }

        /// <summary>
        /// Gets an active or tombstoned quest based on UID.
        /// </summary>
        /// <param name="questUID">Quest UID to retrieve.</param>
        /// <returns>Quest object. Returns null if UID not found.</returns>
        public Quest GetQuest(ulong questUID)
        {
            if (!quests.ContainsKey(questUID))
                return null;

            return quests[questUID];
        }

        /// <summary>
        /// Check if quest UID has been completed in quest machine.
        /// </summary>
        /// <param name="questUID">Quest UID to check.</param>
        /// <returns>True if quest is complete. Also returns false if quest not found.</returns>
        public bool IsQuestComplete(ulong questUID)
        {
            Quest quest = GetQuest(questUID);
            if (quest == null)
                return false;

            return quest.QuestComplete;
        }

        /// <summary>
        /// Check if quest UID has been tombstoned in quest machine.
        /// </summary>
        /// <param name="questUID">Quest UID to check.</param>
        /// <returns>True if quest is tombstoned. Also returns false if quest not found.</returns>
        public bool IsQuestTombstoned(ulong questUID)
        {
            Quest quest = GetQuest(questUID);
            if (quest == null)
                return false;

            return quest.QuestTombstoned;
        }

        /// <summary>
        /// Returns an array of all quest UIDs, even if completed or tombstoned.
        /// </summary>
        /// <returns>ulong[] array of quest UIDs.</returns>
        public ulong[] GetAllQuests()
        {
            List<ulong> keys = new List<ulong>();
            foreach (ulong key in quests.Keys)
            {
                keys.Add(key);
            }

            return keys.ToArray();
        }

        /// <summary>
        /// Returns an array of all active (not completed, not tombstoned) quest UIDs.
        /// </summary>
        /// <returns>ulong[] array of quest UIDs.</returns>
        public ulong[] GetAllActiveQuests()
        {
            List<ulong> keys = new List<ulong>();
            foreach (Quest quest in quests.Values)
            {
                if (!quest.QuestComplete && !quest.QuestTombstoned)
                    keys.Add(quest.UID);
            }

            return keys.ToArray();
        }

        /// <summary>
        /// Creates a yes/no prompt from quest message.
        /// Caller must set events and call Show() when ready.
        /// </summary>
        public DaggerfallMessageBox CreateMessagePrompt(Quest quest, int id)
        {
            Message message = quest.GetMessage(id);
            if (message != null)
                return CreateMessagePrompt(message);
            else
                return null;
        }

        /// <summary>
        /// Creates a yes/no prompt from quest message.
        /// Caller must set events and call Show() when ready.
        /// </summary>
        public DaggerfallMessageBox CreateMessagePrompt(Message message)
        {
            TextFile.Token[] tokens = message.GetTextTokens();
            DaggerfallMessageBox messageBox = new DaggerfallMessageBox(DaggerfallUI.UIManager, DaggerfallMessageBox.CommonMessageBoxButtons.YesNo, tokens);
            messageBox.ClickAnywhereToClose = false;
            messageBox.AllowCancel = false;
            messageBox.ParentPanel.BackgroundColor = Color.clear;

            return messageBox;
        }

        /// <summary>
        /// Checks if last NPC clicked is questor for any quests.
        /// This is used for quest turn-in and reward process.
        /// </summary>
        /// <returns>True if this NPC is a questor in any quest.</returns>
        public bool IsLastNPCClickedAnActiveQuestor()
        {
            foreach(Quest quest in quests.Values)
            {
                if (quest.QuestComplete)
                    continue;

                QuestResource[] questPeople = quest.GetAllResources(typeof(Person));
                foreach (Person person in questPeople)
                {
                    if (person.IsQuestor)
                    {
                        if (IsNPCDataEqual(person.QuestorData, lastNPCClicked.Data))
                        {
                            Debug.LogFormat("This person is used in quest {0} as Person {1}", person.ParentQuest.UID, person.Symbol.Original);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the last StaticNPC clicked by player.
        /// Always called by PlayerActivate when player clicks on GameObject holding StaticNPC behaviour.
        /// </summary>
        public void SetLastNPCClicked(StaticNPC npc)
        {
            // Store the NPC clicked
            lastNPCClicked = npc;

            // Find Person resource if this NPC is involved in any quests
            foreach (Quest quest in quests.Values)
            {
                QuestResource[] questPeople = quest.GetAllResources(typeof(Person));
                foreach (Person person in questPeople)
                {
                    // Set player click in Person resource
                    if (IsNPCDataEqual(person.QuestorData, lastNPCClicked.Data))
                        person.SetPlayerClicked();
                }
            }
        }

        /// <summary>
        /// Checks if two sets of StaticNPC data reference the same NPC.
        /// Notes:
        ///  * Still working through some issues here.
        ///  * Possible for Questor NPC to be moved.
        ///  * This will likely become more robust and conditional as quest system progresses.
        /// </summary>
        /// <returns>True if person1 and person2 are considered the same.</returns>
        public bool IsNPCDataEqual(StaticNPC.NPCData person1, StaticNPC.NPCData person2)
        {
            if (person1.hash == person2.hash &&
                person1.mapID == person2.mapID &&
                person1.nameSeed == person2.nameSeed &&
                person1.buildingKey == person2.buildingKey)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Immediately tombstones then removes all quests.
        /// </summary>
        /// <returns>Number of quests removed.</returns>
        public int PurgeAllQuests()
        {
            ulong[] uids = GetAllQuests();
            if (uids == null || uids.Length == 0)
                return 0;

            foreach (ulong uid in uids)
            {
                RemoveQuest(uid);
            }

            quests.Clear();
            siteLinks.Clear();
            questsToTombstone.Clear();
            questsToRemove.Clear();
            questsToInvoke.Clear();
            lastNPCClicked = null;

            // Clear debugger state
            DaggerfallUI.Instance.DaggerfallHUD.PlaceMarker.ClearSiteTargets();

            return uids.Length;
        }

        /// <summary>
        /// Tombstones a quest. It will remain in quest machine for "talk" links until removed.
        /// This calls Dispose() on quest, removes all related SiteLinks, then calls OnQuestEnded event.
        /// </summary>
        /// <param name="quest">Quest to Tombstone.</param>
        public void TombstoneQuest(Quest quest)
        {
            quest.Dispose();
            quest.TombstoneQuest();
            RemoveAllQuestSiteLinks(quest.UID);
            RaiseOnQuestEndedEvent(quest);
        }

        /// <summary>
        /// Removes a quest completely from quest machine.
        /// Tombstones quest before removal.
        /// </summary>
        /// <param name="questUID">Quest UID.</param>
        /// <returns>True if quest was removed.</returns>
        public bool RemoveQuest(ulong questUID)
        {
            return RemoveQuest(GetQuest(questUID));
        }

        /// <summary>
        /// Removes a quest completely from quest machine.
        /// Tombstones quest before removal.
        /// </summary>
        /// <param name="quest"></param>
        /// <returns>True if quest was removed.</returns>
        public bool RemoveQuest(Quest quest)
        {
            if (quest == null)
                return false;

            if (!quest.QuestTombstoned)
                TombstoneQuest(quest);

            quests.Remove(quest.UID);

            return true;
        }

        /// <summary>
        /// Gets any assigned Person resources of faction ID across all quests.
        /// </summary>
        /// <param name="factionID">FactionID to search for.</param>
        /// <returns>Person array.</returns>
        public Person[] ActiveFactionPersons(int factionID)
        {
            List<Person> assignedFound = new List<Person>();
            foreach (Quest quest in quests.Values)
            {
                QuestResource[] persons = quest.GetAllResources(typeof(Person));
                if (persons == null || persons.Length == 0)
                    continue;

                foreach(Person person in persons)
                {
                    if (person.FactionData.id == factionID)
                        assignedFound.Add(person);
                }
            }

            return assignedFound.ToArray();
        }

        #endregion

        #region Site Links

        /// <summary>
        /// Adds a site link to quest machine.
        /// There is no strong unique key to use for site links so they are stored in a flat list.
        /// Only a small number of site links will be ever active at one time in normal play.
        /// </summary>
        /// <param name="siteLink">SiteLink to add.</param>
        public void AddSiteLink(SiteLink siteLink)
        {
            siteLinks.Add(siteLink);
        }

        /// <summary>
        /// Removes all site links for a quest.
        /// Typically done when quest has completed.
        /// </summary>
        /// <param name="questUID">UID of quest to remove site links to.</param>
        public void RemoveAllQuestSiteLinks(ulong questUID)
        {
            while (RemoveQuestSiteLink(questUID)) { }
        }

        /// <summary>
        /// Selects all actives site links matching parameters.
        /// Very little information is needed to determine if player is in Town, Dungeon, or Building.
        /// This information is intended to be easily reached by scene builders at layout time.
        /// </summary>
        /// <param name="siteType">Type of sites to select.</param>
        /// <param name="mapId">MapID in world.</param>
        /// <param name="buildingKey">Building key for buidings. Not used if left at default 0.</param>
        /// <returns>SiteLink[] array of found links. Check for null or empty on return.</returns>
        public SiteLink[] GetSiteLinks(SiteTypes siteType, int mapId, int buildingKey = 0)
        {
            // Collect a copy of all site links matching params
            List<SiteLink> foundSiteLinks = new List<SiteLink>();
            foreach (SiteLink link in siteLinks)
            {
                // Match site type
                if (link.siteType == siteType && link.mapId == mapId)
                {
                    if (buildingKey != 0)
                    {
                        // Match building key if specified
                        if (buildingKey == link.buildingKey)
                            foundSiteLinks.Add(link);
                    }
                    else
                    {
                        // Otherwise just add link
                        foundSiteLinks.Add(link);
                    }
                }
            }

            return foundSiteLinks.ToArray();
        }

        /// <summary>
        /// Checks if NPC is a special individual NPC.
        /// These NPCs can exist in world even if not currently part of any active quests.
        /// </summary>
        /// <param name="factionID">Faction ID of individual NPC.</param>
        /// <returns>True if this is an individual NPC.</returns>
        public bool IsIndividualNPC(int factionID)
        {
            if (GameManager.Instance.PlayerEntity != null)
            {
                FactionFile.FactionData factionData;
                bool foundFaction = GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionID, out factionData);
                if (foundFaction && factionData.type == (int)FactionFile.FactionTypes.Individual)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Walks SiteLink > Quest > Place > QuestMarkers > Target to see if an individual NPC has been placed elsewhere.
        /// Used only to determine if an individual NPC should be disabled at home location by layout builders.
        /// Ignores non-individual NPCs.
        /// </summary>
        /// <param name="factionID">Faction ID of individual NPC.</param>
        /// <returns>True if individual has been placed elsewhere, otherwise false.</returns>
        public bool IsIndividualQuestNPCAtSiteLink(int factionID)
        {
            // Check this is a valid individual
            if (!IsIndividualNPC(factionID))
                return false;

            // Iterate site links
            foreach (SiteLink link in siteLinks)
            {
                // Attempt to get Quest target
                Quest quest = GetQuest(link.questUID);
                if (quest == null)
                    continue;

                // Attempt to get Place target
                Place place = quest.GetPlace(link.placeSymbol);
                if (place == null)
                    continue;

                // Must have target resources
                SiteDetails siteDetails = place.SiteDetails;
                QuestMarker marker = siteDetails.questSpawnMarkers[siteDetails.selectedQuestItemMarker];
                if (marker.targetResources == null)
                {
                    Debug.Log("IsIndividualQuestNPCAtSiteLink() found a SiteLink with no targetResources assigned.");
                    continue;
                }

                // Check spawn marker at this site for target NPC resource
                foreach(Symbol target in marker.targetResources)
                {
                    // Get target resource
                    QuestResource resource = quest.GetResource(target);
                    if (resource == null)
                        continue;

                    // Must be a Person resource
                    if (!(resource is Person))
                        continue;

                    // Person must be an individual and not at home
                    Person person = (Person)resource;
                    if (!person.IsIndividualNPC || person.IsIndividualAtHome)
                        continue;

                    // Check if factionID match to placed NPC
                    // This means we found an individual placed at site who is not supposed to be at their home location
                    if (person.FactionData.id == factionID)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the quest spawn marker in player's current location.
        /// </summary>
        /// <param name="markerType">Get quest spawn or item marker.</param>
        /// <param name="questMarkerOut">QuestMarker out.</param>
        /// <param name="buildingOriginOut">Building origin in scene, or Vector3.zero if not inside a building.</param>
        /// <returns>True if successful.</returns>
        public bool GetCurrentLocationQuestMarker(MarkerTypes markerType, out QuestMarker questMarkerOut, out Vector3 buildingOriginOut)
        {
            questMarkerOut = new QuestMarker();
            buildingOriginOut = Vector3.zero;

            // Get PlayerEnterExit for world context
            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            if (!playerEnterExit)
                return false;

            // Get SiteLinks for player's current location
            SiteLink[] siteLinks = null;
            if (playerEnterExit.IsPlayerInsideBuilding)
            {
                StaticDoor[] exteriorDoors = playerEnterExit.ExteriorDoors;
                if (exteriorDoors == null || exteriorDoors.Length < 1)
                    return false;

                siteLinks = GetSiteLinks(SiteTypes.Building, GameManager.Instance.PlayerGPS.CurrentMapID, exteriorDoors[0].buildingKey);
                if (siteLinks == null || siteLinks.Length == 0)
                    return false;

                Vector3 buildingPosition = exteriorDoors[0].buildingMatrix.GetColumn(3);
                buildingOriginOut = exteriorDoors[0].ownerPosition + buildingPosition;
            }
            else if (playerEnterExit.IsPlayerInsideDungeon)
            {
                siteLinks = GetSiteLinks(SiteTypes.Dungeon, GameManager.Instance.PlayerGPS.CurrentMapID);
            }
            else
            {
                return false;
            }

            // Exit if no links found
            if (siteLinks == null || siteLinks.Length == 0)
                return false;

            // Walk through all found SiteLinks
            foreach (SiteLink link in siteLinks)
            {
                // Get the Quest object referenced by this link
                Quest quest = GetQuest(link.questUID);
                if (quest == null)
                    return false;

                // Get the Place resource referenced by this link
                Place place = quest.GetPlace(link.placeSymbol);
                if (place == null)
                    return false;

                // Get spawn marker
                QuestMarker spawnMarker = place.SiteDetails.questSpawnMarkers[place.SiteDetails.selectedQuestSpawnMarker];
                if (markerType == MarkerTypes.QuestSpawn && spawnMarker.targetResources != null)
                {
                    questMarkerOut = spawnMarker;
                    return true;
                }

                // Get item marker
                QuestMarker itemMarker = place.SiteDetails.questItemMarkers[place.SiteDetails.selectedQuestItemMarker];
                if (markerType == MarkerTypes.QuestItem && itemMarker.targetResources != null)
                {
                    questMarkerOut = itemMarker;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Removes the first SiteLink found matching quest UID.
        /// </summary>
        /// <returns>True if link removed, false if no matching links found.</returns>
        bool RemoveQuestSiteLink(ulong questUID)
        {
            // Look for a site link matching this questID to remove
            for (int i = 0; i < siteLinks.Count; i++)
            {
                if (siteLinks[i].questUID == questUID)
                {
                    siteLinks.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Static Helper Methods

        /// <summary>
        /// Checks if a Place has a SiteLink available.
        /// </summary>
        public static bool HasSiteLink(Quest parentQuest, Symbol placeSymbol)
        {
            // Attempt to get Place resource
            Place place = parentQuest.GetPlace(placeSymbol);
            if (place == null)
                throw new Exception(string.Format("HasSiteLink() could not find Place symbol {0}", placeSymbol.Name));

            // Collect any SiteLinks associdated with this site
            SiteLink[] siteLinks = Instance.GetSiteLinks(place.SiteDetails.siteType, place.SiteDetails.mapId, place.SiteDetails.buildingKey);
            if (siteLinks == null || siteLinks.Length == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Creates a new SiteLink at Place.
        /// </summary>
        public static void CreateSiteLink(Quest parentQuest, Symbol placeSymbol)
        {
            // Attempt to get Place resource
            Place place = parentQuest.GetPlace(placeSymbol);
            if (place == null)
                throw new Exception(string.Format("Attempted to add SiteLink for invalid Place symbol {0}", placeSymbol.Name));

            // Create SiteLink in QuestMachine
            SiteLink siteLink = new SiteLink();
            siteLink.questUID = parentQuest.UID;
            siteLink.placeSymbol = placeSymbol.Clone();
            siteLink.siteType = place.SiteDetails.siteType;
            siteLink.mapId = place.SiteDetails.mapId;
            siteLink.buildingKey = place.SiteDetails.buildingKey;
            Instance.AddSiteLink(siteLink);

            // Output debug information
            switch (siteLink.siteType)
            {
                case SiteTypes.Building:
                    Debug.LogFormat("Created Building SiteLink to {0} in {1}/{2}", place.SiteDetails.buildingName, place.SiteDetails.regionName, place.SiteDetails.locationName);
                    break;
                case SiteTypes.Dungeon:
                    Debug.LogFormat("Created Dungeon SiteLink to {0}/{1}", place.SiteDetails.regionName, place.SiteDetails.locationName);
                    break;
            }
        }

        #endregion

        #region Singleton

        static QuestMachine instance = null;
        public static QuestMachine Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!FindQuestMachine(out instance))
                    {
                        GameObject go = new GameObject();
                        go.name = "QuestMachine";
                        instance = go.AddComponent<QuestMachine>();
                    }
                }
                return instance;
            }
        }

        public static bool HasInstance
        {
            get
            {
                return (instance != null);
            }
        }

        public static bool FindQuestMachine(out QuestMachine questMachineOut)
        {
            questMachineOut = GameObject.FindObjectOfType<QuestMachine>();
            if (questMachineOut == null)
            {
                DaggerfallUnity.LogMessage("Could not locate QuestMachine GameObject instance in scene!", true);
                return false;
            }

            return true;
        }

        private void SetupSingleton()
        {
            if (instance == null)
                instance = this;
            else if (instance != this)
            {
                if (Application.isPlaying)
                {
                    DaggerfallUnity.LogMessage("Multiple QuestMachine instances detected in scene!", true);
                    Destroy(gameObject);
                }
            }
        }

        #endregion

        #region Serialization

        [fsObject("v1")]
        public class QuestMachineData_v1
        {
            public SiteLink[] siteLinks;
            public Quest.QuestSaveData_v1[] quests;
        }

        public QuestMachineData_v1 GetSaveData()
        {
            QuestMachineData_v1 data = new QuestMachineData_v1();

            // Save SiteLinks
            data.siteLinks = siteLinks.ToArray();

            // Save Questss
            List<Quest.QuestSaveData_v1> questSaveDataList = new List<Quest.QuestSaveData_v1>();
            foreach(Quest quest in quests.Values)
            {
                questSaveDataList.Add(quest.GetSaveData());
            }
            data.quests = questSaveDataList.ToArray();

            return data;
        }

        public void RestoreSaveData(QuestMachineData_v1 data)
        {
            // Restore SiteLinks
            siteLinks = new List<SiteLink>(data.siteLinks);

            // Restore Quests
            foreach(Quest.QuestSaveData_v1 questData in data.quests)
            {
                Quest quest = new Quest();
                quest.RestoreSaveData(questData);
                quests.Add(quest.UID, quest);
            }
        }

        #endregion

        #region Events

        public delegate void OnRegisterCustomActionsEventHandler();
        public static event OnRegisterCustomActionsEventHandler OnRegisterCustomActions;
        protected virtual void RaiseOnRegisterCustomerActionsEvent()
        {
            if (OnRegisterCustomActions != null)
                OnRegisterCustomActions();
        }

        // OnTick
        public delegate void OnTickEventHandler();
        public static event OnTickEventHandler OnTick;
        protected virtual void RaiseOnTickEvent()
        {
            if (OnTick != null)
                OnTick();
        }

        // OnQuestStarted
        public delegate void OnQuestStartedEventHandler(Quest quest);
        public static event OnQuestStartedEventHandler OnQuestStarted;
        protected virtual void RaiseOnQuestStartedEvent(Quest quest)
        {
            if (OnQuestStarted != null)
                OnQuestStarted(quest);
        }

        // OnQuestEnded
        public delegate void OnQuestEndedEventHandler(Quest quest);
        public static event OnQuestEndedEventHandler OnQuestEnded;
        protected virtual void RaiseOnQuestEndedEvent(Quest quest)
        {
            if (OnQuestEnded != null)
                OnQuestEnded(quest);
        }

        #endregion
    }
}