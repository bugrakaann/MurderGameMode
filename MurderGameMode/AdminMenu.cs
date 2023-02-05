// Requires: ImageLibrary
//Requires: EnhancedBanSystem

using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using System;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;
using CurrentEventInfo = Oxide.Plugins.EventManager.CurrentEventInfo;
using BaseEventPlayer = Oxide.Plugins.EventManager.BaseEventPlayer;
using BaseEventGame = Oxide.Plugins.EventManager.BaseEventGame;
using RoomSpectatingBehaviour = Oxide.Plugins.EventManager.RoomSpectatingBehaviour;
using MurderPlayer = Oxide.Plugins.MurderGameMode.MurderPlayer;
using MurderGame = Oxide.Plugins.MurderGameMode.MurderGame;
using PlayerRole = Oxide.Plugins.MurderGameMode.PlayerRole;

using BanData = Oxide.Plugins.EnhancedBanSystem.BanData;

namespace Oxide.Plugins
{
    [Info("Admin Menu", "Apwned", "1.0.0")]
    [Description("Admin Menu integrated with EventManager")]
    public class AdminMenu : RustPlugin
    {
        #region Fields
        [PluginReference] private Plugin ImageLibrary,MurderGameMode,PlayerDatabase,BetterChatMute;
        private static AdminMenu Instance;
        
        //Perms
        private const string ADMINMENU_PERMISSION = "murderadminmenu.use";
        private const string ACTION_PERMISSION = "murderadminmenu.action";
        //UI Parents
        private const string spectatemenu_UI = "murderadminmenu.spectatemenu";
        #endregion

        #region Enums
        
        public enum MenuTab {ActivePlayers,AllPlayers,Player,Rooms,Room,ConVars}
        //Tabs with menu in player tab
        public enum PlayerTab {None, MuteMenu,BanMenu,ReasonMenu}
        //seconds
        public enum BlockDuration {None,Mns30 =1800,Hr1 = 3600,Hrs3 = 10800,Day1=86400,Week1 =604800,Permanent = 0}
        public enum Action {None,Unban,Ban,Mute,UnMute,Kick,Teleport,Spectate,FinishSpectating,StripInventory,CloseRoom}
        public enum Reason {Advertising,Cheating,Griefing,RuleViolation,Racism,Spamming,SusGamePlay,ToxicGamePlay}

        #endregion
        
        #region Hooks

        void Init()
        {
            Instance = this;
        }

        void OnServerInitialized()
        {
            RegisterImages();
        }
        
        void Loaded()
        {
            permission.RegisterPermission(ACTION_PERMISSION,this);
            permission.RegisterPermission(ADMINMENU_PERMISSION,this);
        }
        #endregion

        void RegisterImages()
        {
            AddImage("unknownprofilephoto","https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/b5/b5bd56c1aa4644a474a2e4972be27ef9e82e517e_full.jpg");
        }

        private Dictionary<ulong, string[]> lastPage = new Dictionary<ulong, string[]>();
        [ChatCommand("murderadminmenu")]
        void OpenAdminMenu(BasePlayer player)
        {
            player.SendConsoleCommand("murderadminmenu.ui");
        }
        [ConsoleCommand("murderadminmenu.ui")]
        void AdminMenuUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!permission.UserHasPermission(player.UserIDString, ADMINMENU_PERMISSION))
                return;
            MenuTab menuTab = (MenuTab)arg.GetInt(0);
            
