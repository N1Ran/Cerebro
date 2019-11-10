using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using VRageRender.Messages;

namespace IngameScript
{
    internal class Program : MyGridProgram
    {
        private Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _runtimeTracker = new RuntimeTracker(this);
            ScriptInitiate();

            Setup();
            _scheduledSetup = new ScheduledAction(Setup, 0.1);

            _scheduler = new Scheduler(this);

           SetSchedule();
        }


        private void Main(string arg)
        {
            CurrentState(arg, out _currentMode);
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
            _runtimeTracker.AddRuntime();
            _scheduler.Update();
            _runtimeTracker.AddInstructions();
            ProgramMaintenance();
            Storage = _currentMode.ToString();
            Me.GetSurface(0).WriteText(_runtimeTracker.Write());
            Screens();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        private void ProgramMaintenance()
        {
            _isStatic = Me.CubeGrid.IsStatic;

            _alertCounter = _alertCounter < 10 ? _alertCounter += 1 : 0;

            if (!_hasAntenna && _hive) GrabNewAntenna();
            if (_remoteControl == null && !_isStatic && _autoNavigate) GrabNewRemote();
            _navFlag = _isStatic && _autoNavigate && _remoteControl == null;

            if (IsDocked() && _currentMode != ProgramState.Recharge) _currentMode = ProgramState.Docked;

            switch (_currentMode)
            {
                case ProgramState.ShutOff:
                    Echo("Powered off");
                    PowerDown();
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

        private void SetSchedule()
        {
                        //Scheduled actions
           _scheduler.AddScheduledAction(_scheduledSetup);
           _scheduler.AddScheduledAction(CheckConnectors,1);
            //Queued actions
            _scheduler.AddScheduledAction(AggroBuilder,0.1);
            _scheduler.AddScheduledAction(CheckNavigation,1);
            _scheduler.AddScheduledAction(GridFlags, 0.1);
            _scheduler.AddScheduledAction(CheckProjection,0.01);
            _scheduler.AddScheduledAction(()=>SwitchToggle(_solars),0.001);
            _scheduler.AddScheduledAction(GetBlocks,1f/150f);
            _scheduler.AddScheduledAction(FindFuel, 1f/600f);
            _scheduler.AddScheduledAction(AutoDoors,1);
            _scheduler.AddScheduledAction(ResetTurrets,1);
            _scheduler.AddScheduledAction(()=>DisplayData(_menuPointer),0.1);
            _scheduler.AddScheduledAction(Alerts,0.01);
            _scheduler.AddScheduledAction(AlertLights,1);


            const float step = 1f / 5f;
            const float twoStep = 1f / 10f;
            _scheduler.AddQueuedAction(() => UpdateProduction(0 * step, 1 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateProduction(1 * step, 2 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateProduction(2 * step, 3 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateProduction(3 * step, 4 * step), Tick); 
            _scheduler.AddQueuedAction(() => UpdateProduction(4 * step, 5 * step),Tick); 

            _scheduler.AddQueuedAction(() => UpdateTanks(0 * step, 1 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateTanks(1 * step, 2 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateTanks(2 * step, 3 * step), Tick); 
            _scheduler.AddQueuedAction(() => UpdateTanks(3 * step, 4 * step), Tick);
            _scheduler.AddQueuedAction(() => UpdateTanks(4 * step, 5 * step), Tick); 

           _scheduler.AddQueuedAction(() => SwitchToggle(_gasGens,_tankRefill), Tick);

           _scheduler.AddQueuedAction(() => UpdateVents(0 * (twoStep), 1 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(1 * (twoStep), 2 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(2 * (twoStep), 3 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(3 * (twoStep), 4 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(4 * (twoStep), 5 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(5 * (twoStep), 6 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(6 * (twoStep), 7 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(7 * (twoStep), 8 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(8 * (twoStep), 9 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateVents(9 * (twoStep), 10 * (twoStep)), Tick);
      
           _scheduler.AddQueuedAction(() => UpdateBatteries(0 * (twoStep), 1 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(1 * (twoStep), 2 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(2 * (twoStep), 3 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(3 * (twoStep), 4 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(4 * (twoStep), 5 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(5 * (twoStep), 6 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(6 * (twoStep), 7 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(7 * (twoStep), 8 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(8 * (twoStep), 9 * (twoStep)), Tick);
           _scheduler.AddQueuedAction(() => UpdateBatteries(9 * (twoStep), 10 * (twoStep)), Tick);

           _scheduler.AddQueuedAction(() => UpdateReactors(0 * step, 1 * step), Tick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(1 * step, 2 * step), Tick); 
           _scheduler.AddQueuedAction(() => UpdateReactors(2 * step, 3 * step), Tick);
           _scheduler.AddQueuedAction(() => UpdateReactors(3 * step, 4 * step), Tick);
           _scheduler.AddQueuedAction(() => UpdateReactors(4 * step, 5 * step), Tick);

           _scheduler.AddQueuedAction(() => UpdateFuelTypes(0 * step, 1 * step), Tick); 
           _scheduler.AddQueuedAction(() => UpdateFuelTypes(1 * step, 2 * step), Tick); 
           _scheduler.AddQueuedAction(() => UpdateFuelTypes(2 * step, 3 * step), Tick);
           _scheduler.AddQueuedAction(() => UpdateFuelTypes(3 * step, 4 * step), Tick);
           _scheduler.AddQueuedAction(() => UpdateFuelTypes(4 * step, 5 * step), Tick);



            _scheduler.AddQueuedAction(() => ManageTurrets(0 * 1f / 2f, 1 * 1f / 2f), Tick); 
            _scheduler.AddQueuedAction(() => ManageTurrets(1 * 1f / 2f, 2 * 1f / 2f), Tick); 

           _scheduler.AddQueuedAction(() => CapFuel(0 * (twoStep), 1 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(1 * (twoStep), 2 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(2 * (twoStep), 3 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(3 * (twoStep), 4 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(4 * (twoStep), 5 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(5 * (twoStep), 6 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(6 * (twoStep), 7 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(7 * (twoStep), 8 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(8 * (twoStep), 9 * (twoStep)), Tick);
            _scheduler.AddQueuedAction(() => CapFuel(9 * (twoStep), 10 * (twoStep)), Tick);

        }

        private void SetRechargeSchedule()
        {
            _runtimeTracker.Reset();
            _scheduler.Reset();
            _setRechargeState = true;

            _scheduler.AddScheduledAction(GetBlocks,1f/600f);
            _scheduler.AddScheduledAction(AutoDoors,1);
            const float step = 1f / 10f;

            _scheduler.AddQueuedAction(() => UpdateVents(0 * step, 1 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(1 * step, 2 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(2 * step, 3 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(3 * step, 4 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateVents(4 * step, 5 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(5 * step, 6 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(6 * step, 7 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(7 * step, 8 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateVents(8 * step, 9 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateVents(9 * step, 10 * step), 100*Tick); 

            _scheduler.AddQueuedAction(() => UpdateBatteries(0 * step, 1 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(1 * step, 2 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(2 * step, 3 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(3 * step, 4 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(4 * step, 5 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(5 * step, 6 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(6 * step, 7 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(7 * step, 8 * step), 100*Tick); 
            _scheduler.AddQueuedAction(() => UpdateBatteries(8 * step, 9 * step), 100*Tick);
            _scheduler.AddQueuedAction(() => UpdateBatteries(9 * step, 10 * step), 100*Tick);


        }

        private void Screens()
        {
            Echo(_runtimeTracker.Write());
            _status.Clear();
            _status.Append("AI Running ");
            WriteToScreens(_sb);
            WriteToScreens(_sbPower, "power");
        }

        private void LockLandingGears(bool b = true)
        {
            if (_landingGears?.Any() == false || _landingGears== null) return;
            foreach (var landingGear in _landingGears.Where(landingGear => landingGear.IsLocked))
            {
                landingGear.AutoLock = false;
                switch (b)
                {
                    case true:
                        if (landingGear.LockMode != LandingGearMode.ReadyToLock) continue;
                        landingGear.Lock();
                        continue;
                    case false:
                        if (landingGear.LockMode != LandingGearMode.Locked) continue;
                        landingGear.Unlock();
                        continue;
                }
            }
        }

        private void PowerOn()
        {
            GetBlocks();
            foreach (var funcBlock in _gridBlocks.OfType<IMyFunctionalBlock>().Where(funcBlock =>
                !funcBlock.Enabled && !(funcBlock is IMyShipWelder
                                        || funcBlock is IMyShipGrinder || funcBlock is IMyShipDrill ||
                                        StringContains(funcBlock.BlockDefinition.TypeIdString, "hydrogenengine") || funcBlock is IMyLightingBlock)))
            {
                funcBlock.Enabled = true;
                var tank = funcBlock as IMyGasTank;
                if (tank != null) tank.Stockpile = false;
            }

            var light = _lights.Where(x => !StringContains(x.CustomName, "spot"));
            SwitchToggle(light);
            _currentMode = ProgramState.Normal;
            _autoPilot = Pilot.Disabled;

            _scheduler.Reset();

            SetSchedule();

        }


        private void PowerDown()
        {
            if (!IsDocked() && _currentSpeed > 1 || _inGravity && _currentHeight > 20 && !LandingLocked())
            {
                Echo("Vehicle cannot be switched off while in motion");
                _autoPilot = Pilot.Disabled;
                DampenersOnline(true);
                return;
            }

            foreach (var funcBlock in _gridBlocks.OfType<IMyFunctionalBlock>().Where(funcBlock => funcBlock != Me))
            {
                if (funcBlock is IMyBatteryBlock || funcBlock is IMySolarPanel || funcBlock is IMyLandingGear ||
                    funcBlock is IMyGasTank || funcBlock is IMyMedicalRoom || funcBlock is IMyDoor ||
                    StringContains(funcBlock.BlockDefinition.TypeIdString, "wind") ||
                    funcBlock is IMyShipConnector)
                {
                    funcBlock.Enabled = true;
                    var block = funcBlock as IMyBatteryBlock;
                    if (block != null) block.ChargeMode = ChargeMode.Auto;
                    var tank = funcBlock as IMyGasTank;
                    if (tank != null) tank.Stockpile = true;
                    continue;
                }

                var drive = funcBlock as IMyJumpDrive;
                if (drive != null && _currentMode == ProgramState.Docked)
                {
                    drive.Enabled = true;
                    continue;
                }


                funcBlock.Enabled = false;
            }

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


        private static void SwitchToggle(IEnumerable<IMyCubeBlock> groupBlocks, bool on = true)
        {
            var myCubeBlocks = groupBlocks as IMyCubeBlock[] ?? groupBlocks.ToArray();
            if (myCubeBlocks?.Any() == false)return;
            foreach (var block in myCubeBlocks.OfType<IMyFunctionalBlock>().Where(block=>!Closed(block))) block.Enabled = on;
        }

        private void GrabNewRemote()
        {
            if (_remotes?.Any() == false || _remotes == null) return;
            foreach (var remote in _remotes)
            {
                _remoteControl = remote;
                return;
            }
        }

        private void GrabNewAntenna()
        {
            if (_myAntenna != null) return;
            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType(antennas);
            if (antennas?.Any() == false)
            {
                _hasAntenna = false;
                return;
            }

            foreach (var antenna in antennas)
            {
                _myAntenna = antenna;
                _hasAntenna = true;
                break;
            }
        }

        private void ScriptInitiate()

        {
            _scriptInitialize = true;
            _aggression = _defaultAggression;
            if (!Enum.TryParse(Storage, out _currentMode))
                _currentMode = ProgramState.PowerOn;
            GetBlocks();
        }

        private void Setup()
        {
            ParseIni();
            if (!_scriptInitialize) ScriptInitiate();
            var x = NeedsRepair();
            if (_handleRepair)SwitchToggle(_shipWelders, x || _alert > AlertState.High);

            if (!_powerManagement)return;
            if (_fuelCount?.Values.Sum() < _lowFuel && BatteryLevel() < _rechargePoint)
                _powerFlagDelay = DateTime.Now;
            _powerFlag = (DateTime.Now - _powerFlagDelay).TotalSeconds < 5;
            if (_gravGens?.Any() == true) SwitchToggle(_gravGens, !_powerFlag);
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
                    break;
                }

                if (StringContains(t[0], "takeoff"))
                {
                    if (!_autoNavigate || _remoteControl == null)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    DampenersOnline(true);
                    _cruiseSpeed = t.Length >= 2 && double.TryParse(t[1], out _cruiseSpeed) ? _cruiseSpeed:100;
                    _takeOffAngle = t.Length >= 3 && double.TryParse(t[2], out _takeOffAngle) ? _takeOffAngle:0;
                    _giveControl = t.Length >= 4 && bool.Parse(t[3]);
                    if (_remoteControl != null) _autoPilot = Pilot.Takeoff;
                }

                if (t[0].Equals("dock",StringComparison.OrdinalIgnoreCase))
                    if (IsConnected())
                    {
                        _currentMode = ProgramState.Docked;
                        break;
                    }


                if (StringContains(t[0], "cancel"))
                    switch (t[1].ToLower())
                    {
                        case "land":
                            EndTrip();
                            break;
                        case "takeoff":
                            EndTrip();
                            break;
                        case "cruise":
                            EndTrip();
                            break;
                        case "trip":
                            _autoPilot = Pilot.Disabled;
                            foreach (var remote in _remotes.Where(remote => !Closed(remote)))
                                remote.SetAutoPilotEnabled(false);
                            EndTrip();
                            break;
                        default:
                            EndTrip();
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

                if (StringContains(t[0], "cruise"))
                {
                    if (!_autoNavigate)
                    {
                        Echo("Navigation is not enabled");
                        break;
                    }

                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    _thrust = 0;
                    _autoPilot = Pilot.Cruise;
                    DampenersOnline(true);
                    _cruiseDirection = t.Length >= 2 ? t[1].ToLower():"forward";
                    _cruiseHeight = _inGravity && t.Length >= 4 ? double.Parse(t[3]) : _currentHeight;
                    _cruiseSpeed = t.Length >= 3 ? double.Parse(t[2]): _currentSpeed;
                    _giveControl = t.Length >= 5 && bool.Parse(t[4]);
                    break;
                }

                if (t[0].Equals("poweroff", StringComparison.OrdinalIgnoreCase))
                {
                    _currentMode = ProgramState.ShutOff;
                    break;
                }


                if (t[0].Equals("poweron", StringComparison.OrdinalIgnoreCase))
                {
                    _currentMode = ProgramState.PowerOn;
                    break;
                }

                if (StringContains(t[0], "cyclemenu+"))
                    if (_menuPointer < 3)
                        _menuPointer += 1;
                if (StringContains(t[0], "cyclemenu-"))
                    if (_menuPointer > 0)
                        _menuPointer -= 1;
                break;
            }

            result = _currentMode;
        }


        private void GridFlags()
        {
            if (!_productionFlag && !_powerFlag && !_navFlag && !_damageDetected)
            {
                _alert = AlertState.Clear;
                return;
            }

            if (_navFlag && _productionFlag && _alert < AlertState.Guarded)
            {
                _alert = AlertState.Guarded;
            }

            if (_powerFlag && _alert < AlertState.Elevated)
            {
                _alert = AlertState.Elevated;
            }

            if (_damageDetected && _alert < AlertState.High)
            {
                _alert = AlertState.High;
            }

            if (_combatFlag && _alert < AlertState.Severe)
            {
                _alert = AlertState.Severe;
            }
            //return _productionFlag || _powerFlag || _navFlag || _damageDetected;
        }


        private void DisplayData(int pointer)
        {
            while (true)
            {
                switch (pointer)
                {
                    case 0:
                        Alerts();
                        break;

                    case 1:
                        GridErrors();
                        break;

                    case 2:
                        GetPower();
                        break;
                    default:
                        return;
                }

                break;
            }
        }


        /// <summary>
        /// writes to screens
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="lcd"></param>
        private void WriteToScreens(StringBuilder sb, string lcd = null)
        {
            foreach (var textPanel in _textPanels.Where(textPanel =>
                !SkipBlock(textPanel) && StringContains(textPanel.CustomName, "cerebro")))
            {
                textPanel.ContentType = ContentType.TEXT_AND_IMAGE;
                textPanel.Enabled = true;
                if (string.IsNullOrEmpty(lcd))
                {
                    textPanel.WriteText(sb);
                    continue;
                }

                if (!StringContains(textPanel.CustomName, lcd)) continue;
                textPanel.WriteText(sb);
            }

        }


        /// <summary>
        /// prints alerts
        /// </summary>
        private void Alerts()
        {
            _sb.Clear();
            _sbPower.Clear();
            _sb.Append("AI Running ");
            switch (_alertCounter % 6)
            {
                case 0:
                    _sb.Append("--");
                    break;
                case 1:
                    _sb.Append("\\");
                    break;
                case 2:
                    _sb.Append(" | ");
                    break;
                case 3:
                    _sb.Append("/");
                    break;
            }

            _sb.AppendLine();
            _sb.Append($"Hive Status: {_hive}");
            _sb.AppendLine();

            _sbPower.Append($"PowerFlag = {_powerFlag}");
            _sbPower.AppendLine();
            float j;
            _sbPower.Append($"PowerOverload = {IsOverload(out j)}");
            _sbPower.AppendLine();
            _sbPower.Append($"PowerUsage = {Math.Round(j * 100)}%");
            _sbPower.AppendLine();

            if (_lowBatteries != null && _lowBatteries?.Any() == true)
            {
                double lowBatteriesCount = _lowBatteries.Keys.Count;
                double bat = _batteries.Count();
                _sbPower.Append($"{Math.Round(lowBatteriesCount / bat * 100)}% of batteries in recharge");
                _sbPower.AppendLine();
            }


            if (_alert > AlertState.Clear)
            {
                _sb.Append("Warning: Grid Error Detected!");
                _sb.AppendLine();

                if (_powerFlag)
                {
                    _sb.Append(" Power status warning!");
                    _sb.AppendLine();
                }
            }

            if (_lowBatteries?.Keys != null)
                foreach (var battery in _lowBatteries.Keys)
                {
                    _sbPower.Append(
                        $"  {battery.CustomName} {battery.ChargeMode} - {Math.Round(battery.CurrentStoredPower / battery.MaxStoredPower * 100)}%");
                    _sbPower.AppendLine();
                }

            if (_myProjector != null && _myProjector.IsProjecting)
            {
                _sb.Append($"{_myProjector.CustomName} is active for repairs");
                _sb.AppendLine();
            }

            if (_combatFlag)
            {
                _sb.Append("Engaging Hostiles!");
                _sb.AppendLine();
                _sb.Append(" Aggression Level: " + _aggression);
                _sb.AppendLine();
            }



        }


        /// <summary>
        /// prints grid power
        /// </summary>
        private void GetPower()
        {
            var batteryPower = _batteries.Where(x => !SkipBlock(x))
                .Aggregate<IMyBatteryBlock, double>(0, (current, x) => current + x.CurrentOutput);

            var reactorPower = _reactors.Where(x => !SkipBlock(x))
                .Aggregate<IMyReactor, double>(0, (current, x) => current + x.CurrentOutput);

            var solarPower = _solars.Where(x => !SkipBlock(x))
                .Aggregate<IMySolarPanel, double>(0, (current, x) => current + x.CurrentOutput);

            var power = batteryPower + reactorPower + solarPower;
            _sb.Clear();
            _sb.Append(" Current Power Usage: " + power.ToString("F2") + " MW");
            _sb.AppendLine();
            _sb.Append(" Batteries: " + batteryPower.ToString("F2") + " MW");
            _sb.AppendLine();
            _sb.Append(" SolarPanels: " + solarPower.ToString("F2") + " MW");
            _sb.AppendLine();
            _sb.Append("Reactors: " + reactorPower.ToString("F2") + " MW");
            _sb.AppendLine();
        }


        /// <summary>
        /// prints grid errors
        /// </summary>
        private void GridErrors()
        {
            _sb.Clear();
            _sb.Append(" Cerebro: Current Grid issues");
            _sb.AppendLine();

            if (_powerFlag)
            {
                _sb.Append(" Warning: Reactor Fuel Low");
                _sb.AppendLine();
                _sb.Append(" Essential Systems Only!");
                _sb.AppendLine();
            }

            if (_productionFlag)
            {
                _sb.Append(" Warning: An industrial block fault was detected");
                _sb.AppendLine();
            }

            if (_navFlag)
            {
                _sb.Append(" Warning: A Navigation fault was detected");
                _sb.AppendLine();
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
                SetLight(light, color, _alert > 0);

            foreach (var soundBlock in _soundBlocks.Where(soundBlock =>
                !Closed(soundBlock) && StringContains(soundBlock.CustomName, "alarm")))
            {
                DateTime time;
                if (!_collection.TryGetValue(soundBlock, out time))
                {
                    if (_alert != AlertState.Severe)continue;
                    soundBlock.Enabled = true;
                    _collection.Add(soundBlock, DateTime.Now);
                    soundBlock.Play();
                    soundBlock.LoopPeriod = 5f;
                    continue;
                }

                if ((DateTime.Now - time).TotalSeconds < 5) continue;
                if (_alert != AlertState.Severe)
                {
                    soundBlock.Stop();
                    _collection.Remove(soundBlock);
                    continue;
                }

                soundBlock.Play();

            }


        }

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
            if (!_capReactors || _reactors?.Any() == false || _reactors == null) return;
            var reactors = _reactors?.Where(reactor => !Closed(reactor)).ToArray();
            if (!reactors.Any())return;
            var start = (int) (startProportion * reactors.Count());
            var end = (int) (endProportion * reactors.Count());
            for (var i = start; i < end; ++i)
            {
                var reactor = reactors[i];
                MyItemType fuel;
                if (!_reactorFuel.TryGetValue(reactor.BlockDefinition.SubtypeId, out fuel))
                {
                    reactor.UseConveyorSystem = true;
                    continue;
                }

                reactor.UseConveyorSystem = false;
                double count;
                if (_fuelCount == null || !_fuelCount.TryGetValue(fuel, out count)) continue;
                var lowCap = count / reactors.Count() < _lowFuel
                    ? (MyFixedPoint) (count / reactors.Count())
                    : (int) _lowFuel;
                var reactorFuel = reactor.GetInventory().GetItemAmount(fuel);
                if (Math.Abs((double) (reactorFuel - lowCap)) <= 0.1*(double)lowCap) continue;
                var y = (MyFixedPoint) Math.Abs((double) (reactorFuel - lowCap));

                var transferBlocks = _gridBlocks?.Where(block =>
                        block.HasInventory && block.GetInventory().IsConnectedTo(reactor.GetInventory()))
                    .ToArray();
                var transferBlock = reactorFuel > lowCap
                    ? transferBlocks?.OfType<IMyCargoContainer>()?.Where(block =>
                        !block.GetInventory().IsFull &&
                        reactor.GetInventory().CanTransferItemTo(block.GetInventory(), fuel)).FirstOrDefault()
                    : transferBlocks
                        ?.Where(block => block.GetInventory().CanTransferItemTo(reactor.GetInventory(), fuel) && block.GetInventory().ContainItems(y,fuel))
                        .FirstOrDefault();

                if (transferBlock == null)continue;


                MyInventoryItem? z;
                if (reactorFuel > lowCap)
                {
                    z = reactor.GetInventory().FindItem(fuel);
                    if(z == null)continue;
                    reactor.GetInventory().TransferItemTo(transferBlock.GetInventory(), z.Value, y);
                    continue;
                }

                z = transferBlock.GetInventory().FindItem(fuel);
                if (z== null)continue;
                transferBlock.GetInventory().TransferItemTo(reactor.GetInventory(), z.Value, y);
            }
        }

        private void FindFuel()
        {
            _fuelCount.Clear();
            var newDictionary = new Dictionary<MyItemType, double>();
            var usedFuel = _reactorFuel.Select(x=>x.Value).ToArray();

            if (!usedFuel.Any())return;

            foreach (var item in usedFuel)
            {
                MyFixedPoint count = 0;

                count = _gridBlocks
                    .Where(block =>
                        !Closed(block) && block.HasInventory && block.GetInventory().ItemCount != 0 &&
                        block.GetInventory().ContainItems(1, item)).Aggregate(count,
                        (current, block) => current + block.GetInventory().GetItemAmount(item));

                newDictionary.Add(item, (double) count);
            }

            _fuelCount = newDictionary;
        }



        /// <summary>
        /// Circles through the batteries to maintain charge and power
        /// </summary>
        /// <param name="startProportion"></param>
        /// <param name="endProportion"></param>
        private void UpdateBatteries(float startProportion, float endProportion)
        {
            if (_batteries?.Any() == false || !_powerManagement ||_batteries ==null) return;
            var rechargeAim = Math.Min((_rechargePoint + 0.1f), 1f);
            var highestCharge = _highestChargedBattery?.CurrentStoredPower / _highestChargedBattery?.MaxStoredPower ??
                                0f;
            
            var start = (int) (startProportion * _batteries.Count());
            var end = (int) (endProportion * _batteries.Count());


            for (var i = start; i < end; ++i)
            {
                var battery = _batteries.ToList()[i];
                if (SkipBlock(battery) || !battery.IsFunctional)
                {
                    _lowBatteries.Remove(battery);
                    continue;
                }

                battery.Enabled = true;

                float charge;

                if (!_lowBatteries.TryGetValue(battery, out charge))
                {
                    if (_highestChargedBattery == null && battery.HasCapacityRemaining)
                    {
                        _highestChargedBattery = battery;
                        continue;
                    }
                    if (_currentMode == ProgramState.Recharge  && battery != _highestChargedBattery)
                    {
                        _lowBatteries.Add(battery, 1f);
                        battery.ChargeMode = ChargeMode.Recharge;
                        continue;
                    }

                    if (!battery.HasCapacityRemaining||battery.CurrentStoredPower / battery.MaxStoredPower < _rechargePoint )
                    {
                        _lowBatteries.Add(battery, Math.Min(_rechargePoint * 2, rechargeAim));
                        continue;
                    }

                    battery.ChargeMode =
                        _isStatic && !StringContains(battery.CustomName, "backup") && battery != _highestChargedBattery
                            ? ChargeMode.Discharge
                            : ChargeMode.Auto;


                }

                if (battery.CurrentStoredPower / battery.MaxStoredPower >= highestCharge || _highestChargedBattery == null)
                {
                    _highestChargedBattery = battery;
                    highestCharge = battery.CurrentStoredPower / battery.MaxStoredPower;
                    _lowBatteries.Remove(battery);
                    battery.ChargeMode = ChargeMode.Auto;
                    continue;
                }
                battery.ChargeMode = ChargeMode.Recharge;
                if (_currentMode == ProgramState.Recharge)continue;
                if ((double) (battery.CurrentStoredPower / battery.MaxStoredPower) >= charge)
                {
                    battery.ChargeMode = ChargeMode.Auto;
                    _lowBatteries.Remove(battery);
                    continue;
                }

                if (_alert != AlertState.Severe && (!_autoNavigate || _autoPilot <= Pilot.Disabled) &&
                    (!_inGravity || !(_currentSpeed >= 10))) continue;
                battery.ChargeMode = ChargeMode.Auto;
                _lowBatteries.Remove(battery);

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
            var start = (int) (startProportion * reactors.Count());
            var end = (int) (endProportion * reactors.Count());
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
                if (block is IMyBatteryBlock && (((IMyBatteryBlock) block).IsCharging ||
                                                 !((IMyBatteryBlock) block).HasCapacityRemaining)) continue;
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


//---------------industrial----------------------

//check airvents
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
                    _sb.Append(vent.CustomName + " Can't Pressurize");
                    _sb.AppendLine();
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

//check for damaged blocks
        private bool NeedsRepair()
        {
            if (!_gridBlocks.Any()) return false;
            _damagedBlocks = _gridBlocks?.Where(block =>
                !Closed(block) && (block.IsBeingHacked || block.CubeGrid.GetCubeBlock(block.Position).CurrentDamage > 0));

            var damagedBlocks = _damagedBlocks as IMyTerminalBlock[] ?? _damagedBlocks.ToArray();
            if (!damagedBlocks.Any())
            {
                _damageDetected = false;
                return false;
            }
            _damageDetected = true;
            foreach (var block in damagedBlocks)
            {
                if (_showOnHud)block.ShowOnHUD = true;
                _sb.Append(block.CustomName + " is damaged and needs repair!");
                _sb.AppendLine();
            }

            return true;
        }


//check production
        private void UpdateProduction(float startProportion, float endProportion)
        {
            if (_productionBlocks?.Any() == false || !_controlProduction || _productionBlocks == null) return;
            var prodBlocks = _productionBlocks.Where(block => !Closed(block)).ToList();
            var start = (int) (startProportion * prodBlocks.Count);
            var end = (int) (endProportion * prodBlocks.Count);
            for (var i = start; i < end; ++i)
            {
                var block = prodBlocks[i];

                if (!block.IsQueueEmpty) block.Enabled = true;

                DateTime time;
                if (_collection.TryGetValue(block, out time))
                {
                    if (block.IsProducing || !block.IsQueueEmpty)
                    {
                        _collection.Remove(block);
                        _collection.Add(block, DateTime.Now);
                        continue;
                    }

                    if ((DateTime.Now - time).TotalSeconds < ProductionDelay) continue;
                    block.Enabled = false;
                    _collection.Remove(block);
                }

                if (!block.Enabled) continue;
                _collection.Add(block, DateTime.Now);
            }
        }


//check gas tanks
        private void UpdateTanks(float startProportion, float endProportion)
        {
            if (_gasTanks?.Any() == false || !_controlGasSystem) return;
            var gasTanks = _gasTanks?.Where(tank => tank.Enabled && !SkipBlock(tank)).ToList();
            if (gasTanks == null) return;

            var start = (int) (startProportion * gasTanks.Count);
            var end = (int) (endProportion * gasTanks.Count);

            var lowTanks = 0;
            for (var i = start; i < end; ++i)
            {
                var tank = gasTanks[i];

                if (_rechargeWhenConnected && tank.FilledRatio < 1f && IsDocked())
                {
                    tank.Stockpile = true;
                    lowTanks += 1;
                    continue;
                }

                if (tank.FilledRatio >= _tankFillLevel)
                {
                    tank.Stockpile = false;
                    continue;
                }

                tank.Stockpile = _isStatic;
                lowTanks += 1;
                tank.Enabled = true;
            }

            _tankRefill = lowTanks > 0;
        }




//close open doors
        private void AutoDoors()
        {
            if (!_enableAutoDoor) return;
            foreach (var door in _doors.Where(door => !(door is IMyAirtightHangarDoor) && !SkipBlock(door)))
            {
                DateTime time;
                if (_collection.TryGetValue(door, out time))
                {
                    if ((DateTime.Now - time).TotalSeconds < _doorDelay) continue;
                    door.CloseDoor();
                    _collection.Remove(door);
                    continue;
                }

                if (door.Status == DoorStatus.Closed || door.Status == DoorStatus.Closing) continue;
                _collection.Add(door, DateTime.Now);
            }
        }


        /// <summary>
        ///     Controls Turrets
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
                var priority = new Random().Next(_defaultAggression + 1, _defaultAggression * _aggressionMultiplier);
                var turret = turrets[i];
                //check if Turrets custom data already has a number and assign one if it doesnt
                int cData;
                if (string.IsNullOrEmpty(turret.CustomData) || !int.TryParse(turret.CustomData, out cData) ||
                    cData > _defaultAggression * _aggressionMultiplier)
                    turret.CustomData = priority.ToString();


                //Make sure designators are always online
                if (StringContains(turret.CustomName, _designatorName) ||
                    StringContains(turret.CustomName, _antipersonnelName) ||
                    StringContains(turret.CustomName, _antimissileName))
                {
                    SetTurret(turret);
                    turret.Enabled = true;
                    continue;
                }

                //compare number in custom data to aggression and turn off if higher
                turret.Enabled = int.Parse(turret.CustomData) < _aggression || turret.IsUnderControl;

                if (turret.Enabled == false || StringContains(turret.CustomName, _designatorName) ||
                    StringContains(turret.CustomName, _antimissileName)) continue;
                if (turret.GetInventory().ItemCount != 0 || !turret.HasInventory) continue;
                if (_showOnHud)turret.ShowOnHUD = true;
            }
        }

        private void SetTurret(IMyLargeTurretBase turret)
        {
            turret.Enabled = true;
            if (StringContains(turret.CustomName, _designatorName))
            {
                turret.SetValueBool("TargetCharacters",false);
                turret.SetValueBool("TargetLargeShips",true);
                turret.SetValueBool("TargetStations",true);
                turret.SetValueBool("TargetSmallShips",true);
                turret.SetValueBool("TargetMissiles",false);
                return;
            }
            if (StringContains(turret.CustomName, _antipersonnelName))
            {
                turret.SetValueBool("TargetCharacters",true);
                turret.SetValueBool("TargetLargeShips",false);
                turret.SetValueBool("TargetStations",false);
                turret.SetValueBool("TargetSmallShips",false);
                turret.SetValueBool("TargetMissiles",false);
                return;
            }
            if (StringContains(turret.CustomName, _antimissileName))
            {
                turret.SetValueBool("TargetCharacters",false);
                turret.SetValueBool("TargetLargeShips",false);
                turret.SetValueBool("TargetStations",false);
                turret.SetValueBool("TargetSmallShips",false);
                turret.SetValueBool("TargetMissiles",true);
            }
        }

        private void UpdateFuelTypes(float startProportion, float endProportion)
        {
            if (!_capReactors)return;
            var reactors = _reactors?.Where(x =>!Closed(x) && x.GetInventory().ItemCount > 0).ToList();
            if (_reactorFuel.Keys.Count > 10) _reactorFuel.Clear();
            if (reactors?.Any() == false||reactors==null)
            {
                _reactorFuel.Clear();
                return;
            }

            var start = (int) (startProportion * reactors.Count);
            var end = (int) (endProportion * reactors.Count);
            for (var i = start; i < end; ++i)
            {
                var reactor = reactors[i];
                if (_reactorFuel.ContainsKey(reactor.BlockDefinition.SubtypeId))continue;
                var items = new List<MyInventoryItem>();
                reactor.GetInventory().GetItems(items);
                _reactorFuel.Add(reactor.BlockDefinition.SubtypeId,items.FirstOrDefault().Type);

            }

        }

//handles Cerebro's aggression level
        private void AggroBuilder()
        {
            _combatFlag = _aggression > _defaultAggression;
            if (_combatFlag && _myAntenna != null)
            {
                if (_hive)
                {
                    _myAntenna.Radius = 5000f;
                    _myAntenna.Enabled = true;
                    _myAntenna.EnableBroadcasting = true;
                    _myAntenna.AttachedProgrammableBlock = Me.GetId();
                }
                else
                {
                    _myAntenna.Radius = 500f;
                }
            }
            Refocus();
            if (_hive)
            {
                if (_combatFlag) AttackTarget();
                else
                    FollowMe();
            }


            _aggression = HasTarget(out _myTarget)
                ? Math.Min(_aggression += 1, _defaultAggression * _aggressionMultiplier)
                : Math.Max(_aggression -= 2, _defaultAggression);

        }

//refocus turrets on target in combat
        private void Refocus()
        {
            if (_myTarget.IsEmpty() || _myTarget.Position == new Vector3D(0, 0, 0)) return;
            foreach (var turret in _turrets.Where(turret => !SkipBlock(turret) && turret.Enabled && !turret.HasTarget))
                turret.SetTarget(_myTarget.Position);
        }

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
                if (Closed(connector) || connector.Status != MyShipConnectorStatus.Connectable ||
                    connector.Status == MyShipConnectorStatus.Connected)
                {
                    _collection.Remove(connector);
                    continue;
                }

                DateTime time;
                if (_collection.TryGetValue(connector, out time))
                {
                    if ((DateTime.Now - time).TotalSeconds < _connectDelay) continue;
                    connector.Connect();
                    _collection.Remove(connector);
                }
                else
                {
                    _collection.Add(connector, DateTime.Now);
                }
            }
        }

//clear turret
        private void ResetTurrets()
        {
            foreach (var turret in _turrets.Where(turret=>!SkipBlock(turret) && !turret.HasTarget)) turret.ResetTargetingToDefault();
        }

//returns if any of the turrets are actively targeting something
        private bool HasTarget(out MyDetectedEntityInfo target)
        {
            target = new MyDetectedEntityInfo();

            foreach (var turret in _turrets.Where(turret => turret.Enabled && turret.HasTarget && !SkipBlock(turret)))
            {
                target = turret.GetTargetedEntity();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset block dictionaries
        /// </summary>
        private void GetBlocks()
        {

            _gridBlocks.Clear();
            GridTerminalSystem.GetBlockGroupWithName(_welderGroup)?.GetBlocksOfType(_shipWelders);
            GridTerminalSystem.GetBlocksOfType(_gridBlocks, x => x.IsSameConstructAs(Me)
                                                                 && !StringContains(x.CustomData, "ignore") &&
                                                                 !StringContains(x.CustomName, "ignore"));
            _myProjector = (IMyProjector) GridTerminalSystem.GetBlockWithName(_reProj);

            if (_lowBatteries?.Any() == true)
                foreach (var battery in _lowBatteries.Where(battery => Closed(battery.Key)))
                    _lowBatteries.Remove(battery.Key);
            _landingGears = _gridBlocks.OfType<IMyLandingGear>();
            _soundBlocks = _gridBlocks.OfType<IMySoundBlock>();
            _powerBlocks = _gridBlocks.OfType<IMyPowerProducer>();
            _gyros = _gridBlocks.OfType<IMyGyro>();
            _productionBlocks = _gridBlocks.OfType<IMyProductionBlock>();
            _gravGens = _gridBlocks.OfType<IMyGravityGenerator>();
            _batteries = _gridBlocks.OfType<IMyBatteryBlock>();
            _reactors = _gridBlocks.OfType<IMyReactor>();
            _gasTanks = _gridBlocks.OfType<IMyGasTank>();
            _airVents = _gridBlocks.OfType<IMyAirVent>();
            _textPanels = _gridBlocks.OfType<IMyTextPanel>();
            _connectors = _gridBlocks.OfType<IMyShipConnector>();
            _turrets = _gridBlocks.OfType<IMyLargeTurretBase>();
            _lights = _gridBlocks.OfType<IMyLightingBlock>();
            _doors = _gridBlocks.OfType<IMyDoor>();
            _solars = _gridBlocks.OfType<IMySolarPanel>();
            _cockpits = _gridBlocks.OfType<IMyShipController>();
            _remotes = _gridBlocks.OfType<IMyRemoteControl>();
            _gasGens = _gridBlocks.OfType<IMyGasGenerator>();
            _thrusters = _gridBlocks.OfType<IMyThrust>();
                
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
            private readonly StringBuilder _sb = new StringBuilder();

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
                _sb.Clear();
                _sb.AppendLine("\n_____________________________\nCerebro Runtime Info\n");
                _sb.AppendLine($"Avg instructions: {AverageInstructions:n2}");
                _sb.AppendLine($"Max instructions: {MaxInstructions:n0}");
                _sb.AppendLine($"Avg complexity: {MaxInstructions / _instructionLimit:0.000}%");
                _sb.AppendLine($"Avg runtime: {AverageRuntime:n4} ms");
                _sb.AppendLine($"Max runtime: {MaxRuntime:n4} ms");
                return _sb.ToString();
            }
        }

        #endregion

        #region Vector math

        public static class VectorMath
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
            if (_hive && _myAntenna.IsFunctional) IGC.SendBroadcastMessage($"Follow {Me.Position}", _myAntenna.Radius);
        }

        private void AttackTarget()
        {
            if (_hasAntenna && _myAntenna.IsFunctional)
                IGC.SendBroadcastMessage($"Attack {_myTarget.Position}", _myAntenna.Radius);
        }

        #endregion

        #region Navigation

        private void CheckNavigation()
        {
            if (!_autoNavigate || _remoteControl == null) return;
            _inGravity = _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);
            _currentSpeed = _remoteControl.GetShipSpeed();

            switch (_autoPilot)
            {
                case Pilot.Disabled:
                    break;
                case Pilot.Cruise:
                    Cruise(_cruiseDirection, _cruiseHeight, _cruiseSpeed,_giveControl);
                    return;
                case Pilot.Land:
                    LevelShip(false, 0, 0);
                    Land();
                    if (!(_currentHeight < 20) || !(_currentSpeed < 1)) return;
                    OverrideThrust(false, Vector3D.Zero, 0, 0);
                    _autoPilot = Pilot.Disabled;
                    return;
                case Pilot.Takeoff:
                    TakeOff(_cruiseSpeed,_takeOffAngle,_giveControl);
                    return;
                default:
                    return;
            }

            if (!_combatFlag)
                LevelShip(true, 0, 0);
            if (_combatFlag && !CheckPlayer() && !_myTarget.IsEmpty() && !IsDocked() && !_inGravity)
            {
                if (Vector3D.Distance(_myTarget.Position, _remoteControl.GetPosition()) > 600)
                    SetDestination(_myTarget.Position, true, 50);
                else _remoteControl.SetAutoPilotEnabled(false);
            }
            else if (CheckPlayer())
            {
                _remoteControl.SetAutoPilotEnabled(false);
            }
        }

        private void DampenersOnline(bool enable)
        {
            foreach (var controller in _cockpits.Where(controller => !Closed(controller)))
                controller.DampenersOverride = enable;
        }

        private void SetDestination(Vector3D destination, bool enableCollision, float speed)
        {
            _remoteControl.ClearWaypoints();
            _remoteControl.FlightMode = FlightMode.OneWay;
            _remoteControl.SpeedLimit = speed;
            _remoteControl.AddWaypoint(destination, "AutoPilot");
            _remoteControl.Direction = Base6Directions.Direction.Forward;
            _remoteControl.SetCollisionAvoidance(enableCollision);
            _remoteControl.SetAutoPilotEnabled(true);
        }

        private void Cruise(string dir, double height = 2500, double speed = 100, bool giveControl = true)
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

            _thrust = _currentSpeed < speed - 0.90
                ? Math.Min(_thrust += 0.05f, 1)
                : Math.Max(_thrust -= 0.25f, 0);
            _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);
            var x = Math.Max(height - 500, _safeCruiseHeight);
            var y = _currentHeight < _cruiseHeight + 250 ? 0.025f : -0.075f;
            _cruiseHeight = _cruiseHeight < _safeCruiseHeight ? x : _cruiseHeight;
            LevelShip(giveControl, y, 0);
            if (_inGravity && Math.Abs(_currentHeight - x) > 1500)
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

            OverrideThrust(true, direction, _thrust, _currentSpeed, speed);
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

            DampenersOnline(true);
        }

        private void TakeOff(double takeOffSpeed = 100, double angle = 0, bool giveControl = false)
        {
            if (_remoteControl == null)
            {
                GrabNewRemote();
                return;
            }
            LevelShip(giveControl, (float)angle/100, 0);
            var up = _remoteControl.WorldMatrix.GetDirectionVector(
                _remoteControl.WorldMatrix.GetClosestDirection(-_remoteControl.GetNaturalGravity()));
            
            var maxSpeed = takeOffSpeed - takeOffSpeed * 0.05;

            if (_currentHeight <= _landingBrakeHeight) _thrust = 1f;
            else
                _thrust =  _currentSpeed < maxSpeed
                    ? Math.Min(_thrust += 0.15f, 1)
                    : Math.Max(_thrust -= 0.05f, 0);
            OverrideThrust(_inGravity, up, _thrust, _currentSpeed,
                takeOffSpeed + takeOffSpeed * 0.1);
            _autoPilot = _inGravity ? Pilot.Takeoff : Pilot.Disabled;
        }

        private void Land()
        {
            foreach (var remote in _remotes.Where(remote => !Closed(remote))) remote.SetAutoPilotEnabled(false);

            if (_currentHeight > _landingBrakeHeight)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.001f, _currentSpeed);
                return;
            }

            DampenersOnline(true);
            if (_currentHeight > 450)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.001f, _currentSpeed, 75);
                return;
            }

            if (_currentHeight > 100)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.001f, _currentSpeed, 25);
                return;
            }


            _thrust = _remoteControl.GetShipVelocities().LinearVelocity.Y +
                      _remoteControl.GetShipVelocities().LinearVelocity.Z < 0
                      && _currentSpeed > 5
                ? Math.Min(_thrust += 0.15f, 1f)
                : Math.Max(_thrust -= 0.5f, 0.001f);
            OverrideThrust(true, _remoteControl.WorldMatrix.Up,
                _thrust, _remoteControl.GetShipSpeed(), 5);
        }


        private void OverrideThrust(bool enableOverride, Vector3D direction, float thrustModifier,
            double currentSpeed = 100, double maximumSpeed = 110)
        {
            foreach (var thruster in _thrusters.Where(thruster => thruster != null && thruster.IsFunctional))
                if (enableOverride && currentSpeed < maximumSpeed)
                {
                    if (thruster.WorldMatrix.Forward == direction * -1)
                    {
                        thruster.Enabled = true;
                        thruster.ThrustOverridePercentage = thrustModifier;
                    }

                    if (thruster.WorldMatrix.Forward == direction) thruster.Enabled = false;
                }
                else
                {
                    thruster.Enabled = true;
                    thruster.SetValueFloat("Override", 0);
                }
        }

        private void LevelShip(bool checkPlayer, float setPitch, float setRoll)
        {
            if (checkPlayer && CheckPlayer() || !_inGravity)
            {
                foreach (var gyro in _gyros) gyro.GyroOverride = false;
                return;
            }

            var gravity = _remoteControl.GetNaturalGravity();
            var up = -gravity;
            var left = Vector3D.Cross(up, _remoteControl.WorldMatrix.Forward);
            var forward = Vector3D.Cross(left, up);
            var localUpVector = Vector3D.Rotate(up, MatrixD.Transpose(_remoteControl.WorldMatrix));
            var flattenedUpVector = new Vector3D(localUpVector.X, localUpVector.Y, 0);
            var roll = (float) VectorMath.AngleBetween(flattenedUpVector, Vector3D.Up) *
                       Math.Sign(Vector3D.Dot(Vector3D.Right, flattenedUpVector));
            var pitch = (float) VectorMath.AngleBetween(forward, _remoteControl.WorldMatrix.Forward) *
                        Math.Sign(Vector3D.Dot(up, _remoteControl.WorldMatrix.Forward));
            _pitchDelay = _inGravity && Math.Abs(pitch - setPitch) >= 0.05
                ? Math.Min(_pitchDelay += 1, 25)
                : Math.Max(_pitchDelay -= 1, 0);
            _rollDelay = _inGravity && Math.Abs(roll - setRoll) >= 0.05
                ? Math.Min(_rollDelay += 1, 25)
                : Math.Max(_rollDelay -= 1, 0);
            var pitchOverride = Math.Abs(pitch) > 0.90 ? 0.25f : 0.05f;
            var rollOverride = Math.Abs(roll) > 0.90 ? 0.25f : 0.05f;
            foreach (var gyro in _gyros)
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
            }
        }

        private bool CheckPlayer()
        {
            return _cockpits.Any(cockpit => cockpit.IsUnderControl);
        }

        #endregion

        #region Block Lists

        private IEnumerable<IMyDoor> _doors;
        private IEnumerable<IMyTextSurface> _textSurfaces;
        private IEnumerable<IMyProductionBlock> _productionBlocks;
        private IEnumerable<IMyReactor> _reactors;
        private readonly List<IMyShipWelder> _shipWelders = new List<IMyShipWelder>();
        private IEnumerable<IMyAirVent> _airVents;
        private IEnumerable<IMyGasGenerator> _gasGens;
        private IEnumerable<IMyGasTank> _gasTanks;
        private IEnumerable<IMyGravityGenerator> _gravGens;
        private readonly List<IMyTerminalBlock> _gridBlocks = new List<IMyTerminalBlock>();
        private IEnumerable<IMyThrust> _thrusters;
        private IEnumerable<IMyTerminalBlock> _damagedBlocks;
        private IEnumerable<IMyPowerProducer> _powerBlocks;
        private IEnumerable<IMyLightingBlock> _lights;
        private IEnumerable<IMySolarPanel> _solars;
        private IEnumerable<IMyTextPanel> _textPanels;
        private IEnumerable<IMyLargeTurretBase> _turrets;
        private IEnumerable<IMyRemoteControl> _remotes;
        private IEnumerable<IMyShipConnector> _connectors;
        private IEnumerable<IMyShipController> _cockpits;
        private IEnumerable<IMyBatteryBlock> _batteries;
        private IEnumerable<IMyGyro> _gyros;
        private IEnumerable<IMySoundBlock> _soundBlocks;
        private IEnumerable<IMyLandingGear> _landingGears;

//dictionary
        private readonly Dictionary<IMyBatteryBlock, float> _lowBatteries = new Dictionary<IMyBatteryBlock, float>();
        private readonly Dictionary<IMyCubeBlock, DateTime> _collection = new Dictionary<IMyCubeBlock, DateTime>();
        private readonly Dictionary<string, MyItemType> _reactorFuel = new Dictionary<string, MyItemType>();

        #endregion

        #region Fields

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly StringBuilder _sbPower = new StringBuilder();
        private readonly StringBuilder _status = new StringBuilder();


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
        private bool _scriptInitialize;
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


        private string _designatorName = "designator";
        private string _antipersonnelName = "antiPersonnel";
        private string _antimissileName = "antimissile";
        private string _cruiseDirection;
        private double _cruiseSpeed;
        private bool _giveControl;
        private double _takeOffAngle;
        private double _cruiseHeight;
        private Pilot _autoPilot = Pilot.Disabled;


//Floats, Int, double
        private readonly int _connectDelay = 5;
        private readonly int _defaultAggression = 10;
        private readonly int _aggressionMultiplier = 2;
        private int _doorDelay = 5;
        private const int ProductionDelay = 30;
        private int _aggression;
        private int _alertCounter;
        private int _menuPointer;
        private const int ProjectorShutoffDelay = 30;
        private int _pitchDelay;
        private int _rollDelay;
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
        private MyDetectedEntityInfo _myTarget;

        #endregion

        #region Setup and Initiate

        private readonly MyIni _ini = new MyIni();
        private readonly List<string> _iniSections = new List<string>();
        private readonly StringBuilder _customDataSb = new StringBuilder();
        private Dictionary<MyItemType, double> _fuelCount = new Dictionary<MyItemType, double>();
        private Dictionary<IMyReactor, MyItemType> _fuelCollection;
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
        private const string INI_WEAPONS_DESIGNATORS = "Designator Name";
        private const string INI_WEAPONS_ANTIMISSILE = "Antimissile Name";
        private const string INI_WEAPONS_ANTIPERSONNEL = "AntiPersonnel Name";



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
            _antipersonnelName = _ini.Get(INI_SECTION_WEAPONS, INI_WEAPONS_ANTIPERSONNEL).ToString(_antipersonnelName);
            _designatorName = _ini.Get(INI_SECTION_WEAPONS, INI_WEAPONS_DESIGNATORS).ToString(_designatorName);
            _antimissileName = _ini.Get(INI_SECTION_WEAPONS, INI_WEAPONS_ANTIMISSILE).ToString(_antimissileName);


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
            _ini.Set(INI_SECTION_WEAPONS, INI_WEAPONS_ANTIPERSONNEL,_antipersonnelName);
            _ini.Set(INI_SECTION_WEAPONS, INI_WEAPONS_DESIGNATORS, _designatorName);
            _ini.Set(INI_SECTION_WEAPONS, INI_WEAPONS_ANTIMISSILE, _antimissileName);

            var output = _ini.ToString();
            if (!string.Equals(output, Me.CustomData))
                Me.CustomData = output;
        }

        #endregion
    }
}