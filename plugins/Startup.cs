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
    [Info("Startup", "eandersson", "0.3.2", ResourceId = 2684)]
    public class Startup : RustPlugin
    {
        private void OnNewSave(string filename)
        {
            Puts("OnNewSave");
            Utility.SetServerDefaults();

             var instance = SingletonComponent<SpawnHandler>.Instance;
            instance.RadiusCheckDistance = 20f;
            instance.MaxSpawnsPerTick = 300;

            List<SpawnPopulation> SpawnPopulations = new List<SpawnPopulation>();
            foreach (SpawnPopulation spawn in instance.SpawnPopulations.ToList())
            {
                string name = spawn.LookupFileName();

                if (name == Data.AssetSpawnTempForest)
                {
                    spawn.Filter.TopologyAny = TerrainTopology.Enum.Forest;
                    spawn.Filter.TopologyNot = TerrainTopology.Enum.Decor;

                    spawn._targetDensity = 3500f;
                    spawn.ScaleWithSpawnFilter = false;
                    spawn.ScaleWithLargeMaps = false;
                    spawn.ScaleWithServerPopulation = false;
                    SpawnPopulations.Add(spawn);
                }
                else if (name == Data.AssetSpawnHemp)
                {
                    spawn.Filter.TopologyAny = TerrainTopology.Enum.Decor;
                    spawn.Filter.TopologyNot = TerrainTopology.Enum.Forest;
                    spawn._targetDensity = 50f;
                    spawn.ScaleWithSpawnFilter = false;
                    spawn.ScaleWithLargeMaps = false;
                    spawn.ScaleWithServerPopulation = false;
                    SpawnPopulations.Add(spawn);
                }
                else if (name == Data.AssetSpawnOre)
                {
                    spawn.Filter.TopologyAny = TerrainTopology.Enum.Decor;
                    spawn.Filter.TopologyNot = TerrainTopology.Enum.Forest;
                    spawn._targetDensity = 3500f;
                    spawn.ScaleWithSpawnFilter = false;
                    spawn.ScaleWithLargeMaps = false;
                    spawn.ScaleWithServerPopulation = false;
                    if (spawn.Prefabs != null)
                    {
                        foreach (var prefab in spawn.Prefabs)
                        {
                            var parameters = prefab.Parameters;
                            if (parameters != null)
                            {
                                parameters.Count = 1;
                            }
                        }
                    }
                    SpawnPopulations.Add(spawn);
                }
            }

            instance.SpawnPopulations = SpawnPopulations.ToArray();
            instance.AllSpawnPopulations = SpawnPopulations.ToArray();

            NextTick(() =>
            {
                instance.UpdateDistributions();
            });
        }
    }
}