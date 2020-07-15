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
    [Info("Ui", "eandersson", "0.3.2", ResourceId = 2686)]
    public class Ui : RustPlugin
    {
        public const string TeamUi = "TeamUi";
        public const string ArenaUi = "ArenaUi";
        public const string DeadUiPanel = "DeadUiPanel";
        public const string StageUiPanel = "StageUiPanel";
        public const string TimeUiPanel = "TimeUiPanel";
        public const string TcUiPanel = "TcUiPanel";
        public const string TeamUiPanel = "TeamUiPanel";
        public const string TicketUiPanel = "TicketUiPanel";

        private Arena arena = null;
        private Data data = null;

        public void Configure(Arena arena)
        {
            this.arena = arena;
            this.data = arena.data;
        }

        public void DestroyUi(BasePlayer player, string name)
        {
            CuiHelper.DestroyUi(player, name);
        }

        class PanelRect
        {
            public float left, bottom, right, top;
            public PanelRect()
            {
                left = 0f;
                bottom = 0f;
                right = 1f;
                top = 1f;
            }
            public PanelRect(float left, float bottom, float right, float top)
            {
                this.left = left;
                this.bottom = bottom;
                this.right = right;
                this.top = top;
            }

            public string AnchorMin
            {
                get { return left + " " + bottom; }
            }
            public string AnchorMax
            {
                get { return right + " " + top; }
            }

            public PanelRect RelativeTo(PanelRect other)
            {
                left = other.left + (other.right - other.left) * left;
                right = other.left + (other.right - other.left) * right;
                top = other.bottom + (other.top - other.bottom) * top;
                bottom = other.bottom + (other.top - other.bottom) * bottom;
                return this;
            }

            public PanelRect Copy()
            {
                return new PanelRect(left, bottom, right, top);
            }
        }

        class PanelInfo
        {
            public PanelRect rect;
            public string backgroundColor;

            public PanelInfo(PanelRect rect, string color)
            {
                this.rect = rect;
                this.backgroundColor = color;
            }
        }

        private CuiElementContainer CreateElementContainer(string panelName, PanelInfo panelInfo, bool cursor = false)
        {
            var newElement = new CuiElementContainer()
            {
                {
                    new CuiPanel
                    {
                        Image = {Color = panelInfo.backgroundColor},
                        RectTransform = { AnchorMin = panelInfo.rect.AnchorMin, AnchorMax = panelInfo.rect.AnchorMax },
                        CursorEnabled = cursor
                    },
                    new CuiElement().Parent,
                    panelName
                }
            };
            return newElement;
        }

        private void CreatePanel(ref CuiElementContainer container, string panel, PanelInfo panelInfo, bool cursor = false)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = panelInfo.backgroundColor },
                RectTransform = { AnchorMin = panelInfo.rect.AnchorMin, AnchorMax = panelInfo.rect.AnchorMax },
                CursorEnabled = cursor
            }, panel);
        }

        private void CreateLabel(ref CuiElementContainer container, string panel, PanelRect rect, string color, string text, int size = 30, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = new CuiLabel
            {
                Text = { Color = color, FontSize = size, Align = align, FadeIn = 0.0f, Text = text },
                RectTransform = { AnchorMin = rect.AnchorMin, AnchorMax = rect.AnchorMax }
            };

            container.Add(label, panel);
        }

        private void CreateMenuButton(ref CuiElementContainer container, string panel, string command, string text, PanelRect rect, int fontSize = 22, string color = "1 1 1 0.2", string textcolor = "1 1 1 1", TextAnchor align = TextAnchor.MiddleCenter)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = rect.AnchorMin, AnchorMax = rect.AnchorMax },
                Text = { Text = text, FontSize = fontSize, Align = align, Color = textcolor }
            }, panel);
        }

        public void ShowArenaUi(BasePlayer player)
        {
            var elements = CreateElementContainer(ArenaUi, new PanelInfo(new PanelRect(0.3f, 0.3f, 0.7f, 0.9f), "0.1 0.1 0.1 0.6"), true);
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0f, 0f, 0.001f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0.999f, 0f, 1f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0f, 0.999f, 1f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0f, 0f, 1f, 0.001f), "1 1 1 0.7"));

            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0.05f, 0.85f, 0.95f, 0.95f), "0.1 0.1 0.1 0.9"));
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0.05f, 0.849f, 0.95f, 0.85f), "1 1 1 1"));
            CreateLabel(ref elements, ArenaUi, new PanelRect(0.05f, 0.85f, 0.95f, 0.95f), "1 1 1 1", "SELECT ARENA");
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0.05f, 0.05f, 0.95f, 0.835f), "0.1 0.1 0.1 0.9"));
            CreatePanel(ref elements, ArenaUi, new PanelInfo(new PanelRect(0.05f, 0.049f, 0.95f, 0.05f), "1 1 1 1"));

            int index = 0; 
            foreach (Data.ArenaData arena in this.data.Arenas.Values)
            {
                float padding = 0.09f;
                float yi = index * padding;

                var nameRect = new PanelRect(0.06f, 0.755f - yi, 0.12f, 0.825f - yi);
                var infoRect = new PanelRect(0.13f, 0.755f - yi, 0.30f, 0.825f - yi);
                var infoRect2 = new PanelRect(0.31f, 0.755f - yi, 0.49f, 0.825f - yi);

                CreatePanel(ref elements, ArenaUi, new PanelInfo(nameRect, "1 1 1 0.2"));
                CreateLabel(ref elements, ArenaUi, nameRect, "1 1 1 1", string.Format("{0}", arena.Name.ToUpper()), size: 22);

                CreatePanel(ref elements, ArenaUi, new PanelInfo(infoRect, "1 1 1 0.2"));
                CreateLabel(ref elements, ArenaUi, infoRect, "1 1 1 1", string.Format("{0}", arena.CurrentStageName.ToUpper()), size: 22);

                CreatePanel(ref elements, ArenaUi, new PanelInfo(infoRect2, "1 1 1 0.2"));

                string time = null;
                if (arena.CurrentStage == (int) Data.Stages.Attacking)
                    time = arena.GetTimeAttacking()?.ToString(@"hh\H\ mm\M");
                else
                    time = arena.GetTimeLeft()?.ToString(@"mm\M\ ss\S");

                CreateLabel(ref elements, ArenaUi, infoRect2, "1 1 1 1", time ?? "", size: 22);

                foreach (var teamIndex in arena.Teams.Keys)
                {
                    float teamPadding = teamIndex * 0.1125f;
                    var team = arena.Teams[teamIndex];
                    var buttonRect = new PanelRect(0.50f + teamPadding, 0.755f - yi, 0.60f + teamPadding, 0.825f - yi);
                    CreateMenuButton(ref elements, ArenaUi, string.Format("rustarena.join {0} {1}", arena.Name, team.Name), "Join", buttonRect, textcolor: team.Color);
                }

                index += 1;
            }

            CuiHelper.DestroyUi(player, ArenaUi);
            CuiHelper.DestroyUi(player, TeamUi);
            CuiHelper.AddUi(player, elements);
        }

        public void ShowTeamUi(Data.ArenaData arena, BasePlayer player)
        {
            var elements = CreateElementContainer(TeamUi, new PanelInfo(new PanelRect(0.3f, 0.3f, 0.7f, 0.9f), "0.1 0.1 0.1 0.6"), false);
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0f, 0f, 0.001f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0.999f, 0f, 1f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0f, 0.999f, 1f, 1f), "1 1 1 0.7"));
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0f, 0f, 1f, 0.001f), "1 1 1 0.7"));

            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0.05f, 0.85f, 0.95f, 0.95f), "0.1 0.1 0.1 0.9"));
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0.05f, 0.849f, 0.95f, 0.85f), "1 1 1 1"));
            CreateLabel(ref elements, TeamUi, new PanelRect(0.05f, 0.85f, 0.95f, 0.95f), "1 1 1 1", "SELECT TEAM");
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0.05f, 0.05f, 0.95f, 0.835f), "0.1 0.1 0.1 0.9"));
            CreatePanel(ref elements, TeamUi, new PanelInfo(new PanelRect(0.05f, 0.049f, 0.95f, 0.05f), "1 1 1 1"));

            foreach (var index in arena.Teams.Keys)
            {
                float padding = (index * 0.23f);
                var team = arena.Teams[index];
                var buttonRect = new PanelRect(0.06f + padding, 0.755f, 0.25f + padding, 0.825f);
                CreateMenuButton(ref elements, TeamUi, string.Format("rustarena.join {0} {1}", arena.Name, team.Name), "Join", buttonRect, textcolor: team.Color);

                int playerIndex = 1;
                foreach (var playerId in team.Players)
                {
                    float namePadding = (playerIndex * 0.07f);
                    string playerName = this.data.GetPlayerName(playerId);
                    var infoRect = new PanelRect(0.06f + padding, 0.755f - namePadding, 0.25f + padding, 0.825f - namePadding);
                    CreateLabel(ref elements, TeamUi, infoRect, team.Color, string.Format("{0}", playerName), size: 14);
                    playerIndex += 1;
                }
            }

            CuiHelper.DestroyUi(player, ArenaUi);
            CuiHelper.DestroyUi(player, TeamUi);
            CuiHelper.AddUi(player, elements);
        }

        public void UpdateStageUi(BasePlayer player)
        {
            Data.Team team = this.data.GetPlayerTeam(player);

            CuiHelper.DestroyUi(player, TcUiPanel);
            CuiHelper.DestroyUi(player, TimeUiPanel);
            CuiHelper.DestroyUi(player, StageUiPanel);
            CuiHelper.DestroyUi(player, TeamUiPanel);
            CuiHelper.DestroyUi(player, TicketUiPanel);
            CuiHelper.DestroyUi(player, DeadUiPanel);

            if (team == null) return;
            if (team.Arena == null) return;

            int index = 0;
            DrawStageUi(team.Arena, player, ref index);
            DrawTeamUi(team, player, ref index);
            if (team.Arena.CurrentStage >= (int) Data.Stages.Attacking)
                DrawTcUi(team.Arena, player, ref index);
            DrawTimeUi(team.Arena, player, ref index);
            DrawTicketsUi(team, player, ref index);
            if (this.data.DeadPlayers.Contains(player))
                DrawDeadUi(player, ref index);
        }

        private void DrawDeadUi(BasePlayer player, ref int index)
        {
            Data.PlayerData playerData = this.data.GetPlayerData(player);
            string text = playerData.GetRespawnTimeLeft()?.ToString(@"mm\M\ ss\S");
            string color = "0.8 0 0 1";

            DrawInfo(player, text, color, DeadUiPanel, ref index);
        }

        private void DrawStageUi(Data.ArenaData arena, BasePlayer player, ref int index)
        {
            string text = string.Format("({0}) {1}", arena.Name.ToUpper(), arena.CurrentStageName);
            string color = "1 1 1 1";

            //if (matchData.CurrentStage == 1) color = "0 0.8 0 1";
            //else if (matchData.CurrentStage == 1) color = "1 0.6 0";

            DrawInfo(player, text, color, StageUiPanel, ref index);
        }

        private void DrawTcUi(Data.ArenaData arena, BasePlayer player, ref int index)
        {
            int remaning = this.data.GetTeamsWithCupboards(arena).Count();
            Data.Team winningTeam = arena.WinningTeam;

            string text;
            string color = "0 1 0 1";

            if (winningTeam != null || remaning == 0)
            {
                text = string.Format("Winner: {0}", winningTeam?.Name ?? "Unknown");
                color = "0 1 0 1";
            }
            else
            {
                text = string.Format("TCs Remaning: {0}", remaning.ToString());
                if (remaning < 4) color = "1 0.6 0";
            }

            DrawInfo(player, text, color, TcUiPanel, ref index);
        }

        private void DrawTimeUi(Data.ArenaData arena, BasePlayer player, ref int index)
        {
            string text = null;
            if (arena.CurrentStage == (int) Data.Stages.Attacking)
                text = arena.GetTimeAttacking()?.ToString(@"hh\H\ mm\M\ ss\S");
            else
                text = arena.GetTimeLeft()?.ToString(@"mm\M\ ss\S");

            string color = "1 1 1 1";

            DrawInfo(player, text ?? "", color, TimeUiPanel, ref index);
        }

        private void DrawTeamUi(Data.Team team, BasePlayer player, ref int index)
        {
            string text = string.Format("{0} Team", team.Name);
            string color = "1 1 1 1";

            DrawInfo(player, text, color, TeamUiPanel, ref index);
        }

        private void DrawTicketsUi(Data.Team team, BasePlayer player, ref int index)
        {
            string text = string.Format("{0} Tickets", team.TicketsRemaning);
            string color = "1 1 1 1";

            DrawInfo(player, text, color, TicketUiPanel, ref index);
        }

        private void DrawInfo(BasePlayer player, string text, string color, string panelName, ref int index)
        {
            float padding = 0.04f;
            float bottom = 0.959f - (index * padding);
            float top = 0.999f - (index * padding);
            PanelRect infoRect = new PanelRect(0f, 0.1f, 0.95f, 0.855f);
            PanelRect separator = new PanelRect(0f, 0.05f, 0.95f, 0.05f);
            PanelInfo panel = new PanelInfo(new PanelRect(0.905f, bottom, 0.999f, top), "0 0 0 0");

            var elements = CreateElementContainer(panelName, panel, false);
            CreatePanel(ref elements, panelName, new PanelInfo(infoRect, "0.1 0.1 0.1 0.9"));
            CreatePanel(ref elements, panelName, new PanelInfo(separator, "1 1 1 1"));
            CreateLabel(ref elements, panelName, new PanelRect(0f, 0f, 0.94f, 0.95f), color, text, size: 16);

            CuiHelper.AddUi(player, elements);
            index += 1;
        }
    }
}