﻿using System;
using System.Collections.Generic;
using System.Linq;
using DefenseShields.Support;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace DefenseShields
{
    public partial class Session
    {
        public static int EntSlotAssigner;
        public static int GetSlot()
        {
            if (++EntSlotAssigner >= EntSlotScaler) EntSlotAssigner = 0;
            return EntSlotAssigner;
        }

        #region WebMonitor
        public void WebMonitor()
        {
            try
            {
                Log.Line($"Starting Monitor");
                while (Monitor)
                {
                    if (!Wake && Monitor)
                    {
                        MyAPIGateway.Parallel.Sleep(SleepTime);
                        continue;
                    }
                    Wake = false;

                    if (Enforced.Debug >= 3 && EntSlotTick) Dsutil1.Sw.Restart();
                    _newFrame = false;
                    var tick = Tick;
                    var shieldList = new List<DefenseShields>(FunctionalShields);
                    MyAPIGateway.Parallel.For(0, shieldList.Count, x =>
                    {
                        var s = shieldList[x];
                        if (_newFrame || !s.Warming) return;
                        var shieldActive = false;
                        if (IsServer)
                        {
                            shieldActive = ActiveShields.Contains(s);
                            if (s.LogicPaused)
                            {
                                if (shieldActive) ActiveShields.Remove(s);
                                s.Asleep = false;
                                return;
                            }
                            s.LogicPaused = true;
                        }

                        lock (s.GetCubesLock)
                        {
                            var cleanDistributor = s.MyGridDistributor != null && s.FuncTask.IsComplete && s.MyGridDistributor.SourcesEnabled != MyMultipleEnabledEnum.NoObjects;
                            if (cleanDistributor)
                            {
                                s.GridCurrentPower = s.MyGridDistributor.TotalRequiredInputByType(MyResourceDistributorComponent.ElectricityId);
                                s.GridMaxPower = s.MyGridDistributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);
                            }
                        }

                        if (!IsServer)
                        {
                            s.TicksWithNoActivity = 0;
                            s.LastWokenTick = tick;
                            s.Asleep = false;
                            return;
                        }

                        if (!shieldActive)
                        {
                            s.Asleep = true;
                            return;
                        }

                        if (EntSlotTick && RefreshCycle == s.MonitorSlot) MonitorRefreshTasks(s, tick);

                        if (tick < s.LastWokenTick + 400 || s.ShieldComp.GridIsMoving || s.Missiles.Count > 0)
                        {
                            if (s.ShieldComp.GridIsMoving) s.LastWokenTick = tick;
                            s.Asleep = false;
                            return;
                        }

                        if (!s.PlayerByShield && !s.MoverByShield)
                        {
                            if (s.TicksWithNoActivity++ % EntCleanCycle == 0) s.EntCleanUpTime = true;
                            s.Asleep = true;
                            return;
                        }
                        var monitorList = new List<MyEntity>();
                        var intersect = false;
                        MyGamePruningStructure.GetTopmostEntitiesInBox(ref s.WebBox, monitorList, MyEntityQueryType.Dynamic);
                        for (int i = 0; i < monitorList.Count; i++)
                        {
                            var ent = monitorList[i];

                            if (!(ent is MyCubeGrid || ent is IMyCharacter || ent is IMyMeteor)) continue;
                            if (ent.Physics.IsMoving)
                            {
                                if (s.WebBox.Intersects(ent.PositionComp.WorldAABB))
                                {
                                    intersect = true;
                                    break;
                                }
                            }
                        }

                        if (!intersect)
                        {
                            s.Asleep = true;
                            return;
                        }
                        s.TicksWithNoActivity = 0;
                        s.LastWokenTick = tick;
                        s.Asleep = false;
                    });
                    if (Enforced.Debug >= 3 && EntSlotTick) Dsutil1.StopWatchReport($"[Monitoring] tick:{tick} - ", -1);
                }
                Log.Line($"Stopping Monitor");
            }
            catch (Exception ex) { Log.Line($"Exception in WebMonitor: {ex}"); }
        }

        private void MonitorRefreshTasks(DefenseShields s, uint tick)
        {
            var foundPlayer = false;
            foreach (var player in Players.Values)
            {
                var character = player.Character;
                if (character == null) continue;

                if (Vector3D.DistanceSquared(character.PositionComp.WorldMatrix.Translation, s.DetectionCenter) < _syncDistSqr)
                {
                    foundPlayer = true;
                    break;
                }
            }
            s.PlayerByShield = foundPlayer;

            if (!s.PlayerByShield)
            {
                s.MoverByShield = false;
                var newMover = false;
                var moverList = new List<MyEntity>();

                MyGamePruningStructure.GetTopMostEntitiesInBox(ref s.ShieldBox3K, moverList, MyEntityQueryType.Dynamic);
                for (int i = 0; i < moverList.Count; i++)
                {
                    var ent = moverList[i];

                    if (!(ent is MyCubeGrid || ent is IMyCharacter || ent is IMyMeteor)) continue;
                    var entPos = ent.PositionComp.WorldMatrix.Translation;

                    var keyFound = s.EntsByMe.ContainsKey(ent);
                    if (keyFound)
                    {
                        if (!s.EntsByMe[ent].Pos.Equals(entPos, 1e-3))
                        {
                            MoverInfo moverInfo;
                            s.EntsByMe.TryRemove(ent, out moverInfo);
                            s.EntsByMe.TryAdd(ent, new MoverInfo(entPos, tick));
                            newMover = true;
                            break;
                        }
                    }
                    else s.EntsByMe.TryAdd(ent, new MoverInfo(entPos, tick));
                }
                s.MoverByShield = newMover;
            }
            if (tick < s.LastWokenTick + 400)
            {
                s.Asleep = false;
                return;
            }

            if (s.EntCleanUpTime)
            {
                var entsByMeTmp = new List<KeyValuePair<MyEntity, MoverInfo>>();
                entsByMeTmp.AddRange(s.EntsByMe.Where(info => tick - info.Value.CreationTick > EntMaxTickAge * ++s.CleanCycle && !info.Value.Pos.Equals(info.Key.PositionComp.WorldMatrix.Translation, 1e-3)));
                for (int i = 0; i < entsByMeTmp.Count; i++) s.EntsByMe.Remove(entsByMeTmp[i].Key);
                s.EntCleanUpTime = false;
            }
        }
        #endregion

        private void LoadBalancer()
        {
            _newFrame = true;
            Tick = (uint)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds / MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS;
            Tick20 = Tick % 20 == 0;
            Tick60 = Tick % 60 == 0;
            Tick60 = Tick % 60 == 0;
            Tick180 = Tick % 180 == 0;
            Tick600 = Tick % 600 == 0;
            Tick1800 = Tick % 1800 == 0;
            if (MoreThan600Frames)
            {
                var oldScaler = EntSlotScaler;
                var globalProtCnt = GlobalProtect.Count;
                if (globalProtCnt <= 25) EntSlotScaler = 1;
                else if (globalProtCnt <= 50) EntSlotScaler = 2;
                else if (globalProtCnt <= 75) EntSlotScaler = 3;
                else if (globalProtCnt <= 100) EntSlotScaler = 4;
                else if (globalProtCnt <= 150) EntSlotScaler = 5;
                else if (globalProtCnt <= 200) EntSlotScaler = 6;
                else EntSlotScaler = 9;

                if (oldScaler != EntSlotScaler)
                {
                    GlobalProtect.Clear();
                    foreach (var s in FunctionalShields)
                    {
                        s.AssignSlots();
                        s.Asleep = false;
                    }
                    foreach (var c in Controllers)
                    {
                        if (FunctionalShields.Contains(c)) continue;
                        c.AssignSlots();
                        c.Asleep = false;
                    }
                    ScalerChanged = true;
                }
                else ScalerChanged = false;
            }

            EntSlotTick = Tick % (180 / EntSlotScaler) == 0;

            var shieldsWaking = 0;
            var entsUpdated = 0;
            var entsremoved = 0;
            var entsLostShield = 0;
            if (EntSlotTick)
            {
                if (++RefreshCycle >= EntSlotScaler) RefreshCycle = 0;

                if (Enforced.Debug == 4) SlotCounting();

                GlobalEntTmp.Clear();
                GlobalEntTmp.AddRange(GlobalProtect.Where(info => !MoreThan600Frames || info.Value.RefreshSlot == RefreshCycle && EntSlotTick || info.Value.RefreshSlot > EntSlotScaler - 1));
                for (int i = 0; i < GlobalEntTmp.Count; i++)
                {
                    var ent = GlobalEntTmp[i].Key;
                    var myProtector = GlobalEntTmp[i].Value;
                    var entShields = myProtector.Shields.Keys;

                    var refreshCount = 0;
                    foreach (var s in entShields)
                    {
                        if (s.LogicPaused) continue;

                        if (!ent.InScene || !s.ResetEnts(ent, Tick))
                        {
                            myProtector.Shields.Remove(s);
                            entsLostShield++;
                        }
                        else refreshCount++;

                        var detectedStates = !MoreThan600Frames || s.PlayerByShield || s.MoverByShield || Tick <= s.LastWokenTick + 580;
                        if (ScalerChanged || s.ProtectedEntCache.ContainsKey(ent) && detectedStates)
                        {
                            s.Asleep = false;
                            shieldsWaking++;
                        }
                    }
                    if (entsLostShield > 0) myProtector.Shields.ApplyChanges();

                    if (refreshCount == 0)
                    {
                        GlobalProtect.Remove(ent);
                        entsremoved++;
                    }
                    else entsUpdated++;
                }
                if (Enforced.Debug == 4) Log.Line($"[NewRefresh] SlotScaler:{EntSlotScaler} - EntsUpdated:{entsUpdated} - ShieldsWaking:{shieldsWaking} - EntsRemoved: {entsremoved} - EntsLostShield:{entsLostShield} - EntInRefreshSlots:({SlotCnt[0]} - {SlotCnt[1]} - {SlotCnt[2]} - {SlotCnt[3]} - {SlotCnt[4]} - {SlotCnt[5]} - {SlotCnt[6]} - {SlotCnt[7]} - {SlotCnt[8]})");
            }
        }

        private void LogicUpdates()
        {
            var y = 0;
            if (Enforced.Debug >= 4 && EntSlotTick) Log.Line($"[ShieldStates] ActiveShields:{ActiveShields.Count} - FunctionalShields:{FunctionalShields.Count} - AllControllerBlocks:{Controllers.Count}");
            if (Enforced.Debug >= 4 && EntSlotTick) Dsutil1.Sw.Restart();
            if (Enforced.Debug == 1 && Tick1800) Log.Line($"[ShieldStates] ActiveShields:{ActiveShields.Count} - FunctionalShields:{FunctionalShields.Count} - AllControllerBlocks:{Controllers.Count}");
            foreach (var s in ActiveShields)
            {
                if (!s.WasOnline || s.Asleep) continue;

                if (s.StaleGrids.Count != 0) s.CleanUp(0);
                if (Tick20 && s.EffectsCleanup) s.CleanUp(3);
                if (Tick180) s.CleanUp(2);
                if (Tick600) s.CleanUp(1);

                if (EntSlotTick && s.LogicSlot == RefreshCycle || s.ComingOnline || ScalerChanged) s.ProtectMyself();
                s.WebEntities();
                s.DeformEnabled = false;
                y++;
            }
            if (Enforced.Debug >= 4 && EntSlotTick) Dsutil1.StopWatchReport($"[Protecting] ProtectedEnts:{GlobalProtect.Count} - WakingShields:{y} - CPU:", -1);
            else if (Enforced.Debug >= 4) Dsutil1.Sw.Reset();

            var compCount = Controllers.Count;
            if (SphereOnCamera.Length != compCount) Array.Resize(ref SphereOnCamera, compCount);
        }

        private void SlotCounting()
        {
            for (int i = 0; i < 9; i++) SlotCnt[i] = 0;
            foreach (var k in GlobalProtect.Values)
            {
                switch (k.RefreshSlot)
                {
                    case 0:
                        SlotCnt[0]++;
                        break;
                    case 1:
                        SlotCnt[1]++;
                        break;
                    case 2:
                        SlotCnt[2]++;
                        break;
                    case 3:
                        SlotCnt[3]++;
                        break;
                    case 4:
                        SlotCnt[4]++;
                        break;
                    case 5:
                        SlotCnt[5]++;
                        break;
                    case 6:
                        SlotCnt[6]++;
                        break;
                    case 7:
                        SlotCnt[7]++;
                        break;
                    case 8:
                        SlotCnt[8]++;
                        break;
                }
            }
        }
    }
}