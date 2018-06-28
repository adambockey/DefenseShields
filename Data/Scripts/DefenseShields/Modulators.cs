﻿using System;
using System.Collections.Generic;
using DefenseShields.Control;
using DefenseShields.Support;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace DefenseShields
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "LargeShieldModulator", "SmallShieldModulator")]
    public class Modulators : MyGameLogicComponent
    {

        public bool ServerUpdate;
        private bool _hierarchyDelayed;

        internal bool MainInit;

        private uint _tick;
        private uint _hierarchyTick = 1;

        private int _count = -1;
        private int _lCount;

        private readonly Dictionary<long, Modulators> _modulators = new Dictionary<long, Modulators>();

        public MyModStorageComponentBase Storage { get; set; }
        internal ModulatorSettings Settings = new ModulatorSettings();
        internal ModulatorGridComponent ModulatorComp;
        internal ShieldGridComponent ShieldComp;

        private IMyUpgradeModule Modulator => (IMyUpgradeModule)Entity;
        private RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyUpgradeModule> _modulateVoxels;
        private RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyUpgradeModule> _modulateGrids;

        internal DSUtils Dsutil1 = new DSUtils();

        public Modulators()
        {
            ModulatorComp = new ModulatorGridComponent(this);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            try
            {
                base.Init(objectBuilder);
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

                Modulator.CubeGrid.Components.Add(ModulatorComp);
                Session.Instance.Modulators.Add(this);
                if (!_modulators.ContainsKey(Entity.EntityId)) _modulators.Add(Entity.EntityId, this);
                CreateUi();
                StorageSetup();
                ((MyCubeGrid)Modulator.CubeGrid).OnHierarchyUpdated += HierarchyChanged;
            }
            catch (Exception ex) { Log.Line($"Exception in EntityInit: {ex}"); }
        }

        private void StorageSetup()
        {
            Storage = Modulator.Storage;
            LoadSettings();
            UpdateSettings(Settings, false);
        }

        private void HierarchyChanged(IMyCubeGrid myCubeGrid = null)
        {
            try
            {
                if (_tick == _hierarchyTick || ShieldComp?.DefenseShields != null) return;
                if (_hierarchyTick > _tick - 9)
                {
                    _hierarchyDelayed = true;
                    return;
                }
                _hierarchyTick = _tick;
                var gotGroups = MyAPIGateway.GridGroups.GetGroup(Modulator?.CubeGrid, GridLinkTypeEnum.Mechanical);
                ModulatorComp?.GetSubGrids?.Clear();
                for (int i = 0; i < gotGroups.Count; i++) ModulatorComp?.GetSubGrids?.Add(gotGroups[i]);
            }
            catch (Exception ex) { Log.Line($"Exception in HierarchyChanged: {ex}"); }
        }

        public override void UpdateBeforeSimulation()
        {
            _tick = (uint)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds / MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS;
            Timing();

            if (!MainInit)
            {
                Modulator.CubeGrid.Components.TryGet(out ShieldComp);
                if (ShieldComp == null) return;
                SyncControlsClient();
                ShieldComp.DefenseShields?.GetModulationInfo();
                MainInit = true;
                if (Session.Enforced.Debug == 1) Log.Line($"Modulator initted");
            }
            if (ShieldComp?.GetSubGrids != null && !ShieldComp.GetSubGrids.Equals(ModulatorComp.GetSubGrids))
                ModulatorComp.GetSubGrids = ShieldComp.GetSubGrids;
        }

        public override void UpdateBeforeSimulation100()
        {
            if (!MainInit) return;
            _tick = (uint)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds / MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS;

            if (ServerUpdate) SyncMisc();
            SyncControlsClient();

            if (Modulator.CustomData != ModulatorComp.ModulationPassword)
            {
                ModulatorComp.ModulationPassword = Modulator.CustomData;
                SaveSettings();
                if (Session.Enforced.Debug == 1) Log.Line($"Updating modulator password");
            }
        }

        private void Timing()
        {
            if (_count++ == 59)
            {
                _count = 0;
                _lCount++;
                if (_lCount == 10) _lCount = 0;
            }

            if (_hierarchyDelayed && _tick > _hierarchyTick + 9)
            {
                if (Session.Enforced.Debug == 1) Log.Line($"Delayed tick: {_tick} - hierarchytick: {_hierarchyTick}");
                _hierarchyDelayed = false;
                HierarchyChanged();
            }
        }

        #region Create UI
        private void CreateUi()
        {
            //if (Session.Instance.ModulatorControlsLoaded) return; // fix get existing controls
            Session.Instance.ModulatorControlsLoaded = true;
            _modulateVoxels = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyUpgradeModule>(Modulator, "AllowVoxels", "Voxels may pass", true);
            _modulateGrids = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyUpgradeModule>(Modulator, "AllowGrids", "Grids may pass", false);
        }
        #endregion

        #region Settings
        public bool Enabled
        {
            get { return Settings.Enabled; }
            set { Settings.Enabled = value; }
        }

        public bool ModulateVoxels
        {
            get { return Settings.ModulateVoxels; }
            set { Settings.ModulateVoxels = value; }
        }

        public bool ModulateGrids
        {
            get { return Settings.ModulateGrids; }
            set { Settings.ModulateGrids = value; }
        }

        public void UpdateSettings(ModulatorSettings newSettings, bool localOnly = true)
        {
            Enabled = newSettings.Enabled;
            ModulatorComp.Enabled = newSettings.Enabled;
            ModulateVoxels = newSettings.ModulateVoxels;
            ModulatorComp.Voxels = newSettings.ModulateVoxels;
            ModulateGrids = newSettings.ModulateGrids;
            ModulatorComp.Grids = newSettings.ModulateGrids;
            if (Session.Enforced.Debug == 1) Log.Line($"UpdateSettings for modulator");
        }

        public void SaveSettings()
        {
            if (Modulator.Storage == null)
            {
                Log.Line($"ModulatorId:{Modulator.EntityId.ToString()} - Storage = null");
                Modulator.Storage = new MyModStorageComponent();
            }
            Modulator.Storage[Session.Instance.ModulatorGuid] = MyAPIGateway.Utilities.SerializeToXML(Settings);
        }

        public bool LoadSettings()
        {
            if (Modulator.Storage == null) return false;

            string rawData;
            bool loadedSomething = false;

            if (Modulator.Storage.TryGetValue(Session.Instance.ModulatorGuid, out rawData))
            {
                ModulatorSettings loadedSettings = null;

                try
                {
                    loadedSettings = MyAPIGateway.Utilities.SerializeFromXML<ModulatorSettings>(rawData);
                }
                catch (Exception e)
                {
                    loadedSettings = null;
                    Log.Line($"ModulatorId:{Modulator.EntityId.ToString()} - Error loading settings!\n{e}");
                }

                if (loadedSettings != null)
                {
                    Settings = loadedSettings;
                    loadedSomething = true;
                }
            }
            return loadedSomething;
        }

        private void SyncMisc()
        {
            if (Modulator != null && !Modulator.Enabled.Equals(Settings.Enabled))
            {
                Enabled = Settings.Enabled;
                ModulatorComp.Enabled = Settings.Enabled;
            }

            if (_modulateVoxels != null && !_modulateVoxels.Getter(Modulator).Equals(Settings.ModulateVoxels))
            {
                _modulateVoxels.Setter(Modulator, Settings.ModulateVoxels);
                ModulatorComp.Voxels = Settings.ModulateVoxels;
            }

            if (_modulateGrids != null && !_modulateGrids.Getter(Modulator).Equals(Settings.ModulateGrids))
            {
                _modulateGrids.Setter(Modulator, Settings.ModulateGrids);
                ModulatorComp.Grids = Settings.ModulateGrids;
            }

            ServerUpdate = false;
            SaveSettings();
            if (Session.Enforced.Debug == 1) Log.Line($"SyncMisc (modulator)");
        }

        private void SyncControlsClient()
        {
            var needsSync = false;
            if (!Enabled.Equals(Enabled) 
                || !_modulateVoxels.Getter(Modulator).Equals(ModulateVoxels)
                || !_modulateGrids.Getter(Modulator).Equals(ModulateGrids))
            {
                needsSync = true;
                Enabled = Settings.Enabled;
                ModulatorComp.Enabled = Settings.Enabled;
                ModulateVoxels = _modulateVoxels.Getter(Modulator);
                ModulatorComp.Voxels = _modulateVoxels.Getter(Modulator);
                ModulateGrids = _modulateGrids.Getter(Modulator);
                ModulatorComp.Grids = _modulateGrids.Getter(Modulator);
            }

            if (needsSync)
            {
                NetworkUpdate();
                SaveSettings();
                if (Session.Enforced.Debug == 1) Log.Line($"Needed sync for modulator");
            }
        }
        #endregion

        #region Network
        private void NetworkUpdate()
        {

            if (Session.IsServer)
            {
                if (Session.Enforced.Debug == 1) Log.Line($"server relaying network settings update for modulator {Modulator.EntityId}");
                Session.PacketizeModulatorSettings(Modulator, Settings); // update clients with server's settings
            }
            else // client, send settings to server
            {
                if (Session.Enforced.Debug == 1) Log.Line($"client sent network settings update for modulator {Modulator.EntityId}");
                var bytes = MyAPIGateway.Utilities.SerializeToBinary(new ModulatorData(MyAPIGateway.Multiplayer.MyId, Modulator.EntityId, Settings));
                MyAPIGateway.Multiplayer.SendMessageToServer(Session.PACKET_ID_MODULATOR, bytes);
            }
        }
        #endregion
        public override void OnRemovedFromScene()
        {
            try
            {
                //if (_modulators.ContainsKey(Modulator.EntityId)) _modulators.Remove(Modulator.EntityId);
                //Modulator?.CubeGrid.Components.Remove(typeof(ModulatorGridComponent), this);
                //if (Session.Instance.Modulators.Contains(this)) Session.Instance.Modulators.Remove(this);
            }
            catch (Exception ex) { Log.Line($"Exception in OnRemovedFromScene: {ex}"); }
        }

        public override void OnBeforeRemovedFromContainer() { if (Entity.InScene) OnRemovedFromScene(); }
        public override void Close()
        {
            try
            {
                //Modulator?.CubeGrid.Components.Remove(typeof(ModulatorGridComponent), this);
                //if (Session.Instance.Modulators.Contains(this)) Session.Instance.Modulators.Remove(this);
            }
            catch (Exception ex) { Log.Line($"Exception in Close: {ex}"); }
            base.Close();
        }

        public override void MarkForClose()
        {
            try
            {
            }
            catch (Exception ex) { Log.Line($"Exception in MarkForClose: {ex}"); }
            base.MarkForClose();
        }
        public override void OnAddedToContainer() { if (Entity.InScene) OnAddedToScene(); }
    }
}
