// Requires: Data
// Requires: Setup
// Requires: Startup
// Requires: Stages
// Requires: Team
// Requires: Ui
// Requires: Utility
// Requires: Walls
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
    [Info("Arena", "eandersson", "0.3.2", ResourceId = 2680)]
    public class Arena : RustPlugin
    {
        public bool Initialized = false;

        public Data data = new Data();
        public Setup setup = new Setup();
        public Stages stages = new Stages();
        public Team team = new Team();
        public Ui ui = new Ui();
        public Walls walls = new Walls();

        #region Initialize

        private void Init()
        {
            Puts("Init");
            setup.Configure(this);
            stages.Configure(this);
            team.Configure(this);
            ui.Configure(this);
        }

        private void Loaded()
        {
            Puts("Loaded");
            this.data.PopulatePlayerList();

            NextTick(() =>
            {
                Puts("Starting timers...");

                this.StartStageUiTimer();
                this.StartTeamUiTimer();
                this.StartRespawnTimer();
                this.StartStageTimer();
            });
        }

        private void Unload()
        {
            Puts("Unload called...");
            foreach (var arena in this.data.Arenas.Values)
            {
                this.stages.ClearArena(arena);
            }
        }

        private void OnServerInitialized()
        {
            Utility.SetServerDefaults();

            var instance = SingletonComponent<SpawnHandler>.Instance;
            foreach (SpawnPopulation spawn in instance.SpawnPopulations.ToList())
            {
                if (spawn.LookupFileName() == Data.AssetSpawnOre)
                {
                    if (spawn.Prefabs == null) continue;
                    foreach (var prefab in spawn.Prefabs)
                    {
                        var parameters = prefab.Parameters;
                        if (parameters != null)
                        {
                            parameters.Count = 1;
                        }
                    }
                }
            }

            Utility.SetBuildTimes();
            Utility.SetStackSizes();
            Utility.DisableTimeOfDay();

            this.walls.RemoveAll();
            this.setup.InitializeArenas();

            NextTick(() =>
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    player.ClearTeam();

                    var randomPosition = UnityEngine.Random.Range(0, this.data.InitialStartingPositions.Count);
                    Vector3 position = this.data.InitialStartingPositions[randomPosition];
                    position = Utility.RandomizePosition(position);
                    position = Utility.GetGroundPosition(position);

                    NextTick(() => {
                        Utility.Teleport(player, position);
                        Utility.ClearInventory(player);
                        Utility.SetMaxHealth(player);
                    });
                }

                foreach (Data.ArenaData arena in this.data.Arenas.Values)
                {
                    this.stages.StartWaitingStage(arena);
                    this.stages.ResetArena(arena);
                }

                this.Initialized = true;
            });
        }

        #endregion

        #region Functions

        private void ResetArenas()
        {
            foreach (Data.ArenaData arena in this.data.Arenas.Values)
            {
                this.stages.StartWaitingStage(arena);
            }
        }

        public void RespawnDeadPlayers()
        {
            var currentTime = DateTime.UtcNow;

            // TODO(eandersson): Respawn individually based on death timer.
            foreach (BasePlayer player in this.data.DeadPlayers.ToList())
            {
                if (player == null) continue;
                try
                {
                    if (!player.IsConnected) continue;

                    Data.Team team = this.data.GetPlayerTeam(player);
                    if (team == null) continue;
                    if (team.Arena == null) continue;
                    if (team.Arena.CurrentStage <= (int) Data.Stages.Warmup) continue;

                    Data.PlayerData playerData = this.data.GetPlayerData(player);

                    if (playerData.DeathTime == null) continue;
                    if (Convert.ToDateTime(playerData.DeathTime) > currentTime) continue;

                    Vector3 position = this.data.GetSpawnPoint(team, player);
                    if (position == null) continue;

                    if (team != null)
                    {
                        NextTick(() => {
                            Utility.Teleport(player, position);
                            Utility.GiveStartingItems(player);
                        });
                        this.data.DeadPlayers.Remove(player);
                    }
                }
                catch (Exception e)
                {
                    Puts(e.ToString());
                }
            }
        }

        public void TeamLost(Data.Team team)
        {
            if (team == null) return;

            team.IsOut = true;
            team.TicketsRemaning = 0;

            // RemoveTeamCupboard(team);
            // DestroyMarker(team);

            foreach (ulong playerId in team.Players)
            {
                BasePlayer player = BasePlayer.FindByID(playerId);
                if (player == null) continue;
                player.Hurt(1000);
            }

            covalence.Server.Broadcast(string.Format("Team {0} is out!", team.Name));

            int remaning = this.data.GetTeamsWithCupboards(team.Arena).Count();
            if (remaning <= 1)
            {
                Data.Team winningTeam = this.data.GetTeamsWithCupboards(team.Arena).FirstOrDefault();
                if (winningTeam != null)
                {
                    covalence.Server.Broadcast(string.Format("Team {0} Won!", winningTeam.Name));
                    team.Arena.WinningTeam = winningTeam;
                }

                this.stages.StartGameOverStage(team.Arena);
            }
        }

        #endregion

        #region Gathering

        private void OnQuarryToggled(MiningQuarry quarry)
        {
            if (quarry.IsEngineOn())
                return;

            var fuel = ItemManager.CreateByName("lowgradefuel", 1);
            fuel.MoveToContainer(quarry.fuelStoragePrefab.instance.GetComponent<StorageContainer>().inventory);
            quarry.EngineSwitch(true);
        }

        private void OnQuarryGather(MiningQuarry quarry, Item item)
        {
            item.amount = 0;
            item.Remove(0f);

            int scrapAmount = 10;

            if (this.data.CenterMiningQuarryToArena.ContainsKey(quarry))
            {
                Data.ArenaData arena = this.data.CenterMiningQuarryToArena[quarry];
                scrapAmount *= arena.ScrapMultiplier;
            }

            var gather = ItemManager.CreateByName("scrap", scrapAmount);
            gather.MoveToContainer(quarry.hopperPrefab.instance.GetComponent<StorageContainer>().inventory); ;
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (entity == null) return;
            if (!entity.ToPlayer()) return;

            if (item.info.name == "hq_metal_ore.item")
            {
                item.amount = (int)(item.amount * Data.GatherRate * 4);
            }
            else
            {
                item.amount = (int)(item.amount * Data.GatherRate);
            }
        }

        private void OnDispenserBonus(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            OnDispenserGather(dispenser, entity, item);
        }

        private void OnCollectiblePickup(Item item, BasePlayer player, CollectibleEntity entity)
        {
            item.amount = (int)(item.amount * Data.PickupRate);
        }

        #endregion

        #region Hooks

        private object CanUseLockedEntity(BasePlayer player, BaseLock door)
        {
            if (door.HasLockPermission(player))
            {
                return true;
            }

            var currentTeam = this.data.GetPlayerTeam(player);
            if (currentTeam != null)
                foreach (ulong playerId in currentTeam.Players)
                {
                    if (door.HasLockPermission(BasePlayer.FindByID(playerId)))
                    {
                        return true;
                    }
                }

            return null;
        }

        private object OnBuyVendingItem(VendingMachine machine, BasePlayer player, int sellOrderID, int amount)
        {
            machine.ClientRPC<int>(null, "CLIENT_StartVendingSounds", sellOrderID);
            machine.DoTransaction(player, sellOrderID, amount);
            return false;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!(entity is BasePlayer)) return;
            BasePlayer player = entity as BasePlayer;

            Data.Team playerTeam = this.data.GetPlayerTeam(player);
            if (playerTeam == null) return;
            if (playerTeam.Arena.GameOver == true) return;
            if (playerTeam.Arena.CurrentStage != (int) Data.Stages.Attacking) return;

            if (playerTeam.IsOut == true) return;

            playerTeam.TicketsRemaning -= 1;
            if (playerTeam.TicketsRemaning <= 0)
            {
                this.TeamLost(playerTeam);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity.ShortPrefabName == "sleepingbag_leather_deployed")
            {
                var e = entity as BaseEntity;

                BasePlayer player = BasePlayer.FindByID(e.OwnerID);
                if (player == null) return;

                var team = this.data.GetPlayerTeam(player);
                if (team.PlayerSleepingBags.ContainsKey(e.OwnerID))
                {
                    team.PlayerSleepingBags.Remove(e.OwnerID);
                }
                if (this.data.PlayerSleepingBags.ContainsKey(e.OwnerID))
                {
                    this.data.PlayerSleepingBags.Remove(e.OwnerID);
                }
            }
            else if (entity.ShortPrefabName == "cupboard.tool.deployed")
            {
                Data.Team ownerTeam = this.data.GetCupboardOwner(entity.ToString());
                if (ownerTeam == null) return;

                ownerTeam.TeamCupboard = null;
                //DestroyMarker(ownerTeam);

                if (ownerTeam.IsOut == true) return;

                if (ownerTeam.Arena.CurrentStage == (int) Data.Stages.Attacking)
                {
                    this.TeamLost(ownerTeam);
                }
            }
            else if (entity.PrefabName == Data.PrefabQuarryStatic)
            {
                MiningQuarry quarry = entity as MiningQuarry;
                if (this.data.CenterMiningQuarryToArena.ContainsKey(quarry)) this.data.CenterMiningQuarryToArena.Remove(quarry);
            }
        }

        private void OnEntitySpawned(BaseEntity entity, UnityEngine.GameObject gameObject)
        {
            if (Data.ToolsOreDamage.ContainsKey(entity.ShortPrefabName))
            {
                BaseMelee pick = entity as BaseMelee;
                pick.gathering.Ore.gatherDamage = Data.ToolsOreDamage[entity.ShortPrefabName];
            }
            else if (Data.ToolTreeDamage.ContainsKey(entity.ShortPrefabName))
            {
                BaseMelee pick = entity as BaseMelee;
                pick.gathering.Tree.gatherDamage = Data.ToolTreeDamage[entity.ShortPrefabName];
            }
            else if (entity.ShortPrefabName == "sleepingbag_leather_deployed")
            {
                BasePlayer player = BasePlayer.FindByID(entity.OwnerID);
                if (player == null) return;
                Data.Team team = this.data.GetPlayerTeam(player);
                if (team == null)
                {
                    entity.KillMessage();
                    SendReply(player, "Not allowed to place a sleeping bag unless you are on a team.");
                    return;
                }
                if (team.PlayerSleepingBags.ContainsKey(entity.OwnerID))
                {
                    entity.KillMessage();
                    SendReply(player, "Not allowed to place more than one sleeping bag.");
                }
                else
                {
                    team.PlayerSleepingBags.Add(entity.OwnerID, entity);
                }
            }
            else if (entity.ShortPrefabName == "bed_deployed")
            {
                BasePlayer player = BasePlayer.FindByID(entity.OwnerID);
                if (player == null) return;
                entity.KillMessage();
                SendReply(player, "Not allowed to place a bed.");
            }
            else if (entity.ShortPrefabName == "cupboard.tool.deployed")
            {
                BasePlayer player = BasePlayer.FindByID(entity.OwnerID);
                if (player == null) return;
                Data.Team playerTeam = this.data.GetPlayerTeam(player);

                if (player == null) return;

                if (playerTeam.Arena.CurrentStage >= (int)Data.Stages.Attacking)
                {
                    entity.KillMessage();
                    SendReply(player, "No longer allowed to place new Cupboards.");
                    return;
                }

                if (playerTeam == null)
                {
                    entity.KillMessage();
                    SendReply(player, "Need to be on a team to place a cupboard!");
                }
                else
                {
                    if (playerTeam.TeamCupboard == null)
                    {
                        playerTeam.TeamCupboard = entity.ToString();
                        SendReply(player, "You have successfully placed your teams cupboard!");
                        //CreateMarker(team, entity);
                    }
                    else
                    {
                        entity.KillMessage();
                        SendReply(player, "Your team already has a cupboard!");
                    }
                }
            }
            else if (entity is CollectibleEntity)
            {
                if (entity.PrefabName == Data.PrefabAutoSpawnHemp)
                {
                    var hemp = entity as CollectibleEntity;
                    hemp.itemList = new ItemAmount[2];
                    hemp.itemList[0] = new ItemAmount(ItemManager.FindItemDefinition("cloth"), 10f);
                    hemp.itemList[1] = new ItemAmount(ItemManager.FindItemDefinition("leather"), 5f);
                }
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitinfo)
        {
            if (entity == null || hitinfo == null || entity.OwnerID == 0 ||
                entity.OwnerID == hitinfo.InitiatorPlayer?.userID) return null;
            if (entity?.OwnerID > 0 && entity?.OwnerID <= 26)
            {
                BasePlayer attacker = hitinfo.InitiatorPlayer;
                if (attacker == null) return null;
                var arena = this.data.GetPlayerArena(attacker);
                if (arena.CurrentStage == 2) return null;

                hitinfo.damageTypes = new DamageTypeList();
                hitinfo.DoHitEffects = false;
                hitinfo.HitMaterial = 0;
                hitinfo.damageTypes.ScaleAll(0);
                return true;
            }

            return null;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            this.data.AddPlayerToCache(player);
            this.data.AddPlayerToData(player);
            Utility.UnlockAllBlueprints(player);
            NextTick(() =>
            {
                if (player.IsConnected)
                {
                    player.Respawn();
                    player.EndSleeping();
                }
            });
        }

        private void OnPlayerCorpse(BasePlayer player, PlayerCorpse corpse)
        {
            if (player == null) return;

            NextTick(() =>
            {
                if (player.IsConnected)
                {
                    player.Respawn();
                    player.EndSleeping();
                }
            });
            return;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player.IsConnected) return;

            player.Kill();

            try
            {
                if (this.data.PlayerSleepingBags.ContainsKey(player.userID))
                {
                    var entity = this.data.PlayerSleepingBags[player.userID];
                    if (entity != null)
                    {
                        entity.KillMessage();
                    }
                }
            }
            catch (Exception e)
            {
                Puts(e.ToString());
            }

            var arena = this.data.GetPlayerArena(player);
            if (arena != null)
            {
                this.team.LeaveTeam(player, arena);
            }
            else
            {
                this.data.ClearUserCache(player);
            }
        }

        private object OnPlayerRespawn(BasePlayer player)
        {
            if (player == null) return null;

            Data.Team team = this.data.GetPlayerTeam(player);
            Vector3 position = this.data.GetSpawnPoint(team, player, true);

            if (team != null && !this.data.DeadPlayers.Contains(player))
            {
                Data.ArenaData arena = team.Arena;
                if (arena != null && arena.CurrentStage >= (int) Data.Stages.Building && arena.GameOver == false)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime rs = now.AddSeconds(Data.DeathCooldown);

                    Data.PlayerData playerData = this.data.GetPlayerData(player);
                    playerData.DeathTime = rs.ToString();
                    this.data.DeadPlayers.Add(player);
                }
            }

            NextTick(() =>
            {
                Utility.ClearInventory(player);
                player.health = 100;
                player.metabolism.calories.Increase(500);
                player.metabolism.hydration.Increase(250);
            });

            return new BasePlayer.SpawnPoint()
            { pos = Utility.GetGroundPosition((Vector3)position), rot = new Quaternion(0, 0, 0, 1) };
        }

        #endregion

        #region Timers

        public void StartStageUiTimer()
        {
            timer.Every(1.0f, () =>
            {
                if (this.Initialized == false) return;

                try
                {
                    foreach (BasePlayer player in BasePlayer.activePlayerList)
                    {
                        this.ui.UpdateStageUi(player);
                    }
                }
                catch (Exception e)
                {
                    Puts(e.ToString());
                }
            });
        }

        public void StartStageTimer()
        {
            timer.Every(5.0f, () =>
            {
                if (this.Initialized == false) return;

                foreach (Data.ArenaData arena in this.data.Arenas.Values)
                {
                    try
                    {
                        if (arena.StageEndTime == null)
                        {
                            if (arena.CurrentStage == (int) Data.Stages.Waiting)
                            {
                                if (arena.TotalPlayers() >= Data.MinPlayerAllowed)
                                {
                                    this.stages.StartWarmupStage(arena);
                                }
                            }
                            else if (arena.CurrentStage == (int) Data.Stages.Attacking)
                            {
                                if (arena.TotalPlayers() == 0)
                                {
                                    this.stages.StartWarmupStage(arena);
                                    this.stages.ResetArena(arena);
                                }
                                else
                                {
                                    TimeSpan progress = DateTime.UtcNow.Subtract(Convert.ToDateTime(arena.StageStartTime));
                                    int multiplier = Convert.ToInt32(Math.Min(Math.Floor(progress.TotalMinutes / Data.IncreaseScrapEvery), Data.MaxMultiplier));
                                    if (multiplier > 1 && arena.ScrapMultiplier != multiplier)
                                    {
                                        arena.ScrapMultiplier = multiplier;
                                        covalence.Server.Broadcast(string.Format("[{0}] Scrap rate increased.", arena.Name.ToUpper()));
                                    }
                                }
                            }
                            continue;
                        }

                        if (DateTime.UtcNow >= Convert.ToDateTime(arena.StageEndTime))
                        {
                            arena.CurrentStage += 1;
                            if (arena.CurrentStage == (int) Data.Stages.Building) this.stages.StartBuildingStage(arena);
                            if (arena.CurrentStage == (int) Data.Stages.Attacking) this.stages.StartAttackingStage(arena);
                            if (arena.CurrentStage == (int) Data.Stages.GameOver)
                            {
                                this.stages.StartWaitingStage(arena);
                                this.stages.ResetArena(arena);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Puts(e.ToString());
                    }
                }
            });
        }

        public void StartTeamUiTimer()
        {
            timer.Every(2.50f, () =>
            {
                if (this.Initialized == false) return;

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    try
                    {
                        var arena = this.data.GetPlayerArena(player);

                        if (arena == null)
                        {
                            this.ui.ShowArenaUi(player);
                        }
                        else if (arena.CurrentStage <= (int) Data.Stages.Warmup)
                        {
                            this.ui.ShowTeamUi(arena, player);
                        }
                        else
                        {
                            this.ui.DestroyUi(player, Ui.ArenaUi);
                            this.ui.DestroyUi(player, Ui.TeamUi);
                        }
                    }
                    catch (Exception e)
                    {
                        Puts(e.ToString());
                    }
                }
            });
        }

        public void StartRespawnTimer()
        {
            timer.Every(1.0f, () =>
            {
                if (this.Initialized == false) return;

                try
                {
                    RespawnDeadPlayers();
                }
                catch (Exception e)
                {
                    Puts(e.ToString());
                }
            });
        }

        #endregion

        #region Commands

        private bool IsAdmin(BasePlayer player)
        {
            return player.IsAdmin;
        }

        [ConsoleCommand("rustarena.join")]
        private void JoinArenaConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args.Length != 2) return;

            string arenaName = arg.Args[0].ToLower();
            string teamName = arg.Args[1].ToLower();

            Puts(string.Format("[{0}] rustarena.join {1} {2}", player.displayName, arenaName, teamName));

            Data.ArenaData arena = this.data.GetArena(arenaName);
            if (arena == null) return;

            Data.Team team = this.data.GetTeam(arena, teamName);
            if (team == null) return;

            if (team.Players.Count() + 1 > arena.MaxPlayersPerTeam)
            {
                SendReply(player, string.Format("Too many players already on team {0}", team.Name));
                return;
            }

            this.team.JoinTeam(player, arena, team);
            this.ui.DestroyUi(player, Ui.ArenaUi);
            if (arena.CurrentStage > (int)Data.Stages.Warmup) player.Hurt(1000);
            else
            {
                // Only respawn player if outside of the start zone.
                if (Utility.GetDistanceBetween(arena.Respawnpoint.transform.position, player.transform.position) < 25f) return;

                NextTick(() => {
                    Utility.Teleport(player, this.data.GetSpawnPoint(team, player, true));
                    Utility.ClearInventory(player);
                    Utility.SetMaxHealth(player);
                });
            }
        }

        [ConsoleCommand("rustarena.leave")]
        private void LeaveArenaConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            Data.ArenaData arena = this.data.GetPlayerArena(player);
            if (arena == null) return;

            this.team.LeaveTeam(player, arena);
            this.ui.DestroyUi(player, Ui.TeamUi);
        }

        [ChatCommand("leave")]
        private void LeaveArenaCmd(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            Data.ArenaData arena = this.data.GetPlayerArena(player);
            if (arena == null) return;

            this.team.LeaveTeam(player, arena);
            this.ui.DestroyUi(player, Ui.TeamUi);
        }

        [ChatCommand("reset")]
        private void ResetArenaCmd(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsAdmin(player)) return;
            if (args.Length != 1) return;

            string arenaName = args[0].ToLower();

            Data.ArenaData arena = this.data.GetArena(arenaName);
            if (arena == null) return;

            this.stages.StartWaitingStage(arena);
        }

        [ChatCommand("start")]
        private void StartArenaCmd(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsAdmin(player)) return;
            if (args.Length != 1) return;

            string arenaName = args[0].ToLower();

            Data.ArenaData arena = this.data.GetArena(arenaName);
            if (arena == null) return;

            this.stages.StartBuildingStage(arena);
        }

        [ChatCommand("raid")]
        private void RaidArenaCmd(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsAdmin(player)) return;
            if (args.Length != 1) return;

            string arenaName = args[0].ToLower();

            Data.ArenaData arena = this.data.GetArena(arenaName);
            if (arena == null) return;

            this.stages.StartAttackingStage(arena);
        }

        [ChatCommand("clean")]
        private void CleanArenaCmd(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsAdmin(player)) return;
            if (args.Length != 1) return;

            string arenaName = args[0].ToLower();

            Data.ArenaData arena = this.data.GetArena(arenaName);
            if (arena == null) return;

            this.stages.ClearArena(arena);
        }

        #endregion
    }
}