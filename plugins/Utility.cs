using Network;
using Network.Visibility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Utility", "eandersson", "0.3.2", ResourceId = 2687)]
    public class Utility : RustPlugin
    {
        public readonly static int LayerGround = Rust.Layers.Solid | Rust.Layers.Mask.Water;

        public static void ClearInventory(BasePlayer player)
        {
            if (player == null) return;

            PlayerInventory inventory = player.inventory;
            if (inventory != null)
            {
                inventory.DoDestroy();
                inventory.ServerInit(player);
            }
        }

        public static void DisableTimeOfDay()
        {
            var time = UnityEngine.Object.FindObjectOfType<TOD_Time>();
            time.ProgressTime = false;
        }

        public static List<Vector3> GetCircumferencePositions(Vector3 center, float radius, float next, float y)
        {
            var positions = new List<Vector3>();
            float degree = 0f;

            while (degree < 360)
            {
                float angle = (float)(2 * Math.PI / 360) * degree;
                float x = center.x + radius * (float)Math.Cos(angle);
                float z = center.z + radius * (float)Math.Sin(angle);
                var position = new Vector3(x, center.y, z);

                position.y = y == 0f ? TerrainMeta.HeightMap.GetHeight(position) : y;
                positions.Add(position);

                degree += next;
            }

            return positions;
        }

        public static double GetDistanceBetween(Vector3 obj1, Vector3 obj2)
        {
            Vector3 difference = new Vector3(
                         obj1.x - obj2.x,
                         obj1.y - obj2.y,
                         obj1.z - obj2.z);

            double distance = Math.Sqrt(
            Math.Pow(difference.x, 2f) +
            Math.Pow(difference.y, 2f) +
            Math.Pow(difference.z, 2f));

            return distance;
        }

        public static Vector3 GetGroundPosition(Vector3 position)
        {
            RaycastHit hitinfo;
            if (Physics.Raycast(position + Vector3.up * 200, Vector3.down, out hitinfo, 250f, LayerGround))
                position.y = hitinfo.point.y;
            else position.y = TerrainMeta.HeightMap.GetHeight(position);

            return position;
        }

        public static void GiveStartingItems(BasePlayer player)
        {
            if (player == null) return;

            Utility.ClearInventory(player);
            foreach (string itemName in Data.StartItems)
            {
                Item item = ItemManager.CreateByName(itemName, 1);
                player.GiveItem(item);
            }
        }

        public static Vector3 RandomizePosition(Vector3 position, float randomness = 10f)
        {
            return position + new Vector3(
               (UnityEngine.Random.value - 0.5f) * randomness, 0f, (UnityEngine.Random.value - 0.5f) * randomness
            );
        }

        public static void SetBuildTimes()
        {
            foreach (var bp in ItemManager.bpList)
            {
                if (bp.userCraftable)
                {
                    if (Data.CustomCraftableBuildTime.ContainsKey(bp.name))
                        bp.time = Data.CustomCraftableBuildTime[bp.name];
                    else if (bp.name.StartsWith("ammo"))
                        bp.time = 1.0f;
                    else
                        bp.time = 5.0f;
                }
            }
        }

        public static void SetMaxHealth(BasePlayer player)
        {
            if (player == null) return;

            player.health = 100;
            player.metabolism.calories.Increase(500);
            player.metabolism.hydration.Increase(250);
        }

        public static void SetServerDefaults()
        {
            ConVar.Server.itemdespawn = 32f;
            ConVar.Server.itemdespawn = int.MaxValue - 1;
            ConVar.Decay.upkeep = true;
            ConVar.Decay.upkeep_period_minutes = 7200.0f;
            ConVar.Decay.upkeep_grief_protection = 0.0f;
            ConVar.Spawn.min_rate = 1.0f;
            ConVar.Spawn.max_rate = 1.0f;
            ConVar.Spawn.min_density = 1.0f;
            ConVar.Spawn.max_density = 1.0f;
            ConVar.Spawn.tick_populations = 5.0f;
            ConVar.Spawn.tick_individuals = 60.0f;
        }

        public static void SetStackSizes()
        {
            var gameitemList = ItemManager.itemList;
            foreach (var item in gameitemList)
            {
                if (item.stackable == 1000)
                {
                    item.stackable = 10000;
                }
                else if (item.stackable == 100)
                {
                    item.stackable = 1000;
                }
                else if (item.stackable == 2 || item.stackable == 3)
                {
                    item.stackable = 6;
                }
                else if (item.stackable == 128)
                {
                    item.stackable = 512;
                }
                else if (item.name.Contains("fuel.lowgrade.item"))
                {
                    item.stackable = 1000;
                }
            }
        }

        public static void Teleport(BasePlayer player, Vector3 targetPosition)
        {
            player.EnsureDismounted();
            player.Teleport(targetPosition);
        }

        public static void UnlockAllBlueprints(BasePlayer player)
        {
            player.ClientRPCPlayer(null, player, "craftMode", 1);

            var info = SingletonComponent<ServerMgr>.Instance.persistance.GetPlayerInfo(player.userID);
            info.unlockedItems = ItemManager.bpList.Select(x => x.targetItem.itemid).ToList();
            SingletonComponent<ServerMgr>.Instance.persistance.SetPlayerInfo(player.userID, info);

            player.SendNetworkUpdateImmediate(false);
            player.ClientRPCPlayer<int>(null, player, "UnlockedBlueprint", 0);
        }
    }
}