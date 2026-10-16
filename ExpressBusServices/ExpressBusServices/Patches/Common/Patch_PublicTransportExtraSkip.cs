using System.Reflection;
using ColossalFramework;
using ExpressBusServices.DataTypes;
using HarmonyLib;
using JetBrains.Annotations;

namespace ExpressBusServices.Patches.Common
{
    // set priority such that IPT2 can execute first; essentially this should execute last
    [HarmonyPatch]
    [HarmonyPriority(Priority.LowerThanNormal)]
    [UsedImplicitly]
    public class Patch_PublicTransportExtraSkip
    {
        /*
         * Special thanks to klyte45 from TLM for letting me use this logic.
         */

        [HarmonyTargetMethod]
        [UsedImplicitly]
        public static MethodBase TargetRelevantMethod()
        {
            return AccessTools.Method(typeof(VehicleAI), "ArrivingToDestination");
        }

        [HarmonyPrepare]
        [UsedImplicitly]
        public static bool DetermineIfShouldPatch()
        {
            // dont do it if TLM is detected
            // however, it seems that we can only detect whether the files of TLM exists;
            // we cant actually detect whether they are enabled, so this is modified to be "always true".
            // FileLog.Log($"TLM exists? {ModDetector.TransportLinesManagerIsLoaded()}");
            // return !ModDetector.TransportLinesManagerIsLoaded();
            return true;
        }

        [HarmonyPrefix]
        [UsedImplicitly]
        public static bool ExtraSkippingLogic(VehicleAI __instance, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!(__instance is BusAI /*|| __instance is TrolleybusAI */|| __instance is TramAI))
            {
                // not bus or trolleybus; no
                // note: we are also applying the logic to trams as streetcars
                return true;
            }

            Patch_VehicleAI_SimulationStep.PreparedSkip preparedSkip = Patch_VehicleAI_SimulationStep.PreparedSkips[vehicleID];

            if (preparedSkip.Path != 0)
            {
                if (vehicleData.m_targetBuilding == 0 || vehicleData.m_transportLine == 0)
                {
                    // returning to depot
                    Singleton<PathManager>.instance.ReleasePath(preparedSkip.Path);
                    Patch_VehicleAI_SimulationStep.PreparedSkips[vehicleID] = default;
                    return true;
                }

                if (!preparedSkip.SkipDecisionIsFinal) // if not passing the vehicle ahead
                {
                    TransportLineUtil.CountPassengersWaiting(vehicleData.m_targetBuilding, out int residents, out int tourists);
                    __instance.GetBufferStatus(vehicleID, ref vehicleData, out _, out int passengers, out int capacity);
                    
                    if (residents + tourists > 0 && passengers < capacity)
                    {
                        // there was a skip path prepared, but since then, people arrived at the stop,
                        // and the vehicle is not full, so stop normally and pick them up
                        Singleton<PathManager>.instance.ReleasePath(preparedSkip.Path);
                        Patch_VehicleAI_SimulationStep.PreparedSkips[vehicleID] = default;
                        return true;
                    }
                }
                
                vehicleData.m_targetBuilding = preparedSkip.FollowingStop;
                BusStopSkippingLookupTable.Notify_BusShouldSkipLoading(vehicleID);
                var unloadParams = new object[] { vehicleID, vehicleData, preparedSkip.SkippedStop, preparedSkip.FollowingStop };
                if (vehicleData.m_path != 0U) Singleton<PathManager>.instance.ReleasePath(vehicleData.m_path);
                vehicleData.m_path = preparedSkip.Path;
                vehicleData.m_flags |= Vehicle.Flags.WaitingPath;
                AccessTools.Method(__instance.GetType(), "UnloadPassengers").Invoke(__instance, unloadParams);
                AccessTools.Method(__instance.GetType(), "LoadPassengers").Invoke(__instance, unloadParams);
                Patch_VehicleAI_SimulationStep.PreparedSkips[vehicleID] = default;
                Patch_VehicleAI_SimulationStep.CheckSkip(__instance, vehicleID, ref vehicleData);
                return false;
            }
            
            return true;