            switch (menuTab)
            {
                case MenuTab.Player:
                    PlayerTab playerTab = (PlayerTab)arg.GetInt(1);
                    
                    string userID = arg.GetString(2);
                    Action action = (Action)arg.GetInt(3);
                    BlockDuration blockDuration = (BlockDuration)arg.GetInt(4);
                    OpenMenu(player,new MenuArgs(playerTab,userID,action,blockDuration));
                    break;
                case MenuTab.ActivePlayers:
                case MenuTab.AllPlayers:
                    int pageNum = arg.GetInt(1);
                    OpenMenu(player,new MenuArgs(menuTab,pageNum));
                    break;
                case MenuTab.Room:
                    ulong ID = arg.GetUInt64(1);
                    int pageNum1 = arg.GetInt(2);
                    OpenMenu(player,new MenuArgs(ID,pageNum1));
                    break;
                case MenuTab.Rooms:
                    int pageNum2 = arg.GetInt(1);
                    OpenMenu(player,new MenuArgs(menuTab,pageNum2));
                    break;
            }
            lastPage[player.userID] = arg.Args;
        }

        [ConsoleCommand("murderadminmenu.refresh")]
        void RefreshAdminMenu(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (lastPage.ContainsKey(player.userID))
            {
                player.SendConsoleCommand("murderadminmenu.ui",lastPage[player.userID]);
            }
        }

        void RefreshAdminMenu(BasePlayer player)
        {
            player.SendConsoleCommand("murderadminmenu.ui",lastPage[player.userID]);
        }

        [ConsoleCommand("murderadminmenu.action")]
        void AdminMenuAction(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!permission.UserHasPermission(player.UserIDString, ACTION_PERMISSION))
                return;

            Action action = (Action)arg.GetInt(0);
            switch (action)
            {
                case Action.Ban:
                    ulong userID = arg.GetULong(1);
                    BlockDuration duration = (BlockDuration)arg.GetInt(2);
                    string reason = arg.GetString(3);
                    BanPlayer(userID,reason,duration);
                    break;
                case Action.Unban:
                    ulong userID1 = arg.GetULong(1);
                    UnBanPlayer(userID1);
                    break;
                case Action.Kick:
                    ulong userID2 = arg.GetULong(1);
                    string reason2 = arg.GetString(3);
                    KickPlayer(userID2,reason2,player);
                    break;
                case Action.Teleport:
                    ulong userID3 = arg.GetULong(1);
                    TransportToPlayer(userID3,player);
                    break;
                case Action.StripInventory:
                    ulong userID4 = arg.GetULong(1);
                    StripInventory(userID4,player);
                    break;
                case Action.CloseRoom:
                    int roomID = arg.GetInt(1);
                    CloseRoom(roomID,player);
                    break;
                case Action.Mute:
                    ulong userID5 = arg.GetULong(1);
                    BlockDuration duration2 = (BlockDuration)arg.GetInt(2);
                    string reason3 = arg.GetString(3);
                    MutePlayer(userID5,player,duration2,reason3);
                    break;
                case Action.UnMute:
                    ulong userID6 = arg.GetULong(1);
                    UnMutePlayer(userID6,player);
                    break;
                case Action.Spectate:
                    ulong userID7 = arg.GetULong(1);
                    SpectatePlayer(player,userID7);
                    break;
                case Action.FinishSpectating:
                    FinishSpectating(player);
                    break;
            }
            RefreshAdminMenu(player);
        }

        [ConsoleCommand("murderadminmenu.close")]
        void cmdCloseMenu(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, spectatemenu_UI);
            if (lastPage.ContainsKey(player.userID))
                lastPage.Remove(player.userID);
        }

        void OpenMenu(BasePlayer player, MenuArgs args)
        {
            CuiHelper.DestroyUi(player, spectatemenu_UI);
            CuiElementContainer container = UI.Container(spectatemenu_UI, "0 0 0 1",
                UI.TransformToUI4(99f, 1821, 75f, 1004f), true, "Overall");
            UI.Label(container,spectatemenu_UI,"ADMIN MENU",14,UI.TransformToUI4(45,300f,845f,925f,1722f,929f),TextAnchor.MiddleLeft,"1 1 1 1","PermanentMarker.ttf");
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),"X",12,UI.TransformToUI4(1655f,1702f,869f,906f,1722f,929f),"murderadminmenu.close",TextAnchor.MiddleCenter,"PermanentMarker.ttf");
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),"↻",20,UI.TransformToUI4(1600f,1647f,869f,906f,1722f,929f),"murderadminmenu.refresh");
            UI.Panel(container,spectatemenu_UI,"1 1 1 1",UI.TransformToUI4(49f,1673f,842f,844f,1722f,929f));
            CreateMenuLabels(container,args);

            switch (args.Menu)
            {
                case MenuTab.Player:
                    CreatePlayerTab(container,args,player,Convert.ToUInt64(args.playerTabArgs.targetPlayerID));
                    break;
                case MenuTab.ActivePlayers:
                    CreateActivePlayersTab(container,args);
                    break;
                case MenuTab.Room:
                    CreateRoomTab(container,args);
                    break;
                case MenuTab.Rooms:
                    CreateRoomsTab(container,args);
                    break;
                case MenuTab.AllPlayers:
                    CreateAllPlayersTab(container,args);
                    break;
            }
            
            CuiHelper.AddUi(player, container);
        }

        void CreateMenuLabels(CuiElementContainer container,MenuArgs args)
        {
            UI.Button(container,spectatemenu_UI,UI.Color(args.Menu == MenuTab.ActivePlayers ? configData.highlightedLabelColor: configData.labelColor,1f),"Active Players",14,UI.TransformToUI4(337f,337f + 189f,869f,869f + 37f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.ActivePlayers} ");
            UI.Button(container,spectatemenu_UI,UI.Color(args.Menu == MenuTab.AllPlayers ? configData.highlightedLabelColor: configData.labelColor,1f),"All Players",14,UI.TransformToUI4(337f + 205f,337f + 189f + 205f,869f,869f + 37f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.AllPlayers}");
            UI.Button(container,spectatemenu_UI,UI.Color(args.Menu == MenuTab.Rooms ? configData.highlightedLabelColor: configData.labelColor,1f),"Rooms",14,UI.TransformToUI4(337f + 410f,337f + 189f + 410f,869f,869f + 37f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.Rooms}");
        }

        void CreatePlayerTab(CuiElementContainer container, MenuArgs args,BasePlayer admin,ulong steamID)
        {
            BasePlayer player = BasePlayer.FindByID(steamID);
            
            if (player != null)
            {
                //Profile photo
                UI.Image(container,spectatemenu_UI,GetImage(player.UserIDString),UI.TransformToUI4(67f,67f + 288f,517f,517f + 288f,1722f,929f));

                UI.Label(container,spectatemenu_UI,GetPlayerDetailsLeft(player),15,UI.TransformToUI4(436f,1105f,500f,800f,1722f,929f),TextAnchor.UpperLeft);
                UI.Label(container,spectatemenu_UI,GetPlayerDetailsRight(player),15,UI.TransformToUI4(1030f,1677f,500f,800f,1722f,929f),TextAnchor.UpperLeft);
                
                int xindex = 0; float xoffset = 307f / 1920f;
                int yindex = 0; float yoffset = 67f / 1080f;
                UI4 ui4 = UI.TransformToUI4(55f, 355f, 404f, 458f, 1722f, 929f);
                UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Kick", 12,
                    ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Kick} 0");
                if(!IsPlayerBanned(player.userID))
                    UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Ban", 12,
                        ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.BanMenu} {player.UserIDString}");
                else
                    UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Unban", 12,
                        ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.action {(int)Action.Unban} {player.UserIDString}");
                    
                UI.Button(container,spectatemenu_UI, UI.Color(configData.buttonColor, 1f),"Strip Inventory",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.action {(int)Action.StripInventory} {player.UserIDString}");
                if(!IsPlayerMuted(player.userID))
                    UI.Button(container,spectatemenu_UI, UI.Color(configData.buttonColor, 1f),"Mute",12,ui4.SetOffset(yoffset, yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.MuteMenu} {player.UserIDString}");
                else
                    UI.Button(container,spectatemenu_UI, UI.Color(configData.buttonColor, 1f),"Unmute",12,ui4.SetOffset(yoffset, yindex++),$"murderadminmenu.action {(int)Action.UnMute} {player.UserIDString}");
                UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Teleport", 12,
                    ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.action {(int)Action.Teleport} {player.UserIDString}");
                if(!IsPlayerSpectating(admin))
                    UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Spectate", 12,
                    ui4.SetOffset(yoffset, yindex++),$"murderadminmenu.action {(int)Action.Spectate} {player.UserIDString}");
                    else
                UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Finish Spectating", 12,
                    ui4.SetOffset(yoffset, yindex++),$"murderadminmenu.action {(int)Action.FinishSpectating}");
                UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Room", 12,
                    ui4.SetOffset(yoffset, yindex++,xoffset,xindex),$"murderadminmenu.ui {(int)MenuTab.Room} {player.UserIDString} 0");
            }
            else
            {
                UI.Image(container,spectatemenu_UI,GetImage(steamID.ToString()),UI.TransformToUI4(67f,67f + 288f,517f,517f + 288f,1722f,929f));
                UI.Label(container,spectatemenu_UI,GetPlayerDetailsLeft(steamID),15,UI.TransformToUI4(436f,1105f,500f,800f,1722f,929f),TextAnchor.UpperLeft);
                
                int xindex = 0; float xoffset = 307f / 1920f;
                int yindex = 0; float yoffset = 67f / 1080f;
                UI4 ui4 = UI.TransformToUI4(55f, 355f, 404f, 458f, 1722f, 929f);
                if(!IsPlayerBanned(steamID))
                    UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Ban", 12,
                        ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.BanMenu} {steamID}");
                else
                    UI.Button(container, spectatemenu_UI, UI.Color(configData.buttonColor, 1f), "Unban", 12,
                        ui4.SetOffset(yoffset, yindex++), $"murderadminmenu.action {(int)Action.Unban} {steamID}");
                
            }
            CreatePlayerSmallMenu(container,args,player);
        }
        void CreatePlayerSmallMenu(CuiElementContainer container, MenuArgs args, BasePlayer player)
        {
            if (args.playerTabArgs.playerTab == PlayerTab.None)
                return;
            const string smallMenu = "murderadminmenu.playertab.smallmenu";
            UI.Panel(container,spectatemenu_UI,smallMenu,UI.Color("#363636",1f),UI.TransformToUI4(878f,1649f,100f,456f,1722f, 929f));

            switch (args.playerTabArgs.playerTab)
            {
                case PlayerTab.BanMenu:
                    UI.Label(container,smallMenu,"Ban Menu",12,UI.TransformToUI4(260f,511f,285f,352f,771f, 356f));
                    UI4 ui4 = UI.TransformToUI4(41f, 334f, 242f, 284f, 771f, 356f);

                    int yindex = 0; float yoffset = 49f/356f;
                    int xindex = 0; float xoffset = 379f/771f;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban for 30 minutes",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Mns30}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban for 1 hour",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Hr1}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban for 3 hours",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Hrs3}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban for 1 day",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Day1}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban for 1 week",12,ui4.SetOffset(yoffset,yindex++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Week1}");
                    yindex = 0; xindex++;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Ban Permanently",12,ui4.SetOffset(yoffset,yindex++,xoffset,xindex),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Ban} {(int)BlockDuration.Permanent}");
                    break;
                case PlayerTab.MuteMenu:
                    UI.Label(container,smallMenu,"Mute Menu",12,UI.TransformToUI4(260f,511f,285f,352f,771f, 356f));
                    UI4 ui4_1 = UI.TransformToUI4(41f, 334f, 242f, 284f, 771f, 356f);

                    int yindex_1 = 0; float yoffset_1 = 49f/356f;
                    int xindex_1 = 0; float xoffset_1 = 379f/771f;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute for 30 minutes",12,ui4_1.SetOffset(yoffset_1,yindex_1++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Mns30}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute for 1 hour",12,ui4_1.SetOffset(yoffset_1,yindex_1++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Hr1}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute for 3 hours",12,ui4_1.SetOffset(yoffset_1,yindex_1++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Hrs3}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute for 1 day",12,ui4_1.SetOffset(yoffset_1,yindex_1++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Day1}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute for 1 week",12,ui4_1.SetOffset(yoffset_1,yindex_1++),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Week1}");
                    yindex_1 = 0; xindex_1++;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Mute Permanently",12,ui4_1.SetOffset(yoffset_1,yindex_1++,xoffset_1,xindex_1),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.ReasonMenu} {player.UserIDString} {(int)Action.Mute} {(int)BlockDuration.Permanent}");
                    break;
                case PlayerTab.ReasonMenu:
                    UI.Label(container,smallMenu,"Reason Menu",12,UI.TransformToUI4(260f,511f,285f,352f,771f, 356f));
                    UI4 ui4_2 = UI.TransformToUI4(41f, 334f, 242f, 284f, 771f, 356f);

                    int yindex_2 = 0; float yoffset_2 = 49f/356f;
                    int xindex_2 = 0; float xoffset_2 = 379f/771f;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Advertising",12,ui4_2.SetOffset(yoffset_2,yindex_2++),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.Advertising}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Cheating",12,ui4_2.SetOffset(yoffset_2,yindex_2++),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.Cheating}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Griefing",12,ui4_2.SetOffset(yoffset_2,yindex_2++),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.Griefing}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Racism",12,ui4_2.SetOffset(yoffset_2,yindex_2++),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.Racism}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Spamming",12,ui4_2.SetOffset(yoffset_2,yindex_2++),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.Spamming}");
                    yindex_2 = 0; xindex_2++;
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Rule Violation",12,ui4_2.SetOffset(yoffset_2,yindex_2++,xoffset_2,xindex_2),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.RuleViolation}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Suspicious Game Play",12,ui4_2.SetOffset(yoffset_2,yindex_2++,xoffset_2,xindex_2),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.SusGamePlay}");
                    UI.Button(container,smallMenu,UI.Color(configData.buttonColor,1f),"Toxic Game Play",12,ui4_2.SetOffset(yoffset_2,yindex_2++,xoffset_2,xindex_2),$"murderadminmenu.action {(int)args.playerTabArgs.action} {player.UserIDString} {(int)args.playerTabArgs.blockDuration} {Reason.ToxicGamePlay}");
                    break;
            }
        }

        void CreateActivePlayersTab(CuiElementContainer container, MenuArgs args)
        {
            if (args.Page == -1)
                args.Page = 0;
            else if (args.Page * 75 > BasePlayer.activePlayerList.Count())
                args.Page--;
            UI.Label(container,spectatemenu_UI,args.Page.ToString(),10,UI.TransformToUI4(1430f,1495f,869f,906f,1722f,929f));
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),"<",10,UI.TransformToUI4(1388f,1435f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.ActivePlayers} {args.Page - 1}");
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),">",10,UI.TransformToUI4(1500f,1547f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.ActivePlayers} {args.Page + 1}");

            UI4 labelpos = UI.TransformToUI4(49f, 363f, 756f, 803f, 1722f, 929f);

            for (int i = args.Page * 75; i < (args.Page + 1) * 75; i++)
            {
                if ((args.Page * 75) + i >= BasePlayer.activePlayerList.Count() || !BasePlayer.activePlayerList.Any())
                    break;
                BasePlayer target = BasePlayer.activePlayerList.ToList()[i];
                UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),GetRealName(target),10,labelpos.SetOffset(55f/1024f,i % 15,360f/1920f,i/15),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.None} {target.UserIDString}");
            }
        }
        void CreateAllPlayersTab(CuiElementContainer container, MenuArgs args)
        {
            List<string> allPlayers = PlayerDatabase.Call("GetAllKnownPlayers") as List<string>;
            if (allPlayers == null)
                return;
            if (args.Page == -1)
                args.Page = 0;
            else if (args.Page * 75 > allPlayers.Count())
                args.Page--;
            UI.Label(container,spectatemenu_UI,args.Page.ToString(),10,UI.TransformToUI4(1430f,1495f,869f,906f,1722f,929f));
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),"<",10,UI.TransformToUI4(1388f,1435f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.AllPlayers} {args.Page - 1}");
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),">",10,UI.TransformToUI4(1500f,1547f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.AllPlayers} {args.Page + 1}");

            UI4 labelpos = UI.TransformToUI4(49f, 363f, 756f, 803f, 1722f, 929f);

            for (int i = args.Page * 75; i < (args.Page + 1) * 75; i++)
            {
                if ((args.Page * 75) + i >= allPlayers.Count() || !allPlayers.Any())
                    break;
                string targetID = allPlayers[i];
                string name = PlayerDatabase.Call("GetPlayerData", targetID, "name") as string ?? "Unknown";
                UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),targetID +":"+name,10,labelpos.SetOffset(55f/1024f,i % 15,360f/1920f,i/15),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.None} {targetID}");
            }
        }

        void CreateRoomTab(CuiElementContainer container, MenuArgs args)
        {
            MurderGame room;
            ulong ID = args.roomTabArgs.ID;
            BasePlayer searchedPlayer = BasePlayer.FindByID(ID);
            if (searchedPlayer != null)
                room = (MurderGame)EventManager.GetRoomofPlayer(searchedPlayer);
            else
            {
                BaseEventGame result;
                EventManager.BaseManager.TryGetValue(Convert.ToInt32(ID),out result);
                room = result as MurderGame;
            }

            if (room == null)
            {
                UI.Label(container,spectatemenu_UI,"Room not found",12,UI.TransformToUI4(49f, 328f, 752f, 803f, 1722f, 929f));
                return;
            }
            float yoffset = 63f/1080f;
            UI4 buttontransform = UI.TransformToUI4(49f, 363f, 756f, 803f, 1722f, 929f);
            UI4 label1 = UI.TransformToUI4(330f, 540f, 745f, 813f, 1722f, 929f);
            UI4 label2 = UI.TransformToUI4(515f, 725f, 745f, 813f, 1722f, 929f);
            IList<BasePlayer> players = EventManager.GetPlayersOfRoom(room);
            for (int i = 0; i < players.Count; i++)
            {
                BasePlayer target = players[i];
                if (target == null)
                    continue;
                MurderPlayer murderPlayer;
                target.TryGetComponent(out murderPlayer);
                if (murderPlayer == null)
                {
                    UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),GetRealName(target),11,buttontransform.SetOffset(yoffset,i),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.None} {target.UserIDString}");
                    UI.Label(container,spectatemenu_UI,"Spectating",11,label1.SetOffset(yoffset,i));
                }
                else if (murderPlayer != null && murderPlayer.playerRole != null)
                {
                    UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),GetRealName(target),11,buttontransform.SetOffset(yoffset,i),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.None} {target.UserIDString}");
                    UI.Label(container,spectatemenu_UI,murderPlayer.playerRole?.roleName,11,label1.SetOffset(yoffset,i),TextAnchor.MiddleCenter,UI.Color(murderPlayer.playerRole.roleColor,1f));
                    if(murderPlayer.playerRole.playerRole == PlayerRole.Murderer)
                        UI.Label(container,spectatemenu_UI,"Murderer",11,label2.SetOffset(yoffset,i),TextAnchor.MiddleCenter,UI.Color("#ff0000",1f));
                    else if(murderPlayer.playerRole.playerRole == PlayerRole.Sheriff)
                        UI.Label(container,spectatemenu_UI,"Sheriff",11,label2.SetOffset(yoffset,i),TextAnchor.MiddleCenter,UI.Color("#62acf7",1f));
                    else
                        UI.Label(container,spectatemenu_UI,"Innocent",11,label2.SetOffset(yoffset,i));
                }
                else
                {
                    UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),GetRealName(target),11,buttontransform.SetOffset(yoffset,i),$"murderadminmenu.ui {(int)MenuTab.Player} {(int)PlayerTab.None} {target.UserIDString}");
                    UI.Label(container,spectatemenu_UI,"Joining",11,label1.SetOffset(yoffset,i),TextAnchor.MiddleCenter,UI.Color("#b1b1b1",1f));
                }
            }
            
            if(players.Count == 0)
                UI.Label(container,spectatemenu_UI,"No players found",16,UI.TransformToUI4(49f, 363f, 756f, 803f, 1722f, 929f),TextAnchor.MiddleCenter,UI.Color("#fc0000",1f));
            
            UI.Label(container,spectatemenu_UI,GetRoomDetails(room),15,UI.TransformToUI4(1045f,1574f,276f,785f,1722f, 929f),TextAnchor.UpperLeft);
            UI.Button(container,spectatemenu_UI,UI.Color("#c80000",1f),"Close Room",12,UI.TransformToUI4(1499f,1689f,27f,76f,1722f, 929f),$"murderadminmenu.action {(int)Action.CloseRoom} {room.gameroom.roomID}");
        }

        void CreateRoomsTab(CuiElementContainer container, MenuArgs args)
        {
            List<BaseEventGame> rooms = EventManager.BaseManager.Values.ToList();
            if (args.Page == -1)
                args.Page = 0;
            else if (args.Page * 75 > rooms.Count)
                args.Page--;
            UI.Label(container,spectatemenu_UI,args.Page.ToString(),10,UI.TransformToUI4(1430f,1495f,869f,906f,1722f,929f));
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),"<",10,UI.TransformToUI4(1388f,1435f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.Rooms} {args.Page - 1}");
            UI.Button(container,spectatemenu_UI,UI.Color(configData.labelColor,1f),">",10,UI.TransformToUI4(1500f,1547f,869f,906f,1722f,929f),$"murderadminmenu.ui {(int)MenuTab.Rooms} {args.Page + 1}");

            UI4 labelpos = UI.TransformToUI4(49f, 363f, 756f, 803f, 1722f, 929f);

            for (int i = args.Page * 75; i < (args.Page + 1) * 75; i++)
            {
                if ((args.Page * 75) + i >= rooms.Count || !rooms.Any())
                    break;
                BaseEventGame target = rooms[i];
                UI.Button(container,spectatemenu_UI,UI.Color("#575757",1f),target.gameroom.roomID + ":" + (target.gameroom.isCreatedbySystem?"System":target.gameroom.ownerName),10,labelpos.SetOffset(55f/1024f,i % 15,360f/1920f,i/15),$"murderadminmenu.ui {(int)MenuTab.Room} {target.gameroom.roomID} 0");
            }
        }
 
        private struct MenuArgs
        {
            public int Page;
            public MenuTab Menu;
            public PlayerTabArgs playerTabArgs;
            public RoomTabArgs roomTabArgs;
            
            public MenuArgs(MenuTab menu)
            {
                Page = 0;
                Menu = menu;
                playerTabArgs.playerTab = PlayerTab.None;
                playerTabArgs = default(PlayerTabArgs);
                roomTabArgs = default(RoomTabArgs);
            }
            public MenuArgs(MenuTab menu,int pageNum)
            {
                Page = pageNum;
                Menu = menu;
                playerTabArgs.playerTab = PlayerTab.None;
                playerTabArgs = default(PlayerTabArgs);
                roomTabArgs = default(RoomTabArgs);
            }

            public MenuArgs(PlayerTab playerTab,string userIDString ,Action _action = Action.None, BlockDuration _blockDuration = BlockDuration.None)
            {
                Page = 0;
                Menu = MenuTab.Player;
                playerTabArgs = new PlayerTabArgs(playerTab,userIDString ,_action, _blockDuration);
                roomTabArgs = default(RoomTabArgs);
            }

            public MenuArgs(ulong ID,int pageNum)
            {
                roomTabArgs = new RoomTabArgs(ID);
                Page = pageNum;
                playerTabArgs = default(PlayerTabArgs);
                Menu = MenuTab.Room;
            }

            public struct PlayerTabArgs
            {
                public PlayerTab playerTab;
                public Action action;
                public BlockDuration blockDuration;
                public string targetPlayerID;

                public PlayerTabArgs(PlayerTab _playerTab,string userIDString, Action _action = Action.None,
                    BlockDuration _blockDuration = BlockDuration.None)
                {
                    playerTab = _playerTab;
                    action = _action;
                    blockDuration = _blockDuration;
                    targetPlayerID = userIDString;
                }

            }

            public struct RoomTabArgs
            {
                public ulong ID;

                public RoomTabArgs(ulong _ID)
                {
                    ID = _ID;
                }
            }
        }

        #region Actions

        void BanPlayer(ulong steamID,string reason,BlockDuration duration)
        {
            if (duration != BlockDuration.None)
            {
                BasePlayer player = BasePlayer.FindByID(steamID);
                string success =
                    EnhancedBanSystem.Instance.BanID(player?.IPlayer, steamID.ToString(), reason + "(Banned)", (int)duration);
                Console.WriteLine(success);
            }
        }

        void UnBanPlayer(ulong steamID)
        {
            string success = EnhancedBanSystem.Instance.ExecuteUnban(null, steamID.ToString(), string.Empty, GetUserIP(steamID));
            Console.WriteLine(success);
        }

        void KickPlayer(ulong steamID,string reason,BasePlayer admin)
        {
            BasePlayer player = BasePlayer.FindByID(steamID);
            if (player != null)
            {
                player.Kick(reason);
                admin.ChatMessage(player.displayName + " has been kicked from server");
            }
            else
            {
                admin.ChatMessage("Kick failed. Player with given steamID is not on the server");
            }
            
        }

        void TransportToPlayer(ulong steamID, BasePlayer admin)
        {
            BasePlayer target = BasePlayer.FindByID(steamID);
            if (target != null)
            {
                admin.MovePosition(target.ServerPosition);
                admin.ChatMessage("Transported to Player: " + GetRealName(target) + $"({target.UserIDString})");
            }
            else
            {
                admin.ChatMessage("Player not found");
            }
        }

        void StripInventory(ulong steamID,BasePlayer admin)
        {
            BasePlayer player = BasePlayer.FindByID(steamID);
            if (player != null)
            {
                EventManager.StripInventory(player);
                admin.ChatMessage("Inventory emptied successfully: " + GetRealName(player) + $"({player.UserIDString})");
            }
            else
            {
                admin.ChatMessage("Player not found");
            }
        }

        void CloseRoom(int roomID, BasePlayer admin)
        {
            BaseEventGame game;
            if (EventManager.BaseManager.TryGetValue(roomID, out game))
            {
                game.EndEvent();
                admin.ChatMessage("Room closed successfully");
            }
            else
            {
                admin.ChatMessage("Room is not found.");
            }
        }

        void MutePlayer(ulong userID, BasePlayer admin, BlockDuration duration,string reason)
        {
            BasePlayer target = BasePlayer.FindByID(userID);
            if (target == null)
            {
                admin.ChatMessage("Mute failed.Player not found.");
                return;
            }

            if (duration == BlockDuration.Permanent)
            {
                BetterChatMute.Call("API_Mute", target.IPlayer, admin.IPlayer, reason);
            }
            else
            {
                BetterChatMute.Call("API_TimeMute", target.IPlayer, admin.IPlayer,TimeSpan.FromSeconds((int)duration), reason);
            }
        }
        void UnMutePlayer(ulong userID, BasePlayer admin)
        {
            BasePlayer target = BasePlayer.FindByID(userID);
            if (target == null)
            {
                admin.ChatMessage("Mute failed.Player not found.");
                return;
            }
            BetterChatMute.Call("API_Unmute", target.IPlayer, admin.IPlayer);
        }

        void SpectatePlayer(BasePlayer player,ulong targetID)
        {
            BasePlayer target = BasePlayer.FindByID(targetID);
            if (target == null)
            {
                player.ChatMessage("Invalid command. Target is null. Target player may be disconnected");
                return;
            }
            EventManager.SpectatingBehaviour spectatingBehaviour = player.gameObject.AddComponent<EventManager.SpectatingBehaviour>();
            spectatingBehaviour.Enable(target);
        }

        void FinishSpectating(BasePlayer player)
        {
            EventManager.SpectatingBehaviour behaviour;
            if (player.TryGetComponent<EventManager.SpectatingBehaviour>(out behaviour))
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
                player.ChatMessage("Spectating ended.");
            }
            else
            {
                player.ChatMessage("Unexpected command. You are not spectating.");
            }
        }

        bool IsPlayerMuted(ulong userID)
        {
            BasePlayer target = BasePlayer.FindByID(userID);
            if (target == null)
                return false;
            object result = BetterChatMute.Call("API_IsMuted", target.IPlayer);
            if (!(result is bool))
                return false;

            bool isMuted = (bool)result;
            return isMuted;
        }
        bool IsPlayerSpectating(BasePlayer player)
        {
            if (player.HasComponent<EventManager.SpectatingBehaviour>())
                return true;
            return false;
        }

        #endregion

        #region Helpers
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName, 0UL, null);
        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name);

        string GetRealName(BasePlayer player)
        {
            object result = MurderGameMode.CallHook("GetRealPlayerName", player);
            if (!(result is string))
                return "Invalid";
            string name = result as string;
            return name;
        }

        private static string FormatTime(TimeSpan time)
        {
            var values = new List<string>();

            if (time.Days != 0)
                values.Add($"{time.Days} day(s)");

            if (time.Hours != 0)
                values.Add($"{time.Hours} hour(s)");

            if (time.Minutes != 0)
                values.Add($"{time.Minutes} minute(s)");

            if (time.Seconds != 0)
                values.Add($"{time.Seconds} second(s)");

            return values.ToSentence();
        }
        string GetPlayerDetailsLeft(BasePlayer player)
        {
            MurderPlayer eventPlayer;
            player.TryGetComponent(out eventPlayer);
            RoomSpectatingBehaviour spectatingBehaviour;
            player.TryGetComponent(out spectatingBehaviour);
            string result = $"Name: {GetRealName(player)}\n" +
                            $"SteamID: {player.UserIDString}\n" +
                            $"{(player.IsConnected ? "<color=#47d733><b>Online</b></color>" : "<color=#ff0000><b>Offline</b></color>")}\n" +
                            $"Server Pos: {player.ServerPosition.ToString()}\n" +
                            $"Idle Time: {FormatTime(TimeSpan.FromSeconds(player.IdleTime))} seconds\n";
            if (eventPlayer != null)
            {
                result += $"<color=#ff0000><b>IN-GAME</b></color>\n" +
                          $"Room ID: {eventPlayer.Event.gameroom.roomID}\n" +
                          $"Event Status: {eventPlayer.Event.Status.ToString()}\n" +
                          $"Role Name:<color={eventPlayer.playerRole?.roleColor}>{eventPlayer.playerRole?.roleName}</color>";
            }
            else if (spectatingBehaviour != null)
            {
                result += $"<color=#20dafb><b>Spectating</b></color>\n" +
                          $"RoomID: {spectatingBehaviour.Event.gameroom.roomID}\n";
            }
            return result;
        }

        string GetPlayerDetailsLeft(ulong userID)
        {
            string result = string.Empty;
            result += $"Player IP: {GetUserIP(userID)}\n" +
                      $"Player name: {GetUserName(userID)}\n" +
                      $"SteamID: {userID}";
            return result;
        }

        string GetPlayerDetailsRight(BasePlayer player)
        {
            BanData banData;
            GetPlayerBanData(player.userID,out banData);
            
            string text = String.Empty;
            string result = (string)BetterChatMute.Call("API_GetMuteDetails", player.IPlayer) ?? String.Empty;
            text += result;
            text += "\n";
            if (banData != null)
            {
                text += $"<b><color=red>Banned</color></b>\n" +
                        $"Ban Reason: {banData.reason}\n";
                if (banData.expire == 0.0f)
                    text += "<b><color=red>Banned Permanently</color></b>";
                else
                    text += $"<b><color=red>Ban Expires in {TimeSpan.FromSeconds(banData.expire)}</color></b>";
            }
            else
                text += "<color=lightblue>Not Banned</color>";

            return text;
        }

        string GetRoomDetails(MurderGame room)
        {
            string result = $"Room ID: {room.gameroom.roomID}\n" +
                            $"Arena: {room.gameroom.place}\n" +
                            $"Created by System: {room.gameroom.isCreatedbySystem}\n" +
                            $"Has Password: {room.gameroom.hasPassword}\n";
            if (room.gameroom.hasPassword)
                result += $"Password: {room.gameroom.password}\n";
            if (!room.gameroom.isCreatedbySystem)
                result += $"Owner Name: {room.gameroom.ownerName}\n";
            result += $"Deployed Objects: {room.roomobjects.Count}\n" +
                      $"Room Status: {room.Status}\n" +
                      $"Max Player Limit: {room.gameroom.maxPlayer}\n";
            return result;
        }

        bool IsPlayerBanned(ulong steamID)
        {
            BanData banData;
            string userIP = GetUserIP(steamID);
            return EnhancedBanSystem.Instance.PlayerDatabase_IsBanned(steamID.ToString(), userIP, out banData);
        }

        void GetPlayerBanData(ulong steamID,out BanData banData)
        {
            string userIP = GetUserIP(steamID);
            EnhancedBanSystem.Instance.PlayerDatabase_IsBanned(steamID.ToString(), userIP, out banData);
        }

        string GetUserIP(ulong steamID)
        {
            string result = PlayerDatabase.Call("GetPlayerData", steamID.ToString(), "ip") as string;

            if (string.IsNullOrEmpty(result))
                return string.Empty;
            return result;
        }
        string GetUserName(ulong steamID)
        {
            string result = PlayerDatabase.Call("GetPlayerData", steamID.ToString(), "name") as string;

            if (string.IsNullOrEmpty(result))
                return string.Empty;
            return result;
        }
        string GetUserID(ulong steamID)
        {
            string result = PlayerDatabase.Call("GetPlayerData", steamID.ToString(), "steamid") as string;

            if (string.IsNullOrEmpty(result))
                return string.Empty;
            return result;
        }
        #endregion
        #region Configuration

        private ConfigData configData;
        private class ConfigData
        {
            [JsonProperty("Label Color")] 
            public string labelColor;
            
            [JsonProperty("Highlighted Label Color")]
            public string highlightedLabelColor;

            [JsonProperty("Button Color")] 
            public string buttonColor;
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData()
            {
                highlightedLabelColor = "#822af9",
                labelColor = "#b985ff",
                buttonColor = "#d00202"
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            Config.WriteObject(configData, true);
        }

        protected override void SaveConfig()
        {
            base.SaveConfig();
            Config.WriteObject(configData, true);
        }

        #endregion
        #region Localization
        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);
        public static string Message(string key, ulong playerId = 0U) => Instance.lang.GetMessage(key, Instance, playerId != 0U ? playerId.ToString() : null);
        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
        };
        #endregion
    }
}