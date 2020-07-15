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
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("Data", "eandersson", "0.3.2", ResourceId = 2681)]
    public class Data : RustPlugin
    {
        public SortedDictionary<string, ArenaData> Arenas = new SortedDictionary<string, ArenaData>();
        public Dictionary<MiningQuarry, ArenaData> CenterMiningQuarryToArena = new Dictionary<MiningQuarry, ArenaData>();
        public List<BasePlayer> DeadPlayers = new List<BasePlayer>();
        public List<Vector3> InitialStartingPositions = new List<Vector3>();
        public Dictionary<ulong, BaseEntity> PlayerSleepingBags = new Dictionary<ulong, BaseEntity>();

        private Dictionary<ulong, BasePlayer> PlayerCache = new Dictionary<ulong, BasePlayer>();
        private Dictionary<BasePlayer, PlayerData> PlayersData = new Dictionary<BasePlayer, PlayerData>();
        private Dictionary<BasePlayer, ArenaData> PlayerToArena = new Dictionary<BasePlayer, ArenaData>();
        private Dictionary<BasePlayer, Team> PlayerToTeam = new Dictionary<BasePlayer, Team>();

        #region Enums

        public enum Stages
        {
            Waiting = 0,
            Warmup = 1,
            Building = 2,
            Attacking = 3,
            GameOver = 4,
        }

        #endregion

        #region Prefabs and Assets

        public const string PrefabHighExternalStoneWall = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab";
        public const string PrefabRowboat = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        public const string PrefabQuarry = "assets/prefabs/deployable/quarry/mining_quarry.prefab";
        public const string PrefabQuarryStatic = "assets/bundled/prefabs/static/miningquarry_static.prefab";
        public const string PrefabVendingComponents = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_components.prefab";
        public const string PrefabVendingFarming = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_farming.prefab";
        public const string PrefabVendingBuilding = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_building.prefab";
        public const string PrefabVendingExtra = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_extra.prefab";
        public const string PrefabVendingAttire = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_attire.prefab";
        public const string PrefabVendingWeapons = "assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_weapons.prefab";
        public const string PrefabAutoSpawnHemp = "assets/bundled/prefabs/autospawn/collectable/hemp/hemp-collectable.prefab";
        public const string PrefabSpawnPoint = "assets/bundled/prefabs/modding/spawn_point.prefab";

        public const string AssetSpawnTempForest = "assets/content/properties/spawnpopulation/v2_temp_forest.asset";
        public const string AssetSpawnHemp = "assets/content/properties/spawnpopulation/collectable-resource-hemp.asset";
        public const string AssetSpawnOre = "assets/content/properties/spawnpopulation/ores.asset";

        public const string ArenaTeamQuarry = "assets/content/structures/concrete_slabs/concrete_slabs_9x9.prefab";  // Team Quarry.
        public const string ArenaTeamSpawn = "assets/content/structures/concrete_slabs/concrete_slabs_3x3.prefab";  // Team Spawn Point.
        public const string ArenaCenterQuarry = "assets/content/structures/concrete_slabs/concrete_slabs_9x9_impact.prefab";  // Central Quarry.
        public const string ArenaRespawn = "assets/content/structures/concrete_slabs/concrete_slabs_9x9_path.prefab";  // Arena (aka dead) Respawn Point.

        #endregion

        #region Readyonly & Const

        public readonly static int MinPlayerAllowed = 1;  // Minimum allowed players in an Arena before a match can begin.
        public readonly static int MaxPlayersPerTeam = RelationshipManager.maxTeamSize;  // Max allowed players per team.

        public readonly static int DeathCooldown = 30;  // Player respawn 30 seconds after dying.

        public readonly static int IncreaseScrapEvery = 15;  // Increase scrap every 15 minutes.
        public readonly static int MaxMultiplier = 20;  // Largest allowed Scrap modifier is 20.

        public const int GatherRate = 2;  // Ore, Tree gather multiplier.
        public const int PickupRate = 4;  // Cloth, Leather etc pickup multiplier.

        public readonly static Dictionary<string, float> CustomCraftableBuildTime = new Dictionary<string, float>
        {
            {"gunpowder.item", 0.5f},
            {"explosive.timed.item", 10.0f},
            {"explosives.item", 1.0f},
        };

        public readonly static List<string> DespawnItems = new List<string>
        {
            "cupboard.tool.deployed",
            "item_drop",
            "item_drop_backpack",
            "player_corpse",
            "sleepingbag_leather_deployed",
        };

        public readonly static List<string> StartItems = new List<string>
        {
            "pickaxe",
            "hatchet"
        };

        public readonly static Dictionary<string, float> ToolsOreDamage = new Dictionary<string, float>
        {
            {"pickaxe.entity", 150f},
            {"icepick_salvaged.entity", 200f},
            {"jackhammer.entity", 150f},
        };

        public readonly static Dictionary<string, float> ToolTreeDamage = new Dictionary<string, float>
        {
            {"hatchet.entity", 150f },
            {"axe_salvaged.entity", 200f },
            {"chainsaw.entity", 100f },
        };

        #endregion

        #region Data Structures (Classes)

        public class ArenaData
        {
            public ulong Id;
            public string MatchId = Guid.NewGuid().ToString();
            public string Name;

            public GameObject Entity;
            public List<GameObject> Quarries = new List<GameObject>();
            public List<GameObject> Spawnpoints = new List<GameObject>();
            public GameObject Respawnpoint;
            public GameObject Center;

            public List<ulong> ActivePlayers = new List<ulong>();

            public string StageEndTime;
            public string StageStartTime;
            public string ServerStartTime = DateTime.UtcNow.ToString();
            public int ScrapMultiplier = 1;

            public int CurrentStage = (int) Data.Stages.Waiting;
            public string CurrentStageName;
            public bool GameOver = false;
            public Team WinningTeam = null;

            public int TicketsPerPlayer = 16;
            public int MaxPlayersPerTeam = Data.MaxPlayersPerTeam;
            public Dictionary<int, Team> Teams = new Dictionary<int, Team>();

            public TimeSpan? GetTimeLeft()
            {
                if (this.CurrentStage == (int) Data.Stages.Waiting)
                    return Convert.ToDateTime(DateTime.UtcNow.AddMinutes(5f)).Subtract(DateTime.UtcNow);
                if (this.StageEndTime == null) return null;
                return Convert.ToDateTime(this.StageEndTime).Subtract(DateTime.UtcNow);
            }

            public TimeSpan? GetTimeAttacking()
            {
                if (this.CurrentStage != (int) Data.Stages.Attacking) return null;
                if (this.StageStartTime == null) return null;

                return DateTime.UtcNow.Subtract(Convert.ToDateTime(this.StageStartTime));
            }

            public void Reset()
            {
                this.ResetTeams();
                this.ScrapMultiplier = 1;
                this.WinningTeam = null;
                this.GameOver = false;
                this.WinningTeam = null;
                this.ActivePlayers.Clear();
            }

            public void ResetTeams()
            {
                foreach (Team team in this.Teams.Values)
                {
                    team.Reset();
                }
            }

            public int TotalPlayers()
            {
                int playerCount = 0;
                foreach (Team team in this.Teams.Values)
                {
                    playerCount += team.Players.Count;
                }
                return playerCount;
            }

            public List<ulong> GetPlayers()
            {
                List<ulong> temp = new List<ulong>();
                foreach (Team team in this.Teams.Values)
                {
                    foreach (ulong player in team.Players)
                    {
                        temp.Add(player);
                    }
                }
                return temp;
            }

            public List<BaseEntity> GetSleepingBags()
            {
                List<BaseEntity> temp = new List<BaseEntity>();
                foreach (Team team in this.Teams.Values)
                {
                    foreach (BaseEntity entity in team.PlayerSleepingBags.Values)
                    {
                        temp.Add(entity);
                    }
                }
                return temp;
            }

            public ArenaData(ulong id, GameObject entity, string name)
            {
                this.Name = name;
                this.Id = id;
                this.Entity = entity;
                this.Teams.Add(0, new Team(0, "Red", "0.8 0 0 1", this));
                this.Teams.Add(1, new Team(1, "Blue", "0 0 1 1", this));
                this.Teams.Add(2, new Team(2, "Green", "0 0.8 0 1", this));
                this.Teams.Add(3, new Team(3, "Yellow", "0.8 0.8 0 1", this));
            }
        }

        public class PlayerData
        {
            public ulong userID;
            public string displayName;
            public BasePlayer Instance;
            public int DeathTimer = 0;
            public string DeathTime;

            public TimeSpan? GetRespawnTimeLeft()
            {
                if (this.DeathTime == null) return null;
                return Convert.ToDateTime(this.DeathTime).Subtract(DateTime.UtcNow);
            }

            public PlayerData(BasePlayer player)
            {
                this.userID = player.userID;
                this.displayName = player.displayName;
                this.Instance = player;
            }
        }

        public class Team
        {
            public int Id;
            public string Name;
            public List<ulong> Players = new List<ulong>();
            public Dictionary<ulong, BaseEntity> PlayerSleepingBags = new Dictionary<ulong, BaseEntity>();
            public string TeamCupboard;
            public string TeamCupboardMarker;
            public string Color;
            public bool IsOut = false;
            public int TicketsRemaning;
            public GameObject Spawnpoint;

            public ArenaData Arena;

            public bool HasPlayers()
            {
                return this.Players.Count > 0;
            }

            public void Reset()
            {
                this.TicketsRemaning = this.Arena.MaxPlayersPerTeam * this.Arena.TicketsPerPlayer;
                this.Players.Clear();
                this.PlayerSleepingBags.Clear();
            }

            public Team(int id, string name, string color, ArenaData arena)
            {
                this.Id = id;
                this.Name = name;
                this.Color = color;
                this.Arena = arena;
            }
        }

        public class VendingItem
        {
            public int CurrnencyAmount;
            public string CurrnencyName;
            public string ItemName;
            public int ItemAmount;

            public ItemDefinition item;
            public ItemDefinition currency;

            public VendingItem(string itemName, int itemAmount, string currnencyName, int currencyAmount)
            {
                this.CurrnencyAmount = currencyAmount;
                this.ItemName = itemName;
                this.ItemAmount = itemAmount;
                this.CurrnencyName = currnencyName;

                this.item = ItemManager.FindItemDefinition(this.ItemName);
                this.currency = ItemManager.FindItemDefinition(this.CurrnencyName);
            }
        }

        #endregion

        #region Data Functions

        public void AddPlayerToCache(BasePlayer player)
        {
            if (!this.PlayerCache.ContainsKey(player.userID))
                this.PlayerCache[player.userID] = player;
        }

        public void AddPlayerToData(BasePlayer player)
        {
            if (!this.PlayersData.ContainsKey(player))
                this.PlayersData[player] = new PlayerData(player);
        }

        public void ClearUserCache(BasePlayer player)
        {
            ClearUserCacheForArena(player);
            if (this.PlayerCache.ContainsKey(player.userID)) this.PlayerCache.Remove(player.userID);
        }

        public void ClearUserCacheForArena(BasePlayer player)
        {
            if (this.PlayerToArena.ContainsKey(player)) this.PlayerToArena.Remove(player);
            if (this.PlayerToTeam.ContainsKey(player)) this.PlayerToTeam.Remove(player);
        }

        public void ClearUserData(BasePlayer player)
        {
            if (this.PlayersData.ContainsKey(player)) this.PlayersData.Remove(player);
        }

        public ArenaData GetArena(string name)
        {
            var arenas = Arenas.Where(a => a.Key.ToLower() == name.ToLower());
            return arenas.Any() ? arenas.First().Value : null;
        }

        public Team GetCupboardOwner(string cupboard)
        {
            foreach (var arena in Arenas.Values)
            {
                var matches = arena.Teams.Values.Where(t => t.TeamCupboard != null && t.TeamCupboard == cupboard);
                if (matches.Any())
                {
                    return matches.First();
                }
            }
            return null;
        }

        public BasePlayer GetPlayer(ulong playerId)
        {
            if (!PlayerCache.ContainsKey(playerId))
            {
                BasePlayer player = BasePlayer.FindByID(playerId);
                if (player == null) return null;
                this.PlayerCache[playerId] = player;
            }
            return PlayerCache[playerId];
        }

        public ArenaData GetPlayerArena(BasePlayer player)
        {
            if (PlayerToArena.ContainsKey(player)) return PlayerToArena[player];

            foreach (var arena in this.Arenas.Values)
            {
                var matches = arena.Teams.Where(t => t.Value.Players.Any(p => p == player.userID));
                if (matches.Any())
                {
                    this.PlayerToArena[player] = arena;
                    return arena;
                }
            }

            return null;
        }

        public PlayerData GetPlayerData(BasePlayer player)
        {
            if (player == null) return null;

            if (PlayersData.ContainsKey(player)) return PlayersData[player];

            this.PlayersData[player] = new PlayerData(player);
            return this.PlayersData[player];
        }

        public PlayerData GetPlayerData(ulong playerId)
        {
            return this.GetPlayerData(GetPlayer(playerId));
        }

        public string GetPlayerName(ulong playerId)
        {
            BasePlayer player = GetPlayer(playerId);
            if (player == null) return "unknown";
            return player.displayName;
        }

        public Team GetPlayerTeam(BasePlayer player)
        {
            if (PlayerToTeam.ContainsKey(player)) return PlayerToTeam[player];

            ArenaData arena = GetPlayerArena(player);
            if (arena == null) return null;

            var matches = arena.Teams.Where(t => t.Value.Players.Any(p => p == player.userID));
            if (matches.Any())
            {
                Team team = matches.First().Value;
                this.PlayerToTeam[player] = team;
                return team;
            }

            return null;
        }

        public Vector3 GetSpawnPoint(Team team, BasePlayer player, bool isDead = false)
        {
            Vector3 position;
            if (team == null || team.Arena.Respawnpoint == null)
            {
                var randomPosition = UnityEngine.Random.Range(0, InitialStartingPositions.Count);
                position = InitialStartingPositions[randomPosition];
                position = Utility.RandomizePosition(position);
                position = Utility.GetGroundPosition(position);
            }
            else if (isDead == false)
            {
                if (team.PlayerSleepingBags.ContainsKey(player.userID))
                {
                    var sleepingbag = team.PlayerSleepingBags[player.userID];
                    if (sleepingbag == null)
                    {
                        team.PlayerSleepingBags.Remove(player.userID);
                        return this.GetSpawnPoint(team, player);
                    }
                    position = team.PlayerSleepingBags[player.userID].transform.position;
                }
                else
                {
                    if (team.Spawnpoint == null) Puts(string.Format("{0} missing spawnpoints!", team.Name));
                    position = team.Spawnpoint.transform.position;
                    position = Utility.RandomizePosition(position);
                    position = Utility.GetGroundPosition(position);
                }
            }
            else
            {
                position = team.Arena.Respawnpoint.transform.position;
                position = Utility.RandomizePosition(position, 20f);
                position = Utility.GetGroundPosition(position);
            }

            return position;
        }

        public Team GetTeam(ArenaData arena, int teamId)
        {
            return arena.Teams[teamId];
        }

        public Team GetTeam(ArenaData arena, string name)
        {
            var teams = arena.Teams.Where(t => t.Value.Name.ToLower() == name.ToLower());
            return teams.Any() ? teams.First().Value : null;
        }

        public List<Team> GetTeamsWithCupboards(ArenaData arena)
        {
            return arena.Teams.Values.Where(t => t.TeamCupboard != null).ToList();
        }

        public void PopulatePlayerList()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                this.AddPlayerToCache(player);
                this.AddPlayerToData(player);
            }
        }

        #endregion
    }
}