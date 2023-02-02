using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Newtonsoft.Json.Converters;
using UnityEngine;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("PickableSpawner", "Apwned", "0.1"), Description("A database of sets of pickable item spawn points, created by a user and used by other plugins")]
    class MurderPickableSpawn : CSharpPlugin
    {
        #region Fields
        private SpawnsData _spawnsData;

        private Dictionary<string, List<Vector3>> _loadedSpawnfiles = new Dictionary<string, List<Vector3>>();

        private Dictionary<ulong, List<Vector3>> _spawnFileCreators = new Dictionary<ulong, List<Vector3>>();

        private List<ulong> _isEditing = new List<ulong>();
        #endregion

        #region Oxide Hooks
        private void Loaded() => LoadData();

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void OnServerInitialized()
        {
            VerifyFilesExist();
            AddCovalenceCommand("pickable", "cmdSpawns");
        }
        #endregion

        #region Functions
        private void VerifyFilesExist()
        {
            bool hasChanged = false;
            for (int i = 0; i < _spawnsData.Spawnfiles.Count; i++)
            {
                string name = _spawnsData.Spawnfiles[i];

                if (!Interface.Oxide.DataFileSystem.ExistsDatafile($"MurderPickableDatabase/{name}"))
                {
                    _spawnsData.Spawnfiles.Remove(name);
                    hasChanged = true;
                }
                else
                {
                    if (LoadSpawns(name) != null)
                    {
                        _spawnsData.Spawnfiles.Remove(name);
                        hasChanged = true;
                    }
                    else if (_loadedSpawnfiles[name].Count == 0)
                    {
                        _spawnsData.Spawnfiles.Remove(name);
                        hasChanged = true;
                    }
                }
            }

            if (hasChanged)
                SaveData();
        }

        private object LoadSpawns(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Message("noFile");

            if (!_loadedSpawnfiles.ContainsKey(name))
            {
                object success = LoadSpawnFile(name);
                if (success == null)
                    return Message("noFile");
                else _loadedSpawnfiles.Add(name, (List<Vector3>)success);
            }
            return null;
        }
        #endregion

        #region API
        private object GetSpawnsCount(string filename)
        {
            object success = LoadSpawns(filename);
            if (success != null)
                return (string)success;

            return _loadedSpawnfiles[filename].Count;
        }
        [HookMethod("GetRandomItemSpawn")]
        internal object GetRandomItemSpawn(string filename)
        {
            object success = LoadSpawns(filename);
            if (success != null)
                return (string)success;

            return _loadedSpawnfiles[filename].GetRandom();
        }

        private object GetRandomSpawnRange(string filename, int min, int max)
        {
            object success = LoadSpawns(filename);
            if (success != null)
                return (string)success;

            List<Vector3> list = _loadedSpawnfiles[filename];

            return list[UnityEngine.Random.Range(Mathf.Clamp(min, 0, list.Count - 1), Mathf.Clamp(max, 0, list.Count - 1))];
        }

        private object GetSpawn(string filename, int number)
        {
            object success = LoadSpawns(filename);
            if (success != null)
                return (string)success;

            List<Vector3> list = _loadedSpawnfiles[filename];

            return list[Mathf.Clamp(number, 0, list.Count - 1)];
        }

        private string[] GetSpawnfileNames() => _spawnsData.Spawnfiles.ToArray();
        #endregion

        #region Chat Commands
        void cmdSpawns(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player.net.connection.authLevel < 1)
            {
                PrintToChat(player, Message("noAccess", player.UserIDString));
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendHelpText(player);
                return;
            }

            if (args.Length >= 1)
            {
                switch (args[0].ToLower())
                {
                    case "new":
                        if (IsCreatingFile(player))
                        {
                            PrintToChat(player, Message("alreadyCreating", player.UserIDString));
                            return;
                        }

                        _spawnFileCreators.Add(player.userID, new List<Vector3>());

                        PrintToChat(player, Message("newCreating", player.UserIDString));
                        return;

                    case "open":
                        if (args.Length >= 2)
                        {
                            if (IsCreatingFile(player))
                            {
                                PrintToChat(player, Message("isCreating", player.UserIDString));
                                return;
                            }
                            object spawns = LoadSpawnFile(args[1]);
                            if (spawns != null)
                            {
                                _spawnFileCreators.Add(player.userID, (List<Vector3>)spawns);
                                PrintToChat(player, string.Format(Message("opened", player.UserIDString), _spawnFileCreators[player.userID].Count));
                                _isEditing.Add(player.userID);
                            }
                            else PrintToChat(player, Message("invalidFile", player.UserIDString));
                        }
                        else PrintToChat(player, Message("fileName", player.UserIDString));
                        return;

                    case "add":
                        if (!IsCreatingFile(player))
                        {
                            PrintToChat(player, Message("notCreating", player.UserIDString));
                            return;
                        }
                        else
                        {
                            RaycastHit hit;
                            Physics.Raycast(player.eyes.HeadRay(), out hit);
                            _spawnFileCreators[player.userID].Add(hit.point);
                            int number = _spawnFileCreators[player.userID].Count;
                            DDrawPosition(player, _spawnFileCreators[player.userID][number - 1], number.ToString());
                            PrintToChat(player, string.Format("Added Pickable n°{0}", _spawnFileCreators[player.userID].Count));
                        }
                        return;

                    case "remove":
                        if (args.Length >= 2)
                        {
                            if (!IsCreatingFile(player))
                            {
                                PrintToChat(player, Message("notCreating", player.UserIDString));
                                return;
                            }

                            if (_spawnFileCreators[player.userID].Count > 0)
                            {
                                int number;
                                if (int.TryParse(args[1], out number))
                                {
                                    if (number <= _spawnFileCreators[player.userID].Count)
                                    {
                                        _spawnFileCreators[player.userID].RemoveAt(number - 1);
                                        PrintToChat(player, string.Format(Message("remSuccess", player.UserIDString), number));
                                    }
                                    else PrintToChat(player, Message("nexistNum", player.UserIDString));
                                }
                                else PrintToChat(player, Message("noNum", player.UserIDString));
                            }
                            else PrintToChat(player, Message("noSpawnpoints", player.UserIDString));
                        }
                        else PrintToChat(player, "/spawns remove <number>");
                        return;

                    case "save":
                        if (args.Length >= 2)
                        {
                            if (!IsCreatingFile(player))
                            {
                                PrintToChat(player, Message("noCreate", player.UserIDString));
                                return;
                            }
                            if (_spawnFileCreators.ContainsKey(player.userID) && _spawnFileCreators[player.userID].Count > 0)
                            {
                                if (!_spawnsData.Spawnfiles.Contains(args[1]) && !_loadedSpawnfiles.ContainsKey(args[1]))
                                {
                                    PrintToChat(player, string.Format(Message("saved", player.UserIDString), _spawnFileCreators[player.userID].Count, args[1]));
                                    SaveSpawnFile(player, args[1]);
                                    return;
                                }

                                if (_isEditing.Contains(player.userID))
                                {
                                    SaveSpawnFile(player, args[1]);
                                    PrintToChat(player, string.Format(Message("overwriteSuccess", player.UserIDString), args[1]));
                                    _isEditing.Remove(player.userID);
                                    return;
                                }

                                PrintToChat(player, Message("spawnfileExists", player.UserIDString));
                                return;
                            }
                            else PrintToChat(player, Message("noSpawnpoints", player.UserIDString));
                        }
                        else PrintToChat(player, "/pickable save <filename>");
                        return;

                    case "close":
                        if (!IsCreatingFile(player))
                        {
                            PrintToChat(player, Message("noCreate", player.UserIDString));
                            return;
                        }
                        _spawnFileCreators.Remove(player.userID);
                        PrintToChat(player, Message("noSave", player.UserIDString));
                        return;

                    case "show":
                        if (!IsCreatingFile(player))
                        {
                            PrintToChat(player, Message("notCreating", player.UserIDString));
                            return;
                        }
                        if (_spawnFileCreators[player.userID].Count > 0)
                        {
                            float time = 10f;
                            if (args.Length > 1)
                                float.TryParse(args[1], out time);

                            for (int i = 0; i < _spawnFileCreators[player.userID].Count; i++)
                                DDrawPosition(player, _spawnFileCreators[player.userID][i], i.ToString(), time);

                            return;
                        }
                        else PrintToChat(player, Message("noSp", player.UserIDString));
                        return;

                    default:
                        SendHelpText(player);
                        break;
                }
            }
        }

        private void DDrawPosition(BasePlayer player, Vector3 point, string name, float time = 10f)
        {
            player.SendConsoleCommand("ddraw.text", time, Color.green, point + new Vector3(0, 0.7f, 0), $"<size=40>{name}</size>");
            player.SendConsoleCommand("ddraw.box", time, Color.green, point, 0.4f);
        }

        private void SendHelpText(BasePlayer player)
        {
            PrintToChat(player, Message("newSyn", player.UserIDString));
            PrintToChat(player, Message("openSyn", player.UserIDString));
            PrintToChat(player, Message("addSyn", player.UserIDString));
            PrintToChat(player, Message("remSyn", player.UserIDString));
            PrintToChat(player, Message("saveSyn", player.UserIDString));
            PrintToChat(player, Message("closeSyn", player.UserIDString));
            PrintToChat(player, Message("showSyn", player.UserIDString));
        }

        private bool IsCreatingFile(BasePlayer player) => _spawnFileCreators.ContainsKey(player.userID);
        #endregion

        #region Data Management
        private DynamicConfigFile data;

        private void SaveData() => data.WriteObject(_spawnsData);

        private void LoadData()
        {
            data = Interface.Oxide.DataFileSystem.GetFile("MurderPickableDatabase/spawns_data");

            try
            {
                _spawnsData = data.ReadObject<SpawnsData>();
            }
            catch
            {
                _spawnsData = new SpawnsData();
            }
        }

        private void SaveSpawnFile(BasePlayer player, string name)
        {
            DynamicConfigFile configFile = Interface.Oxide.DataFileSystem.GetFile($"MurderPickableDatabase/{name}");
            configFile.Clear();
            configFile.Settings.Converters = new JsonConverter[] { new StringEnumConverter(), new UnityVector3Converter() };

            Spawnfile spawnFile = new Spawnfile();

            for (int i1 = 0; i1 < _spawnFileCreators[player.userID].Count; i1++)
            {
                Vector3 spawnpoint = _spawnFileCreators[player.userID][i1];

                spawnFile.spawnPoints.Add(i1.ToString(), spawnpoint);
            }

            configFile.WriteObject(spawnFile);

            if (!_spawnsData.Spawnfiles.Contains(name))
                _spawnsData.Spawnfiles.Add(name);

            if (!_loadedSpawnfiles.ContainsKey(name))
                _loadedSpawnfiles.Add(name, _spawnFileCreators[player.userID]);
            else _loadedSpawnfiles[name] = _spawnFileCreators[player.userID];

            SaveData();

            _spawnFileCreators.Remove(player.userID);
        }

        private object LoadSpawnFile(string name)
        {
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile($"MurderPickableDatabase/{name}"))
                return null;

            DynamicConfigFile configFile = Interface.GetMod().DataFileSystem.GetDatafile($"MurderPickableDatabase/{name}");
            configFile.Settings.Converters = new JsonConverter[] { new StringEnumConverter(), new UnityVector3Converter() };

            Spawnfile spawnFile = new Spawnfile();
            spawnFile = configFile.ReadObject<Spawnfile>();

            List<Vector3> list = spawnFile.spawnPoints.Values.ToList();
            if (list.Count < 1)
                return null;

            return list;
        }

        private class SpawnsData
        {
            public List<string> Spawnfiles = new List<string>();
        }

        private class Spawnfile
        {
            public Dictionary<string, Vector3> spawnPoints = new Dictionary<string, Vector3>();
        }

        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector3 vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                JObject o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }
        #endregion

        #region Helpers
        private void PrintToChat(BasePlayer player, string format, params object[] args)
        {
            if (player?.net != null)
            {
                player.SendConsoleCommand("chat.add", 2, 0, (args.Length != 0) ? string.Format(format, args) : format);
            }
        }
        #endregion

        #region Messaging
        private string Message(string key, string ID = null) => lang.GetMessage(key, this, ID);

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            {"noFile", "This file doesn't exist" },
            {"alreadyCreating", "You are already creating a pickable spawn file" },
            {"newCreating", "You now creating a new pickable spawn file" },
            {"isCreating", "You must save/close your current pickable spawn file first. Type /pickable for more information" },
            {"opened", "Opened pickable spawnfile with {0} spawns" },
            {"invalidFile", "This spawnfile is empty or not valid" },
            {"fileName", "You must enter a filename" },
            {"notCreating", "You must create/open a new Spawn file first /pickable for more information" },
            {"remSuccess", "Successfully removed spawn n°{0}" },
            {"nexistNum", "This spawn number doesn't exist" },
            {"noNum", "You must enter a spawn point number" },
            {"noSpawnpoints", "You haven't set any spawn points yet" },
            {"noCreate", "You must create a new Spawn file first. Type /pickable for more information" },
            {"noSave", "Spawn file closed without saving" },
            {"noSp", "You must add spawnpoints first" },
            {"newSyn", "/pickable new - Create a new spawn file" },
            {"openSyn", "/pickable open - Open a existing spawn file for editing" },
            {"addSyn", "/pickable add - Add a new spawn point" },
            {"remSyn", "/pickable remove <number> - Remove a spawn point" },
            {"saveSyn", "/pickable save <filename> - Saves your spawn file" },
            {"closeSyn", "/pickable close - Cancel spawn file creation" },
            {"showSyn", "/pickable show <opt:time> - Display a box at each spawnpoint" },
            {"noAccess", "You are not allowed to use this command" },
            {"saved", "{0} pickable spawnpoints saved into {1}" },
            {"spawnfileExists", "A pickable spawn file with that name already exists" },
            {"overwriteSuccess", "You have successfully edited the pickable spawnfile {0}" }
        };
        #endregion
    }
}
