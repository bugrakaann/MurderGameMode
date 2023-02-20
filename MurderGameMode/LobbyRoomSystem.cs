// Requires: EMInterface
// Requires: ImageLibrary
// Requires: EventManager
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Facepunch.Models.Database;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core.Plugins;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;
using Oxide.Game.Rust.Cui;
using BaseEventGame = Oxide.Plugins.EventManager.BaseEventGame;
using GameRoom = Oxide.Plugins.EventManager.GameRoom;

namespace Oxide.Plugins
{
    [Info("LobbyRoomSystem", "Apwned", "1.0.0")]
    [Description("Game Room Manager for Murder Gamemode")]
    internal class LobbyRoomSystem : RustPlugin
    {
        //Permissions
        const string perm_createroom = "Lobbyroomsystem.createroom";

        [PluginReference] private Plugin ImageLibrary, Kits,Spawns;
        const string lobby_UI = "lobbyui.lobbyroomsystem";
        private const string lobbyhelper_UI = "lobbyui.lobbyhelper";
        const string createroom_UI = "createroomui.lobbyroomsystem";
        const string upperpanel = "createroom.ui.upperpanel";
        const string belowpanel = "createroom.ui.belowpanel";
        const string password_UI = "lobbyui.passwordui";
        private const string welcomermenu_UI = "lobbyui.welcomermenu";

        static LobbyRoomSystem Instance { get; set; } = new LobbyRoomSystem();

        Dictionary<ulong, GameRoom> roomCreators = new Dictionary<ulong, GameRoom>();
        private Dictionary<BasePlayer,int> activeUsers = new Dictionary<BasePlayer, int>();
        Dictionary<BasePlayer, string> EnteredPasswords = new Dictionary<BasePlayer, string>();
        Dictionary<BasePlayer, GameRoom> ongoingPasswordTrial = new Dictionary<BasePlayer, GameRoom>();
        
