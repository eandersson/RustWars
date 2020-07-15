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
    [Info("Walls", "eandersson", "0.3.2", ResourceId = 2688)]
    public class Walls : RustPlugin
    {
        private readonly int worldMask = LayerMask.GetMask("World");
        private readonly int wallMask = LayerMask.GetMask("Terrain", "World", "Default", "Construction", "Deployed");

        public object Create(ulong ownerId, Vector3 w)
        {
            int spawned = 0;
            float gap = 6.25f;
            int stacks = 6;

            Remove(ownerId);

            spawned = CreateCentralWalls(ownerId, w, 30f);

            float startY = w.y;
            float startX = w.x;
            float startZ = w.z;

            Quaternion xRotation = Quaternion.Euler(0, 0, 0);
            Quaternion yRotation = Quaternion.Euler(0, -90, 0);

            for (float i = 0; i < stacks; i++)
            {
                float zPos = w.z;
                float xPos = w.x;
                float yPos = startY + (i * gap);

                for (int index = 0; index < 25; index++)
                {
                    Vector3 position = new Vector3(startX, yPos, zPos);
                    if (CreateWallEntry(ownerId, position, yRotation)) spawned++;
                    zPos += gap;
                }

                zPos = startZ;

                for (int index = 0; index < 25; index++)
                {
                    Vector3 position = new Vector3(startX, yPos, zPos);
                    if (CreateWallEntry(ownerId, position, yRotation)) spawned++;
                    zPos -= gap;
                }


                for (int index = 0; index < 25; index++)
                {
                    Vector3 position = new Vector3(xPos, yPos, startZ);
                    if (CreateWallEntry(ownerId, position, xRotation)) spawned++;
                    xPos += gap;
                }

                xPos = startX;

                for (int index = 0; index < 25; index++)
                {
                    Vector3 position = new Vector3(xPos, yPos, startZ);
                    if (CreateWallEntry(ownerId, position, xRotation)) spawned++;
                    xPos -= gap;
                }
            }

            Puts(string.Format("Spawned {0} Wall entries.", spawned));

            return true;
        }


        public int CreateCentralWalls(ulong ownerId, Vector3 center, float radius)
        {
            int spawned = 0;

            float gap = 0.5f;
            int stacks = 6;
            float next = 360 / radius - gap;

            for (int i = 0; i < stacks; i++)
            {
                foreach (var position in Utility.GetCircumferencePositions(center, radius, next, center.y))
                {
                    if (CreateWallEntry(ownerId, position, default(Quaternion), center)) spawned++;
                }

                center.y += 6f;
            }

            return spawned;
        }


        public bool CreateWallEntry(ulong ownerId, Vector3 position, Quaternion rotation, Vector3? center = null)
        {
            SimpleBuildingBlock entity =
                GameManager.server.CreateEntity(Data.PrefabHighExternalStoneWall, position, rotation, false) as
                    SimpleBuildingBlock;

            if (entity != null)
            {
                entity.OwnerID = ownerId;
                entity.transform.LookAt(center ?? position, Vector3.up);
                entity.Spawn();
                entity.gameObject.SetActive(true);
                return true;
            }

            return false;
        }

        public int Remove(ulong ownerId)
        {
            int removed = 0;
            foreach (SimpleBuildingBlock entity in BaseNetworkable.serverEntities.Where(e => e.name == Data.PrefabHighExternalStoneWall).OfType<SimpleBuildingBlock>().ToList())
            {
                if (entity.OwnerID == ownerId)
                {
                    entity.Kill();
                    removed++;
                }
            }

            return removed;
        }

        public int RemoveAll()
        {
            int removed = 0;
            foreach (SimpleBuildingBlock entity in BaseNetworkable.serverEntities.Where(e => e.name == Data.PrefabHighExternalStoneWall).OfType<SimpleBuildingBlock>().ToList())
            {
                entity.Kill();
                removed++;
            }

            return removed;
        }
    }
}
