//Requires: Kits
//Requires: Spawns
//Requires: ZoneManager
using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Network;
using Facepunch;
using JetBrains.Annotations;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;
using Network.Visibility;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using UnityEngine.Android;

namespace Oxide.Plugins
{
    using EventManagerEx;
    using Facepunch.Extend;

    [Info("EventManager", "k1lly0u", "4.0.5")]
    [Description("The core mechanics for arena combat games")]
    public class EventManager : RustPlugin
    {
        #region Fields        
        private DynamicConfigFile restorationData, eventData,staticobjectsData;

        [PluginReference]
        private Plugin Economics, Kits, NoEscape, ServerRewards, Spawns;
        [PluginReference]
        internal Plugin ZoneManager,MurderPickableSpawn, LobbyRoomSystem; 


        private Timer _autoEventTimer;

        private RewardType rewardType;

        private int scrapItemId;

        private static Regex hexFilter;

        public Hash<string, IEventPlugin> EventModes { get; set; } = new Hash<string, IEventPlugin>();

        public EventData Events { get; private set; }
        public static Dictionary<string, List<StaticObject>> StaticObjects = new Dictionary<string, List<StaticObject>>();

        public static EventManager Instance { get; private set; }

        //public static BaseEventGame BaseManager { get; internal set; }
        public static Dictionary<int, BaseEventGame> BaseManager= new Dictionary<int, BaseEventGame>();
        public static Dictionary<int, AdminEditStaticsRoom> AdminEditingRooms = new Dictionary<int, AdminEditStaticsRoom>();

        private static List<Collider> AllEventColliders = Pool.GetList<Collider>();

        public static ConfigData Configuration { get; set; }

        public static EventResults LastEventResult { get; private set; }

        public static bool IsUnloading { get; private set; }


        internal const string ADMIN_PERMISSION = "eventmanager.admin";
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            restorationData = Interface.Oxide.DataFileSystem.GetFile("EventManager/restoration_data");

            eventData = Interface.Oxide.DataFileSystem.GetFile("EventManager/event_data");

            staticobjectsData = Interface.Oxide.DataFileSystem.GetFile("EventManager/arenastatics_data");
            permission.RegisterPermission(ADMIN_PERMISSION, this);

            Instance = this;
            IsUnloading = false;
            LastEventResult = new EventResults();

