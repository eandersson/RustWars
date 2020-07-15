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
    [Info("Stages", "eandersson", "0.3.2", ResourceId = 2683)]
    public class Stages : RustPlugin
    {
        private Arena arena = null;
        private Data data = null;
        private Setup setup = null;
        private Team team = null;
        private Ui ui = null;
        private Walls walls = null;

        public void Configure(Arena arena)
        {
            this.arena = arena;
            this.data = arena.data;
            this.walls = arena.walls;
            this.team = arena.team;
            this.ui = arena.ui;
            this.setup = arena.setup;
        }

        public void ClearArena(Data.ArenaData arena)
        {
            this.walls.Remove(arena.Id);

            int itemsKilled = 0;

            foreach (var entity in arena.GetSleepingBags())
            {
                if (entity.IsDestroyed == false)
                {
                    entity.Kill();
                    itemsKilled += 1;
                }
            }

            if (arena.Center == null) return;

            List<BaseEntity> list = new List<BaseEntity>();
            Vis.Entities(arena.Center.transform.position, 200.0f, list);
            foreach (BaseEntity entity in list)
            {
                if (entity == null) continue;
                if (entity is NPCVendingMachine) continue;

                if (entity is BuildingBlock || entity is DroppedItem ||
                    entity.PrefabName == Data.PrefabQuarry ||
                    entity.PrefabName == Data.PrefabQuarryStatic ||
                    Data.DespawnItems.Contains(entity.ShortPrefabName) ||
                    arena.ActivePlayers.Contains(entity.OwnerID))
                {
                    if (entity.IsDestroyed == false)
                    {
                        entity.Kill();
                        itemsKilled += 1;
                    }
                }
            }

            Puts(string.Format("Killed {0} entities from Arena {1}", itemsKilled, arena.Name.ToUpper()));
        }

        public void StartWaitingStage(Data.ArenaData arena)
        {
            this.walls.Remove(arena.Id);

            arena.CurrentStage = (int) Data.Stages.Waiting;
            arena.ScrapMultiplier = 1;
            arena.StageEndTime = null;
            arena.StageStartTime = DateTime.UtcNow.ToString();
            arena.CurrentStageName = "Waiting";

            this.ClearArena(arena);
        }

        public void StartWarmupStage(Data.ArenaData arena)
        {
            DateTime now = DateTime.UtcNow;
            DateTime rs = now.AddMinutes(5f);
            arena.CurrentStage = (int) Data.Stages.Warmup;
            arena.StageEndTime = rs.ToString();
            arena.StageStartTime = DateTime.UtcNow.ToString();
            arena.CurrentStageName = "Warmup";
        }

        public void StartBuildingStage(Data.ArenaData arena)
        {
            int playerCount = arena.TotalPlayers();
            if (playerCount < Data.MinPlayerAllowed)
            {
                this.StartWarmupStage(arena);
                return;
            }

            DateTime now = DateTime.UtcNow;
            DateTime rs = now.AddMinutes(20f);

            this.setup.StartArena(arena.Name);

            arena.CurrentStage = (int) Data.Stages.Building;
            arena.StageEndTime = rs.ToString();
            arena.StageStartTime = DateTime.UtcNow.ToString();
            arena.CurrentStageName = "Building";

            timer.Once(3.0f, () => {
                NextTick(() =>
                {
                    foreach (BasePlayer player in BasePlayer.activePlayerList)
                    {
                        Data.Team team = this.data.GetPlayerTeam(player);
                        if (team == null) continue;
                        if (arena != team.Arena) continue;

                        Utility.Teleport(player, this.data.GetSpawnPoint(team, player));

                        NextTick(() => { Utility.GiveStartingItems(player); });
                    }
                });
            });

            covalence.Server.Broadcast(string.Format("[{0}] Building phase has started!", arena.Name.ToUpper()));
        }

        public void StartAttackingStage(Data.ArenaData arena)
        {
            int remaning = this.data.GetTeamsWithCupboards(arena).Count();
            if (remaning <= 1)
            {
                covalence.Server.Broadcast("No TCs built! Ending game.");
                this.StartGameOverStage(arena);
                return;
            }

            DateTime now = DateTime.UtcNow;
            arena.CurrentStage = (int) Data.Stages.Attacking;
            arena.CurrentStageName = "Attacking";
            arena.StageEndTime = null;
            arena.StageStartTime = DateTime.UtcNow.ToString();

            this.walls.Remove(arena.Id);

            covalence.Server.Broadcast(string.Format("[{0}] Attacking phase has started!", arena.Name.ToUpper()));
        }

        public void StartGameOverStage(Data.ArenaData arena)
        {
            DateTime now = DateTime.UtcNow;
            DateTime rs = now.AddMinutes(3f);

            arena.CurrentStage = (int) Data.Stages.GameOver;
            arena.CurrentStageName = "GameOver";
            arena.StageEndTime = rs.ToString();
            arena.StageStartTime = DateTime.UtcNow.ToString();
            arena.GameOver = true;
        }

        public void ResetArena(Data.ArenaData arena)
        {
            if (arena == null) return;

            foreach (ulong player in arena.GetPlayers())
            {
                this.team.LeaveTeam(this.data.GetPlayer(player), arena);
            }

            arena.Reset();
        }
    }
}