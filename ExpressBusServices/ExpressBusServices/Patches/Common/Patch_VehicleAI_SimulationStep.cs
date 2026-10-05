using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace ExpressBusServices.Patches.Common
{
    [HarmonyPatch]
    [UsedImplicitly]
    public class Patch_VehicleAI_SimulationStep
    {
        public static readonly PreparedSkip[] PreparedSkips = new PreparedSkip[65536];
        public static readonly StopPosition[] StopPositions = new StopPosition[65536];
        /*
         * Special thanks to klyte45 from TLM for letting me use this logic.
         */

        [HarmonyTargetMethod]
        [UsedImplicitly]
        public static MethodBase TargetRelevantMethod()
        {
            return AccessTools.Method(typeof(VehicleAI), "SimulationStep", new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(int) });
        }

        [HarmonyPrepare]
        [UsedImplicitly]
        public static bool DetermineIfShouldPatch()
        {
            // this method should exist
            // however, dont do it if TLM is detected
            return !ModDetector.TransportLinesManagerIsLoaded();
        }

        [HarmonyPrefix]
        [UsedImplicitly]
        public static void PreSimulationStep(ushort vehicleID, ref Vehicle vehicleData)
        {
            if (vehicleData.m_transportLine != 0 && vehicleData.m_path == 0 && (vehicleData.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                vehicleData.m_flags &= ~Vehicle.Flags.WaitingPath;
                vehicleData.Info.m_vehicleAI.SetTransportLine(vehicleID, ref vehicleData, 0);
            }
        }

        [HarmonyPostfix]
        [UsedImplicitly]
        public static void PostSimulationStep(VehicleAI __instance, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (vehicleData.m_transportLine != 0 && (vehicleData.m_flags & Vehicle.Flags.Leaving) != 0 && PreparedSkips[vehicleID].Path == 0)
            {
                CheckSkip(__instance, vehicleID, ref vehicleData);
            }
        }

        public static void CheckSkip(VehicleAI __instance, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (Patch_PublicTransportExtraSkip.ExtraSkippingIsDisallowed(__instance, vehicleID, ref vehicleData, out ushort currentApproachingStop))
                return;

            if (!(__instance is BusAI busAI) || vehicleData.m_targetBuilding == 0 || vehicleData.m_path == 0) return;

            Vector3 startPos = StopPositions[currentApproachingStop].Position;
            if (startPos == default) return;
            
            ushort nextStop = TransportLine.GetNextStop(currentApproachingStop);
            if (nextStop == 0) return;
            Vector3 endPos = Singleton<NetManager>.instance.m_nodes.m_buffer[nextStop].m_position;

            if (PreparePath(busAI, vehicleData, startPos, endPos, true, true, false, out uint path))
            {
                PreparedSkips[vehicleID] = new PreparedSkip
                {
                    SkippedStop = currentApproachingStop,
                    FollowingStop = nextStop,
                    Path = path
                };
            }
        }

        public static bool PreparePath(
            BusAI __instance,
            Vehicle vehicleData,
            Vector3 startPos,
            Vector3 endPos,
            bool startBothWays,
            bool endBothWays,
            bool undergroundTarget,
            out uint path)
        {
            path = 0;
            VehicleInfo info = __instance.m_info;
            bool allowUnderground = (vehicleData.m_flags & (Vehicle.Flags.Underground | Vehicle.Flags.Transition)) != ~(Vehicle.Flags.Created | Vehicle.Flags.Deleted | Vehicle.Flags.Spawned | Vehicle.Flags.Inverted | Vehicle.Flags.TransferToTarget | Vehicle.Flags.TransferToSource | Vehicle.Flags.Emergency1 | Vehicle.Flags.Emergency2 | Vehicle.Flags.WaitingPath | Vehicle.Flags.Stopped | Vehicle.Flags.Leaving | Vehicle.Flags.Arriving | Vehicle.Flags.Reversed | Vehicle.Flags.TakingOff | Vehicle.Flags.Flying | Vehicle.Flags.Landing | Vehicle.Flags.WaitingSpace | Vehicle.Flags.WaitingCargo | Vehicle.Flags.GoingBack | Vehicle.Flags.WaitingTarget | Vehicle.Flags.Importing | Vehicle.Flags.Exporting | Vehicle.Flags.Parking | Vehicle.Flags.CustomName | Vehicle.Flags.OnGravel | Vehicle.Flags.WaitingLoading | Vehicle.Flags.Congestion | Vehicle.Flags.DummyTraffic | Vehicle.Flags.Underground | Vehicle.Flags.Transition | Vehicle.Flags.InsideBuilding | Vehicle.Flags.LeftHandDrive);
            if (PathManager.FindPathPosition(startPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, info.vehicleCategory, allowUnderground, false, 32f, false, false, out PathUnit.Position pathPosA1, out PathUnit.Position pathPosB1, out var distanceSqrA1, out float _) && PathManager.FindPathPosition(endPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, info.vehicleCategory, undergroundTarget, false, 32f, false, false, out PathUnit.Position pathPosA2, out PathUnit.Position pathPosB2, out float distanceSqrA2, out float _))
            {
                if (!startBothWays || distanceSqrA1 < 10.0)
                    pathPosB1 = new PathUnit.Position();
                if (!endBothWays || distanceSqrA2 < 10.0)
                    pathPosB2 = new PathUnit.Position();
                if (Singleton<PathManager>.instance.CreatePath(out path, ref Singleton<SimulationManager>.instance.m_randomizer, Singleton<SimulationManager>.instance.m_currentBuildIndex, pathPosA1, pathPosB1, pathPosA2, pathPosB2, new PathUnit.Position(), NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, info.vehicleCategory, 20000f, false, false, false, false, false, false, false))
                    return true;
            }

            return false;
        }

        public struct PreparedSkip
        {
            public ushort SkippedStop;
            public ushort FollowingStop;
            public uint Path;
        }

        public struct StopPosition
        {
            public Vector3 Position;
        }
    }
}