            SubscribeAll();
            LoadData();
            LoadStaticObjectsData();
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void OnServerInitialized()
        {
            if (!CheckDependencies())
                return;            

            rewardType = ParseType<RewardType>(Configuration.Reward.Type);

            scrapItemId = ItemManager.FindItemDefinition("scrap")?.itemid ?? 0;

            hexFilter = new Regex("^([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$");


            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
            if (string.IsNullOrEmpty(Configuration.lobbyspawnfilename))
                PrintError("Lobby spawnfile name is empty");
            else if (ValidateSpawnFile(Configuration.lobbyspawnfilename) != null)
                PrintError("Lobby spawnfile name is corrupted");
            
            //NextFrame(() => DeleteEntitiesInArenas());
        }
        private void Unload()
        {
            IsUnloading = true;
            
            
            //if (BaseManager != null)
            //    UnityEngine.Object.DestroyImmediate(BaseManager.gameObject);

            //Server kapanırken açık odaların kapatılmasını düzenledik.
            foreach (BaseEventGame room in BaseManager.Values.ToList()) { UnityEngine.Object.DestroyImmediate(room); }

            BaseEventPlayer[] eventPlayers = UnityEngine.Object.FindObjectsOfType<BaseEventPlayer>();
            foreach (BaseEventPlayer eventplayer in eventPlayers)
            {
                UnityEngine.Object.DestroyImmediate(eventplayer);
            }

            foreach (AdminEditStaticsRoom adminRoom in AdminEditingRooms.Values)
            {
                adminRoom.Destroy();
            }
            hexFilter = null;

            LastEventResult = null;
            BaseManager = null;
            Configuration = null;
            Instance = null;
            AdminEditingRooms = null;
            Economics = Kits = NoEscape = ServerRewards = Spawns = null;
            ZoneManager = MurderPickableSpawn = LobbyRoomSystem = null;
            restorationData = eventData = staticobjectsData = null;
            UnsubscribeAll();
        }


        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(1f, () => OnPlayerConnected(player));
                return;
            }
            UnlockInventory(player);
            //Waking up without respawn screen
            NextFrame(() =>
            {
                player.LifeStoryEnd();
                player.Respawn();
                player.EndSleeping();
            });
        }
       
        private void OnPlayerDisconnected(BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(player);
                if (room != null)
                    room.LeaveEvent(player);
                else UnityEngine.Object.DestroyImmediate(eventPlayer);
            }

            // Destroying player
            // if (!player.IsDestroyed || player.IsAlive())
            //     NextFrame(() =>
            //     {
            //         StripInventory(player);
            //         player.lifestate = BaseCombatEntity.LifeState.Dead;
            //         player.Kill();
            //     });
        }
        void OnEntityKill(BaseEntity entity)
        {
            foreach (AdminEditStaticsRoom adminRoom in AdminEditingRooms.Values)
            {
                if(adminRoom.canEntitiesBeRemoved)
                    continue;
                
                foreach (BaseEntity staticentity in adminRoom.staticobjects)
                {
                    if (staticentity == entity)
                    {
                        BaseEntity newentity = GameManager.server.CreateEntity(entity.PrefabName, entity.ServerPosition, entity.ServerRotation);
                        adminRoom.staticobjects.Remove(entity);
                        newentity.Spawn();
                        
                        if (newentity is IOEntity)
                        {
                            IOEntity newioEntity = newentity as IOEntity;
                            IOEntity oldioEntity = entity as IOEntity;

                            for (int i = 0; i < oldioEntity.inputs.Length; i++)
                            {
                                if (!oldioEntity.inputs[i].connectedTo.entityRef.IsValid(true))
                                    continue;
                                
                                IOEntity connectedToEntity = oldioEntity.inputs[i].connectedTo.Get();

                                newioEntity.inputs[i].connectedTo.Set(connectedToEntity);
                                newioEntity.inputs[i].connectedToSlot = oldioEntity.inputs[i].connectedToSlot;
                                newioEntity.inputs[i].wireColour = oldioEntity.inputs[i].wireColour;
                                newioEntity.inputs[i].connectedTo.Init();

                                connectedToEntity.outputs[oldioEntity.inputs[i].connectedToSlot].connectedTo
                                    .Set(newioEntity);
                                connectedToEntity.outputs[oldioEntity.inputs[i].connectedToSlot].connectedToSlot = i;
                                connectedToEntity.outputs[oldioEntity.inputs[i].connectedToSlot].wireColour = oldioEntity.inputs[i].wireColour;
                                    connectedToEntity.outputs[oldioEntity.inputs[i].connectedToSlot].connectedTo.Init();
                                connectedToEntity.MarkDirtyForceUpdateOutputs();
                                connectedToEntity.SendNetworkUpdate();
                                connectedToEntity.SendChangedToRoot(true);
                                connectedToEntity.SendNetworkUpdate();
                            }

                            for (int i = 0; i < oldioEntity.outputs.Length; i++)
                            {
                                if (!oldioEntity.outputs[i].connectedTo.entityRef.IsValid(true))
                                    continue;
                                IOEntity connectedToEntity = oldioEntity.outputs[i].connectedTo.Get();
                                
                                newioEntity.outputs[i].connectedTo.Set(oldioEntity.outputs[i].connectedTo.Get());
                                newioEntity.outputs[i].connectedToSlot = oldioEntity.outputs[i].connectedToSlot;
                                newioEntity.outputs[i].linePoints = oldioEntity.outputs[i].linePoints;
                                newioEntity.outputs[i].wireColour = oldioEntity.outputs[i].wireColour;
                                newioEntity.outputs[i].connectedTo.Init();
                                
                                connectedToEntity.inputs[oldioEntity.outputs[i].connectedToSlot].connectedTo
                                    .Set(newioEntity);
                                connectedToEntity.inputs[oldioEntity.outputs[i].connectedToSlot].connectedToSlot = i;
                                connectedToEntity.inputs[oldioEntity.outputs[i].connectedToSlot].wireColour = oldioEntity.outputs[i].wireColour;
                                connectedToEntity.inputs[oldioEntity.outputs[i].connectedToSlot].connectedTo.Init();
                                connectedToEntity.MarkDirtyForceUpdateOutputs();
                                connectedToEntity.SendNetworkUpdate();
                                connectedToEntity.SendChangedToRoot(true);
                                connectedToEntity.SendNetworkUpdate();
                            }
                            newioEntity.MarkDirtyForceUpdateOutputs();
                            newioEntity.SendNetworkUpdate();
                            newioEntity.SendChangedToRoot(true);
                        }
                        adminRoom.AddStaticEntityToRoom(newentity);
                        return;
                    }
                }
            }
        }
        private void OnEntityTakeDamage(BaseEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null)
                return;

            BasePlayer player = entity.ToPlayer(); 

            if (player != null)
            {
                BaseEventPlayer eventPlayer = GetUser(player);
                if (eventPlayer != null)
                {
                    BaseEventGame game = GetRoomofPlayer(player);
                    if (game == null)
                        return;
                    
                    if(game.Status == EventStatus.Started)
                        game.OnPlayerTakeDamage(eventPlayer, hitInfo);
                    else
                        ClearDamage(hitInfo);
                }
                else if (player.HasComponent<RoomSpectatingBehaviour>())
                {
                    ClearDamage(hitInfo);
                }
            }
            else
            {
                BaseEventPlayer attacker = GetUser(hitInfo.InitiatorPlayer);
                if (attacker != null)
                {
                    BaseEventGame game = GetRoomofPlayer(hitInfo.InitiatorPlayer);
                    if (game != null)
                    {
                        if (game.CanDealEntityDamage(attacker, entity, hitInfo))
                            return;
                    }
                    ClearDamage(hitInfo);
                }
            }
        }
        //Used this hook for killing hurt player directly without becoming wounded
        private object CanBeWounded(BasePlayer player, HitInfo hitInfo)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null && GetRoomofPlayer(player) != null)
                return false;
            return null;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (player != null)
            {
                BaseEventPlayer eventPlayer = GetUser(player);
                BaseEventGame game= GetRoomofPlayer(player);
                if (eventPlayer != null && game != null)
                { 
                    if (!eventPlayer.IsDead)
                        game.PrePlayerDeath(eventPlayer, hitInfo);
                    return false;                    
                }
            }
            return null;
        }

        //Eğer oyuncu izliyorsa sol tık attığında izlediği hedef değişsin.
        void OnPlayerInput(BasePlayer player, InputState input)
        {
            RoomSpectatingBehaviour spectatingBehaviour;
            player.TryGetComponent<RoomSpectatingBehaviour>(out spectatingBehaviour);
            if (player == null || spectatingBehaviour == null) return;
            if (input.IsDown(BUTTON.FIRE_PRIMARY) && player.IsSpectating()) { spectatingBehaviour.UpdateSpectateTarget(); }
        }
        private object CanSpectateTarget(BasePlayer player, string name)
        {
            RoomSpectatingBehaviour spectatingBehaviour;
            player.TryGetComponent<RoomSpectatingBehaviour>(out spectatingBehaviour);
            if (spectatingBehaviour == null)
                return null;
            if (player.IsSpectating())
            {
                spectatingBehaviour.UpdateSpectateTarget();
                return false;
            }
            return null;
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            BasePlayer player = planner?.GetOwnerPlayer();
            if (player == null)
                return;

            BaseCombatEntity baseCombatEntity = gameObject?.ToBaseEntity() as BaseCombatEntity;
            if (baseCombatEntity == null)
                return;

            
            BaseEventPlayer eventPlayer = GetUser(player);
            BaseEventGame game = GetRoomofPlayer(player);
            if (eventPlayer != null && game != null)
                game.OnEntityDeployed(baseCombatEntity);
        }

        private void OnItemDeployed(Deployer deployer, BaseCombatEntity baseCombatEntity)
        {
            BasePlayer player = deployer.GetOwnerPlayer();
            if (player == null)
                return;

            BaseEventPlayer eventPlayer = GetUser(player);
            BaseEventGame game = GetRoomofPlayer(player);
            if (eventPlayer != null && game != null)
                game.OnEntityDeployed(baseCombatEntity);
        }

        private object OnCreateWorldProjectile(HitInfo hitInfo, Item item)
        {
            if (hitInfo == null)
                return null;

            if (hitInfo.InitiatorPlayer != null)
            {
                BaseEventPlayer eventPlayer = GetUser(hitInfo.InitiatorPlayer);
                if (eventPlayer != null)
                    return false;
            }

            if (hitInfo.HitEntity?.ToPlayer() != null)
            {
                BaseEventPlayer eventPlayer = GetUser(hitInfo.HitEntity.ToPlayer());
                if (eventPlayer != null)
                    return false;
            }

            return null;
        }

        private object CanDropActiveItem(BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)                           
                return false;            
            return null;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            BaseEventPlayer eventPlayer = GetUser(player);

            if (player == null || player.IsAdmin || eventPlayer == null)
                return null;

            if (Configuration.Event.CommandBlacklist.Any(x => x.StartsWith("/") ? x.Substring(1).ToLower() == command : x.ToLower() == command))
            {
                SendReply(player, Message("Error.CommandBlacklisted", player.userID));
                return false;
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            BaseEventPlayer eventPlayer = GetUser(player);

            if (player == null || player.IsAdmin || eventPlayer == null || arg.Args == null)
                return null;

            if (Configuration.Event.CommandBlacklist.Any(x => arg.cmd.FullName.Equals(x, StringComparison.OrdinalIgnoreCase)))
            {
                SendReply(player, Message("Error.CommandBlacklisted", player.userID));
                return false;
            }
            return null;
        }
        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(player);
                if (room != null) return room.OnItemAction(item, action, player);
            }
            return null;
        }
        private object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            BaseEventPlayer eventPlayer = GetUser(item.GetOwnerPlayer());
            if (eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(item.GetOwnerPlayer());
                if (room != null) return room.CanMoveItem(item, playerLoot,targetContainer, targetSlot,amount);
            }
            return null;
        }
        object OnItemPickup(Item item, BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if(eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(player);
                if (room != null) return room.OnItemPickup(item, player);
            } 
            return null;
        }
        object OnGroupUpdate(uint networkableID)
        {
            BaseEntity entity = BaseNetworkable.serverEntities.Find(networkableID) as BaseEntity;
            if (entity == null)
                return null;

            if (!entity.HasComponent<NetworkGroupData>())
                return null;

            return entity.GetComponent<NetworkGroupData>().groupID;
            /*
            foreach(BaseEventGame room in BaseManager.Values)
            {
                uint groupID = Convert.ToUInt32(room?.gameroom?.roomID);
                BasePlayer filteredplayer = room?.eventPlayers?.FirstOrDefault( x => x.Player.net.ID == networkableID )?.Player;
                if (filteredplayer != default(BasePlayer))
                    return groupID;

                BasePlayer filteredplayer2 = room?.spectators?.FirstOrDefault(x => x.net.ID == networkableID);
                if (filteredplayer2 != default(BasePlayer))
                    return groupID;
                
                BaseEntity _object = room?.roomobjects?.FirstOrDefault(x => x?.net?.ID == networkableID);
                if (_object != default(BaseEntity))
                    if (!_object.IsDestroyed)
                        return groupID;
            }
            return null;
            */
        }
        void OnEntitySpawned(BaseEntity entity)
        {
            BasePlayer owner = BasePlayer.FindByID(entity.OwnerID);
            if (owner == null)
                return;

            BaseEventGame room = GetRoomofPlayer(owner);
            if (room != null)
            {
                BaseEventPlayer baseeventplayer;
                if (owner.TryGetComponent(out baseeventplayer))
                    baseeventplayer.Event.AddItemToRoom(entity);
                return;
            }
            
            AdminEditStaticsRoom adminRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(owner);
            if (adminRoom != null)
            {
                adminRoom.AddStaticEntityToRoom(entity);
                adminRoom.SendNotification($"Entity: {entity.ShortPrefabName} added into {adminRoom.eventConfig.EventName}\n" +
                                           $"Total entity number: {adminRoom.staticobjects.Count}");
            }
        }
        void OnItemDropped(Item item, BaseEntity entity)
        {
            BasePlayer owner = item.GetOwnerPlayer();
            if (owner == null)
                return;
            
            BaseEventPlayer baseeventplayer;
            if (owner.TryGetComponent(out baseeventplayer))
                baseeventplayer.Event.AddItemToRoom(entity);
        }
        object CanLootEntity(BasePlayer player, LootableCorpse  corpse)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if(eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(player);
                if (room != null) return room.CanLootEntity(player,corpse);
            } 
            return null;
        }
        //Custom hook to override recovery from wounded state
        object OnWoundCheck(BasePlayer player)
        {
            BaseEventGame room = GetRoomofPlayer(player);
            if (room != null)
            {
                return room.OnWoundCheck(player);
            }
            return null;
        }

        void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if(eventPlayer != null)
            {
                BaseEventGame room = GetRoomofPlayer(player);
                if (room != null)
                    room.OnWeaponFired(projectile,player);
            } 
        }

        private void OnMeleeThrown(BasePlayer player, Item item)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if(eventPlayer != null)
            {
                eventPlayer.Event.OnMeleeThrown(player,item);
            } 
        }
        void OnLoseCondition(Item item, ref float amount)
        {
            BasePlayer player =item.GetOwnerPlayer();
            if (player == null)
                return;
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)
            {
                eventPlayer.Event.OnLoseCondition(item,ref amount);
            }
        }
        #endregion

        #region Event Construction
        public static void RegisterEvent(string eventName, IEventPlugin plugin) => Instance.EventModes[eventName] = plugin;

        public static void UnregisterEvent(string eventName) => Instance.EventModes.Remove(eventName);
        /// <summary>
        /// Creates a new room with significant roomID
        /// </summary>
        public object OpenEvent(string eventName,GameRoom room)
        {
            EventConfig eventConfig;

            if (Events.events.TryGetValue(eventName, out eventConfig))
            {
                IEventPlugin plugin;
                if (!EventModes.TryGetValue(eventConfig.EventType, out plugin))                
                    return $"Unable to find event plugin for game mode: {eventConfig.EventType}";
                
                if (plugin == null)                
                    return $"Unable to initialize event plugin: {eventConfig.EventType}. Plugin is either unloaded or the class does not derive from IEventGame";

                object success = ValidateEventConfig(eventConfig);
                if (success is string)
                    return $"Failed to open event : {(string)success}";

                if (!plugin.InitializeEvent(eventConfig,room))
                    return $"There was a error initializing the event : {eventConfig.EventType}";

                _autoEventTimer?.Destroy();

                return null;
            }
            else return "Failed to find a event with the specified name";
        }
        /// <summary>
        /// Closes the room (for UI management)
        /// </summary>
        public static bool EndEvent(BasePlayer player,int roomID)
        {
            BaseEventGame room = GetRoombyroomID(roomID);
            if(player.userID != room.gameroom.ownerID || room==null) return false; // sadece odayı kuran kişi kaldırabilsin & oda yoksa işlem yapmasın.

            room.EndEvent();
            
            return true;
        }
        public void JoinGame(BasePlayer player,int roomID) => BaseManager[roomID].JoinEvent(player);
        public void StartGame(int roomID) => BaseManager[roomID].PrestartEvent();
            
        public IList<BaseEventGame> GetAvailableRooms()
            {
                return BaseManager.Values.ToList();
            }
        private static BaseEventGame GetRoombyroomID(int ID) { return BaseManager[ID]; }

        public static bool InitializeEvent<T>(IEventPlugin plugin, EventConfig config,GameRoom _room) where T : BaseEventGame
        {
            foreach(int existingroomID in BaseManager.Keys) 
            {
                if (_room.roomID != existingroomID) { continue; } //Burayı duruma göre düzenlersin.
                return false;
            }
            int id=Instance.RandomRoomID();
            T obj = new GameObject(config.EventName).AddComponent<T>();
            obj.gameroom = _room;
            obj.gameroom.roomID=id;

            BaseManager.Add(id,obj);
            BaseManager[id].InitializeEvent(plugin, config);
            return true;
        }
        private int RandomRoomID()
        {
            int i = 0;
            while (i == 0)
            {
                System.Random random = new System.Random();
                int id = random.Next(0, 999999);
                if (BaseManager.ContainsKey(id) || AdminEditingRooms.ContainsKey(id))
                    return RandomRoomID();
                return id;
            }
            return -1;
        }
        #endregion

        #region Functions
        public IEventPlugin GetPlugin(string name)
        {
            IEventPlugin eventPlugin;
            if (EventModes.TryGetValue(name, out eventPlugin))
                return eventPlugin;

            return null;
        }

        private bool CheckDependencies()
        {
            if (!Spawns)
            {
                PrintError("Unable to load EventManager - Spawns database not found. Please download Spawns database to continue");
                rust.RunServerCommand("oxide.unload", "EventManager");
                return false;
            }

            if (!ZoneManager)            
                PrintError("ZoneManager is not installed! Unable to restrict event players to zones");
               
            if (!Kits)
                PrintError("Kits is not installed! Unable to issue any weapon kits");

            return true;
        }

        private void UnsubscribeAll()
        {
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(CanBeWounded));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnItemDeployed));
            Unsubscribe(nameof(OnCreateWorldProjectile));
            Unsubscribe(nameof(CanDropActiveItem));
            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(OnServerCommand));
            Unsubscribe(nameof(OnItemAction));
            Unsubscribe(nameof(CanMoveItem));
            Unsubscribe(nameof(OnItemPickup));
            Unsubscribe(nameof(OnItemDropped));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnGroupUpdate));
        }

        private void SubscribeAll()
        {
            Subscribe(nameof(OnEntityTakeDamage));
            Subscribe(nameof(CanBeWounded));
            Subscribe(nameof(OnPlayerDeath));
            Subscribe(nameof(OnEntityBuilt));
            Subscribe(nameof(OnItemDeployed));
            Subscribe(nameof(OnCreateWorldProjectile));
            Subscribe(nameof(CanDropActiveItem));
            Subscribe(nameof(OnPlayerCommand));
            Subscribe(nameof(OnServerCommand));
            Subscribe(nameof(CanMoveItem));
            Subscribe(nameof(OnItemAction));
            Subscribe(nameof(OnItemPickup));
            Subscribe(nameof(OnItemDropped));
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnGroupUpdate));
        }

        private static void Broadcast(string key, params object[] args)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("chat.add", 0, Configuration.Message.ChatIcon, string.Format(Configuration.announcerlabel + Message(key, player.userID), args));
            }
        }

        internal static bool IsValidHex(string s) => hexFilter.IsMatch(s);

        /// <summary>
        /// Deletes old entities of arenas and ready for minigames
        /// </summary>
        private void DeleteEntitiesInArenas()
        {
            object zones = ZoneManager.Call("GetZoneIDs");
            if (!(zones is string[]))
            {
                PrintError("Error occured 'DeleteEntities in arena'");
                return;
            }
            string[] zoneIDs = zones as string[];

            foreach (string zoneID in zoneIDs)
            {
                List<BaseEntity> entities = ZoneManager.Call("GetEntitiesInZone", zoneID) as List<BaseEntity>;
                if (entities == null)
                    return;
                foreach (BaseEntity entity in entities)
                {
                    entity.Kill();
                }
            }
        }
        
        #endregion

        #region Classes and Components  
        public class GameRoom
        {
            public string ownerName;
            public ulong ownerID;
            public int roomID=-1;
            public string place;
            public int minPlayer;
            public int maxPlayer;
            public int neededPlayerCount;
            public bool isSpectatable = false;
            internal string password;
            public bool isCreatedbySystem;

            internal bool hasPassword = false;

            public GameRoom (string _place,string _ownerName,int _minPlayer,int _maxPlayer,int _neededPlayerCount,ulong _ownerID,bool _isCreatedbySystem =false)
            {
                this.place = _place;
                this.ownerName = _ownerName;
                this.minPlayer=_minPlayer;
                this.maxPlayer=_maxPlayer;
                this.neededPlayerCount=_neededPlayerCount;
                this.ownerID=_ownerID;
                this.isCreatedbySystem = _isCreatedbySystem;
            }
        }
        internal static BaseEventGame GetRoomofPlayer(BasePlayer player)
        {
            if (player == null)
                return null;
            CurrentEventInfo currentEventInfo;
            if (player.TryGetComponent(out currentEventInfo))
                return currentEventInfo.eventgame;
            return null;
        }
        public class BaseEventGame : MonoBehaviour
        {
            internal IEventPlugin Plugin { get; private set; }

            internal EventConfig Config { get; private set; }

            public EventStatus Status { get; protected set; }

            protected GameTimer Timer;

            internal SpawnSelector _spawnSelectorA;

            internal SpawnSelector _spawnSelectorB;

            protected CuiElementContainer scoreContainer = null;

            internal List<BasePlayer> spectators = Pool.GetList<BasePlayer>();

            internal List<BaseEventPlayer> eventPlayers = Pool.GetList<BaseEventPlayer>();

            internal List<ScoreEntry> scoreData = Pool.GetList<ScoreEntry>();

            private List<BaseCombatEntity> _deployedObjects = Pool.GetList<BaseCombatEntity>();

            internal List<BaseEntity> roomobjects = Pool.GetList<BaseEntity>();
            internal List<BaseEntity> staticobjects = Pool.GetList<BaseEntity>();

            private bool _isClosed = false;
            private bool isstaticsSpawned = false;

            private double _startsAtTime;

            public List<Collider> roomcolliders = new List<Collider>();

            internal GameRoom gameroom;

            internal string TeamAColor { get; set; }

            internal string TeamBColor { get; set; }

            internal string TeamAClothing { get; set; }

            internal string TeamBClothing { get; set; }

            public bool GodmodeEnabled { get; protected set; } = true;

            internal string EventInformation
            {
                get
                {
                    string str = string.Format(Message("Info.Event.Current"), Config.EventName, Config.EventType);
                    str += string.Format(Message("Info.Event.Player"), eventPlayers.Count, Config.MaximumPlayers);
                    return str;
                }
            }

            internal string EventStatus => string.Format(Message("Info.Event.Status"), Status);
            #region GameRoom
            public string GetGamePlace() { return gameroom.place; }
            #endregion

            #region Initialization and Destruction 
            /// <summary>
            /// Called when the event GameObject is created
            /// </summary>
            void Awake()
            {
            }
            /// <summary>
            /// Called when the event GameObject is destroyed
            /// </summary>
            void OnDestroy()
            {
                CleanupEntities();
                CleanupStaticEntities();

                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    if (eventPlayer.IsDead)
                        ResetPlayer(eventPlayer);

                    LeaveEvent(eventPlayer);
                }
                foreach (BasePlayer player in spectators)
                {
                    ResetPlayer(player);
                    LeaveEvent(player);
                }

                Pool.FreeList(ref scoreData);
                Pool.FreeList(ref spectators);
                Pool.FreeList(ref eventPlayers);

                _spawnSelectorA?.Destroy();
                _spawnSelectorB?.Destroy();

                Timer?.StopTimer();

                //Deleting network group
                //Net.sv.visibility.TryGet(Convert.ToUInt32(gameroom.roomID))?.Dispose();
                //Net.sv.visibility.groups.Remove(Convert.ToUInt32(gameroom.roomID));

                if (BaseManager.Keys.Contains(gameroom.roomID))
                    BaseManager.Remove(gameroom.roomID);
                
                Instance?.LobbyRoomSystem?.CallHook("RefreshLobbyUI");
                //Trying to destroy multiple times causing an error.
                Destroy(gameObject);
            }

            /// <summary>
            /// The first function called when an event is being opened
            /// </summary>
            /// <param name="plugin">The plugin the event game belongs to</param>
            /// <param name="config">The event config</param>
            internal virtual void InitializeEvent(IEventPlugin plugin, EventConfig config)
            {
                this.Plugin = plugin;
                this.Config = config;

                _spawnSelectorA = new SpawnSelector(config.EventName, config.TeamConfigA.Spawnfile);

                if (plugin.IsTeamEvent)
                {
                    TeamAColor = config.TeamConfigA.Color;// Bu renkleri daha sonra nerede kullanıyor?
                    TeamBColor = config.TeamConfigB.Color;

                    if (string.IsNullOrEmpty(TeamAColor) || TeamAColor.Length < 6 || TeamAColor.Length > 6 || !hexFilter.IsMatch(TeamAColor))
                        TeamAColor = "#EA3232";
                    else TeamAColor = "#" + TeamAColor;

                    if (string.IsNullOrEmpty(TeamBColor) || TeamBColor.Length < 6 || TeamBColor.Length > 6 || !hexFilter.IsMatch(TeamBColor))
                        TeamBColor = "#3232EA";
                    else TeamBColor = "#" + TeamBColor;

                    _spawnSelectorB = new SpawnSelector(config.EventName, config.TeamConfigB.Spawnfile);
                }

                Timer = new GameTimer(this);

                GodmodeEnabled = true;

                OpenEvent();
            }
            #endregion

            #region Event Management 
            /// <summary>
            /// Opens the event for players to join
            /// </summary>
            internal virtual void OpenEvent()
            {
                _isClosed = false;
                Status = EventManager.EventStatus.Open;

                if (Configuration.Message.Announce)
                {
                    _startsAtTime = Time.time + Configuration.Timer.Start;

                    InvokeHandler.InvokeRepeating(this, BroadcastOpenEvent, 0f, Configuration.Message.AnnounceInterval);
                }

                //InvokeHandler.Invoke(this, PrestartEvent, Configuration.Timer.Start);
            }

            /// <summary>
            /// Closes the event and prevent's more players from joining
            /// </summary>
            internal virtual void CloseEvent()
            {
                _isClosed = true;
                Broadcast("Notification.EventClosed");

                InvokeHandler.CancelInvoke(this, BroadcastOpenEvent);
            }

            /// <summary>
            /// The event prestart where players are created and sent to the arena
            /// </summary>
            internal virtual void PrestartEvent()
            {
                CleanupEntities();
                if (!HasMinimumRequiredPlayers())
                {
                    Broadcast("Notification.NotEnoughToStart");
                    return;
                }
                InvokeHandler.CancelInvoke(this, BroadcastOpenEvent);

                Status = EventManager.EventStatus.Prestarting;

                if(!isstaticsSpawned)
                    SpawnStaticPrefabs();
                
                eventPlayers.ForEach(player => spectators.Add(player.Player));
                Pool.FreeList(ref eventPlayers);
                eventPlayers = Pool.GetList<BaseEventPlayer>();
                StartCoroutine(CreateEventPlayers());
            }

            /// <summary>
            /// Start's the event
            /// </summary>
            protected virtual void StartEvent()
            {
                InvokeHandler.CancelInvoke(this, PrestartEvent);
                if (!HasMinimumRequiredPlayers())
                {
                    Broadcast("Notification.NotEnoughToStart");
                    EndEvent();
                    return;
                }

                Timer.StopTimer();

                Status = EventManager.EventStatus.Started;
                if (Config.TimeLimit > 0)
                    Timer.StartTimer(Config.TimeLimit, string.Empty, PrestartEvent);


                GodmodeEnabled = false;

                eventPlayers.ForEach((BaseEventPlayer eventPlayer) =>
                {
                    if (eventPlayer?.Player == null)
                        return;

                    if (eventPlayer.IsDead)
                        RespawnPlayer(eventPlayer);
                    else
                    {
                        ResetPlayer(eventPlayer);
                        OnPlayerRespawn(eventPlayer);
                    }
                });
            }

            /// <summary>
            /// End's the event and restore's all player's back to the state they were in prior to the event starting
            /// </summary>
            internal virtual void EndEvent()
            {
                InvokeHandler.CancelInvoke(this, BroadcastOpenEvent);

                InvokeHandler.CancelInvoke(this, PrestartEvent);

                Timer.StopTimer();

                Status = EventManager.EventStatus.Finished;

                GodmodeEnabled = true;

                LastEventResult.UpdateFromEvent(this);

                ProcessWinners();

                eventPlayers.ToList().ForEach((BaseEventPlayer eventPlayer) =>
                {
                    if (eventPlayer?.Player == null)
                        return;

                    if (eventPlayer.IsDead)
                        ResetPlayer(eventPlayer);

                    //EventStatistics.Data.OnGamePlayed(eventPlayer.Player, Config.EventType);
                });

                //EventStatistics.Data.OnGamePlayed(Config.EventType);

                EjectAllPlayers();
                Destroy(this);
            }

            /// <summary>
            /// Shows Scoreboard and restarts game couple of seconds later
            /// </summary>
            internal virtual void RestartEvent()
            {
                Timer.StopTimer();
                Timer.StartTimer(10, string.Empty, PrestartEvent);
                foreach (BasePlayer player in GetPlayersOfRoom(this))
                    ResetPlayer(player);
                foreach (BasePlayer player in GetPlayersOfRoom(this))
                {
                    if (player.HasComponent<BaseEventPlayer>())
                        DestroyImmediate(player.GetComponent<BaseEventPlayer>());
                }
                
                //CleanupEntities();    // Transfered this method to PrestartEvent
            }
            #endregion

            #region Player Management
            internal bool IsOpen()
            {
                if (_isClosed || Status == EventManager.EventStatus.Finished)
                    return false;

                if (((int)Status < 2 && spectators.Count >= Config.MaximumPlayers) || eventPlayers.Count >= Config.MaximumPlayers)
                    return false;

                if (!string.IsNullOrEmpty(CanJoinEvent()))
                    return false;

                return true;
            }

            internal bool CanJoinEvent(BasePlayer player)
            {
                if (_isClosed)
                {
                    player.ChatMessage(Message("Notification.EventClosed", player.userID));
                    return false;
                }

                if (Status == EventManager.EventStatus.Finished)
                {
                    player.ChatMessage(Message("Notification.EventFinished", player.userID));
                    return false;
                }

                if (((int)Status < 2 && spectators.Count >= Config.MaximumPlayers) || eventPlayers.Count >= Config.MaximumPlayers)
                {
                    player.ChatMessage(Message("Notification.MaximumPlayers", player.userID));
                    return false;
                }

                string str = CanJoinEvent();

                if (!string.IsNullOrEmpty(str))
                {
                    player.ChatMessage(str);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Allow or disallow players to join the event
            /// </summary>
            /// <returns>Supply a (string) reason to disallow, or a empty string to allow</returns>
            protected virtual string CanJoinEvent()
            {
                return string.Empty;
            }

            /// <summary>
            /// Override to perform additional logic when a player joins an event
            /// </summary>
            /// <param name="player">The BasePlayer object of the player joining the event</param>
            /// <param name="team">The team the player should be placed in</param>
            internal virtual void JoinEvent(BasePlayer player, Team team = Team.None)
            {
                if (GetPlayersOfRoom(this).Contains(player))
                {
                    BroadcastToPlayer(player, Message("Notification.AlreadyInRoom", player.userID));
                    return;
                }
                if (IsinRoom(player))
                {
                    BroadcastToPlayer(player, Message("Notification.Cantjointworoom", player.userID));
                    return;
                }

                if (Status == EventManager.EventStatus.Started)
                {
                    player.gameObject.AddComponent<RoomSpectatingBehaviour>().Enable(this);
                    spectators.Add(player);
                }
                else if(Status == EventManager.EventStatus.Prestarting || Status == EventManager.EventStatus.Open)
                {
                    spectators.Add(player);
                }
                else
                {
                    return;
                }
                player.gameObject.AddComponent<CurrentEventInfo>().Enable(this);
                player.gameObject.AddComponent<NetworkGroupData>().Enable(Convert.ToUInt32(gameroom.roomID));
                if (Configuration.Message.BroadcastJoiners)
                    BroadcastToRoom(string.Format(Message("Notification.PlayerJoined"),player.displayName,Config.EventName));
                JoinGroupNetwork(player);
                OnPlayerJoined(player);
            }

            /// <summary>
            /// Override to perform additional logic when a player leaves an event. This is called when the player uses the leave chat command prior to destroying the BaseEventPlayer
            /// </summary>
            /// <param name="player">The BasePlayer object of the player leaving the event</param>
            internal virtual void LeaveEvent(BasePlayer player)
            {
                BaseEventPlayer eventPlayer = GetUser(player);
                if (spectators.Contains(player))
                {
                    spectators.Remove(player);

                    if (Configuration.Message.BroadcastLeavers)
                        Broadcast("Notification.PlayerLeft", player.displayName, Config.EventName);

                    if (!string.IsNullOrEmpty(Config.ZoneID))
                        Instance.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", Config.ZoneID, player);

                    if (player.HasComponent<BaseEventPlayer>())
                        DestroyImmediate(player.GetComponent<BaseEventPlayer>());

                    if (player.HasComponent<RoomSpectatingBehaviour>())
                        DestroyImmediate(player.GetComponent<RoomSpectatingBehaviour>());

                    if (player.HasComponent<NetworkGroupData>())
                        DestroyImmediate(player.GetComponent<NetworkGroupData>());
                    RestoreGroupNetworks(player);

                    if (!player.IsConnected || player.IsSleeping() || IsUnloading)
                        player.Die();
                    else Instance.RestorePlayer(player);
                    OnPlayerLeft(player);
                    CurrentEventInfo currentEventInfo;
                    if(player.TryGetComponent(out currentEventInfo))
                        DestroyImmediate(currentEventInfo);
                    return;
                }
                else if (eventPlayer != null)
                {
                    if (!eventPlayers.Contains(eventPlayer))
                        return;
                    if (!string.IsNullOrEmpty(Config.ZoneID))
                        Instance.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", Config.ZoneID, player);

                    RestoreGroupNetworks(player);

                    eventPlayers.Remove(eventPlayer);

                    DestroyImmediate(eventPlayer);

                    if (player.HasComponent<NetworkGroupData>())
                        DestroyImmediate(player.GetComponent<NetworkGroupData>());

                    if (!player.IsConnected || player.IsSleeping() || IsUnloading)
                        player.Die();
                    else
                        Instance.RestorePlayer(player);
                    OnPlayerLeft(player);
                    CurrentEventInfo currentEventInfo;
                    if(player.TryGetComponent(out currentEventInfo))
                        DestroyImmediate(currentEventInfo);
                    return;
                }
            }

            /// <summary>
            /// Override to perform additional logic when a event player leaves an event
            /// </summary>
            /// <param name="eventPlayer">The BaseEventPlayer object of the player leaving the event</param>
            internal virtual void LeaveEvent(BaseEventPlayer eventPlayer)
            {
                BasePlayer player = eventPlayer?.Player;
                if (spectators.Contains(player))
                {
                    spectators.Remove(player);

                    if (Configuration.Message.BroadcastLeavers)
                        Broadcast("Notification.PlayerLeft", player.displayName, Config.EventName);

                    if (!string.IsNullOrEmpty(Config.ZoneID))
                        Instance.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", Config.ZoneID, player);

                    if (player.HasComponent<BaseEventPlayer>())
                        DestroyImmediate(player.GetComponent<BaseEventPlayer>());

                    if(player.HasComponent<RoomSpectatingBehaviour>())
                        DestroyImmediate(player.GetComponent<RoomSpectatingBehaviour>());
                    RestoreGroupNetworks(player);

                    if (player.HasComponent<NetworkGroupData>())
                        DestroyImmediate(player.GetComponent<NetworkGroupData>());

                    if (!player.IsConnected || player.IsSleeping() || IsUnloading)
                        player.Die();
                    else Instance.RestorePlayer(player);
                    OnPlayerLeft(player);
                    CurrentEventInfo currentEventInfo;
                    if(player.TryGetComponent(out currentEventInfo))
                        DestroyImmediate(currentEventInfo);
                    return;
                }
                else if (eventPlayer != null && eventPlayers.Contains(eventPlayer))
                {
                    if (!string.IsNullOrEmpty(Config.ZoneID))
                        Instance.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", Config.ZoneID, player);

                    RestoreGroupNetworks(player);

                    eventPlayers.Remove(eventPlayer);

                    DestroyImmediate(eventPlayer);

                    if (player.HasComponent<NetworkGroupData>())
                        DestroyImmediate(player.GetComponent<NetworkGroupData>());

                    if (!player.IsConnected || player.IsSleeping() || IsUnloading)
                        player.Die();
                    else Instance.RestorePlayer(player);

                    //if (Status != EventManager.EventStatus.Finished && !HasMinimumRequiredPlayers())
                    //{
                    //    BroadcastToPlayers("Notification.NotEnoughToContinue");
                    //    EndEvent();
                    //}
                    OnPlayerLeft(player);
                    CurrentEventInfo currentEventInfo;
                    if(player.TryGetComponent(out currentEventInfo))
                        DestroyImmediate(currentEventInfo);
                    return;
                }
            }

            private IEnumerator CreateEventPlayers()
            {
                for (int i = spectators.Count - 1; i >= 0; i--)
                {
                    BasePlayer joiner = spectators[i];

                    //EMInterface.DestroyAllUI(joiner);

                    if(!eventPlayers.Contains(GetUser(joiner)))
                        CreateEventPlayer(joiner, GetPlayerTeam(joiner));

                    yield return CoroutineEx.waitForEndOfFrame;
                    yield return CoroutineEx.waitForEndOfFrame;
                }

                //UpdateScoreboard();

                Timer.StartTimer(Configuration.Timer.Prestart, Message("Notification.RoundStartsIn"), StartEvent);
            }

            /// <summary>
            /// Override to perform additional logic when initializing the BaseEventPlayer component
            /// </summary>
            /// <param name="player">The BasePlayer object of the player joining the event</param>
            /// <param name="team">The team this player is on</param>
            protected virtual void CreateEventPlayer(BasePlayer player, Team team = Team.None)
            {
                if (player == null)
                    return;

                spectators.Remove(player);

                if (player.HasComponent<RoomSpectatingBehaviour>())
                    DestroyImmediate(player.GetComponent<RoomSpectatingBehaviour>());

                BaseEventPlayer eventPlayer = AddPlayerComponent(player);

                eventPlayer.ResetPlayer();

                eventPlayer.Event = this;

                eventPlayer.Team = team;

                eventPlayers.Add(eventPlayer);

                if (!Config.AllowClassSelection || GetAvailableKits(eventPlayer.Team).Count <= 1)
                    eventPlayer.Kit = GetAvailableKits(team).First();

                SpawnPlayer(eventPlayer, Status == EventManager.EventStatus.Started, false);

                if (!string.IsNullOrEmpty(Config.ZoneID))
                    Instance.ZoneManager?.Call("AddPlayerToZoneWhitelist", Config.ZoneID, player);
            }

            /// <summary>
            /// Makes only room participants visible (networking)
            /// </summary>
            protected virtual void JoinGroupNetwork(BasePlayer player)
            {
                uint roomId = Convert.ToUInt32(gameroom.roomID);
                Network.Visibility.Group group = Net.sv.visibility.Get(roomId);
                roomobjects.AddRange(player?.children);
                player.net.SwitchGroup(group);

                player.SendFullSnapshot();
                player.UpdateNetworkGroup();
                player.SendNetworkUpdateImmediate(false);
                player.OnNetworkGroupChange();              // Updates children objects of main BaseNetworkable
                player.SendChildrenNetworkUpdateImmediate();
                player.SendEntityUpdate();
                IsolateColliders(player);
            }

            /// <summary>
            /// Restores groups of players end of the game (networking)
            /// </summary>
            protected virtual void RestoreGroupNetworks(BasePlayer player)
            {
                if (player == null)
                    return;

                Network.Visibility.Group orggroup = Net.sv.visibility.Get(35716);
                player.children.ForEach(x => roomobjects.Remove(x));
                player.net.SwitchGroup(orggroup);

                player.SendFullSnapshot();
                player.OnNetworkGroupChange();              // Updates children objects of main BaseNetworkable
                player.UpdateNetworkGroup();
                player.SendChildrenNetworkUpdateImmediate();
                player.SendNetworkUpdateImmediate(false);
                player.SendEntityUpdate();
                UnIsolateColliders(player);
            }
            /// <summary>
            /// Isolates player colliders from other game rooms
            /// </summary>
            protected virtual void IsolateColliders(BasePlayer player)
            {
                roomcolliders.AddRange(player.GetComponentsInChildren<Collider>(true));
                foreach (Collider playerCollider in player.gameObject.GetComponentsInChildren<Collider>(true))
                {
                    foreach (Collider othercollider in AllEventColliders)
                    {
                        if (othercollider == null || playerCollider == null)
                            continue;
                        Physics.IgnoreCollision(othercollider, playerCollider, true);
                    }
                }
                AllEventColliders.AddRange(roomcolliders);
            }

            /// <summary>
            /// Switches collisions only for lobby players and objects
            /// </summary>
            protected virtual void UnIsolateColliders(BasePlayer player)
            {
                List<Collider> playerColliders = Pool.GetList<Collider>();
                playerColliders = player.GetComponentsInChildren<Collider>(true).ToList();
                roomcolliders.RemoveAll(x => playerColliders.Contains(x));

                //Ignoring left game room
                foreach (Collider playerCollider in player.gameObject.GetComponentsInChildren<Collider>(true))
                {
                    foreach (Collider roomcollider in roomcolliders)
                    {
                        if (roomcollider == null || playerCollider == null)
                            continue;
                        Physics.IgnoreCollision(roomcollider, playerCollider, true);
                    }
                }
                

                //Activating lobby collisions
                foreach (Collider playerCollider in playerColliders)
                    foreach (Collider roomcollider in GetLobbyColliders().Where(x => !playerColliders.Contains(x)))
                        Physics.IgnoreCollision(roomcollider, playerCollider, false);
                Pool.FreeList(ref playerColliders);
            }

            /// <summary>
            /// Override to assign players to teams
            /// </summary>
            /// <param name="player"></param>
            /// <returns>The team the player will be assigned to</returns>
            protected virtual Team GetPlayerTeam(BasePlayer player) => Team.None;

            private bool IsinRoom(BasePlayer player)
            {
                return (player.HasComponent<BaseEventPlayer>() || player.HasComponent<RoomSpectatingBehaviour>());
            }

            /// <summary>
            /// Add's the BaseEventPlayer component to the player. Override with your own component if you want to extend the BaseEventPlayer class
            /// </summary>
            /// <param name="player"></param>
            /// <param name="team"></param>
            /// <returns>The BaseEventPlayer component</returns>
            protected virtual BaseEventPlayer AddPlayerComponent(BasePlayer player) => player.gameObject.GetComponent<BaseEventPlayer>() ?? player.gameObject.AddComponent<BaseEventPlayer>();

            /// <summary>
            /// Called prior to a event player respawning
            /// </summary>
            /// <param name="baseEventPlayer"></param>
            internal virtual void OnPlayerRespawn(BaseEventPlayer baseEventPlayer)
            {
                SpawnPlayer(baseEventPlayer, Status == EventManager.EventStatus.Started);
            }

            /// <summary>
            /// Spawn's the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="giveKit">Should this player recieve a kit?</param>
            /// <param name="sleep">Should this player be put to sleep before teleporting?</param>
            internal void SpawnPlayer(BaseEventPlayer eventPlayer, bool giveKit = true, bool sleep = false)
            {
                BasePlayer player = eventPlayer?.Player;
                if (player == null)
                    return;

                eventPlayer.Player.GetMounted()?.AttemptDismount(eventPlayer.Player);

                if (eventPlayer.Player.HasParent())
                    eventPlayer.Player.SetParent(null, true);

                StripInventory(player);

                ResetMetabolism(player);

                MovePosition(player, eventPlayer.Team == Team.B ? _spawnSelectorB.GetSpawnPoint() : _spawnSelectorA.GetSpawnPoint(), sleep);


                UpdateScoreboard(eventPlayer);

                if (giveKit)
                {
                    Instance.NextTick(() =>
                    {
                        if (!CanGiveKit(eventPlayer))
                            return;

                        GiveKit(player, eventPlayer.Kit);

                        OnKitGiven(eventPlayer);
                    });
                }

                eventPlayer.ApplyInvincibility();

                OnPlayerSpawned(eventPlayer);
            }

            /// <summary>
            /// Called after a player has spawned/respawned
            /// </summary>
            /// <param name="eventPlayer">The player that has spawned</param>
            protected virtual void OnPlayerSpawned(BaseEventPlayer eventPlayer) { }

            /// <summary>
            /// Called after a player joined into room
            /// </summary>
            protected virtual void OnPlayerJoined(BasePlayer player) 
            {
                Instance.LobbyRoomSystem.Call("RefreshLobbyUI");
                if (Status == EventManager.EventStatus.Prestarting && !player.HasComponent<BaseEventPlayer>())
                {
                    CreateEventPlayer(player);
                }
            }

            /// <summary>
            /// Called after a player left the room
            /// </summary>
            protected virtual void OnPlayerLeft(BasePlayer player)
            {
                Instance?.LobbyRoomSystem?.Call("RefreshLobbyUI");
            }

            /// <summary>
            /// Called when event player tries to open dropped dead body
            /// </summary>
            internal virtual object CanLootEntity(BasePlayer player, LootableCorpse corpse) => true;

            internal virtual object OnWoundCheck(BasePlayer player)
            {
                return null;
            }

            internal virtual void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
            {
            }

            internal virtual void OnMeleeThrown(BasePlayer player, Item item)
            {
                
            }
            
            internal virtual void OnLoseCondition(Item item, ref float amount){}
            
            /// <summary>
            /// Kicks all players out of the event
            /// </summary>
            protected void EjectAllPlayers()
            {
                foreach (BaseEventPlayer eventPlayer in eventPlayers.ToList())
                    LeaveEvent(eventPlayer);
                eventPlayers.Clear();

                foreach (BasePlayer player in spectators.ToList())
                {
                    LeaveEvent(player);
                }
                spectators.Clear();
            }

            /// <summary>
            /// Reset's all players that are currently dead and respawn's them
            /// </summary>
            protected void RespawnAllPlayers()
            {
                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                    RespawnPlayer(eventPlayers[i]);
            }

            protected bool HasMinimumRequiredPlayers() => (eventPlayers.Count + spectators.Count) >= Config.MinimumPlayers;
            protected int GetNeededRequiredPlayers() => Config.MinimumPlayers - (eventPlayers.Count + spectators.Count);
            #endregion

            #region Damage and Death
            /// <summary>
            /// Called when a player deals damage to a entity that is not another event player
            /// </summary>
            /// <param name="attacker">The player dealing the damage</param>
            /// <param name="entity">The entity that was hit</param>
            /// <param name="hitInfo">The HitInfo</param>
            /// <returns>True allows damage, false prevents damage</returns>
            internal virtual bool CanDealEntityDamage(BaseEventPlayer attacker, BaseEntity entity, HitInfo hitInfo)
            {
                return false;
            }

            /// <summary>
            /// Scale's player-to-player damage
            /// </summary>
            /// <param name="eventPlayer">The player that is attacking</param>
            /// <returns>1.0f is normal damage</returns>
            protected virtual float GetDamageModifier(BaseEventPlayer eventPlayer) => 1f;

            /// <summary>
            /// Calculates and applies damage to the player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="hitInfo"></param>
            internal virtual void OnPlayerTakeDamage(BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                BaseEventPlayer attacker = GetUser(hitInfo?.InitiatorPlayer);

                if (eventPlayer == null || attacker == null)
                    return;

                if (GodmodeEnabled || eventPlayer.IsDead || eventPlayer.IsInvincible)
                {
                    Console.WriteLine("couldnt take damage");
                    ClearDamage(hitInfo);
                    return;
                }
                
                float damageModifier = GetDamageModifier(attacker);
                if (damageModifier != 1f)
                    hitInfo.damageTypes.ScaleAll(damageModifier);

                eventPlayer.OnTakeDamage(attacker?.Player.userID ?? 0U);
            }
            /// <summary>
            /// Called when a button is clicked on an item in the inventory (drop, unwrap, ...)
            /// </summary>
            /// /// <returns>Returning a non-null value overrides default behavior</returns>
            internal virtual object OnItemAction(Item item, string action, BasePlayer player) { return null; }
            /// <summary>
            /// Called when moving an item from one inventory slot to another
            /// </summary>
            /// /// <returns>Returning a non-null value overrides default behavior</returns>
            internal virtual object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount) { return null; }
            
            /// <summary>
            /// Called prior to event player death logic. Prepares the player for the death cycle by hiding them from other players
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="hitInfo"></param>
            internal virtual void PrePlayerDeath(BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                if (CanDropBackpack())
                    eventPlayer.DropInventory();

                if (eventPlayer.Player.isMounted)
                {
                    BaseMountable baseMountable = eventPlayer.Player.GetMounted();
                    if (baseMountable != null)
                    {
                        baseMountable.DismountPlayer(eventPlayer.Player);
                        eventPlayer.Player.EnsureDismounted();
                    }
                }

                eventPlayer.IsDead = true;

                UpdateDeadSpectateTargets(eventPlayer);

                eventPlayer.Player.DisablePlayerCollider();

                eventPlayer.Player.RemoveFromTriggers();

                eventPlayer.RemoveFromNetwork();                

                OnEventPlayerDeath(eventPlayer, GetUser(hitInfo?.InitiatorPlayer), hitInfo);

                ClearDamage(hitInfo);
            }

            internal virtual void OnEventPlayerDeath(BaseEventPlayer victim, BaseEventPlayer attacker = null, HitInfo hitInfo = null)
            {
                if (victim == null || victim.Player == null)
                    return;

                StripInventory(victim.Player);

                if (Configuration.Message.BroadcastKills)
                    DisplayKillToChat(victim, attacker?.Player != null ? attacker.Player.displayName : string.Empty);
            }

            /// <summary>
            /// Display's the death message in chat
            /// </summary>
            /// <param name="victim"></param>
            /// <param name="attackerName"></param>
            protected virtual void DisplayKillToChat(BaseEventPlayer victim, string attackerName)
            {
                if (string.IsNullOrEmpty(attackerName))
                {
                    if (victim.IsOutOfBounds)
                        BroadcastToPlayers("Notification.Death.OOB", victim.Player.displayName);
                    else BroadcastToPlayers("Notification.Death.Suicide", victim.Player.displayName);
                }
                else BroadcastToPlayers("Notification.Death.Killed", victim.Player.displayName, attackerName);                
            }
            #endregion

            #region Winners
            /// <summary>
            /// Applies winner statistics, give's rewards and print's winner information to chat
            /// </summary>
            protected void ProcessWinners()
            {
                List<BaseEventPlayer> winners = Pool.GetList<BaseEventPlayer>();
                GetWinningPlayers(ref winners);

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer == null)
                        continue;

                    if (winners.Contains(eventPlayer))
                    {
                        //EventStatistics.Data.AddStatistic(eventPlayer.Player, "Wins");
                        Instance.GiveReward(eventPlayer, Configuration.Reward.WinAmount);
                    }
                    //else EventStatistics.Data.AddStatistic(eventPlayer.Player, "Losses");

                    //EventStatistics.Data.AddStatistic(eventPlayer.Player, "Played");
                }

                if (Configuration.Message.BroadcastWinners && winners.Count > 0)
                {
                    if (Plugin.IsTeamEvent)
                    {
                        Team team = winners[0].Team;
                        Broadcast("Notification.EventWin.Multiple.Team", team == Team.B ? TeamBColor : TeamAColor, team, winners.Select(x => x.Player.displayName).ToSentence());
                    }
                    else
                    {
                        if (winners.Count > 1)
                            Broadcast("Notification.EventWin.Multiple", winners.Select(x => x.Player.displayName).ToSentence());
                        else Broadcast("Notification.EventWin", winners[0].Player.displayName);
                    }
                }

                Pool.FreeList(ref winners);
            }

            /// <summary>
            /// Override to calculate the winning player(s). This should done done on a per event basis
            /// </summary>
            /// <param name="list"></param>
            protected virtual void GetWinningPlayers(ref List<BaseEventPlayer> list) { }
            #endregion

            #region Kits and Items
            /// <summary>
            /// Drop's the players belt and main containers in to a bag on death
            /// </summary>
            /// <returns>Return false to disable this feature</returns>
            protected virtual bool CanDropBackpack() => true;

            protected virtual bool CanDropBody() => false; 

            internal virtual object OnItemPickup(Item item, BasePlayer player) { return null; }

            protected virtual object GetRandomItemSpawn(string place) 
            {
                return Instance.MurderPickableSpawn?.CallHook("GetRandomItemSpawn", place);
            }

            /// <summary>
            /// Override to prevent players being given kits
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual bool CanGiveKit(BaseEventPlayer eventPlayer) => true;

            /// <summary>
            /// Called after a player has been given a kit. If the event is team based and team attire kits have been set team attire will be given
            /// </summary>
            /// <param name="eventPlayer"></param>
            protected virtual void OnKitGiven(BaseEventPlayer eventPlayer)
            {
                if (Plugin.IsTeamEvent)
                {
                    string kit = eventPlayer.Team == Team.B ? Config.TeamConfigB.Clothing : Config.TeamConfigA.Clothing;
                    if (!string.IsNullOrEmpty(kit))
                    {
                        List<Item> items = eventPlayer.Player.inventory.containerWear.itemList;
                        for (int i = 0; i < items.Count; i++)
                        {
                            Item item = items[i];
                            item.RemoveFromContainer();
                            item.Remove();
                        }

                        GiveKit(eventPlayer.Player, kit);
                    }
                }
            }

            /// <summary>
            /// Get's the list of Kits available for the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal List<string> GetAvailableKits(Team team) => team == Team.B ? Config.TeamConfigB.Kits : Config.TeamConfigA.Kits;
            #endregion

            #region Overrides
            /// <summary>
            /// Allows you to display additional event details in the event menu. The key should be a localized message for the target player
            /// </summary>
            /// <param name="list"></param>
            /// <param name="playerId">The user's ID for localization purposes</param>
            internal virtual void GetAdditionalEventDetails(ref List<KeyValuePair<string, object>> list, ulong playerId) { }
            #endregion

            #region Spectating
            /// <summary>
            /// Fill's a list with valid spectate targets
            /// </summary>
            /// <param name="list"></param>
            internal virtual void GetSpectateTargets(ref List<BaseEventPlayer> list)
            {
                list.Clear();
                list.AddRange(eventPlayers);
            }

            /// <summary>
            /// Checks all spectating event players and updates their spectate target if the target has just died
            /// </summary>
            /// <param name="victim"></param>
            private void UpdateDeadSpectateTargets(BaseEventPlayer victim)
            {
                List<BaseEventPlayer> list = Pool.GetList<BaseEventPlayer>();
                GetSpectateTargets(ref list);

                bool hasValidSpectateTargets = list.Count > 0;

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    if (eventPlayer.Player.IsSpectating() && eventPlayer.SpectateTarget == victim)
                    {
                        if (hasValidSpectateTargets)
                            eventPlayer.GetComponent<RoomSpectatingBehaviour>().UpdateSpectateTarget();
                        else eventPlayer.GetComponent<RoomSpectatingBehaviour>().FinishSpectating();
                    }
                }
            }
            #endregion

            #region Player Counts
            /// <summary>
            /// Count the amount of player's that are alive
            /// </summary>
            /// <returns></returns>
            internal int GetAlivePlayerCount()
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    if (!eventPlayers[i]?.IsDead ?? false)
                        count++;
                }
                return count;
            }

            /// <summary>
            /// Count the amount of player's on the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal int GetTeamCount(Team team)
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    if (eventPlayers[i]?.Team == team)
                        count++;
                }
                return count;
            }

            /// <summary>
            /// Count the amount of player's that are alive on the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal int GetTeamAliveCount(Team team)
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer != null && eventPlayer.Team == team && !eventPlayer.IsDead)
                        count++;
                }
                return count;               
            }
            #endregion

            #region Teams
            /// <summary>
            /// Get the score for the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal virtual int GetTeamScore(Team team) => 0;

            /// <summary>
            /// Balance the team's if one team has > 2 more player's on it
            /// </summary>
            protected void BalanceTeams()
            {
                int aCount = GetTeamCount(Team.A);
                int bCount = GetTeamCount(Team.B);

                int difference = aCount > bCount + 1 ? aCount - bCount : bCount > aCount + 1 ? bCount - aCount : 0;
                Team moveFrom = aCount > bCount + 1 ? Team.A : bCount > aCount + 1 ? Team.B : Team.None;

                if (difference > 1 && moveFrom != Team.None)
                {
                    BroadcastToPlayers("Notification.Teams.Unbalanced");

                    List<BaseEventPlayer> teamPlayers = Pool.GetList<BaseEventPlayer>();

                    eventPlayers.ForEach(x =>
                    {
                        if (x.Team == moveFrom)
                            teamPlayers.Add(x);
                    });

                    for (int i = 0; i < (int)Math.Floor((float)difference / 2); i++)
                    {
                        BaseEventPlayer eventPlayer = teamPlayers.GetRandom();
                        teamPlayers.Remove(eventPlayer);

                        eventPlayer.Team = moveFrom == Team.A ? Team.B : Team.A;
                        BroadcastToPlayer(eventPlayer, string.Format(Message("Notification.Teams.TeamChanged", eventPlayer.Player.userID), eventPlayer.Team));
                    }

                    Pool.FreeList(ref teamPlayers);
                }
            }
            #endregion

            #region Entity Management
            /// <summary>
            /// Keep's track of entities deployed by event players
            /// </summary>
            /// <param name="entity"></param>
            internal void OnEntityDeployed(BaseCombatEntity entity) => _deployedObjects.Add(entity);

            /// <summary>
            /// Adds Item to Room and manages colliders
            /// </summary>
            public void AddItemToRoom(BaseEntity entity)
            {
                roomobjects.Add(entity);
                entity.gameObject.AddComponent<NetworkGroupData>().Enable(Convert.ToUInt32(gameroom.roomID));
                entity.net.SwitchGroup(Net.sv.visibility.Get(Convert.ToUInt32(gameroom.roomID)));
                roomcolliders.AddRange(entity.GetComponentsInChildren<Collider>(true));
                
                List<Collider> otherColliders = Pool.GetList<Collider>();
                otherColliders = AllEventColliders;
                foreach (Collider collider in roomcolliders)
                    if (otherColliders.Contains(collider))
                        otherColliders.Remove(collider);

                foreach (Collider colliderinroom in roomcolliders)
                {
                    foreach (Collider othercollider in otherColliders)
                    {
                        if (othercollider != null && colliderinroom != null)
                            Physics.IgnoreCollision(othercollider, colliderinroom, true);
                    }
                }
                //entity.SendNetworkGroupChange();
                //entity.SendNetworkUpdateImmediate(false);
                entity.gameObject.AddComponent<BaseEventObject>();

                Pool.FreeList(ref otherColliders);
            }

            /// <summary>
            /// Adds Static Entity to Room and isolates
            /// </summary>
            public void AddStaticEntityToRoom(BaseEntity staticentity)
            {
                staticobjects.Add(staticentity);
                staticentity.gameObject.AddComponent<NetworkGroupData>().Enable(Convert.ToUInt32(gameroom.roomID));
                staticentity.net.SwitchGroup(Net.sv.visibility.Get(Convert.ToUInt32(gameroom.roomID)));
                roomcolliders.AddRange(staticentity.GetComponentsInChildren<Collider>(true));
                
                List<Collider> otherColliders = Pool.GetList<Collider>();
                otherColliders = AllEventColliders;
                foreach (Collider collider in roomcolliders)
                    if (otherColliders.Contains(collider))
                        otherColliders.Remove(collider);

                foreach (Collider colliderinroom in roomcolliders)
                {
                    foreach (Collider othercollider in otherColliders)
                    {
                        if (othercollider != null && colliderinroom != null)
                            Physics.IgnoreCollision(othercollider, colliderinroom, true);
                    }
                }
                Pool.FreeList(ref otherColliders);
                staticentity.SendNetworkUpdateImmediate();
            }

            public virtual void SpawnStaticPrefabs()
            {
                if (!Config.HasStaticObjects)
                    return;
                foreach(StaticObject staticobject in StaticObjects[Config.EventName])
                {
                    var entity = staticobject.Spawn();
                    Instance.NextFrame(() => AddStaticEntityToRoom(entity));
                    isstaticsSpawned = true;
                }
                SetStaticPrefabRelations();

                foreach (StaticObject staticObject in StaticObjects[Config.EventName])
                    staticObject.FreeEntity();
            }
            
            /// <summary>
            /// Manages the relationships of static entities i.e. Electricity
            /// </summary>
            public virtual void SetStaticPrefabRelations()
            {
                //Setting connections between electrical devices
                foreach (StaticObject iostaticentity in StaticObjects[Config.EventName]
                             .Where(x => x.staticEntity is IOEntity)) //nameof sıkıntı çıkarabilir
                {
                    iostaticentity.SetElectricConnections(StaticObjects[Config.EventName]);
                }
            }
            

            /// <summary>
            /// Destroy's any entities deployed by event players
            /// </summary>
            private void CleanupEntities()
            {
                for (int i = _deployedObjects.Count - 1; i >= 0; i--)
                {
                    BaseCombatEntity entity = _deployedObjects[i];
                    if (entity != null && !entity.IsDestroyed)
                        entity.DieInstantly();
                }

                Pool.FreeList(ref _deployedObjects);
                _deployedObjects = Pool.GetList<BaseCombatEntity>();

                foreach(BaseEntity obj in roomobjects)
                {
                    if(obj != null && !obj.IsDestroyed)
                        obj.Kill();
                }
                Pool.FreeList(ref roomobjects);
                roomobjects = Pool.GetList<BaseEntity>();
            }

            private void CleanupStaticEntities()
            {
                foreach(BaseEntity staticentity in staticobjects)
                    if(!staticentity.IsDestroyed)
                        staticentity.Kill();
                Pool.FreeList(ref staticobjects);
            }
            #endregion

            #region Scoreboard    
            /// <summary>
            /// Rebuild and send the scoreboard to players
            /// </summary>
            internal void UpdateScoreboard()
            {
                UpdateScores();
                BuildScoreboard();

                if (scoreContainer != null)
                {
                    eventPlayers.ForEach((BaseEventPlayer eventPlayer) =>
                    {
                        if (!eventPlayer.IsDead) { }
                            //eventPlayer.AddUI(EMInterface.UI_SCORES, scoreContainer);
                    });
                }
            }

            /// <summary>
            /// Send the last generated scoreboard to the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            protected void UpdateScoreboard(BaseEventPlayer eventPlayer)
            {
                if (scoreContainer != null && !eventPlayer.IsDead) { }
                    //eventPlayer.AddUI(EMInterface.UI_SCORES, scoreContainer);
            }

            /// <summary>
            /// Update the score list and sort it
            /// </summary>
            protected void UpdateScores()
            {
                scoreData.Clear();

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    scoreData.Add(new ScoreEntry(eventPlayer, GetFirstScoreValue(eventPlayer), GetSecondScoreValue(eventPlayer)));
                }

                SortScores(ref scoreData);
            }

            /// <summary>
            /// Called when building the scoreboard. This should be done on a per event basis
            /// </summary>
            protected virtual void BuildScoreboard() { }

            /// <summary>
            /// The first score value to be displayed on scoreboards
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual float GetFirstScoreValue(BaseEventPlayer eventPlayer) => 0f;

            /// <summary>
            /// The second score value to be displayed on scoreboards
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual float GetSecondScoreValue(BaseEventPlayer eventPlayer) => 0f;

            /// <summary>
            /// Sort's the score list. This should be done on a per event basis
            /// </summary>
            /// <param name="list"></param>
            protected virtual void SortScores(ref List<ScoreEntry> list) { }
            #endregion

            #region Event Messaging
            /// <summary>
            /// Broadcasts a localized message to all event players
            /// </summary>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToPlayers(string key, params object[] args)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(Message(key, eventPlayer.Player.userID), args) : Message(key, eventPlayer.Player.userID));
                }
            }
            /// <summary>
            /// Broadcasts a localized message to all room players
            /// </summary>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToRoom(string msg)
            {
                foreach(BasePlayer player in GetPlayersOfRoom(this))
                {
                    BroadcastToPlayer(player, msg);
                }
            }

            /// <summary>
            /// Broadcasts a localized message to all event players, using the calling plugins localized messages
            /// </summary>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToPlayers(Func<string, ulong, string> GetMessage, string key, params object[] args)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(GetMessage(key, eventPlayer.Player.userID), args) : GetMessage(key, eventPlayer.Player.userID));
                }
            }

            /// <summary>
            /// Broadcasts a localized message to all event players on the specified team
            /// </summary>
            /// <param name="team">Target team</param>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToTeam(Team team, string key, string[] args = null)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null && eventPlayer.Team == team)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(Message(key, eventPlayer.Player.userID), args) : Message(key, eventPlayer.Player.userID));
                }
            }

            /// <summary>
            /// Sends a message directly to the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="message"></param>
            internal void BroadcastToPlayer(BaseEventPlayer eventPlayer, string message) => eventPlayer?.Player?.SendConsoleCommand("chat.add", 0, Configuration.Message.ChatIcon,Configuration.announcerlabel + message);
            internal void BroadcastToPlayer(BasePlayer player, string message) => player.SendConsoleCommand("chat.add", 0, Configuration.Message.ChatIcon,Configuration.announcerlabel + message);

            private void BroadcastOpenEvent()
            {
                int timeRemaining = (int)(_startsAtTime - Time.time);
                if (timeRemaining > 0)
                    Broadcast("Notification.EventOpen", Config.EventName, Config.EventType, timeRemaining);
            }
            #endregion

            #region UserUI
            protected virtual void SendRoleUI(BaseEventPlayer player) { }

            protected virtual void SendScoreboard() { }
            #endregion
        }

        public class BaseEventPlayer : MonoBehaviour
        {
            protected float _respawnDurationRemaining;

            protected float _invincibilityEndsAt;

            private double _resetDamageTime;

            private List<ulong> _damageContributors = Pool.GetList<ulong>();

            private bool _isOOB;

            private int _oobTime;

            private int _spectateIndex = 0;


            internal BasePlayer Player { get; private set; }

            internal BaseEventGame Event { get; set; }

            internal Team Team { get; set; } = Team.None;

            internal int Kills { get; set; }

            internal int Deaths { get; set; }
            


            internal bool IsDead { get; set; }

            internal bool AutoRespawn { get; set; }

            internal bool CanRespawn => _respawnDurationRemaining <= 0;

            internal int RespawnRemaining => Mathf.CeilToInt(_respawnDurationRemaining);

            internal bool IsInvincible => Time.time < _invincibilityEndsAt;


            internal BaseEventPlayer SpectateTarget { get; private set; } = null;


            internal string Kit { get; set; }

            internal bool IsSelectingClass { get; set; }

            internal bool IsOutOfBounds
            {
                get
                {
                    return _isOOB;
                }
                set
                {
                    if (value)
                    {
                        _oobTime = 10;
                        InvokeHandler.Invoke(this, TickOutOfBounds, 1f);
                    }
                    else InvokeHandler.CancelInvoke(this, TickOutOfBounds);

                    _isOOB = value;
                }
            }
            
            void Awake()
            {
                Player = GetComponent<BasePlayer>();

                Player.metabolism.bleeding.max = 0;
                Player.metabolism.bleeding.value = 0;
                Player.metabolism.radiation_level.max = 0;
                Player.metabolism.radiation_level.value = 0;
                Player.metabolism.radiation_poison.max = 0;
                Player.metabolism.radiation_poison.value = 0;

                Player.metabolism.SendChangesToClient();
            }

            void OnDestroy()
            {
                //if (Player.IsSpectating())
                //    FinishSpectating();

                Player.EnablePlayerCollider();

                Player.health = Player.MaxHealth();

                Player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

                Player.metabolism.bleeding.max = 1;
                Player.metabolism.bleeding.value = 0;
                Player.metabolism.radiation_level.max = 100;
                Player.metabolism.radiation_level.value = 0;
                Player.metabolism.radiation_poison.max = 500;
                Player.metabolism.radiation_poison.value = 0;

                Player.metabolism.SendChangesToClient();

                if (Player.isMounted)
                    Player.GetMounted()?.AttemptDismount(Player);
                
                DestroyUI();

                if (IsUnloading)
                    StripInventory(Player);

                UnlockInventory(Player);
                
                InvokeHandler.CancelInvoke(this, TickOutOfBounds);

                Pool.FreeList(ref _damageContributors);
                Pool.FreeList(ref _openPanels);
            }
            public class Role
            {
                public string realName { get; set; }
            }

            internal void ResetPlayer()
            {
                Team = Team.None;
                Kills = 0;
                Deaths = 0;
                IsDead = false;
                AutoRespawn = false;
                Kit = string.Empty;
                IsSelectingClass = false;

                _spectateIndex = 0;
                _respawnDurationRemaining = 0;
                _invincibilityEndsAt = 0;
                _resetDamageTime = 0;
                _oobTime = 0;
                _isOOB = false;

                _damageContributors.Clear();
            }

            internal void ForceSelectClass()
            {
                IsDead = true;
                IsSelectingClass = true;
            }

            protected void RespawnTick()
            {
                _respawnDurationRemaining = Mathf.Clamp(_respawnDurationRemaining - 1f, 0f, float.MaxValue);

                //EMInterface.UpdateRespawnButton(this);

                if (_respawnDurationRemaining <= 0f)
                {
                    InvokeHandler.CancelInvoke(this, RespawnTick);

                    if (AutoRespawn)
                        RespawnPlayer(this);
                }
            }

            #region Death
            internal void OnKilledPlayer(HitInfo hitInfo)
            {
                Kills++;

                int rewardAmount = Configuration.Reward.KillAmount;

                //EventStatistics.Data.AddStatistic(Player, "Kills");

                if (hitInfo != null)
                {
                    //if (hitInfo.damageTypes.IsMeleeType())
                    //    EventStatistics.Data.AddStatistic(Player, "Melee");

                    //if (hitInfo.isHeadshot)
                    //{
                    //    EventStatistics.Data.AddStatistic(Player, "Headshots");
                    //    rewardAmount = Configuration.Reward.HeadshotAmount;
                    //}
                }

                if (rewardAmount > 0)
                    Instance.GiveReward(this, rewardAmount);
            }

            internal virtual void OnPlayerDeath(BaseEventPlayer attacker = null, float respawnTime = 5f)
            {
                AddPlayerDeath(attacker);

                _respawnDurationRemaining = respawnTime;

                InvokeHandler.InvokeRepeating(this, RespawnTick, 1f, 1f);

                DestroyUI();

                string message = attacker != null ? string.Format(Message("UI.Death.Killed", Player.userID), attacker.Player.displayName) : 
                                 IsOutOfBounds ? Message("UI.Death.OOB", Player.userID) :
                                 Message("UI.Death.Suicide", Player.userID);

                //EMInterface.DisplayDeathScreen(this, message, true);
            }

            internal void AddPlayerDeath(BaseEventPlayer attacker = null)
            {
                Deaths++;
                //EventStatistics.Data.AddStatistic(Player, "Deaths");
                ApplyAssistPoints(attacker);
            }

            protected void ApplyAssistPoints(BaseEventPlayer attacker = null)
            {
                if (_damageContributors.Count > 1)
                {
                    for (int i = 0; i < _damageContributors.Count - 1; i++)
                    {
                        ulong contributorId = _damageContributors[i];
                        if (attacker != null && attacker.Player.userID == contributorId)
                            continue;

                        //EventStatistics.Data.AddStatistic(contributorId, "Assists");
                    }
                }

                _resetDamageTime = 0;
                _damageContributors.Clear();
            }

            internal void ApplyInvincibility() => _invincibilityEndsAt = Time.time + 3f;
            #endregion
            
            protected void TickOutOfBounds()
            {
                if (Player == null)
                {
                    BaseEventGame room = GetRoomofPlayer(Player);
                    room?.LeaveEvent(this);
                    return;
                }
                
                if (IsDead)
                    return;

                if (IsOutOfBounds)
                {
                    BaseEventGame room = GetRoomofPlayer(Player);
                    if (_oobTime == 10)
                        room.BroadcastToPlayer(this, Message("Notification.OutOfBounds", Player.userID));
                    else if (_oobTime == 0)
                    {
                        Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", Player.transform.position);
                        try
                        {
                            if (room.Status == EventStatus.Started)
                                room.PrePlayerDeath(this, null);
                            else room.SpawnPlayer(this, false);
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                    else room.BroadcastToPlayer(this, string.Format(Message("Notification.OutOfBounds.Time", Player.userID), _oobTime));

                    _oobTime--;

                    InvokeHandler.Invoke(this, TickOutOfBounds, 1f);
                }
            }

            internal void DropInventory()
            {
                const string BACKPACK_PREFAB = "assets/prefabs/misc/item drop/item_drop_backpack.prefab";

                DroppedItemContainer itemContainer = ItemContainer.Drop(BACKPACK_PREFAB, Player.transform.position, Quaternion.identity, new ItemContainer[] { Player.inventory.containerBelt, Player.inventory.containerMain });
                if (itemContainer != null)
                {
                    itemContainer.playerName = Player.displayName;
                    itemContainer.playerSteamID = Player.userID;

                    itemContainer.CancelInvoke(itemContainer.RemoveMe);
                    itemContainer.Invoke(itemContainer.RemoveMe, Configuration.Timer.Bag);
                }
            }

            internal virtual void DropBody(HitInfo hitInfo)
            {
                
            }

            #region Networking
            internal void RemoveFromNetwork()
            {
                var write = Net.sv.StartWrite();
                write.PacketID(Network.Message.Type.EntityDestroy);
                write.EntityID(Player.net.ID);
                write.UInt8((byte)BaseNetworkable.DestroyMode.None);
                write.Send(new SendInfo(Player.net.group.subscribers.Where(x => x.userid != Player.userID).ToList()));
            }

            internal void AddToNetwork() => Player.SendFullSnapshot();            
            #endregion

            #region Damage Contributors
            internal void OnTakeDamage(ulong attackerId)
            {
                float time = Time.realtimeSinceStartup;
                if (time > _resetDamageTime)
                {
                    _resetDamageTime = time + 3f;
                    _damageContributors.Clear();
                }

                if (attackerId != 0U && attackerId != Player.userID)
                {
                    if (_damageContributors.Contains(attackerId))
                        _damageContributors.Remove(attackerId);
                    _damageContributors.Add(attackerId);
                }
            }

            internal List<ulong> DamageContributors => _damageContributors;
            #endregion

            #region UI Management
            private List<string> _openPanels = Pool.GetList<string>();

            internal void AddUI(string panel, CuiElementContainer container)
            {
                DestroyUI(panel);

                _openPanels.Add(panel);
                CuiHelper.AddUi(Player, container);
            }

            internal void DestroyUI()
            {
                foreach (string panel in _openPanels)
                    CuiHelper.DestroyUi(Player, panel);
                _openPanels.Clear();
            }

            internal void DestroyUI(string panel)
            {
                if (_openPanels.Contains(panel))
                    _openPanels.Remove(panel);
                CuiHelper.DestroyUi(Player, panel);
            }
            #endregion
        }

        public class BaseEventObject : MonoBehaviour
        {

        }

        /// <summary>
        /// Used for spectating other players in the room
        /// </summary>
        public class RoomSpectatingBehaviour : MonoBehaviour
        {
            public BasePlayer Player { get; set; }
            public BaseEventGame Event { get; set; }
            internal BaseEventPlayer SpectateTarget { get; private set; } = null;
            private int _spectateIndex = 0;

            public void Enable(BaseEventGame baseEventGame)
            {
                Player = GetComponent<BasePlayer>();
                Event = baseEventGame;
                if (Event.eventPlayers.Count > 0)
                    BeginSpectating();
            }
            void OnDestroy()
            {
                FinishSpectating();
            }

            public void BeginSpectating()
            {
                if (Player.IsSpectating())
                    return;

                UpdateSpectateTarget();
                Player.StartSpectating();
                Player.ChatMessage(Message("Notification.SpectateCycle", Player.userID));
            }

            public void FinishSpectating()
            {
                if (!Player.IsSpectating())
                    return;
                
                Player.SetParent(null, false, false);
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                Player.gameObject.SetLayerRecursive(17);
            }

            public void SetSpectateTarget(BaseEventPlayer eventPlayer)
            {
                SpectateTarget = eventPlayer;

                Event.BroadcastToPlayer(Player, $"Spectating: {eventPlayer.Player.displayName}");

                Player.SendEntitySnapshot(eventPlayer.Player);
                Player.gameObject.Identity();
                Player.SetParent(eventPlayer.Player, false, false);
            }

            public void UpdateSpectateTarget()
            {
                List<BaseEventPlayer> list = Pool.GetList<BaseEventPlayer>();

                Event.GetSpectateTargets(ref list);

                int newIndex = (int)Mathf.Repeat(_spectateIndex += 1, list.Count - 1);

                if (list[newIndex] != SpectateTarget)
                {
                    _spectateIndex = newIndex;
                    SetSpectateTarget(list[_spectateIndex]);
                }

                Pool.FreeList(ref list);
            }
        }
        
        /// <summary>
        /// Used for spectating only one person without room
        /// </summary>
        public class SpectatingBehaviour : MonoBehaviour
        {
            private BasePlayer Player { get; set; }
            internal BasePlayer SpectateTarget { get; private set; } = null;
            private int _spectateIndex = 0;

            public void Enable(BasePlayer target)
            {
                Player = GetComponent<BasePlayer>();
                NetworkGroupData data = Player.gameObject.AddComponent<NetworkGroupData>();
                data.Enable(target.net.group.ID);
                BeginSpectating(target);
            }
            void OnDestroy()
            {
                NetworkGroupData data;
                if(Player.TryGetComponent(out data))
                    DestroyImmediate(data);
                FinishSpectating();
            }

            private void BeginSpectating(BasePlayer player)
            {
                if (Player.IsSpectating())
                    return;
                SetSpectateTarget(player);
                Player.StartSpectating();
                Player.ChatMessage(Message("Notification.SpectateCycle", Player.userID));
            }

            private void FinishSpectating()
            {
                if (!Player.IsSpectating())
                    return;

                Player.SetParent(null, false, false);
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                Player.gameObject.SetLayerRecursive(17);
            }

            private void SetSpectateTarget(BasePlayer spectatedPlayer)
            {
                SpectateTarget = spectatedPlayer;
                Player.ChatMessage($"Spectating: {spectatedPlayer.displayName}");

                Player.SendEntitySnapshot(spectatedPlayer);
                Player.gameObject.Identity();
                Player.SetParent(spectatedPlayer, false, false);
            }
        }

        public class NetworkGroupData : MonoBehaviour
        {
            public uint groupID { get; set; }
            public void Enable(uint _groupID)
            {
                groupID = _groupID;
            }
        }

        public class CurrentEventInfo : MonoBehaviour
        {
            public BaseEventGame eventgame;
            internal void Enable(BaseEventGame _eventGame)
            {
                eventgame = _eventGame;
            }
            private void OnDestroy()
            {
                eventgame = null;
            }
        }
        public class StaticObject
        {
            public StaticObject(string _prefabName,Vector3 _position,Vector3 _eulerrotation,ulong _skinID = 0)
            {
                prefabName = _prefabName;
                position = _position;
                rotation = _eulerrotation;
                skinID = _skinID;
            }
            [JsonProperty("Prefab Full Name")]
            public string prefabName { get; set; }
            [JsonProperty("Position(Vector3)")]
            public Vector3 position { get; set; }
            [JsonProperty("Rotation")]
            public Vector3 rotation { get; set; }
            [JsonProperty("Skin ID")]
            public ulong skinID { get; set; }
            [JsonProperty("Additional Parameters")]
            public Hash<string, object> additionalParams = new Hash<string, object>();
            [JsonIgnore] 
            public BaseEntity staticEntity;

            public void FreeEntity()
            {
                staticEntity = null;
            }
            public BaseEntity Spawn()
            {
                var entity = GameManager.server.CreateEntity(prefabName, position, Quaternion.Euler(rotation));
                if (entity == null)
                {
                    Instance.PrintError("ArenaStaticObjects.json is corrupted");
                    return null;
                }
                if (entity is Door)
                {
                    (entity as Door).grounded = true;
                    (entity as Door).pickup.enabled = false;
                }
                if (entity is ElectricGenerator)
                {
                    ElectricGenerator generator = entity as ElectricGenerator;
                    generator.electricAmount = 9999999f;
                    generator.SendNetworkUpdate();
                }
                
                entity.skinID = skinID;
                entity.Spawn();
                staticEntity = entity;
                return entity;
            }

            public void SetElectricConnections(List<StaticObject> staticObjects, bool setwires = false)
            {
                object _object;
                additionalParams.TryGetValue(nameof(IOEntitySpecs), out _object);
                IOEntitySpecs ioSpecs = _object as IOEntitySpecs;
                if (ioSpecs == null) { 
                     Instance.PrintError("IOEntitySpecs couldnt be read. Error occured."); 
                     return;
                }
                
                IOEntity ioEntity = staticEntity as IOEntity;
                for (int i = 0; i < ioSpecs.inputs.Length; i++)
                {
                    IOEntitySpecs.IOSlotSpecs input = ioSpecs.inputs[i];
                    if(input == null)
                        continue;
                    StaticObject staticObject = staticObjects.FirstOrDefault(x =>
                        (x.additionalParams.ContainsKey(nameof(IOEntitySpecs)) &&
                            (x.additionalParams[nameof(IOEntitySpecs)] as IOEntitySpecs).ID == input.connectedTo));
                    if(staticObject == null)
                        continue;
                    ioEntity.inputs[i].connectedTo.Set(staticObject.staticEntity as IOEntity);
                    ioEntity.inputs[i].connectedToSlot = input.connectedToSlot;
                    ioEntity.inputs[i].connectedTo.Init();
                }
                for (int i = 0; i < ioSpecs.outputs.Length; i++)
                {
                    IOEntitySpecs.IOSlotSpecs output = ioSpecs.outputs[i];
                    if(output == null)
                        continue;
                    StaticObject staticObject = staticObjects.FirstOrDefault(x =>
                        (x.additionalParams.ContainsKey(nameof(IOEntitySpecs)) &&
                         (x.additionalParams[nameof(IOEntitySpecs)] as IOEntitySpecs).ID == output.connectedTo));
                    if(staticObject == null)
                        continue;
                    ioEntity.outputs[i].connectedTo.Set(staticObject.staticEntity as IOEntity);
                    ioEntity.outputs[i].connectedToSlot = output.connectedToSlot;

                    if (setwires)
                    {
                        ioEntity.outputs[i].linePoints = output.electricLines.linePoints;
                        ioEntity.outputs[i].wireColour = output.electricLines.wireColour;
                    }
                    ioEntity.outputs[i].connectedTo.Init();
                }
                ioEntity.MarkDirtyForceUpdateOutputs();
                ioEntity.SendNetworkUpdate();
            }
            public static List<StaticObject> ConvertToStaticObjects(List<BaseEntity> list)
            {
                List<StaticObject> staticsList = new List<StaticObject>();
                foreach (BaseEntity entity in list)
                {
                    StaticObject staticObject = new StaticObject(entity.PrefabName, entity.ServerPosition,
                        entity.ServerRotation.eulerAngles, entity.skinID);
                    
                    if (entity is IOEntity)
                    {
                        staticObject.additionalParams.Add(nameof(IOEntitySpecs),new IOEntitySpecs(entity as IOEntity));
                    }
                    staticsList.Add(staticObject);
                }
                return staticsList;
            }

            public class IOEntitySpecs
            {
                [JsonProperty("ID")]
                public uint ID { get; set; }
                [JsonProperty("Inputs")]
                public IOSlotSpecs[] inputs { get; set; }
                [JsonProperty("Outputs")]
                public IOSlotSpecs[] outputs { get; set; }

                public IOEntitySpecs (IOEntity ioEntity = null)
                {
                    if (ioEntity == null)
                        return;
                    ID = ioEntity.net.ID;
                    inputs = new IOSlotSpecs[ioEntity.inputs.Length];
                    outputs = new IOSlotSpecs[ioEntity.outputs.Length];
                    for (int i = 0; i < ioEntity.inputs.Length; i++)
                    {
                        IOEntity.IOSlot ioSlot= ioEntity.inputs[i];
                        if (ioSlot.connectedTo.Get(true) != null)
                            inputs[i] = new IOSlotSpecs(ioSlot);
                    }
                    for (int i = 0; i < ioEntity.outputs.Length; i++)
                    {
                        IOEntity.IOSlot ioSlot= ioEntity.outputs[i];
                        if(ioSlot.connectedTo.Get(true) != null)
                        outputs[i] = new IOSlotSpecs(ioSlot);
                    }
                }
                
                public class IOSlotSpecs 
                {
                    [JsonProperty("connectedTo")]
                    public uint connectedTo{ get; set; }
                    [JsonProperty("connectedToSlot")]
                    public int connectedToSlot{ get; set; }
                    [JsonProperty("Electric Lines")]
                    public ElectricLines electricLines { get; set; }

                    public IOSlotSpecs(IOEntity.IOSlot ioSlot = null)
                    {
                        if (ioSlot == null)
                            return;
                        connectedTo = ioSlot.connectedTo.Get(true).net.ID;
                        connectedToSlot = ioSlot.connectedToSlot;
                        electricLines = new ElectricLines(ioSlot);
                    }
                    public class ElectricLines
                    {
                        [JsonProperty("Wire colour")]
                        public WireTool.WireColour wireColour{ get; set; }
                        [JsonProperty("Electric Cable Line Points")]
                        public Vector3[] linePoints { get; set; }

                        public ElectricLines(IOEntity.IOSlot ioSlot = null)
                        {
                            if (ioSlot == null)
                                return;
                            wireColour = ioSlot.wireColour;
                            linePoints = ioSlot.linePoints;
                        }
                    }
                }
            }
        }
        
        public class MoveController : MonoBehaviour
        {
            public float _distance = 3f;
            public BasePlayer _player;
            BaseEntity _entity;
            public bool _holding = false;

            void Start()
            {
                _entity = gameObject.GetComponent<BaseEntity>();
                Activate();
            }

            void Update()
            {
                if (_holding)
                {
                    if (_entity == null || _player == null)
                        return;

                    _entity.transform.position = _player.transform.position + _player.eyes.HeadRay().direction * _distance;
                    _entity.transform.LookAt(_player.transform.position);
                    _entity.UpdateNetworkGroup();
                    _entity.SendNetworkUpdateImmediate();
                }
            }

            public void Activate()
            {
                if (_entity == null || _player == null)
                    return;

                _entity.transform.position = _player.transform.position + _player.eyes.BodyRay().direction * _distance;
                _entity.transform.LookAt(_player.transform.position);
                _entity.UpdateNetworkGroup();
                _entity.SendNetworkUpdateImmediate();

                _holding = true;
            }

            public void Deactivate()
            {
                _holding = false;
                _entity = null;
                _player = null;
                UnityEngine.Object.Destroy(this);
            }
        }

        #region Event Timer
        public class GameTimer
        {
            private BaseEventGame _owner = null;

            private string _message;
            private int _timeRemaining;
            private Action _callback;

            internal GameTimer(BaseEventGame owner)
            {
                _owner = owner;
            }
                        
            internal void StartTimer(int time, string message = "", Action callback = null)
            {
                this._timeRemaining = time;
                this._message = message;
                this._callback = callback;

                InvokeHandler.InvokeRepeating(_owner, TimerTick, 1f, 1f);
            }

            internal void StopTimer()
            {
                InvokeHandler.CancelInvoke(_owner, TimerTick);

                for (int i = 0; i < _owner?.eventPlayers?.Count; i++)                
                    _owner.eventPlayers[i].DestroyUI(EMInterface.UI_TIMER);                
            }

            private void TimerTick()
            {
                _timeRemaining--;
                if (_timeRemaining == 0)
                {
                    StopTimer();
                    _callback?.Invoke();
                }
                else UpdateTimer();                
            }

            private void UpdateTimer()
            {
                string clockTime = string.Empty;

                TimeSpan dateDifference = TimeSpan.FromSeconds(_timeRemaining);
                int hours = dateDifference.Hours;
                int mins = dateDifference.Minutes;
                int secs = dateDifference.Seconds;

                if (hours > 0)
                    clockTime = string.Format("{0:00}:{1:00}:{2:00}", hours, mins, secs);
                else clockTime = string.Format("{0:00}:{1:00}", mins, secs);

                CuiElementContainer container = UI.Container(EMInterface.UI_TIMER, "0.1 0.1 0.1 0.7", new UI4(0.46f, 0.92f, 0.54f, 0.95f), false, "Hud");

                UI.Label(container, EMInterface.UI_TIMER, clockTime, 14, UI4.Full);

                if (!string.IsNullOrEmpty(_message))
                    UI.Label(container, EMInterface.UI_TIMER, _message, 14, new UI4(-5f, 0f, -0.1f, 1), TextAnchor.MiddleRight);

                for (int i = 0; i < _owner.eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = _owner.eventPlayers[i];
                    if (eventPlayer == null)
                        continue;

                    eventPlayer.DestroyUI(EMInterface.UI_TIMER);
                    eventPlayer.AddUI(EMInterface.UI_TIMER, container);
                }               
            }            
        }
        #endregion

        #region Spawn Management
        internal class SpawnSelector
        {
            private List<Vector3> _defaultSpawns;
            private List<Vector3> _availableSpawns;

            internal SpawnSelector(string eventName, string spawnFile)
            {
                _defaultSpawns = Instance.Spawns.Call("LoadSpawnFile", spawnFile) as List<Vector3>;
                _availableSpawns = Pool.GetList<Vector3>();
                _availableSpawns.AddRange(_defaultSpawns);
            }

            internal Vector3 GetSpawnPoint()
            {
                Vector3 point = _availableSpawns.GetRandom();
                _availableSpawns.Remove(point);

                if (_availableSpawns.Count == 0)
                    _availableSpawns.AddRange(_defaultSpawns);

                return point;
            }

            internal Vector3 ReserveSpawnPoint(int index)
            {
                Vector3 reserved = _defaultSpawns[index];
                _defaultSpawns.RemoveAt(index);

                _availableSpawns.Clear();
                _availableSpawns.AddRange(_defaultSpawns);

                return reserved;
            }

            internal void Destroy()
            {
                Pool.FreeList(ref _availableSpawns);
            }
        }
        #endregion

        #region Event Config
        public class EventConfig
        {            
            public string EventName { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            
            public string ZoneID { get; set; } = string.Empty;
            [JsonIgnore]
            public bool HasStaticObjects => StaticObjects.ContainsKey(EventName);

            public int TimeLimit { get; set; }
            public int ScoreLimit { get; set; }
            public int MinimumPlayers { get; set; }
            public int MaximumPlayers { get; set; }

            public bool AllowClassSelection { get; set; }

            public TeamConfig TeamConfigA { get; set; } = new TeamConfig();
            public TeamConfig TeamConfigB { get; set; } = new TeamConfig();

            public Hash<string, object> AdditionalParams { get; set; } = new Hash<string, object>();

            public EventConfig() { }

            public EventConfig(string type, IEventPlugin eventPlugin)
            {
                this.EventType = type;
                this.Plugin = eventPlugin;

                if (eventPlugin.AdditionalParameters != null)
                {
                    for (int i = 0; i < eventPlugin.AdditionalParameters.Count; i++)
                    {
                        EventParameter eventParameter = eventPlugin.AdditionalParameters[i];

                        if (eventParameter.DefaultValue == null && eventParameter.IsList)
                            AdditionalParams[eventParameter.Field] = new List<string>();
                        else AdditionalParams[eventParameter.Field] = eventParameter.DefaultValue;
                    }
                }
            }

            public T GetParameter<T>(string key)
            {
                try
                {
                    object obj;
                    if (AdditionalParams.TryGetValue(key, out obj))
                        return (T)Convert.ChangeType(obj, typeof(T));
                }
                catch { }
                
                return default(T);
            }

            public string GetString(string fieldName)
            {
                switch (fieldName)
                {
                    case "teamASpawnfile":
                        return TeamConfigA.Spawnfile;
                    case "teamBSpawnfile":
                        return TeamConfigB.Spawnfile;
                    case "zoneID":
                        return ZoneID;
                    default:
                        object obj;
                        if (AdditionalParams.TryGetValue(fieldName, out obj) && obj is string)
                            return obj as string;
                        return null;
                }
            }

            public List<string> GetList(string fieldName)
            {
                switch (fieldName)
                {
                    case "teamAKits":
                        return TeamConfigA.Kits;
                    case "teamBKits":
                        return TeamConfigB.Kits;
                    default:
                        object obj;
                        if (AdditionalParams.TryGetValue(fieldName, out obj) && obj is List<string>)
                            return obj as List<string>;
                        return null;
                }
            }

            public class TeamConfig
            {
                public string Color { get; set; } = string.Empty;
                public string Spawnfile { get; set; } = string.Empty;
                public List<string> Kits { get; set; } = new List<string>();
                public string Clothing { get; set; } = string.Empty;
            }

            [JsonIgnore]
            public IEventPlugin Plugin { get; set; }
        }
        #endregion
        #endregion

        #region Rewards
        private void GiveReward(BaseEventPlayer baseEventPlayer, int amount)
        {
            switch (rewardType)
            {
                case RewardType.ServerRewards:
                    ServerRewards?.Call("AddPoints", baseEventPlayer.Player.UserIDString, amount);
                    break;
                case RewardType.Economics:
                    Economics?.Call("Deposit", baseEventPlayer.Player.UserIDString, (double)amount);
                    break;               
            }
        }
        #endregion

        #region Enums
        public enum RewardType { ServerRewards, Economics, Scrap }

        public enum EventStatus { Finished, Open, Prestarting, Started }
        
        public enum Team { A, B, None }
        public enum roomPlace { House, Hospital, Island }
        #endregion

        #region Helpers  
        private T ParseType<T>(string type)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), type, true);
            }
            catch
            {
                return default(T);
            }
        }
        
        // API for direct plugin hook calls ie. EventManager.Call("IsEventPlayer", player);
        private object IsEventPlayer(BasePlayer player) => GetUser(player) != null ? (object)true : null;

        // API for global plugin hook calls ie. Interface.Oxide.CallHook("isEventPlayer", player); Global hook calls can't start with a uppercase I
        private object isEventPlayer(BasePlayer player) => GetUser(player) != null ? (object)true : null;

        /// <summary>
        /// Get the BaseEventPlayer component on the specified BasePlayer
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        internal static BaseEventPlayer GetUser(BasePlayer player) => player?.GetComponent<BaseEventPlayer>();
       
        internal static List<BaseEntity> GetAllRoomEntities(BaseEventGame room)
        {
            List<BaseEntity> entities = Pool.GetList<BaseEntity>(); // make this list freed (pooling etc)
            entities.AddRange(room.roomobjects);
            entities.AddRange(room.spectators);
            foreach (BaseEventPlayer eventplayer in room.eventPlayers)
            {
                entities.Add(eventplayer.Player);
            }
            if(!entities.IsEmpty())
                return entities;
            return null;
        }

        /// <summary>
        /// Used for restoring player's collisions to its default
        /// </summary>
        /// <returns>Returns player and object colliders of lobby</returns>
        public static List<Collider> GetLobbyColliders()
        {
            List<Collider> colliders = Pool.GetList<Collider>();
            IEnumerable<BaseNetworkable> lobbyentities = BaseEntity.serverEntities.Where(x => GetRoomofPlayer(x as BasePlayer) == null && !x.gameObject.HasComponent<BaseEventObject>());
            foreach (BaseEntity entity in lobbyentities)
                colliders.AddRange(entity.gameObject.GetComponentsInChildren<Collider>(true));
            return colliders.Where(x => !(x is TerrainCollider)).ToList();
        }

        /// <summary>
        /// Get all players of any game room
        /// </summary>
        /// /// <returns>Returns all spectators and eventplayers as BasePlayer</returns>
        public static IList<BasePlayer> GetPlayersOfRoom(BaseEventGame room)
        {
            IList<BasePlayer> players = new List<BasePlayer>();
            for (int i = 0; i < room.spectators.Count; i++)
                players.Add(room.spectators[i]);
            room.eventPlayers.ForEach(x => players.Add(x.Player));
            return players;
        }

        public static IEnumerable<BaseEventPlayer> GetEventPlayersOfRoom(BaseEventGame room)
        {
            IList<BaseEventPlayer> list = new List<BaseEventPlayer>();
            foreach(BasePlayer player in room.spectators)
            {
                if(player.HasComponent<BaseEventPlayer>())
                    list.Add(player.GetComponent<BaseEventPlayer>());
            }
            foreach (BaseEventPlayer eventPlayer in room.eventPlayers)
                list.Add(eventPlayer);
            return list;
        }

        /// <summary>
        /// Teleport player to the specified position
        /// </summary>
        /// <param name="player"></param>
        /// <param name="destination"></param>
        /// <param name="sleep"></param>
        internal static void MovePosition(BasePlayer player, Vector3 destination, bool sleep)
        {
            if (player == null)
                return;
            if (player.isMounted)
                player.GetMounted().DismountPlayer(player, true);

            if (player.GetParentEntity() != null)
                player.SetParent(null);

            if (sleep)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.MovePosition(destination);
                player.UpdateNetworkGroup();
                player.StartSleeping();
                player.SendNetworkUpdateImmediate(false);
                player.ClearEntityQueue(null);
                player.ClientRPCPlayer(null, player, "StartLoading");
                player.SendFullSnapshot();
            }
            else
            {
                player.MovePosition(destination);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                player.SendNetworkUpdateImmediate();
                player.ClearEntityQueue(null);
            }
        }

        /// <summary>
        /// Lock the players inventory so they can't remove items
        /// </summary>
        /// <param name="player"></param>
        internal static void LockInventory(BasePlayer player)
        {
            if (player == null)
                return;

            if (!player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, true);
                player.inventory.SendSnapshot();
            }
        }

        /// <summary>
        /// Unlock the players inventory
        /// </summary>
        /// <param name="player"></param>
        internal static void UnlockInventory(BasePlayer player)
        {
            if (player == null)
                return;

            if (player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, false);
                player.inventory.SendSnapshot();
            }
        }

        /// <summary>
        /// Removes all items from the players inventory
        /// </summary>
        /// <param name="player"></param>
        internal static void StripInventory(BasePlayer player)
        {
            Item[] allItems = player.inventory.AllItems();

            for (int i = allItems.Length - 1; i >= 0; i--)
            {
                Item item = allItems[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        /// <summary>
        /// Reset the players health and metabolism
        /// </summary>
        /// <param name="player"></param>
        internal static void ResetMetabolism(BasePlayer player)
        {
            player.health = player.MaxHealth();

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

            player.metabolism.calories.value = player.metabolism.calories.max;
            player.metabolism.hydration.value = player.metabolism.hydration.max;
            player.metabolism.heartrate.Reset();

            player.metabolism.bleeding.value = 0;
            player.metabolism.radiation_level.value = 0;
            player.metabolism.radiation_poison.value = 0;
            player.metabolism.SendChangesToClient();            
        }

        /// <summary>
        /// Gives the player the specified kit
        /// </summary>
        /// <param name="player"></param>
        /// <param name="kitname"></param>
        internal static void GiveKit(BasePlayer player, string kitname) => Instance.Kits?.Call("GiveKit", player, kitname);

        /// <summary>
        /// Nullifies damage being dealt
        /// </summary>
        /// <param name="hitInfo"></param>
        internal static void ClearDamage(HitInfo hitInfo)
        {
            if (hitInfo == null)
                return;

            hitInfo.damageTypes.Clear();
            hitInfo.HitEntity = null;
            hitInfo.HitMaterial = 0;
            hitInfo.PointStart = Vector3.zero;
        }

        /// <summary>
        /// Resets the player so they have max health and are visible to other players
        /// </summary>
        /// <param name="player"></param>
        internal static void ResetPlayer(BasePlayer player)
        {
            if (player == null)
                return;

            if (player.IsSpectating())      //Finish Spectating
            {
                player.SetParent(null, false, false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                player.gameObject.SetLayerRecursive(17);
            }

            player.EnablePlayerCollider();

            player.health = player.MaxHealth();

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

            player.SendFullSnapshot();
        }
        internal static void ResetPlayer(BaseEventPlayer eventPlayer)
        {
            if (eventPlayer == null)
                return;

            eventPlayer.Player.EnablePlayerCollider();

            eventPlayer.Player.health = eventPlayer.Player.MaxHealth();

            eventPlayer.Player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

            eventPlayer.IsDead = false;

            eventPlayer.AddToNetwork();
        }

        /// <summary>
        /// Respawn the player if they are dead
        /// </summary>
        /// <param name="eventPlayer"></param>
        internal static void RespawnPlayer(BaseEventPlayer eventPlayer)
        {
            if (!eventPlayer.IsDead)
                return;

            //eventPlayer.DestroyUI(EMInterface.UI_DEATH);
            //eventPlayer.DestroyUI(EMInterface.UI_RESPAWN);            

            ResetPlayer(eventPlayer);
            BaseEventGame room = GetRoomofPlayer(eventPlayer.Player);
            room.OnPlayerRespawn(eventPlayer);
        }

        /// <summary>
        /// Strip's clan tags out of a player display name
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        internal static string StripTags(string str)
        {
            if (str.StartsWith("[") && str.Contains("]") && str.Length > str.IndexOf("]"))
                str = str.Substring(str.IndexOf("]") + 1).Trim();

            if (str.StartsWith("[") && str.Contains("]") && str.Length > str.IndexOf("]"))
                StripTags(str);

            return str;
        }

        /// <summary>
        /// Trim's a player's display name to the specified size
        /// </summary>
        /// <param name="str"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        internal static string TrimToSize(string str, int size = 18)
        {
            if (str.Length > size)
                str = str.Substring(0, size);
            return str;
        }
        #endregion

        #region Zone Management
        private void OnExitZone(string zoneId, BasePlayer player)
        {
            if (player == null)
                return;

            BaseEventPlayer eventPlayer = GetUser(player);

            if (eventPlayer == null || eventPlayer.IsDead)
                return;
            BaseEventGame room = GetRoomofPlayer(player);
            if (room != null && zoneId == room.Config.ZoneID)            
                eventPlayer.IsOutOfBounds = true;
        }

        private void OnEnterZone(string zoneId, BasePlayer player)
        {           
            BaseEventPlayer eventPlayer = GetUser(player);

            if (eventPlayer == null || eventPlayer.IsDead)
                return;
            BaseEventGame room = GetRoomofPlayer(player);
            if (room != null && zoneId == room.Config.ZoneID)            
                eventPlayer.IsOutOfBounds = false;   
        }
        #endregion
        
        #region Static Object Handler

        private static Dictionary<ulong,string> StaticObjectCreator = new Dictionary<ulong,string>();//working on arena
        private static Dictionary<BasePlayer,BaseEntity> holdedBaseEntity = new Dictionary<BasePlayer, BaseEntity>();
        [ChatCommand("staticobject")]
        void cmdStaticObject(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("You dont have the permission");
                return;
            }

            if(args.Length == 0)
            {
                if (StaticObjectCreator.ContainsKey(player.userID))
                {
                    player.ChatMessage($"Selected Arena: {StaticObjectCreator[player.userID]} \nAvailable commands: " +
                                       $"\n/staticobject create <Prefab full name> =>Create prefab with specific shortname" +
                                       $"\n/staticobject save => Add created object to arena config" +
                                       $"\n/staticobject substitute <newprefabName> => substitute looked prefab with new prefab with same location");
                }
                player.ChatMessage("/staticobject addzoneentities <arenaName> Saves all zone entities of relevant eventConfig for spawning staticobjects per room");
                return;
            }
            else if(args.Length >= 1)
            {
                switch (args[0])
                {
                    
                    case "create":
                        if(!StaticObjectCreator.ContainsKey(player.userID))
                        {
                            player.ChatMessage("You havent selected arena");
                            return;
                        }
                        if (holdedBaseEntity.ContainsKey(player))
                        {
                            player.ChatMessage("You still have object on your hand");
                            return;
                        }

                        Vector3 entityposition = player.transform.position + player.eyes.transform.forward * 1.5f;
                        var entity = GameManager.server.CreateEntity(args[1], entityposition, Quaternion.Euler(0, 0, 0));
                        if(entity == null)
                        {
                            player.ChatMessage("Prefab with given short name is not found");
                            return;
                        }
                        var controllercomponent =entity.gameObject.AddComponent<MoveController>();
                        holdedBaseEntity.Add(player, entity);
                        if(entity is Door)
                        {
                            (entity as Door).grounded = true;
                        }
                        entity.Spawn();
                        if(controllercomponent != null)
                        {
                            controllercomponent._player = player;
                        }
                        AdminEditStaticsRoom adminsStaticsRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                        adminsStaticsRoom.AddStaticEntityToRoom(entity);
                        return;
                    case "save":
                        if (holdedBaseEntity.ContainsKey(player))
                        {
                            AdminEditStaticsRoom editRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                            BaseEntity baseentity = holdedBaseEntity[player];
                            var component = baseentity.GetComponent<MoveController>();
                            component.Deactivate();
                            player.ChatMessage($"'{baseentity.ShortPrefabName}'({editRoom.staticobjects.Count}) added to {editRoom.eventConfig.EventName}");
                            holdedBaseEntity.Remove(player);
                            baseentity.SendNetworkUpdate();
                        }
                        else if (AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(player))
                        {
                            AdminEditStaticsRoom admineditRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                            if (StaticObjects.ContainsKey(StaticObjectCreator[player.userID]))
                                StaticObjects.Remove(StaticObjectCreator[player.userID]);
                            StaticObjects.Add(StaticObjectCreator[player.userID], StaticObject.ConvertToStaticObjects(admineditRoom.staticobjects));
                            SaveStaticObjectsData();
                            player.ChatMessage($"{admineditRoom.staticobjects.Count} entities saved into {admineditRoom.eventConfig.EventName} arena");
                        }
                        return;
                    case "cancel":
                        if (holdedBaseEntity.ContainsKey(player))
                        {
                            AdminEditStaticsRoom admineditstaticRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                            admineditstaticRoom.RemoveEntity(holdedBaseEntity[player]);
                            holdedBaseEntity.Remove(player);
                            
                            player.ChatMessage("Canceled adding selected baseentity");
                            return;
                        }
                        player.ChatMessage("No operation to cancel");
                        return;
                    case "addzoneentities":
                        if(args.Length != 2)
                        {
                            player.ChatMessage("Correct format: /staticobject addzoneentities <arenaName>");
                            return;
                        }

                        EventConfig eventData;
                        Events.events.TryGetValue(args[1], out eventData);
                        if (eventData == null)
                        {
                            player.ChatMessage("Event data with given name is not found.");
                            return;
                        }
                        if (ZoneManager.Call("CheckZoneID", eventData.ZoneID) == null)
                        {
                            player.ChatMessage("Zone of event config is not found. Check event configs and try again.");
                            return;
                        }
                        List<BaseEntity> entities = ZoneManager.Call("GetEntitiesInZone",eventData.ZoneID) as List<BaseEntity>;
                        if (StaticObjects.ContainsKey(args[1]))
                            StaticObjects.Remove(args[1]);
                        StaticObjects.Add(args[1], StaticObject.ConvertToStaticObjects(entities));
                        player.ChatMessage($"Entities of relevant zoneID has been added into oxide/data/{Name}/staticobjects.json");
                        SaveStaticObjectsData();
                        break;
                    case "remove":
                        if (!AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(player))
                        {
                            player.ChatMessage("First, select an arena from admin menu to edit");
                            return;
                        }
                        RaycastHit hit;
                        Physics.Raycast(player.eyes.HeadRay(), out hit);
                        BaseEntity removedEntity = hit.GetEntity();
                        if (removedEntity == null)
                        {
                            player.ChatMessage("Look to an entity in order to delete");
                            return;
                        }

                        AdminEditStaticsRoom adminRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                        if (!adminRoom.RemoveEntity(removedEntity))
                        {
                            player.ChatMessage("This entity is not specified in the arena you are editing.");
                            return;
                        }
                        break;
                    case "substitute":
                        if (AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(player))
                        {
                            AdminEditStaticsRoom admineditRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
                            RaycastHit hit2;
                            Physics.Raycast(player.eyes.HeadRay(), out hit2);
                            BaseEntity substitutedentity = hit2.GetEntity();
                            Vector3 pos = substitutedentity.ServerPosition;
                            Quaternion rot = substitutedentity.ServerRotation;
                            var ent = GameManager.server.CreateEntity(args[1], pos, rot);
                            ent.Spawn();
                            admineditRoom.RemoveEntity(substitutedentity);
                            admineditRoom.AddStaticEntityToRoom(ent);
                            player.ChatMessage(substitutedentity.ShortPrefabName + " is substituted with " + ent.ShortPrefabName + " successfully.");
                        }
                        break;
                    default:
                        player.ChatMessage("Enter valid parameter");
                        return;
                }
            }
        }

        [ConsoleCommand("emui.editstaticobjects")]
        void cmdEditstaticObjects(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
                return;
            if (AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(player))
                return;
            if (GetRoomofPlayer(player) != null)
            {
                player.ChatMessage("Leave game room and try again.");
                return;
            }
            AdminEditStaticsRoom adminRoom = new AdminEditStaticsRoom();
            adminRoom.StartEditing(arg);
        }
        
        [ConsoleCommand("emui.finisheditingstaticobjects")]
        void cmdFinishEditingstaticObjects(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
                return;
            if (!AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(player))
                return;
            AdminEditStaticsRoom adminRoom = AdminEditStaticsRoom.GetEditRoomofPlayer(player);
            adminRoom.FinishEditing(player,true);
        }

        private const string admineditroom_UI = "eventmanager.admineditroom.ui";
        public class AdminEditStaticsRoom
        {
            List<Collider> roomcolliders = Pool.GetList<Collider>();
            public List<BaseEntity> staticobjects = Pool.GetList<BaseEntity>();
            private uint roomID;
            public EventConfig eventConfig;
            public List<BasePlayer> editorPlayers = new List<BasePlayer>();
            public bool canEntitiesBeRemoved { get; private set; } = false;

            public AdminEditStaticsRoom()
            {
                roomID = Convert.ToUInt32(Instance.RandomRoomID());
                AdminEditingRooms.Add(Convert.ToInt32(roomID),this);
            }

            public void Destroy()
            {
                foreach (BasePlayer player in editorPlayers.ToList())
                {
                    DestroyAdminUI(player);
                    FinishEditing(player,false);
                }
                Pool.FreeList(ref staticobjects);
                Pool.FreeList(ref roomcolliders);
            }

            public void StartEditing(ConsoleSystem.Arg args)
            {
                string gamemodeName = args.GetString(0);
                BasePlayer player = args.Player();
                if (player == null)
                    return;
                if (StaticObjectCreator.ContainsKey(player.userID))
                {
                    player.ChatMessage($"You are already editing static objects of arena : {StaticObjectCreator[player.userID]}");
                    return;
                }
                eventConfig = Instance.Events.events[gamemodeName];
                StaticObjectCreator.Add(player.userID, eventConfig.EventName);

                Network.Visibility.Group group = Net.sv.visibility.Get(roomID);
                player.gameObject.AddComponent<NetworkGroupData>().Enable(roomID);
                player.net.SwitchGroup(group);

                player.SendFullSnapshot();
                player.UpdateNetworkGroup();
                player.SendNetworkUpdateImmediate(false);
                player.OnNetworkGroupChange();              // Updates children objects of main BaseNetworkable
                player.SendChildrenNetworkUpdateImmediate();
                player.SendEntityUpdate();

                object objectspawn = Instance.Spawns.Call("GetRandomSpawn", gamemodeName);
                Vector3 randomspawn = (Vector3)objectspawn; 
                MovePosition(player, randomspawn, false);
                SpawnStaticEntities();
                
                editorPlayers.Add(player);
                SendAdminUI(player);
                
                StripInventory(player);
                GiveKit(player,"EditStaticObjectsKit");
            }

            public void FinishEditing(BasePlayer player,bool save)
            {
                if (save)
                {
                    if (StaticObjects.ContainsKey(eventConfig.EventName))
                        StaticObjects.Remove(eventConfig.EventName);
                    StaticObjects.Add(eventConfig.EventName, StaticObject.ConvertToStaticObjects(staticobjects));
                    Instance.SaveStaticObjectsData();
                }
                Instance.LobbyRoomSystem?.Call("SpawnInLobby", player);

                editorPlayers.Remove(player);
                UnityEngine.Object.DestroyImmediate(player.gameObject.GetComponent<NetworkGroupData>());
                canEntitiesBeRemoved = true;
                foreach (BaseEntity entity in staticobjects.ToList())
                {
                    entity.Kill();
                }
                canEntitiesBeRemoved = false;
                StaticObjectCreator.Remove(player.userID);
                Pool.FreeList(ref staticobjects);
                Pool.FreeList(ref roomcolliders);
                AdminEditingRooms.Remove(Convert.ToInt32(roomID));
                DestroyAdminUI(player);
            }
            public void AddStaticEntityToRoom(BaseEntity staticentity)
            {
                staticobjects.Add(staticentity);
                staticentity.gameObject.AddComponent<NetworkGroupData>().Enable(roomID);
                staticentity.net.SwitchGroup(Net.sv.visibility.Get(roomID));
                roomcolliders.AddRange(staticentity.GetComponentsInChildren<Collider>(true));
                    
                List<Collider> otherColliders = Pool.GetList<Collider>();
                otherColliders = AllEventColliders;
                foreach (Collider collider in roomcolliders)
                    if (otherColliders.Contains(collider))
                        otherColliders.Remove(collider);

                foreach (Collider colliderinroom in roomcolliders)
                {
                    foreach (Collider othercollider in otherColliders)
                    {
                        if (othercollider != null && colliderinroom != null)
                            Physics.IgnoreCollision(othercollider, colliderinroom, true);
                    }
                }
                Pool.FreeList(ref otherColliders);
                staticentity.SendNetworkUpdateImmediate();
                RefreshUIForAll();
            }
            void SpawnStaticEntities()
            {
                List<StaticObject> staticObjects;
                StaticObjects.TryGetValue(eventConfig.EventName, out staticObjects);
                if (staticObjects == null)
                {
                    SendNotification("No entities found in the config.");
                    return;
                }
                
                foreach(StaticObject staticobject in StaticObjects[eventConfig.EventName])
                {
                    var entity = staticobject.Spawn();
                    Instance.NextFrame(() => AddStaticEntityToRoom(entity));
                }
                //Setting connections between electrical devices
                foreach (StaticObject iostaticentity in StaticObjects[eventConfig.EventName]
                             .Where(x => x.staticEntity is IOEntity))
                {
                    iostaticentity.SetElectricConnections(StaticObjects[eventConfig.EventName], true);
                }
                //Set whatever relationships you set here!

                foreach (StaticObject staticObject in StaticObjects[eventConfig.EventName])
                    staticObject.FreeEntity();
                
            }
            public bool RemoveEntity(BaseEntity entity)
            {
                if (staticobjects.Contains(entity))
                {
                    canEntitiesBeRemoved = true;
                    staticobjects.Remove(entity);
                    entity.Kill();
                    canEntitiesBeRemoved = false;
                    RefreshUIForAll();
                    return true;
                }
                return false;
            }

            void SendAdminUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, admineditroom_UI);
                CuiElementContainer container = UI.Container(admineditroom_UI, "0 0 0 0",
                    UI.TransformToUI4(1550f, 1920f, 854f, 1055f));
                string info = $"--- Editing ({eventConfig.EventType}) Static Objects ---\n" +
                              $"Room ID: {roomID}   Editing Arena: {eventConfig.EventName}\n" +
                              $"Static Object Count: {staticobjects.Count}   Admins in Room : {editorPlayers.Count}\n" +
                              $"ZoneID: {eventConfig.ZoneID}";
                UI.Label(container,admineditroom_UI,info,12,UI4.Full,TextAnchor.UpperLeft,UI.Color("#cccccc",1f));
                CuiHelper.AddUi(player, container);
            }

            void DestroyAdminUI(BasePlayer player) => CuiHelper.DestroyUi(player, admineditroom_UI);

            void RefreshUIForAll()
            {
                foreach (BasePlayer player in editorPlayers)
                    SendAdminUI(player);
            }

            public static AdminEditStaticsRoom GetEditRoomofPlayer(BasePlayer player)
            {
                foreach (AdminEditStaticsRoom room in AdminEditingRooms.Values)
                {
                    if (room.editorPlayers.Contains(player))
                        return room;
                }
                return null;
            }
            public static bool IsAdminAlreadyEditingRoom(BasePlayer player)
            {
                if (AdminEditingRooms == null)
                    return false;
                foreach (AdminEditStaticsRoom adminEditStaticsRoom in AdminEditingRooms.Values)
                {
                    if (adminEditStaticsRoom.editorPlayers.Contains(player))
                        return true;
                }
                return false;
            }
            public void SendNotification(string msg)
            {
                foreach (BasePlayer player in editorPlayers)
                {
                    player.ChatMessage(msg);
                }
            }
        }
        #endregion

        #region File Validation
        internal object ValidateEventConfig(EventConfig eventConfig)
        {
            IEventPlugin plugin;

            if (string.IsNullOrEmpty(eventConfig.EventType) || !EventModes.TryGetValue(eventConfig.EventType, out plugin))
                return string.Concat("Event mode ", eventConfig.EventType, " is not currently loaded");

            if (!plugin.CanUseClassSelector && eventConfig.TeamConfigA.Kits.Count == 0)
                return "You must set atleast 1 kit";

            if (eventConfig.MinimumPlayers == 0)
                return "You must set the minimum players";

            if (eventConfig.MaximumPlayers == 0)
                return "You must set the maximum players";

            if (plugin.RequireTimeLimit && eventConfig.TimeLimit == 0)
                return "You must set a time limit";

            if (plugin.RequireScoreLimit && eventConfig.ScoreLimit == 0)
                return "You must set a score limit";

            object success;

            foreach (string kit in eventConfig.TeamConfigA.Kits)
            {
                success = ValidateKit(kit);
                if (success is string)
                    return $"Invalid kit: {kit}";
            }
            
            success = ValidateSpawnFile(eventConfig.TeamConfigA.Spawnfile);
            if (success is string)
                return $"Invalid spawn file: {eventConfig.TeamConfigA.Spawnfile}";

            if (plugin.IsTeamEvent)
            {
                success = ValidateSpawnFile(eventConfig.TeamConfigB.Spawnfile);
                if (success is string)
                    return $"Invalid second spawn file: {eventConfig.TeamConfigB.Spawnfile}";

                if (eventConfig.TeamConfigB.Kits.Count == 0)
                    return "You must set atleast 1 kit for Team B";

                foreach (string kit in eventConfig.TeamConfigB.Kits)
                {
                    success = ValidateKit(kit);
                    if (success is string)
                        return $"Invalid kit: {kit}";
                }
            }

            success = ValidateZoneID(eventConfig.ZoneID);
            if (success is string)
                return $"Invalid zone ID: {eventConfig.ZoneID}";

            for (int i = 0; i < plugin.AdditionalParameters?.Count; i++)
            {
                EventParameter eventParameter = plugin.AdditionalParameters[i];

                if (eventParameter.IsRequired)
                {
                    object value;
                    eventConfig.AdditionalParams.TryGetValue(eventParameter.Field, out value);

                    if (value == null)
                        return $"Missing event parameter: ({eventParameter.DataType}){eventParameter.Field}";
                    else
                    {
                        success = plugin.ParameterIsValid(eventParameter.Field, value);
                        if (success is string)
                            return (string)success;
                    }
                }
            }

            return null;
        }

        internal object ValidateSpawnFile(string name)
        {
            object success = Spawns?.Call("GetSpawnsCount", name);
            if (success is string)
                return (string)success;
            return null;
        }

        internal object ValidateZoneID(string name)
        {
            object success = ZoneManager?.Call("CheckZoneID", name);
            if (name is string && !string.IsNullOrEmpty((string)name))
                return null;
            return $"Zone \"{name}\" does not exist!";
        }

        internal object ValidateKit(string name)
        {
            object success = Kits?.Call("isKit", name);
            if ((success is bool))
            {
                if (!(bool)success)
                    return $"Kit \"{name}\" does not exist!";
            }
            return null;
        }
        #endregion

        #region Scoring
        public struct ScoreEntry
        {
            internal int position;
            internal string displayName;
            internal float value1;
            internal float value2;
            internal Team team;

            internal ScoreEntry(BaseEventPlayer eventPlayer, int position, float value1, float value2)
            {
                this.position = position;
                this.displayName = StripTags(eventPlayer.Player.displayName);
                this.team = eventPlayer.Team;
                this.value1 = value1;
                this.value2 = value2;
            }

            internal ScoreEntry(BaseEventPlayer eventPlayer, float value1, float value2)
            {
                this.position = 0;
                this.displayName = StripTags(eventPlayer.Player.displayName);
                this.team = eventPlayer.Team;
                this.value1 = value1;
                this.value2 = value2;
            }

            internal ScoreEntry(float value1, float value2)
            {
                this.position = 0;
                this.displayName = string.Empty;
                this.team = Team.None;
                this.value1 = value1;
                this.value2 = value2;
            }
        }

        public class EventResults
        {
            public string EventName { get; private set; }

            public string EventType { get; private set; }

            public ScoreEntry TeamScore { get; private set; }

            public IEventPlugin Plugin { get; private set; }

            public List<ScoreEntry> Scores { get; private set; } = new List<ScoreEntry>();

            public bool IsValid => Plugin != null;

            public void UpdateFromEvent(BaseEventGame baseEventGame)
            {
                EventName = baseEventGame.Config.EventName;
                EventType = baseEventGame.Config.EventType;
                Plugin = baseEventGame.Plugin;

                if (Plugin.IsTeamEvent)
                    TeamScore = new ScoreEntry(baseEventGame.GetTeamScore(Team.A), baseEventGame.GetTeamScore(Team.B));
                else TeamScore = default(ScoreEntry);

                Scores.Clear();

                if (baseEventGame.scoreData.Count > 0)
                    Scores.AddRange(baseEventGame.scoreData);
            }
        }
        #endregion

        #region Commands
        [ChatCommand("adminmenu")]
        private void cmdEvent(BasePlayer player, string command, string[] args)
        {
            if(permission.UserHasPermission(player.UserIDString,ADMIN_PERMISSION))
                EMInterface.Instance.OpenMenu(player, new EMInterface.MenuArgs(EMInterface.MenuTab.Event));
        }
        #endregion

        #region Config
        public class ConfigData
        {
            [JsonProperty(PropertyName = "Auto-Event Options")]
            public AutoEventOptions AutoEvents { get; set; }

            [JsonProperty(PropertyName = "Event Options")]
            public EventOptions Event { get; set; }

            [JsonProperty(PropertyName = "Reward Options")]
            public RewardOptions Reward { get; set; }

            [JsonProperty(PropertyName = "Timer Options")]
            public TimerOptions Timer { get; set; }

            [JsonProperty(PropertyName = "Message Options")]
            public MessageOptions Message { get; set; }

            [JsonProperty(PropertyName = "Lobby Spawn Filename")]
            public string lobbyspawnfilename { get; set; }
            [JsonProperty(PropertyName = "System Announcer Label")]
            public string announcerlabel { get; set; }

            public class EventOptions
            {      
                [JsonProperty(PropertyName = "Blacklisted commands for event players")]
                public string[] CommandBlacklist { get; set; }
            }

            public class RewardOptions
            {                
                [JsonProperty(PropertyName = "Amount rewarded for kills")]
                public int KillAmount { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for wins")]
                public int WinAmount { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for headshots")]
                public int HeadshotAmount { get; set; }

                [JsonProperty(PropertyName = "Reward type (ServerRewards, Economics, Scrap)")]
                public string Type { get; set; }
            }

            public class TimerOptions
            {
                [JsonProperty(PropertyName = "Match start timer (seconds)")]
                public int Start { get; set; }

                [JsonProperty(PropertyName = "Match pre-start timer (seconds)")]
                public int Prestart { get; set; }

                [JsonProperty(PropertyName = "Backpack despawn timer (seconds)")]
                public int Bag { get; set; }
            }

            public class MessageOptions
            {
                [JsonProperty(PropertyName = "Announce events when one opens")]
                public bool Announce { get; set; }

                [JsonProperty(PropertyName = "Event announcement interval (seconds)")]
                public int AnnounceInterval { get; set; }

                [JsonProperty(PropertyName = "Broadcast when a player joins an event to chat")]
                public bool BroadcastJoiners { get; set; }

                [JsonProperty(PropertyName = "Broadcast when a player leaves an event to chat")]
                public bool BroadcastLeavers { get; set; }

                [JsonProperty(PropertyName = "Broadcast the name(s) of the winning player(s) to chat")]
                public bool BroadcastWinners { get; set; }

                [JsonProperty(PropertyName = "Broadcast kills to chat")]
                public bool BroadcastKills { get; set; }  

                [JsonProperty(PropertyName = "Chat icon Steam ID")]
                public ulong ChatIcon { get; set; }
            }

            public class AutoEventOptions
            {
                [JsonProperty(PropertyName = "Enable auto-events")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "List of event configs to run through")]
                public string[] Events { get; set; }

                [JsonProperty(PropertyName = "Randomize auto-event selection")]
                public bool Randomize { get; set; }

                [JsonProperty(PropertyName = "Auto-event interval (seconds)")]
                public int Interval { get; set; }
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                AutoEvents = new ConfigData.AutoEventOptions
                {
                    Enabled = false,
                    Events = new string[0],
                    Randomize = false,
                    Interval = 3600
                },
                Event = new ConfigData.EventOptions
                {
                    CommandBlacklist = new string[] { "s", "tp" }
                },
                Message = new ConfigData.MessageOptions
                {
                    Announce = true,
                    AnnounceInterval = 120,
                    BroadcastJoiners = true,
                    BroadcastLeavers = true,
                    BroadcastWinners = true,
                    BroadcastKills = true,
                    ChatIcon = 76561198199395155
                },
                Reward = new ConfigData.RewardOptions
                {
                    KillAmount = 1,
                    WinAmount = 5,
                    HeadshotAmount = 2,
                    Type = "Scrap"
                },
                Timer = new ConfigData.TimerOptions
                {
                    Start = 60,
                    Prestart = 10,
                    Bag = 30
                },
                lobbyspawnfilename = string.Empty,
                announcerlabel = "<color=#d10000>System </color><color=#787878>| </color>",
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (Configuration.Version < new VersionNumber(4, 0, 1))
                Configuration.AutoEvents.Interval = baseConfig.AutoEvents.Interval;

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion

        #region Data Management
        internal void SaveEventData() => eventData.WriteObject(Events);
        internal void SaveStaticObjectsData()
        {
            staticobjectsData.WriteObject(StaticObjects);
        }

        private void LoadData()
        {
            try
            {
                Events = eventData.ReadObject<EventData>();
            }
            catch(Exception e)
            {
                Events = new EventData();
                PrintWarning("Event configs('event_data.json') couldnt be loaded. Restoring 'event_data.json'" +
                             "\n" + e.Message);
            }
        }
        private void LoadStaticObjectsData()
        {
            try
            {
                staticobjectsData.Settings.NullValueHandling = NullValueHandling.Ignore;
                staticobjectsData.Settings.MissingMemberHandling = MissingMemberHandling.Ignore;
                StaticObjects = staticobjectsData.ReadObject<Dictionary<string, List<StaticObject>>>();
                //Converted JObjects to specific classes otherwise we cant access additional params via jObjects
                foreach (List<StaticObject> arenaEntities in StaticObjects.Values)
                {
                    foreach (StaticObject staticObject in arenaEntities)
                    {
                        if (staticObject.additionalParams.ContainsKey(nameof(StaticObject.IOEntitySpecs)))
                        {
                            object _object;
                            staticObject.additionalParams.TryGetValue(nameof(StaticObject.IOEntitySpecs), out _object);
                            JObject jobject = _object as JObject;
                            StaticObject.IOEntitySpecs ioSpecs = jobject.ToObject<StaticObject.IOEntitySpecs>();
                            staticObject.additionalParams[nameof(StaticObject.IOEntitySpecs)] = ioSpecs;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PrintError("Static Objects Data couldnt be read. Check json file to edit static objects with admin tool\n" +
                           e.Message);
            }
        }

        public static void SaveEventConfig(EventConfig eventConfig)
        {
            Instance.Events.events[eventConfig.EventName] = eventConfig;
            Instance.SaveEventData();
            Broadcast("Event Saved");
        }

        public class EventData
        {
            public Hash<string, EventConfig> events = new Hash<string, EventConfig>();
        }

        public class EventParameter
        {
            public string Name; // The name shown in the UI
            public InputType Input; // The type of input used to select the value in the UI

            public string Field; // The name of the custom field stored in the event config
            public string DataType; // The type of the field (string, int, float, bool, List<string>)

            public bool IsRequired; // Is this field required to complete event creation?

            public string SelectorHook; // The hook that is called to gather the options that can be selected. This should return a string[] (ex. GetZoneIDs from ZoneManager, GetAllKits from Kits)
            public bool SelectMultiple; // Allows the user to select multiple elements when using the selector

            public object DefaultValue; // Set the default value for this field

            [JsonIgnore]
            public bool IsList => Input == InputType.Selector && DataType.Equals("List<string>", StringComparison.OrdinalIgnoreCase);
            
            public enum InputType { InputField, Toggle, Selector }
        }

        #region Player Restoration
        internal void RestorePlayer(BasePlayer player)
        {
            object result = Spawns.Call("GetRandomSpawn", Configuration.lobbyspawnfilename);
            if(result is string)
            {
                PrintError("Couldnt get lobby spawnfile");
                return;
            }
            Vector3 spawnpoint = (Vector3)result;

            StripInventory(player);
            player.metabolism.Reset();

            if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                Instance.timer.Once(1, () => RestorePlayer(player));
                return;
            }
            MovePosition(player, spawnpoint, true);
            LobbyRoomSystem.CallHook("GiveLobbyCostume", player);
        }
        #endregion

        #region Serialized Items
        internal static Item CreateItem(ItemData itemData)
        {
            Item item = ItemManager.CreateByItemID(itemData.itemid, itemData.amount, itemData.skin);
            item.condition = itemData.condition;
            item.maxCondition = itemData.maxCondition;

            if (itemData.frequency > 0)
            {
                ItemModRFListener rfListener = item.info.GetComponentInChildren<ItemModRFListener>();
                if (rfListener != null)
                {
                    PagerEntity pagerEntity = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity) as PagerEntity;
                    if (pagerEntity != null)
                    {
                        pagerEntity.ChangeFrequency(itemData.frequency);
                        item.MarkDirty();
                    }
                }
            }

            if (itemData.instanceData?.IsValid() ?? false)
                itemData.instanceData.Restore(item);

            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon != null)
            {
                if (!string.IsNullOrEmpty(itemData.ammotype))
                    weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(itemData.ammotype);
                weapon.primaryMagazine.contents = itemData.ammo;
            }

            FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
            if (flameThrower != null)
                flameThrower.ammo = itemData.ammo;

            if (itemData.contents != null)
            {
                foreach (ItemData contentData in itemData.contents)
                {
                    Item newContent = ItemManager.CreateByItemID(contentData.itemid, contentData.amount);
                    if (newContent != null)
                    {
                        newContent.condition = contentData.condition;
                        newContent.MoveToContainer(item.contents);
                    }
                }
            }
            return item;
        }

        internal static ItemData SerializeItem(Item item)
        {
            return new ItemData
            {
                itemid = item.info.itemid,
                amount = item.amount,
                ammo = item.GetHeldEntity() is BaseProjectile ? (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents :
                               item.GetHeldEntity() is FlameThrower ? (item.GetHeldEntity() as FlameThrower).ammo : 0,
                ammotype = (item.GetHeldEntity() as BaseProjectile)?.primaryMagazine.ammoType.shortname ?? null,
                position = item.position,
                skin = item.skin,
                condition = item.condition,
                maxCondition = item.maxCondition,
                frequency = ItemModAssociatedEntity<PagerEntity>.GetAssociatedEntity(item)?.GetFrequency() ?? -1,
                instanceData = new ItemData.InstanceData(item),
                contents = item.contents?.itemList.Select(item1 => new ItemData
                {
                    itemid = item1.info.itemid,
                    amount = item1.amount,
                    condition = item1.condition
                }).ToArray()
            };
        }
        public class ItemData
        {
            public int itemid;
            public ulong skin;
            public int amount;
            public float condition;
            public float maxCondition;
            public int ammo;
            public string ammotype;
            public int position;
            public int frequency;
            public InstanceData instanceData;
            public ItemData[] contents;

            public class InstanceData
            {
                public int dataInt;
                public int blueprintTarget;
                public int blueprintAmount;
                public uint subEntity;

                public InstanceData() { }
                public InstanceData(Item item)
                {
                    if (item.instanceData == null)
                        return;

                    dataInt = item.instanceData.dataInt;
                    blueprintAmount = item.instanceData.blueprintAmount;
                    blueprintTarget = item.instanceData.blueprintTarget;
                }

                public void Restore(Item item)
                {
                    if (item.instanceData == null)
                        item.instanceData = new ProtoBuf.Item.InstanceData();

                    item.instanceData.ShouldPool = false;

                    item.instanceData.blueprintAmount = blueprintAmount;
                    item.instanceData.blueprintTarget = blueprintTarget;
                    item.instanceData.dataInt = dataInt;

                    item.MarkDirty();
                }

                public bool IsValid()
                {
                    return dataInt != 0 || blueprintAmount != 0 || blueprintTarget != 0;
                }
            }
        }
        #endregion
        #endregion

        #region Localization
        public static string Message(string key, ulong playerId = 0U) => Instance.lang.GetMessage(key, Instance, playerId != 0U ? playerId.ToString() : null);

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Notification.NotEnoughToContinue"] = "There are not enough players to continue the event...",
            ["Notification.NotEnoughToStart"] = "There is not enough players to start the event...",
            ["Notification.EventOpen"] = "The event <color=#007acc>{0}</color> (<color=#007acc>{1}</color>) is open for players\nIt will start in <color=#007acc>{2} seconds</color>\nType <color=#007acc>/event</color> to join",
            ["Notification.EventClosed"] = "The event has been closed to new players",
            ["Notification.EventFinished"] = "The event has finished",
            ["Notification.MaximumPlayers"] = "The event is already at maximum capacity",
            ["Notification.PlayerJoined"] = "<color=#007acc>{0}</color> has joined the <color=#007acc>{1}</color> event!",
            ["Notification.AlreadyInRoom"] = "You are already in room",
            ["Notification.Cantjointworoom"] = "You can't join more than one room at the same time",
            ["Notification.PlayerLeft"] = "<color=#007acc>{0}</color> has left the <color=#007acc>{1}</color> event!",
            ["Notification.RoundStartsIn"] = "Round starts in",
            ["Notification.EventWin"] = "<color=#007acc>{0}</color> won the event!",
            ["Notification.EventWin.Multiple"] = "The following players won the event; <color=#007acc>{0}</color>",
            ["Notification.EventWin.Multiple.Team"] = "<color={0}>Team {1}</color> won the event (<color=#007acc>{2}</color>)",
            ["Notification.Teams.Unbalanced"] = "The teams are unbalanced. Shuffling players...",
            ["Notification.Teams.TeamChanged"] = "You were moved to team <color=#007acc>{0}</color>",
            ["Notification.OutOfBounds"] = "You are out of the playable area. <color=#007acc>Return immediately</color> or you will be killed!",
            ["Notification.OutOfBounds.Time"] = "You have <color=#007acc>{0} seconds</color> to return...",
            ["Notification.Death.Suicide"] = "<color=#007acc>{0}</color> killed themselves...",
            ["Notification.Death.OOB"] = "<color=#007acc>{0}</color> tried to run away...",
            ["Notification.Death.Killed"] = "<color=#007acc>{0}</color> was killed by <color=#007acc>{1}</color>",
            ["Notification.Suvival.Remain"] = "(<color=#007acc>{0}</color> players remain)",
            ["Notification.SpectateCycle"] = "Press <color=#007acc>JUMP</color> to cycle spectate targets",
            ["Notification.NoRoomOwner"] = "Room owner left the game. Room is closing.",
            ["Info.Event.Current"] = "Current Event: {0} ({1})",
            ["Info.Event.Players"] = "\n{0} / {1} Players",
            ["Info.Event.Status"] = "Status : {0}",
            ["UI.SelectClass"] = "Select a class to continue...",
            ["UI.Death.Killed"] = "You were killed by {0}",
            ["UI.Death.Suicide"] = "You are dead...",
            ["UI.Death.OOB"] = "Don't wander off...",            
            ["Error.CommandBlacklisted"] = "You can not run that command whilst playing an event",
        };
        #endregion
    }

    namespace EventManagerEx
    {
        public interface IEventPlugin
        {            
            bool InitializeEvent(EventManager.EventConfig config,EventManager.GameRoom room);

            void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2);

            List<EventManager.EventParameter> AdditionalParameters { get; }

            string ParameterIsValid(string fieldName, object value);

            bool CanUseClassSelector { get; }

            bool RequireTimeLimit { get; }

            bool RequireScoreLimit { get; }

            bool UseScoreLimit { get; }

            bool UseTimeLimit { get; }

            bool IsTeamEvent { get; }
        }
        
    }
}

