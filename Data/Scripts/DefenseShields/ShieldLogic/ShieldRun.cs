﻿using VRageMath;

namespace DefenseSystems
{
    using System;
    using Support;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Game.Entities;
    using Sandbox.ModAPI;
    using VRage.Game.Components;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "DSControlLarge", "DSControlSmall", "DSControlTable")]
    public partial class Controllers : MyGameLogicComponent
    {
        #region Simulation
        public override void OnAddedToContainer()
        {
            if (!_containerInited)
            {
                PowerPreInit();
                Shield = (IMyUpgradeModule)Entity;
                _containerInited = true;
            }

            if (Entity.InScene) OnAddedToScene();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            StorageSetup();
        }

        public override void OnAddedToScene()
        {
            try
            {
                if (!ResetEntity()) return;
                if (Session.Enforced.Debug == 3) Log.Line($"OnAddedToScene: GridId:{Shield.CubeGrid.EntityId} - ShieldId [{Shield.EntityId}]");
            }
            catch (Exception ex) { Log.Line($"Exception in OnAddedToScene: {ex}"); }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            try
            {
                if (!_bInit) BeforeInit();
                else if (!_aInit) AfterInit();
                else if (_bCount < SyncCount * _bTime)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    if (Bus.ActiveController != null && Bus.ActiveController.Warming) _bCount++;
                }
                else _readyToSync = true;

            }
            catch (Exception ex) { Log.Line($"Exception in Controller UpdateOnceBeforeFrame: {ex}"); }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (!EntityAlive()) return;
                var shield = ShieldOn();
                if (shield != State.Active)
                {
                    if (NotFailed)
                    {
                        if (Session.Enforced.Debug >= 2) Log.Line($"FailState: {shield} - ShieldId [{Shield.EntityId}]");
                        var up = shield != State.Lowered;
                        var awake = shield != State.Sleep;
                        var clear = up && awake;
                        OfflineShield(clear, up, shield);
                    }
                    else if (DsState.State.Message) ShieldChangeState();
                    return;
                }

                if (!_isServer || !DsState.State.Online) return;
                if (_comingOnline) ComingOnlineSetup();
                if (_mpActive && (_forceBufferSync || _count == 29))
                {
                    var newPercentColor = UtilsStatic.GetShieldColorFromFloat(DsState.State.ShieldPercent);
                    if (_forceBufferSync || newPercentColor != _oldPercentColor)
                    {
                        ShieldChangeState();
                        _oldPercentColor = newPercentColor;
                        _forceBufferSync = false;
                    }
                    else if (_tick1800) ShieldChangeState();
                }
                if (Session.Instance.EmpWork.EventRunning) AbsorbEmp();
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateBeforeSimulation: {ex}"); }
        }

        public override bool IsSerialized()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (Shield.Storage != null)
                {
                    DsState.SaveState();
                    DsSet.SaveSettings();
                }
            }
            return false;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (Entity.InScene) OnRemovedFromScene();
        }

        public override void OnRemovedFromScene()
        {
            try
            {
                if (!_allInited) return;
                if (Session.Enforced.Debug >= 3) Log.Line($"OnRemovedFromScene: {ShieldMode} - GridId:{Shield.CubeGrid.EntityId} - ShieldId [{Shield.EntityId}]");

                if (Bus != null && Bus.SubGrids.Contains(LocalGrid))
                {
                    if (Bus.ActiveController == this) OfflineShield(true, false, State.Other, true);
                    Registry.RegisterWithBus(this, LocalGrid, false, Bus, out Bus);
                }

                InitEntities(false);
                IsWorking = false;
                IsFunctional = false;
                _shellPassive?.Render?.RemoveRenderObjects();
                _shellActive?.Render?.RemoveRenderObjects();
                ShieldEnt?.Render?.RemoveRenderObjects();
            }
            catch (Exception ex) { Log.Line($"Exception in OnRemovedFromScene: {ex}"); }
        }

        public override void MarkForClose()
        {
            try
            {
                base.MarkForClose();
            }
            catch (Exception ex) { Log.Line($"Exception in MarkForClose: {ex}"); }
        }

        public override void Close()
        {
            try
            {
                base.Close();
                if (!_allInited) return;
                if (Session.Enforced.Debug >= 3) Log.Line($"Close: {ShieldMode} - ShieldId [{Shield.EntityId}]");

                if (Bus != null && Bus.SubGrids.Contains(LocalGrid))
                {
                    if (Bus.ActiveController == this) OfflineShield(true, false, State.Other, true);
                    Registry.RegisterWithBus(this, LocalGrid, false, Bus, out Bus);
                }

                if (Session.Instance.AllControllers.Contains(this)) Session.Instance.AllControllers.Remove(this);
                bool value1;

                if (Session.Instance.FunctionalShields.ContainsKey(this)) Session.Instance.FunctionalShields.TryRemove(this, out value1);

                Icosphere = null;
                InitEntities(false);
                MyAPIGateway.Session.OxygenProviderSystem.RemoveOxygenGenerator(_ellipsoidOxyProvider);
            }
            catch (Exception ex) { Log.Line($"Exception in Close: {ex}"); }
        }
        #endregion
    }
}