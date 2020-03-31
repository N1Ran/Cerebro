using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using ContentType = VRage.Game.GUI.TextPanel.ContentType;
using IMyAirtightHangarDoor = Sandbox.ModAPI.Ingame.IMyAirtightHangarDoor;
using IMyAssembler = Sandbox.ModAPI.Ingame.IMyAssembler;
using IMyBatteryBlock = Sandbox.ModAPI.Ingame.IMyBatteryBlock;
using IMyCargoContainer = Sandbox.ModAPI.Ingame.IMyCargoContainer;
using IMyDoor = Sandbox.ModAPI.Ingame.IMyDoor;
using IMyFunctionalBlock = Sandbox.ModAPI.Ingame.IMyFunctionalBlock;
using IMyGasGenerator = Sandbox.ModAPI.Ingame.IMyGasGenerator;
using IMyGasTank = Sandbox.ModAPI.Ingame.IMyGasTank;
using IMyGyro = Sandbox.ModAPI.Ingame.IMyGyro;
using IMyJumpDrive = Sandbox.ModAPI.Ingame.IMyJumpDrive;
using IMyLargeTurretBase = Sandbox.ModAPI.Ingame.IMyLargeTurretBase;
using IMyLightingBlock = Sandbox.ModAPI.Ingame.IMyLightingBlock;
using IMyPowerProducer = Sandbox.ModAPI.Ingame.IMyPowerProducer;
using IMyProductionBlock = Sandbox.ModAPI.Ingame.IMyProductionBlock;
using IMyProjector = Sandbox.ModAPI.Ingame.IMyProjector;
using IMyRadioAntenna = Sandbox.ModAPI.Ingame.IMyRadioAntenna;
using IMyReactor = Sandbox.ModAPI.Ingame.IMyReactor;
using IMyRemoteControl = Sandbox.ModAPI.Ingame.IMyRemoteControl;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyShipController = Sandbox.ModAPI.Ingame.IMyShipController;
using IMyShipDrill = Sandbox.ModAPI.Ingame.IMyShipDrill;
using IMyShipGrinder = Sandbox.ModAPI.Ingame.IMyShipGrinder;
using IMyShipWelder = Sandbox.ModAPI.Ingame.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using IMyTextPanel = Sandbox.ModAPI.Ingame.IMyTextPanel;
using IMyThrust = Sandbox.ModAPI.Ingame.IMyThrust;

namespace IngameScript
{
    internal class Program : MyGridProgram
    {
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            _runtimeTracker = new RuntimeTracker(this);
            _scheduledSetup = new ScheduledAction(Setup, 0.1);
            _scheduler = new Scheduler(this);
            Load();
        }

        /// <summary>
        /// Saves the script state
        /// </summary>
        private void Save()
        {
            var settings = new []
            {
                $":CurrentMode={_currentMode}",
                $":AutoPilot={_autoPilot}",
                $":Aggression={_aggression}"
            };
            var useSb = new StringBuilder();

            foreach (var item in settings)
            {
                useSb.AppendLine(item);
            }

            Storage = useSb.ToString();
        }

        /// <summary>
        /// Loads saved states
        /// </summary>
        private void Load()
        {
            GetBlocks();
            if (!string.IsNullOrEmpty(Storage))
            {
                var settings = Storage.Split(new []{':'},StringSplitOptions.RemoveEmptyEntries);

                foreach (var item in settings)
                {
                    if (item.StartsWith("CurrentMode"))
                    {
                        var mode = ProgramState.PowerOn;
                        if (Enum.TryParse(item.Replace("CurrentMode=", ""), out mode))
                            _currentMode = mode;
                        continue;

                    }
                    if (item.StartsWith("AutoPilot"))
                    {
                        var pilotMode = Pilot.Disabled;
                        if (Enum.TryParse(item.Replace("AutoPilot=",""), out pilotMode))
                            _autoPilot = pilotMode;
                        continue;
                    }
                    if (item.StartsWith("Aggression"))
                    {
                        _aggression = int.Parse(item.Replace("Aggression=", ""));
                        continue;
                    }
                }
            }
            Setup();
            _scheduler.Reset();
            if (_currentMode != ProgramState.ShutOff && _currentMode != ProgramState.Recharge)
            {
                SetSchedule();
            }
            else if (_currentMode == ProgramState.Recharge)
            {
                _setRechargeState = false;
            }

        }

        /// <summary>
        /// Main Method. Runs each tick
        /// </summary>
        /// <param name="arg"></param>
        private void Main(string arg)
        {
            CurrentState(arg, out _currentMode);
            Save();
            if (_currentMode == ProgramState.Stop)
            {
                Echo("Script Paused");
                return;
            }

            if (!_gridBlocks.Any())
            {
                GetBlocks();
                return;
            }
            ProgramMaintenance();
            Echo(_runtimeTracker.Write());

        }

        /// <summary>
        /// Method to control Script. Runs Each tick
        /// </summary>
        private void ProgramMaintenance()
        {
            _isStatic = Me.CubeGrid.IsStatic;
            _runtimeTracker.AddRuntime();
            _scheduler.Update();
            _runtimeTracker.AddInstructions();
            if (!_hasAntenna && _hive && !TryGetAntenna(out _myAntenna))
            {
                _hive = false;
            }

            if (_remoteControl == null && !_isStatic && _autoNavigate && !TryGetRemote(out _remoteControl))
            {
                _autoPilot = Pilot.Disabled;
            }

            _navFlag = _remoteControl == null && !_isStatic && _autoNavigate;

            if (!GridFlags(out _alert))
            {
                _alert = AlertState.Clear;
            }

            if (IsDocked() && _currentMode != ProgramState.Recharge) _currentMode = ProgramState.Docked;

            switch (_currentMode)
            {
                case ProgramState.ShutOff:
                    Echo("Powered off");
                    PowerDown();
                    AutoDoors();
                    return;
                case ProgramState.PowerOn:
                    PowerOn();
                    break;
                case ProgramState.Docked:
                    _setRechargeState = false;
                    PowerDown();
                   if (_rechargeWhenConnected) _currentMode = ProgramState.Recharge;
                    LockLandingGears();
                    break;
                case ProgramState.Normal:
                    break;
                case ProgramState.Recharge:
                    Echo($"Recharging --- {Math.Round((double)BatteryLevel() * 100)}%");
                    if (!_setRechargeState) SetRechargeSchedule();
                    if (IsConnected()) return;
                    _setRechargeState = false;
                    _currentMode = ProgramState.PowerOn;
                    LockLandingGears(false);
                    return;
                case ProgramState.PowerSave:
                default:
                    break;
            }

        }

