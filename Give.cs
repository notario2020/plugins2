﻿/***********************************************************************************************************************/
/*** DO NOT edit this file! Edit the files under `oxide/config` and/or `oxide/lang`, created once plugin has loaded. ***/
/*** Please note, support cannot be provided if the plugin has been modified. Please use a fresh copy if modified.   ***/
/***********************************************************************************************************************/

//#define DEBUG

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
#if REIGNOFKINGS
using CodeHatch.Engine.Networking;
using CodeHatch.Inventory.Blueprints;
using CodeHatch.Inventory.Blueprints.Components;
using CodeHatch.ItemContainer;
using UnityEngine;
#endif
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Give", "Wulf", "3.2.2")]
    [Description("Allows players with permission to give items or kits")]
    class Give : CovalencePlugin
    {
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            // TODO: Add optional cooldown for commands

            [JsonProperty("Item blacklist (name or item ID)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ItemBlacklist = new List<string>();

            [JsonProperty("Log usage to console")]
            public bool LogToConsole = false;

            //[JsonProperty("Log usage to file")] // TODO: Implement
            //public bool LogToFile = false;

            //[JsonProperty("Rotate logs daily")] // TODO: Implement
            //public bool RotateLogs = true;

            [JsonProperty("Show chat notices")]
            public bool ShowChatNotices = false;
#if RUST
            [JsonProperty("Show popup notices")]
            public bool ShowPopupNotices = false;
#endif
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["GiveKitFailed"] = "Could not give kit {0} to '{1}'",
                ["GiveKitSuccessful"] = "Giving kit {0} to '{1}'",
                ["GiveToFailed"] = "Could not give item {0} to '{1}'",
                ["GiveToSuccessful"] = "Giving item {0} x {1} to '{2}",
                ["InvalidItem"] = "{0} is not a valid item or is blacklisted",
                ["InvalidKit"] = "{0} is not a valid kit",
                ["ItemNotFound"] = "Could not find any item by name or ID '{0}' to give",
                ["ItemReceived"] = "You've received {0} x {1}",
                ["KitsUnavailable"] = "Kits plugin is not installed or is not loaded",
                ["NotAllowed"] = "You are not allowed to use the '{0}' command",
                ["NoPlayersConnected"] = "There are no players connected to give items to",
                ["NoPlayersFound"] = "No players found with name or ID '{0}'",
                ["PlayersFound"] = "Multiple players were found, please specify: {0}",
                ["PlayersOnly"] = "Command '{0}' can only be used by a player",
                ["PlayerNotConnected"] = "Unable to give to player '{0} ({1})', not connected",
                ["UsageGive"] = "Usage: {0} <item id or name> [amount] [player id or name]",
                ["UsageGiveAll"] = "Usage: {0} <item id or name> [amount]",
                ["UsageGiveTo"] = "Usage: {0} <player id or name> <item id or name> [amount]",
                ["UsageGiveArm"] = "Usage: {0} <item id or name> [amount]",
                ["UsageGiveKit"] = "Usage: {0} <kit name>",
                ["UsageGiveKitTo"] = "Usage: {0} <player id or name> <kit name>",
                ["UsageGiveKitAll"] = "Usage: {0} <kit name>",
            }, this);
        }

        #endregion Localization

        #region Initialization

        [PluginReference("Kits")]
        private Plugin HWKits;

        [PluginReference]
        private Plugin Kits;

        private const string permGive = "give.self";
        private const string permGiveAll = "give.all";
#if REIGNOFKINGS || RUST
        private const string permGiveArm = "give.arm";
