﻿using Sandbox.Game;
using VRageMath;
using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using System.Linq;
using DefenseShields.Support;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Voxels;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace DefenseShields
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OreDetector), false, "DSControlStation", "DSControlLarge", "DSControlSmall")]
    public partial class DefenseShields : MyGameLogicComponent
    {
        #region Simulation
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (Session.Enforced.Debug == 1) Dsutil1.Sw.Restart();
                _tick = (uint)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds / MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS;
                if (!BlockFunctional()) return;

                if (ServerUpdate) SyncControlsServer();
                
                if (_gridIsMobile) MobileUpdate();
                else _shapeAdjusted = false;
                if (_updateDimensions) RefreshDimensions();

                if (_fitChanged || _lCount == 0 && _count == 0 && _blocksChanged)
                {
                    _oldEllipsoidAdjust = _ellipsoidAdjust;
                    _fitChanged = false;

                    if (_gridIsMobile)
                    {
                        CreateHalfExtents();
                        if (_shapeAdjusted) _shapeLoaded = true;
                        else if (_shapeLoaded) MyAPIGateway.Parallel.StartBackground(GetShapeAdjust);
                    }

                    if (_blocksChanged)
                    {
                        ShieldComp.CheckEmitters = true;
                        MyAPIGateway.Parallel.StartBackground(BackGroundChecks);
                        _blocksChanged = false;
                    } 
                }

                ShieldComp.ShieldActive = ControlBlockWorking && !ShieldOffline;
                if (_prevShieldActive == false && ShieldComp.ShieldActive) ShieldComp.ComingOnline = true;
                else if (ShieldComp.ComingOnline && _prevShieldActive && ShieldComp.ShieldActive) ShieldComp.ComingOnline = false;
                _prevShieldActive = ShieldComp.ShieldActive;

                Timing();
                UpdateGridPower();
                CalculatePowerCharge();
                SetPower();
                if (_count == 29)
                {
                    if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
                    {
                        Shield.ShowInToolbarConfig = false;
                        Shield.ShowInToolbarConfig = true;
                    }
                    else if (_lCount == 0 || _lCount == 5)
                    {
                        Shield.RefreshCustomInfo();
                    }
                    _shieldDps = 0f;
                }
                if (ShieldComp.ShieldActive)
                {
                    //var def = MyDefinitionManager.Static.GetCubeBlockDefinition(new MyDefinitionId(typeof(MyObjectBuilder_OreDetector), "DSControlLarge"));
                    //var test = MyCubeBuilder.Static.CubeBuilderState.CurrentBlockDefinition;
                    //if (test != null && test != def)
                        //test = MyCubeBuilder.Static.CubeBuilderState.CurrentBlockDefinition = def;
                    //Log.Line($"selected block {test?.BlockPairName}");
                    //Shield.SetEmissiveParts("iconlock", Color.Transparent, 0f);
                    //Shield.SetEmissiveParts("iconshieldrear", Color.Black, 0f);
                    if (_lCount % 2 != 0 && _count == 20)
                    {
                        GetModulationInfo();
                        if (_reModulationLoop > -1) return;
                    }
                    if (ShieldComp.ComingOnline)
                    {
                        if (ShieldComp.ComingOnline && _gridIsMobile && FieldShapeBlocked()) return;
                        if (!_hidePassiveCheckBox.Getter(Shield).Equals(true)) _shellPassive.Render.UpdateRenderObject(true);

                        _shellActive.Render.UpdateRenderObject(true);
                        _shellActive.Render.UpdateRenderObject(false);
                        _shield.Render.Visible = true;
                        _shield.Render.UpdateRenderObject(true);
                        SyncThreadedEnts(true);
                        if (!_gridIsMobile) EllipsoidOxyProvider.UpdateMatrix(_detectMatrixOutsideInv);
                        if (!WarmedUp) 
                        {
                            WarmedUp = true;
                            if (Session.Enforced.Debug == 1) Log.Line($"Warmup complete");
                            return;
                        }
                    }
                    SyncThreadedEnts();
                    _enablePhysics = false;
                    WebEntities();
                    if (!Session.DedicatedServer) HudCheck();
                }
                else
                {
                    SyncThreadedEnts();
                }
                if (Session.Enforced.Debug == 1) Dsutil1.StopWatchReport($"MainLoop: ShieldId:{Shield.EntityId.ToString()} - Active: {ShieldComp.ShieldActive} - Tick: {_tick} loop: {_lCount}-{_count}", 3);
            }
            catch (Exception ex) {Log.Line($"Exception in UpdateBeforeSimulation: {ex}"); }
        }

        private void Timing()
        {
            if (_count++ == 59)
            {
                _count = 0;
                _lCount++;
                if (_lCount == 10)
                {
                    _lCount = 0;
                    _eCount++;
                    if (_eCount == 10) _eCount = 0;
                }
            }

            if (_eCount == 0 && _lCount == 0 && _count == 0) _randomCount = _random.Next(0, 10);

            if (_staleGrids.Count != 0) CleanUp(0);
            if (_lCount == 9 && _count == 58) CleanUp(1);
            if (_effectsCleanup && (_count == 1 || _count == 21 || _count == 41)) CleanUp(2);
            if (_eCount == 0 && _lCount == _randomCount && _count == 15 && (Session.DedicatedServer || Session.IsServer)) DsSet.SaveSettings();
            if (_hierarchyDelayed && _tick > _hierarchyTick + 9)
            {
                if (Session.Enforced.Debug == 1) Log.Line($"Delayed tick: {_tick} - hierarchytick: {_hierarchyTick}");
                _hierarchyDelayed = false;
                HierarchyChanged();
            }

            if ((_lCount * 60 + _count + 1) % 150 == 0)
            {
                CleanUp(3);
                CleanUp(4);
            }
        }

        private bool BlockFunctional()
        {

            if (ShieldComp.DefenseShields == null) ShieldComp.DefenseShields = this;
            if (!AllInited || ShieldComp.DefenseShields != this || !WarmUpSequence()) return false;

            if (_shieldDownLoop > -1 || _reModulationLoop > -1 || _genericDownLoop > -1)
            {
                FailureConditions();
                return false;
            }

            if (ShieldComp.EmitterEvent)
            {
                if (!_gridIsMobile && ShieldComp?.EmitterComp?.PrimeComp != null)
                {
                    _updateDimensions = true;
                    RefreshDimensions();
                }
                ShieldComp.EmitterEvent = false;
                if (!ShieldComp.EmittersWorking) _genericDownLoop = 0;
            }

            if (!Shield.IsWorking || !Shield.IsFunctional || !ShieldComp.EmittersWorking)
            {
                _genericDownLoop = 0;
                return false;
            }

            if (_lCount == 4 && _count == 4 && Shield.Enabled && ConnectCheck()) return false;

            var blockCount = BlockCount();
            if (!_blocksChanged) _blocksChanged = blockCount != _oldBlockCount;
            _oldBlockCount = blockCount;
            ControlBlockWorking = AllInited && Shield.IsWorking && Shield.IsFunctional;
            return ControlBlockWorking;
        }

        private bool WarmUpSequence()
        {
            if (ShieldComp.Warming) return true;

            if (ShieldComp.Starting)
            {
                EmitterGridComponent eComp;
                Shield.CubeGrid.Components.TryGet(out eComp);
                if (eComp != null)
                {
                    ShieldComp.EmitterComp = eComp;
                    if (_gridIsMobile)
                    {
                        CreateHalfExtents();
                        GetShapeAdjust();
                        MobileUpdate();
                    }
                    else
                    {
                        _updateDimensions = true;
                        RefreshDimensions();
                    }
                    _shapeAdjusted = false;
                    _blocksChanged = false;
                    ShieldComp.CheckEmitters = true;
                    Icosphere.ReturnPhysicsVerts(DetectionMatrix, ShieldComp.PhysicsOutside);
                    ShieldComp.Warming = true;
                    return false;
                }
                return false;
            }

            _hierarchyDelayed = false;
            HierarchyChanged();
            var blockCnt = BlockCount();
            if (!_blocksChanged) _blocksChanged = blockCnt != _oldBlockCount;
            _oldBlockCount = blockCnt;

            BackGroundChecks();
            UpdateGridPower();
            GetModulationInfo();
            ControlBlockWorking = AllInited && Shield.IsWorking && Shield.IsFunctional;
            if (Session.Enforced.Debug == 1) Log.Line($"start warmup enforced:\n{Session.Enforced}");
            if (Session.Enforced.Debug == 1) Log.Line($"start warmup buffer:{ShieldBuffer} - BlockWorking:{ControlBlockWorking} - Active:{ShieldComp.ShieldActive}");
            ShieldComp.Starting = true;
            return false;
        }

        private void HudCheck()
        {
            if (_tick % 60 != 0) return;

            var playerEnt = MyAPIGateway.Session.ControlledObject?.Entity;
            if (playerEnt?.Parent != null) playerEnt = playerEnt.Parent;
            if (playerEnt == null || !FriendlyCache.Contains(playerEnt))
            {
                if (Session.HudComp != this) return;

                Session.HudComp = null;
                Session.HudShieldDist = double.MaxValue;
                return;
            }

            var distFromShield = Vector3D.DistanceSquared(playerEnt.WorldVolume.Center, _detectionCenter);
            if (Session.HudComp != this && distFromShield <= Session.HudShieldDist)
            {
                Session.HudShieldDist = distFromShield;
                Session.HudComp = this;
            }
        }

        public void CreateHalfExtents()
        {
            var myAabb = Shield.CubeGrid.PositionComp.LocalAABB;
            var shieldGrid = Shield.CubeGrid;
            var expandedAabb = myAabb;
            foreach (var grid in ShieldComp.GetSubGrids)
            {
                if (grid != shieldGrid)
                {
                    var shieldMatrix = shieldGrid.WorldMatrixNormalizedInv;
                    var gQuaternion = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
                    var gOriBBoxD = new MyOrientedBoundingBox(grid.PositionComp.WorldAABB.Center, grid.PositionComp.LocalAABB.HalfExtents, gQuaternion);
                    gOriBBoxD.Transform(shieldMatrix);
                    expandedAabb.Include(gOriBBoxD.GetAABB());
                }
            }

            _expandedAabb = expandedAabb;
            _shieldFudge = 0f;
            if (SphereFit || FortifyShield)
            {
                var extend = ExtendFit ? 2 : 1;
                var fortify = FortifyShield ? 3 : 1;

                var size = expandedAabb.HalfExtents.Max() * fortify;
                _gridHalfExtents = new Vector3D(size, size, size);
                _shieldFudge = (shieldGrid.GridSize * 4 * extend);
            }
            else _gridHalfExtents = expandedAabb.HalfExtents;
        }

        private void GetShapeAdjust()
        {
            if (SphereFit || FortifyShield) _ellipsoidAdjust = 1f;
            else if (!ExtendFit) _ellipsoidAdjust = UtilsStatic.CreateNormalFit(Shield, _gridHalfExtents);
            else _ellipsoidAdjust = UtilsStatic.CreateExtendedFit(Shield, _gridHalfExtents);
        }

        public int BlockCount()
        {
            var blockCnt = 0;
            foreach (var subGrid in ShieldComp.GetSubGrids) blockCnt += ((MyCubeGrid)subGrid).BlocksCount;
            return blockCnt;
        }

        private void BackGroundChecks()
        {
            lock (_powerSources) _powerSources.Clear();
            lock (_functionalBlocks) _functionalBlocks.Clear();

            foreach (var block in ((MyCubeGrid)Shield.CubeGrid).GetFatBlocks())
            {
                lock (_functionalBlocks) if (block.IsFunctional) _functionalBlocks.Add(block);
                var source = block.Components.Get<MyResourceSourceComponent>();
                if (source == null) continue;
                foreach (var type in source.ResourceTypes)
                {
                    if (type != MyResourceDistributorComponent.ElectricityId) continue;
                    lock (_powerSources) _powerSources.Add(source);
                    break;
                }
            }
            if (Session.Enforced.Debug == 1) Log.Line($"ShieldId:{Shield.EntityId.ToString()} - powerCnt: {_powerSources.Count.ToString()}");
        }

        private bool ConnectCheck(bool startUp = false)
        {
            if (!Shield.Enabled) return true;

            var myGrid = Shield.CubeGrid;
            var myGridIsSub = false;
            if (startUp)
            {
                if (_connectedGrids.Count == 0) _connectedGrids = MyAPIGateway.GridGroups.GetGroup(myGrid, GridLinkTypeEnum.Physical);
                foreach (var grid in _connectedGrids)
                {
                    if (myGrid == grid) continue;
                    if (myGrid.PositionComp.WorldAABB.Volume <= grid.PositionComp.WorldAABB.Volume) return true;
                }
            }

            if (ShieldComp.GetSubGrids.Count <= 1) return false;
            if (startUp)
            {
                var gearLocked = MyAPIGateway.GridGroups.GetGroup(myGrid, GridLinkTypeEnum.NoContactDamage);
                if (gearLocked.Count > 0)
                {
                    Log.Line($"I am locked");
                    foreach (var grid in gearLocked)
                    {
                        if (grid != myGrid) Log.Line($"{grid.DisplayName}");
                    }
                }
                CreateHalfExtents();
            }

            foreach (var grid in ShieldComp.GetSubGrids)
            {
                if (grid == myGrid) continue;
                if (myGrid.PositionComp.WorldAABB.Volume < grid.PositionComp.WorldAABB.Volume)
                {
                    myGridIsSub = true;
                    break;
                }
            }

            if (myGridIsSub)
            {
                var realPlayerIds = new List<long>();
                UtilsStatic.GetRealPlayers(Shield.PositionComp.WorldVolume.Center, 500f, realPlayerIds);
                foreach (var id in realPlayerIds)
                {
                    MyVisualScriptLogicProvider.ShowNotification("[ " + Shield.CubeGrid.DisplayName + " ]" + " -- primary grid is connected to much larger body, powering shield down.", 4800, "White", id);
                }
                Shield.Enabled = false;
            }
            return myGridIsSub;
        }
        #endregion

        private void OfflineShield()
        {
            ShieldOffline = true;
            _power = 0.0001f;
            Sink.Update();
            _shieldCurrentPower = Sink.CurrentInputByType(GId);
            UpdateGridPower();

            if (!_gridIsMobile) EllipsoidOxyProvider.UpdateMatrix(MatrixD.Zero);
            ShieldComp.ShieldActive = false;
            _prevShieldActive = false;
            _shellPassive.Render.UpdateRenderObject(false);
            _shellActive.Render.UpdateRenderObject(false);
            _shield.Render.Visible = false;
            _shield.Render.UpdateRenderObject(false);
            Absorb = 0;
            ShieldBuffer = 0;
            _shieldChargeRate = 0;
            _shieldMaxChargeRate = 0;
            _shieldMaxBuffer = 0;
            Shield.ShowInToolbarConfig = false;
            Shield.ShowInToolbarConfig = true;

            CleanUp(0);
            CleanUp(1);
            CleanUp(3);
            CleanUp(4);
        }

        private void FailureConditions()
        {
            if (!ShieldOffline) ShieldComp.CheckEmitters = true;

            if (_shieldDownLoop == 0 || _reModulationLoop == 0)
            {
                if (!ShieldOffline) OfflineShield();
                var realPlayerIds = new List<long>();
                UtilsStatic.GetRealPlayers(Shield.PositionComp.WorldVolume.Center, 500f, realPlayerIds);
                foreach (var id in realPlayerIds)
                {
                    if (_shieldDownLoop == 0) MyVisualScriptLogicProvider.ShowNotification("[ " + Shield.CubeGrid.DisplayName + " ]" + " -- shield has overloaded, restarting in 20 seconds!!", 19200, "Red", id);
                    if (_reModulationLoop == 0) MyVisualScriptLogicProvider.ShowNotification("[ " + Shield.CubeGrid.DisplayName + " ]" + " -- shield remodulating, restarting in 5 seconds.", 4800, "White", id);
                }

            }
            else if (_genericDownLoop == 0 && !ShieldOffline) OfflineShield();

            if (_reModulationLoop > -1)
            {
                _reModulationLoop++;
                if (_reModulationLoop == ReModulationCount)
                {
                    ShieldOffline = false;
                    _reModulationLoop = -1;
                    return;
                }
                return;
            }

            if (_genericDownLoop > -1)
            {
                _genericDownLoop++;
                if (_genericDownLoop == GenericDownCount)
                {
                    if (!ShieldComp.EmittersWorking) _genericDownLoop = 0;
                    else
                    {
                        ShieldOffline = false;
                        _genericDownLoop = -1;
                    }
                    return;
                }
                return;
            }

            _shieldDownLoop++;
            if (_shieldDownLoop == ShieldDownCount)
            {
                if (!ShieldComp.EmittersWorking) _genericDownLoop = 0;
                else
                {
                    ShieldOffline = false;
                    _shieldDownLoop = -1;
                }
                var nerf = Session.Enforced.Nerf > 0 && Session.Enforced.Nerf < 1;
                var nerfer = nerf ? Session.Enforced.Nerf : 1f;
                ShieldBuffer = (_shieldMaxBuffer / 25) * nerfer; // replace this with something that scales based on charge rate
            }
        }

        #region Field Check
        private bool FieldShapeBlocked()
        {
            if (ModulateVoxels) return false;

            var pruneSphere = new BoundingSphereD(_detectionCenter, ShieldComp.BoundingRange);
            var pruneList = new List<MyVoxelBase>();
            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref pruneSphere, pruneList);

            if (pruneList.Count == 0) return false;
            MobileUpdate();
            Icosphere.ReturnPhysicsVerts(_detectMatrixOutside, ShieldComp.PhysicsOutsideLow);
            foreach (var voxel in pruneList)
            {
                if (voxel.RootVoxel == null) continue;

                if (!CustomCollision.VoxelContact(Shield.CubeGrid, ShieldComp.PhysicsOutsideLow, voxel, new MyStorageData(), _detectMatrixOutside)) continue;

                Shield.Enabled = false;
                MyVisualScriptLogicProvider.ShowNotification("The shield's field cannot form when in contact with a solid body", 6720, "Blue", Shield.OwnerId);
                return true;
            }
            return false;
        }
        #endregion

        #region Shield Shape
        private void MobileUpdate()
        {
            ShieldComp.ShieldVelocitySqr = Shield.CubeGrid.Physics.LinearVelocity.LengthSquared();
            _sAvelSqr = Shield.CubeGrid.Physics.AngularVelocity.LengthSquared();
            if (ShieldComp.ShieldVelocitySqr > 0.00001 || _sAvelSqr > 0.00001 || ShieldComp.ComingOnline)
            {
                ShieldComp.GridIsMoving = true;
                if (FortifyShield && Math.Sqrt(ShieldComp.ShieldVelocitySqr) > 15)
                {
                    _fitChanged = true;
                    _fortifyShield.Setter(Shield, false);
                }
            }
            else ShieldComp.GridIsMoving = false;

            _shapeAdjusted = !_ellipsoidAdjust.Equals(_oldEllipsoidAdjust) || !_gridHalfExtents.Equals(_oldGridHalfExtents);
            _oldGridHalfExtents = _gridHalfExtents;
            _oldEllipsoidAdjust = _ellipsoidAdjust;
            _entityChanged = Shield.CubeGrid.Physics.IsMoving || ShieldComp.ComingOnline || _shapeAdjusted;
            if (_entityChanged || ShieldComp.BoundingRange <= 0 || ShieldComp.ComingOnline) CreateShieldShape();
        }

        private void CreateShieldShape()
        {
            if (_gridIsMobile)
            {
                _shieldGridMatrix = Shield.CubeGrid.WorldMatrix;
                if (_shapeAdjusted) CreateMobileShape();
                //DsDebugDraw.DrawSingleVec(_detectionCenter, 10f, Color.Blue);

                //_detectionCenter = Vector3D.Transform(_expandedAabb.Center, Shield.CubeGrid.PositionComp.WorldMatrix);
                //var newDir = Vector3D.TransformNormal(_expandedAabb.HalfExtents, Shield.CubeGrid.PositionComp.WorldMatrix);
                //_expandedMatrix = MatrixD.CreateFromTransformScale(_sQuaternion, _detectionCenter, newDir);
                //DetectionMatrix = _shieldShapeMatrix * _expandedMatrix;
                DetectionMatrix = _shieldShapeMatrix * _shieldGridMatrix;
                _detectionCenter = Shield.CubeGrid.PositionComp.WorldVolume.Center;
                _sQuaternion = Quaternion.CreateFromRotationMatrix(Shield.CubeGrid.WorldMatrix);
                _sOriBBoxD = new MyOrientedBoundingBoxD(_detectionCenter, ShieldSize, _sQuaternion);
                //_sOriBBoxD = new MyOrientedBoundingBoxD(_detectionCenter, _expandedAabb.HalfExtents, _sQuaternion);

                _shieldAabb = new BoundingBox(ShieldSize, -ShieldSize);
                _shieldSphere = new BoundingSphereD(Shield.PositionComp.LocalVolume.Center, ShieldSize.AbsMax());
                EllipsoidSa.Update(_detectMatrixOutside.Scale.X, _detectMatrixOutside.Scale.Y, _detectMatrixOutside.Scale.Z);
            }
            else
            {
                var emitter = ShieldComp.EmitterComp.PrimeComp.Emitter;
                _shieldGridMatrix = emitter.WorldMatrix;
                DetectionMatrix = MatrixD.Rescale(_shieldGridMatrix, new Vector3D(Width, Height, Depth));
                _shieldShapeMatrix = MatrixD.Rescale(emitter.LocalMatrix, new Vector3D(Width, Height, Depth));
                ShieldSize = DetectionMatrix.Scale;
                _detectionCenter = emitter.PositionComp.WorldVolume.Center;
                _sQuaternion = Quaternion.CreateFromRotationMatrix(emitter.CubeGrid.WorldMatrix);
                _sOriBBoxD = new MyOrientedBoundingBoxD(_detectionCenter, ShieldSize, _sQuaternion);
                _shieldAabb = new BoundingBox(ShieldSize, -ShieldSize);
                _shieldSphere = new BoundingSphereD(Shield.PositionComp.LocalVolume.Center, ShieldSize.AbsMax());
                EllipsoidSa.Update(_detectMatrixOutside.Scale.X, _detectMatrixOutside.Scale.Y, _detectMatrixOutside.Scale.Z);
            }
            ShieldComp.BoundingRange = ShieldSize.AbsMax() + 5f;
            _ellipsoidSurfaceArea = EllipsoidSa.Surface;
            SetShieldShape();
        }

        private void CreateMobileShape()
        {

            var shieldSize = _gridHalfExtents * _ellipsoidAdjust + _shieldFudge;
            ShieldSize = shieldSize;
            var mobileMatrix = MatrixD.CreateScale(shieldSize);
            mobileMatrix.Translation = Shield.CubeGrid.PositionComp.LocalVolume.Center;
            //mobileMatrix.Translation = _expandedAabb.Center;
            _shieldShapeMatrix = mobileMatrix;
        }

        private void SetShieldShape()
        {
            _shellPassive.PositionComp.LocalMatrix = Matrix.Zero;  // Bug - Cannot just change X coord, so I reset first.
            _shellActive.PositionComp.LocalMatrix = Matrix.Zero;
            _shield.PositionComp.LocalMatrix = Matrix.Zero;

            _shellPassive.PositionComp.LocalMatrix = _shieldShapeMatrix;
            _shellActive.PositionComp.LocalMatrix = _shieldShapeMatrix;
            _shield.PositionComp.LocalMatrix = _shieldShapeMatrix;
            _shield.PositionComp.LocalAABB = _shieldAabb;

            MatrixD matrix;
            if (!_gridIsMobile)
            {
                EllipsoidOxyProvider.UpdateMatrix(_detectMatrixOutsideInv);
                matrix = _shieldShapeMatrix * ShieldComp.EmitterComp.PrimeComp.Emitter.WorldMatrix;
                _shield.PositionComp.SetWorldMatrix(matrix);
                _shield.PositionComp.SetPosition(_detectionCenter);
            }
            else
            {
                matrix = _shieldShapeMatrix * Shield.WorldMatrix;
                _shield.PositionComp.SetWorldMatrix(matrix);
                _shield.PositionComp.SetPosition(_detectionCenter);
            }
        }

        private void RefreshDimensions()
        {

            if (!_updateDimensions) return;
            _updateDimensions = false;
            CreateShieldShape();
            Icosphere.ReturnPhysicsVerts(DetectionMatrix, ShieldComp.PhysicsOutside);
            _entityChanged = true;
            _shapeAdjusted = true;
        }
        #endregion

        #region Block Power Logic
        private void UpdateGridPower()
        {
            _gridMaxPower = 0;
            _gridCurrentPower = 0;
            _gridAvailablePower = 0;
            _shieldMaintaintPower = 0;
            if (!ShieldComp.ShieldActive) return;
            var eId = MyResourceDistributorComponent.ElectricityId;
            lock (_powerSources)
                for (int i = 0; i < _powerSources.Count; i++)
                {
                    var source = _powerSources[i];
                    if (!source.Enabled || !source.ProductionEnabled) continue;
                    _gridMaxPower += source.MaxOutputByType(eId);
                    _gridCurrentPower += source.CurrentOutputByType(eId);
                }
            _gridAvailablePower = _gridMaxPower - _gridCurrentPower;
            if (_gridCurrentPower <= 0) Shield.Enabled = false;
        }

        private void CalculatePowerCharge()
        {
            var nerf = Session.Enforced.Nerf > 0 && Session.Enforced.Nerf < 1;
            var rawNerf = nerf ? Session.Enforced.Nerf : 1f;
            var nerfer = rawNerf / _shieldRatio;
            var shieldVol = _detectMatrixOutside.Scale.Volume;
            var powerForShield = 0f;
            const float ratio = 1.25f;
            var percent = Rate * ratio;
            var shieldMaintainCost = 1 / percent;
            _shieldMaintaintPower = shieldMaintainCost;
            var fPercent = (percent / ratio) / 100;
            _sizeScaler = (shieldVol / _ellipsoidSurfaceArea) / 2.40063050674088;

            if (ShieldBuffer > 0 && _shieldCurrentPower < 0.00000000001f) 
            {
                if (ShieldBuffer > _gridMaxPower * shieldMaintainCost) ShieldBuffer -= _gridMaxPower * shieldMaintainCost;
                else ShieldBuffer = 0f;
            }

            _shieldCurrentPower = Sink.CurrentInputByType(GId);
            var otherPower = _gridMaxPower - _gridAvailablePower - _shieldCurrentPower;
            var cleanPower = _gridMaxPower - otherPower;
            powerForShield = (cleanPower * fPercent);

            _shieldMaxChargeRate = powerForShield > 0 ? powerForShield : 0f;
            _shieldMaxBuffer = ((_gridMaxPower * (100 / percent) * Session.Enforced.BaseScaler) / (float)_sizeScaler) * nerfer;
            if (_sizeScaler < 1)
            {
                if (ShieldBuffer + (_shieldMaxChargeRate * nerfer) < _shieldMaxBuffer) _shieldChargeRate = (_shieldMaxChargeRate * nerfer);
                else if (_shieldMaxBuffer - ShieldBuffer > 0) _shieldChargeRate = _shieldMaxBuffer - ShieldBuffer;
                else _shieldMaxChargeRate = 0f;
                _shieldConsumptionRate = _shieldMaxChargeRate;
            }
            else if (ShieldBuffer + (_shieldMaxChargeRate / (_sizeScaler / nerfer)) < _shieldMaxBuffer)
            {
                _shieldChargeRate = _shieldMaxChargeRate / ((float)_sizeScaler / nerfer);
                _shieldConsumptionRate = _shieldMaxChargeRate;
            }
            else
            {
                if (_shieldMaxBuffer - ShieldBuffer > 0)
                {
                    _shieldChargeRate = _shieldMaxBuffer - ShieldBuffer;
                    _shieldConsumptionRate = _shieldChargeRate;
                }
                else _shieldMaxChargeRate = 0f;
            }

            if (_count != -2)
            {
                if (ShieldBuffer < _shieldMaxBuffer) ShieldComp.ShieldPercent = (ShieldBuffer / _shieldMaxBuffer) * 100;
                else if (ShieldBuffer <= 1) ShieldComp.ShieldPercent = 0f;
                else ShieldComp.ShieldPercent = 100f;
            }

            if (_shieldMaxChargeRate < 0.001f)
            {
                _shieldChargeRate = 0f;
                _shieldConsumptionRate = 0f;
                if (ShieldBuffer > _shieldMaxBuffer)  ShieldBuffer = _shieldMaxBuffer;
                return;
            }

            if (ShieldBuffer < _shieldMaxBuffer && _count == 29)
            {
                ShieldBuffer += _shieldChargeRate;
            }
        }

        private double PowerCalculation(IMyEntity breaching)
        {
            var bPhysics = breaching.Physics;
            var sPhysics = Shield.CubeGrid.Physics;

            const double wattsPerNewton = (3.36e6 / 288000);
            var velTarget = sPhysics.GetVelocityAtPoint(breaching.Physics.CenterOfMassWorld);
            var accelLinear = sPhysics.LinearAcceleration;
            var velTargetNext = velTarget + accelLinear * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            var velModifyNext = bPhysics.LinearVelocity;
            var linearImpulse = bPhysics.Mass * (velTargetNext - velModifyNext);
            var powerCorrectionInJoules = wattsPerNewton * linearImpulse.Length();

            return powerCorrectionInJoules * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        }

        private void SetPower()
        {
            _power = _shieldConsumptionRate + _gridMaxPower * _shieldMaintaintPower;
            if (_power <= 0 || float.IsNaN(_power)) _power = 0.0001f; // temporary definitely 100% will fix this to do - Find ThE NaN!
            Sink.Update();

            _shieldCurrentPower = Sink.CurrentInputByType(GId);
            if (Absorb > 0)
            {
                _shieldDps += Absorb;
                _effectsCleanup = true;
                ShieldBuffer -= (Absorb / Session.Enforced.Efficiency);
            }
            else if (Absorb < 0) ShieldBuffer += (Absorb / Session.Enforced.Efficiency);

            if (ShieldBuffer < 0)
            {
                _shieldDownLoop = 0;
            }
            else if (ShieldBuffer > _shieldMaxBuffer) ShieldBuffer = _shieldMaxBuffer;

            Absorb = 0f;
        }

        private string ShieldStatus()
        {
            return ShieldOffline ? "Offline" : "Online";
        }

        private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder stringBuilder)
        {
            var secToFull = 0;
            var shieldPercent = ShieldOffline ? 0f : 100f;
            if (ShieldBuffer < _shieldMaxBuffer) shieldPercent = (ShieldBuffer / _shieldMaxBuffer) * 100;
            if (_shieldChargeRate > 0) secToFull = (int) ((_shieldMaxBuffer - ShieldBuffer) / _shieldChargeRate);
            stringBuilder.Append("[Shield " + ShieldStatus() +"] MaxHP: " + (_shieldMaxBuffer * Session.Enforced.Efficiency).ToString("N0") +
                                 "\n" +
                                 "\n[Shield HP__]: " + (ShieldBuffer * Session.Enforced.Efficiency).ToString("N0") + " (" + shieldPercent.ToString("0") + "%)" +
                                 "\n[HP Per Sec_]: " + (_shieldChargeRate * Session.Enforced.Efficiency).ToString("N0") +
                                 "\n[DPS_______]: " + (_shieldDps).ToString("N0") +
                                 "\n[Charge Rate]: " + _shieldChargeRate.ToString("0.0") + " Mw" +
                                 "\n[Full Charge_]: " + secToFull.ToString("N0") + "s" +
                                 "\n[Efficiency__]: " + Session.Enforced.Efficiency.ToString("0.0") +
                                 "\n[Maintenance]: " + (_gridMaxPower * _shieldMaintaintPower).ToString("0.0") + " Mw" +
                                 "\n[Availabile]: " + _gridAvailablePower.ToString("0.0") + " Mw" +
                                 "\n[Current__]: " + Sink.CurrentInputByType(GId).ToString("0.0"));
        }
        #endregion

        #region Shield Draw
        private void UpdateIcon()
        {

            var position = new Vector3D(_shieldIconPos.X, _shieldIconPos.Y, 0);
            var fov = MyAPIGateway.Session.Camera.FovWithZoom;
            double aspectratio = MyAPIGateway.Session.Camera.ViewportSize.X / MyAPIGateway.Session.Camera.ViewportSize.Y;
            var scale = 0.075 * Math.Tan(fov / 2);
            position.X *= scale * aspectratio;
            position.Y *= scale;

            var cameraWorldMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            position = Vector3D.Transform(new Vector3D(position.X, position.Y, -.1), cameraWorldMatrix);

            var origin = position;
            var left = cameraWorldMatrix.Left;
            var up = cameraWorldMatrix.Up;
            const double scaler = 0.07d;
            scale = scaler * scale;
            var color = UtilsStatic.GetEmissiveColorFromFloatIcon(ShieldComp.ShieldPercent);
            if (color == Color.DarkRed && _lCount % 2 == 0) color = Color.Transparent;
            MyTransparentGeometry.AddBillboardOriented(_hudIcon, color, origin, left, up, (float)scale, BlendTypeEnum.SDR);
        }		
		
        public void Draw(int onCount, bool sphereOnCamera)
        {
            _onCount = onCount;
            var enemy = false;
            var relation = MyAPIGateway.Session.Player.GetRelationTo(Shield.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.Enemies) enemy = true;
            _enemy = enemy;

            if (!enemy && SendToHud && !MyAPIGateway.Session.Config.MinimalHud && Session.HudComp == this) UpdateIcon();

            var passiveVisible = !(_hidePassiveCheckBox.Getter(Shield).Equals(true) && !enemy);
            var activeVisible = !(_hideActiveCheckBox.Getter(Shield).Equals(true) && !enemy);

            if (!passiveVisible && !_hideShield)
            {
                _hideShield = true;
                _shellPassive.Render.UpdateRenderObject(false);
            }
            else if (passiveVisible && _hideShield)
            {
                _hideShield = false;
                _shellPassive.Render.UpdateRenderObject(true);
            }

            if (BulletCoolDown > -1) BulletCoolDown++;
            if (BulletCoolDown > 9) BulletCoolDown = -1;
            if (EntityCoolDown > -1) EntityCoolDown++;
            if (EntityCoolDown > 9) EntityCoolDown = -1;
            var impactPos = WorldImpactPosition;
            _localImpactPosition = Vector3D.NegativeInfinity;
            if (impactPos != Vector3D.NegativeInfinity & ((BulletCoolDown == -1 && EntityCoolDown == -1)))
            {
                if (EntityCoolDown == -1 && ImpactSize > 5) EntityCoolDown = 0;
                BulletCoolDown = 0;

                var cubeBlockLocalMatrix = Shield.CubeGrid.LocalMatrix;
                var referenceWorldPosition = cubeBlockLocalMatrix.Translation;
                var worldDirection = impactPos - referenceWorldPosition;
                var localPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(cubeBlockLocalMatrix));
                _localImpactPosition = localPosition;
            }
            WorldImpactPosition = Vector3D.NegativeInfinity;

            if (Shield.IsWorking)
            {
                var prevlod = _prevLod;
                var lod = CalculateLod(_onCount);
                if (_shapeAdjusted || lod != prevlod) Icosphere.CalculateTransform(_shieldShapeMatrix, lod);
                Icosphere.ComputeEffects(_shieldShapeMatrix, _localImpactPosition, _shellPassive, _shellActive, prevlod, ShieldComp.ShieldPercent, passiveVisible, activeVisible);
                _entityChanged = false;
            }
            if (sphereOnCamera && Shield.IsWorking) Icosphere.Draw(GetRenderId());
        }

        private int CalculateLod(int onCount)
        {
            var lod = 4;

            if (onCount > 20) lod = 2;
            else if (onCount > 10) lod = 3;

            _prevLod = lod;
            return lod;
        }

        private uint GetRenderId()
        {
            return Shield.CubeGrid.Render.GetRenderObjectID();
        }
        #endregion

        #region Cleanup
        private void CleanUp(int task)
        {
            try
            {
                switch (task)
                {
                    case 0:
                        IMyCubeGrid grid;
                        while (_staleGrids.TryDequeue(out grid)) lock (_webEnts) _webEnts.Remove(grid);
                        break;
                    case 1:
                        lock (_webEnts)
                        {
                            _webEntsTmp.AddRange(_webEnts.Where(info => _tick - info.Value.FirstTick > 599 && _tick - info.Value.LastTick > 1));
                            foreach (var webent in _webEntsTmp) _webEnts.Remove(webent.Key);
                        }
                        break;
                    case 2:
                        lock (_functionalBlocks)
                        {
                            foreach (var funcBlock in _functionalBlocks) funcBlock.SetDamageEffect(false);
                            _effectsCleanup = false;
                        }
                        break;
                    case 3:
                        {
                            FriendlyCache.Clear();
                            foreach (var sub in ShieldComp.GetSubGrids) FriendlyCache.Add(sub);
                            FriendlyCache.Add(_shield);
                        }
                        break;
                    case 4:
                        {
                            IgnoreCache.Clear();
                        }
                        break;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in CleanUp: {ex}"); }
        }

        #region Shield Support Blocks
        private void GetModulationInfo()
        {

            ModulatorGridComponent modComp;
            Shield.CubeGrid.Components.TryGet(out modComp);
            if (modComp != null)
            {
                var reModulate = ModulateVoxels != modComp.ModulateVoxels || ModulateGrids != modComp.ModulateGrids;
                if (reModulate) _reModulationLoop = 0;

                ModulateVoxels = modComp.ModulateVoxels;
                ModulateGrids = modComp.ModulateGrids;
            }
            else
            {
                ModulateVoxels = false;
                ModulateGrids = false;
            }
        }
        #endregion


        public override void OnAddedToScene()
        {
            try
            {
            }
            catch (Exception ex) { Log.Line($"Exception in OnAddedToScene: {ex}"); }
        }

        public override void OnRemovedFromScene()
        {
            try
            {
                if (!Entity.MarkedForClose)
                {
                    return;
                }
                _power = 0f;
                if (AllInited) Sink.Update();
                Icosphere = null;
                _shield?.Close();
                _shellPassive?.Close();
                _shellActive?.Close();
                if (ShieldComp?.DefenseShields == this) ShieldComp.DefenseShields = null;
                //Shield?.CubeGrid.Components.Remove(typeof(ShieldGridComponent), this);
                MyAPIGateway.Session.OxygenProviderSystem.RemoveOxygenGenerator(EllipsoidOxyProvider);
                Session.Instance.Components.Remove(this);
            }
            catch (Exception ex) { Log.Line($"Exception in OnRemovedFromScene: {ex}"); }
        }

        public override void OnAddedToContainer() { if (Entity.InScene) OnAddedToScene(); }
        public override void OnBeforeRemovedFromContainer() { if (Entity.InScene) OnRemovedFromScene(); }
        public override void Close()
        {
            try
            {
                if (Session.Instance.Components.Contains(this)) Session.Instance.Components.Remove(this);
                _power = 0f;
                Icosphere = null;
                MyAPIGateway.Session.OxygenProviderSystem.RemoveOxygenGenerator(EllipsoidOxyProvider);
                if (AllInited) Sink.Update();
                if (ShieldComp?.DefenseShields == this) ShieldComp.DefenseShields = null;
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
        #endregion
    }
}