        /// <summary>
        /// Sets Schedule for normal runs
        /// </summary>
        private void SetSchedule()
        {
                        //Scheduled actions
           _scheduler.AddScheduledAction(_scheduledSetup);
           _scheduler.AddScheduledAction(CheckConnectors,1);
            //Queued actions
            _scheduler.AddScheduledAction(AggroBuilder,0.1);
            _scheduler.AddScheduledAction(CheckNavigation,100);
            _scheduler.AddScheduledAction(CheckProjection,0.01);
            _scheduler.AddScheduledAction(()=>BlockGroupEnable(_solars),0.001);
            _scheduler.AddScheduledAction(GetBlocks,1f/150f);
            _scheduler.AddScheduledAction(FindFuel, 1f/50f);
            _scheduler.AddScheduledAction(AutoDoors,1);
            _scheduler.AddScheduledAction(Alerts,0.1);
            _scheduler.AddScheduledAction(AlertLights,1);


            const float step = 1f / 5f;
            const float twoStep = 1f / 10f;
            const double mehTick = 5 * Tick;

            _scheduler.AddQueuedAction(() => UpdateProduction(0 * step, 1 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(1 * step, 2 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(3 * step, 4 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateProduction(4 * step, 5 * step),mehTick); 

            _scheduler.AddQueuedAction(() => UpdateTanks(0 * step, 1 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(1 * step, 2 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(2 * step, 3 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateTanks(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(4 * step, 5 * step), mehTick); 

           _scheduler.AddQueuedAction(() => UpdateVents(0 * (twoStep), 1 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(1 * (twoStep), 2 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(2 * (twoStep), 3 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(3 * (twoStep), 4 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(4 * (twoStep), 5 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(5 * (twoStep), 6 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(6 * (twoStep), 7 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(7 * (twoStep), 8 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(8 * (twoStep), 9 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateVents(9 * (twoStep), 10 * (twoStep)), mehTick);


           _scheduler.AddQueuedAction(() => UpdateGasGen(0 * 1f / 4f, 1 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateGasGen(1 * 1f / 4f, 2 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateGasGen(2 * 1f / 4f, 3 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateGasGen(2 * 1f / 4f, 4 * 1f / 4f), mehTick); 

      
           _scheduler.AddQueuedAction(() => UpdateBatteries(0 * (twoStep), 1 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(1 * (twoStep), 2 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(2 * (twoStep), 3 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(3 * (twoStep), 4 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(4 * (twoStep), 5 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(5 * (twoStep), 6 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(6 * (twoStep), 7 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(7 * (twoStep), 8 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(8 * (twoStep), 9 * (twoStep)), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(9 * (twoStep), 10 * (twoStep)), mehTick);

           _scheduler.AddQueuedAction(() => UpdateReactors(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateReactors(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateReactors(4 * step, 5 * step), mehTick);

           _scheduler.AddQueuedAction(() => ManageTurrets(0 * 1f / 4f, 1 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => ManageTurrets(1 * 1f / 4f, 2 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => ManageTurrets(2 * 1f / 4f, 3 * 1f / 4f), mehTick); 
           _scheduler.AddQueuedAction(() => ManageTurrets(2 * 1f / 4f, 4 * 1f / 4f), mehTick); 

            _scheduler.AddQueuedAction(() => CapFuel(0 * (twoStep), 1 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(1 * (twoStep), 2 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(2 * (twoStep), 3 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(3 * (twoStep), 4 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(4 * (twoStep), 5 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(5 * (twoStep), 6 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(6 * (twoStep), 7 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(7 * (twoStep), 8 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(8 * (twoStep), 9 * (twoStep)), mehTick);
            _scheduler.AddQueuedAction(() => CapFuel(9 * (twoStep), 10 * (twoStep)), mehTick);

            _scheduler.AddQueuedAction(() => UpdateScreens(0 * step, 1 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateScreens(1 * step, 2 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateScreens(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateScreens(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateScreens(4 * step, 5 * step), mehTick);

        }


        /// <summary>
        /// Sets schedule for recharge phase
        /// </summary>
        private void SetRechargeSchedule()
        {
            _runtimeTracker.Reset();
            _scheduler.Reset();
            _setRechargeState = true;

            _scheduler.AddScheduledAction(GetBlocks,1f/600f);
            _scheduler.AddScheduledAction(AutoDoors,1);
            const float step = 1f / 10f;
            const double mehTick = 500 * Tick;
            _scheduler.AddQueuedAction(() => UpdateVents(0 * step, 1 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(1 * step, 2 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(3 * step, 4 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateVents(4 * step, 5 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(5 * step, 6 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(6 * step, 7 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(7 * step, 8 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(8 * step, 9 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateVents(9 * step, 10 * step), mehTick); 

            _scheduler.AddQueuedAction(() => UpdateBatteries(0 * step, 1 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(1 * step, 2 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(2 * step, 3 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(3 * step, 4 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(4 * step, 5 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(5 * step, 6 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(6 * step, 7 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(7 * step, 8 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(8 * step, 9 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(9 * step, 10 * step), mehTick);


        }

        /// <summary>
        /// Updates screen displays
        /// </summary>
        private void UpdateScreens(float startProportion, float endProportion)
        {
            Me.GetSurface(0).WriteText(_runtimeTracker.Write());
            if (!_textPanels.Any())return;
            _sbStatus.Clear();
            _sbStatus.Append("AI Running ");

            var start = (int) (startProportion * _textPanels.Count);
            var end = (int) (endProportion * _textPanels.Count);

            for (var i = start; i < end; i++)
            {
                var panel = _textPanels[i];
                if (SkipBlock(panel) || !StringContains(panel.CustomName, "cerebro"))continue;
                panel.ContentType = ContentType.TEXT_AND_IMAGE;
                panel.Enabled = true;
                if (StringContains(panel.CustomName, "damage"))
                {
                    panel.WriteText(_sbDamages);
                    continue;
                }
                if (StringContains(panel.CustomName, "status"))
                {
                    panel.WriteText(_sbStatus);
                    continue;
                }
                if (StringContains(panel.CustomName, "debug"))
                {
                    panel.WriteText(_sbDebug);
                    continue;
                }
                if (StringContains(panel.CustomName, "power"))
                {
                    panel.WriteText(_sbPower);
                    continue;
                }

                panel.WriteText(_sbInfo);
            }

        }

        /// <summary>
        /// Controls lock state of landing gears
        /// </summary>
        /// <param name="b"></param>
        private void LockLandingGears(bool b = true)
        {
            if (_landingGears?.Any() == false || _landingGears== null) return;
            foreach (var landingGear in _landingGears)
            {
                landingGear.Enabled = true;
                landingGear.AutoLock = false;

                if (b == false)
                {
                    landingGear.Unlock();
                    continue;
                }

                landingGear.Lock();
            }
        }

        private void PowerOn()
        {
            var blocksToSwitchOn = new List<IMyCubeBlock>();

            blocksToSwitchOn.AddRange(_gridBlocks.OfType<IMyFunctionalBlock>().Where(funcBlock => !Closed(funcBlock) &&
                !funcBlock.Enabled && !(funcBlock is IMyShipWelder || funcBlock is IMyShipGrinder ||
                                        funcBlock is IMyShipDrill || funcBlock.BlockDefinition.TypeIdString.Substring(16).Equals("hydrogenengine",
                                            StringComparison.OrdinalIgnoreCase) || funcBlock is IMyLightingBlock)));

            blocksToSwitchOn.AddRange(_lights.Where(x => !x.BlockDefinition.TypeIdString.Substring(16).Equals("ReflectorLight",StringComparison.OrdinalIgnoreCase) &&
                                                         !x.BlockDefinition.TypeIdString.Substring(16).Equals("RotatingLight",StringComparison.OrdinalIgnoreCase)));

            BlockGroupEnable(blocksToSwitchOn);

            _currentMode = ProgramState.Normal;

            _autoPilot = Pilot.Disabled;

            _scheduler.Reset();
            
            SetSchedule();

            foreach (var tank in blocksToSwitchOn.OfType<IMyGasTank>())
            {
                tank.Stockpile = false;
            }

        }


        private void PowerDown()
        {
            if (!IsDocked() && _currentSpeed > 1 || _inGravity && _currentHeight > 20 && !LandingLocked())
            {
                Echo("Vehicle cannot be switched off while in motion");
                _autoPilot = Pilot.Disabled;
                EnableDampeners(true);
                return;
            }

            var blocksOff = new List<IMyTerminalBlock>();

            foreach (var funcBlock in _gridBlocks.OfType<IMyFunctionalBlock>().Where(funcBlock => funcBlock != Me))
            {
                if (funcBlock is IMyBatteryBlock || funcBlock is IMySolarPanel || funcBlock is IMyLandingGear ||
                    funcBlock is IMyGasTank || funcBlock is IMyMedicalRoom || funcBlock is IMyDoor ||
                    funcBlock.BlockDefinition.TypeIdString.Substring(16).Equals("WindTurbine",StringComparison.OrdinalIgnoreCase) ||
                    funcBlock is IMyShipConnector || funcBlock is IMyButtonPanel || funcBlock is IMyJumpDrive)
                {
                    funcBlock.Enabled = true;
                    var battery = funcBlock as IMyBatteryBlock;
                    var drive = funcBlock as IMyJumpDrive;
                    if (drive != null && _currentMode == ProgramState.Docked && _rechargeWhenConnected)
                    {
                        drive.Enabled = true;
                        continue;
                    }

                    if (battery != null)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                        continue;
                    }
                    var tank = funcBlock as IMyGasTank;
                    if (tank != null) tank.Stockpile = true;
                    continue;
                }

                blocksOff.Add(funcBlock);
            }

            if (!_isStatic)
                LockLandingGears();

            BlockGroupEnable(blocksOff,false);

            _scheduler.Reset();

        }


        private void CheckProjection()
        {
            if (_myProjector == null || !_handleRepair) return;
            DateTime time;
            if (!_collection.TryGetValue(_myProjector, out time))
            {
                if (!_damageDetected && _alert != AlertState.Severe)
                {
                    _myProjector.Enabled = false;
                    return;
                }

                _myProjector.Enabled = true;
                _collection.Add(_myProjector,DateTime.Now);
                return;
            }
            if (_myProjector.RemainingBlocks > 0 || _damageDetected)
            {
                _collection[_myProjector] = DateTime.Now;
                return;
            }

            if ((DateTime.Now - time).TotalSeconds < ProjectorShutoffDelay) return;
            _myProjector.Enabled = false;
            _collection.Remove(_myProjector);
        }


        private static void BlockGroupEnable(IEnumerable<IMyCubeBlock> groupBlocks, bool on = true)
        {
            var myCubeBlocks = groupBlocks as IMyCubeBlock[] ?? groupBlocks?.ToArray();
            if (myCubeBlocks == null || !myCubeBlocks.Any())return;
            foreach (var block in myCubeBlocks.OfType<IMyFunctionalBlock>().Where(block=>!Closed(block))) block.Enabled = on;
        }

        private bool TryGetRemote(out IMyRemoteControl remote)
        {
            if (!_remotes.Any())
            {
                remote = null;
                return false;
            }

            remote = _remotes.FirstOrDefault(x => x?.IsFunctional== true && !SkipBlock(x));

            return remote != null;

        }

        private bool TryGetAntenna(out IMyRadioAntenna antenna)
        {
            var antennas = new List<IMyRadioAntenna>();

            antennas.AddRange(_gridBlocks.OfType<IMyRadioAntenna>());

            if (!antennas.Any())
            {
                antenna = null;
                return false;
            }

            antenna = antennas.FirstOrDefault(x => x.IsFunctional && !SkipBlock(x));
            return antenna != null;
        }


        private void Setup()
        {
            ParseIni();
            _damageDetected = NeedsRepair(out _damagedBlocks);
            if (_handleRepair)BlockGroupEnable(_shipWelders, _damageDetected || _alert > AlertState.High || _shipWelders.Any(w=>w.IsWorking));

            if (!_powerManagement || !_fuelCollection.Any())return;
            if (_fuelCollection.Values.Sum(x=> (double)x) < _lowFuel || BatteryLevel() < _rechargePoint)
                _powerFlagDelay = DateTime.Now;
            _powerFlag = (DateTime.Now - _powerFlagDelay).TotalSeconds < 15;
            if (_gravGens?.Any() == true) BlockGroupEnable(_gravGens, !_powerFlag);
        }

        private void CurrentState(string st, out ProgramState result)
        {
            var t = st.Split('"', ' ');
            while (true)
            {
                if (t[0].Equals("pause",StringComparison.OrdinalIgnoreCase)||t[0].Equals("stop",StringComparison.OrdinalIgnoreCase))
                {
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    _currentMode = ProgramState.Stop;
                    break;
                }

                if (t[0].Equals("start",StringComparison.OrdinalIgnoreCase))
                {
                    ParseIni();
                    _currentMode = ProgramState.Start;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    break;
                }

                if (t[0].Equals("takeoff", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_autoNavigate || _remoteControl == null)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    EnableDampeners(true);

                    _giveControl =t.Length > 1 && bool.TryParse(t[1], out _giveControl) && _giveControl;
                    _cruiseSpeed = t.Length > 2 &&double.TryParse(t[2], out _cruiseSpeed) ? _cruiseSpeed : 100;
                    _takeOffAngle = t.Length > 3 && double.TryParse(t[3], out _takeOffAngle) ? _takeOffAngle : 0;
                    _autoPilot = Pilot.Takeoff;
                    break;

                }

                if (t[0].Equals("dock", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsConnected())
                    {
                        _currentMode = ProgramState.Docked;
                        break;
                    }
                }


                if (t[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    if (t[1].Equals("land", StringComparison.OrdinalIgnoreCase))
                    {
                        EndTrip();
                        break;
                    }

                    if (t[1].Equals("takeoff", StringComparison.OrdinalIgnoreCase))
                    {
                        EndTrip();
                        break;
                    }

                    if (t[1].Equals("cruise", StringComparison.OrdinalIgnoreCase))
                    {
                        EndTrip();
                        break;
                    }

                    if (t[1].Equals("trip", StringComparison.OrdinalIgnoreCase))
                    {
                        _autoPilot = Pilot.Disabled;
                        foreach (var remote in _remotes.Where(remote => !Closed(remote)))
                            remote.SetAutoPilotEnabled(false);
                        EndTrip();
                    }
                    break;
                }

                if (t[0].Equals("land",StringComparison.OrdinalIgnoreCase))
                {
                    if (!_autoNavigate)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    _autoPilot = _inGravity ? Pilot.Land : Pilot.Disabled;
                    break;
                }

                if (t[0].Equals("cruise", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_autoNavigate)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    double setHeight;
                    double setSpeed;
                    bool giveControl;
                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    _thrust = 0;
                    _autoPilot = Pilot.Cruise;
                    EnableDampeners(true);

                    _giveControl = t.Length > 1 && bool.TryParse(t[1], out giveControl) && giveControl;
                    _cruiseSpeed = t.Length > 2 && double.TryParse(t[2], out setSpeed) ? setSpeed : _currentSpeed;
                    _cruiseHeight = t.Length > 3 && double.TryParse(t[3], out setHeight) ? setHeight : _currentHeight;
                    _cruiseDirection = t.Length > 4 &&!string.IsNullOrEmpty(t[3]) ? t[4] : "forward";
                    break;
                }

                if (t[0].Equals("power", StringComparison.OrdinalIgnoreCase))
                {
                    if (t[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                        _currentMode = ProgramState.ShutOff;
                    if (t[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                        _currentMode = ProgramState.PowerOn;
                    if (t[1].Equals("recharge", StringComparison.OrdinalIgnoreCase))
                    {
                        _setRechargeState = false;
                        _currentMode = ProgramState.Recharge;
                    }
                    break;


                }



                break;
            }

            result = _currentMode;
        }


        private bool GridFlags(out AlertState state)
        {

            if (_combatFlag)
            {
                state = AlertState.Severe;
                return true;
            }

            if (_damageDetected)
            {
                state = AlertState.High;
                return true;
            }

            if (_powerFlag)
            {
                state = AlertState.Elevated;
                return true;
            }

            if (_navFlag || _productionFlag)
            {
                state = AlertState.Guarded;
                return true;
            }

            state = AlertState.Clear;
            return false;
        }




        /// <summary>
        /// prints alerts
        /// </summary>
        private void Alerts()
        {
            _sbPower.Clear();
            _sbDamages.Clear();
            _sbStatus.Clear();
            _sbDebug.Clear();
            _sbInfo.Clear();
            _sbStatus.Append("AI Running ");
            switch (_alertCounter % 6)
            {
                case 0:
                    _sbInfo.Append("--");
                    break;
                case 1:
                    _sbStatus.Append("\\");
                    break;
                case 2:
                    _sbStatus.Append(" | ");
                    break;
                case 3:
                    _sbStatus.Append("/");
                    break;
            }
            _alertCounter = _alertCounter < 10 ? _alertCounter += 1 : 0;
            _sbStatus.AppendLine();
            _sbStatus.Append($"Hive Status: {_hive}");
            _sbStatus.AppendLine();

            _sbPower.AppendLine($"PowerFlag = {_powerFlag}");
            float j;
            _sbPower.AppendLine($"PowerOverload = {IsOverload(out j)}");
            _sbPower.AppendLine($"PowerUsage = {Math.Round(j * 100),0}%");
            _sbPower.AppendLine($"Battery Charge = {Math.Round(BatteryLevel() * 100),0}%");
            _sbPower.AppendLine(
                $"Reactors Active = {_reactors.Where(x => x.IsWorking).ToList().Count} of {_reactors.Count}");


            if (_alert > AlertState.Clear)
            {
                _sbStatus.Append("Warning: Grid Error Detected!");
                _sbStatus.AppendLine();

                if (_powerFlag)
                {
                    _sbStatus.Append(" Power status warning!");
                    _sbStatus.AppendLine();
                }
            }

            if (_myProjector != null && _myProjector.IsProjecting)
            {
                _sbStatus.Append($"{_myProjector.CustomName} is active for repairs");
                _sbStatus.AppendLine();
            }

            if (_combatFlag)
            {
                _sbStatus.Append("Engaging Hostiles!");
                _sbStatus.AppendLine();
                _sbStatus.AppendLine(" Aggression Level: " + _aggression);
            }

            if (_damagedBlocks.Any())
            {
                _sbDamages.AppendLine($"{_damagedBlocks.Count} blocks damaged");
                foreach (var block in _damagedBlocks)
                {
                    _sbDamages.AppendLine($"->{block.CustomName}");
                }
            }

            _sbInfo.AppendLine($"{_runtimeTracker.Write()}");

            if (_lowBlocks == null || _lowBlocks?.Keys.OfType<IMyBatteryBlock>().Any() == false) return;
            double lowBatteriesCount = _lowBlocks.Keys.OfType<IMyBatteryBlock>().Count();
            double bat = _batteries.Count;
            _sbPower.Append($"{lowBatteriesCount} of {bat} batteries in recharge");
            _sbPower.AppendLine();
            foreach (var battery in _lowBlocks.Keys.OfType<IMyBatteryBlock>())
            {
                if (SkipBlock(battery) || !battery.IsFunctional)
                {
                    _lowBlocks.Remove(battery);
                    continue;
                }
                _sbPower.AppendLine(
                    $"->{battery.CustomName} [{battery.ChargeMode}] - {Math.Round(battery.CurrentStoredPower / battery.MaxStoredPower * 100)}%");
            }


        }




        /// <summary>
        /// Sets off alarms
        /// </summary>
        private void AlertLights()
        {
            Color color;
            switch (_alert)
            {
                case AlertState.Clear:
                    color = Color.Green;
                    break;
                case AlertState.Guarded:
                    color = Color.Blue;
                    break;
                case AlertState.Elevated:
                    color = Color.Yellow;
                    break;
                case AlertState.High:
                    color = Color.Orange;
                    break;
                case AlertState.Severe:
                    color = Color.Red;
                    break;
                default:
                    color = Color.Green;
                    break;
            }

            foreach (var light in _lights.Where(light => !Closed(light) && StringContains(light.CustomName, "alert")))
                SetLight(light, color, _alert > AlertState.Clear);

            foreach (var soundBlock in _soundBlocks.Where(soundBlock =>
                !Closed(soundBlock) && StringContains(soundBlock.CustomName, "alarm")))
            {
                DateTime time;
                if (!_collection.TryGetValue(soundBlock, out time))
                {
                    if (_alert != AlertState.Severe)
                    {
                        soundBlock.Enabled = false;
                        soundBlock.Stop();
                        continue;
                    }
                    soundBlock.Enabled = true;
                    _collection.Add(soundBlock, DateTime.Now);
                    soundBlock.Play();
                    soundBlock.LoopPeriod = 5f;
                    continue;
                }


                if ((DateTime.Now - time).TotalSeconds < 30) continue;

                if (_alert == AlertState.Severe)
                {
                    _collection[soundBlock] = DateTime.Now;
                    soundBlock.Play();
                    continue;
                }

                soundBlock.Stop();
                _collection.Remove(soundBlock);

            }


        }

        /// <summary>
        /// Set light color for alert lights
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="blink"></param>
        private static void SetLight(IMyLightingBlock x, Color y, bool blink)
        {
            var rotatingLight = StringContains(x.BlockDefinition.SubtypeId, "rotatinglight");
            x.Enabled = !rotatingLight || y != Color.Green;
            x.Color = y;
            x.BlinkIntervalSeconds = blink && !rotatingLight ? 1 : 0;
            x.Radius = blink ? 6 : 4;
            x.BlinkLength = 50;
        }

        /// <summary>
        /// Caps Fuel
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void CapFuel(float startProportion, float endProportion)
        {
            if (!_capReactors || !_reactors.Any())return;

            var start = (int) (startProportion * _reactors.Count);
            var end = (int) (endProportion * _reactors.Count);
            for (var i = start; i < end; ++i)
            {
                var reactor = _reactors[i];
                if (Closed(reactor) || reactor == null)
                {
                    _reactors.Remove(reactor);
                    continue;
                }

                //MyItemType fuel = null;

                var fuel = GetFuel(reactor.BlockDefinition.SubtypeId);

                //if (fuel == null)continue;

                MyFixedPoint count;


                if (!_fuelCollection.TryGetValue(fuel, out count))
                {
                    reactor.UseConveyorSystem = (double)reactor.GetInventory().GetItemAmount(fuel) < _lowFuel;
                    continue;
                }

                reactor.UseConveyorSystem = false;

                var lowCap = (double)count / _reactors.Count < _lowFuel
                    ? (MyFixedPoint) ((double)count  / _reactors.Count)
                    : (int) _lowFuel;
                var reactorFuel = reactor.GetInventory().GetItemAmount(fuel);
                if (Math.Abs((double) (reactorFuel - lowCap)) <= 0.1*(double)lowCap) continue;
                var y = (MyFixedPoint) Math.Abs((double) (reactorFuel - lowCap));

                var transferBlocks = _gridBlocks?.Where(block =>
                        block.HasInventory && block.GetInventory().IsConnectedTo(reactor.GetInventory()))
                    .ToList();

                if (transferBlocks?.Any()==false)continue;

                IMyTerminalBlock transferBlock;

                MyInventoryItem? z;
                if (reactorFuel > lowCap)
                {
                    transferBlock = transferBlocks?.FirstOrDefault(block =>
                        !block.GetInventory().IsFull &&
                        reactor.GetInventory().CanTransferItemTo(block.GetInventory(), fuel));
                    z = reactor.GetInventory().FindItem(fuel);
                    if(z == null)continue;
                    try
                    {
                        reactor.GetInventory().TransferItemTo(_dump.FirstOrDefault(x=>x.GetInventory().IsConnectedTo(reactor.GetInventory()))?.GetInventory(), z.Value, y);
                    }
                    catch (Exception e)
                    {
                        _sbStatus.AppendLine(e.ToString());
                    }
                    continue;
                }
                transferBlock = transferBlocks?.FirstOrDefault(block =>
                        block.GetInventory().CanTransferItemTo(reactor.GetInventory(), fuel) &&
                        block.GetInventory().ContainItems(y, fuel));
                z = transferBlock?.GetInventory().FindItem(fuel);
                if (z== null)continue;
                transferBlock.GetInventory().TransferItemTo(reactor.GetInventory(), z.Value, y);
            }
        }

        private MyItemType GetFuel(string reactorSubId)
        {
            MyItemType fuel;
            if (_reactorFuel.TryGetValue(reactorSubId, out fuel)) return fuel;

            fuel = MyItemType.MakeIngot("Uranium");

            foreach (var reactor in _reactors)
            {
                if (reactor.BlockDefinition.SubtypeId != reactorSubId) continue;
                if (reactor.GetInventory().ItemCount == 0)continue;
                var fuels = new List<MyInventoryItem>();

                reactor.GetInventory().GetItems(fuels);

                if (fuels.Count > 0)
                {
                    reactor.UseConveyorSystem = false;
                    fuel = fuels.FirstOrDefault().Type;
                    _reactorFuel[reactorSubId] = fuel;

                    break;
                }

                reactor.UseConveyorSystem = true;

            }

            return fuel;
        }

        private void FindFuel()
        {
            _fuelCollection.Clear();
            var usedFuel = _reactorFuel.Values.ToList();

            if (usedFuel?.Any() == false)
            {
                return;
            }

            var inventBlocks = new List<IMyTerminalBlock>();

            inventBlocks.AddRange(_gridBlocks.Where(x => !Closed(x) && x.HasInventory && x.GetInventory().ItemCount != 0));


            if (inventBlocks?.Any() == false)
            {
                return;
            }


            foreach (var item in usedFuel)
            {
                MyFixedPoint count = 0;

                foreach (var block in inventBlocks)
                {
                    if (Closed(block) || !block.GetInventory().ContainItems(1, item)) continue;
                    var itemCount = block.GetInventory().GetItemAmount(item);

                    if (itemCount < 1) continue;
                    count += itemCount;
                }

                _fuelCollection[item] = count;
            }

        }



        /// <summary>
        /// Circles through the batteries to maintain charge and power
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateBatteries(float startProportion, float endProportion)
        {
            if (_batteries?.Any() == false || !_powerManagement ||_batteries ==null) return;
            var highestCharge = _highestChargedBattery?.CurrentStoredPower / _highestChargedBattery?.MaxStoredPower ??
                                0f;

            var maxRechargeBatteries = _isStatic ? Math.Round(0.35 * _batteries.Count,0):Math.Round(0.15 * _batteries.Count,0);

            var start = (int) (startProportion * _batteries.Count);
            var end = (int) (endProportion * _batteries.Count);

            for (var i = start; i < end; ++i)
            {
                var allowRecharge = _lowBlocks.Keys.OfType<IMyBatteryBlock>().Count() < maxRechargeBatteries;
                var battery = _batteries[i];
                if (SkipBlock(battery) || !battery.IsFunctional)
                {
                    _lowBlocks.Remove(battery);
                    continue;
                }

                battery.Enabled = true;

                float charge;

                if (_lowBlocks.TryGetValue(battery, out charge))
                {
                    if (battery == _highestChargedBattery || (battery.CurrentStoredPower / battery.MaxStoredPower) >= charge || _autoPilot > Pilot.Disabled || _currentSpeed > 25)
                    {
                    
                        _lowBlocks.Remove(battery);
                    }

                    battery.ChargeMode = ChargeMode.Recharge;
                    continue;
                }

                if (battery.CurrentStoredPower / battery.MaxStoredPower > highestCharge ||
                    (_highestChargedBattery == null && battery.HasCapacityRemaining))
                {
                    _highestChargedBattery = battery;
                    continue;
                }

                if (_currentMode == ProgramState.Recharge)
                {
                    _lowBlocks[battery] = 1f;
                    continue;
                }

                if (allowRecharge && (!battery.HasCapacityRemaining||battery.CurrentStoredPower / battery.MaxStoredPower < _rechargePoint ))
                {
                    _lowBlocks[battery] = 0.5f;
                    continue;
                }

                battery.ChargeMode = _isStatic && BatteryLevel() > Math.Max(_rechargePoint,0.5f) ? ChargeMode.Discharge : ChargeMode.Auto;


            }

            _batteryHighestCharge = highestCharge > _rechargePoint ? highestCharge - 0.1f : 1f;
        }


        /// <summary>
        /// Circles through reactors and turns them on if needed
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateReactors(float startProportion, float endProportion)
        {
            float meh;
            var overload = IsOverload(out meh);
            var reactors = _reactors.Where(reactor => !SkipBlock(reactor) && reactor.GetInventory().ItemCount != 0)
                .ToList();

            var start = (int) (startProportion * reactors.Count);
            var end = (int) (endProportion * reactors.Count);
            for (var i = start; i < end; ++i)
            {
                var reactor = reactors[i];
                reactor.Enabled = overload || _powerFlag;
            }
        }

        private bool IsOverload(out float power)
        {
            double currentPower = 0;
            double maxPower = 0;

            foreach (var block in _powerBlocks.Where(block => !SkipBlock(block) && block.Enabled))
            {
                var batteryBlock = block as IMyBatteryBlock;
                if (batteryBlock != null && (batteryBlock.IsCharging ||
                                             !batteryBlock.HasCapacityRemaining)) continue;
                currentPower += block.CurrentOutput;
                maxPower += block.MaxOutput;
            }

            power = (float) (currentPower / maxPower);
            return power >= _overload;
        }

        private bool LandingLocked()
        {
            return !_isStatic && _landingGears.Any(landingGear => landingGear.IsLocked);
        }

        private bool IsConnected()
        {
            return _connectors.Any(connector => connector.Status == MyShipConnectorStatus.Connected);
        }

        private bool IsDocked()
        {
            return !_isStatic && _connectors.Any(connector =>
                       connector.Status == MyShipConnectorStatus.Connected &&
                       connector.OtherConnector.CubeGrid.IsStatic);
        }


        /// <summary>
        /// Checks and updates ventilation
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateVents(float startProportion, float endProportion)
        {
            if (_airVents?.Any() == false || !_controlVents || _airVents == null) return;
            var vents = _airVents.Where(vent => !SkipBlock(vent) && !StringContains(vent.CustomData, "outside"))
                .ToList();

            var start = (int) (startProportion * vents.Count);
            var end = (int) (endProportion * vents.Count);
            for (var i = start; i < end; ++i)
            {
                var vent = vents[i];
                if (vent.Depressurize)
                {
                    vent.Enabled = true;
                    continue;
                }
                if (!vent.CanPressurize)
                {
                    if (_showOnHud)vent.ShowOnHUD = true;
                    _sbStatus.Append(vent.CustomName + " Can't Pressurize");
                    _sbStatus.AppendLine();
                    continue;
                }

                vent.Enabled = IsNeedAir(vent);
            }
        }

        private static bool IsNeedAir(IMyAirVent vent)
        {
            var powerState = vent.Enabled;
            vent.Enabled = true;
            var oxygenState = vent.GetOxygenLevel();
            vent.Enabled = powerState;
            return oxygenState < 0.75f;
        }

        private  bool IsConnectedToStatic()
        {
            return _allBlocks.Any(x => x.CubeGrid.IsStatic);
        }

        /// <summary>
        /// check if grid needs repair and returns damaged blocks
        /// </summary>
        /// <param name="damagedBlocks"></param>
        /// <returns></returns>
        private bool NeedsRepair(out List<IMyTerminalBlock> damagedBlocks)
        {
            damagedBlocks = new List<IMyTerminalBlock>(50);
            if (!_allBlocks.Any()) return false;
            damagedBlocks.Clear();
            var dam = _allBlocks?.Where(block =>
                !Closed(block) && (block.IsBeingHacked || block.CubeGrid.GetCubeBlock(block.Position).CurrentDamage > 0));
            damagedBlocks.AddRange(dam);

            if (!_damagedBlocks.Any())
            {
                return false;
            }

            foreach (var block in damagedBlocks)
            {
                if (_showOnHud)block.ShowOnHUD = true;
                _sbDamages.AppendLine(block.CustomName + " is damaged and needs repair!");
            }

            return true;
        }

        #region MyRegion
        /// <summary>
        /// Checks and updates production blocks
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateProduction(float startProportion, float endProportion)
        {
            if (_productionBlocks?.Any() == false || !_controlProduction || _productionBlocks == null) return;
            var prodBlocks = _productionBlocks?.Where(block => !SkipBlock(block)).ToList();
            if (!prodBlocks.Any())return;

            var start = (int) (startProportion * prodBlocks.Count);
            var end = (int) (endProportion * prodBlocks.Count);
            for (var i = start; i < end; ++i)
            {
                var block = prodBlocks[i];

                if (!block.IsQueueEmpty) block.Enabled = true;

                DateTime time;
                if (!_collection.TryGetValue(block, out time))
                {
                    if (!block.Enabled)
                    {
                        if (block.GetInventory().ItemCount !=0)
                            EmptyProductionBlock(block);
                        continue;
                    }
                    _collection.Add(block,DateTime.Now);
                    continue;
                }
                if (block.IsProducing || !block.IsQueueEmpty)
                {
                    _collection[block] = DateTime.Now;
                    continue;
                }

                if ((DateTime.Now - time).TotalSeconds < ProductionDelay) continue;
                block.Enabled = false;
                _collection.Remove(block);
            }
        }

        private void EmptyProductionBlock(IMyProductionBlock block)
        {
            var someCargo = _dump.Where(x=>!Closed(x) && !x.GetInventory().IsFull).ToList();
            var assembler = block as IMyAssembler;
            var meh = new List<MyInventoryItem>();
            var cargo = someCargo.FirstOrDefault(x =>
                x.GetInventory().IsConnectedTo(block.GetInventory()));
            if (assembler != null && someCargo.Any())
            {
                assembler.InputInventory.GetItems(meh);
                foreach (var item in meh.TakeWhile(item => cargo != null))
                {
                    try
                    {
                        assembler.InputInventory.TransferItemTo(cargo?.GetInventory(),item,item.Amount);
                    }
                    catch (Exception e)
                    {
                        _sbStatus.Append(e);
                    }
                }
            }
            meh.Clear();
            block.OutputInventory.GetItems(meh);
            foreach (var item in meh.TakeWhile(item => cargo!=null))
            {
                try
                {
                    block.OutputInventory.TransferItemTo(cargo?.GetInventory(),item,item.Amount);
                }
                catch (Exception e)
                {
                    _sbStatus.Append(e);
                }
            }

        }



        private void UpdateGasGen(float startProportion, float endProportion)
        {
            if (!_controlGasSystem || _gasTanks.Count == 0 || !_gasGens.Any())return;

            var start = (int) (startProportion * _gasGens.Count);
            var end = (int) (endProportion * _gasGens.Count);

            
            for (int i = start; i < end; i++)
            {
                var gen = _gasGens[i];
                gen.Enabled = _tankRefill;
            }


        }

        /// <summary>
        /// Updates gas tanks
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateTanks(float startProportion, float endProportion)
        {
            if (_gasTanks?.Any() == false || !_controlGasSystem) return;
            var gasTanks = _gasTanks?.Where(tank =>!SkipBlock(tank)).ToList();
            if (gasTanks == null || !gasTanks.Any()) return;
            var genOnline = _gasGens != null && _gasGens.Any(x => x.IsWorking);
            var start = (int) (startProportion * gasTanks.Count);
            var end = (int) (endProportion * gasTanks.Count);
            var refill = _rechargeWhenConnected && IsDocked() ? 1f : (float)_tankFillLevel;
            for (var i = start; i < end; ++i)
            {
                var tank = gasTanks[i];
                float value;
                if (!_lowBlocks.TryGetValue(tank, out value))
                {
                    if (tank.FilledRatio >= refill)
                    {
                        tank.Stockpile = false;
                        tank.Enabled = !genOnline || (_inGravity && !_isStatic) || _currentSpeed > 5;
                        continue;
                    }
                    tank.Enabled = true;
                    tank.Stockpile = _isStatic;
                    _lowBlocks.Add(tank,refill);
                    continue;
                }
                if (tank.FilledRatio < value)continue;
                tank.Stockpile = false;
                _lowBlocks.Remove(tank);
            }

            _tankRefill =_lowBlocks.Keys.OfType<IMyGasTank>().Any();
        }

        

        #endregion





        /// <summary>
        /// Controls Doors
        /// </summary>
        private void AutoDoors()
        {
            if (!_enableAutoDoor) return;
            foreach (var door in _doors.Where(door => !(door is IMyAirtightHangarDoor) && !SkipBlock(door)))
            {
                DateTime time;
                if (!_collection.TryGetValue(door, out time))
                {
                    if (door.Status == DoorStatus.Closed || door.Status == DoorStatus.Closing) continue;
                    _collection.Add(door, DateTime.Now);
                    continue;
                }
                if ((DateTime.Now - time).TotalSeconds < _doorDelay) continue;
                door.CloseDoor();
                _collection.Remove(door);
            }
        }

        #region Turret management

        private readonly MyIni _turretIni = new MyIni();
        private readonly List<string> _turretIniSections = new List<string>();
        private readonly StringBuilder _turretIniCustomDataSb = new StringBuilder();


        private const string INI_SECTION_TURRETMAIN = "Turret Settings";
        private const string INI_TURRETMAIN_TURRETAGGRESION = "Turret Aggression Trigger";
        private const string INI_TURRETMAIN_TURRETTARGET = "Turret Duty";
        private const string INI_SECTION_ROTATION = "Turret Rotation Limits (Radian)";
        private const string INI_ROTATION_AZIMAX = "Azmimuth Max";
        private const string INI_ROTATION_AZIMIN = "Azmimuth Min";
        private const string INI_ROTATION_ELEVMAX = "Elevation Max";
        private const string INI_ROTATION_ELEVMIN = "Elevation MIn";

        private static double _azimuthMax = Math.Round(Math.PI,2);
        private static double _azimuthMin = Math.Round(-Math.PI,2);
        private static double _elevationMax = Math.Round(Math.PI,2);
        private static double _elevationMin = Math.Round(-0.5 *Math.PI,2);
        private static string _turretDuty;
        private static int _aggressionTrigger = 0;


        private void TurretParseIni(IMyLargeTurretBase turret)
        {
            _turretIni.Clear();
            _turretIni.TryParse(turret.CustomData);

            var minPriority = Math.Max(_defaultAggression * _aggressionMultiplier, _turrets.Count/2);
            var priority = new Random().Next(_defaultAggression + 1, minPriority);

            _turretIniSections.Clear();
            _turretIni.GetSections(_turretIniSections);

            if (_turretIniSections?.Any() == false)
            {
                _turretIniCustomDataSb.Clear();
                _turretIniCustomDataSb.Append(turret.CustomData);
                _turretIniCustomDataSb.Replace("---\n", "");

                _turretIni.EndContent = _turretIniCustomDataSb.ToString();
            }

            //Get
            _turretDuty = _turretIni.Get(INI_SECTION_TURRETMAIN, INI_TURRETMAIN_TURRETTARGET).ToString(_turretDuty);
            _aggressionTrigger = _turretIni.Get(INI_SECTION_TURRETMAIN, INI_TURRETMAIN_TURRETAGGRESION).ToInt32(_aggressionTrigger);

            _azimuthMin = _turretIni.Get(INI_SECTION_ROTATION, INI_ROTATION_AZIMIN).ToDouble(_azimuthMin);
            _azimuthMax = _turretIni.Get(INI_SECTION_ROTATION, INI_ROTATION_AZIMAX).ToDouble(_azimuthMax);
            _elevationMin = _turretIni.Get(INI_SECTION_ROTATION, INI_ROTATION_ELEVMIN).ToDouble(_elevationMin);
            _elevationMax = _turretIni.Get(INI_SECTION_ROTATION, INI_ROTATION_ELEVMAX).ToDouble(_elevationMax);

            if (_aggressionTrigger > _turrets.Count / 2 ||_aggressionTrigger == 0 && string.IsNullOrEmpty(_turretDuty))
            {
                _aggressionTrigger = priority;
            }


            //Set
            _turretIni.Set(INI_SECTION_TURRETMAIN,INI_TURRETMAIN_TURRETTARGET, _turretDuty);
            _turretIni.Set(INI_SECTION_TURRETMAIN,INI_TURRETMAIN_TURRETAGGRESION,_aggressionTrigger);
            _turretIni.Set(INI_SECTION_ROTATION,INI_ROTATION_AZIMIN, _azimuthMin);
            _turretIni.Set(INI_SECTION_ROTATION,INI_ROTATION_AZIMAX, _azimuthMax);
            _turretIni.Set(INI_SECTION_ROTATION,INI_ROTATION_ELEVMIN, _elevationMin);
            _turretIni.Set(INI_SECTION_ROTATION,INI_ROTATION_ELEVMAX, _elevationMax);
            

            var output = _turretIni.ToString();
            if (!string.Equals(output, turret.CustomData))
            {
                turret.CustomData = output;
            }


        }

        private static void TurretSettingsDefault()
        {
            _turretDuty = null;
            _aggressionTrigger = 0;

            _azimuthMin = Math.Round(-Math.PI,2);
            _azimuthMax = Math.Round(Math.PI,2);

            _elevationMin = Math.Round(-0.5 * Math.PI,2);
            _elevationMax = Math.Round(Math.PI,2);


        }


        
        /// <summary>
        /// Checks if target is in turret's sight.
        /// </summary>
        /// <param name="turret"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        private static bool InSight(IMyLargeTurretBase turret, MyDetectedEntityInfo target)
        {
            var aziVector = Vector3D.Cross(turret.WorldMatrix.Forward, turret.WorldMatrix.Left);
            var eleVector = Vector3D.Cross(turret.WorldMatrix.Forward, turret.WorldMatrix.Up);

            var targetForwardAngle = VectorMath.AngleBetween(eleVector, target.Position);
            var targetSideAngle = VectorMath.AngleBetween(aziVector, target.Position);
            var targetDistance = Vector3D.Distance(turret.GetPosition(), target.Position);

            return targetForwardAngle <= _elevationMax && targetForwardAngle >= _elevationMin  && targetSideAngle <= _azimuthMax && targetSideAngle >= _azimuthMin  && targetDistance <= turret.Range;
        }
        
        /// <summary>
        /// Controls Turrets
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void ManageTurrets(float startProportion, float endProportion)
        {
            if (_turrets?.Any() == false || !_turretControl || _turrets == null)
                return;

            var turrets = _turrets.Where(turret => !SkipBlock(turret)).ToList();
            var start = (int) (startProportion * turrets.Count);
            var end = (int) (endProportion * turrets.Count);
            for (var i = start; i < end; ++i)
            {
                var turret = turrets[i];

                TurretParseIni(turret);
                
                if (!turret.IsAimed && !turret.IsUnderControl && turret.IsShooting)turret.SetValueBool("Shoot",false);

                if (!string.IsNullOrEmpty(_turretDuty))
                {
                    SetTurret(turret);
                    if (!turret.HasTarget)
                    {
                        DateTime time;
                        if (!_collection.TryGetValue(turret, out time))
                        {
                            _collection[turret] = DateTime.Now;
                            continue;
                        }
                        if (Math.Abs((DateTime.Now - time).TotalSeconds) >= 1) continue;
                        _collection.Remove(turret);
                        turret.ResetTargetingToDefault();
                    }
                    TurretSettingsDefault();
                    continue;
                }

                /*
                //Make sure designators are always online
                if (StringContains(turret.CustomName, _designatorName) ||
                    StringContains(turret.CustomName, _antipersonnelName) ||
                    StringContains(turret.CustomName, _antimissileName))
                {
                    SetTurret(turret);
                    if (!turret.HasTarget)
                    {
                        DateTime time;
                        if (!_collection.TryGetValue(turret, out time))
                        {
                            _collection[turret] = DateTime.Now;
                            continue;
                        }
                        if (Math.Abs((DateTime.Now - time).TotalSeconds) >= 1) continue;
                        _collection.Remove(turret);
                        turret.ResetTargetingToDefault();
                    }
                    TurretSettingsDefault();
                    continue;
                }
                */
                
                //compare number in custom data to aggression and turn off if higher
                turret.Enabled = Math.Abs(turret.Elevation) > 0 || Math.Abs(turret.Azimuth) > 0 || _aggressionTrigger < _aggression || turret.IsUnderControl;


                if ((!turret.HasTarget || !turret.IsAimed ||
                     turret.GetTargetedEntity().Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) &&
                    turret.Enabled)
                {
                    Refocus(turret);
                }

                TurretSettingsDefault();

                if (turret.GetInventory().ItemCount != 0 || !turret.HasInventory || !_showOnHud) continue;
                turret.ShowOnHUD = true;
            }
        }

        /// <summary>
        /// Sets target for turrets
        /// </summary>
        private void Refocus(IMyLargeTurretBase turret)
        {
            if (turret.IsShooting && !turret.IsUnderControl)turret.SetValueBool("Shoot",false);

            if (_myTargets?.Any(x => x.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies) == false)
            {
                turret.ResetTargetingToDefault();
                turret.EnableIdleRotation = false;
                turret.Elevation = 0;
                turret.Azimuth = 0;
                turret.SyncAzimuth();
                turret.SyncElevation();
                return;
            }


            for (var i = 0; i < _myTargets?.Count; i++)
            {
                var target = _myTargets[i];
                if (!InSight(turret,target))continue;
                turret.SetTarget(target.Position);
                return;
            }
        }

        /// <summary>
        /// Set custom turrets targets
        /// </summary>
        /// <param name="turret"></param>
        private void SetTurret(IMyLargeTurretBase turret)
        {
            turret.Enabled = true;

            if (string.IsNullOrEmpty(_turretDuty))return;

            if (_turretDuty.Equals(_designatorName, StringComparison.OrdinalIgnoreCase))
            {
                turret.SetValueBool("TargetCharacters",false);
                turret.SetValueBool("TargetLargeShips",true);
                turret.SetValueBool("TargetStations",true);
                turret.SetValueBool("TargetSmallShips",true);
                turret.SetValueBool("TargetMissiles",false);
                return;
            }

            if (_turretDuty.Equals(_antipersonnelName, StringComparison.OrdinalIgnoreCase))
            {
                turret.SetValueBool("TargetCharacters",true);
                turret.SetValueBool("TargetLargeShips",false);
                turret.SetValueBool("TargetStations",false);
                turret.SetValueBool("TargetSmallShips",false);
                turret.SetValueBool("TargetMissiles",false);
                return;
            }

            if (_turretDuty.Equals(_antimissileName, StringComparison.OrdinalIgnoreCase))
            {
                turret.SetValueBool("TargetCharacters",false);
                turret.SetValueBool("TargetLargeShips",false);
                turret.SetValueBool("TargetStations",false);
                turret.SetValueBool("TargetSmallShips",false);
                turret.SetValueBool("TargetMissiles",true);
            }
        }


        /// <summary>
        /// Handles aggression levels
        /// </summary>
        private void AggroBuilder()
        {
            if (_combatFlag && _myAntenna != null)
            {
                if (_hive)
                {
                    _myAntenna.Radius = 5000f;
                    _myAntenna.Enabled = true;
                    _myAntenna.EnableBroadcasting = true;
                }
                else
                {
                    _myAntenna.Radius = 500f;
                }
            }
            if (_hive)
            {
                if (_combatFlag) AttackTarget();
                else
                    FollowMe();
            }

            _myTargets.Clear();
            _aggression = HasTarget(out _myTargets)
                ? Math.Min(_aggression += 1, _turrets.Count)
                : Math.Max(_aggression -= 2, _defaultAggression);

            _combatFlag = _aggression > _defaultAggression;

        }







        #endregion



        private float BatteryLevel()
        {
            float juice = 0;
            float totalJuice = 0;
            foreach (var battery in _batteries.Where(battery => !SkipBlock(battery)))
            {
                juice += battery.CurrentStoredPower;
                totalJuice += battery.MaxStoredPower;
            }

            return juice / totalJuice;
        }

        private void CheckConnectors()
        {
            foreach (var connector in _connectors)
            {
                if (SkipBlock(connector) || connector.Status != MyShipConnectorStatus.Connectable)
                {
                    _collection.Remove(connector);
                    continue;
                }

                DateTime time;
                if (!_collection.TryGetValue(connector, out time))
                {
                    _collection.Add(connector, DateTime.Now);
                    continue;
                }
                if ((DateTime.Now - time).TotalSeconds < _connectDelay) continue;
                connector.Connect();
                _collection.Remove(connector);
            }
        }


//returns if any of the turrets are actively targeting something
        private bool HasTarget(out List<MyDetectedEntityInfo> targets)
        {
            targets = new List<MyDetectedEntityInfo>();

            foreach (var turret in _turrets.Where(turret => turret.Enabled && turret.HasTarget && !SkipBlock(turret)))
            {
                var target = turret.GetTargetedEntity();

                if (Vector3D.Distance(target.Position, Me.GetPosition()) > 1500)
                {
                    targets.Remove(target);
                    continue;
                }

                if (targets.Contains(target) ||
                    target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;

                if (!targets.Contains(target))targets.Add(target);
            }

            return targets.Any();
        }

        /// <summary>
        /// Reset block dictionaries
        /// </summary>
        private void GetBlocks()
        {
            if (_gridBlocks.Any(Closed))
            {
                var removeBlocks = _gridBlocks.Where(Closed);
                foreach (var block in removeBlocks)
                {
                    _collection?.Remove(block);
                    _lowBlocks?.Remove(block);
                }
            }


            _gridBlocks.Clear();
            _dump.Clear();
            _landingGears.Clear();
            _soundBlocks.Clear();
            _powerBlocks.Clear();
            _gyros.Clear();
            _productionBlocks.Clear();
            _gravGens.Clear();
            _batteries.Clear();
            _reactors.Clear();
            _gasTanks.Clear();
            _airVents.Clear();
            _connectors.Clear();
            _turrets.Clear();
            _lights.Clear();
            _doors.Clear();
            _solars.Clear();
            _windTurbine.Clear();
            _cockpits.Clear();
            _remotes.Clear();
            _gasGens.Clear();
            _thrusters.Clear();
             
            

            GridTerminalSystem.GetBlockGroupWithName(_welderGroup)?.GetBlocksOfType(_shipWelders);
            GridTerminalSystem.GetBlocks(_allBlocks);

            _gridBlocks.AddRange(_allBlocks.Where(x=>x.IsSameConstructAs(Me) && !StringContains(x.CustomData, "ignore") &&
                                                     !StringContains(x.CustomName, "ignore") ));


            _myProjector = GridTerminalSystem.GetBlockWithName(_reProj) as IMyProjector;

            _landingGears.AddRange(_gridBlocks.OfType<IMyLandingGear>());
            _soundBlocks.AddRange( _gridBlocks.OfType<IMySoundBlock>());
            _powerBlocks.AddRange(_gridBlocks.OfType<IMyPowerProducer>());
            _gyros.AddRange(_gridBlocks.OfType<IMyGyro>());
            _productionBlocks.AddRange(_gridBlocks.OfType<IMyProductionBlock>().Where(x=>!StringContains(x.BlockDefinition.TypeIdString.Substring(16),"survivalkit")));
            _gravGens.AddRange(_gridBlocks.OfType<IMyGravityGenerator>());
            _batteries.AddRange(_gridBlocks.OfType<IMyBatteryBlock>());
            _reactors.AddRange(_gridBlocks.OfType<IMyReactor>());
            _gasTanks.AddRange(_gridBlocks.OfType<IMyGasTank>());
            _airVents.AddRange(_gridBlocks.OfType<IMyAirVent>());
            _textPanels.AddRange(_gridBlocks.OfType<IMyTextPanel>());
            _connectors.AddRange(_gridBlocks.OfType<IMyShipConnector>());
            _turrets.AddRange(_gridBlocks.OfType<IMyLargeTurretBase>());
            _lights.AddRange(_gridBlocks.OfType<IMyLightingBlock>());
            _doors.AddRange(_gridBlocks.OfType<IMyDoor>());
            _solars.AddRange(_gridBlocks.OfType<IMySolarPanel>());
            _windTurbine.AddRange(_gridBlocks.OfType<IMyPowerProducer>().Where(x => x.BlockDefinition.TypeIdString.ToString().Substring(16).Equals("windturbine", StringComparison.OrdinalIgnoreCase)));
            _cockpits.AddRange(_gridBlocks.OfType<IMyShipController>());
            _remotes.AddRange(_gridBlocks.OfType<IMyRemoteControl>());
            _gasGens.AddRange(_gridBlocks.OfType<IMyGasGenerator>());
            _thrusters.AddRange(_gridBlocks.OfType<IMyThrust>());
            var dump = _gridBlocks.OfType<IMyCargoContainer>()
                .Where(x => StringContains(x.CustomName,"dump" ) || StringContains(x.CustomData, "dump"));
            _dump.AddRange(dump);
                
            foreach (var block in _gridBlocks)
            {
                block.ShowOnHUD = false;
                if (_removeBlocksOnTerminal) block.ShowInTerminal = false;
                if (_removeBlocksInConfigurationTab) block.ShowInToolbarConfig = false;
            }
        }

        private static bool StringContains(string source, string toCheck,
            StringComparison comp = StringComparison.OrdinalIgnoreCase)
        {
            return source?.IndexOf(toCheck, comp) >= 0;
        }

        private bool SkipBlock(IMyCubeBlock block)
        {
            return Closed(block) || !IsOwned(block);
        }

//check for non-existent blocks
        private static bool Closed(IMyEntity block)
        {
            return Vector3D.IsZero(block.WorldMatrix.Translation);
        }



//check block for ownership rites
        private bool IsOwned(IMyCubeBlock block)
        {
            return block.GetUserRelationToOwner(Me.OwnerId) == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                   block.OwnerId == Me.OwnerId;
        }

        private enum Pilot
        {
            Disabled,
            Cruise,
            Land,
            Takeoff
        }


        private enum AlertState
        {
            Clear,
            Guarded,
            Elevated,
            High,
            Severe
        }

        private enum ProgramState
        {
            ShutOff,
            Recharge,
            PowerSave,
            Docked,
            Stop,
            Start,
            PowerOn,
            Normal
        }

        #region Runtime tracking

        /// <summary>
        ///     Class that tracks runtime history.
        /// </summary>
        public class RuntimeTracker
        {
            private readonly int _instructionLimit;
            private readonly Queue<double> _instructions = new Queue<double>();
            private readonly Program _program;

            private readonly Queue<double> _runtimes = new Queue<double>();
            private readonly StringBuilder _sbStatus = new StringBuilder();

            public RuntimeTracker(Program program, int capacity = 100, double sensitivity = 0.01)
            {
                _program = program;
                Capacity = capacity;
                Sensitivity = sensitivity;
                _instructionLimit = _program.Runtime.MaxInstructionCount;
            }

            public int Capacity { get; set; }
            public double Sensitivity { get; set; }
            public double MaxRuntime { get; private set; }
            public double MaxInstructions { get; private set; }
            public double AverageRuntime { get; private set; }
            public double AverageInstructions { get; private set; }

            public void AddRuntime()
            {
                var runtime = _program.Runtime.LastRunTimeMs;
                AverageRuntime = Sensitivity * (runtime - AverageRuntime) + AverageRuntime;

                _runtimes.Enqueue(runtime);
                if (_runtimes.Count == Capacity) _runtimes.Dequeue();

                MaxRuntime = _runtimes.Max();
            }

            public void AddInstructions()
            {
                double instructions = _program.Runtime.CurrentInstructionCount;
                AverageInstructions = Sensitivity * (instructions - AverageInstructions) + AverageInstructions;

                _instructions.Enqueue(instructions);
                if (_instructions.Count == Capacity) _instructions.Dequeue();

                MaxInstructions = _instructions.Max();
            }

            public void Reset()
            {
                _runtimes.Clear();
                _instructions.Clear();
                _runtimes.Clear();
            }

            public string Write()
            {
                _sbStatus.Clear();
                _sbStatus.AppendLine("\n_____________________________\nCerebro Runtime Info\n");
                _sbStatus.AppendLine($"Avg instructions: {AverageInstructions:n2}");
                _sbStatus.AppendLine($"Max instructions: {MaxInstructions:n0}");
                _sbStatus.AppendLine($"Avg complexity: {MaxInstructions / _instructionLimit:0.000}%");
                _sbStatus.AppendLine($"Avg runtime: {AverageRuntime:n4} ms");
                _sbStatus.AppendLine($"Max runtime: {MaxRuntime:n4} ms");
                return _sbStatus.ToString();
            }
        }

        #endregion

        #region Vector math

        private static class VectorMath
        {
            /// <summary>
            ///     Computes angle between 2 vectors
            /// </summary>
            public static double AngleBetween(Vector3D a, Vector3D b) //returns radians
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return 0;
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
            }
        }

        #endregion

        #region Scheduler

        /// <summary>
        ///     Class for scheduling actions to occur at specific frequencies. Actions can be updated in parallel or in sequence
        ///     (queued).
        /// </summary>
        public class Scheduler
        {
            private const double runtimeToRealtime = 1.0 / 60.0 / 0.0166666;
            private readonly List<ScheduledAction> _actionsToDispose = new List<ScheduledAction>();
            private readonly Program _program;
            private Queue<ScheduledAction> _queuedActions = new Queue<ScheduledAction>();
            private readonly List<ScheduledAction> _scheduledActions = new List<ScheduledAction>();
            private ScheduledAction _currentlyQueuedAction;

            /// <summary>
            ///     Constructs a scheduler object with timing based on the runtime of the input program.
            /// </summary>
            /// <param name="program"></param>
            public Scheduler(Program program)
            {
                _program = program;
            }

            /// <summary>
            ///     Updates all ScheduledAcions in the schedule and the queue.
            /// </summary>
            public void Update()
            {
                var deltaTime = Math.Max(0, _program.Runtime.TimeSinceLastRun.TotalSeconds * runtimeToRealtime);

                _actionsToDispose.Clear();
                foreach (var action in _scheduledActions)
                {
                    action.Update(deltaTime);
                    if (action.JustRan && action.DisposeAfterRun) _actionsToDispose.Add(action);
                }

                // Remove all actions that we should dispose
                _scheduledActions.RemoveAll(x => _actionsToDispose.Contains(x));

                if (_currentlyQueuedAction == null)
                    // If queue is not empty, populate current queued action
                    if (_queuedActions.Count != 0)
                        _currentlyQueuedAction = _queuedActions.Dequeue();

                // If queued action is populated
                if (_currentlyQueuedAction != null)
                {
                    _currentlyQueuedAction.Update(deltaTime);
                    if (_currentlyQueuedAction.JustRan)
                    {
                        // If we should recycle, add it to the end of the queue
                        if (!_currentlyQueuedAction.DisposeAfterRun)
                            _queuedActions.Enqueue(_currentlyQueuedAction);

                        // Set the queued action to null for the next cycle
                        _currentlyQueuedAction = null;
                    }
                }
            }

            /// <summary>
            ///     Adds an Action to the schedule. All actions are updated each update call.
            /// </summary>
            /// <param name="action"></param>
            /// <param name="updateFrequency"></param>
            /// <param name="disposeAfterRun"></param>
            public void AddScheduledAction(Action action, double updateFrequency, bool disposeAfterRun = false)
            {
                var scheduledAction = new ScheduledAction(action, updateFrequency, disposeAfterRun);
                _scheduledActions.Add(scheduledAction);
            }

            /// <summary>
            ///     Adds a ScheduledAction to the schedule. All actions are updated each update call.
            /// </summary>
            /// <param name="scheduledAction"></param>
            public void AddScheduledAction(ScheduledAction scheduledAction)
            {
                _scheduledActions.Add(scheduledAction);
            }


            /// <summary>
            ///     Adds an Action to the queue. Queue is FIFO.
            /// </summary>
            /// <param name="action"></param>
            /// <param name="updateInterval"></param>
            /// <param name="disposeAfterRun"></param>
            public void AddQueuedAction(Action action, double updateInterval, bool disposeAfterRun = false)
            {
                if (updateInterval <= 0) updateInterval = 0.001; // avoids divide by zero
                var scheduledAction = new ScheduledAction(action, 1.0 / updateInterval, disposeAfterRun);
                _queuedActions.Enqueue(scheduledAction);
            }

            /// <summary>
            ///     Clears all queued actions
            /// </summary>
            public void Reset()
            {
                _queuedActions.Clear();
                _scheduledActions.Clear();

            }

            /// <summary>
            ///     Adds a ScheduledAction to the queue. Queue is FIFO.
            /// </summary>
            /// <param name="scheduledAction"></param>
            public void AddQueuedAction(ScheduledAction scheduledAction)
            {
                _queuedActions.Enqueue(scheduledAction);
            }
        }

        public class ScheduledAction
        {
            private readonly Action _action;

            private readonly double _runFrequency;
            public readonly double RunInterval;
            protected bool _justRun = false;

            /// <summary>
            ///     Class for scheduling an action to occur at a specified frequency (in Hz).
            /// </summary>
            /// <param name="action">Action to run</param>
            /// <param name="runFrequency">How often to run in Hz</param>
            public ScheduledAction(Action action, double runFrequency, bool removeAfterRun = false)
            {
                _action = action;
                _runFrequency = runFrequency;
                RunInterval = 1.0 / _runFrequency;
                DisposeAfterRun = removeAfterRun;
            }

            public bool JustRan { get; private set; }
            public bool DisposeAfterRun { get; }
            public double TimeSinceLastRun { get; private set; }

            public virtual void Update(double deltaTime)
            {
                TimeSinceLastRun += deltaTime;

                if (TimeSinceLastRun >= RunInterval)
                {
                    _action.Invoke();
                    TimeSinceLastRun = 0;

                    JustRan = true;
                }
                else
                {
                    JustRan = false;
                }
            }
        }

        #endregion

        #region Drone Control

//--------------Command Broadcasts---------------

//Command to order drones to follow

        private void FollowMe()
        {
            if (!_hive || _myAntenna?.IsFunctional == false) return;
                
            IGC.SendBroadcastMessage($"Follow {Me.Position}", _myAntenna.Radius);
        }

        private void AttackTarget()
        {
            if (!_myTargets.Any()) return;
            var test = new Random();
            var ran = test.Next(1, _myTargets.Count);
            if (_hasAntenna && _myAntenna.IsFunctional)
                IGC.SendBroadcastMessage($"Attack {_myTargets[ran].Position}", _myAntenna.Radius);
        }

        #endregion

        #region Navigation
        /// <summary>
        /// Main navigation control
        /// </summary>
        private void CheckNavigation()
        {
            if (!_autoNavigate || _remoteControl == null) return;
            _inGravity = _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);
            _currentSpeed = _remoteControl.GetShipSpeed();

            switch (_autoPilot)
            {
                case Pilot.Disabled:
                    CheckThrusters();
                    break;
                case Pilot.Cruise:
                    _sbInfo.AppendLine($"Cruise set to {_cruiseSpeed}m/s");
                    _sbStatus.AppendLine($"Cruise set to {_cruiseSpeed}m/s");
                    Cruise(_cruiseSpeed,_cruiseDirection, _cruiseHeight, _giveControl);
                    return;
                case Pilot.Land:
                    _sbInfo.AppendLine($"AutoPilot Landing");
                    _sbStatus.AppendLine($"AutoPilot Landing");
                    LevelShip(false);
                    Land();
                    if (!(_currentHeight < 20) || !(_currentSpeed < 1)) return;
                    OverrideThrust(false, Vector3D.Zero, 0, 0);
                    _autoPilot = Pilot.Disabled;
                    return;
                case Pilot.Takeoff:
                    _sbInfo.AppendLine($"AutoPilot exiting planetary gravity");
                    _sbStatus.AppendLine($"AutoPilot exiting planetary gravity");
                    if (LandingLocked())LockLandingGears(false);
                    TakeOff(_cruiseSpeed,_takeOffAngle,_giveControl);
                    return;
                default:
                    return;
            }

            if (_currentHeight < _landingBrakeHeight)
                EnableDampeners(true);

            if (IsUnderControl())
            {
                ResetGyros();
                _remoteControl?.SetAutoPilotEnabled(false);
                return;
            }


            if (!_combatFlag && _inGravity)
            {
                LevelShip(true);
                return;
            }


            if (!_myTargets.Any() || IsDocked() ||_hive) return;
            ResetGyros();
            var selectedTarget = _myTargets.FirstOrDefault();
            if (Vector3D.Distance(selectedTarget.Position, Me.CubeGrid.GetPosition()) <= 400)
            {
                _remoteControl?.SetAutoPilotEnabled(false);
                return;
            }
            SetDestination(selectedTarget.Position, false, 75);
        }

        private void EnableDampeners(bool enable)
        {
            foreach (var controller in _cockpits.Where(controller => !Closed(controller)))
                controller.DampenersOverride = enable;
        }

        private void ResetGyros()
        {
            if (!_gyros.Any())return;
            foreach (var gyro in _gyros)
            {
                gyro.Enabled = true;
                gyro.GyroOverride = false;
                gyro.Pitch = 0;
                gyro.Roll = 0;
                gyro.Yaw = 0;
            }
        }

        private void SetDestination(Vector3D destination, bool enableCollision = true, float speed = 15)
        {
            _remoteControl.ClearWaypoints();
            _remoteControl.FlightMode = FlightMode.OneWay;
            _remoteControl.SpeedLimit = speed;
            _remoteControl.AddWaypoint(destination, "AutoPilot");
            _remoteControl.Direction = Base6Directions.Direction.Forward;
            _remoteControl.SetCollisionAvoidance(enableCollision);
            _remoteControl.SetAutoPilotEnabled(true);
            _remoteControl.WaitForFreeWay = true;
        }

        private void Cruise(double speed = 50, string dir = "forward", double height = 2500, bool giveControl = true)
        {
            giveControl = !_inGravity || giveControl;
            Vector3D direction;
            switch (dir)
            {
                case "up":
                    direction = _remoteControl.WorldMatrix.Up;
                    break;
                case "down":
                    direction = _remoteControl.WorldMatrix.Down;
                    break;
                case "left":
                    direction = _remoteControl.WorldMatrix.Left;
                    break;
                case "right":
                    direction = _remoteControl.WorldMatrix.Right;
                    break;
                case "forward":
                    direction = _remoteControl.WorldMatrix.Forward;
                    break;
                case "backward":
                    direction = _remoteControl.WorldMatrix.Backward;
                    break;
                default:
                    direction = _remoteControl.WorldMatrix.Forward;
                    break;
            }

            _thrust = _currentSpeed < speed - 5
                ? Math.Min(_thrust += 0.01f, 1)
                : Math.Max(_thrust -= 0.1f, 0);

            _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);

            var x = Math.Max(height - 500, _safeCruiseHeight);
            
            
            _cruiseHeight = _cruiseHeight < _safeCruiseHeight ? x : _cruiseHeight;

            _cruisePitch = _currentHeight < _cruiseHeight
                ? Math.Min(_cruisePitch += 0.01f, 0.15f)
                : Math.Max(_cruisePitch -= 0.01f, -0.05f);

            LevelShip(giveControl, _cruisePitch, 0);

            if (_inGravity && Math.Abs(_currentHeight - x) > 2500)
            {
                var adjustSpeed = Math.Abs(_currentHeight - height) > 2500 ? 110 : 50;
                if (_currentHeight > x + 1000)
                {
                    OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.01f, _currentSpeed, adjustSpeed);
                    return;
                }

                if (_currentHeight < x)
                {
                    OverrideThrust(true, _remoteControl.WorldMatrix.Up, 1, _currentSpeed, adjustSpeed);
                    return;
                }
            }

            var useThrust = _cruisePitch > 0 ? _thrust : 0;

            OverrideThrust(true, direction, useThrust, _currentSpeed, speed);
        }

        private void EndTrip()
        {
            _autoPilot = Pilot.Disabled;
            _cruiseDirection = string.Empty;
            _cruiseHeight = 0;
            _cruiseSpeed = 0;
            foreach (var gyro in _gyros.Where(gyro => !Closed(gyro)))
            {
                gyro.Enabled = true;
                gyro.GyroOverride = false;
                gyro.Pitch = 0;
                gyro.Roll = 0;
                gyro.Yaw = 0;
            }

            foreach (var thruster in _thrusters.Where(thruster => !Closed(thruster)))
            {
                thruster.ThrustOverridePercentage = 0;
                thruster.Enabled = true;
            }

            EnableDampeners(true);
        }

        private void TakeOff(double takeOffSpeed = 100, double angle = 0, bool giveControl = false)
        {
            if (_remoteControl == null && !TryGetRemote(out _remoteControl))
            {
                _autoPilot = Pilot.Disabled;
                return;
            }
            LevelShip(giveControl, angle*0.0174, 0);
            var up = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(-_remoteControl.GetNaturalGravity()));
            
            var maxSpeed = takeOffSpeed - takeOffSpeed * 0.05;

            if (_currentHeight <= _landingBrakeHeight) _thrust = 1f;
            else
                _thrust =  _currentSpeed < maxSpeed
                    ? Math.Min(_thrust += 0.001f, 1)
                    : Math.Max(_thrust -= 0.1f, 0);
            OverrideThrust(_inGravity, up, _thrust, _currentSpeed,
                takeOffSpeed + takeOffSpeed * 0.1, 0.25f);
            _autoPilot = _inGravity ? Pilot.Takeoff : Pilot.Disabled;
        }

        private void Land()
        {
            if (_remoteControl == null && !TryGetRemote(out _remoteControl))
            {
                _autoPilot = Pilot.Disabled;
                return;
            }

            var down = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(_remoteControl.GetNaturalGravity()));

            var up = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(-_remoteControl.GetNaturalGravity()));

            foreach (var remote in _remotes.Where(remote => !Closed(remote))) remote.SetAutoPilotEnabled(false);

            if (_currentHeight > _landingBrakeHeight)
            {
                OverrideThrust(true, down, 0.001f, _currentSpeed);
                EnableDampeners(false);
                return;
            }

            EnableDampeners(true);
            if (_currentHeight > 450)
            {
                OverrideThrust(true, down, 0.001f, _currentSpeed, 75);
                return;
            }

            if (_currentHeight > 100)
            {
                OverrideThrust(true, down, 0.001f, _currentSpeed, 25);
                return;
            }


            _thrust = _remoteControl.GetShipVelocities().LinearVelocity.Y +
                      _remoteControl.GetShipVelocities().LinearVelocity.Z < 0
                      && _currentSpeed > 5
                ? Math.Min(_thrust += 0.15f, 1f)
                : Math.Max(_thrust -= 0.5f, 0.001f);
            OverrideThrust(true, up,
                _thrust, _currentSpeed, 1.5);
        }


        private void CheckThrusters()
        {
            if (!_thrusters.Any() || _currentMode != ProgramState.Normal)
                return;

            foreach (var thruster in _thrusters)
            {
                var maxThrust = thruster.MaxEffectiveThrust / thruster.MaxThrust;
               thruster.Enabled = maxThrust >= 0.35f;
            }

        }


        private void OverrideThrust(bool enableOverride, Vector3D direction, float thrustModifier,
            double currentSpeed = 100, double maximumSpeed = 110, float maxThrustModifier = 0.3f)
        {
            foreach (var thruster in _thrusters.Where(thruster => !Closed(thruster) && thruster.IsFunctional))
            {
                var maxThrust = thruster.MaxEffectiveThrust / thruster.MaxThrust;


                if (enableOverride && currentSpeed < maximumSpeed)
                {
                    if (thruster.WorldMatrix.Forward == direction * -1)
                    {
                        thruster.Enabled = maxThrust >= maxThrustModifier;
                        thruster.ThrustOverridePercentage = thrustModifier;
                    }

                    if (thruster.WorldMatrix.Forward == direction) thruster.Enabled = false;
                }
                else
                {
                    thruster.Enabled =  maxThrust >= maxThrustModifier;
                    thruster.SetValueFloat("Override", 0);
                }
            }
        }

        private void LevelShip(bool checkPlayer, double setPitch = 0, double setRoll = 0, double setYaw = 0)
        {
            if (!_gyros.Any())return;
            if (checkPlayer && IsUnderControl() || !_inGravity)
            {
                ResetGyros();
                return;
            }

            var rad = Math.PI / 180;

            var gravity = _remoteControl.GetNaturalGravity();
            var up = -gravity;
            var left = Vector3D.Cross(up, _remoteControl.WorldMatrix.Forward);
            var forward = Vector3D.Cross(left, up);
            var localUpVector = Vector3D.Rotate(up, MatrixD.Transpose(_remoteControl.WorldMatrix));
            var flattenedUpVector = new Vector3D(localUpVector.X, localUpVector.Y, 0);

            var roll  =VectorMath.AngleBetween(flattenedUpVector, Vector3D.Up) *
                       Math.Sign(Vector3D.Dot(Vector3D.Right, flattenedUpVector));

            var pitch = VectorMath.AngleBetween(forward, _remoteControl.WorldMatrix.Forward) *
                        Math.Sign(Vector3D.Dot(up, _remoteControl.WorldMatrix.Forward));

            var yaw = VectorMath.AngleBetween(left, _remoteControl.WorldMatrix.Forward) *
                        Math.Sign(Vector3D.Dot(Vector3.Right, _remoteControl.WorldMatrix.Forward));

            _pitchDelay = _inGravity && Math.Abs(pitch - setPitch) >= 1*rad
                ? Math.Min(_pitchDelay += 1, 10)
                : Math.Max(_pitchDelay -= 1, 0);

            _rollDelay = _inGravity && Math.Abs(roll - setRoll) >= 1*rad
                ? Math.Min(_rollDelay += 1, 10)
                : Math.Max(_rollDelay -= 1, 0);

            
            var pitchOverride = Math.Abs(pitch) > 90*rad ? 0.1f : 0.05f;
            var rollOverride = Math.Abs(roll) > 90*rad ? 0.1f : 0.05f;
            var yawOverride = Math.Abs(yaw) > 90*rad ? 0.1f : 0.05f;

            if (_pitchDelay == 0 && _rollDelay == 0)
            {
                ResetGyros();
                return;
            }

            for (var i = 0; i < Math.Max(1,Math.Round(_gyros.Count*0.15)); i++)
            {
                var gyro = _gyros.Where(g=>!SkipBlock(g)).ToList()[i];
                if (_pitchDelay == 0 && _rollDelay == 0)
                {
                    gyro.GyroOverride = false;
                    gyro.Pitch = 0f;
                    gyro.Roll = 0f;
                    gyro.Yaw = 0f;
                    return;
                }

                gyro.Enabled = true;
                gyro.GyroOverride = _pitchDelay > 1 || _rollDelay > 1;
                gyro.Yaw = 0f;
                gyro.Pitch = 0f;
                gyro.Roll = 0f;


                //Pitch
                if (_pitchDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Left)
                        gyro.Pitch = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Right)
                        gyro.Pitch = pitch > setPitch ? pitchOverride : -pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Down)
                        gyro.Yaw = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Up)
                        gyro.Yaw = pitch > setPitch ? pitchOverride : -pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Forward)
                        gyro.Roll = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Backward)
                        gyro.Roll = pitch > setPitch ? pitchOverride : -pitchOverride;
                }

                //Roll
                if (_rollDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Forward)
                        gyro.Roll = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Backward)
                        gyro.Roll = roll > setRoll ? -rollOverride : rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Down)
                        gyro.Yaw = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Up)
                        gyro.Yaw = roll > setRoll ? -rollOverride : rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Left)
                        gyro.Pitch = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Right)
                        gyro.Pitch = roll > setRoll ? -rollOverride : rollOverride;
                }

                //Yaw


            }
            /*foreach (var gyro in _gyros)
            {
                if (_pitchDelay == 0 && _rollDelay == 0)
                {
                    gyro.GyroOverride = false;
                    gyro.Pitch = 0f;
                    gyro.Roll = 0f;
                    gyro.Yaw = 0f;
                    return;
                }

                gyro.Enabled = true;
                gyro.GyroOverride = _pitchDelay > 1 || _rollDelay > 1;
                gyro.Yaw = 0f;
                gyro.Pitch = 0f;
                gyro.Roll = 0f;


                //Pitch
                if (_pitchDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Left)
                        gyro.Pitch = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Right)
                        gyro.Pitch = pitch > setPitch ? pitchOverride : -pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Down)
                        gyro.Yaw = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Up)
                        gyro.Yaw = pitch > setPitch ? pitchOverride : -pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Forward)
                        gyro.Roll = pitch > setPitch ? -pitchOverride : pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Backward)
                        gyro.Roll = pitch > setPitch ? pitchOverride : -pitchOverride;
                }

                //Roll
                if (_rollDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Forward)
                        gyro.Roll = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Backward)
                        gyro.Roll = roll > setRoll ? -rollOverride : rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Down)
                        gyro.Yaw = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Up)
                        gyro.Yaw = roll > setRoll ? -rollOverride : rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Left)
                        gyro.Pitch = roll > setRoll ? rollOverride : -rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Right)
                        gyro.Pitch = roll > setRoll ? -rollOverride : rollOverride;
                }

                //Yaw

            }*/
        }

        private bool IsUnderControl()
        {
            return _cockpits.Any(cockpit => cockpit.CanControlShip && cockpit.ControlThrusters && cockpit.IsUnderControl);
        }


        #endregion

        #region Block Lists

        private List<IMyDoor> _doors = new List<IMyDoor>();
        private List<IMyTextPanel> _textPanels = new List<IMyTextPanel>();
        private List<IMyProductionBlock> _productionBlocks = new List<IMyProductionBlock>();
        private List<IMyReactor> _reactors = new List<IMyReactor>();
        private readonly List<IMyShipWelder> _shipWelders = new List<IMyShipWelder>();
        private List<IMyAirVent> _airVents = new List<IMyAirVent>() ;
        private List<IMyGasGenerator> _gasGens = new List<IMyGasGenerator>();
        private List<IMyGasTank> _gasTanks = new List<IMyGasTank>();
        private List<IMyGravityGenerator> _gravGens = new List<IMyGravityGenerator>();
        private readonly List<IMyTerminalBlock> _gridBlocks = new List<IMyTerminalBlock>();
        private readonly List<IMyTerminalBlock> _allBlocks = new List<IMyTerminalBlock>();
        private List<IMyThrust> _thrusters = new List<IMyThrust>();
        private List<IMyCargoContainer> _dump = new List<IMyCargoContainer>(10);
        private List<IMyTerminalBlock> _damagedBlocks = new List<IMyTerminalBlock>(10);
        private List<IMyPowerProducer> _powerBlocks = new List<IMyPowerProducer>();
        private List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
        private List<IMySolarPanel> _solars = new List<IMySolarPanel>();
        private List<IMyPowerProducer> _windTurbine = new List<IMyPowerProducer>();
        private List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        private List<IMyRemoteControl> _remotes = new List<IMyRemoteControl>();
        private List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        private List<IMyShipController> _cockpits= new List<IMyShipController>();
        private List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private List<IMyGyro> _gyros = new List<IMyGyro>();
        private List<IMySoundBlock> _soundBlocks = new List<IMySoundBlock>();
        private List<IMyLandingGear> _landingGears = new List<IMyLandingGear>();

//dictionary
        private readonly Dictionary<IMyCubeBlock, float> _lowBlocks = new Dictionary<IMyCubeBlock, float>();
        private readonly Dictionary<IMyCubeBlock, DateTime> _collection = new Dictionary<IMyCubeBlock, DateTime>();
        private readonly Dictionary<string, MyItemType> _reactorFuel = new Dictionary<string, MyItemType>();

        #endregion

        #region Fields

        private readonly StringBuilder _sbInfo = new StringBuilder();
        private readonly StringBuilder _sbDamages = new StringBuilder();
        private readonly StringBuilder _sbPower = new StringBuilder();
        private readonly StringBuilder _sbStatus = new StringBuilder();
        private readonly StringBuilder _sbDebug = new StringBuilder();


//enum
        private ProgramState _currentMode = ProgramState.Start;

        private AlertState _alert = AlertState.Clear;

//Boolean
        private bool _combatFlag;
        private bool _hasAntenna;
        private bool _hive;
        private bool _setRechargeState;
        private bool _showOnHud;
        private bool _removeBlocksInConfigurationTab;
        private bool _removeBlocksOnTerminal;
        private readonly bool _productionFlag = false;
        private bool _isStatic;
        private bool _powerFlag;
        private bool _tankRefill;
        private bool _turretControl;
        private bool _controlVents;
        private bool _controlGasSystem;
        private bool _controlProduction;
        private bool _navFlag;
        private bool _capReactors;
        private bool _powerManagement;
        private bool _master;
        private bool _handleRepair;
        private bool _damageDetected;
        private bool _autoNavigate;
        private bool _rechargeWhenConnected;
        private bool _inGravity;
        private bool _enableAutoDoor;


        private static string _designatorName = "designator";
        private static string _antipersonnelName = "antiPersonnel";
        private static string _antimissileName = "antimissile";
        private string _cruiseDirection;
        private double _cruiseSpeed;
        private bool _giveControl;
        private double _takeOffAngle = 0;
        private double _cruiseHeight;
        private float _cruisePitch;
        private Pilot _autoPilot = Pilot.Disabled;


//Floats, Int, double
        private readonly int _connectDelay = 5;
        private readonly int _defaultAggression = 10;
        private readonly int _aggressionMultiplier = 2;
        private int _doorDelay = 5;
        private const int ProductionDelay = 30;
        private int _aggression;
        private int _alertCounter;
        private const int ProjectorShutoffDelay = 30;
        private int _pitchDelay;
        private int _rollDelay;
        private int _yawDelay;
        private float _thrust;
        private double _currentHeight;
        private double _currentSpeed;
        private double _safeCruiseHeight = 2500;

//battery life
        private float _batteryHighestCharge = 0.5f;
        private static float _rechargePoint = .15f;
        private IMyBatteryBlock _highestChargedBattery;
        private DateTime _powerFlagDelay;

        private static double _lowFuel = 50;

        private static double _tankFillLevel = 1;
        private static double _landingBrakeHeight = 1700;
        private static float _overload = 0.90f;

        private string _welderGroup = "Welders";
        private string _reProj = "Projector";


        private IMyRadioAntenna _myAntenna;
        private IMyProjector _myProjector;
        private IMyRemoteControl _remoteControl;

//entityinfo
        private List<MyDetectedEntityInfo> _myTargets = new List<MyDetectedEntityInfo>();

        #endregion

        #region Settings

        private readonly MyIni _ini = new MyIni();
        private readonly List<string> _iniSections = new List<string>();
        private readonly StringBuilder _customDataSb = new StringBuilder();
        private Dictionary<MyItemType, MyFixedPoint> _fuelCollection = new Dictionary<MyItemType, MyFixedPoint>();
        private const double Tick = 1.0 / 60.0;
        private readonly Scheduler _scheduler;

        private readonly ScheduledAction _scheduledSetup;

        private readonly RuntimeTracker _runtimeTracker;

//Settings
        private const string INI_SECTION_GENERAL = "Cerebro Settings - General";
        private const string INI_GENERAL_MASTER = "Is Main Script";
        private const string INI_GENERAL_HIVE = "Control Drones";
        private const string INI_GENERAL_SHOWONHUD = "Show Faulty Block On Hud";
        private const string INI_GENERAL_BLOCKSONTERMINAL = "Remove Blocks From Terminal";
        private const string INI_GENERAL_BLOCKSONCONFIGTAB = "Remove Blocks From Config Tab";
        private const string INI_GENERAL_DOORCLOSURE = "Auto Door Closure";
        private const string INI_GENERAL_DOOR = "Door Delay";


//Navigation
        private const string INI_SECTION_NAVIGATION = "Cerebro Settings - Navigation";
        private const string INI_NAVIGATION_NAVIGATE = "Enable Navigation";
        private const string INI_NAVIGATION_LANDINGHEIGHT = "Landing Braking Height";
        private const string INI_NAVIGATION_DEFAULTCRUISEHEIGHT = "Safe Cruise Height";


//Production
        private const string INI_SECTION_PRODUCTION = "Cerebro Settings - Production";
        private const string INI_PRODUCTION_BLOCKSHUTOFFS = "Control Refineries and Assemblers";
        private const string INI_PRODUCTION_VENTS = "Control Vents";
        private const string INI_PRODUCTION_GAS = "Control Gas Production";
        private const string INI_PRODUCTION_TANKLEVEL = "Tank Refill Level";
        private const string INI_PRODUCTION_REPAIR = "Auto Repair Ship";
        private const string INI_PRODUCTION_PROJECTOR = "Repair Projector";
        private const string INI_PRODUCTION_WELDERS = "Repair Welders GroupName";

//Power
        private const string INI_SECTION_POWER = "Cerebro Settings - Power";
        private const string INI_POWER_POWERMANAGEMENT = "Power Management";
        private const string INI_POWER_ENABLECAP = "Cap Reactor Full";
        private const string INI_POWER_CAPLEVEL = "Reactor Fuel Fill Level";
        private const string INI_POWER_RECHARGEPOINT = "Battery Recharge Point";
        private const string INI_POWER_OVERLOAD = "Power Overload";
        private const string INI_POWER_RECHARGEONCONNECT = "Recharge When Connected";


//Weapons
        private const string INI_SECTION_WEAPONS = "Cerebro Settings - Weapons";
        private const string INI_WEAPONS_TURRETS = "Control Turrets";


        private void ParseIni()
        {
            _ini.Clear();
            _ini.TryParse(Me.CustomData);

            _iniSections.Clear();
            _ini.GetSections(_iniSections);

            if (_iniSections?.Any() == false)
            {
                _customDataSb.Clear();
                _customDataSb.Append(Me.CustomData);
                _customDataSb.Replace("---\n", "");

                _ini.EndContent = _customDataSb.ToString();
            }

//Get Config
//General
            _master = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_MASTER).ToBoolean(_master);
            _hive = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_HIVE).ToBoolean(_hive);
            _showOnHud = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_SHOWONHUD).ToBoolean(_showOnHud);
            _removeBlocksInConfigurationTab = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONCONFIGTAB)
                .ToBoolean(_removeBlocksInConfigurationTab);
            _removeBlocksOnTerminal = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONTERMINAL)
                .ToBoolean(_removeBlocksOnTerminal);
            _enableAutoDoor = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_DOORCLOSURE).ToBoolean(_enableAutoDoor);
            _doorDelay = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_DOOR).ToInt32(_doorDelay);


//Navigation
            _autoNavigate = _ini.Get(INI_SECTION_NAVIGATION, INI_NAVIGATION_NAVIGATE).ToBoolean(_autoNavigate);
            _landingBrakeHeight = _ini.Get(INI_SECTION_NAVIGATION, INI_NAVIGATION_LANDINGHEIGHT)
                .ToDouble(_landingBrakeHeight);
            _safeCruiseHeight = _ini.Get(INI_SECTION_NAVIGATION, INI_NAVIGATION_DEFAULTCRUISEHEIGHT)
                .ToDouble(_safeCruiseHeight);

//Production
            _controlProduction = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_BLOCKSHUTOFFS)
                .ToBoolean(_controlProduction);
            _controlVents = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_VENTS).ToBoolean(_controlVents);
            _controlGasSystem = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_GAS).ToBoolean(_controlGasSystem);
            _handleRepair = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_REPAIR).ToBoolean(_handleRepair);
            _welderGroup = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_WELDERS).ToString(_welderGroup);
            _reProj = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_PROJECTOR).ToString(_reProj);
            _tankFillLevel = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_TANKLEVEL).ToDouble(_tankFillLevel);

//Power
            _powerManagement = _ini.Get(INI_SECTION_POWER, INI_POWER_POWERMANAGEMENT).ToBoolean(_powerManagement);
            _capReactors = _ini.Get(INI_SECTION_POWER, INI_POWER_ENABLECAP).ToBoolean(_capReactors);
            _rechargePoint = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT).ToSingle(_rechargePoint);
            _rechargeWhenConnected = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEONCONNECT)
                .ToBoolean(_rechargeWhenConnected);
            _overload = _ini.Get(INI_SECTION_POWER, INI_POWER_OVERLOAD).ToSingle(_overload);
            _rechargePoint = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT).ToSingle(_rechargePoint);
            _lowFuel = _ini.Get(INI_SECTION_POWER, INI_POWER_CAPLEVEL).ToDouble(_lowFuel);

//Weapons
            _turretControl = _ini.Get(INI_SECTION_WEAPONS, INI_WEAPONS_TURRETS).ToBoolean(_turretControl);


            WriteIni();
        }

        private void WriteIni()
        {
//General
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_MASTER, _master);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_HIVE, _hive);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_SHOWONHUD, _showOnHud);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONCONFIGTAB, _removeBlocksInConfigurationTab);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONTERMINAL, _removeBlocksOnTerminal);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_DOORCLOSURE, _enableAutoDoor);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_DOOR, _doorDelay);


//Navigation
            _ini.Set(INI_SECTION_NAVIGATION, INI_NAVIGATION_NAVIGATE, _autoNavigate);
            _ini.Set(INI_SECTION_NAVIGATION, INI_NAVIGATION_LANDINGHEIGHT, _landingBrakeHeight);
            _ini.Set(INI_SECTION_NAVIGATION, INI_NAVIGATION_DEFAULTCRUISEHEIGHT, _safeCruiseHeight);

//Production
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_BLOCKSHUTOFFS, _controlProduction);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_VENTS, _controlVents);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_GAS, _controlGasSystem);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_REPAIR, _handleRepair);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_WELDERS, _welderGroup);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_PROJECTOR, _reProj);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_TANKLEVEL, _tankFillLevel);

//Power
            _ini.Set(INI_SECTION_POWER, INI_POWER_POWERMANAGEMENT, _powerManagement);
            _ini.Set(INI_SECTION_POWER, INI_POWER_ENABLECAP, _capReactors);
            _ini.Set(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT, _rechargePoint);
            _ini.Set(INI_SECTION_POWER, INI_POWER_RECHARGEONCONNECT, _rechargeWhenConnected);
            _ini.Set(INI_SECTION_POWER, INI_POWER_OVERLOAD, _overload);
            _ini.Set(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT, _rechargePoint);
            _ini.Set(INI_SECTION_POWER, INI_POWER_CAPLEVEL, _lowFuel);

//Weapons
            _ini.Set(INI_SECTION_WEAPONS, INI_WEAPONS_TURRETS, _turretControl);

            var output = _ini.ToString();
            if (!string.Equals(output, Me.CustomData))
                Me.CustomData = output;
        }

        #endregion

    }
}