#endif
        private const string permGiveTo = "give.to";
        private const string permGiveKit = "give.kit";
        private const string permGiveKitAll = "give.kitall";
        private const string permGiveKitTo = "give.kitto";
        private const string permBypassBlacklist = "give.bypassblacklist";

        private void Init()
        {
            permission.RegisterPermission(permGive, this);
            permission.RegisterPermission(permGiveAll, this);
#if REIGNOFKINGS || RUST
            permission.RegisterPermission(permGiveArm, this);
#endif
            permission.RegisterPermission(permGiveTo, this);
            permission.RegisterPermission(permGiveKit, this);
            permission.RegisterPermission(permGiveKitAll, this);
            permission.RegisterPermission(permGiveKitTo, this);
            permission.RegisterPermission(permBypassBlacklist, this);

            AddUniversalCommand(new[] { "inventory.give", "inventory.giveid", "give", "giveid" }, "GiveCommand");
            AddUniversalCommand(new[] { "inventory.giveall", "giveall" }, "GiveAllCommand");
#if REIGNOFKINGS || RUST
            AddUniversalCommand(new[] { "inventory.givearm", "givearm" }, "GiveArmCommand");
#endif
            AddUniversalCommand(new[] { "inventory.giveto", "giveto" }, "GiveToCommand");
            AddUniversalCommand(new[] { "inventory.givekit", "givekit" }, "GiveKitCommand");
            AddUniversalCommand(new[] { "inventory.givekitto", "givekitto" }, "GiveKitToCommand");
            AddUniversalCommand(new[] { "inventory.givekitall", "givekitall" }, "GiveKitAllCommand");

            // TODO: Localize commands
        }

        #endregion Initialization

        #region Item Giving

        // TODO: Add item giving support for other games

#if HURTWORLD
        private ItemObject FindItem(string itemNameOrId)
        {
            ItemObject item = null;
            int itemId;
            if (int.TryParse(itemNameOrId, out itemId))
            {
                item = GlobalItemManager.Instance.GetItem(itemId);
            }
            else
            {
                Dictionary<int, ItemObject>.Enumerator items = GlobalItemManager.Instance.GetItemEnumeration();
                while (items.MoveNext())
                {
                    if (items.Current.Value.GetNameKey().Equals(itemNameOrId, StringComparison.OrdinalIgnoreCase))
                    {
                        item = items.Current.Value;
                        break;
                    }
                }
            }
            return item;
        }
#elif RUST
        private ItemDefinition FindItem(string itemNameOrId)
        {
            ItemDefinition itemDef = ItemManager.FindItemDefinition(itemNameOrId.ToLower());
            if (itemDef == null)
            {
                int itemId;
                if (int.TryParse(itemNameOrId, out itemId))
                {
                    itemDef = ItemManager.FindItemDefinition(itemId);
                }
            }
            return itemDef;
        }
