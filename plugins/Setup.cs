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
    [Info("Setup", "eandersson", "0.3.2", ResourceId = 2682)]
    public class Setup : RustPlugin
    {
        private Arena arena = null;
        private Data data = null;
        private Walls walls = null;

        private List<Data.VendingItem> components = new List<Data.VendingItem>();
        private List<Data.VendingItem> building = new List<Data.VendingItem>();
        private List<Data.VendingItem> scrap = new List<Data.VendingItem>();
        private List<Data.VendingItem> armor = new List<Data.VendingItem>();
        private List<Data.VendingItem> attire = new List<Data.VendingItem>();
        private List<Data.VendingItem> weapons = new List<Data.VendingItem>();

        public void Configure(Arena arena)
        {
            this.arena = arena;
            this.data = arena.data;
            this.walls = arena.walls;

            LoadVendingItems();
        }

        private void LoadVendingItems()
        {
            building.Add(new Data.VendingItem("sewingkit", 1, "scrap", 5));
            building.Add(new Data.VendingItem("metalblade", 1, "scrap", 5));
            building.Add(new Data.VendingItem("techparts", 1, "scrap", 25));
            building.Add(new Data.VendingItem("gears", 1, "scrap", 25));
            building.Add(new Data.VendingItem("lowgradefuel", 50, "scrap", 10));
            building.Add(new Data.VendingItem("leather", 100, "scrap", 10));

            components.Add(new Data.VendingItem("riflebody", 1, "scrap", 250));
            components.Add(new Data.VendingItem("smgbody", 1, "scrap", 30));
            components.Add(new Data.VendingItem("semibody", 1, "scrap", 15));
            components.Add(new Data.VendingItem("metalpipe", 1, "scrap", 5));
            components.Add(new Data.VendingItem("metalspring", 1, "scrap", 10));
            components.Add(new Data.VendingItem("roadsigns", 1, "scrap", 10));
            components.Add(new Data.VendingItem("rope", 1, "scrap", 1));

            armor.Add(new Data.VendingItem("coffeecan.helmet", 1, "scrap", 75));
            armor.Add(new Data.VendingItem("roadsign.gloves", 1, "scrap", 50));
            armor.Add(new Data.VendingItem("roadsign.jacket", 1, "scrap", 150));
            armor.Add(new Data.VendingItem("roadsign.kilt", 1, "scrap", 100));

            scrap.Add(new Data.VendingItem("scrap", 10, "wood", 600));
            scrap.Add(new Data.VendingItem("scrap", 20, "stones", 600));
            scrap.Add(new Data.VendingItem("scrap", 50, "metal.fragments", 600));
            scrap.Add(new Data.VendingItem("scrap", 500, "metal.fragments", 6000));
            scrap.Add(new Data.VendingItem("scrap", 250, "metal.refined", 50));
            
            weapons.Add(new Data.VendingItem("rifle.bolt", 1, "scrap", 8000));
            weapons.Add(new Data.VendingItem("rifle.lr300", 1, "scrap", 400));
            weapons.Add(new Data.VendingItem("rifle.m39", 1, "scrap", 300));
            weapons.Add(new Data.VendingItem("jackhammer", 1, "scrap", 400)); 

            attire.Add(new Data.VendingItem("rocket.launcher", 1, "scrap", 500));
            attire.Add(new Data.VendingItem("explosive.timed", 1, "scrap", 250));
            attire.Add(new Data.VendingItem("ammo.rocket.basic", 1, "scrap", 100));
            attire.Add(new Data.VendingItem("weapon.mod.small.scope", 1, "scrap", 1600));
        }

        private MiningQuarry CreateCenterQuarry(ulong ownerId, Vector3 postion)
        {
            Vector3 spawnPos = postion;
            Quaternion rotation = Quaternion.Euler(new Vector3(0f, 0f, 0f));
            MiningQuarry entity = GameManager.server.CreateEntity(Data.PrefabQuarryStatic, spawnPos, rotation, true) as MiningQuarry;
            entity.isStatic = false;
            entity.OwnerID = ownerId;
            // entity.SetFlag(BaseEntity.Flags.On, true, true, true);
            // entity.InvokeRepeating(new Action(entity.ProcessResources), entity.processRate, entity.processRate);
            entity.Spawn();
            entity.fuelStoragePrefab.instance.GetComponent<StorageContainer>().isLootable = false;
            var fuel = ItemManager.CreateByName("lowgradefuel", 10000);
            fuel.MoveToContainer(entity.fuelStoragePrefab.instance.GetComponent<StorageContainer>().inventory);
            return entity;
        }

        public void StartArena(string Id)
        {
            var arena = this.data.Arenas[Id];

            this.walls.Create(arena.Id, arena.Center.transform.position);

            foreach (var quarry in arena.Quarries)
            {
                Vector3 spawnPos = quarry.transform.position;
                Quaternion rotation = Quaternion.Euler(new Vector3(0f, 90f, 0f));
                MiningQuarry entity = GameManager.server.CreateEntity(Data.PrefabQuarry, spawnPos, rotation, true) as MiningQuarry;

                entity.OwnerID = arena.Id;
                entity.SetFlag(BaseEntity.Flags.On, true, true, true);
                entity.InvokeRepeating(new Action(entity.ProcessResources), entity.processRate, entity.processRate);
                entity.Spawn();
                entity.fuelStoragePrefab.instance.GetComponent<StorageContainer>().isLootable = false;
                var fuel = ItemManager.CreateByName("lowgradefuel", 10000);
                fuel.MoveToContainer(entity.fuelStoragePrefab.instance.GetComponent<StorageContainer>().inventory);
            }

            MiningQuarry centerQuarry = CreateCenterQuarry(arena.Id, arena.Center.transform.position);
            this.data.CenterMiningQuarryToArena.Add(centerQuarry, arena);
        }

        public void InitializeArenas()
        {
            Puts("Initializing Arenas");
            this.data.InitialStartingPositions.Clear();

            foreach (var spawn in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (spawn.name.Contains("letter_"))
                {
                    string name = spawn.name.Split('/').Last();
                    name = name.Split('.').First();
                    name = name.Split('_').Last();
                    this.data.Arenas.Add(name, new Data.ArenaData(Convert.ToUInt64(this.data.Arenas.Count()), spawn, name));
                }
                else if (spawn.name.Contains("spawn_point"))
                {
                    this.data.InitialStartingPositions.Add(spawn.transform.position);
                }
            }

            foreach (NPCVendingMachine vending in BaseNetworkable.serverEntities.Where(e => e?.name != null).OfType<NPCVendingMachine>().ToList())
            {
                if (vending.name == Data.PrefabVendingComponents)
                {
                    SetupVendingMachine(vending, components);
                }
                else if (vending.name == Data.PrefabVendingFarming)
                {
                    SetupVendingMachine(vending, scrap);
                }
                else if (vending.name == Data.PrefabVendingBuilding)
                {
                    SetupVendingMachine(vending, building);
                }
                else if (vending.name == Data.PrefabVendingExtra)
                {
                    SetupVendingMachine(vending, armor);
                }
                else if (vending.name == Data.PrefabVendingAttire)
                {
                    SetupVendingMachine(vending, attire);
                }
                else if (vending.name == Data.PrefabVendingWeapons)
                {
                    SetupVendingMachine(vending, weapons);
                }
            }

            foreach (var spawn in UnityEngine.Object.FindObjectsOfType<GameObject>().Where(e => e.name == Data.ArenaRespawn).ToList())
            {
                foreach (var arena in this.data.Arenas.Values)
                {
                    double distance = Utility.GetDistanceBetween(arena.Entity.transform.position, spawn.transform.position);

                    if (distance < 250.0f)
                    {
                        List<BaseEntity> list = new List<BaseEntity>();
                        Vis.Entities(spawn.transform.position, 1.0f, list);
                        foreach (var x in list)
                        {
                            x.Kill();
                        }

                        arena.Respawnpoint = spawn;
                        break;
                    }
                }
            }

            foreach (var spawn in UnityEngine.Object.FindObjectsOfType<GameObject>().Where(e => e.name == Data.ArenaTeamQuarry).ToList())
            {
                foreach (var arena in this.data.Arenas.Values)
                {
                    double distance = Utility.GetDistanceBetween(arena.Entity.transform.position, spawn.transform.position);

                    if (distance < 200.0f)
                    {
                        List<BaseEntity> list = new List<BaseEntity>();
                        Vis.Entities(spawn.transform.position, 1.0f, list);
                        foreach (var x in list)
                        {
                            x.Kill();
                        }

                        arena.Quarries.Add(spawn);
                        break;
                    }
                }
            }

            foreach (var spawn in UnityEngine.Object.FindObjectsOfType<GameObject>().Where(e => e.name == Data.ArenaCenterQuarry).ToList())
            {
                foreach (var arena in this.data.Arenas.Values)
                {
                    double distance = Utility.GetDistanceBetween(arena.Entity.transform.position, spawn.transform.position);

                    if (distance < 200.0f)
                    {
                        List<BaseEntity> list = new List<BaseEntity>();
                        Vis.Entities(spawn.transform.position, 1.0f, list);
                        foreach (var x in list)
                        {
                            x.Kill();
                        }

                        arena.Center = spawn;
                        break;
                    }
                }
            }

            foreach (var spawn in UnityEngine.Object.FindObjectsOfType<GameObject>().Where(e => e.name == Data.ArenaTeamSpawn).ToList())
            {
                foreach (var arena in this.data.Arenas.Values)
                {
                    double distance = Utility.GetDistanceBetween(arena.Entity.transform.position, spawn.transform.position);

                    if (distance < 200.0f)
                    {
                        foreach (var team in arena.Teams.Values)
                        {
                            if (team.Spawnpoint == null)
                            {
                                team.Spawnpoint = spawn;
                                break;
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void SetupVendingMachine(NPCVendingMachine vending, List<Data.VendingItem> items)
        {
            List<NPCVendingOrder.Entry> temp = new List<NPCVendingOrder.Entry>();
            foreach (var item in items)
            {
                temp.Add(new NPCVendingOrder.Entry
                {
                    currencyAmount = item.CurrnencyAmount,
                    currencyAsBP = false,
                    currencyItem = item.currency,
                    sellItem = item.item,
                    sellItemAmount = item.ItemAmount,
                    sellItemAsBP = false
                });
            }

            vending.vendingOrders.orders = temp.ToArray();
            vending.InstallFromVendingOrders();
        }
    }
}