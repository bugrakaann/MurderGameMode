// Requires: EMInterface
// Requires: ImageLibrary
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("MurderSkinManager", "Apwned", "0.0.1")]
    [Description("Manages costumes&skins of MurderGameMode")]
    internal class MurderSkinManager: RustPlugin
    {
        //Permissions
        const string perm_skinmanager_use = "MurderSkinManager.use";
        [PluginReference] private Plugin ImageLibrary;
        public static MurderSkinManager Instance { get; private set; } = new MurderSkinManager();
        const string skinpanel_UI = "murderskinmanager.skinpanelui";

        #region Fields
        static Dictionary<ulong, SkinPreferences> skinPreferences = new Dictionary<ulong, SkinPreferences>();
        #endregion

        #region Config
        static ConfigData Configuration;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Listed costumes in the UI")]
            //string is kit name
            public Hash<string, SkinInfo> costumes { get; set; }
            [JsonProperty(PropertyName = "Listed revolvers in the UI")]
            public Hash<string, SkinInfo> revolverskins { get; set; }
            [JsonProperty(PropertyName = "Listed melees in the UI")]
            public Hash<string, SkinInfo> meleeskins { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            Config.WriteObject(Configuration, true);
        }
        protected override void LoadDefaultConfig() => Configuration = new ConfigData()
        {
            costumes = new Hash<string, SkinInfo>()
            {
                //Costumes are checked with their Key names& works with Kits plugin
                new KeyValuePair<string, SkinInfo>("defaultcostume", new SkinInfo("https://www.dropbox.com/s/blqabdyptm4zyh1/defaultcostume.png?dl=1","defaultcostumethumbnail")),
                new KeyValuePair<string, SkinInfo>("costume1", new SkinInfo("https://www.dropbox.com/s/ht55nnqasu21wna/costume1.png?dl=1","costume1thumbnail")),
                new KeyValuePair<string, SkinInfo>("costume2", new SkinInfo("https://www.dropbox.com/s/utsffmqodistl42/costume2.png?dl=1","costume2thumbnail")),
                new KeyValuePair<string, SkinInfo>("costume3", new SkinInfo("https://www.dropbox.com/s/0jsl9e29q9vy9u4/costume3.png?dl=1","costume3thumbnail")),
                new KeyValuePair<string, SkinInfo>("costume4", new SkinInfo("","costume4thumbnail"))
            },
            meleeskins = new Hash<string, SkinInfo>()
            {
                new KeyValuePair<string, SkinInfo>("trainwreckstvcombatknife", new SkinInfo("https://rustlabs.com/img/skins/324/11098.png","trainwreckstvcombatknife","knife.combat",2545769270)),
                new KeyValuePair<string, SkinInfo>("spoonkidscombatspoon", new SkinInfo("https://rustlabs.com/img/skins/324/11075.png","spoonkidscombatspoon","knife.combat",2468530027)),
                new KeyValuePair<string, SkinInfo>("emeraldknife", new SkinInfo("https://rustlabs.com/img/skins/324/39404.png","emeraldknife","knife.combat",1738307827)),
                new KeyValuePair<string, SkinInfo>("teaceremonyknife", new SkinInfo("https://rustlabs.com/img/skins/324/38804.png","teaceremonyknife","knife.combat",2187058228)),
                new KeyValuePair<string, SkinInfo>("toothedknife", new SkinInfo("https://rustlabs.com/img/skins/324/35805.png","toothedknife","knife.combat",1952506333)),
                new KeyValuePair<string, SkinInfo>("dreadlordknife", new SkinInfo("https://rustlabs.com/img/skins/324/35100.png","dreadlordknife","knife.combat",1910941833)),
                new KeyValuePair<string, SkinInfo>("razorknife", new SkinInfo("https://rustlabs.com/img/skins/324/32607.png","razorknife", "knife.combat",1739818618)),
                new KeyValuePair<string, SkinInfo>("bronzeravenknife", new SkinInfo("https://rustlabs.com/img/skins/324/32402.png","bronzeravenknife","knife.combat",1730634130)),
                new KeyValuePair<string, SkinInfo>("nukecombatknife", new SkinInfo("https://rustlabs.com/img/skins/324/32307.png","nukecombatknife","knife.combat",1706692846)),
                new KeyValuePair<string, SkinInfo>("combatknifefromhell", new SkinInfo("https://rustlabs.com/img/skins/324/32207.png","combatknifefromhell", "knife.combat",1719795241)),
                new KeyValuePair<string, SkinInfo>("thugknife", new SkinInfo("https://rustlabs.com/img/skins/324/32200.png","thugknife","knife.combat",1715608877)),
                new KeyValuePair<string, SkinInfo>("emperorsknife", new SkinInfo("https://rustlabs.com/img/skins/324/32003.png","emperorsknife","combatknife",1707332381)),
                new KeyValuePair<string, SkinInfo>("carbonelitecombatknife", new SkinInfo("https://rustlabs.com/img/skins/324/32002.png","carbonelitecombatknife","knife.combat",1706788762)),
                new KeyValuePair<string, SkinInfo>("glorycombatknife", new SkinInfo("https://rustlabs.com/img/skins/324/31901.png","glorycombatknife","knife.combat",1702783691)),
                new KeyValuePair<string, SkinInfo>("phantomcombatknife", new SkinInfo("https://rustlabs.com/img/skins/324/31900.png","phantomcombatknife","knife.combat",1702530691))
            },
            revolverskins = new Hash<string, SkinInfo>()
            {
                new KeyValuePair<string, SkinInfo>("neosoulpython", new SkinInfo("https://rustlabs.com/img/skins/324/47507.png","neosoulpython","pistol.python",2779090705)),
                new KeyValuePair<string, SkinInfo>("peterpartvpython", new SkinInfo("https://rustlabs.com/img/skins/324/11073.png","peterpartvpython", "pistol.python", 2470433447)),
                new KeyValuePair<string, SkinInfo>("venomouspythonrevolver", new SkinInfo("https://rustlabs.com/img/skins/324/37200.png","venomouspythonrevolver","pistol.python",2059988260)),
                new KeyValuePair<string, SkinInfo>("nomercypython", new SkinInfo("https://rustlabs.com/img/skins/324/33505.png", "nomercypython","pistol.python",1812135451)),
                new KeyValuePair<string, SkinInfo>("celticpython", new SkinInfo("https://rustlabs.com/img/skins/324/30805.png","celticpython","pistol.python",1624620555)),
                new KeyValuePair<string, SkinInfo>("temperedpython", new SkinInfo("https://rustlabs.com/img/skins/324/29303.png","temperedpython","pistol.python",1529514494)),
                new KeyValuePair<string, SkinInfo>("duelistpython", new SkinInfo("https://rustlabs.com/img/skins/324/28507.png","duelistpython","pistol.python",1455062983)),
                new KeyValuePair<string, SkinInfo>("slaughterpython", new SkinInfo("https://rustlabs.com/img/skins/324/27711.png","slaughterpython", "pistol.python",1356665596)),
                new KeyValuePair<string, SkinInfo>("punkpython", new SkinInfo("https://rustlabs.com/img/skins/324/26704.png","punkpython","pistol.python",1328632407)),
                new KeyValuePair<string, SkinInfo>("holypython", new SkinInfo("https://rustlabs.com/img/skins/324/26214.png","holypython","pistol.python",1265214612)),
                new KeyValuePair<string, SkinInfo>("trausispython", new SkinInfo("https://rustlabs.com/img/skins/324/25508.png","trausispython","pistol.python",1223105431)),
                new KeyValuePair<string, SkinInfo>("phantompython", new SkinInfo("https://rustlabs.com/img/skins/324/25208.png","phantompython","pistol.python",1214609010))

            }
        };
        protected override void SaveConfig() => Config.WriteObject(Configuration, true);
        #endregion

        #region Hooks
        void OnServerInitialized()
        {
            RegisterImages();
            permission.RegisterPermission(perm_skinmanager_use, this);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (skinPreferences.ContainsKey(player.userID))
                skinPreferences.Remove(player.userID);
        }
        #endregion

        #region UI
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName, 0UL, null);
        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name);

        void RegisterImages()
        {
            AddImage("skinmanagermainpanel.sm", "https://www.dropbox.com/s/vh4c4xetbzc7j8x/skinmanagermainpanel.png?dl=1");
            AddImage("revolverskinslabel.sm", "https://www.dropbox.com/s/83oiq7sqhh9ln7l/revolverskinslabel.png?dl=1");
            AddImage("previouspg.sm", "https://www.dropbox.com/s/02v7wmmzib9z0pf/previousarrow.png?dl=1");
            AddImage("nextpg.sm", "https://www.dropbox.com/s/ywwnnckcf52hv1t/nextarrow.png?dl=1");
            AddImage("pagenumborder.sm", "https://www.dropbox.com/s/m7tf0e9gdfoxhfk/pagenumborder.png?dl=1");
            AddImage("meleeskinslabel.sm", "https://www.dropbox.com/s/7e4su0ahaij109n/meleeskinslabel.png?dl=1");
            AddImage("labelequipped.sm", "https://www.dropbox.com/s/mpssd0ljujmwxkh/labelequipped.png?dl=1");
            AddImage("labelequip.sm", "https://www.dropbox.com/s/mvxxsl6rxc67ga2/labelequip.png?dl=1");
            AddImage("itemcontainer.sm", "https://www.dropbox.com/s/hytmr8tzcemuqyk/itemcontainer.png?dl=1");
            AddImage("costumeslabel.sm", "https://www.dropbox.com/s/8u1surmqluhcd81/costumeslabel.png?dl=1");
            AddImage("closebuttonrounded.sm", "https://www.dropbox.com/s/crzcqax8ar2nl47/closebuttonrounded.png?dl=1");
            AddImage("categoryhighlight.sm", "https://www.dropbox.com/s/r5lfl57tgnti4f7/categoryhighlight.png?dl=1");
            AddImage("categoryhighlight2.sm", "https://www.dropbox.com/s/0c9tn09qewvah5f/categoryhighlight2.png?dl=1");
            AddImage("categoryhighlight3.sm", "https://www.dropbox.com/s/ukhxslklg3ydhlb/categoryhighlight3.png?dl=1");

            RegisterItemThumbnails();
        }
        void RegisterItemThumbnails()
        {
            foreach(SkinInfo info in Configuration.costumes.Values)
                AddImage(info.thumbnailname, info.thumbnailurl);

            foreach(SkinInfo info in Configuration.meleeskins.Values)
                AddImage(info.thumbnailname, info.thumbnailurl);

            foreach(SkinInfo info in Configuration.revolverskins.Values)
                AddImage(info.thumbnailname, info.thumbnailurl);
        }
        [ConsoleCommand("murderskinmanager.closeui")]
        void cmdCloseSkinManager(ConsoleSystem.Arg arg)
        {
            CuiHelper.DestroyUi(arg.Player(), skinpanel_UI);
        }
        [ConsoleCommand("murderskinmanager.select")]
        void cmdSelectSkin(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            int itemno = arg.GetInt(0);
            PageCategory category = (PageCategory)arg.GetInt(1);
            int pagenum = arg.GetInt(2);

            if (!skinPreferences.ContainsKey(arg.Player().userID))
                skinPreferences.Add(player.userID, new SkinPreferences());

            switch (category)
            {
                case PageCategory.Costumes:
                    KeyValuePair<string,SkinInfo> costume = Configuration.costumes.ElementAt(itemno);
                    skinPreferences[player.userID].costume = costume.Key;
                    break;
                case PageCategory.RevolverSkins:
                    KeyValuePair<string, SkinInfo> revolverskin = Configuration.revolverskins.ElementAt(itemno);
                    skinPreferences[player.userID].revolverSkin.skinname = revolverskin.Key;
                    skinPreferences[player.userID].revolverSkin.skinID = revolverskin.Value.skinID;
                    skinPreferences[player.userID].revolverSkin.itemshortname = revolverskin.Value.shortname;
                    break;
                case PageCategory.MeleeSkins:
                    KeyValuePair<string, SkinInfo> meleeskin = Configuration.meleeskins.ElementAt(itemno);
                    skinPreferences[player.userID].meleeSkin.skinname = meleeskin.Key;
                    skinPreferences[player.userID].meleeSkin.skinID =meleeskin.Value.skinID;
                    skinPreferences[player.userID].meleeSkin.itemshortname =meleeskin.Value.shortname;
                    break;
            }
            SendSkinPanel(player, category, pagenum);
        }
        [ConsoleCommand("murderskinmanager.ui")]
        void cmdSkinUI(ConsoleSystem.Arg arg)
        {
            if (arg.GetInt(1) == -1 || !PageHasElements((PageCategory)arg.GetInt(0), arg.GetInt(1)))
                return;
            BasePlayer player = arg.Player();

            SendSkinPanel(player, (PageCategory)arg.GetInt(0), arg.GetInt(1));
        }
        void SendSkinPanel(BasePlayer player, PageCategory category, int pagenum = 0)
        {
            const string closebutton_UI = "murderskinmanager.closebuttonui";
            const string previouspage_UI = "murderskinmanager.previouspageui";
            const string nextpage_UI = "murderskinmanager.nextpageui";
            const string pagenumborder_UI = "murderskinmanager.pagenumborderui";
            const string costumescategory_UI = "murderskinmanager.costumescategoryui";
            const string revolverskincategory_UI = "murderskinmanager.revolverskincategoryui";
            const string meleeskincategory_UI = "murderskinmanager.meleeskincategoryui";

            CuiElementContainer container = UI.Container(skinpanel_UI, "0 0 0 0", UI.TransformToUI4(310f, 1610f, 217f, 937f), true,"Overall");
            UI.Image(container, skinpanel_UI, GetImage("skinmanagermainpanel.sm"), UI4.Full);
            UI.Image(container, closebutton_UI ,skinpanel_UI, GetImage("closebuttonrounded.sm"), UI.TransformToUI4(1227f, 1269f, 661f, 693f, 1300f, 720f));
            UI.Label(container, closebutton_UI, "X", 9, UI4.Full, TextAnchor.MiddleCenter, "1 1 1 1", "PermanentMarker.ttf");
            UI.Button(container, closebutton_UI, "0 0 0 0", string.Empty, 1, UI4.Full, "murderskinmanager.closeui");

            UI.Image(container, skinpanel_UI, GetImage("brandlogo"), UI.TransformToUI4(574f, 726f, 661f, 690f, 1300f, 720f));

            UI.Image(container, nextpage_UI,skinpanel_UI, GetImage("nextpg.sm"), UI.TransformToUI4(1257f, 1288f, 295f, 333f, 1300f, 720f));
            UI.Button(container, nextpage_UI, "0 0 0 0", string.Empty, 1, UI4.Full, $"murderskinmanager.ui {((int)category)} {pagenum + 1}");

            UI.Image(container, previouspage_UI,skinpanel_UI, GetImage("previouspg.sm"), UI.TransformToUI4(12f, 43f, 295f, 333f, 1300f, 720f));
            UI.Button(container, previouspage_UI, "0 0 0 0", string.Empty, 1, UI4.Full, $"murderskinmanager.ui {((int)category)} {pagenum - 1}");

            UI.Image(container, pagenumborder_UI,skinpanel_UI, GetImage("pagenumborder.sm"), UI.TransformToUI4(626f, 673f, 22f, 59f, 1300f, 720f));
            UI.Label(container, pagenumborder_UI, (pagenum + 1).ToString(), 15, UI4.Full);

            //Highlight selected tab
            if(category == PageCategory.Costumes)
                UI.Image(container, skinpanel_UI, GetImage("categoryhighlight.sm"), UI.TransformToUI4(31f, 220f, 568f, 646f, 1300f, 720f));
            else if(category == PageCategory.RevolverSkins)
                UI.Image(container, skinpanel_UI, GetImage("categoryhighlight2.sm"), UI.TransformToUI4(266f, 506f, 561f, 649f, 1300f, 720f));
            else if(category == PageCategory.MeleeSkins)
                UI.Image(container, skinpanel_UI, GetImage("categoryhighlight3.sm"), UI.TransformToUI4(530f, 735f, 561f, 648f, 1300f, 720f));

            UI.Image(container, costumescategory_UI,skinpanel_UI, GetImage("costumeslabel.sm"), UI.TransformToUI4(62f, 194.7f, 597f, 617.58f, 1300f, 720f));
            UI.Button(container, costumescategory_UI, "0 0 0 0", string.Empty, 5, UI4.Full, "murderskinmanager.ui 0 0");
            UI.Image(container, revolverskincategory_UI,skinpanel_UI, GetImage("revolverskinslabel.sm"), UI.TransformToUI4(300f, 476.16f, 597f, 617.58f, 1300, 720f));
            UI.Button(container, revolverskincategory_UI, "0 0 0 0", string.Empty, 5, UI4.Full, "murderskinmanager.ui 1 0");
            UI.Image(container, meleeskincategory_UI,skinpanel_UI, GetImage("meleeskinslabel.sm"), UI.TransformToUI4(565f, 707f, 597f, 617.58f, 1300f, 720f));
            UI.Button(container, meleeskincategory_UI, "0 0 0 0", string.Empty, 5, UI4.Full, "murderskinmanager.ui 2 0");

            for (int i = pagenum * 10; i < (pagenum +1) * 10; i++)
            {
                AddItemLabel(player ,container, category, skinpanel_UI, i, pagenum);
            }

            CuiHelper.DestroyUi(player, skinpanel_UI);
            CuiHelper.AddUi(player, container);
        }
        void AddItemLabel(BasePlayer player,CuiElementContainer container, PageCategory category, string parent, int position, int pagenum)
        {
            const string skinpanellabel_UI = "murderskinmanager.panellabelui";
            float xoffset = (position % 5) * 243.71f;
            int yoffset = (position / (5 + (pagenum * 10))) * 243;
            
            if(category == PageCategory.Costumes)
            {
                KeyValuePair<string, SkinInfo> skin = Configuration.costumes.ElementAtOrDefault(position);
                if (skin.Equals(default(KeyValuePair<string,SkinInfo>)))
                    return;
                UI.Image(container, skinpanellabel_UI, parent, GetImage("itemcontainer.sm"), UI.TransformToUI4(57f + xoffset, 267f + xoffset, 334f - yoffset, 542f - yoffset, 1300f, 720f));
                UI.Image(container, skinpanellabel_UI, GetImage(skin.Value.thumbnailname), UI.TransformToUI4(19f, 190f, 33f, 205f, 210f, 205f));
                if (!skinPreferences.ContainsKey(player.userID) || skinPreferences[player.userID]?.costume != skin.Key)
                {
                    UI.Image(container,skinpanellabel_UI, GetImage("labelequip.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    if (permission.UserHasPermission(player.UserIDString, perm_skinmanager_use))
                    {
                        UI.Label(container, skinpanellabel_UI, "Equip", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                        UI.Button(container, skinpanellabel_UI, "0 0 0 0", string.Empty, 5, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f), $"murderskinmanager.select {position} {((int)category)} {pagenum}");
                    }
                    else
                    {
                        UI.Label(container, skinpanellabel_UI, "Buy <color=#fed559>V</color><color=#fcacb2>I</color><color=#fd70da>P</color>", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    }
                }
                else if(skinPreferences[player.userID].costume == skin.Key)
                {
                    UI.Image(container, skinpanellabel_UI, GetImage("labelequipped.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    UI.Label(container, skinpanellabel_UI, "Equipped", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                }
            }
            else if(category == PageCategory.RevolverSkins)
            {
                KeyValuePair<string, SkinInfo> skin = Configuration.revolverskins.ElementAtOrDefault(position);
                if (skin.Equals(default(KeyValuePair<string, SkinInfo>)))
                    return;
                UI.Image(container, skinpanellabel_UI, parent, GetImage("itemcontainer.sm"), UI.TransformToUI4(57f + xoffset, 267f + xoffset, 334f - yoffset, 542f - yoffset, 1300f, 720f));
                UI.Image(container, skinpanellabel_UI, GetImage(skin.Value.thumbnailname), UI.TransformToUI4(19f, 190f, 33f, 205f, 210f, 205f));
                if (!skinPreferences.ContainsKey(player.userID) || skinPreferences[player.userID]?.revolverSkin?.skinname != skin.Key)
                {
                    UI.Image(container, skinpanellabel_UI, GetImage("labelequip.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    if (permission.UserHasPermission(player.UserIDString, perm_skinmanager_use))
                    {
                        UI.Label(container, skinpanellabel_UI, "Equip", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                        UI.Button(container, skinpanellabel_UI, "0 0 0 0", string.Empty, 5, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f), $"murderskinmanager.select {position} {((int)category)} {pagenum}");
                    }
                    else
                    {
                        UI.Label(container, skinpanellabel_UI, "Buy <color=#fed559>V</color><color=#fcacb2>I</color><color=#fd70da>P</color>", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    }
                }
                else if (skinPreferences[player.userID].revolverSkin.skinname == skin.Key)
                {
                    UI.Image(container, skinpanellabel_UI, GetImage("labelequipped.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    UI.Label(container, skinpanellabel_UI, "Equipped", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                }

            }
            else if(category == PageCategory.MeleeSkins)
            {
                KeyValuePair<string, SkinInfo> skin = Configuration.meleeskins.ElementAtOrDefault(position);
                if (skin.Equals(default(KeyValuePair<string, SkinInfo>)))
                    return;
                UI.Image(container, skinpanellabel_UI, parent, GetImage("itemcontainer.sm"), UI.TransformToUI4(57f + xoffset, 267f + xoffset, 334f - yoffset, 542f - yoffset, 1300f, 720f));
                UI.Image(container, skinpanellabel_UI, GetImage(skin.Value.thumbnailname), UI.TransformToUI4(19f, 190f, 33f, 205f, 210f, 205f));
                if (!skinPreferences.ContainsKey(player.userID) || skinPreferences[player.userID]?.meleeSkin?.skinname != skin.Key)
                {
                    UI.Image(container, skinpanellabel_UI, GetImage("labelequip.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    if (permission.UserHasPermission(player.UserIDString, perm_skinmanager_use))
                    {
                        UI.Label(container, skinpanellabel_UI, "Equip", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                        UI.Button(container, skinpanellabel_UI, "0 0 0 0", string.Empty, 5, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f), $"murderskinmanager.select {position} {((int)category)} {pagenum}");
                    }
                    else
                    {
                        UI.Label(container, skinpanellabel_UI, "Buy <color=#fed559>V</color><color=#fcacb2>I</color><color=#fd70da>P</color>", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    }
                }
                else if (skinPreferences[player.userID].meleeSkin.skinname == skin.Key)
                {
                    UI.Image(container, skinpanellabel_UI, GetImage("labelequipped.sm"), UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                    UI.Label(container, skinpanellabel_UI, "Equipped", 12, UI.TransformToUI4(0f, 209f, 0f, 31f, 210f, 205f));
                }
            }
        }
        bool PageHasElements(PageCategory category, int pagenum)
        {
            int startindex = pagenum * 10;
            if(category == PageCategory.Costumes)
            {
                if (Configuration.costumes.IsNullOrEmpty())
                    return false;
                return Configuration.costumes.Count > startindex;
            }
            if (category == PageCategory.MeleeSkins)
            {
                if (Configuration.meleeskins.IsNullOrEmpty())
                    return false;
                return Configuration.meleeskins.Count > startindex;
            }
            if (category == PageCategory.RevolverSkins)
            {
                if (Configuration.revolverskins.IsNullOrEmpty())
                    return false;
                return Configuration.revolverskins.Count > startindex;
            }
            return false;
        }
#endregion

        #region Classes
        public class SkinInfo
        {
            public SkinInfo(string _thumbnailurl, string _thumbnailname, string _shortname = "", ulong _skinID = 0)
            {
                thumbnailurl = _thumbnailurl;
                thumbnailname = _thumbnailname;
                shortname = _shortname;
                skinID = _skinID;
            }
            [JsonProperty("Url of the thumbnail")]
            public string thumbnailurl { get; set; }
            [JsonProperty("Name of thumbnail image (Registering image to ImageLibrary) (210x172)")]
            public string thumbnailname { get; set; }
            [JsonProperty("SkinID")]
            public ulong skinID { get; set; }
            [JsonProperty("Item short name")]
            public string shortname {  get; set; }
        }

        public class SkinPreferences
        {
            public string costume { get; set; } = "defaultcostume";
            public Skin meleeSkin { get; set; } = new Skin();
            public Skin revolverSkin { get; set; } = new Skin();

            public class Skin
            {
                public ulong skinID { get; set; } = 0;
                public string skinname { get; set; } =String.Empty;
                public string itemshortname { get; set; } = String.Empty;
            }
        }
        #endregion

        #region Enums
        enum PageCategory { Costumes, RevolverSkins, MeleeSkins}
        #endregion

        #region API
        public static SkinPreferences GetPreferencesOfPlayer(BasePlayer player)
        {
            if(skinPreferences.ContainsKey(player.userID))
                return skinPreferences[player.userID];
            return new SkinPreferences();
        }
        #endregion
    }
}