#endif

        private object GiveItem(IPlayer player, string itemNameOrId, int amount = 1, string container = "main")
        {
            if (!player.IsConnected)
            {
                return false;
            }

            if (config.ItemBlacklist.Contains(itemNameOrId, StringComparer.OrdinalIgnoreCase) && !player.HasPermission(permBypassBlacklist))
            {
                return null;
            }

            string itemName = itemNameOrId;
#if HURTWORLD
            ItemObject item = FindItem(itemNameOrId);
            if (item == null)
            {
                return false;
            }

            PlayerSession session = player.Object as PlayerSession;
            if (session == null)
            {
                return false;
            }

            PlayerInventory inventory = session.WorldPlayerEntity.GetComponent<PlayerInventory>();
            ItemGeneratorAsset generator = item.Generator;
            ItemObject itemObj;
            if (generator.IsStackable())
            {
                itemObj = GlobalItemManager.Instance.CreateItem(generator, amount);
                if (!inventory.GiveItemServer(itemObj))
                {
                    GlobalItemManager.SpawnWorldItem(itemObj, inventory);
                }
            }
            else
            {
                int amountGiven = 0;
                while (amountGiven < amount)
                {
                    itemObj = GlobalItemManager.Instance.CreateItem(generator);
                    if (!inventory.GiveItemServer(itemObj))
                    {
                        GlobalItemManager.SpawnWorldItem(itemObj, inventory);
                    }
                    amountGiven++;
                }
            }
#elif REIGNOFKINGS
            Player rokPlayer = player.Object as Player;
            if (rokPlayer == null)
            {
                return false;
            }

            Container itemContainer = null;
            switch (container.ToLower())
            {
                case "belt":
                    itemContainer = rokPlayer.CurrentCharacter.Entity.GetContainerOfType(CollectionTypes.Hotbar);
                    break;

                default:
                    itemContainer = rokPlayer.CurrentCharacter.Entity.GetContainerOfType(CollectionTypes.Inventory);
                    break;
            }

            InvItemBlueprint blueprint = InvDefinitions.Instance.Blueprints.GetBlueprintForName(itemName, true, true);
            if (blueprint == null)
            {
                return false;
            }

            ContainerManagement containerManagement = blueprint.TryGet<ContainerManagement>();
            int stackableAmount = containerManagement != null ? containerManagement.StackLimit : 0;
            int amountGiven = 0;
            while (amountGiven < amount)
            {
                int amountToGive = Mathf.Min(stackableAmount, amount - amountGiven);
                InvGameItemStack itemStack = new InvGameItemStack(blueprint, amountToGive, null);
                if (!ItemCollection.AutoMergeAdd(itemContainer.Contents, itemStack))
                {
                    int stackAmount = amountToGive - itemStack.StackAmount;
                    if (stackAmount != 0)
                    {
                        amountGiven += stackAmount;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    amountGiven += amountToGive;
                }
                if (itemContainer.Contents.FreeSlotCount == 0)
                {
                    break;
                }
            }
#elif RUST
            Item item = ItemManager.Create(FindItem(itemNameOrId));
            if (item == null)
            {
                return false;
            }

            BasePlayer basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
            {
                return false;
            }

            ItemContainer itemContainer = null;
            switch (container.ToLower())
            {
                case "belt":
                    itemContainer = basePlayer.inventory.containerBelt;
                    break;

                default:
                    itemContainer = basePlayer.inventory.containerMain;
                    break;
            }

            item.amount = amount;
            if (!item.MoveToContainer(itemContainer) && !basePlayer.inventory.GiveItem(item))
            {
                item.Remove();
                return false;
            }

            itemName = item.info.displayName.english;
            if (config.ShowPopupNotices)
            {
                player.Command("note.inv", item.info.itemid, amount);
                player.Command("gametip.showgametip", GetLang("ItemReceived", player.Id, itemName, amount));
                timer.Once(2f, () => player.Command("gametip.hidegametip"));
            }
#endif
            if (config.ShowChatNotices)
            {
                Message(player, "ItemReceived", itemName, amount);
            }
            if (config.LogToConsole)
            {
                Log($"{player.Name} {amount} x {itemName}");
            }

            return true;
        }

        private bool TryGiveItem(IPlayer player, string itemNameOrId, int amount = 1, string container = "main")
        {
            object giveItem = GiveItem(player, itemNameOrId, amount, container);
            if (giveItem == null)
            {
                Message(player, "InvalidItem", itemNameOrId);
            }
            else if (!(bool)giveItem)
            {
                Message(player, "GiveToFailed", itemNameOrId, player.Name);
            }
            else
            {
                Message(player, "GiveToSuccessful", itemNameOrId, amount, player.Name);
            }

            return giveItem is bool ? (bool)giveItem : false;
        }

        private void GiveCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permGive))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 1)
            {
                Message(player, "UsageGive", command);
                return;
            }

            int amount;
            TryGiveItem(player, args[0], args.Length >= 2 && int.TryParse(args[1], out amount) ? amount : 1);
        }

        private void GiveToCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permGiveTo))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 2)
            {
                Message(player, "UsageGiveTo", command);
                return;
            }

            IPlayer target = FindPlayer(args[0], player);
            if (target == null)
            {
                target = player;
                if (target.IsServer)
                {
                    Message(player, "PlayersOnly", command);
                    return;
                }
            }

            int amount;
            TryGiveItem(target, args[1], args.Length >= 3 && int.TryParse(args[2], out amount) ? amount : 1);
        }

        private void GiveAllCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permGiveAll))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 1)
            {
                Message(player, "UsageGive", command);
                return;
            }

            if (!players.Connected.Any())
            {
                Message(player, "NoPlayersConnected");
                return;
            }

            int amount = args.Length >= 2 && int.TryParse(args[1], out amount) ? amount : 1;
            foreach (IPlayer target in players.Connected.ToArray())
            {
                GiveItem(target, args[0], amount);
            }
        }

