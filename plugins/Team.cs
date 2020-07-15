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
    [Info("Team", "eandersson", "0.3.2", ResourceId = 2685)]
    public class Team : RustPlugin
    {
        private Arena arena = null;
        private Data data = null;
        private Walls walls = null;

        private Dictionary<int, RelationshipManager.PlayerTeam> parties = new Dictionary<int, RelationshipManager.PlayerTeam>();

        public void Configure(Arena arena)
        {
            this.arena = arena;
            this.data = arena.data;
            this.walls = arena.walls;
        }

        private void Unload()
        {
            Puts("Unload called...");

            foreach (var team in RelationshipManager.Instance.teams.Values.ToList())
            {
                RelationshipManager.Instance.DisbandTeam(team);
            }
        }

        public void JoinTeam(BasePlayer player, Data.ArenaData arena, Data.Team newTeam)
        {
            if (player == null) return;
            if (newTeam == null) return;
            if (arena == null) return;
            if (newTeam.Players.Count() + 1 > arena.MaxPlayersPerTeam) return;
            if (!arena.ActivePlayers.Contains(player.userID)) arena.ActivePlayers.Add(player.userID);

            LeaveTeam(player, arena);

            NextTick(() =>
            {
                var t = CreateOrValidateTeam(newTeam, player);
                if (t != null)
                {
                    if (!newTeam.Players.Contains(player.userID))
                    {
                        newTeam.Players.Add(player.userID);
                    }
                    covalence.Server.Broadcast(string.Format("{0}: {1} joined team {2} 1.", arena.Name.ToUpper(), player.displayName, newTeam.Name));
                    return;
                }

                if (!parties.ContainsKey(newTeam.Id))
                {
                    Puts("No such party!");
                }

                if (!newTeam.Players.Contains(player.userID))
                {
                    newTeam.Players.Add(player.userID);
                }

                if (!parties[newTeam.Id].AddPlayer(player))
                {
                    Puts("Already on team");
                }

                if (parties[newTeam.Id].GetLeader() == null)
                {
                    parties[newTeam.Id].SetTeamLeader(player.userID);
                    Puts("No leader!");
                }

                covalence.Server.Broadcast(string.Format("{0}: {1} joined team {2} 2.", arena.Name.ToUpper(), player.displayName, newTeam.Name));
            });
        }

        private RelationshipManager.PlayerTeam CreateOrValidateTeam(Data.Team team, BasePlayer player)
        {
            var c = RelationshipManager.Instance.FindPlayersTeam(player.userID);
            if (c != null)
            {
                c.RemovePlayer(player.userID);
            }

            if (!parties.ContainsKey(team.Id))
            {
                Puts(string.Format("New Party with TeamId {0}", team.Id));
                RelationshipManager.PlayerTeam newTeam = RelationshipManager.Instance.CreateTeam();
                RelationshipManager.PlayerTeam playerTeam = newTeam;
                playerTeam.teamLeader = player.userID;
                playerTeam.AddPlayer(player);
                parties.Add(team.Id, playerTeam);
                return playerTeam;
            }

            var currentTeam = RelationshipManager.Instance.FindTeam(parties[team.Id].teamID);
            if (currentTeam == null)
            {
                Puts("Re-creating bad team!");
                if (parties.ContainsKey(team.Id))
                {
                    if (parties[team.Id] != null)
                    {
                        Puts("Trying to Disband previous team!");
                        parties[team.Id].Disband();
                    }

                    parties.Remove(team.Id);
                }

                RelationshipManager.PlayerTeam newTeam = RelationshipManager.Instance.CreateTeam();
                RelationshipManager.PlayerTeam playerTeam = newTeam;
                playerTeam.teamLeader = player.userID;
                playerTeam.AddPlayer(player);
                parties.Add(team.Id, playerTeam);
                return playerTeam;
            }
            return null;
        }

        private void ValidateAndFixTeams()
        {
            //foreach (BasePlayer player in BasePlayer.activePlayerList)
            //{
            //    Data.Team team = this.data.GetPlayerTeam(player);

            //    if (player.currentTeam == 0UL && team != null)
            //    {
            //        if (CreateOrValidateTeam(team, player) == true) return;

            //        if (!parties.ContainsKey(team.Id))
            //        {
            //            Puts("No such party!");
            //        }

            //        if (!parties[team.Id].AddPlayer(player))
            //        {
            //            Puts("Already on team");
            //        }

            //        if (parties[team.Id].GetLeader() == null)
            //        {
            //            parties[team.Id].SetTeamLeader(player.userID);
            //            Puts("No leader!");
            //        }
            //    }
            //}
        }

        public void LeaveTeam(BasePlayer player, Data.ArenaData arena)
        {
            if (player == null) return;

            var currentTeam = this.data.GetPlayerTeam(player);
            if (currentTeam != null)
            {
                covalence.Server.Broadcast(string.Format("{0}: {1} left team {2}.", arena.Name.ToUpper(), player.displayName, currentTeam.Name));
            }

            this.data.ClearUserCacheForArena(player);

            foreach (Data.Team team in arena.Teams.Values)
            {
                if (team.Players.Contains(player.userID))
                {
                    team.Players.Remove(player.userID);
                }

                if (parties.ContainsKey(team.Id))
                {
                    var party = parties[team.Id];
                    if (party == null) continue;
                    party.RemovePlayer(player.userID);
                    if (party.members.Count == 0) parties.Remove(team.Id);
                }
            }

            player.ClearTeam();
        }
    }
}