            /*if (ExtraSkippingIsDisallowed(__instance, vehicleID, ref vehicleData, out var currentStop))
            {
                // actually, cannot do extra skip, so we allow the original method to execute.
                return true;
            }

            // okay can do
            ushort nextStop = TransportLine.GetNextStop(currentStop);
            vehicleData.m_targetBuilding = nextStop;
            BusStopSkippingLookupTable.Notify_BusShouldSkipLoading(vehicleID);
            var pathfindParams = new object[] { vehicleID, vehicleData };
            var unloadParams = new object[] { vehicleID, vehicleData, currentStop, nextStop };
            if (__instance is BusAI busAi)
            {
                if (!(bool) AccessTools.Method(typeof(BusAI), "StartPathFind", new Type[] { typeof(ushort), typeof(Vehicle).MakeByRefType() }).Invoke(busAi, pathfindParams))
                {
                    // something bad happened; cancel
                    vehicleData.m_targetBuilding = currentStop;
                    return true;
                }

                vehicleData = (Vehicle)pathfindParams[1];
                // I think this is to let it iterate their stuff
                AccessTools.Method(typeof(BusAI), "UnloadPassengers").Invoke(busAi, unloadParams);
                AccessTools.Method(typeof(BusAI), "LoadPassengers").Invoke(busAi, unloadParams);
            }
            else if (__instance is TrolleybusAI trolleyAi)
            {
                if (!(bool) AccessTools.Method(typeof(TrolleybusAI), "StartPathFind", new Type[] { typeof(ushort), typeof(Vehicle).MakeByRefType() }).Invoke(trolleyAi, pathfindParams))
                {
                    // something bad happened; cancel
                    vehicleData.m_targetBuilding = currentStop;
                    return true;
                }

                vehicleData = (Vehicle)pathfindParams[1];
                // I think this is to let it iterate their stuff
                AccessTools.Method(typeof(TrolleybusAI), "UnloadPassengers").Invoke(trolleyAi, unloadParams);
                AccessTools.Method(typeof(TrolleybusAI), "LoadPassengers").Invoke(trolleyAi, unloadParams);
            }
            else if (__instance is TramAI tramAI)
            {
                if (!(bool) AccessTools.Method(typeof(TramAI), "StartPathFind", new Type[] { typeof(ushort), typeof(Vehicle).MakeByRefType() }).Invoke(tramAI, pathfindParams))
                {
                    // something bad happened; cancel
                    vehicleData.m_targetBuilding = currentStop;
                    return true;
                }

                vehicleData = (Vehicle)pathfindParams[1];
                // I think this is to let it iterate their stuff
                AccessTools.Method(typeof(TramAI), "UnloadPassengers").Invoke(tramAI, unloadParams);
                AccessTools.Method(typeof(TramAI), "LoadPassengers").Invoke(tramAI, unloadParams);
            }
            else
            {
                // we should have already filtered this...?
                return true;
            }

            // get next path
            if (vehicleData.m_path == 0 && (vehicleData.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                vehicleData.m_flags &= ~Vehicle.Flags.WaitingPath;
                vehicleData.Info.m_vehicleAI.SetTransportLine(vehicleID, ref vehicleData, 0);
            }
            return false;*/
        }

