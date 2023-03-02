// Requires: EventManager
// Requires: EMInterface
// Requires: MurderSkinManager
// Requires: ImageLibrary
// Requires: Kits
using Newtonsoft.Json;
using Oxide.Plugins.EventManagerEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Linq;
using Facepunch;
using HarmonyLib;
using Oxide.Core.Plugins;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;
using Oxide.Game.Rust.Cui;
using Network;
using Network.Visibility;
using Oxide.Core.Configuration;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("MurderGameMode", "Apwned", "1.0.0"), Description("Murder Game Mode for Event Plugin")]
    public class MurderGameMode : RustPlugin, IEventPlugin
    {
        [PluginReference] private Plugin ImageLibrary, ZoneManager,PlayerDatabase,BetterChatMute;

        public static MurderGameMode Instance { get; private set; }
        #region UI Variables
        const string role_UI = "murder.roleui";
        const string name_UI = "murder.nameui";
        const string death_UI = "murder.deathui";
        private const string roomhelper_UI = "murder.roomhelper";
        const string namelabel_UI = "murder.namelabelui";
        #endregion

        #region Oxide Hooks
        private void OnServerInitialized()
        {
            EventManager.RegisterEvent(Title, this);

            GetMessage = Message;

            RegisterGmodImages();
        }
        void Loaded()
        {
            Instance = this;
            //ConVar.Player.woundforever = true;
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void Unload()
        {
            if (!EventManager.IsUnloading)
                EventManager.UnregisterEvent(Title);
            
            Configuration = null;
            foreach (EventManager.BaseEventGame room in EventManager.BaseManager.Values.OfType<MurderGame>())
            {
                (room as MurderGame).DestroyAllRoomUI();
            }
            Instance = null;
            ImageLibrary = null;
            ZoneManager = null;
        }
        [HookMethod("OnKitCreated")]
        void OnKitCreated(Kits.ItemData item, ItemContainer container)
        {
            BasePlayer targetplayer = container.GetOwnerPlayer();
            if (targetplayer == null)
                return;
            MurderSkinManager.SkinPreferences preference = MurderSkinManager.GetPreferencesOfPlayer(targetplayer);

            if (item.Shortname == preference.meleeSkin.itemshortname && preference.meleeSkin.skinID != 0)
            {
                item.Skin = preference.meleeSkin.skinID;
            }
            else if (item.Shortname == "pistol.python" && preference.revolverSkin.skinID != 0)
            {
                item.Skin = preference.revolverSkin.skinID;
            }
            else if (Configuration.coloredAttires.ContainsKey(item.Shortname))
            {
                MurderPlayer murderplayer = EventManager.GetUser(targetplayer) as MurderPlayer;
                if(murderplayer != null)
                    item.Skin = Configuration.coloredAttires[item.Shortname][murderplayer.playerRole.roleColorName];
            }
        }

        void OnSpectateTargetUpdated(BasePlayer player, EventManager.BaseEventPlayer spectateTarget)
        {
            MurderPlayer murderPlayer = spectateTarget as MurderPlayer;
            MurderGame.SendRoleNamePanel(player,murderPlayer.playerRole);
        }

        void OnEventSpectateEnded(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, name_UI);
        }
        #region BetterChat

        private void MutePlayer(ulong userID)
        {
            PlayerDatabase.Call("SetPlayerData", userID.ToString(), "muted", true);
        }

        private void UnMutePlayer(ulong userID)
        {
            PlayerDatabase.Call("SetPlayerData", userID.ToString(), null);
        }

        private bool IsPlayerMuted(ulong userID)
        {
            object isMuted = PlayerDatabase.Call("GetPlayerData", userID, "muted");

            if (isMuted is bool)
                return true;
            else
                return false;
        }
        
        private void OnBetterChat(Dictionary<string, object> data)
        {
            IPlayer iplayer = data["Player"] as IPlayer;
            BasePlayer player = iplayer.Object as BasePlayer;

            EventManager.CurrentEventInfo currenteventinfo;
            if (player.TryGetComponent(out currenteventinfo))
            {
                MurderGame game = currenteventinfo.eventgame as MurderGame;
                if (game.spectators.Contains(player))
                {
                    List<string> _targetplayers = new List<string>();
                    foreach(BasePlayer basePlayer in game.spectators)
                        _targetplayers.Add(basePlayer.UserIDString);
                    data["targetPlayers"] = _targetplayers;
                }
                else if (game.eventPlayers.Contains(EventManager.GetUser(player)))
                {
                    MurderPlayer eventPlayer = EventManager.GetUser(player) as MurderPlayer;
                    List<string> _targetplayers = new List<string>();
                    foreach(BasePlayer basePlayer in game.spectators)
                        _targetplayers.Add(basePlayer.UserIDString);
                    foreach(EventManager.BaseEventPlayer basePlayer in game.eventPlayers)
                        _targetplayers.Add(basePlayer.Player.UserIDString);
                    data["targetPlayers"] = _targetplayers;
                    if (eventPlayer.playerRole != null)
                    {
                        data["Username"] = eventPlayer.playerRole.roleName;
                        (data["UsernameSettings"] as Dictionary<string, object>)["Color"] =
                            eventPlayer.playerRole.roleColor;
                        data["isAvatarHidden"] = true;
                    }
                }
            }
            //Muted players
            var chatChannel = (ConVar.Chat.ChatChannel)data["ChatChannel"];
            bool isPublicMessage = chatChannel == ConVar.Chat.ChatChannel.Global;
            if (BetterChatMute.Call("HandleChat", iplayer, isPublicMessage) != null)
            {
                data["CancelOption"] = 2;
            }
        }
        #endregion
        
        #endregion

        #region Functions
        private static void RunEffect(Vector3 position, string prefab, BasePlayer player = null)
        {
            var effect = new Effect();
            effect.Init(Effect.Type.Generic,position, Vector3.zero);
            effect.pooledString = prefab;

            if (player != null)
            {
                EffectNetwork.Send(effect, player.net.connection);
            }
        }
        private static void RunEffect(Vector3 position, string prefab, uint groupID)
        {
            Group group = Net.sv.visibility.TryGet(groupID);
            if (group == null)
                return;

            var effect = new Effect();
            effect.Init(Effect.Type.Generic, position, Vector3.zero);
            effect.pooledString = prefab;

            foreach (Connection connection in group.subscribers)
                EffectNetwork.Send(effect, connection);
        }
        internal static void BroadcastToPlayer(BasePlayer player, string message) => player.SendConsoleCommand("chat.add", 0, EventManager.Configuration.Message.ChatIcon,EventManager.Configuration.announcerlabel + message);
        private static string ToOrdinal(int i) => (i + "th").Replace("1th", "1st").Replace("2th", "2nd").Replace("3th", "3rd");
        public static Quaternion StringToQuaternion(string sQuaternion)
        {
            // Remove the parentheses
            if (sQuaternion.StartsWith("(") && sQuaternion.EndsWith(")"))
            {
                sQuaternion = sQuaternion.Substring(1, sQuaternion.Length - 2);
            }

            // split the items
            string[] sArray = sQuaternion.Split(',');

            // store as a Vector3
            Quaternion result = new Quaternion(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]),
                float.Parse(sArray[3]));

            return result;
        }
        #endregion

        #region Gmod User Interface
        void RegisterGmodImages()
        {
            AddImage("murdererUI", "https://www.dropbox.com/s/9w3ecyqfuq5pnrn/murderer.png?dl=1");
            AddImage("sheriffUI", "https://www.dropbox.com/s/ivibaqotti7cyt7/sheriff.png?dl=1");
            AddImage("innocentUI", "https://www.dropbox.com/s/swawsj2u8tdi9to/innocent.png?dl=1");
            AddImage("darkbluenameUI", "https://www.dropbox.com/s/7x8e3i5fmlqjtw0/darkblue.png?dl=1");
            AddImage("lightgreennameUI", "https://www.dropbox.com/s/0yqgmg1mf4nbqx2/lightgreen.png?dl=1");
            AddImage("greennameUI", "https://www.dropbox.com/s/syu8ep3y6il2big/green.png?dl=1");
            AddImage("greynameUI", "https://www.dropbox.com/s/8ed21djxyhhcbup/grey.png?dl=1");
            AddImage("lightbluenameUI", "https://www.dropbox.com/s/nhzlk8uedc7638x/lightblue.png?dl=1");
            AddImage("orangenameUI", "https://www.dropbox.com/s/hflzk1cvmdm21y2/orange.png?dl=1");
            AddImage("pinknameUI", "https://www.dropbox.com/s/uphw9u3uky1ps7m/pink.png?dl=1");
            AddImage("purplenameUI", "https://www.dropbox.com/s/uh9hi1w22yqwhtp/purple.png?dl=1");
            AddImage("rednameUI", "https://www.dropbox.com/s/nukyyjwc9yat5m9/red.png?dl=1");
            AddImage("yellownameUI", "https://www.dropbox.com/s/dvfasz3a55lwbse/yellow.png?dl=1");
            AddImage("murdererwins", "https://www.dropbox.com/s/eqrdi5q9pl4rlmd/Themurdererwins.png?dl=1");
            AddImage("bystanderswin", "https://www.dropbox.com/s/ucrph02a2vyytxg/Bystanderswin.png?dl=1");
            AddImage("lootcollected", "https://www.dropbox.com/s/3jaxr33g3ho3cil/Lootcollected.png?dl=1");
            AddImage("murder", "https://www.dropbox.com/s/6rqfcanjxl7yzre/Murder.png?dl=1");
            AddImage("shotwrongperson", "https://www.dropbox.com/s/b530d62h877au1p/shotwrongperson.png?dl=1");
            AddImage("brandlogo", "https://www.dropbox.com/s/mgz1o153j00iae4/logo.png?dl=1");
        }
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName, 0UL, null);
        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name, 0UL, false);
        
        static void SendRoomHelper(BasePlayer player,EventManager.GameRoom gameRoom)
        {
            CuiElementContainer container = UI.Container(roomhelper_UI, "1 1 1 0",UI.TransformToUI4(1250f, 1590f, 23f, 53f));
            //Leave Button
            string leavebutton_UI = "murder.leavebuttonui";
            UI.Panel(container,roomhelper_UI,leavebutton_UI,"0 0 0 0",UI.TransformToUI4(234f, 340f, 0f, 30f,340f,30f));
            UI.AddBlur(container,"0 0 0 0.1",leavebutton_UI);
            UI.Button(container, leavebutton_UI, UI.Color("#373737", 0.7f), "<color=#dddddd>Leave</color>", 10,  UI4.Full, "murder.leaveroom");
            //Skin Manager Button
            string skinmanagerbutton_UI = "murder.skinmanagerbuttonui";
            UI.Panel(container,roomhelper_UI,skinmanagerbutton_UI,"0 0 0 0",UI.TransformToUI4(122f, 228f, 0f, 30f,340f,30f));
            UI.AddBlur(container,"0 0 0 0.1",skinmanagerbutton_UI);
            UI.Button(container, skinmanagerbutton_UI, UI.Color("#373737", 0.7f), "<color=#dddddd>Skin Menu</color>", 10, UI4.Full, "murderskinmanager.ui");
            //Password Info Label
            if(gameRoom.hasPassword)
                UI.Label(container,roomhelper_UI,$"Room Password\n{gameRoom.password}",8,UI.TransformToUI4(0f, 106f, 0f, 30f,340f,30f),TextAnchor.MiddleLeft,UI.Color("#dddddd", 0.7f));
            
            CuiHelper.DestroyUi(player, roomhelper_UI);
            CuiHelper.AddUi(player, container);
        }
        [ConsoleCommand("murder.deathui.close")]
        void cmdCloseDeathUI(ConsoleSystem.Arg arg)
        {
            CuiHelper.DestroyUi(arg.Player(), death_UI);
        }
        [ConsoleCommand("murder.leaveroom")]
        void cmdLeaveRoom(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            EventManager.BaseEventGame room = EventManager.GetRoomofPlayer(player);
            if(room == null)
            {
                BroadcastToPlayer(player, Message("NotInRoom", player.userID));
                CuiHelper.DestroyUi(player,roomhelper_UI);
                return;
            }
            room.LeaveEvent(player);
            BroadcastToPlayer(player, Message("LeftTheRoom", player.userID));
            EventManager.Instance.LobbyRoomSystem.CallHook("OpenLobbyUI", player);
            EventManager.Instance.LobbyRoomSystem.CallHook("OpenLobbyHelperUI", player);
        }
        #endregion

        #region Event Classes
        public class MurderGame : EventManager.BaseEventGame
        {
            #region Fields
            private int rolepanelduration = 5;
            public EventManager.BaseEventPlayer winner;
            public EventWinner winnerside;
            public int murdererCount = 0;
            public int sheriffCount = 0;
            public int innocentCount = 0;
            public int bystandersCount { get { return sheriffCount + innocentCount; } }
            #endregion
            #region Player Role Determination
            Dictionary<string,int> givenRoleNames = new Dictionary<string,int>(); //Created to prevent players same take multiple names
            Dictionary<string,int> givenRoleColors = new Dictionary<string, int>(); //Created to prevent players take multiple same colors
            System.Random random = new System.Random();
            void SetRole(EventManager.BaseEventPlayer eventPlayer, PlayerRole role)
            {
                eventPlayer.Kit=role.ToString();
                KeyValuePair<string, string> kvp = GetRandomRoleColor();
                (eventPlayer as MurderPlayer).playerRole = new MurderPlayer.MurderRole(role, eventPlayer.Player.displayName ,GetRandomRoleName(), kvp.Key, kvp.Value);
                SwitchName(eventPlayer);
            }
            string GetRandomRoleName()
            {
                string name = Configuration.roleNames.GetRandom();
                if (givenRoleNames.Keys.Contains(name) && givenRoleNames[name]>=2)
                {
                    return GetRandomRoleName();
                }
                if(!givenRoleNames.Keys.Contains(name)) givenRoleNames.Add(name,0);
                givenRoleNames[name]++;
                return name;
            }
            KeyValuePair<string,string> GetRandomRoleColor()
            {
                int index = random.Next(0, Configuration.roleColors.Count - 1);
                KeyValuePair<string,string> kvp = Configuration.roleColors.ElementAt(index);
                string color = kvp.Value;
                if (givenRoleColors.Keys.Contains(color))
                {
                    int a =0;
                    givenRoleColors.TryGetValue(color, out a);
                    if(a>=2)
                        return GetRandomRoleColor();
                }
                givenRoleNames.TryAdd(color, 0);
                givenRoleNames[color]++;
                
                return kvp;
            }
            void DeclareRoles()
            {
                List<int> index = Pool.GetList<int>();
                for (int i = 0; i < eventPlayers.Count; i++) { index.Add(i); }
                foreach (EventManager.BaseEventPlayer player in eventPlayers)    //Gives sheriff and murderer kits randomly according to config
                {
                    int i = index.GetRandom();
                    if (murdererCount < Configuration.murdererNum)
                    {
                        SetRole(eventPlayers[i], PlayerRole.Murderer);
                        index.Remove(i);
                        murdererCount++;
                    }
                    else if (sheriffCount < Configuration.sheriffNum)
                    {
                        SetRole(eventPlayers[i], PlayerRole.Sheriff);
                        index.Remove(i);
                        sheriffCount++;
                    }
                    else
                    {
                        SetRole(eventPlayers[i], PlayerRole.Bystander);
                        index.Remove(i);
                        innocentCount++;
                    }
                }
                Pool.FreeList(ref index);
            }

            void SwitchName(EventManager.BaseEventPlayer player)
            {
                player.Player.displayName = (player as MurderPlayer).playerRole.roleName;
                // This is needed to update overhead name tag
                player.Player.limitNetworking = true;
                player.Player.limitNetworking = false;
            }
            #endregion
            #region Overrides

            protected override bool CanDropBody() => true;

            protected override EventManager.BaseEventPlayer AddPlayerComponent(BasePlayer player) => player.gameObject.AddComponent<MurderPlayer>();
            protected override void StartEvent()
            {
                InvokeHandler.CancelInvoke(this, PrestartEvent);
                if (!HasMinimumRequiredPlayers())
                {
                    BroadcastToRoom(EventManager.Message("Notification.NotEnoughToStart"));
                    return;
                }
                Timer.StopTimer();
                Status = EventManager.EventStatus.Started;
                if (Config.TimeLimit > 0)
                    Timer.StartTimer(Config.TimeLimit, string.Empty, RestartEvent);    
                GodmodeEnabled = false;

                DeclareRoles();
                eventPlayers.ForEach(player =>
                {
                    if (player?.Player == null)
                        return;

                    if (player.IsDead)
                        EventManager.RespawnPlayer(player);
                    else
                    {
                        EventManager.ResetPlayer(player);
                    }
                    (player as MurderPlayer).StartFartTimer();

                    //UI sound effect for each individual player
                    RunEffect(player.Player.ServerPosition, "assets/bundled/prefabs/fx/item_unlock.prefab", player.Player);
                    SendRoleNamePanel((player as MurderPlayer).Player,(player as MurderPlayer).playerRole);
                     SendRoleUI(player);

                    SpawnPlayer(player, true);
                    // Giving suits to player
                    MurderSkinManager.SkinPreferences preference = MurderSkinManager.GetPreferencesOfPlayer(player.Player);
                    EventManager.GiveKit(player.Player, preference.costume);
                    InvokeHandler.InvokeRepeating(this, SpawnRandomItems,0f,Configuration.itemspawndelay);
                    player.Player.SendNetworkUpdate();
                });
            }
            
            internal override void EndEvent()
            {
                //Brings networking to its original
                eventPlayers.ForEach(player => RestoreGroupNetworks(player?.Player));
                spectators.ForEach(player => RestoreGroupNetworks(player));
                DestroyAllRoomUI();
                base.EndEvent();
                InvokeHandler.CancelInvoke(this, SpawnRandomItems);
            }

            internal override void RestartEvent()
            {
                foreach (BasePlayer player in EventManager.GetPlayersOfRoom(this))
                    DestroyRoomUI(player);

                GodmodeEnabled = true;
                murdererCount = 0;      sheriffCount = 0;    innocentCount = 0;
                SendScoreboard();
                InvokeHandler.CancelInvoke(this, SpawnRandomItems);
                
                if (!gameroom.isCreatedbySystem)
                {
                    BasePlayer roomowner;
                    BasePlayer.TryFindByID(gameroom.ownerID, out roomowner);
                    if (!EventManager.GetPlayersOfRoom(this).Contains(roomowner))
                    {
                        BroadcastToRoom(EventManager.Message("Notification.NoRoomOwner"));
                        InvokeHandler.Invoke(this,EndEvent,5f);
                        return;
                    }
                }

                Status = EventManager.EventStatus.Open;
                //Restoring display names
                foreach(MurderPlayer eventPlayer in EventManager.GetEventPlayersOfRoom(this))
                {
                    eventPlayer.Player.displayName = eventPlayer.playerRole.realName;
                    if(eventPlayer.fartTimer != null)
                        eventPlayer.ResetFartTimer();
                }
                base.RestartEvent();
            }

            internal override void PrestartEvent()
            {
                DestroyRoomUI();
                base.PrestartEvent();
            }

            protected override void OnPlayerJoined(BasePlayer player)
            {
                if (HasMinimumRequiredPlayers() && (Status == EventManager.EventStatus.Open || Status == EventManager.EventStatus.Finished))
                {
                    PrestartEvent();
                }
                else
                {
                    BroadcastToPlayers(string.Format(Instance.Message("NeededPlayer"),GetNeededRequiredPlayers()));
                }
                SendRoomHelper(player,this.gameroom);
                base.OnPlayerJoined(player);
            }

            protected override void OnPlayerLeft(BasePlayer player)
            {
                 if(EventManager.GetPlayersOfRoom(this).Count == 0 && !EventManager.IsUnloading)
                     RestartEvent();
                base.OnPlayerLeft(player);
            }
            internal override void LeaveEvent(BasePlayer player)
            {
                DestroyRoomUI(player);
                CuiHelper.DestroyUi(player,roomhelper_UI);
                MurderPlayer eventPlayer = EventManager.GetUser(player) as MurderPlayer;
                if (eventPlayer != null && eventPlayer?.playerRole != null)
                    player.displayName = eventPlayer.playerRole.realName;
                
                if(player.HasComponent<DisplayRoleNameBehaviour>())
                     DestroyImmediate(player.GetComponent<DisplayRoleNameBehaviour>());
                FartEffect fartEffect;
                if(player.TryGetComponent(out fartEffect))
                    DestroyImmediate(fartEffect);
                
                if (eventPlayer?.playerRole?.playerRole == PlayerRole.Murderer)
                {
                    RestartEvent();
                    BroadcastToRoom("Murderer left the game!");
                }
                base.LeaveEvent(player);
            }
            internal override void LeaveEvent(EventManager.BaseEventPlayer eventPlayer)
            {
                eventPlayer.Player.displayName = (eventPlayer as MurderPlayer).playerRole.realName;
                FartEffect fartEffect;
                if(eventPlayer.Player.TryGetComponent(out fartEffect))
                    DestroyImmediate(fartEffect);
                if (eventPlayer.Player.HasComponent<DisplayRoleNameBehaviour>())
                    DestroyImmediate(eventPlayer.Player.GetComponent<DisplayRoleNameBehaviour>());

                if ((eventPlayer as MurderPlayer)?.playerRole?.playerRole == PlayerRole.Murderer)
                {
                    RestartEvent();
                }
                base.LeaveEvent(eventPlayer);
            }
            protected override bool CanDropBackpack() { return false;}
            //Disguising kılık değiştirme
            internal override object CanLootEntity(BasePlayer player, LootableCorpse corpse)
            {
                MurderPlayer murderPlayer = EventManager.GetUser(player) as MurderPlayer;
                if (murderPlayer == null)
                    return null;
                if (murderPlayer.playerRole == null)
                    return null;
                DroppedEventCorpse droppedEventCorpse;
                if (corpse.TryGetComponent(out droppedEventCorpse))
                {
                    if (murderPlayer.playerRole.playerRole == PlayerRole.Murderer &&
                        murderPlayer.playerRole.collectedItems > 0)
                    {
                        murderPlayer.playerRole.roleColor = droppedEventCorpse.corpseRole.roleColor;
                        murderPlayer.playerRole.roleName = droppedEventCorpse.corpseRole.roleName;
                        murderPlayer.playerRole.roleColorName = droppedEventCorpse.corpseRole.roleColorName;
                        murderPlayer.Player.inventory.containerWear.Clear();
                        foreach (Item item in corpse.containers[1].itemList)
                        {
                            Item newItem =ItemManager.Create(item.info, 1, item.skin);
                            murderPlayer.Player.inventory.containerWear.Insert(newItem);
                        }
                        murderPlayer.Player.SendFullSnapshot();
                        murderPlayer.playerRole.collectedItems--;
                        SendRoleNamePanel(murderPlayer.Player,murderPlayer.playerRole);
                        BroadcastToPlayer(player,$"You disguised to <color={murderPlayer.playerRole.roleColor}>{murderPlayer.playerRole.roleName}</color>");
                        RunEffect(player.ServerPosition,"assets/prefabs/deployable/locker/sound/equip_zipper.prefab",player);
                    }
                    return false;
                }
                return null;
            }

            internal override void OnPlayerTakeDamage(EventManager.BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                base.OnPlayerTakeDamage(eventPlayer, hitInfo);
                if (hitInfo?.Weapon?.ShortPrefabName == "python.entity" ||
                    hitInfo.Weapon?.ShortPrefabName == "knife.combat.entity")
                {
                    hitInfo.damageTypes.ScaleAll(100f);
                }
            }
            
            internal override object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
            {
                return false;
            }
            internal override object OnItemPickup(Item item, BasePlayer player)
            {
                MurderPlayer murderPlayer = EventManager.GetUser(player) as MurderPlayer;
                if (murderPlayer.playerRole.playerRole == PlayerRole.Murderer && item.info.shortname == "knife.combat") return null;

                //Sheriffin birisini vurduktan sonra silahı geri alamaması
                if (item.GetWorldEntity().gameObject.HasComponent<CollectableItem>())
                {
                    murderPlayer.playerRole.collectedItems++;
                    if(murderPlayer.playerRole.collectedItems >= Configuration.neededCollectableforRevolver && murderPlayer.playerRole.playerRole != PlayerRole.Murderer && !murderPlayer.HasRevolver())
                    {
                        EventManager.GiveKit(murderPlayer.Player, "sheriff");
                        murderPlayer.playerRole.collectedItems -= Configuration.neededCollectableforRevolver;
                    }
                    
                    item.Remove();
                    SendRoleNamePanel(murderPlayer.Player, murderPlayer.playerRole);
                    return null;
                }
                return false;
            }
            internal override object OnItemAction(Item item, string action, BasePlayer player)
            {
                if (action == "drop") return false;
                return null;
            }
            
            internal override void OnEventPlayerDeath(EventManager.BaseEventPlayer victim, EventManager.BaseEventPlayer attacker = null, HitInfo info = null)
            {
                if (victim == null)
                    return;

                if(eventPlayers.Contains(victim))
                {
                    eventPlayers.Remove(victim);
                    spectators.Add(victim.Player);
                }

                victim.OnPlayerDeath(attacker, Configuration.RespawnTime);

                if (attacker != null && victim != attacker)
                {
                    attacker.OnKilledPlayer(info);

                    // if (GetAlivePlayerCount() <= 1)
                    // {
                    //     winner = attacker;
                    //     InvokeHandler.Invoke(this, EndEvent, 0.1f);
                    //     return;
                    // }
                }

                UpdateScoreboard();

                base.OnEventPlayerDeath(victim, attacker);
            }

            internal override object OnWoundCheck(BasePlayer player)
            {
                using (TimeWarning.New("WoundingTick", 0))
                {
                    if (!player.IsDead())
                    {
                        if (player.TimeSinceWoundedStarted > Configuration.woundedDuration)
                        {
                            player.health = 100;
                            player.SetPlayerFlag(global::BasePlayer.PlayerFlags.Wounded, false);
                            player.SetPlayerFlag(global::BasePlayer.PlayerFlags.Incapacitated, false);
                            player.CancelInvoke(player.WoundingTick);
                            BroadcastToPlayer(player, Instance.Message("NoLongerWounded", player.userID));
                            return false;
                        }
                    }
                }
                player.Invoke(player.WoundingTick, 1f);
                return false;
            }

            internal override void OnMeleeThrown(BasePlayer player, Item item)
            {
                EventManager.BaseEventPlayer eventPlayer = EventManager.GetUser(player);
                Instance.timer.Once(20f, () =>
                {
                    if (eventPlayer == null)
                        return;
                    var newMelee = ItemManager.CreateByItemID(item.info.itemid, item.amount, item.skin);
                    (newMelee.GetHeldEntity() as BaseMelee).holsterInfo.displayWhenHolstered = false;
                    newMelee._condition = newMelee.maxCondition;

                    player.GiveItem(newMelee, BaseEntity.GiveItemReason.PickedUp);
                    BroadcastToPlayer(player,"Melee is retrieved");
                });
            }

            internal override void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
            {
                EventManager.BaseEventPlayer eventPlayer = EventManager.GetUser(player);
                SupplyPistolBullet(eventPlayer);
            }

            internal override void OnLoseCondition(Item item, ref float amount)
            {
                amount = 0;
            }

            internal override void PrePlayerDeath(EventManager.BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                BasePlayer baseAttacker = hitInfo?.InitiatorPlayer;
                MurderPlayer attacker = EventManager.GetUser(baseAttacker) as MurderPlayer;
                MurderPlayer victim = eventPlayer as MurderPlayer;

                if(CanDropBody())
                    eventPlayer.DropBody(hitInfo);
                
                base.PrePlayerDeath(eventPlayer, hitInfo);
                if (victim.playerRole.playerRole != PlayerRole.Murderer &&
                    hitInfo.Weapon?.ShortPrefabName == "python.entity")
                {
                    attacker.Player.GoToCrawling(null);
                    (victim.Event as MurderGame).DropRevolver(attacker?.Player, 7);
                    BroadcastToPlayer(attacker,"You've shot the wrong person!");
                }
            }

            protected override void GetWinningPlayers(ref List<EventManager.BaseEventPlayer> winners)
            {
                if (winner == null)
                {
                    if (this.eventPlayers.Count > 0)
                    {
                        int kills = 0;

                        for (int i = 0; i < this.eventPlayers.Count; i++)
                        {
                            EventManager.BaseEventPlayer eventPlayer = this.eventPlayers[i];
                            if (eventPlayer == null)
                                continue;

                            if (eventPlayer.Kills > kills)
                            {
                                winner = eventPlayer;
                                kills = eventPlayer.Kills;
                            }
                        }
                    }
                }
                if (winner != null)
                    winners.Add(winner);
            }
            protected override void CreateEventPlayer(BasePlayer player, EventManager.Team team = EventManager.Team.None)
            {
                if (player == null)
                    return;

                spectators.Remove(player);
                
                if (player.HasComponent<EventManager.RoomSpectatingBehaviour>())
                    DestroyImmediate(player.GetComponent<EventManager.RoomSpectatingBehaviour>());

                EventManager.BaseEventPlayer eventPlayer = AddPlayerComponent(player);

                eventPlayer.ResetPlayer();

                eventPlayer.Event = this;

                eventPlayer.Team = team;

                eventPlayers.Add(eventPlayer);

                //if (!Config.AllowClassSelection || GetAvailableKits(eventPlayer.Team).Count <= 1)
                //    eventPlayer.Kit = GetAvailableKits(team).First();

                SpawnPlayer(eventPlayer, Status == EventManager.EventStatus.Started, false);

                if (!string.IsNullOrEmpty(Config.ZoneID))
                    EventManager.Instance.ZoneManager?.Call("AddPlayerToZoneWhitelist", Config.ZoneID, player);

                (eventPlayer as MurderPlayer).MurderPlayerInit();
            }
            protected override void SendRoleUI(EventManager.BaseEventPlayer eventPlayer)
            {
                MurderPlayer player = (eventPlayer as MurderPlayer);
                if (player == null || player.Kit==null) return;
                CuiElementContainer container = UI.Container(role_UI, "0 0 0 1", UI4.Full);
                if(player.playerRole.playerRole == PlayerRole.Murderer)
                {
                    string png = EMInterface.Instance.GetImage("murdererUI");
                    UI.Image(container, role_UI, png, UI4.Full);
                }
                else if(player.playerRole.playerRole == PlayerRole.Sheriff)
                {
                    string png = EMInterface.Instance.GetImage("sheriffUI");
                    UI.Image(container, role_UI, png, UI4.Full);
                }
                else if (player.playerRole.playerRole == PlayerRole.Bystander)
                {
                    string png = EMInterface.Instance.GetImage("innocentUI");
                    UI.Image(container, role_UI, png, UI4.Full);
                }
                else { return; }

                player.AddUI(role_UI, container);
                Instance.timer.In(rolepanelduration, () => { player.DestroyUI(role_UI); });
            }

            public override void SetStaticPrefabRelations()
            {
                base.SetStaticPrefabRelations();
                foreach (EventManager.StaticObject staticComputerStation in EventManager.StaticObjects[Config.EventName]
                             .Where(x => x.staticEntity is ComputerStation))
                {
                    ComputerStation computerStation = staticComputerStation.staticEntity as ComputerStation;
                    foreach (EventManager.StaticObject staticCamera in EventManager.StaticObjects[Config.EventName]
                                 .Where(x => x.staticEntity is CCTV_RC))
                    {
                        CCTV_RC cctvCam = staticCamera.staticEntity as CCTV_RC;
                        string cctvIdentifier = gameroom.roomID.ToString() + cctvCam.net.ID;
                        cctvCam.UpdateIdentifier(cctvIdentifier);
                        computerStation.controlBookmarks.Add(cctvCam.GetIdentifier(),cctvCam.net.ID);
                    }
                }
            }

            #endregion
            #region UI
            protected override void SendScoreboard()
            {
                MurderPlayer murderer = GetMurderer();
                string murdererwinspng = EMInterface.Instance.GetImage("murdererwins");
                string bystanderswinpng = EMInterface.Instance.GetImage("bystanderswin");
                string lootcollectedpng = EMInterface.Instance.GetImage("lootcollected");
                string murderlabel = EMInterface.Instance.GetImage("murder");
                foreach (BasePlayer player in EventManager.GetPlayersOfRoom(this))
                {
                    CuiElementContainer container = UI.Container(death_UI, UI.Color("#232323", 1), UI.TransformToUI4(360f, 1560f, 190f, 890f), true);
                    UI.Panel(container, death_UI, UI.Color("#2e2e2e", 1), UI.TransformToUI4(15f, 1185f, 510f, 660f, 1200f, 700f));    //upper panel
                    UI.Panel(container, death_UI, UI.Color("#2e2e2e", 1), UI.TransformToUI4(15f, 855f, 105f, 505f, 1200f, 700f));     //left panel
                    UI.Panel(container, death_UI, UI.Color("#2e2e2e", 1), UI.TransformToUI4(860f, 1185f, 105f, 505f, 1200f, 700f));   //right panel
                    UI.Panel(container, death_UI, UI.Color("#890909", 1), UI.TransformToUI4(15f, 1184f, 657f, 660f, 1200f, 700f));  //Red line

                    UI.Image(container, death_UI, lootcollectedpng, UI.TransformToUI4(37f, 307f, 451f, 485f, 1200f, 700f));

                    if (winnerside == EventWinner.Murderer)
                        UI.Image(container, death_UI, murdererwinspng, UI.TransformToUI4(37f, 486f, 592f, 632f, 1200f, 700f));
                    else if (winnerside == EventWinner.Bystanders)
                        UI.Image(container, death_UI, bystanderswinpng, UI.TransformToUI4(37f, 420f, 580f, 639f, 1200f, 700f));

                    UI.Label(container, death_UI, $"The murderer was <color={murderer.playerRole.roleColor}>{murderer.playerRole.roleName} </color>", 18, UI.TransformToUI4(45f, 353f, 540f, 575f, 1200f, 700f), TextAnchor.MiddleLeft);

                    for (int i = 0; i < EventManager.GetPlayersOfRoom(this).Count; i++)
                    {
                        if (EventManager.GetPlayersOfRoom(this)[i].HasComponent<EventManager.BaseEventPlayer>())
                        {
                            MurderPlayer eventPlayer = EventManager.GetPlayersOfRoom(this)[i].GetComponent<EventManager.BaseEventPlayer>() as MurderPlayer;
                            SendScoreboardNameLabel(container, eventPlayer.playerRole.realName, eventPlayer.playerRole.roleName, eventPlayer.playerRole.collectedItems, i, UI.Color(eventPlayer.playerRole.roleColor, 1));
                        }
                    }

                    UI.Image(container, death_UI, murderlabel, UI.TransformToUI4(966f, 1080f, 451f, 479f, 1200f, 700f));
                    UI.Label(container, death_UI, "Room ID:", 14, UI.TransformToUI4(886f, 1050f, 387f, 423f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, "Arena:", 14, UI.TransformToUI4(886f, 1050f, 352f, 388f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, "Owner:", 14, UI.TransformToUI4(886f, 1050f, 317f, 353f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, "Max Players:", 14, UI.TransformToUI4(886f, 1050f, 282f, 317f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, "Can be spectated:", 14, UI.TransformToUI4(886f, 1050f, 247f, 282f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));

                    UI.Label(container, death_UI, $"{gameroom.roomID}", 14, UI.TransformToUI4(1050f, 1180f, 388f, 423f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, $"{gameroom.place}", 14, UI.TransformToUI4(1050f, 1180f, 353f, 388f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, $"{gameroom.ownerName}", 14, UI.TransformToUI4(1050f, 1180f, 317f, 353f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, $"{gameroom.maxPlayer}", 14, UI.TransformToUI4(1050f, 1180f, 282f, 317f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));
                    UI.Label(container, death_UI, $"{gameroom.isSpectatable}", 14, UI.TransformToUI4(1050f, 1180f, 247f, 282f, 1200f, 700f), TextAnchor.MiddleLeft, UI.Color("#b3b3b3", 1f));

                    UI.Button(container, death_UI, UI.Color("#890909", 1), "X", 10, UI.TransformToUI4(1141f, 1185f, 667f, 690f, 1200f, 700f), "murder.deathui.close");
                    CuiHelper.DestroyUi(player, death_UI);
                    CuiHelper.AddUi(player, container);
                }
            }
            /// <summary>
            /// Used in SendScoreboard for creating name labels
            /// </summary>
            private void SendScoreboardNameLabel(CuiElementContainer container, string username, string rolename, int collecteditem, int position, string color)
            {
                const int ELEMENT_HEIGHT = 30;
                const int ELEMENT_WIDTH = 410;
                int minusy = ELEMENT_HEIGHT * (position % 10);
                int plusx = ELEMENT_WIDTH * (position / 10);
                UI.Label(container, death_UI, $"{username}", 15, UI.TransformToUI4(45f + plusx, 240f + plusx, 416f - minusy, 441f - minusy, 1200f, 700f), TextAnchor.MiddleLeft, color);
                UI.Label(container, death_UI, $"{rolename}", 15, UI.TransformToUI4(259f + plusx, 394f + plusx, 416f - minusy, 441f - minusy, 1200f, 700f), TextAnchor.MiddleLeft, color);
                UI.Label(container, death_UI, $"{collecteditem}", 15, UI.TransformToUI4(409f + plusx, 435f + plusx, 416f - minusy, 441f - minusy, 1200f, 700f), TextAnchor.MiddleLeft, color);
            }
            public static void SendRoleNamePanel(BasePlayer target,MurderPlayer.MurderRole playerRole)
            {
                UI4 dimension = new UI4(0f, 0f, 0.078f, 0.176f);
                CuiElementContainer container = UI.Container(name_UI, "0 0 0 0", dimension);
                string url = EMInterface.Instance.GetImage($"{playerRole.roleColorName}nameUI");
                UI.Image(container, name_UI, url, new UI4(0f, 0f, 1f, 0.789f));
                string color = UI.Color(playerRole.roleColor, 1);
                UI.Label(container, name_UI, playerRole.roleName, 18, new UI4(0f, 0.751f, 1f, 0.93f), TextAnchor.MiddleCenter, color);
                UI.Label(container, name_UI, playerRole.collectedItems.ToString(), 45, new UI4(0.35f, 0.121f, 0.66f, 0.668f), TextAnchor.MiddleCenter, "1 1 1 1");
                CuiHelper.DestroyUi(target, name_UI);
                CuiHelper.AddUi(target, container);
            }
            internal void DestroyRoomUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, role_UI);
                CuiHelper.DestroyUi(player, name_UI);
                CuiHelper.DestroyUi(player, death_UI);
            }
            internal void DestroyRoomUI()
            {
                foreach (BasePlayer player in EventManager.GetPlayersOfRoom(this))
                {
                    CuiHelper.DestroyUi(player, role_UI);
                    CuiHelper.DestroyUi(player, name_UI);
                    CuiHelper.DestroyUi(player, death_UI);
                }
            }
            internal void DestroyAllRoomUI()
            {
                foreach (BasePlayer player in EventManager.GetPlayersOfRoom(this))
                {
                    CuiHelper.DestroyUi(player, role_UI);
                    CuiHelper.DestroyUi(player, name_UI);
                    CuiHelper.DestroyUi(player, death_UI);
                    CuiHelper.DestroyUi(player, roomhelper_UI);
                }
            }
            #endregion
            #region Helpers
            void SpawnRandomItems()
            {
                string place = GetGamePlace();
                object randomloc = GetRandomItemSpawn(place);
                if (randomloc is string) { string msg = (string)randomloc; Instance.PrintError(msg); return; }
                if (randomloc == null) { Instance.PrintError("randomloc null"); return; }
                Vector3 location = (Vector3)randomloc;
                DroppedItem item = ItemManager.CreateByItemID(Configuration.randomItemIDs.GetRandom()).Drop(location, Vector3.zero).GetComponent<DroppedItem>();
                item.GetComponent<Rigidbody>().isKinematic = false;
                Group group = Net.sv.visibility.Get(Convert.ToUInt32(gameroom.roomID));
                roomobjects.Add(item);
                item.gameObject.AddComponent<EventManager.NetworkGroupData>().Enable(Convert.ToUInt32(gameroom.roomID));
                item.net.SwitchGroup(group);
                item.gameObject.AddComponent<CollectableItem>();
            }
            public void DropRevolver(BasePlayer player,int multiplier)
            {
                Item item = player.inventory.containerBelt.FindItemsByItemName("pistol.python");
                item.RemoveFromWorld();
                item.Drop(player.eyes.position, player.eyes.HeadRay().direction * multiplier, new Quaternion());
            }

            private void SupplyPistolBullet(EventManager.BaseEventPlayer eventPlayer)
            {
                var item = ItemManager.CreateByItemID(785728077);
                
                Instance.timer.Once(20f, () =>
                {
                    if (eventPlayer == null)
                        return;
                    if (!item.MoveToContainer(eventPlayer.Player.inventory.containerMain))
                    {
                        item.Remove();
                    }
                    BroadcastToPlayer(eventPlayer,"Ammo loaded");
                });
            }

            MurderPlayer GetMurderer()
            {
                foreach(MurderPlayer eventPlayer in EventManager.GetEventPlayersOfRoom(this))
                {
                    if (eventPlayer.playerRole.playerRole == PlayerRole.Murderer)
                        return eventPlayer;
                }
                return null;
            }
            class CollectableItem : MonoBehaviour { }
            #endregion
            #region Scoreboards
            protected override void BuildScoreboard()
            {
                //scoreContainer = EMInterface.CreateScoreboardBase(this);

                int index = -1;

                if (Config.ScoreLimit > 0)
                    //EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Remaining", 0UL), eventPlayers.Count), index += 1);

                //EMInterface.CreateScoreEntry(scoreContainer, string.Empty, string.Empty, "K", index += 1);

                for (int i = 0; i < Mathf.Min(scoreData.Count, 15); i++)
                {
                    EventManager.ScoreEntry score = scoreData[i];
                    //EMInterface.CreateScoreEntry(scoreContainer, score.displayName, string.Empty, ((int)score.value2).ToString(), i + index + 1);
                }
            }

            protected override float GetFirstScoreValue(EventManager.BaseEventPlayer eventPlayer) => 0;

            protected override float GetSecondScoreValue(EventManager.BaseEventPlayer eventPlayer) => eventPlayer.Kills;

            protected override void SortScores(ref List<EventManager.ScoreEntry> list)
            {
                list.Sort(delegate (EventManager.ScoreEntry a, EventManager.ScoreEntry b)
                {
                    return a.value2.CompareTo(b.value2);
                });
            }
            #endregion
        }
        
        public class MurderPlayer : EventManager.BaseEventPlayer
        {
            internal Timer fartTimer;
            internal MurderRole playerRole;
            public void MurderPlayerInit()
            {
                Player.gameObject.AddComponent<DisplayRoleNameBehaviour>();
            }
            internal override void OnPlayerDeath(EventManager.BaseEventPlayer baseattacker = null, float respawnTime = 0f)
            {
                MurderPlayer attacker = baseattacker as MurderPlayer;
                AddPlayerDeath(attacker);
                
                //DestroyUI();
                if(Player.HasComponent<DisplayRoleNameBehaviour>())
                    DestroyImmediate(Player.GetComponent<DisplayRoleNameBehaviour>());
                int position = Event.GetAlivePlayerCount();

                string message = attacker != null ? string.Format(GetMessage("Death.Killed", Player.userID), attacker.Player.displayName, ToOrdinal(position + 1), position) :
                                 IsOutOfBounds ? string.Format(GetMessage("Death.OOB", Player.userID), ToOrdinal(position + 1), position) :
                                 string.Format(GetMessage("Death.Suicide", Player.userID), ToOrdinal(position + 1), position);

                //EMInterface.DisplayDeathScreen(this, message, false);
                //murderer öldüğündeki patlama efekti
                if (playerRole?.playerRole == PlayerRole.Murderer)
                {
                    RunEffect(Player.ServerPosition, "assets/bundled/prefabs/fx/survey_explosion.prefab", Convert.ToUInt32(Event.gameroom.roomID));
                    (Event as MurderGame).murdererCount--;
                }
                else if (playerRole?.playerRole == PlayerRole.Sheriff)
                {
                    (Event as MurderGame).sheriffCount--;
                }
                else if(playerRole?.playerRole == PlayerRole.Bystander)
                {
                    (Event as MurderGame).innocentCount--;
                }

                if (attacker?.playerRole.playerRole == PlayerRole.Murderer)
                {
                    attacker.ResetFartTimer();
                }
                
                //Event handling
                if((Event as MurderGame).murdererCount == 0 && Event.Status == EventManager.EventStatus.Started)
                {
                    (Event as MurderGame).winnerside = EventWinner.Bystanders;
                    Event.RestartEvent();
                }
                else if((Event as MurderGame).bystandersCount == 0 && Event.Status == EventManager.EventStatus.Started)
                {
                    (Event as MurderGame).winnerside = EventWinner.Murderer;
                    Event.RestartEvent();
                }
                fartTimer?.Destroy();

                Player.gameObject.AddComponent<EventManager.RoomSpectatingBehaviour>().Enable(Event);
            }
            internal override void DropBody(HitInfo hitInfo)
            {
                BaseCorpse baseCorpse = Player.CreateCorpse();
                baseCorpse.gameObject.AddComponent<DroppedEventCorpse>().Enable(playerRole);
                if (baseCorpse != null)
                {
                    baseCorpse.ResetRemovalTime(7200f);
                    Event.AddItemToRoom(baseCorpse);
                    if (hitInfo != null)
                    {
                        Rigidbody component = baseCorpse.GetComponent<Rigidbody>();
                        if (component != null)
                        {
                            component.AddForce((hitInfo.attackNormal + Vector3.up * 0.5f).normalized * 1f, ForceMode.VelocityChange);
                        }
                    }
                }
            }

            internal void Fart()
            {
                if (this == null)
                {
                    fartTimer.DestroyToPool();
                    return;
                }
                FartEffect effect = Player.gameObject.AddComponent<FartEffect>();
                effect.InitializeEffect(Convert.ToUInt32(Event.gameroom.roomID));
                fartTimer = Instance.timer.In(2 * 60, Fart);
            }

            internal void StartFartTimer()
            {
                if (playerRole.playerRole == PlayerRole.Murderer)
                {
                    fartTimer = Instance.timer.In(Configuration.startFarting * 60, Fart);
                }
                else
                {
                    FartEffect fartEffect;
                    if (Player.TryGetComponent(out fartEffect))
                    {
                        DestroyImmediate(fartEffect);
                    }
                    fartTimer?.DestroyToPool();
                    fartTimer = null;
                }
            }

            internal void ResetFartTimer()
            {
                FartEffect fartEffect;
                if (Player.TryGetComponent(out fartEffect))
                {
                    DestroyImmediate(fartEffect);
                }
                fartTimer.DestroyToPool();
                fartTimer = Instance.timer.In(Configuration.startFarting * 60, Fart);
            }

            internal bool HasRevolver()
            {
                Item revolver = Player.inventory.containerMain.FindItemsByItemName("pistol.python");
                if (revolver == null)
                    return false;
                return true;
            }

            public class MurderRole : Role
            {
                public PlayerRole playerRole;
                public string roleName;
                public int collectedItems;
                public string roleColor;
                public string roleColorName;
                /// <summary>
                /// Create new murder role
                /// </summary>
                /// <param name="_playerrole">Murderer, Sheriff or Bystander</param>
                /// <param name="_realname">Real name of the player</param>
                /// <param name="_rolename">Sierra, Alpha, Bravo etc.</param>
                /// <param name="_rolecolorname">Blue,pink, yellow etc.</param>
                /// <param name="_roleColor">Hex code of the color</param>
                public MurderRole(PlayerRole _playerrole, string _realname, string _rolename, string _rolecolorname, string _roleColor)
                {
                    playerRole = _playerrole;
                    roleName = _rolename;
                    roleColorName = _rolecolorname;
                    roleColor = _roleColor;
                    realName = _realname;
                }
            }
        }
        public class DisplayRoleNameBehaviour : MonoBehaviour
        {
            private MurderPlayer murderPlayer;
            Timer labeltimer;
            private bool isActive = false;

            private void Awake()
            {
                murderPlayer = GetComponent<MurderPlayer>();
                isActive = true;
            }

            private IEnumerator Start()
            {
                while (isActive)
                {
                    yield return StartCoroutine(CheckEntity());
                    yield return new WaitForSeconds(.2f);
                }
            }

            void OnDestroy()
            {
                labeltimer?.Destroy();
                CuiHelper.DestroyUi(murderPlayer.Player, namelabel_UI);
                StopAllCoroutines();
                isActive = false;
            }
            IEnumerator CheckEntity()
            {
                BaseEntity entity;
                RaycastHit hit;
                bool isHit = Physics.Raycast(murderPlayer.Player.eyes.HeadRay(), out hit);
                if (!isHit)
                    yield break;

                entity = hit.GetEntity();
                if (entity == null)
                {
                    yield break;
                }
                BasePlayer baseopponent = entity as BasePlayer;
                MurderPlayer opponent = baseopponent?.GetComponent<MurderPlayer>();
                if (opponent != null && hit.distance < 16 && opponent.playerRole != null)
                {
                    SendName(opponent);
                    yield break;
                }
                BaseCorpse droppedcorpse = entity as BaseCorpse;
                MurderPlayer.MurderRole corpseRole = droppedcorpse?.gameObject.GetComponent<DroppedEventCorpse>()?.corpseRole;
                if (corpseRole != null && hit.distance < 2 && murderPlayer?.playerRole?.playerRole == PlayerRole.Murderer)
                {
                    SendCorpseName(corpseRole);
                    yield break;
                }
            }
            void SendName(MurderPlayer opponent)
            {
                CuiHelper.DestroyUi(murderPlayer.Player, namelabel_UI);
                CuiElementContainer container =
                    UI.Container(namelabel_UI, "0 0 0 0", UI.TransformToUI4(853f, 1077f, 449f, 525f));
                UI.Label(container,namelabel_UI,opponent.playerRole.roleName,13,UI.TransformToUI4(0f,224f,22f,46f,224f,76f),TextAnchor.UpperCenter,UI.Color(opponent.playerRole.roleColor,1f));
                CuiHelper.AddUi(murderPlayer.Player, container);
                DestroyLabelDelayed(3f);
            }

            void SendCorpseName(MurderPlayer.MurderRole corpseRole)
            {
                CuiHelper.DestroyUi(murderPlayer.Player, namelabel_UI);
                CuiElementContainer container =
                    UI.Container(namelabel_UI, "0 0 0 0", UI.TransformToUI4(853f, 1077f, 449f, 525f));
                UI.Label(container,namelabel_UI,"Press E to Disguise",13,UI.TransformToUI4(0f,224f,40f,76f,224f,76f));
                UI.Label(container,namelabel_UI,corpseRole.roleName,13,UI.TransformToUI4(0f,224f,22f,46f,224f,76f),TextAnchor.UpperCenter,corpseRole.roleColor);
                CuiHelper.AddUi(murderPlayer.Player, container);
                DestroyLabelDelayed(3f);
            }
            void DestroyLabelDelayed(float delay)
            {
                if (labeltimer != null)
                    labeltimer.Destroy();
                labeltimer = Instance.timer.Once(delay, () =>
                {
                    CuiHelper.DestroyUi(murderPlayer.Player, namelabel_UI);
                });
            }
        }
        #endregion

        #region Classes

        public class DroppedEventCorpse : MonoBehaviour
        {
            public MurderPlayer.MurderRole corpseRole;

            public void Enable(MurderPlayer.MurderRole _corpseRole)
            {
                corpseRole = _corpseRole;
            }
        }
        public class FartEffect : MonoBehaviour
        {
            public BasePlayer player;
            public string effect;
            public Vector3 effectPosition;
            public float repeattime;
            public int toberepeated;
            public int repeated;
            private uint groupID;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                effect = "assets/bundled/prefabs/fx/door/barricade_spawn.prefab";
                effectPosition = Vector3.zero;
                repeattime = 1f;
                toberepeated = 20;
            }

            public void InitializeEffect(uint _groupID)
            {
                groupID = _groupID;
                RunTimer();
            }

            public void RunTimer() => InvokeRepeating("ApplyEffect", 0.2f, repeattime);

            public void DestroyTimer() => CancelInvoke("ApplyEffect");

            private void ApplyEffect()
            {
                if (string.IsNullOrEmpty(effect) || player == null)
                    return;
                if (repeated > toberepeated)
                {
                    DestroyImmediate(this);
                    return;
                }
                
                RunEffect(player.ServerPosition, effect, groupID);
                repeated++;
            }

            private void OnDestroy()
            {
                DestroyTimer();
                Destroy(this); 
            }
        }
        
        #endregion

        #region API

        [HookMethod("GetRealPlayerName")]
        object GetRealPlayerName(BasePlayer player)
        {
            if (player == null)
                return null;
            MurderPlayer murderPlayer;
            player.TryGetComponent(out murderPlayer);

            if (murderPlayer == null || murderPlayer.playerRole == null)
                return player.displayName;
            else
                return murderPlayer.playerRole.realName;
        }
        
        #endregion
        
        #region Event Checks
        public bool InitializeEvent(EventManager.EventConfig config, EventManager.GameRoom room) => EventManager.InitializeEvent<MurderGame>(this, config, room);

        public bool CanUseClassSelector => true;

        public bool RequireTimeLimit => false;

        public bool RequireScoreLimit => false;

        public bool UseScoreLimit => false;

        public bool UseTimeLimit => false;

        public bool IsTeamEvent => false;

        public void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2)
        {
            score1 = string.Empty;
            score2 = string.Format(Message("Score.Kills", langUserId), scoreEntry.value2);
        }

        public List<EventManager.EventParameter> AdditionalParameters { get; } = null;

        public string ParameterIsValid(string fieldName, object value) => null;
        #endregion

        #region Config        
        private static ConfigData Configuration;
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Respawn time (seconds)")]
            public int RespawnTime { get; set; }

            public Oxide.Core.VersionNumber Version { get; set; }

            [JsonProperty(PropertyName = "Sheriff Number per Room")]
            public int sheriffNum { get; set; }

            [JsonProperty(PropertyName = "Murderer number per Room(Changing is not recommended)")]
            public int murdererNum { get; set; }

            [JsonProperty("Role name i.e.'Delta','Echo','India'")]
            public List<string> roleNames { get; set; }

            [JsonProperty("Role Colors specified for every person")]
            public Hash<string, string> roleColors { get; set; }

            [JsonProperty("Randomly given items")]
            public List<int> randomItemIDs { get; set; }

            [JsonProperty("Collectable Item Spawn Delay")]
            public float itemspawndelay { get; set; }

            [JsonProperty("Needed item count for giving revolver")]
            public int neededCollectableforRevolver { get; set; }

            [JsonProperty("Colored attires (Used for changing attires color by player role)")]
            public Hash<string, Hash<string, ulong>> coloredAttires { get; set; }
            
            [JsonProperty("Wounded state time duration")]
            public float woundedDuration { get; set; }
            [JsonProperty("Murderer start farting after ... minutes")]
            public float startFarting { get; set; }
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
                RespawnTime = 0, // Auto Respawning Disabled.
                Version = Version,
                itemspawndelay = 20f,
                sheriffNum = 2,
                murdererNum = 1,
                neededCollectableforRevolver = 5,
                roleNames = new List<string>
                {
                    "Echo", "India", "Golf", "X-Ray", "Miko", "Tango", "November", "Quebec", "Delta", "Alfa", "Uniform", "Yankee", "Victor", "Romeo", "Lima", "Papa",
                    "Juliett", "Charlie", "Kilo", "Whiskey", "Hotel", "Bravo", "Foxtrot", "Sierra"
                },
                roleColors = new Hash<string, string>
                {
                    new KeyValuePair<string, string>("red","#ED2A00"),
                    new KeyValuePair<string, string>("yellow","#d1cf02"),
                    new KeyValuePair<string, string>("lightblue","#5ddcdd"),
                    new KeyValuePair<string, string>("darkblue","#095fbf"),
                    new KeyValuePair<string, string>("lightgreen","#55f936"),
                    new KeyValuePair<string, string>("purple","#8a2be2"),
                    new KeyValuePair<string, string>("orange","#ffa500"),
                    new KeyValuePair<string, string>("green","#117c00"),
                    new KeyValuePair<string, string>("grey","#989898"),
                    new KeyValuePair<string, string>("pink","#f1b2ca")
                },
                coloredAttires= new Hash<string, Hash<string, ulong>>()
                {
                    new KeyValuePair<string, Hash<string,ulong>>("tshirt", new Hash<string, ulong>()
                    {
                        new KeyValuePair<string, ulong>("red", 2839168580),
                        new KeyValuePair<string, ulong>("yellow",2839170134),
                        new KeyValuePair<string, ulong>("lightblue", 2839171538),
                        new KeyValuePair<string, ulong>("darkblue",2839165971),
                        new KeyValuePair<string, ulong>("lightgreen",2839177002),
                        new KeyValuePair<string, ulong>("purple",2839173174),
                        new KeyValuePair<string, ulong>("orange",2839173910),
                        new KeyValuePair<string, ulong>("green",2839172484),
                        new KeyValuePair<string, ulong>("grey", 2839177513),
                        new KeyValuePair<string, ulong>("pink", 2839178447)
                    }),
                    new KeyValuePair<string, Hash<string, ulong>>("pants", new Hash<string, ulong>()
                    {
                        new KeyValuePair<string, ulong>("red", 2840394833),
                        new KeyValuePair<string, ulong>("yellow",2840393403),
                        new KeyValuePair<string, ulong>("lightblue",2840392566),
                        new KeyValuePair<string, ulong>("darkblue",2840391436),
                        new KeyValuePair<string, ulong>("lightgreen",2840390039),
                        new KeyValuePair<string, ulong>("purple",2840389295),
                        new KeyValuePair<string, ulong>("orange",2840388460),
                        new KeyValuePair<string, ulong>("green",2840387574),
                        new KeyValuePair<string, ulong>("grey",2840385701),
                        new KeyValuePair<string, ulong>("pink",2840384961)
                    }),
                    new KeyValuePair<string, Hash<string, ulong>>("shirt.collared", new Hash<string, ulong>()
                    {
                        new KeyValuePair<string, ulong>("red", 2904441606),
                        new KeyValuePair<string, ulong>("yellow",2904442305),
                        new KeyValuePair<string, ulong>("lightblue",2904442662),
                        new KeyValuePair<string, ulong>("darkblue",2904443076),
                        new KeyValuePair<string, ulong>("lightgreen",2904443724),
                        new KeyValuePair<string, ulong>("purple",2904444257),
                        new KeyValuePair<string, ulong>("orange",2904444958),
                        new KeyValuePair<string, ulong>("green",2904445390),
                        new KeyValuePair<string, ulong>("grey",2904449179),
                        new KeyValuePair<string, ulong>("pink",2904449577)
                    })
                },
                randomItemIDs = new List<int>() 
                {
                   -1651220691,//pookiebear
                    1668858301,//smallstocking
                    613961768,//botabag
                    -1824943010,//jackolantern
                    634478325,//cctvcam
                    -602717596,//reddogtags
                    -932201673,//scrap
                    -1779183908,//paper
                    -1941646328,//tuna can
                    317398316,//hq metal
                    -592016202,//explosives
                    1381010055,//leather
                    1397052267,//Supply signal
                    -2001260025,//instant camera
                    1975934948,//survey charge
                    1973165031,//birthday cake
                    304481038,//flare
                    -2072273936,//bandage
                    1548091822,//apple
                    342438846,//anchovy
                    621915341,//raw pork
                    1121925526,//candy cane
                    -1039528932,//small water bottle
                    -119235651,//waterjug
                    -1843426638,//mlrs rocket
                    -484206264,//blue keycard
                    -1880870149,//red keycard
                    756517185,//medium present
                    -1622660759,//large present
                    -722241321,//small present
                    -1368584029,//sickle
                    844440409,//bronze egg
                    -1002156085,//gold egg
                    -936921910,//flasbang
                    -363689972,//snowball
                    1965232394,//crossbow
                    1401987718,//duct tape
                    1553078977,//bleach
                    479143914,//gears
                    656371026,//hq carburetor
                    1158340332,//hq crankshaft
                    1883981800,//hq pistons
                    1072924620,//hq spark plugs
                    -1802083073,//hq valves
                    1882709339,//metal blade
                    95950017,//metal pipe
                    1414245522,//rope
                    1199391518,//road signs
                    1234880403,//sewing kit
                    2019042823,//tarp
                    1784406797,//sousaphone
                    576509618,//portable boombox
                    -1379036069,//canbourine
                    -20045316,//mobile phone
                    -2040817543,//pan flute
                    -583379016,//megaphone
                    -1049881973,//cowbell
                    476066818,//cassette long
                    -912398867,//cassette medium
                    1523403414,//cassette short
                    -2107018088 //shovel bass
                },
                woundedDuration = 25f,
                startFarting = 5f
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Localization
        public string Message(string key, ulong playerId = 0U) => lang.GetMessage(key, this, playerId != 0U ? playerId.ToString() : null);

        private static Func<string, ulong, string> GetMessage;

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Score.Kills"] = "Kills: {0}",
            ["Score.Name"] = "Kills",
            ["Score.Remaining"] = "Players Remaining : {0}",
            ["Death.Killed"] = "You were killed by {0}\nYou placed {1}\n{2} players remain",
            ["Death.Suicide"] = "You died...\nYou placed {0}\n{1} players remain",
            ["Death.OOB"] = "You left the playable area\nYou placed {0}\n{1} players remain",
            ["NeededPlayer"] = "{0} more players needed to start the game",
            ["NotInRoom"] = "You are not in a room",
            ["LeftTheRoom"] = "You left the room",
            ["NoLongerWounded"] = "You are no longer wounded"
        };
        #endregion

        #region Enums
        public enum EventWinner { Murderer, Bystanders, None }
        public enum PlayerRole { Murderer, Sheriff, Bystander }
        #endregion
    }
}