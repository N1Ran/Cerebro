using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library.Utils;
using VRageMath;
using ContentType = VRage.Game.GUI.TextPanel.ContentType;
using IMyAirtightHangarDoor = Sandbox.ModAPI.Ingame.IMyAirtightHangarDoor;
using IMyAssembler = Sandbox.ModAPI.Ingame.IMyAssembler;
using IMyBatteryBlock = Sandbox.ModAPI.Ingame.IMyBatteryBlock;
using IMyCargoContainer = Sandbox.ModAPI.Ingame.IMyCargoContainer;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
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
            _scheduler = new Scheduler(this,true);
            Load();
        }

        /// <summary>
        /// Saves the script state
        /// </summary>
        private void Save()
        {

            Storage = string.Join(":",
               _currentMode,
               _alert,
                _autoPilot,
                _cruiseHeight,
                _setSpeed);
        }

        /// <summary>
        /// Loads saved states
        /// </summary>
        private void Load()
        {
            GetBlocks();
            Setup();
            _scheduler.Reset();
            if (_currentMode != ProgramState.ShuttingOff && _currentMode != ProgramState.Recharge)
            {
                SetSchedule();
            }
            else if (_currentMode == ProgramState.Recharge)
            {
                _setRechargeState = false;
            }


            var settings = Storage.Split(':');
            if (settings.Length < 1) return;
            
            if (settings.Length > 1)
                Enum.TryParse(settings[0], out _currentMode);

            if (settings.Length > 2)
                Enum.TryParse(settings[1], out _alert);

            if (settings.Length > 3)
                Enum.TryParse(settings[2], out _autoPilot);

            if (settings.Length > 4)
                double.TryParse(settings[3], out _cruiseHeight);

            if (settings.Length > 5)
                double.TryParse(settings[4], out _setSpeed);

        }

        /// <summary>
        /// Main Method. Runs each tick
        /// </summary>
        /// <param name="arg"></param>
        /// <param name="updateType"></param>
        private void Main(string arg, UpdateType updateType)
        {


            if (updateType == UpdateType.Terminal || updateType == UpdateType.Trigger)
            {
                GetCommandState(arg, out _currentMode);
            }

            Save();
            if (_currentMode == ProgramState.Stop)
            {
                Echo("Script Paused");
                return;
            }
            
            _runtimeTracker.AddRuntime();
            _scheduler.Update();
            _scheduler.Update();
            _runtimeTracker.AddInstructions();

            ProgramMaintenance();
            Echo(Log.Write(false));
            Echo(_runtimeTracker.Write());

        }

        /// <summary>
        /// Method to control Script. Runs Each tick
        /// </summary>
        private void ProgramMaintenance()
        {
            if (!_gridBlocks.Any())
            {
                GetBlocks();
                return;
            }
            Log.Info($"Status: {_currentMode}");

            switch (_currentMode)
            {
                case ProgramState.ShuttingOff:
                    PowerDown();
                    _scheduler.AddScheduledAction(AutoDoors,1);
                    _currentMode = ProgramState.PoweredOff;
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
                    _isStatic = Me.CubeGrid.IsStatic;
                    if (!_isStatic && IsDocked() && _currentMode != ProgramState.Recharge) _currentMode = ProgramState.Docked;
                    if (!_hasAntenna && _hive)
                    {
                        _hasAntenna = TryGetAntenna(out _myAntenna);
                        _hive = _hasAntenna;
                    }

                    _alert = GridFlags();
                    break;
                case ProgramState.Recharge:
                    Log.Info($"Recharging --- {Math.Round((double)BatteryLevel() * 100)}%");
                    if (!_setRechargeState) SetRechargeSchedule();
                    if (IsConnected()) return;
                    _setRechargeState = false;
                    _currentMode = ProgramState.PowerOn;
                    LockLandingGears(false);
                    break;
                case ProgramState.PoweredOff:
                    Log.Info("Powered off");
                    break;
                case ProgramState.Stop:
                    break;
                case ProgramState.Start:
                    _currentMode = ProgramState.Normal;
                    break;
            }

        }

        /// <summary>
        /// Sets Schedule for normal runs
        /// </summary>
        private void SetSchedule()
        {
            if (_autoNavigate && _remoteControl!=null)
            {
                var edgeDirection = VectorMath.GetShipEdgeVector(_remoteControl, _remoteControl.WorldMatrix.Down);
                var edgePos = _remoteControl.GetPosition() + edgeDirection;
                _shipHeight = Vector3D.Distance(_remoteControl.CenterOfMass, edgePos);

            }

            
            //Scheduled actions
           _scheduler.AddScheduledAction(_scheduledSetup);
           _scheduler.AddScheduledAction(CheckConnectors,1);
            
           //Queued actions
            _scheduler.AddScheduledAction(AggroBuilder,6);
            _scheduler.AddScheduledAction(CheckNavigation,60);
            _scheduler.AddScheduledAction(AutoDoors,1);
            _scheduler.AddScheduledAction(AlertLights,1);
            _scheduler.AddScheduledAction(ReturnToCenter,20);
            _scheduler.AddScheduledAction(UpdateInventory,0.1);
            


            const float step = 1f / 5f;
            const double mehTick = Tick;

            _scheduler.AddQueuedAction(CheckProjection,mehTick);
            _scheduler.AddQueuedAction(()=>BlockGroupEnable(_solars),mehTick);

            _scheduler.AddQueuedAction(() => UpdateProduction(0 * step, 1 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(1 * step, 2 * step),  mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(2 * step, 3 * step),  mehTick);
            _scheduler.AddQueuedAction(() => UpdateProduction(3 * step, 4 * step),  mehTick); 
            _scheduler.AddQueuedAction(() => UpdateProduction(4 * step, 5 * step), mehTick); 

            _scheduler.AddQueuedAction(() => UpdateTanks(0 * step, 1 * step),  mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(1 * step, 2 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(2 * step, 3 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateTanks(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateTanks(4 * step, 5 * step), mehTick); 

            _scheduler.AddQueuedAction(() => UpdateVents(0 * step, 1 * step),  mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(1 * step, 2 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(2 * step, 3 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateVents(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(4 * step, 5 * step), mehTick); 

           _scheduler.AddQueuedAction(() => UpdateGasGen(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateGasGen(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateGasGen(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateGasGen(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateGasGen(4 * step, 5 * step), mehTick);

      
           _scheduler.AddQueuedAction(() => UpdateBatteries(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateBatteries(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateBatteries(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(4 * step, 5 * step), mehTick);

           _scheduler.AddQueuedAction(() => UpdateReactors(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateReactors(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateReactors(4 * step, 5 * step), mehTick);

           _scheduler.AddQueuedAction(() => UpdateTurrets(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateTurrets(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => UpdateTurrets(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateTurrets(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => UpdateTurrets(4 * step, 5 * step), mehTick);

           _scheduler.AddQueuedAction(() => CapFuel(0 * step, 1 * step), mehTick); 
           _scheduler.AddQueuedAction(() => CapFuel(1 * step, 2 * step), mehTick); 
           _scheduler.AddQueuedAction(() => CapFuel(2 * step, 3 * step), mehTick);
           _scheduler.AddQueuedAction(() => CapFuel(3 * step, 4 * step), mehTick);
           _scheduler.AddQueuedAction(() => CapFuel(4 * step, 5 * step), mehTick);

            _scheduler.AddQueuedAction(() => UpdateScreens(0 * step, 1 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateScreens(1 * step, 2 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateScreens(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateScreens(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateScreens(4 * step, 5 * step), mehTick);

            _scheduler.AddQueuedAction(GetBlocks,100);


        }


        /// <summary>
        /// Sets schedule for recharge phase
        /// </summary>
        private void SetRechargeSchedule()
        {
            _runtimeTracker.Reset();
            _scheduler.Reset();
            _setRechargeState = true;

            _scheduler.AddScheduledAction(AutoDoors,1);
            _scheduler.AddScheduledAction(ReturnToCenter,1);

            const float step = 1f / 5f;
            const double mehTick = 500 * Tick;
            _scheduler.AddQueuedAction(() => UpdateBatteries(0 * step, 1 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(1 * step, 2 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(4 * step, 5 * step), mehTick);

            _scheduler.AddQueuedAction(() => UpdateVents(0 * step, 1 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateVents(1 * step, 2 * step), mehTick); 
            _scheduler.AddQueuedAction(() => UpdateVents(2 * step, 3 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(3 * step, 4 * step), mehTick);
            _scheduler.AddQueuedAction(() => UpdateVents(4 * step, 5 * step), mehTick);
            _scheduler.AddQueuedAction(GetBlocks, 10000);


        }

        /// <summary>
        /// Updates screen displays
        /// </summary>
        private void UpdateScreens(float startProportion, float endProportion)
        {
            Me.GetSurface(0).WriteText(_runtimeTracker.Write());
            if (_textPanels.Count <= 1)return;

            var start = (int) (startProportion * _textPanels.Count);
            var end = (int) (endProportion * _textPanels.Count);

            for (var i = start; i < end; i++)
            {
                var panel = _textPanels[i];
                if (SkipBlock(panel) || !StringContains(panel.CustomName, "Cerebro"))continue;
                panel.ContentType = ContentType.TEXT_AND_IMAGE;
                panel.Enabled = true;
                if (StringContains(panel.CustomName, "Damage"))
                {
                    panel.WriteText(Log.Write("Damage"));
                    continue;
                }
                if (StringContains(panel.CustomName, "Status"))
                {
                    panel.WriteText(Log.Write(false));
                    continue;
                }
                if (StringContains(panel.CustomName, "Debug"))
                {
                    panel.WriteText(Log.Write( "Debug"));
                    continue;
                }
                if (StringContains(panel.CustomName, "Power"))
                {
                    continue;
                }

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

           var  blocksToSwitchOn = new HashSet<IMyCubeBlock>(_gridBlocks.OfType<IMyFunctionalBlock>().Where(funcBlock => !Closed(funcBlock) &&
                                                                                                                         !funcBlock.Enabled && !(funcBlock is IMyShipWelder || funcBlock is IMyShipGrinder ||
                                                                                                                                                 funcBlock is IMyShipDrill || funcBlock.BlockDefinition.TypeIdString.Substring(16).Equals("hydrogenengine",
                                                                                                                                                     StringComparison.OrdinalIgnoreCase) || funcBlock is IMyLightingBlock)));

            blocksToSwitchOn.UnionWith(_lights.Where(x => !x.BlockDefinition.TypeIdString.Substring(16).Equals("ReflectorLight",StringComparison.OrdinalIgnoreCase) &&
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
            if (!IsDocked() && _currentSpeed > 1 || _inGravity && _currentAltitude > 20 && !LandingLocked())
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
                        if (_highestChargedBattery != null && battery != _highestChargedBattery) battery.Enabled = false;
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
            foreach (var block in myCubeBlocks.OfType<IMyFunctionalBlock>())
            {
                if (Closed(block) || block.Enabled == on)continue;
                block.Enabled = on;
            }
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
            _damageDetected = TryGetDamagedBlocks(out _damagedBlocks);

            if (_handleRepair)BlockGroupEnable(_shipWelders, _damageDetected || _alert > AlertState.High || _shipWelders.Any(w=>w.IsWorking));

            if (!_powerManagement || !_fuelCollection.Any())return;

            if (_fuelCollection.Values.Sum(x=> (double)x) < _lowFuel || BatteryLevel() < _rechargePoint)
                _powerFlagDelay = DateTime.Now;
            _lowPower = (DateTime.Now - _powerFlagDelay).TotalSeconds < 15;

            _powerFlag = _lowPower && _reactors.Count(x => x.Enabled) == 0;

            if (_gravGens?.Any() == true) BlockGroupEnable(_gravGens, !_powerFlag);
        }

        private void GetCommandState(string st, out ProgramState result)
        {
            var t = st.Split('"', ' ');
            while (true)
            {

                if (t[0].Equals("switch", StringComparison.InvariantCultureIgnoreCase))
                {

                    if (t[1].Equals("toggle"))
                    {
                        if (t[2].Equals("weapons", StringComparison.OrdinalIgnoreCase) ||
                            t[2].Equals("turrets", StringComparison.OrdinalIgnoreCase))
                        {
                            _turretsActive = !_turretsActive;
                            break;
                        }

                    }
                    
                    if (t[2].Equals("weapons", StringComparison.OrdinalIgnoreCase) ||
                        t[2].Equals("turrets", StringComparison.OrdinalIgnoreCase))
                    {
                        if (t[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                            _turretsActive = true;
                        if (t[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                            _turretsActive = false;
                        break;
                    }
                }


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
                    EndTrip();

                    _giveControl =t.Length > 1 && bool.TryParse(t[1], out _giveControl) && _giveControl;
                    _setSpeed = t.Length > 2 &&double.TryParse(t[2], out _setSpeed) ? _setSpeed : 100;
                    _takeOffAngle = t.Length > 3 && double.TryParse(t[3], out _takeOffAngle) ? _takeOffAngle : 0;
                    _autoPilot = Pilot.Takeoff;
                    _thrust = 1f;
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
                    if (!_autoNavigate || !_inGravity)
                    {
                        Echo("Landing sequence unable to run");
                        break;
                    }
                    EndTrip();
                    _autoPilot = Pilot.Land;
                    break;
                }

                if (t[0].Equals("cruise", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_autoNavigate)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    if (IsConnectedToStatic() || _currentMode == ProgramState.Docked || _currentMode == ProgramState.Recharge)
                    {
                        Echo("Currently Docked");
                        break;
                    }

                    if (t[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_autoPilot == Pilot.Cruise)
                        {
                            Echo("Ending Trip");
                            EndTrip();
                            break;
                        }

                    }

                    double setHeight;
                    double setSpeed;
                    bool giveControl;
                    _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentAltitude);
                    _currentSpeed = _remoteControl.GetShipSpeed();
                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    _thrust = 0;
                    _autoPilot = Pilot.Cruise;
                    EnableDampeners(true);

                    _giveControl = t.Length > 1 && bool.TryParse(t[1], out giveControl) && giveControl || t[1].Equals("toggle",StringComparison.OrdinalIgnoreCase);
                    _setSpeed = t.Length > 2 && double.TryParse(t[2], out setSpeed) ? setSpeed : _currentSpeed;
                    _cruiseHeight = t.Length > 3 && double.TryParse(t[3], out setHeight) ? setHeight : _currentAltitude;
                    break;
                }

                if (t[0].Equals("power", StringComparison.OrdinalIgnoreCase))
                {
                    if (t[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_currentMode == ProgramState.PoweredOff)
                        {
                            _currentMode = ProgramState.PowerOn;
                            break;
                        }
                        _currentMode = ProgramState.ShuttingOff;
                        break;
                    }
                    if (t[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                        _currentMode = ProgramState.ShuttingOff;
                    if (t[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                        _currentMode = ProgramState.PowerOn;
                    if (t[1].Equals("recharge", StringComparison.OrdinalIgnoreCase))
                    {
                        _setRechargeState = false;
                        _currentMode = ProgramState.Recharge;
                    }
                }



                break;
            }

            result = _currentMode;
        }


        private AlertState GridFlags()
        {
            var state = AlertState.Clear;

            if (_navFlag || _productionFlag || _lowPower)
            {
                state = AlertState.Guarded;
                if (_powerFlag)
                    Log.Info(string.Join("\n",
                        $"Batteries in Recharge {_lowBlocks.OfType<IMyBatteryBlock>().Count()}/{_batteries.Count}",
                        $"Number of reactors online {_reactors.Count(x=>x.Enabled)}/{_reactors.Count} "));

            }

            if (_powerFlag)
            {
                state = AlertState.Elevated;
            }

            if (_damageDetected)
            {
                state = AlertState.High;
            }


            if (_combatFlag)
            {
                state = AlertState.Severe;
            }

            return state;
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
            if (!_capReactors || _reactors.Count <= 1)return;

            _fuelCollection = GetFuel();
            var start = (int) (startProportion * _reactors.Count);
            var end = (int) (endProportion * _reactors.Count);
            for (var i = start; i < end; ++i)
            {
                var reactor = _reactors[i];
                if (Closed(reactor))
                {
                    _reactors.RemoveAtFast(i);
                    continue;
                }

                reactor.UseConveyorSystem = false;

                MyItemType fuel;
                var reactorInvent = reactor.GetInventory();
                if (!TryGetFuel(reactor.BlockDefinition.SubtypeId, out fuel))
                {
                    continue;
                }



                MyFixedPoint count;

                if (!_fuelCollection.TryGetValue(fuel, out count))
                {
                    continue;
                }
                
                reactor.UseConveyorSystem = false;
                var lowCap = (double)count / _reactors.Count < _lowFuel
                    ? (MyFixedPoint) ((double)count  / _reactors.Count)
                    : (int) _lowFuel;

                var reactorFuel = reactorInvent.GetItemAmount(fuel);
                var amountDiff = reactorFuel - lowCap;
                if (Math.Abs((double)amountDiff) <= 1) continue;

                if (amountDiff < 0)
                {
                    var neededAmount = (MyFixedPoint) Math.Abs((double) amountDiff);
                    HashSet<IMyCargoContainer> cargoList;
                    if (!TryFindItem(fuel, out cargoList))
                    {
                        continue;
                    }
                    foreach (var cargo in cargoList)
                    {
                        var cargoInvent = cargo.GetInventory();
                        if (!cargoInvent.CanTransferItemTo(reactorInvent, fuel) || !cargoInvent.ContainItems(neededAmount, fuel)) continue;
                        var inventItem = cargoInvent.FindItem(fuel);
                        if (inventItem == null)
                        {
                            continue;
                        }
                        cargoInvent.TransferItemTo(reactorInvent, inventItem.Value, neededAmount);
                        break;
                    }
                    continue;
                }


                var z = reactorInvent.FindItem(fuel);
                if(z == null)continue;
                try
                {
                    reactorInvent.TransferItemTo(_dump.FirstOrDefault(x=>x.GetInventory().IsConnectedTo(reactorInvent))?.GetInventory(), z.Value, amountDiff);
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
                
            }
        }

        private bool TryGetFuel(string reactorSubId, out MyItemType fuel)
        {

            if (_reactorFuel.TryGetValue(reactorSubId, out fuel))
            {
                return true;
            }
            foreach (var reactor in _reactors)
            {
                if (Closed(reactor))
                {
                    _reactors.Remove(reactor);
                    continue;
                }
                if (reactor.BlockDefinition.SubtypeId != reactorSubId) continue;

                var itemList = new List<MyItemType>();
                reactor.GetInventory().GetAcceptedItems(itemList);
                fuel = itemList.FirstOrDefault();

                _reactorFuel[reactorSubId] = fuel;
                break;
            }
            return !string.IsNullOrEmpty(fuel.SubtypeId);
        }

        /// <summary>
        /// Finds fuel types present on the grids
        /// </summary>
        private Dictionary<MyItemType,MyFixedPoint> GetFuel()
        {
            var fuelCollection = new Dictionary<MyItemType,MyFixedPoint>();
            var usedFuel =new HashSet<MyItemType>( _reactorFuel.Values);

            if (usedFuel?.Any() == false || _cargoDict.Count == 0)
            {
                return fuelCollection;
            }


            foreach (var item in usedFuel)
            {
                MyFixedPoint count = 0;

                HashSet<IMyCargoContainer> inventBlocks;
                
                if (!TryFindItem(item, out inventBlocks)) continue;


                foreach (var block in inventBlocks)
                {
                    if (Closed(block)) continue;
                    var itemCount = block.GetInventory().GetItemAmount(item);
                    if (itemCount == 0) continue;
                    count += itemCount;
                }

                fuelCollection[item] = count;
            }

            return fuelCollection;
        }



        /// <summary>
        /// Circles through the batteries to maintain charge and power
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateBatteries(float startProportion, float endProportion)
        {
            if (!_powerManagement || !_batteries.Any()) return;

            if (_batteries.Count == 1)
            {
                _scheduler.AddQueuedAction(()=>RunBattery(_batteries[0]),0.01,true);
                return;
            }


            var start = (int) (startProportion * _batteries.Count);
            var end = (int) (endProportion * _batteries.Count);

            for (var i = start; i < end; ++i)
            {
                var battery = _batteries[i];

                _scheduler.AddQueuedAction(()=>RunBattery(battery),0.01,true);
            }

        }

        private void RunBattery(IMyBatteryBlock battery)
        {
            if (battery == null)
            {
                return;
            }

            if (Closed(battery))
            {
                _lowBlocks.Remove(battery);
                return;
            }
            var highestCharge = _highestChargedBattery?.CurrentStoredPower / _highestChargedBattery?.MaxStoredPower ??
                                0f;

            var maxRechargeBatteries = _isStatic ? Math.Round(0.35 * _batteries.Count,0):Math.Round(0.15 * _batteries.Count,0);

            var currentPercentCharge = battery.CurrentStoredPower / battery.MaxStoredPower;

                var allowRecharge = _lowBlocks.Keys.OfType<IMyBatteryBlock>().Count() < maxRechargeBatteries;

                if (SkipBlock(battery) || !battery.IsFunctional)
                {
                    _lowBlocks.Remove(battery);
                    return;
                }

                battery.Enabled = true;

                float charge;

                if (!_lowBlocks.TryGetValue(battery, out charge))
                {
                    if (currentPercentCharge >= highestCharge ||
                        (_highestChargedBattery == null && battery.HasCapacityRemaining))
                    {
                        _highestChargedBattery = battery;
                        battery.ChargeMode = ChargeMode.Auto;
                        return;
                    }

                    if (_currentMode == ProgramState.Recharge)
                    {
                        _lowBlocks[battery] = 1f;
                        return;
                    }

                    if (allowRecharge && (!battery.HasCapacityRemaining|| currentPercentCharge < _rechargePoint ))
                    {
                        _lowBlocks[battery] = 0.5f;
                        return;
                    }

                    battery.ChargeMode = _isStatic && BatteryLevel() > Math.Max(_rechargePoint,0.5f) ? ChargeMode.Discharge : ChargeMode.Auto;

                }

                if (battery == _highestChargedBattery || currentPercentCharge >= charge || _autoPilot > Pilot.Disabled || _currentSpeed > 25)
                {
                    
                    _lowBlocks.Remove(battery);
                    battery.ChargeMode = ChargeMode.Auto;
                    return;
                }

                battery.ChargeMode = ChargeMode.Recharge;
                _batteryHighestCharge = highestCharge > _rechargePoint ? highestCharge - 0.1f : 1f;
        }
        /// <summary>
        /// Obtain the total battery level
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Circles through reactors and turns them on if needed
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateReactors(float startProportion, float endProportion)
        {
            float meh;
            _isOverload = IsOverload(out meh);
            if (!_powerManagement) return;

            if (_reactors.Count <= 1)
            {
                _scheduler.AddQueuedAction(()=>RunReactor(_reactors[0]),0.01,true);
                return;
            }

            var start = (int) (startProportion * _reactors.Count);
            var end = (int) (endProportion * _reactors.Count);
            for (var i = start; i < end; ++i)
            {
                var reactor = _reactors[i];
                _scheduler.AddQueuedAction(()=>RunReactor(reactor),0.01,true);
            }
        }

        private void RunReactor(IMyReactor reactor)
        {
            if (Closed(reactor) || reactor == null)
            {
                return;
            }
            reactor.Enabled = (_isOverload || _alert > AlertState.Clear) && reactor.GetInventory().CurrentMass > 0;
        }
        /// <summary>
        /// Checks if power is overloaded
        /// </summary>
        /// <param name="power"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Check if any landing gear on the grid is locked
        /// </summary>
        /// <returns></returns>
        private bool LandingLocked()
        {
            return !_isStatic && _landingGears.Any(landingGear => landingGear.IsLocked);
        }

        /// <summary>
        /// Check if any connector on the grid is connected
        /// </summary>
        /// <returns></returns>
        private bool IsConnected()
        {
            return _connectors.Any(connector => connector.Status == MyShipConnectorStatus.Connected);
        }

        /// <summary>
        /// Checks if ship is connected to a static grid
        /// </summary>
        /// <returns></returns>
        private  bool IsConnectedToStatic()
        {
            GridTerminalSystem.GetBlocks(_allBlocks);
            return _allBlocks.Any(x =>x.CubeGrid.IsStatic);
        }

        /// <summary>
        /// Checks docking state
        /// </summary>
        /// <returns></returns>
        private bool IsDocked()
        {
            return !_isStatic && IsConnectedToStatic() && _connectors.Any(connector =>
                       connector.Status == MyShipConnectorStatus.Connected);
        }





        /// <summary>
        /// check if grid needs repair and returns damaged blocks
        /// </summary>
        /// <param name="damagedBlocks"></param>
        /// <returns></returns>
        private bool TryGetDamagedBlocks(out HashSet<IMyTerminalBlock> damagedBlocks)
        {
            damagedBlocks = new HashSet<IMyTerminalBlock>();
            if (!_allBlocks.Any()) return false;
            var dam = _allBlocks?.Where(block =>
                !Closed(block) && (block.CubeGrid.GetCubeBlock(block.Position).CurrentDamage > 0));
            damagedBlocks.UnionWith(dam);

            if (damagedBlocks.Count == 0)
            {
                return false;
            }
            Log.Info($"{damagedBlocks.Count} blocks damaged");
            Log.Damage($"{damagedBlocks.Count} blocks in need of repair:");
            foreach (var block in damagedBlocks)
            {
                if (_showOnHud)block.ShowOnHUD = true;
                Log.Damage( $"->{block.CustomName}");
            }

            return true;
        }

        #region Inventory

        private void UpdateInventory()
        {
            if (_containers.Count == 0) return;
            var alreadyUpdatedList = new HashSet<IMyCargoContainer>();
            for (int i = 0; i < 5; i++)
            {
                var container = _containers.Dequeue();
                if (alreadyUpdatedList.Contains(container))break;
                var itemList = new List<MyInventoryItem>();
                container.GetInventory().GetItems(itemList);
                _cargoDict[container] = new HashSet<MyInventoryItem>(itemList);
                _containers.Enqueue(container);
                alreadyUpdatedList.Add(container);
            }


        }

        private bool TryFindItem(MyItemType item, out HashSet<IMyCargoContainer> cargoContainers)
        {
            cargoContainers = new HashSet<IMyCargoContainer>();

            if (_cargoDict.Count <= 0) return false;

            foreach (var cargoInfo in _cargoDict )
            {
                if (cargoInfo.Value.All(x => x.Type != item))continue;
                cargoContainers.Add(cargoInfo.Key);
            }

            return cargoContainers.Any();
        }

        #endregion

        #region Production

        /// <summary>
        /// Checks and updates production blocks
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateProduction(float startProportion, float endProportion)
        {
            if (!_controlProduction ) return;

            if (_productionBlocks.Count <= 1)
            {
                _scheduler.AddQueuedAction(()=>RunProductionBlock(_productionBlocks[0]),0.01,true);
                return;
            }

            var start = (int) (startProportion * _productionBlocks.Count);
            var end = (int) (endProportion * _productionBlocks.Count);
            for (var i = start; i < end; ++i)
            {
                var block = _productionBlocks[i];
                _scheduler.AddQueuedAction(()=>RunProductionBlock(block),0.01,true);
            }
        }

        private void RunProductionBlock(IMyProductionBlock block)
        {
            if (block == null || Closed(block))return;

            if (_powerFlag)
            {
                block.Enabled = false;
                return;
            }

            var blockInvent = block.GetInventory();
                var blockIsRefinery = block is IMyRefinery;

                DateTime time;

                if (!_collection.TryGetValue(block, out time))
                {
                    if (!block.Enabled)
                    {
                        if (!block.IsQueueEmpty)
                        {
                            _collection[block] = DateTime.Now;
                        }
                        if (blockIsRefinery)
                        {
                            var inputItemType = new List<MyItemType>();
                            block.InputInventory.GetAcceptedItems(inputItemType);

                            foreach (var item in inputItemType)
                            {
                                HashSet<IMyCargoContainer> itemContainers;
                                if (!TryFindItem(item, out itemContainers)) continue;
                                var chosenContainer =
                                    itemContainers.FirstOrDefault(x => x.GetInventory().CanTransferItemTo(blockInvent, item));
                                HashSet<MyInventoryItem> inventItems;
                                if (chosenContainer == null || !_cargoDict.TryGetValue(chosenContainer, out inventItems)) continue;
                                var inventItem = inventItems.FirstOrDefault(x=>x.Type == item);
                                var maxToPull = (MyFixedPoint)(Math.Min(Math.Abs((double)(block.InputInventory.MaxVolume - block.InputInventory.CurrentVolume)),(double)inventItem.Amount));
                                chosenContainer.GetInventory().TransferItemTo(block.InputInventory, inventItem, maxToPull);
                            }
                        }

                        if (block.OutputInventory.ItemCount > 0)
                            EmptyProductionBlock(block);
                        return;
                    }
                    _collection[block] = DateTime.Now;
                    return;
                }

                if (block.IsProducing || !block.IsQueueEmpty)
                {
                    block.Enabled = true;
                    _collection[block] = DateTime.Now;
                    return;
                }

                if ((DateTime.Now - time).TotalSeconds < ProductionDelay) return;
                block.Enabled = false;
                _collection.Remove(block);

        }
        /// <summary>
        /// Empties any production block not working
        /// </summary>
        /// <param name="block"></param>
        private void EmptyProductionBlock(IMyProductionBlock block)
        {
            var someCargo = new List<IMyCargoContainer>(_containers.Where(x=>!Closed(x) && !x.GetInventory().IsFull));
            
            if (someCargo.Count == 0) return;

            var assembler = block as IMyAssembler;
            var meh = new List<MyInventoryItem>();
            var cargo = someCargo.FirstOrDefault(x =>
                x.GetInventory().IsConnectedTo(block.GetInventory()));

            if (cargo == null)return;

            var blockInputInventory = block.InputInventory;
            var blockOutputInventory = block.OutputInventory;

            var cargoInventory = cargo.GetInventory();

            blockInputInventory.GetItems(meh);

            foreach (var item in meh.TakeWhile(x => assembler != null))
            {
                try
                {
                    blockInputInventory.TransferItemTo(cargoInventory,item,item.Amount);
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
            meh.Clear();

            blockOutputInventory.GetItems(meh);

            foreach (var item in meh.TakeWhile(x=>true))
            {
                try
                {
                    blockOutputInventory.TransferItemTo(cargoInventory,item,item.Amount);
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }

        }

        #endregion

        #region Gas Montoring

        /// <summary>
        /// Controls Gas Generators
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateGasGen(float startProportion, float endProportion)
        {
            if (!_controlGasSystem)return;

            var enableGen = _lowBlocks.Keys.OfType<IMyGasTank>().Any() || _tankRefill;
            _tankRefill = false;
            if (_gasGens.Count <= 1)
            {
                _scheduler.AddQueuedAction(()=>RunGen(_gasGens[0],enableGen),0.01,true);
                return;
            }

            var start = (int) (startProportion * _gasGens.Count);
            var end = (int) (endProportion * _gasGens.Count);
            
            for (int i = start; i < end; i++)
            {
                var gen = _gasGens[i];
                _scheduler.AddQueuedAction(()=>RunGen(gen,enableGen),0.01,true);
            }


        }

        private void RunGen(IMyGasGenerator gen, bool turnOn)
        {
            if (Closed(gen) || gen == null)return;

            GasBlockParseIni(gen);
            if (!_gasBlockControlled)
            {
                GasBlockParseIniDefault();
                return;
            }

            gen.Enabled = turnOn && !_powerFlag;
        }

        /// <summary>
        /// Updates gas tanks
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateTanks(float startProportion, float endProportion)
        {
            if (!_controlGasSystem  ) return;
            _genOnline = _gasGens.Any(x => x.IsWorking);
            if (_gasTanks.Count <= 1)
            {
                _scheduler.AddQueuedAction(()=>RunTank(_gasTanks[0]),0.01,true);
                return;
            }
            var start = (int) (startProportion * _gasTanks.Count);
            var end = (int) (endProportion * _gasTanks.Count);
            for (var i = start; i < end; ++i)
            {
                var tank = _gasTanks[i];
                _scheduler.AddQueuedAction(()=>RunTank(tank),0.01,true);
            }

        }

        private void RunTank(IMyGasTank tank)
        {
            if (tank == null || Closed(tank))return;
            GasBlockParseIni(tank);
            if (!_gasBlockControlled)
            {
                GasBlockParseIniDefault();
                return;
            }
            float value;
            if (!_lowBlocks.TryGetValue(tank, out value))
            {
                if (tank.FilledRatio > _gasBlockTankMinFilledRatio)
                {
                    tank.Stockpile = false;
                    tank.Enabled = !_genOnline || (_inGravity && !_isStatic) || _currentSpeed > 10;
                    GasBlockParseIniDefault();
                    return;
                }

                _lowBlocks[tank] = _gasBlockTankMaxFilledRatio;
                GasBlockParseIniDefault();
                return;
            }

            if (tank.FilledRatio < value - value * 0.1)
            {
                tank.Enabled = true;
                tank.Stockpile = _isStatic || !_inGravity;
                _tankRefill = true;
                GasBlockParseIniDefault();
                return;
            }

            GasBlockParseIniDefault();

            tank.Stockpile = false;
            _lowBlocks.Remove(tank);


        }

        /// <summary>
        /// Checks and updates ventilation
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateVents(float startProportion, float endProportion)
        {
            if (!_controlVents) return;

            if (_airVents.Count <= 1)
            {
                _scheduler.AddQueuedAction(()=>RunVent(_airVents[0]),0.001,true);
                return;
            }

            var start = (int) (startProportion * _airVents.Count);

            var end = (int) (endProportion * _airVents.Count);

            for (var i = start; i < end; ++i)
            {
                var vent = _airVents[i];
                _scheduler.AddQueuedAction(()=>RunVent(vent),0.001,true);
            }
        }

        private void RunVent(IMyAirVent vent)
        {
            if (vent == null)return;
            if (Closed(vent))
            {
                return;
            }
            GasBlockParseIni(vent);

            if (!_gasBlockControlled)
            {
                GasBlockParseIniDefault();
                return;
            }

            if (vent.Depressurize)
            {
                vent.Enabled = true;
                GasBlockParseIniDefault();
                return;
            }

            if (!vent.CanPressurize)
            {
                if (_showOnHud)vent.ShowOnHUD = true;
                Log.Error(vent.CustomName + " Can't Pressurize");
                GasBlockParseIniDefault();
                return;
            }

            vent.Enabled = IsNeedAir(vent);
            GasBlockParseIniDefault();


        }
        /// <summary>
        /// Checks vents O2 levels
        /// </summary>
        /// <param name="vent"></param>
        /// <returns></returns>
        private bool IsNeedAir(IMyAirVent vent)
        {
            var powerState = vent.Enabled;
            vent.Enabled = true;
            var oxygenState = vent.GetOxygenLevel();
            vent.Enabled = powerState;
            return oxygenState <= _gasBlockVentMinAirDensity;
        }



        // Settings

        private readonly MyIni _gasBlockIni = new MyIni();
        private readonly List<string> _gasIniSections = new List<string>();
        private readonly StringBuilder _gasIniCustomDataSb = new StringBuilder();


        private const string IniSectionGasSystem = "Cerebro Gas Control System Settings";
        private const string IniGasSystemControlled = "Controlled";
        private const string IniSectionVents = "Cerebro Vent Controls";
        private const string IniVentsMinAir = "Min AirDensity";
        private const string IniSectionTanks = "Cerebro Tank Controls";
        private const string IniTanksMinFill = "Min Filled Ratio";
        private const string IniTanksMaxFill = "Max Filled Ratio";

        // Gas Blocks Settings Variables
        private bool _gasBlockControlled = true;
        private float _gasBlockVentMinAirDensity = 0.75f;
        private float _gasBlockTankMinFilledRatio = 0.5f;
        private float _gasBlockTankMaxFilledRatio = 1f;


        /// <summary>
        /// Gas block settings
        /// </summary>
        /// <param name="gasBlock"></param>
        private void GasBlockParseIni(IMyTerminalBlock gasBlock)
        {
            _gasBlockIni.Clear();
            _gasBlockIni.TryParse(gasBlock.CustomData);


            _gasIniSections.Clear();
            _gasBlockIni.GetSections(_gasIniSections);

            if (_gasIniSections?.Any() == false)
            {
                _gasIniCustomDataSb.Clear();
                _gasIniCustomDataSb.Append(gasBlock.CustomData);
                _gasIniCustomDataSb.Replace("---\n", "");

                _gasBlockIni.EndContent = _gasIniCustomDataSb.ToString();
            }

            _gasBlockControlled = _gasBlockIni.Get(IniSectionGasSystem, IniGasSystemControlled)
                .ToBoolean(_gasBlockControlled);

            _gasBlockIni.Set(IniSectionGasSystem,IniGasSystemControlled,_gasBlockControlled);

            if (gasBlock is IMyAirVent)
            {
                _gasBlockVentMinAirDensity = _gasBlockIni.Get(IniSectionVents,IniVentsMinAir).ToSingle(_gasBlockVentMinAirDensity);
                _gasBlockIni.Set(IniSectionVents,IniVentsMinAir,_gasBlockVentMinAirDensity);
            }

            if (gasBlock is IMyGasTank)
            {
                //Get
                _gasBlockTankMinFilledRatio = _gasBlockIni.Get(IniSectionTanks,IniTanksMinFill).ToSingle(_gasBlockTankMinFilledRatio);
                _gasBlockTankMaxFilledRatio = _gasBlockIni.Get(IniSectionTanks,IniTanksMaxFill).ToSingle(_gasBlockTankMaxFilledRatio);

                //Set
                _gasBlockIni.Set(IniSectionTanks,IniTanksMinFill,_gasBlockTankMinFilledRatio);
                _gasBlockIni.Set(IniSectionTanks,IniTanksMaxFill,_gasBlockTankMaxFilledRatio);
            }



            var output = _gasBlockIni.ToString();
            if (!string.Equals(output, gasBlock.CustomData))
            {
                gasBlock.CustomData = output;
            }


        }

        /// <summary>
        /// Resets Gas block Ini
        /// </summary>
        private void GasBlockParseIniDefault()
        {
         _gasBlockControlled = true;
         _gasBlockVentMinAirDensity = 0.75f;
         _gasBlockTankMinFilledRatio = 0.5f;
         _gasBlockTankMaxFilledRatio = 1f;
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
        private bool _turretsActive = true;


        private const string INI_SECTION_TURRETMAIN = "Turret Settings";
        private const string INI_TURRETMAIN_TURRETAGGRESION = "Turret Aggression Trigger";
        private const string INI_TURRETMAIN_TURRETTARGET = "Turret Duty";
        private const string INI_SECTION_ROTATION = "Turret Rotation Limits";
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

        /// <summary>
        /// Sets turret Ini
        /// </summary>
        /// <param name="turret"></param>
        private void TurretParseIni(IMyLargeTurretBase turret)
        {
            _turretIni.Clear();
            _turretIni.TryParse(turret.CustomData);

            var minPriority = Math.Min(_defaultAggression * _aggressionMultiplier, _turrets.Count);
            var priority = new Random().Next(_defaultAggression - 1, minPriority);

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

            if (_aggressionTrigger > minPriority || _aggressionTrigger == 0 && string.IsNullOrEmpty(_turretDuty))
            {
                _aggressionTrigger = priority;
            }

            if (string.IsNullOrEmpty(_turretDuty))
            {
                _turretDuty = _turrets.Any(x=>x.Enabled)?"None":_designatorName;
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

        /// <summary>
        /// Resets turret Ini
        /// </summary>
        private static void TurretSettingsDefault()
        {
            _turretDuty = "";
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
            var elevateAngle = VectorMath.AngleBetween(turret.WorldMatrix.Forward, target.Position);
            var azimuthAngle = VectorMath.AngleBetween(turret.WorldMatrix.Up, target.Position);
            var targetDistance = Vector3D.Distance(turret.GetPosition(), target.Position);

            return targetDistance <= turret.Range;
        }
        
        /// <summary>
        /// Controls Turrets
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateTurrets(float startProportion, float endProportion)
        {
            if (_turrets.Count <= 1 || !_turretControl)
                return;

            if (_turrets.Count <= 1)
            {
                _turretControl = false;
                return;
            }

            var start = (int) (startProportion * _turrets.Count);
            var end = (int) (endProportion * _turrets.Count);
            for (var i = start; i < end; ++i)
            {
                var turret = _turrets[i];
                if (Closed(turret) || turret.IsUnderControl)
                {
                    continue;
                }

                if (!_turretsActive)
                {
                    turret.Enabled = false;
                    if (Math.Abs(turret.Elevation + turret.Azimuth) < 0.01  || _turretsToCenter.Contains(turret)) continue;

                    _turretsToCenter.Enqueue(turret);

                    continue;
                }
                TurretSettingsDefault();
                TurretParseIni(turret);
                

                if (!_turretDuty.Equals("None",StringComparison.OrdinalIgnoreCase))
                {
                    SetTurret(turret);
                    if (_turretDuty == _antimissileName)
                        turret.Enabled = _combatFlag;
                    if (!turret.HasTarget)
                    {
                        DateTime time;
                        if (!_collection.TryGetValue(turret, out time))
                        {
                            _collection[turret] = DateTime.Now;
                            continue;

                        }
                        if (Math.Abs((DateTime.Now - time).TotalSeconds) >= 10) continue;
                        _collection.Remove(turret);
                        turret.EnableIdleRotation = false;
                        if (Math.Abs(turret.Elevation + turret.Azimuth) < 0.01  || _turretsToCenter.Contains(turret)) continue;

                        _turretsToCenter.Enqueue(turret);
                    }

                    ResetAi(turret);
                    continue;
                }

                
                //compare number in custom data to aggression and turn off if higher
                //turret.Enabled = _aggressionTrigger < _aggression;


                if (!turret.HasTarget)
                {
                    ResetAi(turret);
                    Refocus(turret);
                }

                turret.SetValueBool("Shoot", false);

                if (turret.GetInventory().ItemCount != 0 || !turret.HasInventory || !_showOnHud) continue;
                turret.ShowOnHUD = true;

            }
        }

        /// <summary>
        /// Sets target for turrets
        /// </summary>
        private void Refocus(IMyLargeTurretBase turret)
        {

            if (turret.IsShooting)turret.SetValueBool("Shoot",false);
            var possibleTargets = _myTargets.Where(x =>  InSight(turret, x)).ToArray();
            
            if (!possibleTargets.Any())
            {
                turret.Enabled = _aggressionTrigger < _aggression;
                if (turret.Enabled) return;
                if (Math.Abs(turret.Elevation + turret.Azimuth) < 0.01) return;

                if (_turretsToCenter.Contains(turret))return;
                _turretsToCenter.Enqueue(turret);
                return;
            }

            var target = possibleTargets[new Random().Next(0, possibleTargets.Length)];
            turret.SetTarget(target.Position);
            turret.TrackTarget(target.Position, target.Velocity);

            ResetAi(turret);
            

        }

        /// <summary>
        /// Reset turret AI
        /// </summary>
        /// <param name="turret"></param>
        private void ResetAi(IMyLargeTurretBase turret)
        {
            turret.ResetTargetingToDefault();
            turret.EnableIdleRotation = false;
        }

        /// <summary>
        /// Return turrets back to center
        /// </summary>
        private void ReturnToCenter()
        {
            if (_turretsToCenter.Count == 0)return;

            var turret = _turretsToCenter.Dequeue();
            if (Closed(turret) || turret.IsUnderControl)
            {
                return;
            }
            if (Math.Abs(turret.Elevation + turret.Azimuth) < 0.01)
            {
               turret.ResetTargetingToDefault();
                return;
            }
            _turretsToCenter.Enqueue(turret);
            turret.EnableIdleRotation = false;

            if (Math.Abs(turret.Elevation) > 0.01)
            {
                turret.Elevation = turret.Elevation > 0 ? turret.Elevation - 0.01f : turret.Elevation + 0.01f;
                turret.SyncElevation();
                return;
            }
            turret.Azimuth = turret.Azimuth > 0 ? turret.Azimuth - 0.01f : turret.Azimuth + 0.01f;
            turret.SyncAzimuth();
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
        /// Acquire lists of targets from turrets
        /// </summary>
        /// <param name="targets"></param>
        /// <returns></returns>
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
                ? Math.Min(_aggression += 0.2, _turrets.Count)
                : Math.Max(_aggression -= 2, _defaultAggression);

            _combatFlag = _aggression > _defaultAggression;

        }



        #endregion




        /// <summary>
        /// Check if any connector is requesting connection and connects
        /// </summary>
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



        /// <summary>
        /// Reset block lists
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
            _barWelders.Clear();
             
            

            GridTerminalSystem.GetBlockGroupWithName(_welderGroup)?.GetBlocksOfType(_shipWelders);

            foreach (var welder in _shipWelders)
            {
                if (!welder.BlockDefinition.SubtypeId.Substring(16).Equals("SELtdLargeNanobotBuildAndRepairSystem")) continue;
                _barWelders.Add(welder);
            }

            GridTerminalSystem.GetBlocks(_allBlocks);

            _gridBlocks.AddRange(_allBlocks.Where(x=>x.IsSameConstructAs(Me) && !StringContains(x.CustomData, "ignore") &&
                                                     !StringContains(x.CustomName, "ignore") ));


            _myProjector = GridTerminalSystem.GetBlockWithName(_reProj) as IMyProjector;

            _landingGears = new HashSet<IMyLandingGear>(_gridBlocks.OfType<IMyLandingGear>());
            _soundBlocks = new HashSet<IMySoundBlock>( _gridBlocks.OfType<IMySoundBlock>());
            _powerBlocks = new HashSet<IMyPowerProducer>(_gridBlocks.OfType<IMyPowerProducer>());
            _gyros = new HashSet<IMyGyro>(_gridBlocks.OfType<IMyGyro>());
            _productionBlocks = new List<IMyProductionBlock>(_gridBlocks.OfType<IMyProductionBlock>().Where(x=>!StringContains(x.BlockDefinition.TypeIdString.Substring(16),"survivalkit")));
            _gravGens = new HashSet<IMyGravityGenerator>(_gridBlocks.OfType<IMyGravityGenerator>());
            _batteries = new List<IMyBatteryBlock>(_gridBlocks.OfType<IMyBatteryBlock>());
            _reactors = new List<IMyReactor>(_gridBlocks.OfType<IMyReactor>());
            _gasTanks = new List<IMyGasTank>(_gridBlocks.OfType<IMyGasTank>());
            _airVents = new List<IMyAirVent>(_gridBlocks.OfType<IMyAirVent>());
            _textPanels = new List<IMyTextPanel>(_gridBlocks.OfType<IMyTextPanel>());
            _connectors = new HashSet<IMyShipConnector>(_gridBlocks.OfType<IMyShipConnector>());
            _turrets = new List<IMyLargeTurretBase>(_gridBlocks.OfType<IMyLargeTurretBase>());
            _lights = new HashSet<IMyLightingBlock>(_gridBlocks.OfType<IMyLightingBlock>());
            _doors = new HashSet<IMyDoor>(_gridBlocks.OfType<IMyDoor>());
            _solars = new HashSet<IMySolarPanel>(_gridBlocks.OfType<IMySolarPanel>());
            _windTurbine = new HashSet<IMyPowerProducer>(_gridBlocks.OfType<IMyPowerProducer>().Where(x => x.BlockDefinition.TypeIdString.ToString().Substring(16).Equals("windturbine", StringComparison.OrdinalIgnoreCase)));
            _cockpits = new HashSet<IMyShipController>(_gridBlocks.OfType<IMyShipController>());
            _remotes = new HashSet<IMyRemoteControl>(_gridBlocks.OfType<IMyRemoteControl>());
            _gasGens = new List<IMyGasGenerator>(_gridBlocks.OfType<IMyGasGenerator>());
            _thrusters = new HashSet<IMyThrust>(_gridBlocks.OfType<IMyThrust>());
            _dump = new List<IMyCargoContainer>(_gridBlocks.OfType<IMyCargoContainer>()
                .Where(x => StringContains(x.CustomName,"dump" ) || StringContains(x.CustomData, "dump")));

            if (_master)
            {
                foreach (var container in _allBlocks.OfType<IMyCargoContainer>())
                {
                    if(_containers.Contains(container))continue;
                    _containers.Enqueue(container);
                }
            }

            else
            {
                foreach (var container in _gridBlocks.OfType<IMyCargoContainer>())
                {
                    if(_containers.Contains(container))continue;
                    _containers.Enqueue(container);
                }
            }

            if (_dump.Count < 10)
            {
                _dump.AddRange(_gridBlocks.OfType<IMyCargoContainer>().Where(x=>!x.GetInventory().IsFull));
            }

                
            foreach (var block in _allBlocks)
            {
                block.ShowOnHUD = false;
                if (_removeBlocksOnTerminal) block.ShowInTerminal = false;
                if (_removeBlocksInConfigurationTab) block.ShowInToolbarConfig = false;
            }
        }

        /// <summary>
        /// Compare strings
        /// </summary>
        /// <param name="source"></param>
        /// <param name="toCheck"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        private static bool StringContains(string source, string toCheck,
            StringComparison comp = StringComparison.OrdinalIgnoreCase)
        {
            return source?.IndexOf(toCheck, comp) >= 0;
        }

        /// <summary>
        /// Check if block should be skip
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool SkipBlock(IMyCubeBlock block)
        {
            return Closed(block) || !IsOwned(block);
        }

        /// <summary>
        /// Check if block is still present in the world
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private static bool Closed(IMyEntity block)
        {
            return Vector3D.IsZero(block.WorldMatrix.Translation);
        }


        /// <summary>
        /// Check block ownership
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool IsOwned(IMyCubeBlock block)
        {
            return block.GetUserRelationToOwner(Me.OwnerId) == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                   block.OwnerId == Me.OwnerId;
        }

        #region mdk preserve 

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
            PoweredOff,
            ShuttingOff,
            Recharge,
            Docked,
            Stop,
            Start,
            PowerOn,
            Normal
        }

        #endregion

        #region Runtime tracking

        /// <summary>
        ///     Class that tracks runtime history.
        /// </summary>
        private class RuntimeTracker
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

            private int Capacity { get; set; }
            private double Sensitivity { get; set; }
            private double MaxRuntime { get;  set; }
            private double MaxInstructions { get;  set; }
            private double AverageRuntime { get;  set; }
            private double AverageInstructions { get; set; }

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
            public static Vector3D VectorProjection(Vector3D a, Vector3D b) //proj a on b    
            {
                Vector3D projection = a.Dot(b) / b.LengthSquared() * b;
                return projection;
            }

            public static Vector3D VectorRejection(Vector3D a, Vector3D b) //proj a on b    
            {
                return a - VectorProjection(a, b);
            }

            /// <summary>
            ///     Computes angle between 2 vectors
            /// </summary>
            public static double AngleBetween(Vector3D a, Vector3D b) //returns radians
            {
                if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                    return 0;
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
            }

            public static Vector3D GetShipEdgeVector(IMyTerminalBlock reference, Vector3D direction)
            {
                //get grid relative max and min
                var gridMinimum = reference.CubeGrid.Min;
                var gridMaximum = reference.CubeGrid.Max;

                //get dimension of grid cubes
                var gridSize = reference.CubeGrid.GridSize;

                //get worldmatrix for the grid
                var gridMatrix = reference.CubeGrid.WorldMatrix;

                //convert grid coordinates to world coords
                var worldMinimum = Vector3D.Transform(gridMinimum * gridSize, gridMatrix);
                var worldMaximum = Vector3D.Transform(gridMaximum * gridSize, gridMatrix);

                //get reference position
                var origin = reference.GetPosition();

                //compute max and min relative vectors
                var minRelative = worldMinimum - origin;
                var maxRelative = worldMaximum - origin;

                //project relative vectors on desired direction
                var minProjected = Vector3D.Dot(minRelative, direction) / direction.LengthSquared() * direction;
                var maxProjected = Vector3D.Dot(maxRelative, direction) / direction.LengthSquared() * direction;

                //check direction of the projections to determine which is correct
                if (Vector3D.Dot(minProjected, direction) > 0)
                    return minProjected;
                else
                    return maxProjected;
            }

        }

        #endregion

        #region Scheduler
        /// <summary>
        /// Class for scheduling actions to occur at specific frequencies. Actions can be updated in parallel or in sequence (queued).
        /// </summary>
        private class Scheduler
        {
            private ScheduledAction _currentlyQueuedAction;
            private bool _firstRun = true;

            private readonly bool _ignoreFirstRun;
            private readonly List<ScheduledAction> _scheduledActions = new List<ScheduledAction>();
            private readonly HashSet<ScheduledAction> _actionsToDispose = new HashSet<ScheduledAction>();
            private readonly Queue<ScheduledAction> _queuedActions = new Queue<ScheduledAction>();
            private readonly Program _program;

            private const double RuntimeToRealtime = 1.0 / 60.0 / 0.0166666;

            /// <summary>
            /// Constructs a scheduler object with timing based on the runtime of the input program.
            /// </summary>
            public Scheduler(Program program, bool ignoreFirstRun = false)
            {
                _program = program;
                _ignoreFirstRun = ignoreFirstRun;
            }

            /// <summary>
            /// Updates all ScheduledActions in the schedule and the queue.
            /// </summary>
            public void Update()
            {
                var deltaTime = Math.Max(0, _program.Runtime.TimeSinceLastRun.TotalSeconds * RuntimeToRealtime);
                
                if (_ignoreFirstRun && _firstRun)
                    deltaTime = 0;

                _firstRun = false;
                _actionsToDispose.Clear();
                foreach (var action in _scheduledActions)
                {
                    action.Update(deltaTime);
                    if (action.JustRan && action.DisposeAfterRun)
                    {
                        _actionsToDispose.Add(action);
                    }
                }

                // Remove all actions that we should dispose
                _scheduledActions.RemoveAll((x) => _actionsToDispose.Contains(x));

                if (_currentlyQueuedAction == null)
                {
                    // If queue is not empty, populate current queued action
                    if (_queuedActions.Count != 0)
                        _currentlyQueuedAction = _queuedActions.Dequeue();
                }

                // If queued action is populated
                if (_currentlyQueuedAction == null) return;
                _currentlyQueuedAction.Update(deltaTime);

                if (!_currentlyQueuedAction.JustRan) return;
                // If we should recycle, add it to the end of the queue
                if (!_currentlyQueuedAction.DisposeAfterRun)
                    _queuedActions.Enqueue(_currentlyQueuedAction);

                // Set the queued action to null for the next cycle
                _currentlyQueuedAction = null;
            }

            public void Reset()
            {
                _currentlyQueuedAction = null;
                _queuedActions.Clear();
                _scheduledActions.Clear();
            }

            /// <summary>
            /// Adds an Action to the schedule. All actions are updated each update call.
            /// </summary>
            public void AddScheduledAction(Action action, double updateFrequency, bool disposeAfterRun = false, double timeOffset = 0)
            {
                var scheduledAction = new ScheduledAction(action, updateFrequency, disposeAfterRun, timeOffset);
                _scheduledActions.Add(scheduledAction);
            }

            /// <summary>
            /// Adds a ScheduledAction to the schedule. All actions are updated each update call.
            /// </summary>
            public void AddScheduledAction(ScheduledAction scheduledAction)
            {
                _scheduledActions.Add(scheduledAction);
            }

            /// <summary>
            /// Adds an Action to the queue. Queue is FIFO.
            /// </summary>
            public void AddQueuedAction(Action action, double updateInterval, bool disposeAfterRun = false)
            {
                if (updateInterval <= 0)
                {
                    updateInterval = 0.001; // avoids divide by zero
                }
                var scheduledAction = new ScheduledAction(action, 1.0 / updateInterval, disposeAfterRun);
                _queuedActions.Enqueue(scheduledAction);
            }

            /// <summary>
            /// Adds a ScheduledAction to the queue. Queue is FIFO.
            /// </summary>
            public void AddQueuedAction(ScheduledAction scheduledAction)
            {
                _queuedActions.Enqueue(scheduledAction);
            }
        }


        private class ScheduledAction
        {
            public bool JustRan { get; private set; } = false;
            public bool DisposeAfterRun { get; private set; } = false;
            private double TimeSinceLastRun { get; set; } = 0;
            private readonly double _runInterval;
            private readonly double _runFrequency;
            
            private readonly Action _action;


            /// <summary>
            /// Class for scheduling an action to occur at a specified frequency (in Hz).
            /// </summary>
            /// <param name="action">Action to run</param>
            /// <param name="runFrequency">How often to run in Hz</param>
            /// <param name="removeAfterRun"></param>
            /// <param name="timeOffset"></param>
            public ScheduledAction(Action action, double runFrequency, bool removeAfterRun = false, double timeOffset = 0)
            {
                _action = action;
                _runFrequency = runFrequency;
                _runInterval = 1.0 / runFrequency;
                DisposeAfterRun = removeAfterRun;
                TimeSinceLastRun = timeOffset;
            }

            public void Update(double deltaTime)
            {
                TimeSinceLastRun += deltaTime;

                if (TimeSinceLastRun < _runInterval)
                {
                    JustRan = false;
                    return;
                }
                _action.Invoke();
                TimeSinceLastRun = 0;

                JustRan = true;
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
            var ran = new Random().Next(1, _myTargets.Count);
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
            if (!_autoNavigate) return;
            if (_remoteControl == null || !_isStatic && _autoNavigate && !TryGetRemote(out _remoteControl))
            {
                if (_isStatic || !_autoNavigate || TryGetRemote(out _remoteControl)) return;
                _autoPilot = Pilot.Disabled;
                _navFlag = true;
                return;
            }

            _navFlag = false;
            _currentAltitude = 0;
            _inGravity = _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentAltitude);
            _currentAltitude -= _shipHeight;
            _currentSpeed = _remoteControl.GetShipSpeed();
            _shipMass = _remoteControl.CalculateShipMass().PhysicalMass;
            _gravityVec = _remoteControl.GetNaturalGravity();
            _gravityMagnitude = _gravityVec.Length();
            _shipVelocityVec = _remoteControl.GetShipVelocities().LinearVelocity;

            if (_shipVelocityVec.LengthSquared() > _maxSpeed * _maxSpeed)
                _maxSpeed = _shipVelocityVec.Length();

            _downSpeed = VectorMath.VectorProjection(_shipVelocityVec, _gravityVec).Length() * Math.Sign(_shipVelocityVec.Dot(_gravityVec));

            switch (_autoPilot)
            {
                case Pilot.Disabled:
                    CheckThrusters();
                    break;
                case Pilot.Cruise:
                    Log.Info($"Cruise set to {_setSpeed}m/s");
                    //_sbStatus.AppendLine($"Cruise set to {_setSpeed}m/s");
                    Cruise(_setSpeed, _cruiseHeight, _giveControl);
                    return;
                case Pilot.Land:
                    Log.Info($"AutoPilot Landing");
                    //_sbStatus.AppendLine($"AutoPilot Landing");
                    RotateGrid(false);
                    _downSpeed = VectorMath.VectorProjection(_shipVelocityVec, _gravityVec).Length() * Math.Sign(_shipVelocityVec.Dot(_gravityVec));
                    Land();
                    return;
                case Pilot.Takeoff:
                    Log.Info($"AutoPilot exiting planetary gravity\nSet Speed: {_setSpeed}\nSetAngle: {_takeOffAngle}");
                    //_sbStatus.AppendLine($"AutoPilot exiting planetary gravity");
                    _upSpeed = - VectorMath.VectorProjection(_shipVelocityVec, _gravityVec).Length() * Math.Sign(_shipVelocityVec.Dot(_gravityVec));
                    if (LandingLocked())LockLandingGears(false);
                    TakeOff(_setSpeed,_takeOffAngle,_giveControl);
                    return;
                default:
                    return;
            }

            if (_currentAltitude < _landingAltitudeSafetyCushion)
                EnableDampeners(true);

            if (IsUnderControl())
            {
                ResetGyros();
                _remoteControl?.SetAutoPilotEnabled(false);
                return;
            }


            if (!_combatFlag && _inGravity)
            {
                RotateGrid(true);
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
            _rollOverride = 0;
            _pitchOverride = 0;
            _yawOverride = 0;
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
            _remoteControl.WaitForFreeWay = enableCollision;
            _remoteControl.SetAutoPilotEnabled(true);
            _remoteControl.WaitForFreeWay = true;
        }

        private void Cruise(double speed = 50, double height = 2500, bool giveControl = true)
        {
            giveControl = !_inGravity || giveControl;

            _thrust =  _currentSpeed < speed - 5
                ? Math.Min(_thrust += 0.001f, 1)
                : Math.Max(_thrust -= 0.1f, 0);


            _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentAltitude);

            
            _cruisePitch = _currentAltitude < _cruiseHeight
                ? Math.Min(_cruisePitch += 0.01f, 0.15f)
                : Math.Max(_cruisePitch -= 0.01f, -0.05f);


            if (_inGravity && Math.Abs(_currentAltitude - _cruiseHeight) > 2500 && (!giveControl || !IsUnderControl()))
            {
                var adjustSpeed = Math.Abs(_currentAltitude - height) > 2500 ? 110 : 50;
                if (_currentAltitude > _cruiseHeight + 1000)
                {
                    RotateGrid(giveControl, -1f);
                    return;
                }

                if (_currentAltitude < _cruiseHeight)
                {
                    RotateGrid(giveControl, 1f);
                    return;
                }
            }

            var useThrust = _cruisePitch > 0 || (giveControl && IsUnderControl()) ? _thrust : 0;

            OverrideThrust(true, _remoteControl.WorldMatrix.Forward, useThrust, _currentSpeed, speed, 0.5f);
            RotateGrid(giveControl, _cruisePitch);

        }

        private void EndTrip()
        {
            _autoPilot = Pilot.Disabled;
            _cruiseHeight = 0;
            _setSpeed = 0;



            if (_remoteControl != null)
            {
                _remoteControl.SetAutoPilotEnabled(false);
                _remoteControl.ClearWaypoints();
            }
            ResetGyros();
            CheckThrusters(true);
            EnableDampeners(true);
        }

        private void TakeOff(double takeOffSpeed = 100, double angle = 0, bool giveControl = false)
        {
            if (_remoteControl == null && !TryGetRemote(out _remoteControl))
            {
               EndTrip();
                return;
            }
            _currentSpeed = VectorMath.VectorProjection(_shipVelocityVec, _gravityVec).Length() * Math.Sign(_shipVelocityVec.Dot(-_gravityVec));

            var up = GetUpDirection();
            var maxSpeed =  takeOffSpeed - takeOffSpeed * 0.01;

            if (_currentAltitude <= _landingAltitudeSafetyCushion) _thrust = 1f;
            else
            {
                _thrust =  _currentSpeed < maxSpeed
                    ? Math.Min(_thrust += 0.001f, 1)
                    : Math.Max(_thrust -= 0.1f, 0);

                RotateGrid(giveControl, angle*(Math.PI / 180), 0);

            }
            if (_currentSpeed > _maxSpeed) _maxSpeed = _currentSpeed;

            OverrideThrust(_inGravity, up, _thrust, _currentSpeed,
                _maxSpeed, 0.25f);
            _autoPilot = _inGravity ? Pilot.Takeoff : Pilot.Disabled;
        }

        private Vector3D GetUpDirection()
        {
            if (_remoteControl == null) return Me.WorldMatrix.Up;
            
            var up = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(-_remoteControl.GetNaturalGravity()));

            return up;
        }


        private void Land()
        {
            if (_remoteControl == null && !TryGetRemote(out _remoteControl))
            {
                _autoPilot = Pilot.Disabled;
                return;
            }

            GetBrakingThrusters();

            var brakeAltitudeThreshold = GetBrakingAltitudeThreshold();

            if (_currentAltitude <= brakeAltitudeThreshold)
            {
                EnableDampeners(false);
                if (_downSpeed > DescentSpeed)
                    BrakingOn();
                else
                {  
                    if (_downSpeed < 1)
                    {
                        if (_stationaryTime < 5)
                        {
                            _stationaryTime += 1;
                            return;
                        }
                        _stationaryTime = 0;
                        EndTrip();
                        EnableDampeners(true);
                        return;
                    }

                    BrakingThrust();
                }
            }
            else
            {
                EnableDampeners(true);
                BrakingOff();
            }

        }

        void BrakingThrust()
        {
            double forceSum = 0;
            foreach (IMyThrust thisThrust in _brakingThrusters)
            {
                forceSum += thisThrust.MaxEffectiveThrust;
            }

            var equilibriumThrustPercentage = _shipMass * _gravityMagnitude / forceSum * 100;
            var err = _downSpeed - DescentSpeed;
            double errDerivative = (err - _lastErr) / 0.1;
            if (Math.Abs(_lastErr - 8675309) < 0.1)
                errDerivative = 0;
            var deltaThrustPercentage = Kp * err + Kd * errDerivative;

            _lastErr = err;
            foreach (IMyThrust thisThrust in _brakingThrusters)
            {
                thisThrust.ThrustOverridePercentage = (float)(equilibriumThrustPercentage + deltaThrustPercentage) / 100f;
                thisThrust.Enabled = true;
            }

            foreach (IMyThrust thisThrust in _otherThrusters)
            {
                thisThrust.Enabled = false;
            }
        }

        void BrakingOn()
        {
            foreach (IMyThrust thisThrust in _brakingThrusters)
            {
                thisThrust.Enabled = true;
                thisThrust.ThrustOverridePercentage = 1f;
            }

            foreach (IMyThrust thisThrust in _otherThrusters)
            {
                thisThrust.ThrustOverridePercentage = 0.00001f;
            }
        }

        void BrakingOff()
        {
            foreach (IMyThrust thisThrust in _brakingThrusters)
            {
                thisThrust.Enabled = false;
                thisThrust.ThrustOverridePercentage = 0.00001f;
            }

            foreach (IMyThrust thisThrust in _otherThrusters)
            {
                thisThrust.Enabled = true;
                thisThrust.ThrustOverridePercentage = 0f;
            }
        }



        private double GetBrakingAltitudeThreshold()
        {
            double forceSum = 0;
            foreach (var thruster in _brakingThrusters)
            {
                forceSum += thruster.MaxEffectiveThrust;
            }

            if (Math.Abs(forceSum) < 0.1)
            {
                return 1000d;
            }
            
            double deceleration = (forceSum / _shipMass - _gravityMagnitude) * BurnThrustPercentage;

            double safetyCushion = _maxSpeed * 0.2 * SafetyCushionConstant;

            double distanceToStop = _shipVelocityVec.LengthSquared() / (2 * deceleration) + safetyCushion +
                                    _landingAltitudeSafetyCushion;

            return distanceToStop;
        }

        private void GetBrakingThrusters()
        {
            _brakingThrusters.Clear();
            _otherThrusters.Clear();
            var down = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(_gravityVec));
            foreach (var thruster in _thrusters)
            {
                var thrusterDir = thruster.WorldMatrix.Forward;
                bool sameDir = thrusterDir == down;
                if (sameDir)
                {
                    _brakingThrusters.Add(thruster);
                }
                else
                {
                    _otherThrusters.Add(thruster);
                }
            }
        }

        private void CheckThrusters(bool reset = false)
        {
            if (!_thrusters.Any())
                return;

            foreach (var thruster in _thrusters)
            {
                var maxThrust = thruster.MaxEffectiveThrust / thruster.MaxThrust;
               thruster.Enabled = maxThrust >= 0.35f;
               if (!reset)continue;
               thruster.ThrustOverride = 0;
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
                    if (thruster.WorldMatrix.Forward == direction)
                    {
                        thruster.Enabled = false;
                        continue;
                    }
                    thruster.Enabled = maxThrust >= maxThrustModifier;

                    if (thruster.WorldMatrix.Forward != direction * -1)
                    {
                        thruster.ThrustOverridePercentage = 0;
                        continue;
                    }
                    thruster.Enabled = maxThrust >= maxThrustModifier;
                    thruster.ThrustOverridePercentage = thrustModifier;

                }
                else
                {
                    thruster.Enabled =  maxThrust >= maxThrustModifier;
                    thruster.ThrustOverridePercentage = 0;
                    //thruster.SetValueFloat("Override", 0);
                }
            }
        }

        /// <summary>
        /// Rotates the grid
        /// </summary>
        /// <param name="checkPlayer"></param>
        /// <param name="setPitch"></param>
        /// <param name="setRoll"></param>
        /// <param name="setYaw"></param>
        private void RotateGrid(bool checkPlayer, double setPitch = 0, double setRoll = 0, double setYaw = 0)
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
                ? Math.Min(_pitchDelay += 0.5, 10)
                : Math.Max(_pitchDelay -= 5, 0);

            _rollDelay = _inGravity && Math.Abs(roll - setRoll) >= 1*rad
                ? Math.Min(_rollDelay += 0.5, 10)
                : Math.Max(_rollDelay -= 5, 0);


            _pitchOverride = (float) (_shipMass / (10 *_shipMass));
            _rollOverride = (float) (_shipMass / (10 *_shipMass));
            //var yawOverride = Math.Abs(yaw) > 90*rad ? 0.1f : 0.05f;

            if (Math.Abs(_pitchDelay) < 1 && Math.Abs(_rollDelay) < 1)
            {
                ResetGyros();
                return;
            }

            for (var i = 0; i < Math.Max(1,Math.Round(_gyros.Count*0.15)); i++)
            {
                var gyro = _gyros.Where(g=>!SkipBlock(g)).ToList()[i];
                if (Math.Abs(_pitchDelay) < 1 && Math.Abs(_rollDelay) < 1)
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

                //Roll: Controls grid's roll
                if (_rollDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Forward)
                        gyro.Roll = roll > setRoll ? _rollOverride : -_rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Backward)
                        gyro.Roll = roll > setRoll ? -_rollOverride : _rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Down)
                        gyro.Yaw = roll > setRoll ? _rollOverride : -_rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Up)
                        gyro.Yaw = roll > setRoll ? -_rollOverride : _rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Left)
                        gyro.Pitch = roll > setRoll ? _rollOverride : -_rollOverride;
                    if (_remoteControl.WorldMatrix.Forward == gyro.WorldMatrix.Right)
                        gyro.Pitch = roll > setRoll ? -_rollOverride : _rollOverride;
                    continue;
                }


                //Pitch: Controls the pitch of the grid
                if (_pitchDelay > 1)
                {
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Left)
                        gyro.Pitch = pitch > setPitch ? -_pitchOverride : _pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Right)
                        gyro.Pitch = pitch > setPitch ? _pitchOverride : -_pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Down)
                        gyro.Yaw = pitch > setPitch ? -_pitchOverride : _pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Up)
                        gyro.Yaw = pitch > setPitch ? _pitchOverride : -_pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Forward)
                        gyro.Roll = pitch > setPitch ? -_pitchOverride : _pitchOverride;
                    if (_remoteControl.WorldMatrix.Right == gyro.WorldMatrix.Backward)
                        gyro.Roll = pitch > setPitch ? _pitchOverride : -_pitchOverride;
                    continue;
                }


                //Yaw


            }
        }

        private bool IsUnderControl()
        {
            return _cockpits.Any(cockpit => cockpit.CanControlShip && cockpit.ControlThrusters && cockpit.IsUnderControl);
        }


        #endregion

        #region Block Lists

        private HashSet<IMyDoor> _doors = new HashSet<IMyDoor>();
        private List<IMyTextPanel> _textPanels = new List<IMyTextPanel>();
        private List<IMyProductionBlock> _productionBlocks = new List<IMyProductionBlock>();
        private List<IMyReactor> _reactors = new List<IMyReactor>();
        private readonly List<IMyShipWelder> _shipWelders = new List<IMyShipWelder>();
        private readonly List<IMyShipWelder> _barWelders = new List<IMyShipWelder>();
        private List<IMyAirVent> _airVents = new List<IMyAirVent>() ;
        private List<IMyGasGenerator> _gasGens = new List<IMyGasGenerator>();
        private List<IMyGasTank> _gasTanks = new List<IMyGasTank>();
        private HashSet<IMyGravityGenerator> _gravGens = new HashSet<IMyGravityGenerator>();
        private readonly List<IMyTerminalBlock> _gridBlocks = new List<IMyTerminalBlock>();
        private readonly List<IMyTerminalBlock> _allBlocks = new List<IMyTerminalBlock>();
        private HashSet<IMyThrust> _thrusters = new HashSet<IMyThrust>();
        private readonly HashSet<IMyThrust> _brakingThrusters = new HashSet<IMyThrust>();
        private readonly HashSet<IMyThrust> _otherThrusters = new HashSet<IMyThrust>();
        private List<IMyCargoContainer> _dump = new List<IMyCargoContainer>(10);
        private readonly Queue<IMyCargoContainer> _containers = new Queue<IMyCargoContainer>();
        private HashSet<IMyTerminalBlock> _damagedBlocks = new HashSet<IMyTerminalBlock>();
        private HashSet<IMyPowerProducer> _powerBlocks = new HashSet<IMyPowerProducer>();
        private HashSet<IMyLightingBlock> _lights = new HashSet<IMyLightingBlock>();
        private HashSet<IMySolarPanel> _solars = new HashSet<IMySolarPanel>();
        private HashSet<IMyPowerProducer> _windTurbine = new HashSet<IMyPowerProducer>();
        private List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        private HashSet<IMyRemoteControl> _remotes = new HashSet<IMyRemoteControl>();
        private HashSet<IMyShipConnector> _connectors = new HashSet<IMyShipConnector>();
        private HashSet<IMyShipController> _cockpits= new HashSet<IMyShipController>();
        private List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private HashSet<IMyGyro> _gyros = new HashSet<IMyGyro>();
        private HashSet<IMySoundBlock> _soundBlocks = new HashSet<IMySoundBlock>();
        private HashSet<IMyLandingGear> _landingGears = new HashSet<IMyLandingGear>();
        private readonly Queue<IMyLargeTurretBase> _turretsToCenter = new Queue<IMyLargeTurretBase>();
        private readonly Queue<IMyLargeTurretBase> _emptyProductionQueue = new Queue<IMyLargeTurretBase>();

//dictionary
        private readonly Dictionary<IMyCubeBlock, float> _lowBlocks = new Dictionary<IMyCubeBlock, float>();
        private readonly Dictionary<IMyCubeBlock, DateTime> _collection = new Dictionary<IMyCubeBlock, DateTime>();
        private readonly Dictionary<string, MyItemType> _reactorFuel = new Dictionary<string, MyItemType>();
        private readonly Dictionary<IMyCargoContainer, HashSet<MyInventoryItem>> _cargoDict = new Dictionary<IMyCargoContainer, HashSet<MyInventoryItem>>();
        #endregion

        #region Fields


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
        private bool _lowPower;
        private bool _powerFlag;
        private bool _isOverload;
        private bool _tankRefill;
        private bool _genOnline;
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
        private static string _broadcastTag = "Cerebro";
        private double _setSpeed;
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
        private double _aggression;
        private const int ProjectorShutoffDelay = 30;
        private double _pitchDelay;
        private double _rollDelay;
        private double _yawDelay;
        private float _pitchOverride = 0;
        private float _rollOverride = 0;
        private float _yawOverride = 0;
        private float _thrust;
        //private double _currentAltitude;
        private double _shipHeight;
        private double _currentAltitude;
        private double _currentSpeed;
        private double _maxSpeed = 104.4;

        private double _lastErr = 8675309;
        private const double Kp = 5;
        private const double Kd = 2;
        private double _shipMass;
        private double _gravityMagnitude;
        private double _downSpeed;
        private double _upSpeed;
        private int _stationaryTime;
        private const double DescentSpeed = 3;
        private const double BurnThrustPercentage = 0.80;
        private const double SafetyCushionConstant = 0.5;


//battery life
        private float _batteryHighestCharge = 0.5f;
        private static float _rechargePoint = .15f;
        private IMyBatteryBlock _highestChargedBattery;
        private DateTime _powerFlagDelay;

        private static double _lowFuel = 50;

        private static double _landingAltitudeSafetyCushion = 0;
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

        //Vectors
        private Vector3D _shipVelocityVec = new Vector3D(0,0,0);
        private Vector3D _gravityVec = new Vector3D(0,0,0);

        //Settings
        private const string INI_SECTION_GENERAL = "Cerebro Settings - General";
        private const string INI_GENERAL_MASTER = "Is Main Script";
        private const string INI_GENERAL_HIVE = "Control Drones";
        private const string INI_GENERAL_BROADCASTTAG = "Broadcast Tag";
        private const string INI_GENERAL_SHOWONHUD = "Show Faulty Block On Hud";
        private const string INI_GENERAL_BLOCKSONTERMINAL = "Remove Blocks From Terminal";
        private const string INI_GENERAL_BLOCKSONCONFIGTAB = "Remove Blocks From Config Tab";
        private const string INI_GENERAL_DOORCLOSURE = "Auto Door Closure";
        private const string INI_GENERAL_DOOR = "Door Delay";



        //Navigation
        private const string INI_SECTION_NAVIGATION = "Cerebro Settings - Navigation";
        private const string INI_NAVIGATION_NAVIGATE = "Enable Navigation";
        private const string INI_NAVIGATION_LANDINGALTITUDECUSHION = "Landing Altitude Cushion";
        private const string INI_NAVIGATION_DEFAULTCRUISEHEIGHT = "Safe Cruise Height";



        //Production
        private const string INI_SECTION_PRODUCTION = "Cerebro Settings - Production";
        private const string INI_PRODUCTION_BLOCKSHUTOFFS = "Control Refineries and Assemblers";
        private const string INI_PRODUCTION_VENTS = "Control Vents";
        private const string INI_PRODUCTION_GAS = "Control Gas Production";
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
            _broadcastTag = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_BROADCASTTAG).ToString(_broadcastTag);
            _showOnHud = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_SHOWONHUD).ToBoolean(_showOnHud);
            _removeBlocksInConfigurationTab = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONCONFIGTAB)
                .ToBoolean(_removeBlocksInConfigurationTab);
            _removeBlocksOnTerminal = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONTERMINAL)
                .ToBoolean(_removeBlocksOnTerminal);
            _enableAutoDoor = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_DOORCLOSURE).ToBoolean(_enableAutoDoor);
            _doorDelay = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_DOOR).ToInt32(_doorDelay);



            //Navigation
            _autoNavigate = _ini.Get(INI_SECTION_NAVIGATION, INI_NAVIGATION_NAVIGATE).ToBoolean(_autoNavigate);
            _landingAltitudeSafetyCushion = _ini.Get(INI_SECTION_NAVIGATION, INI_NAVIGATION_LANDINGALTITUDECUSHION)
                .ToDouble(_landingAltitudeSafetyCushion);

            //Production
            _controlProduction = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_BLOCKSHUTOFFS)
                .ToBoolean(_controlProduction);
            _controlVents = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_VENTS).ToBoolean(_controlVents);
            _controlGasSystem = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_GAS).ToBoolean(_controlGasSystem);
            _handleRepair = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_REPAIR).ToBoolean(_handleRepair);
            _welderGroup = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_WELDERS).ToString(_welderGroup);
            _reProj = _ini.Get(INI_SECTION_PRODUCTION, INI_PRODUCTION_PROJECTOR).ToString(_reProj);


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
            _ini.Set(INI_SECTION_GENERAL,INI_GENERAL_BROADCASTTAG,_broadcastTag);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_SHOWONHUD, _showOnHud);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONCONFIGTAB, _removeBlocksInConfigurationTab);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_BLOCKSONTERMINAL, _removeBlocksOnTerminal);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_DOORCLOSURE, _enableAutoDoor);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_DOOR, _doorDelay);


            //Navigation
            _ini.Set(INI_SECTION_NAVIGATION, INI_NAVIGATION_NAVIGATE, _autoNavigate);
            _ini.Set(INI_SECTION_NAVIGATION, INI_NAVIGATION_LANDINGALTITUDECUSHION, _landingAltitudeSafetyCushion);


            //Production
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_BLOCKSHUTOFFS, _controlProduction);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_VENTS, _controlVents);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_GAS, _controlGasSystem);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_REPAIR, _handleRepair);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_WELDERS, _welderGroup);
            _ini.Set(INI_SECTION_PRODUCTION, INI_PRODUCTION_PROJECTOR, _reProj);


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

        #region Script Logging
        public static class Log
        {
            static StringBuilder _builder = new StringBuilder();
            static List<string> _errorList = new List<string>();
            static List<string> _warningList = new List<string>();
            static List<string> _infoList = new List<string>();
            static List<string> _damageList = new List<string>();
            const int _logWidth = 530; //chars, conservative estimate

            public static void Clear()
            {
                _builder.Clear();
                _errorList.Clear();
                _warningList.Clear();
                _infoList.Clear();
                _damageList.Clear();
            }

            public static void Error(string text)
            {
                _errorList.Add(text);
            }

            public static void Warning(string text)
            {
                _warningList.Add(text);
            }

            public static void Info(string text)
            {
                _infoList.Add(text);
            }

            public static void Damage(string text)
            {
                _damageList.Add(text);
            }

            public static string Write(string type = "Info",bool preserveLog = false)
            {

                if (_errorList.Count != 0 && _warningList.Count != 0 && _infoList.Count != 0 && _damageList.Count != 0)
                    WriteLine("");


                switch (type)
                {
                    case "Damage":
                        for (int i = 0; i < _damageList.Count; i++)
                        {
                            WriteElement(i + 1, "Damage", _damageList[i]);
                            //if (i < _warningList.Count - 1)
                        }
                        break;
                    case "Power" :
                        break;
                }

                string output = _builder.ToString();

                if (!preserveLog)
                    Clear();

                return output;
            }

            public static string Write(bool preserveLog = false)
            {
                if (_errorList.Count != 0 && _warningList.Count != 0 && _infoList.Count != 0 && _damageList.Count != 0)
                    WriteLine("");

                if (_errorList.Count != 0)
                {
                    for (int i = 0; i < _errorList.Count; i++)
                    {
                        WriteElement(i + 1, "ERROR", _errorList[i]);
                        //if (i < _errorList.Count - 1)
                    }
                }

                if (_warningList.Count != 0)
                {
                    for (int i = 0; i < _warningList.Count; i++)
                    {
                        WriteElement(i + 1, "WARNING", _warningList[i]);
                        //if (i < _warningList.Count - 1)
                    }
                }

                if (_damageList.Count != 0)
                {
                    for (int i = 0; i < _damageList.Count; i++)
                    {
                        WriteElement(i + 1, "Damage", _damageList[i]);
                        //if (i < _warningList.Count - 1)
                    }
                }

                if (_infoList.Count != 0)
                {
                    for (int i = 0; i < _infoList.Count; i++)
                    {
                        WriteElement(i + 1, "Info", _infoList[i]);
                        //if (i < _infoList.Count - 1)
                    }
                }

                string output = _builder.ToString();

                if (!preserveLog)
                    Clear();

                return output;
            }

            private static void WriteElement(int index, string header, string content)
            {
                WriteLine($"{header} {index}:");

                string wrappedContent = TextHelper.WrapText(content, 1, _logWidth);
                string[] wrappedSplit = wrappedContent.Split('\n');

                foreach (var line in wrappedSplit)
                {
                    _builder.Append("  ").Append(line).Append('\n');
                }
            }

            private static void WriteLine(string text)
            {
                _builder.Append(text).Append('\n');
            }
        }

        // Whip's TextHelper Class v2
        public class TextHelper
        {
            static StringBuilder textSB = new StringBuilder();
            const float adjustedPixelWidth = (512f / 0.778378367f);
            const int monospaceCharWidth = 24 + 1; //accounting for spacer
            const int spaceWidth = 8;

            #region bigass dictionary
            static Dictionary<char, int> _charWidths = new Dictionary<char, int>()
            {
            {'.', 9},
            {'!', 8},
            {'?', 18},
            {',', 9},
            {':', 9},
            {';', 9},
            {'"', 10},
            {'\'', 6},
            {'+', 18},
            {'-', 10},

            {'(', 9},
            {')', 9},
            {'[', 9},
            {']', 9},
            {'{', 9},
            {'}', 9},

            {'\\', 12},
            {'/', 14},
            {'_', 15},
            {'|', 6},

            {'~', 18},
            {'<', 18},
            {'>', 18},
            {'=', 18},

            {'0', 19},
            {'1', 9},
            {'2', 19},
            {'3', 17},
            {'4', 19},
            {'5', 19},
            {'6', 19},
            {'7', 16},
            {'8', 19},
            {'9', 19},

            {'A', 21},
            {'B', 21},
            {'C', 19},
            {'D', 21},
            {'E', 18},
            {'F', 17},
            {'G', 20},
            {'H', 20},
            {'I', 8},
            {'J', 16},
            {'K', 17},
            {'L', 15},
            {'M', 26},
            {'N', 21},
            {'O', 21},
            {'P', 20},
            {'Q', 21},
            {'R', 21},
            {'S', 21},
            {'T', 17},
            {'U', 20},
            {'V', 20},
            {'W', 31},
            {'X', 19},
            {'Y', 20},
            {'Z', 19},

            {'a', 17},
            {'b', 17},
            {'c', 16},
            {'d', 17},
            {'e', 17},
            {'f', 9},
            {'g', 17},
            {'h', 17},
            {'i', 8},
            {'j', 8},
            {'k', 17},
            {'l', 8},
            {'m', 27},
            {'n', 17},
            {'o', 17},
            {'p', 17},
            {'q', 17},
            {'r', 10},
            {'s', 17},
            {'t', 9},
            {'u', 17},
            {'v', 15},
            {'w', 27},
            {'x', 15},
            {'y', 17},
            {'z', 16}
            };
            #endregion

            public static int GetWordWidth(string word)
            {
                int wordWidth = 0;
                foreach (char c in word)
                {
                    int thisWidth = 0;
                    bool contains = _charWidths.TryGetValue(c, out thisWidth);
                    if (!contains)
                        thisWidth = monospaceCharWidth; //conservative estimate

                    wordWidth += (thisWidth + 1);
                }
                return wordWidth;
            }

            public static string WrapText(string text, float fontSize, float pixelWidth = adjustedPixelWidth)
            {
                textSB.Clear();
                var words = text.Split(' ');
                var screenWidth = (pixelWidth / fontSize);
                int currentLineWidth = 0;
                foreach (var word in words)
                {
                    if (currentLineWidth == 0)
                    {
                        textSB.Append($"{word}");
                        currentLineWidth += GetWordWidth(word);
                        continue;
                    }

                    currentLineWidth += spaceWidth + GetWordWidth(word);
                    if (currentLineWidth > screenWidth) //new line
                    {
                        currentLineWidth = GetWordWidth(word);
                        textSB.Append($"\n{word}");
                    }
                    else
                    {
                        textSB.Append($" {word}");
                    }
                }

                return textSB.ToString();
            }
        }
        #endregion

public class Screen
{
    public string GetStatus()
    {
        var sb = new StringBuilder();

        return sb.ToString();
    }

    public string GetPower()
    {
        var sb = new StringBuilder();

        return sb.ToString();
    }

    public string GetDamages()
    {
        var sb = new StringBuilder();

        return sb.ToString();
    }

    public string GetCombatInfo()
    {
        var sb = new StringBuilder();

        return sb.ToString();
    }

}
    }
}