        public static bool ExtraSkippingIsDisallowed(VehicleAI __instance, ushort vehicleID, ref Vehicle vehicleData, out ushort currentApproachingStop, out bool skipDecisionIsFinal)
        {
            // note: we extract this to be its own method to narrow down the place that triggers the strange NullRefEx bug

            currentApproachingStop = 0;
            skipDecisionIsFinal = false;
            // per-vehicle detection; we have buses and trams
            /*if (__instance is BusAI || __instance is TrolleybusAI)
            {
                if ((int)EBSModConfig.CurrentExpressBusMode < (int)EBSModConfig.ExpressMode.AGGRESSIVE)
                {
                    // settings not enabled; no
                    return true;
                }
            }
            else if (__instance is TramAI)
            {
                if (EBSModConfig.CurrentExpressTramMode < EBSModConfig.ExpressTramMode.STREET_CAR)
                {
                    // settings not enabled; no
                    return true;
                }
            }*/

            currentApproachingStop = vehicleData.m_targetBuilding;
            /*if (currentApproachingStop == 0 || vehicleData.m_transportLine == 0)
            {
                // this can happen when e.g. the depot is forced to deactivate and the vehicles are therefore forced to return to base
                // in this case, don't do it
                return true;
            }*/

            if (!DepartureChecker.CanSkipNextStop(ref vehicleData))
            {
                // is arriving at terminus; dont do this!
                return true;
            }

            var unloadPredict = TransportLineUtil.GetQuantityPassengerUnloadOnNextStop(vehicleID, ref vehicleData, out bool full, out _);

            if (unloadPredict > 0)
                return true; // always needs to stop if someone wants to get off
            
            // nobody wants to alight
            
            TransportLineUtil.CountPassengersWaiting(currentApproachingStop, out int residents, out int tourists);
            int totalWaiting = residents + tourists;

            if (!(__instance is TramAI) && !CheckVehicleAhead(vehicleID, vehicleData, currentApproachingStop, ref skipDecisionIsFinal, totalWaiting))
                return false;
            
            // can only skip if full or nobody is waiting at the stop
            return !full && totalWaiting > 0;
        }

        private static bool CheckVehicleAhead(ushort vehicleID, Vehicle vehicleData, ushort currentApproachingStop, ref bool skipDecisionIsFinal, int totalWaiting)
        {
            // nobody wants to alight, but some want to board
            // check if there's a vehicle on this line in front of this one going to the same stop with enough space to fit all waiting passengers at the next stop

            TransportLineVehicleProgress lineProgress = VehicleLineProgress.GetTransportLineVehicleProgress(vehicleData.m_transportLine);
            
            if (lineProgress.VehiclesCount < 2) // only this vehicle is on this line, irrelevant
                return true;
            
            VehicleLineProgress? selfProgress = lineProgress.GetProgressOf(vehicleID);
            if (!selfProgress.HasValue) return true;
            
            VehicleLineProgress? frontVehicleProgress = lineProgress.GetProgressOfFrontOf(vehicleID);
            if (!frontVehicleProgress.HasValue) return true;
            
            if (frontVehicleProgress.Value.PercentProgress < selfProgress.Value.PercentProgress) // not ahead of current vehicle, try back one
            {
                frontVehicleProgress = lineProgress.GetProgressOfBackOf(vehicleID);
                if (!frontVehicleProgress.HasValue) return true;

                if (frontVehicleProgress.Value.PercentProgress < selfProgress.Value.PercentProgress)
                    return true; // also not ahead, check becomes irrelevant
            }
            
            ushort aheadVehicleID = frontVehicleProgress.Value.VehicleID;
            if (aheadVehicleID == 0) return true;
            Vehicle aheadVehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[aheadVehicleID];
            
            // check if the vehicle ahead is also approaching the same stop
            if (aheadVehicle.m_transportLine == 0 || aheadVehicle.m_path == 0 || aheadVehicle.m_targetBuilding != currentApproachingStop)
                return true;
            
            Patch_VehicleAI_SimulationStep.PreparedSkip preparedSkip = Patch_VehicleAI_SimulationStep.PreparedSkips[aheadVehicleID];
            
            // if the vehicle ahead is skipping the next stop, and that decision won't change, this vehicle needs to stop to pick up the waiting passengers
            if (preparedSkip.Path != 0 && preparedSkip.SkipDecisionIsFinal)
                return true;
            
            // get the current load of the vehicle ahead and how many people will get off of it at the next stop
            aheadVehicle.Info.m_vehicleAI.GetBufferStatus(aheadVehicleID, ref aheadVehicle, out _, out int passengers, out int capacity);
            var unloadPredict = TransportLineUtil.GetQuantityPassengerUnloadOnNextStop(aheadVehicleID, ref aheadVehicle, out _, out _);

            // if all waiting passengers will be able to board the vehicle ahead, this one can prepare to skip the stop
            if (passengers - unloadPredict + totalWaiting <= capacity)
            {
                skipDecisionIsFinal = true;
                return false;
            }

            return true;
        }
    }
}
