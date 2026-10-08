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
                CheckSkip(__instance, vehicleID, ref vehicleData);
        }

        public static void CheckSkip(VehicleAI __instance, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (Patch_PublicTransportExtraSkip.ExtraSkippingIsDisallowed(__instance, vehicleID, ref vehicleData, out ushort currentApproachingStop))
                return;

            if (!(__instance is BusAI || __instance is TramAI) || vehicleData.m_targetBuilding == 0 || vehicleData.m_path == 0) return;

            if (!Singleton<PathManager>.instance.m_pathUnits.m_buffer[(int)vehicleData.m_path].GetLastPosition(out PathUnit.Position startPos)) return;

            ushort nextStop = TransportLine.GetNextStop(currentApproachingStop);
            if (nextStop == 0) return;
            Vector3 endPos = Singleton<NetManager>.instance.m_nodes.m_buffer[nextStop].m_position;

            if (__instance is BusAI busAI ? PreparePathBus(busAI, startPos, endPos, true, false, out uint path) : PreparePathTram((TramAI)__instance, startPos, endPos, true, out path))
                PreparedSkips[vehicleID] = new PreparedSkip
                {
                    SkippedStop = currentApproachingStop,
                    FollowingStop = nextStop,
                    Path = path
                };
        }

        private static bool PreparePathBus(
            BusAI __instance,
            PathUnit.Position startPos,
            Vector3 endPos,
            bool endBothWays,
            bool undergroundTarget,
            out uint path)
        {
            path = 0;
            VehicleInfo info = __instance.m_info;
            if (PathManager.FindPathPosition(endPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, info.vehicleCategory, undergroundTarget, false, 32f, false, false, out PathUnit.Position pathPosA2, out PathUnit.Position pathPosB2, out float distanceSqrA2, out float _))
            {
                if (!endBothWays || distanceSqrA2 < 10.0)
                    pathPosB2 = new PathUnit.Position();
                if (Singleton<PathManager>.instance.CreatePath(out path, ref Singleton<SimulationManager>.instance.m_randomizer, Singleton<SimulationManager>.instance.m_currentBuildIndex, startPos, new PathUnit.Position(), pathPosA2, pathPosB2, new PathUnit.Position(), NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, info.m_vehicleType, info.vehicleCategory, 20000f, false, false, false, false, false, false, false))
                    return true;
            }

            return false;
        }

        private static bool PreparePathTram(
            TramAI __instance,
            PathUnit.Position startPos,
            Vector3 endPos,
            bool endBothWays,
            out uint path)
        {
            path = 0;
            VehicleInfo info = __instance.m_info;
            if (PathManager.FindPathPosition(endPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle, info.m_vehicleType, info.vehicleCategory, false, false, 32f, false, false, out PathUnit.Position pathPosA2, out PathUnit.Position pathPosB2, out float distanceSqrA2, out float distanceSqrB2))
            {
                if (!endBothWays || distanceSqrB2 > distanceSqrA2 * 1.2000000476837158)
                    pathPosB2 = new PathUnit.Position();
                if (Singleton<PathManager>.instance.CreatePath(out path, ref Singleton<SimulationManager>.instance.m_randomizer, Singleton<SimulationManager>.instance.m_currentBuildIndex, startPos, new PathUnit.Position(), pathPosA2, pathPosB2, NetInfo.LaneType.Vehicle, info.m_vehicleType, info.vehicleCategory, 20000f, false, false, true, false))
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
    }
}