﻿using System;
using System.Collections;
using System.ComponentModel;
using ProtoBuf;

namespace DefenseShields
{
    /// Used for serializing the settings.
    [ProtoContract]
    public class DefenseShieldsModSettings
    {
        [ProtoMember(1)]
        public bool Enabled = false;

        [ProtoMember(2), DefaultValue(-1)]
        public float Width = -1f;

        [ProtoMember(3), DefaultValue(-1)]
        public float Height = -1f;

        [ProtoMember(4), DefaultValue(-1)]
        public float Depth = -1f;

        [ProtoMember(5)]
        public bool IdleInvisible = false;

        [ProtoMember(6)]
        public bool ActiveInvisible = false;

        [ProtoMember(7), DefaultValue(-1)]
        public float Rate = -1f;

        [ProtoMember(8), DefaultValue(-1)]
        public float Buffer = 0f;

        [ProtoMember(9), DefaultValue(-1)]
        public float Nerf = -1f;

        [ProtoMember(10), DefaultValue(-1)]
        public int BaseScaler = -1;

        [ProtoMember(11), DefaultValue(-1)]
        public float Efficiency = -1;

        [ProtoMember(12), DefaultValue(-1)]
        public int StationRatio = -1;

        [ProtoMember(13), DefaultValue(-1)]
        public int LargeShipRatio = -1;

        [ProtoMember(14), DefaultValue(-1)]
        public int SmallShipRatio = -1;

        [ProtoMember(15), DefaultValue(-1)]
        public int DisableVoxelSupport = -1;

        [ProtoMember(16), DefaultValue(-1)]
        public int DisableGridDamageSupport = -1;

        [ProtoMember(17), DefaultValue(-1)]
        public int Debug = -1;

        public override string ToString()
        {
            return $"Enabled = {Enabled}\nIdleVisible = {IdleInvisible}\nActiveVisible = {ActiveInvisible}\nWidth = {Math.Round(Width, 4)}" +
                   $"\nHeight = {Math.Round(Height, 4)}\nDepth = {Math.Round(Depth, 4)}\nRate = {Math.Round(Rate, 4)}\nNerf = {Math.Round(Nerf, 4)}" +
                   $"\nBaseScaler = {BaseScaler}\nEfficiency = {Math.Round(Efficiency, 4)}\nStationRatio = {StationRatio}\nLargeShipRatio = {LargeShipRatio}" +
                   $"\nSmallShipRatio = {SmallShipRatio}\nDisableVoxelSupport = {DisableVoxelSupport}\nDisableGridDamageSupport = {DisableGridDamageSupport}" +
                   $"\nDebug = {Debug}";
        }
    }

    [ProtoContract]
    public class DefenseShieldsEnforcement
    {
        [ProtoMember(1), DefaultValue(-1)]
        public float Nerf = -1f;

        [ProtoMember(2), DefaultValue(-1)]
        public int BaseScaler = -1;

        [ProtoMember(3), DefaultValue(-1)]
        public float Efficiency = -1f;

        [ProtoMember(4), DefaultValue(-1)]
        public int StationRatio = -1;

        [ProtoMember(5), DefaultValue(-1)]
        public int LargeShipRatio = -1;

        [ProtoMember(6), DefaultValue(-1)]
        public int SmallShipRatio = -1;

        [ProtoMember(7), DefaultValue(-1)]
        public int DisableVoxelSupport = -1;

        [ProtoMember(8), DefaultValue(-1)]
        public int DisableGridDamageSupport = -1;

        [ProtoMember(9), DefaultValue(-1)]
        public int Debug = -1;

        public override string ToString()
        {
            return $"Nerf = {Math.Round(Nerf, 4)}\nBaseScaler = {BaseScaler}\nEfficiency = {Math.Round(Efficiency, 4)}\nStationRatio = {StationRatio}\nLargeShipRatio = {LargeShipRatio}" +
                   $"\nSmallShipRatio = {SmallShipRatio}\nDisableVoxelSupport = {DisableVoxelSupport}\nDisableGridDamageSupport = {DisableGridDamageSupport}" +
                   $"\nDebug = {Debug}";
        }

    }

    [ProtoContract]
    public class ModulatorSettings
    {
        [ProtoMember(1)]
        public bool Enabled = false;

        [ProtoMember(2)]
        public bool IdleInvisible = false;

        [ProtoMember(3)]
        public bool ActiveInvisible = false;

        public override string ToString()
        {
            return $"Enabled";
        }
    }

    [ProtoContract]
    public class PacketData
    {
        [ProtoMember(1)]
        public PacketType Type = PacketType.SETTINGS;

        [ProtoMember(2)]
        public long EntityId = 0;

        [ProtoMember(3)]
        public ulong Sender = 0;

        [ProtoMember(4)]
        public DefenseShieldsModSettings Settings = null;

        public PacketData() { } // empty ctor is required for deserialization

        public PacketData(ulong sender, long entityId, DefenseShieldsModSettings settings)
        {
            Type = PacketType.SETTINGS;
            Sender = sender;
            EntityId = entityId;
            Settings = settings;
        }

        public PacketData(ulong sender, long entityId, PacketType action)
        {
            Type = action;
            Sender = sender;
            EntityId = entityId;
            Settings = null;
        }
    }

    [ProtoContract]
    public class EnforceData
    {
        [ProtoMember(1)]
        public PacketType Type = PacketType.ENFORCE;

        [ProtoMember(2)]
        public long EntityId = 0;

        [ProtoMember(3)]
        public ulong Sender = 0;

        [ProtoMember(4)]
        public DefenseShieldsEnforcement Enforce = null;

        public EnforceData() { } // empty ctor is required for deserialization

        public EnforceData(ulong sender, long entityId, DefenseShieldsEnforcement enforce)
        {
            Type = PacketType.ENFORCE;
            Sender = sender;
            EntityId = entityId;
            Enforce = enforce;
        }

        public EnforceData(ulong sender, long entityId, PacketType action)
        {
            Type = action;
            Sender = sender;
            EntityId = entityId;
            Enforce = null;
        }
    }
    public enum PacketType : byte
    {
        SETTINGS,
        ENFORCE,
    }
}
