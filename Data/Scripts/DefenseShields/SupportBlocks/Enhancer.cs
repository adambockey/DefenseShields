﻿namespace DefenseShields
{
    using System;
    using System.Text;
    using Support;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Game.Entities;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.Entity;
    using VRage.Game.ModAPI;
    using VRage.Game.ObjectBuilders.Definitions;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;
    using VRage.Utils;
    using VRageMath;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "LargeEnhancer", "SmallEnhancer")]
    public class Enhancers : MyGameLogicComponent
    {
        internal ShieldGridComponent ShieldComp;
        internal MyResourceSinkInfo ResourceInfo;

        private const float Power = 0.01f;
        private const int SyncCount = 60;

        private readonly MyDefinitionId _gId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        private uint _tick;
        private int _count = -1;
        private int _bCount;
        private int _bTime;
        private bool _firstLoop = true;
        private bool _readyToSync;
        private bool _firstSync;
        private bool _tick60;
        private bool _isServer;
        private bool _isDedicated;
        private bool _bInit;

        private MyEntitySubpart _subpartRotor;

        internal EnhancerState EnhState { get; set; }
        internal MyResourceSinkComponent Sink { get; set; }

        internal bool InControlPanel => MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel;
        internal bool InThisTerminal => Session.Instance.LastTerminalId == Enhancer.EntityId;

        internal int RotationTime { get; set; }
        internal bool ContainerInited { get; set; }
        internal bool IsFunctional { get; set; }
        internal bool IsWorking { get; set; }
        internal IMyUpgradeModule Enhancer { get; set; }
        internal MyCubeGrid MyGrid { get; set; }
        internal MyCubeBlock MyCube { get; set; }

        public override void OnAddedToContainer()
        {
            if (!ContainerInited)
            {
                PowerPreInit();
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                if (!MyAPIGateway.Utilities.IsDedicated) NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
                else NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                Enhancer = (IMyUpgradeModule)Entity;
                ContainerInited = true;
                if (Session.Enforced.Debug == 3) Log.Line($"ContainerInited:  EnhancerId [{Enhancer.EntityId}]");
            }
            if (Entity.InScene) OnAddedToScene();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            try
            {
                base.Init(objectBuilder);
                StorageSetup();
            }
            catch (Exception ex) { Log.Line($"Exception in EntityInit: {ex}"); }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            try
            {
                if (!_bInit) BeforeInit();
                else if (_bCount < SyncCount * _bTime)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    if (ShieldComp?.DefenseShields?.MyGrid == MyGrid) _bCount++;
                }
                else _readyToSync = true;
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateOnceBeforeFrame: {ex}"); }
        }

        private void BeforeInit()
        {
            if (Enhancer.CubeGrid.Physics == null) return;
            Session.Instance.Enhancers.Add(this);
            PowerInit();
            Entity.TryGetSubpart("Rotor", out _subpartRotor);
            _isServer = Session.Instance.IsServer;
            _isDedicated = Session.Instance.DedicatedServer;
            Enhancer.RefreshCustomInfo();
            IsWorking = MyCube.IsWorking;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            _bTime = _isDedicated ? 10 : 1;
            _bInit = true;
        }

        public override bool IsSerialized()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (Enhancer.Storage != null) EnhState.SaveState();
            }
            return false;
        }

        public override void OnAddedToScene()
        {
            try
            {
                MyGrid = (MyCubeGrid)Enhancer.CubeGrid;
                MyCube = Enhancer as MyCubeBlock;
                RegisterEvents();
                if (Session.Enforced.Debug == 3) Log.Line($"OnAddedToScene: - EnhancerId [{Enhancer.EntityId}]");
            }
            catch (Exception ex) { Log.Line($"Exception in OnAddedToScene: {ex}"); }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                _tick = Session.Instance.Tick;
                _tick60 = _tick % 60 == 0;
                var wait = _isServer && !_tick60 && EnhState.State.Backup;

                MyGrid = MyCube.CubeGrid;
                if (wait || MyGrid?.Physics == null) return;

                Timing();
                if (!EnhancerReady()) return;
                if (!_isDedicated && UtilsStatic.DistanceCheck(Enhancer, 1000, 1))
                {
                    var blockCam = MyCube.PositionComp.WorldVolume;
                    if (MyAPIGateway.Session.Camera.IsInFrustum(ref blockCam) && EnhState.State.Online) BlockMoveAnimation();
                }
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateBeforeSimulation: {ex}"); }
        }

        public override void UpdateBeforeSimulation10()
        {
            try
            {
                _tick = Session.Instance.Tick;
                if (_count++ == 5) _count = 0;
                var wait = _isServer && _count != 0 && EnhState.State.Backup;

                MyGrid = MyCube.CubeGrid;
                if (wait || MyGrid?.Physics == null) return;

                EnhancerReady();
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateBeforeSimulation10: {ex}"); }
        }

        public override void OnRemovedFromScene()
        {
            try
            {
                if (Session.Instance.Enhancers.Contains(this)) Session.Instance.Enhancers.Remove(this);
                if (ShieldComp?.Enhancer == this)
                {
                    ShieldComp.Enhancer = null;
                }
                RegisterEvents(false);

                IsWorking = false;
                IsFunctional = false;
            }
            catch (Exception ex) { Log.Line($"Exception in OnRemovedFromScene: {ex}"); }
        }

        public override void Close()
        {
            try
            {
                base.Close();
                if (Session.Instance.Enhancers.Contains(this)) Session.Instance.Enhancers.Remove(this);
                if (ShieldComp?.Enhancer == this)
                {
                    ShieldComp.Enhancer = null;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in Close: {ex}"); }
        }

        public override void MarkForClose()
        {
            try
            {
                base.MarkForClose();
            }
            catch (Exception ex) { Log.Line($"Exception in MarkForClose: {ex}"); }
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (Entity.InScene) OnRemovedFromScene();
        }

        internal void UpdateState(EnhancerStateValues newState)
        {
            if (newState.MId > EnhState.State.MId)
            {
                EnhState.State = newState;
                if (Session.Enforced.Debug >= 3) Log.Line($"UpdateState: EnhancerId [{Enhancer.EntityId}]");
            }
        }

        private void SaveAndSendAll()
        {
            _firstSync = true;
            if (!_isServer) return;
            EnhState.SaveState();
            EnhState.NetworkUpdate();
            if (Session.Enforced.Debug >= 3) Log.Line($"SaveAndSendAll: EnhancerId [{Enhancer.EntityId}]");
        }

        private void Timing()
        {
            if (_count++ == 59) _count = 0;

            if (_count == 29 && !_isDedicated)
            {
                TerminalRefresh(true);
            }
        }

        private bool EnhancerReady()
        {
            if (_subpartRotor == null)
            {
                Entity.TryGetSubpart("Rotor", out _subpartRotor);
                if (_subpartRotor == null) return false;
            }

            if (ShieldComp?.DefenseShields?.MyGrid != MyGrid) MyGrid.Components.TryGet(out ShieldComp);
            if (_isServer)
            {
                if (!_firstSync && _readyToSync) SaveAndSendAll();

                if (!BlockWorking()) return false;
            }
            else
            {
                if (ShieldComp?.DefenseShields == null) return false;

                if (!EnhState.State.Backup && ShieldComp.Enhancer != this) ShieldComp.Enhancer = this;

                if (!EnhState.State.Online) return false;
            }

            return BlockMoveAnimationReset();
        }

        private bool BlockWorking()
        {
            if (!IsWorking || ShieldComp?.DefenseShields == null)
            {
                NeedUpdate(EnhState.State.Online, false);
                return false;
            }

            if (ShieldComp.Enhancer != this)
            {
                if (ShieldComp.Enhancer == null)
                {
                    Session.Instance.BlockTagActive(Enhancer);
                    ShieldComp.Enhancer = this;
                    EnhState.State.Backup = false;
                }
                else if (ShieldComp.Enhancer != this)
                {
                    if (!EnhState.State.Backup || _firstLoop) Session.Instance.BlockTagBackup(Enhancer);
                    EnhState.State.Backup = true;
                    EnhState.State.Online = false;
                }
            }
            _firstLoop = false;
            if (!EnhState.State.Backup && ShieldComp.Enhancer == this && ShieldComp.DefenseShields.NotFailed)
            {
                NeedUpdate(EnhState.State.Online, true);
                return true;
            }

            NeedUpdate(EnhState.State.Online, false);

            return false;
        }

        private void NeedUpdate(bool onState, bool turnOn)
        {
            if (!onState && turnOn)
            {
                EnhState.State.Online = true;
                EnhState.SaveState();
                EnhState.NetworkUpdate();
                if (!_isDedicated) Enhancer.RefreshCustomInfo();
            }
            else if (onState & !turnOn)
            {
                EnhState.State.Online = false;
                EnhState.SaveState();
                EnhState.NetworkUpdate();
                if (!_isDedicated) Enhancer.RefreshCustomInfo();
            }
        }

        private void StorageSetup()
        {
            if (EnhState == null) EnhState = new EnhancerState(Enhancer);
            EnhState.StorageInit();
            EnhState.LoadState();
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                EnhState.State.Backup = false;
                EnhState.State.Online = false;
            }
        }

        private void PowerPreInit()
        {
            try
            {
                if (Sink == null)
                {
                    Sink = new MyResourceSinkComponent();
                }

                ResourceInfo = new MyResourceSinkInfo()
                {
                    ResourceTypeId = _gId,
                    MaxRequiredInput = 0.02f,
                    RequiredInputFunc = () => Power
                };

                Sink.Init(MyStringHash.GetOrCompute("Utility"), ResourceInfo);
                Sink.AddType(ref ResourceInfo);
                Entity.Components.Add(Sink);
            }
            catch (Exception ex) { Log.Line($"Exception in PowerPreInit: {ex}"); }
        }

        private void PowerInit()
        {
            try
            {
                var enableState = Enhancer.Enabled;

                if (enableState)
                {
                    Enhancer.Enabled = false;
                    Enhancer.Enabled = true;
                }

                Sink.Update();
                if (Session.Enforced.Debug == 3) Log.Line($"PowerInit: EnhancerId [{Enhancer.EntityId}]");
            }
            catch (Exception ex) { Log.Line($"Exception in AddResourceSourceComponent: {ex}"); }
        }

        private bool BlockMoveAnimationReset()
        {
            if (!IsFunctional) return false;

            if (_subpartRotor == null)
            {
                return Entity.TryGetSubpart("Rotor", out _subpartRotor);
            }

            if (!_subpartRotor.Closed) return true;

            _subpartRotor.Subparts.Clear();
            return Entity.TryGetSubpart("Rotor", out _subpartRotor);
        }

        private void BlockMoveAnimation()
        {
            if (!BlockMoveAnimationReset()) return;
            RotationTime -= 1;
            var rotationMatrix = MatrixD.CreateRotationY(0.05f * RotationTime);
            _subpartRotor.PositionComp.LocalMatrix = rotationMatrix;
        }

        internal void TerminalRefresh(bool update = true)
        {
            Enhancer.RefreshCustomInfo();
            if (update && InControlPanel && InThisTerminal)
            {
                MyCube.UpdateTerminal();
            }
        }

        private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder stringBuilder)
        {
            if (ShieldComp?.DefenseShields == null)
            {
                stringBuilder.Append("[Controller Link]: False");
            }
            else if (!EnhState.State.Backup && ShieldComp.DefenseShields.ShieldMode == DefenseShields.ShieldType.Station)
            {
                stringBuilder.Append("[Online]: " + EnhState.State.Online +
                                     "\n" +
                                     "\n[Amplifying Shield]: " + EnhState.State.Online +
                                     "\n[Enhancer Mode]: Fortress" +
                                     "\n[Bonsus] MaxHP, Repel Grids");
            }
            else if (!EnhState.State.Backup)
            {
                stringBuilder.Append("[Online]: " + EnhState.State.Online +
                                     "\n" +
                                     "\n[Shield Detected]: " + EnhState.State.Online +
                                     "\n[Enhancer Mode]: EMP Option");
            }
            else
            {
                stringBuilder.Append("[Backup]: " + EnhState.State.Backup);
            }
        }

        private void RegisterEvents(bool register = true)
        {
            if (register)
            {
                Enhancer.AppendingCustomInfo += AppendingCustomInfo;
                MyCube.IsWorkingChanged += IsWorkingChanged;
                IsWorkingChanged(MyCube);
            }
            else
            {
                Enhancer.AppendingCustomInfo -= AppendingCustomInfo;
                MyCube.IsWorkingChanged -= IsWorkingChanged;
            }
        }

        private void IsWorkingChanged(MyCubeBlock myCubeBlock)
        {
            IsFunctional = myCubeBlock.IsFunctional;
            IsWorking = myCubeBlock.IsWorking;
        }
    }
}
