﻿using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace DefenseShields.Support
{
    internal static class DsUtilsStatic
    {
        public static void GetRealPlayers(Vector3D center, float radius, List<long> realPlayers)
        {
            List<IMyIdentity> realPlayersIdentities = new List<IMyIdentity>();
            MyAPIGateway.Players.GetAllIdentites(realPlayersIdentities, p => !string.IsNullOrEmpty(p?.DisplayName));
            var pruneSphere = new BoundingSphereD(center, radius);
            var pruneList = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref pruneSphere, pruneList);

            foreach (var ent in pruneList)
            {
                if (ent == null || !(ent is IMyCubeGrid || ent is IMyCharacter)) continue;

                IMyPlayer player = null;

                if (ent is IMyCharacter)
                {
                    player = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
                    if (player == null) continue;
                }
                else
                {
                    var playerTmp = MyAPIGateway.Players.GetPlayerControllingEntity(ent);

                    if (playerTmp?.Character != null) player = playerTmp;
                }

                if (player == null) continue;
                if (realPlayersIdentities.Contains(player.Identity)) realPlayers.Add(player.IdentityId);
            }
        }

        public static void GetDefinitons()
        {
            try
            {
                var defintions = MyDefinitionManager.Static.GetAllDefinitions();
                foreach (var def in defintions)
                {
                    if (!(def is MyAmmoMagazineDefinition)) continue;
                    var ammoDef = def as MyAmmoMagazineDefinition;
                    var ammo = MyDefinitionManager.Static.GetAmmoDefinition(ammoDef.AmmoDefinitionId);
                    if (!(ammo is MyMissileAmmoDefinition)) continue;
                    var shot = ammo as MyMissileAmmoDefinition;
                    if (Session.AmmoCollection.ContainsKey(shot.MissileModelName)) continue;
                    Session.AmmoCollection.Add(shot.MissileModelName, new AmmoInfo(shot.IsExplosive, shot.MissileExplosionDamage, shot.MissileExplosionRadius, shot.DesiredSpeed, shot.MissileMass, shot.BackkickForce));
                }
                Log.Line($"Definitions Loaded");
            }
            catch (Exception ex) { Log.Line($"Exception in GetAmmoDefinitions: {ex}"); }
        }

        public static double CreateShieldFit(IMyCubeBlock shield, bool buffer)
        {
            var blockPoints = new Vector3D[8];
            var blocks = new List<IMySlimBlock>();
            shield.CubeGrid.GetBlocks(blocks, null);

            var last = 0;
            for (int i = 0; i <= 10; i++)
            {
                var ellipsoidAdjust = MathHelper.Lerp(Math.Sqrt(2), Math.Sqrt(3), i * 0.1);
                Vector3D gridHalfExtents = shield.CubeGrid.PositionComp.LocalAABB.HalfExtents;

                var shieldSize = gridHalfExtents * ellipsoidAdjust;
                var mobileMatrix = MatrixD.CreateScale(shieldSize);
                mobileMatrix.Translation = shield.CubeGrid.PositionComp.LocalVolume.Center;
                var matrixInv = MatrixD.Invert(mobileMatrix * shield.CubeGrid.WorldMatrix);

                var c = 0;
                foreach (var block in blocks)
                {
                    BoundingBoxD blockBox;
                    block.GetWorldBoundingBox(out blockBox);

                    blockBox.GetCorners(blockPoints);

                    foreach (var point in blockPoints) if (!CustomCollision.PointInShield(point, matrixInv)) c++;
                }
                //Log.Line($"step:{i} - matched:{c} - computed:{ellipsoidAdjust} - sqrt2:{Math.Sqrt(2)} - sqrt3:{Math.Sqrt(3)} - {shield.CubeGrid.DisplayName}");

                if (c == 0 || last == c && !buffer)
                {
                    var extra = 0;
                    if (buffer) extra = 10 - i;
                    return MathHelper.Lerp(Math.Sqrt(2), Math.Sqrt(3), (i + extra) * 0.1);
                }
                last = c;
            }
            return Math.Sqrt(3);
        }

        public static void PrepConfigFile()
        {
            var baseScaler = 30;
            var nerf = 0f;
            var efficiency = 100f;
            var stationRatio = 2;
            var largeShipRate = 3;
            var smallShipRatio = 1;
            var disableVoxel = 0;
            var disableGridDmg = 0;
            var debug = 0;
            var altRecharge = false;
            var version = 56;

            var dsCfgExists = MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg");
            if (dsCfgExists)
            {
                var unPackCfg = MyAPIGateway.Utilities.ReadFileInGlobalStorage("DefenseShields.cfg");
                var unPackedData = MyAPIGateway.Utilities.SerializeFromXML<DefenseShieldsEnforcement>(unPackCfg.ReadToEnd());

                if (Session.Enforced.Debug == 1) Log.Line($"unPackedData is: {unPackedData}\nServEnforced are: {Session.Enforced}");

                if (unPackedData.Version == version) return;
                Log.Line($"outdated config file regenerating, file version: {unPackedData.Version} - current version: {version}");
                Session.Enforced.BaseScaler = !unPackedData.BaseScaler.Equals(-1) ? unPackedData.BaseScaler : baseScaler;
                Session.Enforced.Nerf = !unPackedData.Nerf.Equals(-1f) ? unPackedData.Nerf : nerf;
                Session.Enforced.Efficiency = !unPackedData.Efficiency.Equals(-1f) ? unPackedData.Efficiency : efficiency;
                Session.Enforced.StationRatio = !unPackedData.StationRatio.Equals(-1) ? unPackedData.StationRatio : stationRatio;
                Session.Enforced.LargeShipRatio = !unPackedData.LargeShipRatio.Equals(-1) ? unPackedData.LargeShipRatio : largeShipRate;
                Session.Enforced.SmallShipRatio = !unPackedData.SmallShipRatio.Equals(-1) ? unPackedData.SmallShipRatio : smallShipRatio;
                Session.Enforced.DisableVoxelSupport = !unPackedData.DisableVoxelSupport.Equals(-1) ? unPackedData.DisableVoxelSupport : disableVoxel;
                Session.Enforced.DisableGridDamageSupport = !unPackedData.DisableGridDamageSupport.Equals(-1) ? unPackedData.DisableGridDamageSupport : disableGridDmg;
                Session.Enforced.Debug = !unPackedData.Debug.Equals(-1) ? unPackedData.Debug : debug;
                Session.Enforced.AltRecharge = unPackedData.AltRecharge ? unPackedData.AltRecharge : altRecharge;
                Session.Enforced.Version = !unPackedData.Version.Equals(-1) ? unPackedData.Version : version;

                unPackedData = null;
                unPackCfg.Close();
                unPackCfg.Dispose();
                MyAPIGateway.Utilities.DeleteFileInGlobalStorage("DefenseShields.cfg");
                var newCfg = MyAPIGateway.Utilities.WriteFileInGlobalStorage("DefenseShields.cfg");
                var newData = MyAPIGateway.Utilities.SerializeToXML(Session.Enforced);
                newCfg.Write(newData);
                newCfg.Flush();
                newCfg.Close();

                if (Session.Enforced.Debug == 1)
                    Log.Line(
                        $"wrote modified config file - file exists: {MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg")}");
            }
            else
            {
                Session.Enforced.BaseScaler = baseScaler;
                Session.Enforced.Nerf = nerf;
                Session.Enforced.Efficiency = efficiency;
                Session.Enforced.StationRatio = stationRatio;
                Session.Enforced.LargeShipRatio = largeShipRate;
                Session.Enforced.SmallShipRatio = smallShipRatio;
                Session.Enforced.DisableVoxelSupport = disableVoxel;
                Session.Enforced.DisableGridDamageSupport = disableGridDmg;
                Session.Enforced.Debug = debug;
                Session.Enforced.AltRecharge = altRecharge;
                Session.Enforced.Version = version;

                var cfg = MyAPIGateway.Utilities.WriteFileInGlobalStorage("DefenseShields.cfg");
                var data = MyAPIGateway.Utilities.SerializeToXML(Session.Enforced);
                cfg.Write(data);
                cfg.Flush();
                cfg.Close();

                if (Session.Enforced.Debug == 1)
                    Log.Line($"wrote new config file - file exists: {MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg")}");
            }
        }

        public static void ReadConfigFile()
        {
            var dsCfgExists = MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg");

            if (Session.Enforced.Debug == 1) Log.Line($"Reading config, file exists? {dsCfgExists}");

            if (!dsCfgExists) return;

            var cfg = MyAPIGateway.Utilities.ReadFileInGlobalStorage("DefenseShields.cfg");
            var data = MyAPIGateway.Utilities.SerializeFromXML<DefenseShieldsEnforcement>(cfg.ReadToEnd());
            Session.Enforced = data;

            if (Session.Enforced.Debug == 1) Log.Line($"Writing settings to mod:\n{data}");
        }
    }
}