#if REIGNOFKINGS || RUST
        private void GiveArmCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permGiveArm))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 1)
            {
                Message(player, "UsageGiveArm", command);
                return;
            }

            int amount;
            TryGiveItem(player, args[0], args.Length >= 2 && int.TryParse(args[1], out amount) ? amount : 1, "belt");
        }
#endif

        #endregion Item Giving

        #region Kit Giving

        private bool TryGiveKit(IPlayer player, string kitName)
        {
            if (!Kits.Call<bool>("isKit", kitName))
            {
                Message(player, "InvalidKit", kitName);
                return false;
            }

            if (player.IsConnected)
            {
                Message(player, "PlayerNotConnected", player.Name, player.Id);
                return false;
            }

            bool giveKit = false;
#if HURTWORLD
            giveKit = Kits.Call<bool>("GiveKit", player.Object as PlayerSession, kitName);
#elif REIGNOFKINGS
            giveKit = Kits.Call<bool>("GiveKit", player.Object as Player, kitName);
#elif RUST
            giveKit = Kits.Call<bool>("GiveKit", player.Object as BasePlayer, kitName);
#endif

            Message(player, giveKit ? "GiveKitSuccessful" : "GiveKitFailed", kitName, player.Name);

            return giveKit;
        }

        private void GiveKitCommand(IPlayer player, string command, string[] args)
        {
            if (Kits == null || !Kits.IsLoaded)
            {
                Message(player, "KitsUnavailable");
                return;
            }

            if (!player.HasPermission(permGiveKit))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 1)
            {
                Message(player, "UsageGiveKit", command);
                return;
            }

            if (player.IsServer)
            {
                Message(player, "PlayersOnly", command);
                return;
            }

            TryGiveKit(player, args[0]);
        }

        private void GiveKitToCommand(IPlayer player, string command, string[] args)
        {
            if (Kits == null || !Kits.IsLoaded)
            {
                Message(player, "KitsUnavailable");
                return;
            }

            if (!player.HasPermission(permGiveKitTo))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 2)
            {
                Message(player, "UsageGiveKitTo", command);
                return;
            }

            IPlayer target = FindPlayer(args[1], player);
            if (target != null)
            {
                TryGiveKit(target, args[0]);
            }
        }

        private void GiveKitAllCommand(IPlayer player, string command, string[] args)
        {
            if (Kits == null || !Kits.IsLoaded)
            {
                Message(player, "KitsUnavailable");
                return;
            }

            if (!player.HasPermission(permGiveKitAll))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args.Length < 1)
            {
                Message(player, "UsageGiveKitAll", command);
                return;
            }

            if (!Kits.Call<bool>("isKit", args[0]))
            {
                Message(player, "InvalidKit", args[0]);
                return;
            }

            if (!players.Connected.Any())
            {
                Message(player, "NoPlayersConnected");
                return;
            }

            foreach (IPlayer target in players.Connected.ToArray())
            {
                if (!TryGiveKit(target, args[0]))
                {
                    break;
                }
            }

        }

        #endregion Kit Giving

        #region Helpers

        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (message.Key.Equals(command))
                    {
                        if (!string.IsNullOrEmpty(message.Value))
                        {
                            AddCovalenceCommand(message.Value, command);
                        }
                    }
                }
            }
        }

        private IPlayer FindPlayer(string playerNameOrId, IPlayer player)
        {
            IPlayer[] foundPlayers = players.FindPlayers(playerNameOrId).ToArray();
            if (foundPlayers.Length > 1)
            {
                Message(player, "PlayersFound", string.Join(", ", foundPlayers.Select(p => p.Name).Take(10).ToArray()).Truncate(60));
                return null;
            }

            IPlayer target = foundPlayers.Length == 1 ? foundPlayers[0] : null;
            if (target == null)
            {
                Message(player, "NoPlayersFound", playerNameOrId);
                return null;
            }

            return target;
        }

        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }

        private void Message(IPlayer player, string textOrLang, params object[] args)
        {
            if (player.IsConnected)
            {
                string message = GetLang(textOrLang, player.Id, args);
                player.Reply(message != textOrLang ? message : textOrLang);
            }
        }

        #endregion Helpers
    }
}