        //Lobby spawn points
        private List<Vector3> lobbyspawnPoints = new List<Vector3>();
        #region Configuration
        static ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Colors used for coloring vip name labels")]
            public List<string> vipnamelabelcolors = new List<string>();
            [JsonProperty(PropertyName = "Created room number per arena")]
            public int createdRoomperArena;
            [JsonProperty("Max room password length")]
            public int maxpasswordlength;
            [JsonProperty("Default lobby kit")]
            public string defaultlobbykit;
            [JsonProperty("VIP lobby kit")]
            public string viplobbykit;
            [JsonProperty("Lobby Spawnfile name")]
            public string spawnfilename;
            [JsonProperty("Welcome UI Page Texts (In order)")]
            public List<string> welcomeuitexts;
        }
        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData()
            {
                vipnamelabelcolors = new List<string>()
                {
                    "#ede342","#f2bf6c","#f69a97","#fb76c1","#ff51eb","#e85c90","#c481a7","#a0a6be","#7ccad5","#58efec","#8ecae6","#8093f1","#b388eb","#f7aef8","#fb76c1","#f69a97","#f2bf6c"
                },
                createdRoomperArena = 2,
                maxpasswordlength = 15,
                defaultlobbykit = String.Empty,
                viplobbykit = String.Empty,
                spawnfilename = string.Empty,
                welcomeuitexts = new List<string>()
                {
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty
                }
            };
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            Config.WriteObject(configData, true);
        }
        protected override void SaveConfig() => Config.WriteObject(configData, true);

        void CheckConfig()
        {
            if (string.IsNullOrEmpty(configData.defaultlobbykit) || string.IsNullOrEmpty(configData.viplobbykit))
            {
                PrintError("Check lobby kits in the configuration and reload");
                UnsubscibeAllHooks();
            }
        }
        #endregion
        #region Hooks
        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);
        void Loaded()
        {
            CheckConfig();
        }
        void OnServerInitialized()
        {
            RegisterUIComponents();
            permission.RegisterPermission(perm_createroom, this);
            timer.Once(10f, () => CreateSystemRooms());
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                OpenLobbyUI(player);
                OpenLobbyHelperUI(player);
            }
            LoadLobbySpawnPoints();
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (!activeUsers.ContainsKey(player))
                activeUsers.Add(player,0);
            SendLobbyUI(player);
            SendLobbyHelperUI(player);
            GiveLobbyCostume(player);
        }
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (activeUsers.ContainsKey(player))
                activeUsers.Remove(player);

            if (roomCreators.ContainsKey(player.userID))
                roomCreators.Remove(player.userID);
        }
        object OnPlayerRespawn(BasePlayer player)
        {
            if (player == null)
                return null;
            if (player.HasComponent<EventManager.BaseEventPlayer>())
                return null;
            Vector3 position = lobbyspawnPoints.GetRandom();
            return new BasePlayer.SpawnPoint() { pos = position, rot = new Quaternion(0, 0, 0, 1) };
        }
        void OnPlayerRespawned(BasePlayer player)
        {
            if (player.HasComponent<EventManager.BaseEventPlayer>())
                return;
            GiveLobbyCostume(player);
        }
        #endregion
        #region API
        [HookMethod("GiveLobbyCostume")]
        void GiveLobbyCostume(BasePlayer player)
        {
            EventManager.StripInventory(player);
            EventManager.ResetMetabolism(player);
            if (permission.UserHasPermission(player.UserIDString, perm_createroom))
                Kits.Call("GiveKit", player, configData.viplobbykit);
            else
                Kits.Call("GiveKit", player, configData.defaultlobbykit);
        }
        [HookMethod("RefreshLobbyUI")]
        void RefreshLobbyUI()
        {
            foreach (BasePlayer player in activeUsers.Keys)
            {
                CuiHelper.DestroyUi(player, lobby_UI);
                SendLobbyUI(player,activeUsers[player]);
            }
        }
        #endregion
        [HookMethod("OpenLobbyUI")]
        void OpenLobbyUI(BasePlayer player,int pageNum  = 0)
        {
            SendLobbyUI(player, pageNum);

            activeUsers[player] = pageNum;
        }

        [HookMethod("SpawnInLobby")]
        void SpawnInLobby(BasePlayer player)
        {
            Vector3 randomspawn = lobbyspawnPoints.GetRandom();
            EventManager.MovePosition(player,randomspawn,false);
        }
        void CloseLobbyUI(BasePlayer player)
        {
            activeUsers.Remove(player);
            CuiHelper.DestroyUi(player, lobby_UI);
        }
        [HookMethod("OpenLobbyHelperUI")]
        void OpenLobbyHelperUI(BasePlayer player)
        {
            SendLobbyHelperUI(player);
        }
        void CloseLobbyHelperUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, lobbyhelper_UI);
        }
        #region Console Commmands
        [ConsoleCommand("lobbysystem.ui")]
        void cmdSendLobbyUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            int pageNum = arg.GetInt(0);

            if (player == null || pageNum == -1)
                return;

            if (pageNum > 0 && !PageHasElements(pageNum))
                return;
            
            OpenLobbyUI(player,pageNum);
            ClickEffect(player);
        }
        [ConsoleCommand("lobbysystem.join")]
        void cmdJoinRoom(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            int roomID = arg.GetInt(0);
            if (player == null || roomID == 0 || player.IsDead())
                return;
            ClickEffect(player);

            BaseEventGame game;
            EventManager.BaseManager.TryGetValue(roomID, out game);
            if (game == null)
                return;
            if (game.gameroom.maxPlayer <= (game.spectators.Count + game.eventPlayers.Count))
            {
                player.ChatMessage(Message("Notification.Roomisfull", player.userID));
                return;
            }
            if (game.gameroom.hasPassword)
            {
                SendPasswordUI(player, game.gameroom);
                return;
            }
            game.JoinEvent(player);
            CloseLobbyUI(player);
            CloseLobbyHelperUI(player);
        }
        [ConsoleCommand("createroom.closeui")]
        void cmdCloseCreateRoomUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            ClickEffect(player);

            roomCreators.Remove(player.userID);
            CuiHelper.DestroyUi(player, createroom_UI);
        }
        [ConsoleCommand("createroom.ui")]
        void cmdOpenCreateRoomUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            ClickEffect(player);

            if (!permission.UserHasPermission(player.UserIDString, perm_createroom))
            {
                player.ChatMessage(Message("Notification.CantCreateRoom", player.userID));
                return;
            }

            RoomCreatorPage page = (RoomCreatorPage)arg.GetInt(0);

            GameRoom outvalue;
            if (!roomCreators.TryGetValue(player.userID, out outvalue))
                roomCreators[player.userID] = new GameRoom(String.Empty, player.displayName, 1, 10, 1, player.userID);
            SendCreateRoomUI(player, page);
        }
        [ConsoleCommand("lobbysystem.setparameter")]
        void cmdCreateRoom(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            ClickEffect(player);

            if (!arg.HasArgs(2))
                return;

            if (arg.GetString(0) == "createroom.arena" || arg.GetString(0) == "createroom.haspassword" || arg.GetString(0) == "createroom.password")
            {
                GameRoom gameRoom;
                if (!roomCreators.TryGetValue(player.userID, out gameRoom))
                    return;
                SetParameter(player, gameRoom, arg.GetString(0), string.Join(" ", arg.Args.Skip(1)));

                SendCreateRoomUI(player, RoomCreatorPage.Main);
            }
            else if (arg.GetString(0) == "password.enteredpassword")
            {
                SetParameter(player, arg.GetString(0), string.Join(" ", arg.Args.Skip(1)));

                SendPasswordUI(player,ongoingPasswordTrial[player]);
            }
        }
        [ConsoleCommand("createroom.complete")]
        void cmdCompleteCreatingRoom(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            ClickEffect(player);

            GameRoom room;
            roomCreators.TryGetValue(player.userID, out room);

            if ((room.hasPassword == true && string.IsNullOrEmpty(room.password)) || string.IsNullOrEmpty(room.place))
                return;

            object success = EventManager.Instance.OpenEvent(room.place, room);
            CuiHelper.DestroyUi(player, createroom_UI);
            roomCreators.Remove(player.userID);
            RefreshLobbyUI();
            BaseEventGame game = EventManager.BaseManager[room.roomID];
            CloseLobbyUI(player);
            CloseLobbyHelperUI(player);
            game.JoinEvent(player);
        }
        [ConsoleCommand("createroom.clear")]
        void cmdClearCreateRoom(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            ClickEffect(player);

            string fieldName = arg.GetString(0);

            switch (fieldName)
            {
                case "createroom.password":
                    GameRoom gameRoom;
                    if (!roomCreators.TryGetValue(player.userID, out gameRoom))
                        return;
                    gameRoom.password = string.Empty;
                    SendCreateRoomUI(player, RoomCreatorPage.Main);
                    break;
                case "password.enteredpassword":
                    if (EnteredPasswords.ContainsKey(player))
                        EnteredPasswords.Remove(player);
                    SendPasswordUI(player, ongoingPasswordTrial[player]);
                    break;
                default:
                    break;
            }
            
        }
        [ConsoleCommand("lobbysystem.password.closeui")]
        void cmdClosePasswordUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;
            ClickEffect(player);
            CuiHelper.DestroyUi(player, password_UI);
            ongoingPasswordTrial.Remove(player);
            EnteredPasswords.Remove(player);
            ClickEffect(player);
        }
        [ConsoleCommand("lobbysystem.password.enterpassword")]
        void cmdEnterPassword(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            ClickEffect(player);
            int roomID = arg.GetInt(0);

            BaseEventGame game;
            if (!EventManager.BaseManager.TryGetValue(roomID, out game))
                return;

            string password;
            EnteredPasswords.TryGetValue(player, out password);
            if (password == game.gameroom.password)
            {
                game.JoinEvent(player);
                CuiHelper.DestroyUi(player, password_UI);
                CloseLobbyUI(player);
                CloseLobbyHelperUI(player);
                ongoingPasswordTrial.Remove(player);
                EnteredPasswords.Remove(player);
            }
            else
            {
                player.ChatMessage("Invalid password");
            }
                
        }
        [ConsoleCommand("lobbyhelper.toggleui")]
        void cmdToggleLobbyHelperUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            if(activeUsers.ContainsKey(player))
                CloseLobbyUI(player);
            else
                OpenLobbyUI(player);
        }

        [ConsoleCommand("welcome.ui")]
        void cmdOpenWelcomeUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            int pageNum = arg.GetInt(0);
            SendWelcomerUI(player,pageNum);
            ClickEffect(player);
        }

        [ConsoleCommand("welcome.closeui")]
        void cmdCloseWelcomeUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, welcomermenu_UI);
        }
        #endregion
        #region Chat Commands
        [ChatCommand("closelobbyui")]
        void cmdCloseLobbyUI(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                CloseLobbyUI(player);
            }
        }
        [ChatCommand("openlobbyui")]
        void cmdOpenLobbyUI(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                OpenLobbyUI(player);
            }
        }

        [ChatCommand("help")]
        void cmdOpenHelpMenu(BasePlayer player)
        {
            SendWelcomerUI(player,0);
        }
        #endregion
        #region UI
        void RegisterUIComponents()
        {
            //Lobby Components
            AddImage("blooddripping", "https://www.dropbox.com/s/p2dwv8iohmeb70u/blooddripping.png?dl=1");
            AddImage("menulogo", "https://www.dropbox.com/s/ksmp1nmqqecelgd/menulogo.png?dl=1");
            AddImage("joinbutton", "https://www.dropbox.com/s/fem3sqimq7jdbqc/joinbutton.png?dl=1");
            AddImage("marker", "https://www.dropbox.com/s/sfznx8upkq15108/marker.png?dl=1");
            AddImage("person", "https://www.dropbox.com/s/0nci9yan97ndlod/person.png?dl=1");
            AddImage("lock", "https://www.dropbox.com/s/i7awr5tpyar8udu/lock.png?dl=1");
            AddImage("questionmark", "https://www.dropbox.com/s/ey5yto67n8dw9oe/questionmark.png?dl=1");
            AddImage("key", "https://www.dropbox.com/s/wwuo1g6tbeoiknj/key.png?dl=1");
            
            //Welcome UI Components
            AddImage("upgradelogo","https://www.dropbox.com/s/e1tso0kungt9sce/boost.png?dl=1");
            AddImage("discordlogo","https://www.dropbox.com/s/cm3crzc1sw3j3pr/discordlogo.png?dl=1");
            AddImage("howto","https://www.dropbox.com/s/74wsrp5qo8bh5qj/howto.png?dl=1");
            AddImage("ruleslogo","https://www.dropbox.com/s/fhh6lyjzru2lnwc/rules.png?dl=1");
            AddImage("updateslogo","https://www.dropbox.com/s/uz3umoffecrz1i1/updates.png?dl=1");
            AddImage("murderhowtoplaythumbnail","https://www.dropbox.com/s/d4edub4cjsstigz/murderhowtoplaythumbnail.png?dl=1");
            AddImage("clickwebsite","https://www.dropbox.com/s/vlqwqxeu1uayr1u/clickwebsite.png?dl=1");
            AddImage("vipspeclogo","https://www.dropbox.com/s/w2oc6ykftd8rh90/vipspeclogo.png?dl=1");
        }

        void SendWelcomerUI(BasePlayer player,int pageNum)
        {
            CuiHelper.DestroyUi(player, welcomermenu_UI);
            CuiElementContainer container = UI.Container(welcomermenu_UI, "0 0 0 0.75", UI4.Full, true,"Overall");
            UI.AddBlur(container,"0 0 0 0.12",welcomermenu_UI);
            const string leftpanel = "welcomermenu.leftpanel";
            const string rightpanel = "welcomermenu.rightpanel";
            UI.Panel(container,welcomermenu_UI,leftpanel,"0 0 0 1",UI.TransformToUI4(337f,624f,266f,893f));
            UI.Panel(container,welcomermenu_UI,rightpanel,"0 0 0 1",UI.TransformToUI4(640f,1583f,266f,893f));
            UI.Image(container,welcomermenu_UI,GetImage("brandlogo"),UI.TransformToUI4(844f,1074f,913f,961f));
            UI.Button(container,welcomermenu_UI,UI.Color("#850000",1f),"X    Close",UI.Color("#f2f2f2",1f),12,UI.TransformToUI4(1360f,1583f,194f,246f),"welcome.closeui",TextAnchor.MiddleCenter,"PermanentMarker.ttf");
            
            int index = 0;
            float skipheight = 65f;
            UI.Button(container,leftpanel,(pageNum == 0 ? UI.Color("#850000",1f):"0 0 0 1"),"                Rules",UI.Color("#f2f2f2",1f),14,UI.TransformToUI4(0f,287f,471f-(skipheight * index),515f-(skipheight * index++),287f,627f),"welcome.ui 0",TextAnchor.MiddleLeft,"RobotoCondensed-Regular.ttf");
            UI.Button(container,leftpanel,(pageNum == 1 ? UI.Color("#850000",1f):"0 0 0 1"),"                How To Play",UI.Color("#f2f2f2",1f),14,UI.TransformToUI4(0f,287f,471f-(skipheight * index),515f-(skipheight * index++),287f,627f),"welcome.ui 1",TextAnchor.MiddleLeft,"RobotoCondensed-Regular.ttf");
            UI.Button(container,leftpanel,(pageNum == 2 ? UI.Color("#850000",1f):"0 0 0 1"),"                Buy <color=#d9a919><b>VIP</b></color>",UI.Color("#f2f2f2",1f),14,UI.TransformToUI4(0f,287f,471f-(skipheight * index),515f-(skipheight * index++),287f,627f),"welcome.ui 2",TextAnchor.MiddleLeft,"RobotoCondensed-Regular.ttf");
            UI.Button(container,leftpanel,(pageNum == 3 ? UI.Color("#850000",1f):"0 0 0 1"),"                Updates",UI.Color("#f2f2f2",1f),14,UI.TransformToUI4(0f,287f,471f-(skipheight * index),515f-(skipheight * index++),287f,627f),"welcome.ui 3",TextAnchor.MiddleLeft,"RobotoCondensed-Regular.ttf");
            UI.Button(container,leftpanel,(pageNum == 4 ? UI.Color("#850000",1f):"0 0 0 1"),"                Discord",UI.Color("#f2f2f2",1f),14,UI.TransformToUI4(0f,287f,471f-(skipheight * index),515f-(skipheight * index++),287f,627f),"welcome.ui 4",TextAnchor.MiddleLeft,"RobotoCondensed-Regular.ttf");
            
            //Icons
            index = 0;
            UI.Image(container,leftpanel,GetImage("ruleslogo"),UI.TransformToUI4(27f,50f,479f-(skipheight * index),505f-(skipheight * index),287f,627f));
            index++;
            UI.Image(container,leftpanel,GetImage("howto"),UI.TransformToUI4(27f,50f,479f-(skipheight * index),505f-(skipheight * index),287f,627f));
            index++;
            UI.Image(container,leftpanel,GetImage("upgradelogo"),UI.TransformToUI4(27f,50f,479f-(skipheight * index),505f-(skipheight * index),287f,627f));
            index++;
            UI.Image(container,leftpanel,GetImage("updateslogo"),UI.TransformToUI4(27f,50f,484f-(skipheight * index),505f-(skipheight * index),287f,627f));
            index++;
            UI.Image(container,leftpanel,GetImage("discordlogo"),UI.TransformToUI4(27f,50f,479f-(skipheight * index),505f-(skipheight * index),287f,627f));

            switch (pageNum)
            {
                case 0:
                    UI.Label(container,rightpanel,"RULES",25,UI.TransformToUI4(393f,553f,537f,612f,943f,627f),TextAnchor.MiddleCenter,UI.Color("#c7aa65",1f),"PermanentMarker.ttf");
                    UI.Label(container,rightpanel,configData.welcomeuitexts[0],18,UI.TransformToUI4(69f,869f,80f,518f,943f,627f),TextAnchor.UpperLeft,UI.Color("#f2f2f2",1f),"RobotoCondensed-Regular.ttf");
                    UI.Label(container,rightpanel,"- Rusty's Mod Team",15,UI.TransformToUI4(625f,885f,50f,100f,943f,627f),TextAnchor.MiddleLeft,UI.Color("#c7aa65",1f));
                    break;
                case 1:
                    UI.Label(container,rightpanel,"How To Play?",25,UI.TransformToUI4(208f,735f,536f,622f,943f,627f),TextAnchor.MiddleCenter,UI.Color("6dc36b",1f),"PermanentMarker.ttf");
                    UI.Label(container,rightpanel,configData.welcomeuitexts[1],15,UI.TransformToUI4(50f,910f,25f,518f,943f,627f),TextAnchor.UpperLeft);
                    UI.Image(container,rightpanel,GetImage("murderhowtoplaythumbnail"),UI.TransformToUI4(715f,901f,1f,161f,943f,627f));
                    break;
                case 2:
                    UI.Label(container,rightpanel,"Support Us",25,UI.TransformToUI4(255f,665f,524f,620f,943f,627f),TextAnchor.MiddleCenter,UI.Color("#d9a919",1f),"PermanentMarker.ttf");
                    UI.Label(container,rightpanel,configData.welcomeuitexts[2],18,UI.TransformToUI4(50f,910f,105f,518f,943f,627f),TextAnchor.UpperLeft);
                    for(int i =0;i<5;i++)
                        UI.Image(container,rightpanel,GetImage("vipspeclogo"),UI.TransformToUI4(51f,77f,398f- (32f * i),418f- (32f * i),943f,627f));
                    UI.Image(container,rightpanel,GetImage("clickwebsite"),UI.TransformToUI4(51f,71f,171f,198f,943f,627f));
                    UI.Label(container,rightpanel,"rustysmod.tebex.io",18,UI.TransformToUI4(88f,460f,165f,204f,943f,627f),TextAnchor.MiddleLeft,UI.Color("#f2f2f2",1f));
                    break;
                case 4:
                    UI.Label(container,rightpanel,"Discord",25,UI.TransformToUI4(255f,665f,524f,620f,943f,627f),TextAnchor.MiddleCenter,UI.Color("c8a7cd",1f),"PermanentMarker.ttf");
                    UI.Label(container,rightpanel,configData.welcomeuitexts[4],18,UI.TransformToUI4(50f,910f,105f,518f,943f,627f),TextAnchor.UpperLeft);
                    UI.Label(container,rightpanel,"discord.gg/6uSgfSA7uq",18,UI.TransformToUI4(250f,693f,175f,270f,943f,627f),TextAnchor.MiddleCenter,UI.Color("#7aaecc",1f));
                    break;
            }
            CuiHelper.AddUi(player, container);
        }
        void SendLobbyHelperUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, lobbyhelper_UI);
            CuiElementContainer container = UI.Container(lobbyhelper_UI, "0 0 0 0",
                UI.TransformToUI4(1293f, 1594f, 22f, 146f));
            const string upperpanel = "lobbyhelperui.upperpanel";
            const string belowpanel = "lobbyhelperui.belowpanel";
            UI.Panel(container,lobbyhelper_UI,belowpanel,"0 0 0 0.85",UI.TransformToUI4(0f,301f,0f,103f,301f,124f));
            UI.Panel(container,lobbyhelper_UI,upperpanel,"0 0 0 0.85",UI.TransformToUI4(0f,301f,105f,124f,301f,124f));
            UI.AddBlur(container,"0 0 0 0.05",upperpanel);
            UI.AddBlur(container,"0 0 0 0.05",belowpanel);
            UI.Label(container,lobbyhelper_UI,"LOBBY",9,UI.TransformToUI4(0f,301f,105f,124f,301f,124f),TextAnchor.MiddleCenter,"1 1 1 1","RobotoCondensed-Regular.ttf");
            
            UI.Button(container,lobbyhelper_UI,UI.Color("#7c0000",1f),"Toggle",UI.Color("#f0f0f0",1f),10,UI.TransformToUI4(6f,67f,76f,96f,301f,124f),"lobbyhelper.toggleui",TextAnchor.MiddleCenter,"RobotoCondensed-Regular.ttf");
            UI.Button(container,lobbyhelper_UI,UI.Color("#7c0000",1f),"Skins",UI.Color("#b1e5ff",1f),10,UI.TransformToUI4(6f,67f,46f,66f,301f,124f),"murderskinmanager.ui",TextAnchor.MiddleCenter,"RobotoCondensed-Regular.ttf");
            UI.Button(container,lobbyhelper_UI,UI.Color("#7c0000",1f),"Help",UI.Color("#f2b582",1f),10,UI.TransformToUI4(6f,67f,16f,36f,301f,124f),"welcome.ui 1",TextAnchor.MiddleCenter,"RobotoCondensed-Regular.ttf");
            
            UI.Label(container,lobbyhelper_UI,"Hide UI",10,UI.TransformToUI4(71f,301f,79f,98f,301f,124f),TextAnchor.MiddleLeft,UI.Color("#f6f6f6",1f),"RobotoCondensed-Regular.ttf");
            UI.Label(container,lobbyhelper_UI,"Select your in-game costume&skin",10,UI.TransformToUI4(71f,301f,47f,66f,301f,124f),TextAnchor.MiddleLeft,UI.Color("#f6f6f6",1f),"RobotoCondensed-Regular.ttf");
            UI.Label(container,lobbyhelper_UI,"How to play this game mode?",10,UI.TransformToUI4(71f,301f,17f,36f,301f,124f),TextAnchor.MiddleLeft,UI.Color("#f6f6f6",1f),"RobotoCondensed-Regular.ttf");
            CuiHelper.AddUi(player, container);
        }
        void SendLobbyUI(BasePlayer player, int pageNum = 0)
        {
            CuiHelper.DestroyUi(player, lobby_UI);
            CuiElementContainer container = UI.Container(lobby_UI, "0 0 0 0.85", UI.TransformToUI4(1220f, 1920f, 177f, 1007f));
            UI.AddBlur(container, "0 0 0 0.08", lobby_UI);
            UI.Image(container, lobby_UI, GetImage("blooddripping"), UI.TransformToUI4(-5f, 708f, 720f, 832f, 702f, 830f));
            UI.Image(container, lobby_UI, GetImage("brandlogo"), UI.TransformToUI4(265f, 457f, 741f, 779f, 702f, 830f));

            UI.Button(container, lobby_UI, UI.Color("#cecece", 1f), "<", "0 0 0 1", 18, UI.TransformToUI4(0f, 231f, 0f, 31f, 702f, 830f), $"lobbysystem.ui {pageNum - 1}");

            UI.Button(container, lobby_UI, UI.Color("#7c0000", 1f), "Create Room", 15, UI.TransformToUI4(235f, 466f, 0f, 31f, 702f, 830f), "createroom.ui", TextAnchor.MiddleCenter, "PermanentMarker.ttf");

            UI.Button(container, lobby_UI, UI.Color("#cecece", 1f), ">", "0 0 0 1", 18, UI.TransformToUI4(470f, 702f, 0f, 31f, 702f, 830f), $"lobbysystem.ui {pageNum + 1}", TextAnchor.MiddleCenter);

            UI.Label(container, lobby_UI, $"({pageNum + 1}/{(EventManager.BaseManager.Count / 7) + 1})", 12, UI.TransformToUI4(639f, 701f, 695f, 725f, 702f, 830f), TextAnchor.MiddleCenter, UI.Color("#b0a9a9", 1f));
            for (int i = 0; i < 6; i++)
            {
                if ((EventManager.BaseManager.Values.ToList().Count - 1) >= (i + (6 * pageNum)))
                    AddRoomLabel(container, EventManager.BaseManager.Values.ToList()[i + (6 * pageNum)], i);
            }

            CuiHelper.AddUi(player, container);
        }
        void AddRoomLabel(CuiElementContainer container, BaseEventGame game, int index)
        {
            float yoffset = 96f * index;

            const string roomlabel = "roomlabel.ui";
            UI.Panel(container, lobby_UI, roomlabel, UI.Color("#515151", 1f), UI.TransformToUI4(14f, 686f, 601f - yoffset, 693f - yoffset, 702f, 830f));
            UI.Label(container, roomlabel, "MURDER", 18, UI.TransformToUI4(12f, 182f, 55f, 93f, 672f, 92f), TextAnchor.MiddleLeft, "1 1 1 1", "PermanentMarker.ttf");

            UI.Image(container, roomlabel, GetImage("marker"), UI.TransformToUI4(12f, 27f, 32f, 49f, 672f, 92f));
            UI.Image(container, roomlabel, GetImage("person"), UI.TransformToUI4(12f, 27f, 8f, 24f, 672f, 92f));

            UI.Label(container, roomlabel, "Arena:", 12, UI.TransformToUI4(16f, 115f, 30f, 50f, 672f, 92f), TextAnchor.MiddleCenter, "1 1 1 1", "RobotoCondensed-Regular.ttf");
            UI.Label(container, roomlabel, "Owner:", 12, UI.TransformToUI4(16f, 115f, 5f, 25f, 672f, 92f), TextAnchor.MiddleCenter, "1 1 1 1", "RobotoCondensed-Regular.ttf");
            if(game.gameroom.hasPassword)
                UI.Image(container, roomlabel, GetImage("lock"), UI.TransformToUI4(140f, 155f, 63f, 81f, 672f, 92f));
            
            UI.Label(container, roomlabel, $"{game.GetGamePlace()}", 12, UI.TransformToUI4(120f, 300f, 30f, 50f, 672f, 92f), TextAnchor.MiddleLeft, "1 1 1 1", "RobotoCondensed-Regular.ttf");
            UI.Label(container, roomlabel, $"{((game.gameroom.ownerID == 0) ? game.gameroom.ownerName: ColoredOwnerName(game.gameroom.ownerName))}", 12, UI.TransformToUI4(120f, 300f, 5f, 25f, 672f, 92f), TextAnchor.MiddleLeft, "1 1 1 1", "RobotoCondensed-Regular.ttf");

            UI.Label(container, roomlabel, $"{GetConvertedRoomStatus(game.Status)}", 24, UI.TransformToUI4(288f, 468f, 39f, 81f, 672f, 92f));
            UI.Label(container, roomlabel, $"{game.eventPlayers?.Count}/{game.gameroom.maxPlayer}", 15, UI.TransformToUI4(356f, 406f, 13f, 38f, 672f, 92f));

            UI.Button(container, roomlabel, UI.Color("#7c0000", 1f), "    JOIN", 20, UI.TransformToUI4(529f, 672f, 0f, 91f, 672f, 92f), $"lobbysystem.join {game.gameroom.roomID}", TextAnchor.MiddleCenter, "RobotoCondensed-Regular.ttf");
            UI.Image(container, roomlabel, GetImage("joinbutton"), UI.TransformToUI4(542f, 573f, 31f, 62f, 672f, 92f));
        }
        void AddArenaLabels(CuiElementContainer container)
        {
            int xoffset = 133;
            int yoffset = 32;

            for (int i = 0; i < EventManager.Instance.Events.events.Count; i++)
            {
                UI.Button(container, belowpanel, UI.Color("#b4b0b0", 1f), EventManager.Instance.Events.events.Keys.ElementAt(i), UI.Color("#000000", 1f), 12, UI.TransformToUI4(15f + (xoffset * (i % 4)), 144f + (xoffset * (i % 4)), 172f - ((i / 4) * yoffset), 200f - ((i / 4) * yoffset), 559f, 221f), $"lobbysystem.setparameter createroom.arena {EventManager.Instance.Events.events.Keys.ElementAt(i)}");
            }
        }
        void SendCreateRoomUI(BasePlayer player, RoomCreatorPage page)
        {
            GameRoom gameRoom;
            roomCreators.TryGetValue(player.userID, out gameRoom);

            CuiHelper.DestroyUi(player, createroom_UI);
            CuiElementContainer container = UI.Container(createroom_UI, UI.Color("#000000", 0.85f), UI.TransformToUI4(631f, 1216f, 417f, 724f), true);
            UI.AddBlur(container, "0 0 0 0.05", createroom_UI);
            UI.Panel(container, createroom_UI, upperpanel, UI.Color("#515151", 1f), UI.TransformToUI4(16f, 575f, 239f, 293f, 585f, 307f));
            UI.Panel(container, createroom_UI, belowpanel, UI.Color("#515151", 1f), UI.TransformToUI4(16f, 575f, 11f, 232f, 585f, 307f));
            UI.Button(container, upperpanel, UI.Color("#842730", 1f), "X", 8, UI.TransformToUI4(525f, 558f, 34f, 53f, 559f, 54f), "createroom.closeui", TextAnchor.MiddleCenter, "PermanentMarker.ttf");

            switch (page)
            {
                case RoomCreatorPage.Main:
                    UI.Label(container, upperpanel, "Create New Room", 18, UI.TransformToUI4(40f, 340f, 10f, 50f, 559f, 54f), TextAnchor.MiddleLeft, UI.Color("#d63737", 1f), "PermanentMarker.ttf");

                    UI.Image(container, belowpanel, GetImage("marker"), UI.TransformToUI4(18f, 33f, 179f, 196f, 559f, 221f));
                    UI.Image(container, belowpanel, GetImage("questionmark"), UI.TransformToUI4(17f, 32f, 145f, 162f, 559f, 221f));

                    UI.Label(container, belowpanel, "Arena", 15, UI.TransformToUI4(40f, 190f, 174f, 199f, 559f, 221f), TextAnchor.MiddleLeft, "1 1 1 1");
                    UI.Label(container, belowpanel, "Has Password", 15, UI.TransformToUI4(40f, 190f, 141f, 166f, 559f, 221f), TextAnchor.MiddleLeft, "1 1 1 1");

                    AddSelectorField(container, belowpanel, UI.Color("#7e7a7a", 1f), UI.Color("#842730", 1f), UI.TransformToUI4(244f, 466f, 175f, 199f, 559f, 221f), "createroom.arena", gameRoom.place, String.Empty);
                    AddToggleField(container, belowpanel, UI.TransformToUI4(244f, 277f, 143f, 167f, 559f, 221f), UI.Color("#7e7a7a", 1f), "createroom.haspassword", gameRoom.hasPassword);
                    if (gameRoom.hasPassword)
                    {
                        UI.Image(container, belowpanel, GetImage("lock"), UI.TransformToUI4(18f, 31f, 113f, 132f, 559f, 221f));
                        UI.Label(container, belowpanel, "Password", 15, UI.TransformToUI4(40f, 190f, 108f, 133f, 559f, 221f), TextAnchor.MiddleLeft, "1 1 1 1");
                        AddInputField(container, belowpanel, UI.Color("#7e7a7a", 1f), UI.Color("#842730", 1f), UI.TransformToUI4(244f, 466f, 113f, 137f, 559f, 221f), "createroom.password", gameRoom.password,configData.maxpasswordlength,false);
                    }

                    UI.Button(container, belowpanel, UI.Color("#842730", 1f), "Create", 12, UI.TransformToUI4(172f, 382f, 13f, 49f, 559f, 221f), "createroom.complete");
                    break;
                case RoomCreatorPage.ArenaSelector:
                    UI.Label(container, upperpanel, "Select Arena", 18, UI.TransformToUI4(40f, 340f, 10f, 50f, 559f, 54f), TextAnchor.MiddleLeft, UI.Color("#d63737", 1f), "PermanentMarker.ttf");
                    AddArenaLabels(container);
                    break;
                default:
                    break;
            }


            CuiHelper.AddUi(player, container);
        }
        void SendPasswordUI(BasePlayer player, GameRoom room)
        {
            if(!ongoingPasswordTrial.ContainsKey(player))
                ongoingPasswordTrial.Add(player, room);
            CuiHelper.DestroyUi(player, password_UI);
            const string insidepanel = "lobbysystem.password.ui.insidepanel";
            CuiElementContainer container = UI.Container(password_UI, UI.Color("#000000", 0.85f), UI.TransformToUI4(679f, 1201f, 503f, 697f), true);
            UI.AddBlur(container, "0 0 0 0.05", password_UI);
            UI.Panel(container, password_UI, insidepanel, UI.Color("#515151", 1f), UI.TransformToUI4(13f, 509f, 13f, 181f, 522f, 194f));
            UI.Button(container, insidepanel, UI.Color("#842730", 1f), "X", 8, UI.TransformToUI4(461f, 495f, 147f, 167f, 496f, 168f), "lobbysystem.password.closeui", TextAnchor.MiddleCenter, "PermanentMarker.ttf");
            UI.Label(container, insidepanel, "Enter Room Password", 18, UI.TransformToUI4(52f, 462f, 110f, 155f, 496f, 168f), TextAnchor.MiddleCenter, UI.Color("#e6dddd", 1f), "PermanentMarker.ttf");
            UI.Image(container, insidepanel, GetImage("key"), UI.TransformToUI4(97f, 125f, 60, 88f, 496f, 168f));
            UI.Image(container, insidepanel, GetImage("key"), UI.TransformToUI4(378f, 404f, 60, 86f, 496f, 168f));

            string password;
            AddInputField(container, insidepanel, UI.Color("#7e7a7a", 1f), UI.Color("#842730", 1f), UI.TransformToUI4(133f, 368f, 59f, 86f, 496f, 168f), "password.enteredpassword", EnteredPasswords.TryGetValue(player, out password) ? password : string.Empty, configData.maxpasswordlength,false);
            UI.Button(container, insidepanel, UI.Color("#7c0000", 1f), "OK", 10, UI.TransformToUI4(201f, 295f, 13f, 35f, 496f, 168f), $"lobbysystem.password.enterpassword {room.roomID}", TextAnchor.MiddleCenter);
            CuiHelper.AddUi(player, container);
        }
        private void AddToggleField(CuiElementContainer container, string panel, UI4 dimensions, string color, string fieldName, bool currentValue)
        {
            UI.Toggle(container, panel, color, 12, dimensions, $"lobbysystem.setparameter {fieldName} {!currentValue}", currentValue);
        }
        private void AddInputField(CuiElementContainer container, string panel, string color, string closebuttoncolor, UI4 dimensions, string fieldName, object currentValue,int charslimit = 300,bool isPassword = false)
        {
            UI.Panel(container, panel, color, dimensions);

            string label = GetInputLabel(currentValue);
            UI4 closebuttondimensions = new UI4(dimensions.xMin, dimensions.yMin, dimensions.xMax, dimensions.yMax);
            closebuttondimensions.xMin = closebuttondimensions.xMax - (80f / 1920f);
            if (!string.IsNullOrEmpty(label))
            {
                UI.Label(container, panel, label, 12, dimensions, TextAnchor.MiddleLeft);
                UI.Button(container, panel, closebuttoncolor, "X", 12, closebuttondimensions, $"createroom.clear {fieldName}");
            }
            else UI.Input(container, panel, string.Empty, 12, $"lobbysystem.setparameter {fieldName}", dimensions,charslimit,isPassword);
        }
        private void AddSelectorField(CuiElementContainer container, string panel, string color, string selectbuttoncolor, UI4 dimensions, string fieldName, string currentValue, string hook, bool allowMultiple = false)
        {
            UI.Panel(container, panel, color, dimensions);

            if (!string.IsNullOrEmpty(currentValue))
                UI.Label(container, panel, currentValue.ToString(), 12, dimensions, TextAnchor.MiddleLeft);

            UI4 selectbuttondimension = dimensions;
            selectbuttondimension.xMin = selectbuttondimension.xMax - (200f / 1920f);
            UI.Button(container, panel, selectbuttoncolor, "Select", 11, selectbuttondimension, $"createroom.ui 1");
        }
        private void SetParameter(BasePlayer player, GameRoom gameRoom, string fieldName, object value)
        {
            if (value == null)
                return;

            switch (fieldName)
            {
                case "createroom.arena":
                    gameRoom.place = (string)value;
                    break;
                case "createroom.haspassword":
                    bool boolValue;
                    if (!TryConvertValue<bool>(value, out boolValue))
                    {
                        //You must enter enter boolean bla bla bla
                    }
                    else gameRoom.hasPassword = boolValue;
                    break;
                case "createroom.password":
                    //Eğer 5 karakterden fazla girerse uyarı ver etc etc
                    gameRoom.password = (string)value;
                    break;
                default:
                    break;
            }
        }
        private void SetParameter(BasePlayer player, string fieldName, object value)
        {
            if (value == null)
                return;

            switch (fieldName)
            {
                case "password.enteredpassword":
                    string password;
                    if(EnteredPasswords.TryGetValue(player, out password))
                        EnteredPasswords.Remove(player);

                    EnteredPasswords.Add(player, (string)value);
                    break;
                default:
                    break;
            }
        }
        #endregion
        #region Helpers
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName, 0UL, null);
        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name);
        bool PageHasElements(int index)
        {
            if ((index * 6) <= EventManager.BaseManager.Count - 1)
                return true;
            return false;
        }
        private bool TryConvertValue<T>(object value, out T result)
        {
            try
            {
                result = (T)Convert.ChangeType(value, typeof(T));
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }
        }
        private string GetInputLabel(object obj)
        {
            if (obj is string)
                return string.IsNullOrEmpty(obj as string) ? null : obj.ToString();
            else if (obj is int)
                return (int)obj <= 0 ? null : obj.ToString();
            else if (obj is float)
                return (float)obj <= 0 ? null : obj.ToString();
            return null;
        }
        private string GetConvertedRoomStatus(EventManager.EventStatus status)
        {
            switch (status)
            {
                case EventManager.EventStatus.Finished:
                    return "<color=#bcc43f>Finished</color>";
                case EventManager.EventStatus.Open:
                    return "<color=#60c0e8>Waiting</color>";
                case EventManager.EventStatus.Prestarting:
                    return "<color=#30b069>Starting</color>";
                case EventManager.EventStatus.Started:
                    return "<color=#c87575>Started</color>";
                default:
                    PrintError("GetRoomStatus method not working properly");
                    return string.Empty;
            }
        }
        private string ColoredOwnerName(string ownername)
        {
            string formattedname = string.Empty;
            for (int i = 0; i < ownername.Length; i++)
            {
                formattedname += string.Format("<color={0}>{1}</color>", configData.vipnamelabelcolors[i % 17], ownername.ElementAt(i));
            }
            return formattedname;
        }
        private void CreateSystemRooms()
        {
            object success;
            foreach(string arena in EventManager.Instance.Events.events.Keys)
            {
                for(int i = 0; i < configData.createdRoomperArena; i++)
                {
                    GameRoom room = new GameRoom(arena, "System", 1, 10, 1, 0,true);
                    success = EventManager.Instance.OpenEvent(arena, room);
                    if(success != null)
                        PrintError((string)success);
                }
            }
            RefreshLobbyUI();
            PrintToConsole("System rooms have been created");
        }
        private static string CommandSafe(string text, bool unpack = false) => unpack ? text.Replace("▊▊", " ") : text.Replace(" ", "▊▊");
        public enum RoomCreatorPage { Main, ArenaSelector }
        private void ClickEffect(BasePlayer player)
        {
            var effect = new Effect();
            effect.Init(Effect.Type.Generic, player.ServerPosition, Vector3.zero);
            effect.pooledString = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";
            EffectNetwork.Send(effect, player.net.connection);
        }
        private void LoadLobbySpawnPoints()
        {
            if (string.IsNullOrEmpty(configData.spawnfilename))
            {
                PrintError("No lobby spawnfile set in the config. Unable to continue");
                UnsubscibeAllHooks();
                return;
            }
            object success = Spawns?.Call("LoadSpawnFile", configData.spawnfilename);
            if (success is List<Vector3>)
            {
                lobbyspawnPoints = success as List<Vector3>;
                if (lobbyspawnPoints.Count == 0)
                {
                    PrintError("Loaded lobby spawnfile contains no spawn points. Unable to continue");
                    UnsubscibeAllHooks();
                    return;
                }
                PrintWarning($"Successfully loaded {lobbyspawnPoints.Count} lobby spawn points");
            }
            else
            {
                PrintError($"Unable to load the specified lobby spawnfile: {configData.spawnfilename}");
                UnsubscibeAllHooks();
                return;
            }
            
        }

        private void UnsubscibeAllHooks()
        {
            Unsubscribe(nameof(OnPlayerRespawn));
            Unsubscribe(nameof(OnServerInitialized));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
        }
        #endregion
        #region Localization
        public static string Message(string key, ulong playerId = 0U) => Instance.lang.GetMessage(key, Instance, playerId != 0U ? playerId.ToString() : null);
        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Notification.CantCreateRoom"] = "Please visit our website to buy VIP kits",
            ["Notification.Roomisfull"] = "Room is full"
        };
        #endregion
    }
}
