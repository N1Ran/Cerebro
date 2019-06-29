using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    internal sealed class Program : MyGridProgram
    {
        private enum Switch
        {
            Start,
            Pause
        }

        private enum AlertState
        {
            Clear,
            Error,
            Combat
        }

        #region Block Lists
        private readonly List<IMyDoor> _doors = new List<IMyDoor>();
        private readonly List<IMyGasTank> _oxygenTanks = new List<IMyGasTank>();
        private readonly List<IMyProductionBlock> _productionBlocks = new List<IMyProductionBlock>();
        private readonly List<IMyReactor> _reactors = new List<IMyReactor>();
        private readonly List<IMyRefinery> _refineries = new List<IMyRefinery>();
        private readonly List<IMyShipWelder> _shipWelders = new List<IMyShipWelder>();
        private readonly List<IMyAirVent> _airVents = new List<IMyAirVent>();
        private readonly List<IMyGasGenerator> _gasGens = new List<IMyGasGenerator>();
        private readonly List<IMyGasTank> _gasTanks = new List<IMyGasTank>();
        private readonly List<IMyGravityGenerator> _gravGens = new List<IMyGravityGenerator>();
        private readonly List<IMyLargeTurretBase> _designators = new List<IMyLargeTurretBase>();
        private readonly List<IMyTerminalBlock> _gridBlocks = new List<IMyTerminalBlock>();
        private readonly List<IMyGasTank> _hydrogenTanks = new List<IMyGasTank>();
        private readonly List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
        private readonly List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
        private readonly List<IMySolarPanel> _solars = new List<IMySolarPanel>();
        private readonly StringBuilder status = new StringBuilder();
        private readonly List<IMyTextPanel> _textPanels = new List<IMyTextPanel>();
        private readonly List<IMyTimerBlock> _timers = new List<IMyTimerBlock>();
        private readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        private readonly List<IMyRemoteControl> _remotes = new List<IMyRemoteControl>();
        private readonly List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        private readonly List<IMyShipController> _cockpits = new List<IMyShipController>();
        private readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private readonly List<IMyBatteryBlock> _lowBatteries = new List<IMyBatteryBlock>();
        private readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();

        //dictionary
        readonly Dictionary<IMyCubeBlock,DateTime>_collection = new Dictionary<IMyCubeBlock, DateTime>();
        readonly Dictionary<IMyCubeBlock, MyItemType> _reactorFuel = new Dictionary<IMyCubeBlock, MyItemType>();
        #endregion

        #region Fields
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly StringBuilder _info = new StringBuilder();

        //enum
        private Switch _currentMode = Switch.Start;
        private AlertState _alert = AlertState.Clear;
        //Boolean
        private bool _combatFlag;
        private bool _hasAntenna;
        private bool _hasProjector;
        private bool _hive = true;
        private bool _industFlag = false;
        private bool _isStatic = false;
        private bool _needAir = false;
        private bool _powerFlag = false;
        private bool _scriptInitialize;
        private bool _tankRefill = false;
        private bool _turretControl = true;
        private bool _controlVents = true;
        private bool _controlGasSystem = true;
        private bool _controlProduction = true;
        private readonly bool _navFlag = false;
        private bool _capReactors = true;
        private bool _powerManagement = false;
        private bool _master = true;
        private bool _handleRepair = true;
        private bool _damageDetected = false;


        //Floats, Int, double
        private const int _connectDelay = 5;
        private const int _defaultAggression = 10;
        private int _doorDelay = 5;
        private int _productionDelay = 30;
        private int _aggression;
        private int _alertCounter;
        private int _connectTimer;
        private int _counter;
        private int _doorTimer;
        private int _menuPointer;
        private const int _projectorShutoffDelay = 30;

        private static int _lowFuel = 50;
        private float _script;

        private static float _tankFillLevel = 0.5f;
        private static float _fillLevel = 150;
        private static float _rechargePoint = .15f;
        private static float _overload = 0.90f;

        private string _welderGroup = "Welders";
        private string _reProj = "Projector";


        private IMyRadioAntenna _myAntenna;
        private IMyProjector _myProjector;

        //entityinfo
        private MyDetectedEntityInfo _myTarget;

        #endregion

        #region Setup and Initiate
        readonly MyIni _ini = new MyIni();
        readonly List<string> _iniSections = new List<string>();
        StringBuilder _customDataSB = new StringBuilder();

        //Settings
        private const string INI_SECTION_GENERAL = "Cerebro Settings - General";
        private const string INI_GENERAL_MASTER = "Is Main Script";
        private const string INI_GENERAL_HIVE = "Control Drones";
        private const string INI_GENERAL_PROJECTOR = "Repair Projector";
        private const string INI_GENERAL_WELDERS = "Welders GroupName";


        //controls
        private const string INI_SECTION_CONTROLS = "Cerebro Settings - Controls";
        private const string INI_CONTROLS_TURRETS = "Control Turrets";
        private const string INI_CONTROLS_PRODUCTION = "Control Refineries and Assemblers";
        private const string INI_CONTROLS_VENTS = "Control Vents";
        private const string INI_CONTROLS_GAS = "Control Gas Production";
        private const string INI_CONTROLS_REPAIR = "Auto Repair Ship";
        private const string INI_CONTROLS_POWER = "Power Management";

        //Delays
        private const string INI_SECTION_DELAYS = "Cerebro Settings - Delays and Floats";
        private string INI_DELAYS_DOOR = "Door Delay";

        //Reactor
        private string INI_SECTION_POWER = "Cerebro Settings - Power";
        private string INI_POWER_ENABLECAP = "Enable Reactor Fuel Cap";
        private string INI_POWER_CAPLEVEL = "Reactor Fuel Fill Level";

        //Battery
        private string INI_POWER_RECHARGEPOINT = "Battery Recharge Point";
        private string INI_POWER_OVERLOAD = "Power Overload";


        private void ParseIni()
        {
            _ini.Clear();
            _ini.TryParse(Me.CustomData);

            _iniSections.Clear();
            _ini.GetSections(_iniSections);

            if (_iniSections.Count == 0)
            {
                _customDataSB.Clear();
                _customDataSB.Append(Me.CustomData);
                _customDataSB.Replace("---\n", "");

                _ini.EndContent = _customDataSB.ToString();
            }

            //Get Config
            _reProj = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_PROJECTOR).ToString(_reProj);
            _master = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_MASTER).ToBoolean(_master);
            _hive = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_HIVE).ToBoolean(_hive);

            _turretControl = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_TURRETS).ToBoolean(_turretControl);
            _powerManagement = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_POWER).ToBoolean(_powerManagement);
            _welderGroup = _ini.Get(INI_SECTION_GENERAL, INI_GENERAL_WELDERS).ToString(_welderGroup);
            _controlProduction = _ini.Get(INI_SECTION_GENERAL, INI_CONTROLS_PRODUCTION).ToBoolean(_controlProduction);
            _controlVents = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_VENTS).ToBoolean(_controlVents);
            _controlGasSystem = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_GAS).ToBoolean(_controlGasSystem);
            _handleRepair = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_REPAIR).ToBoolean(_handleRepair);

            _doorDelay = _ini.Get(INI_SECTION_DELAYS, INI_DELAYS_DOOR).ToInt32(_doorDelay);

            _lowFuel = _ini.Get(INI_SECTION_POWER, INI_POWER_CAPLEVEL).ToInt32(_lowFuel);
            _rechargePoint = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT).ToSingle(_rechargePoint);
            _overload = _ini.Get(INI_SECTION_POWER, INI_POWER_OVERLOAD).ToSingle(_overload);
            _capReactors = _ini.Get(INI_SECTION_POWER, INI_POWER_ENABLECAP).ToBoolean(_capReactors);
            
            

            WriteIni();
        }

        private void WriteIni()
        {
            //General Settings
            _ini.Set(INI_SECTION_GENERAL,INI_GENERAL_MASTER,_master);
            _ini.Set(INI_SECTION_GENERAL,INI_GENERAL_HIVE,_hive);
            _ini.Set(INI_SECTION_GENERAL,INI_GENERAL_PROJECTOR,_reProj);
            _ini.Set(INI_SECTION_GENERAL,INI_GENERAL_WELDERS,_welderGroup);

            //Control Settings
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_REPAIR,_handleRepair);
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_GAS,_controlGasSystem);
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_POWER,_powerManagement);
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_PRODUCTION,_controlProduction);
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_TURRETS,_turretControl);
            _ini.Set(INI_SECTION_CONTROLS,INI_CONTROLS_VENTS,_controlVents);

            //Delays
            _ini.Set(INI_SECTION_DELAYS,INI_DELAYS_DOOR,_doorDelay);

            //Power
            _ini.Set(INI_SECTION_POWER,INI_POWER_ENABLECAP,_capReactors);
            _ini.Set(INI_SECTION_POWER,INI_POWER_CAPLEVEL,_lowFuel);
            _ini.Set(INI_SECTION_POWER,INI_POWER_RECHARGEPOINT,_rechargePoint);
            _ini.Set(INI_SECTION_POWER,INI_POWER_OVERLOAD,_overload);

            var output = _ini.ToString();
            if (!string.Equals(output, Me.CustomData))
                Me.CustomData = output;


        }


        


        #endregion


        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            ScriptInitiate();
        }

        
        private void Main(string argument)
        {         
            if (!_scriptInitialize) ScriptInitiate();
            if (!_hasAntenna && _hive) GrabNewAntenna();
            CurrentState(argument, out _currentMode);
            if (_currentMode != Switch.Start)
            {
                Echo("Script Paused!");
                return;
            }
            CountTicks();
            if (GridFlags() || _combatFlag)
            {
                _alert = _combatFlag ? AlertState.Combat : AlertState.Error;
            }
            else {_alert = AlertState.Clear;}
            Runtime.UpdateFrequency = _master ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
            status.Clear();
            status.Append("AI Running ");
            //debug running
            WriteToScreens(_sb);
            DisplayData(_menuPointer);
            _script = (float) Runtime.LastRunTimeMs;
            Echo($"Script RunTime: {_script:F2} ms");
            Echo($"Current Aggression Level: {_aggression}");
            SystemsCheck(_counter);
            //if (_script > 1.3f) Runtime.UpdateFrequency = UpdateFrequency.None;
            //Echo(_counter.ToString());
        }

        private void SystemsCheck(int i)
        {           
            _turretControl = _turrets.Count > 0;
            _isStatic = Me.CubeGrid.IsStatic;
            _hive = _isStatic && _hasAntenna;
            _hasProjector = _myProjector != null && !SkipBlock(_myProjector);
            _hasAntenna = _myAntenna != null && !SkipBlock(_myAntenna);
            _doorTimer = _doorTimer < _doorDelay ? _doorTimer += 1 : 0;
            AutoDoors();
            switch (i)
            {
                case 0:
                    GetBlocks();
                    break;
                case 1:
                    if (_handleRepair)
                    {
                        DamageReport(out _damageDetected);
                        SetWeldersOnline(_damageDetected);
                        if (!_hasProjector) return;
                        if (_collection.TryGetValue(_myProjector, out var time))
                        {
                            if (time.Second < _projectorShutoffDelay || _damageDetected) break;
                            _myProjector.Enabled = false;
                            _collection.Remove(_myProjector);
                        }
                        if (!_damageDetected && !_myProjector.Enabled)break;
                        _myProjector.Enabled = true;
                        _collection.Add(_myProjector, DateTime.Now);
                    }
                    break;
                case 2:
                    if (_powerManagement) BatteryCheck();
                    break;
                case 3:
                    if (!_turretControl)break;
                    _combatFlag = _aggression > _defaultAggression;
                    AggroBuilder();
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

                    break;
                case 4:
                    if (_turretControl) ManageTurrets();
                    break;
                case 5:
                    if (!_turretControl) break;
                    Refocus();
                    if (_hive)
                    {
                        if (_combatFlag)AttackTarget();
                        else
                        {
                            FollowMe();
                        }
                    }
                    break;
                case 6:
                     if(!_controlProduction) break;
                     CheckProduction();
                    break;
                case 7:
                    ParseIni();
                    //AutoDoors();
                    break;
                case 8:
                    CheckConnectors();
                    _connectTimer = _connectTimer < _connectDelay ? _connectTimer += 1 : 0;
                    break;
                case 9:
                    if (_powerManagement) CheckFuel();
                    break;
                case 10:
                    if (GridFlags() || !_controlVents) return;
                    CheckVents();
                    break;
                case 11:
                    if (GridFlags() || !_controlGasSystem) return;
                    CheckTanks();
                    break;
                case 12:
                    if (GridFlags() || !_controlGasSystem) return;
                    GasGenSwitch(_tankRefill);
                    break;
                default:
                    return;
            }

            
        }


        private void GrabNewAntenna()
        {
            if (_myAntenna != null) return;
            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType(antennas);
            if (antennas.Count < 1)
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
            ParseIni();
            GetBlocks();
        }

        private void CurrentState(string st, out Switch result)
        {
            while (true)
            {
                if (StringContains(st, "pause"))
                {
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    result = Switch.Pause;
                    return;
                }
                if (StringContains(st, "start"))
                {
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                    result = Switch.Start;
                    return;
                }
                if (StringContains(st,"cyclemenu+"))
                {
                    if (_menuPointer < 3) _menuPointer += 1;
                }
                if (StringContains(st,"cyclemenu-"))
                {
                    if (_menuPointer > 0) _menuPointer -= 1;
                }

                break;
            }

            result = Switch.Start;

        }


        private static void SetTurret(IMyTerminalBlock turret)
        {
            if (!(turret is IMyLargeTurretBase))return;
            if (StringContains(turret.CustomName, "antimissile"))
            {
                turret.SetValueBool("TargetSmallShips", false);
                turret.SetValueBool("TargetLargeShips", false);
                turret.SetValueBool("TargetMissiles", true);
                turret.SetValueBool("TargetMeteors", false);
                turret.SetValueBool("TargetCharacters", false);
                turret.SetValueFloat("Range", 750);
                return;
            }

            if (turret is IMyLargeInteriorTurret && !StringContains(turret.CustomName,"designator"))
            {
                turret.SetValueBool("TargetSmallShips", true);
                turret.SetValueBool("TargetLargeShips", false);
                turret.SetValueBool("TargetMissiles", true);
                turret.SetValueBool("TargetMeteors", false);
                turret.SetValueBool("TargetCharacters", true);
                turret.SetValueFloat("Range", 500);
                return;
            }

            turret.SetValueFloat("Range", 1500);
            turret.SetValueBool("TargetMeteors", false);
            turret.SetValueBool("TargetCharacters", false);
            turret.SetValueBool("TargetLargeShips", true);
            turret.SetValueBool("TargetMissiles", false);
            turret.SetValueBool("TargetCharacters", false);
            turret.SetValueBool("TargetSmallShips", true);
        }


        //Handle Flags
        private bool GridFlags()
        {
            return _industFlag || _powerFlag || _navFlag || _damageDetected;
        }

        //count ticks
        private void CountTicks()
        {
            _counter = _counter < 12 ? _counter += 1 : 0;
        }

        //--------------Command Broadcasts---------------

        //Command to order drones to follow

        private void FollowMe()
        {
            if (_hive && _myAntenna.IsFunctional)
            {
                IGC.SendBroadcastMessage($"Follow {Me.Position}",_myAntenna.Radius);
            }
        }

        void AttackTarget()
        {
            if (_hasAntenna && _myAntenna.IsFunctional)
            {
                IGC.SendBroadcastMessage($"Attack {_myTarget.Position}",_myAntenna.Radius);
            }
        }
        
        //---------------Data Display--------------------


        //Display Data on screen
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

        //write sb to screens with "Cerebro" in their custom data
        private void WriteToScreens(StringBuilder sb)
        {
            foreach (var textPanel in _textPanels)
            {
                if (SkipBlock(textPanel) || !StringContains(textPanel.CustomName,"cerebro"))continue;
                textPanel.WriteText(sb);
            }
            //Me.GetSurface(0).WriteText(sb);
        }

        //Alerts the player should see
        private void Alerts()
        {
            _sb.Clear();
            _sb.AppendLine();
            _sb.Append("AI Running ");
            //debug running
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

            if (GridFlags())
            {
                _sb.Append("Warning: Grid Error Detected!");
                _sb.AppendLine();

                if (_powerFlag)
                {
                    _sb.Append(" Power status warning!");
                    _sb.AppendLine();
                    foreach (var battery in _lowBatteries)
                    {
                        _sb.Append($"  {battery.CustomName} Recharging - {Math.Round((battery.CurrentStoredPower / battery.MaxStoredPower) * 100)}%");
                        _sb.AppendLine();
                    }
                }
            }

            if (_hasProjector && _myProjector.IsProjecting)
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

            _alertCounter = _alertCounter < 10 ? _alertCounter +=1 : 0;


            AlertLights();
        }

        //Get power usage
        private void GetPower()
        {
            double batteryPower = 0;
            double reactorPower = 0;
            double solarPower = 0;

            foreach (var x in _batteries)
            {
                if (SkipBlock(x)) continue;
                batteryPower += x.CurrentOutput;
            }

            foreach (var x in _reactors)
            {
                if (SkipBlock(x)) continue;
                reactorPower += x.CurrentOutput;
            }

            foreach (var x in _solars)
            {
                if (SkipBlock(x)) continue;
                solarPower += x.CurrentOutput;
            }

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

        //grid errors
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

            if (_industFlag)
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

        //grab lights and swap to red
        private void AlertLights()
        {
            Color color;
            switch (_alert)
            {
                case AlertState.Clear:
                    color = Color.Green;
                    break;
                case AlertState.Error:
                    color = Color.Yellow;
                    break;
                case AlertState.Combat:
                    color = Color.Red;
                    break;
                default:
                    color = Color.Green;
                    break;
            }

            foreach (var light in _lights)
            {
                if (Closed(light) || !StringContains(light.CustomName,"alert")) continue;
                SetLight(light, color, _alert > 0);
            }
        }

        private static void SetLight(IMyLightingBlock x, Color y, bool blink)
        {
            x.Color = y;
            x.BlinkIntervalSeconds = blink ? 1 : 0;
            x.Radius = blink ? 6 : 4;
            x.BlinkLength = 50;
        }

        //---------------power management----------------


        //shut down blocks if reactor fuel is low
        private void CheckFuel()
        {
            if (!_capReactors) return;
            var lowCap = (MyFixedPoint) _lowFuel;
            foreach (var reactor in _reactors)
            {
                if (Closed(reactor))continue;
                if (!_reactorFuel.TryGetValue(reactor, out var fuel))
                {
                    reactor.UseConveyorSystem = true;
                    continue;
                }
                reactor.UseConveyorSystem = false;
                foreach (var block in _gridBlocks)
                {
                    if (Closed(block) || !block.HasInventory || block is IMyReactor) continue;

                    var y = reactor.GetInventory(0).GetItemAmount(fuel) < lowCap ? 
                        lowCap - reactor.GetInventory(0).GetItemAmount(fuel):
                        reactor.GetInventory(0).GetItemAmount(fuel) - lowCap;
                    if (reactor.GetInventory(0).GetItemAmount(fuel) < lowCap)
                    {
                        var z = block.GetInventory().FindItem(fuel);
                        if (z == null || !block.GetInventory().CanTransferItemTo(reactor.GetInventory(0), fuel)) continue;
                        block.GetInventory().TransferItemTo(reactor.GetInventory(0), z.Value, y);
                    }
                    else if (reactor.GetInventory(0).GetItemAmount(fuel) > lowCap)
                    { 
                        var z = reactor.GetInventory().GetItemAt(0);
                        if (z != null && !block.GetInventory().IsFull && reactor.GetInventory().CanTransferItemTo(block.GetInventory(),fuel))
                        {
                            reactor.GetInventory(0).TransferItemTo(block.GetInventory(), z.Value, y);
                        }
                    }
                    
                }

            }

        }

        //handle batteries
        private void BatteryCheck()
        {
            double batteryStoredAvg = 0;
            double batteryCurrentAvg = 0;
            double batteryPower = 0;
            double batteryMax = 0;
            double batteryInput = 0;
            foreach (var panel in _solars)
            {
                panel.Enabled = true;
            }
            foreach (var battery in _batteries)
            {

                if (SkipBlock(battery)) continue;
                battery.Enabled = true;
                if (StringContains(battery.CustomName,"backup")  && !_lowBatteries.Contains(battery))
                {
                    if (battery.CurrentStoredPower / battery.MaxStoredPower < 0.1f)
                    {
                        _lowBatteries.Add(battery);
                    }
                    else
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                    continue;
                }
                batteryCurrentAvg += battery.CurrentStoredPower;
                batteryStoredAvg += battery.MaxStoredPower;
                batteryMax += battery.MaxOutput;
                batteryPower += battery.CurrentOutput;
                batteryInput += battery.CurrentInput;
                if (battery.CurrentStoredPower / battery.MaxStoredPower < _rechargePoint)
                {
                    if (!_lowBatteries.Contains(battery))_lowBatteries.Add(battery);
                }

                if (_lowBatteries.Contains(battery))
                {
                    if ((battery.CurrentStoredPower/battery.MaxStoredPower) < 0.99f)
                    {
                        battery.ChargeMode = ChargeMode.Recharge;
                    }

                    else
                    {
                        battery.ChargeMode = ChargeMode.Discharge;
                        _lowBatteries.Remove(battery);
                    }

                }
                else
                {
                    battery.ChargeMode = ChargeMode.Discharge;
                }
                
            }
            var batteriesInRecharge = _lowBatteries.Count;
            var totalBatteries = _batteries.Count;
            var batteryUse = batteryPower / batteryMax;
            _powerFlag = (float) batteriesInRecharge / totalBatteries >= 0.50f || batteryUse >= _overload;
            foreach (var reactor in _reactors)
            {
                if (SkipBlock(reactor))continue;
                reactor.Enabled = _powerFlag;

            }

        }


        //---------------industrial----------------------

        //check airvents
        private void CheckVents()
        {
            if (_airVents.Count == 0) return;
            var ventNeedAir = 0;
            foreach (var vent in _airVents)
            {
                if (SkipBlock(vent) || StringContains(vent.CustomData,"outside")) continue;
                if (!vent.CanPressurize)
                {
                    vent.ShowOnHUD = true;
                    _sb.Append(vent.CustomName + " Can't Pressurize");
                    _sb.AppendLine();
                    continue;
                }

                if (IsNeedAir(vent))
                {
                    vent.Enabled = true;
                    ventNeedAir+=1;
                    continue;
                }
                vent.Enabled = false;
            }

            _needAir = ventNeedAir > 0;
        }

        private static bool IsNeedAir(IMyAirVent vent)
        {

            var powerState = vent.Enabled;
            vent.Enabled = true;
            var oxygenState = vent.GetOxygenLevel();
            vent.Enabled = powerState;
            return oxygenState < 0.80;
        }

        //check for damaged blocks
        private void DamageReport(out bool result)
        {
            var damageCount = 0;

            foreach (var block in _gridBlocks)
            {
                if (Closed(block)) continue;
                var myBlock = block.CubeGrid.GetCubeBlock(block.Position);
                if (myBlock.CurrentDamage < 1)
                {
                    block.ShowOnHUD = false;
                    continue;
                }

                block.ShowOnHUD = true;
                _sb.Append(block.CustomName + " is damaged and needs repair!");
                _sb.AppendLine();
                damageCount+=1;
            }

            result = damageCount > 0;
        }

        private void SetWeldersOnline(bool b)
        {
            GridTerminalSystem.GetBlockGroupWithName(_welderGroup).GetBlocksOfType(_shipWelders);
            if (_shipWelders.Count == 0 || !_handleRepair) return;
            foreach (var welder in _shipWelders)
            {
                if (StringContains(welder.CustomData, "ignore"))continue;
                welder.Enabled = welder.IsWorking || b;
            }
        }

        //check production
        private void CheckProduction()
        {
            foreach (var block in _productionBlocks)
            {
                if (SkipBlock(block)) continue;
                if (!block.IsQueueEmpty)
                {
                    block.Enabled = true;
                    continue;
                }
                if (_collection.TryGetValue(block, out var time))
                {
                    if (time.Second < _productionDelay || block.IsProducing) continue;
                    block.Enabled = false;
                    _collection.Remove(block);
                }
                _collection.Add(block, DateTime.Now);

            }
        }



        //check gas tanks
        private void CheckTanks()
        {
            if (_gasTanks.Count == 0)
            {
                return;
            }
            var lowTanks = 0;
            foreach (var tank in _gasTanks)
            {
                if (!tank.Enabled || SkipBlock(tank))
                    continue;
                if (tank.FilledRatio >= _tankFillLevel)
                {
                    tank.ShowOnHUD = false;
                    continue;
                }
                lowTanks+=1;
                tank.Enabled = true;
                tank.ShowOnHUD = true;
            }

            _tankRefill = lowTanks > 0;
        }

        
        private void GasGenSwitch(bool b)
        {
            if (_gasGens.Count == 0)return;
            foreach (var gen in _gasGens)
            {
                if (SkipBlock(gen) || gen.Enabled == b) continue;
                gen.Enabled = b;
            }
        }


        //close open doors
        private void AutoDoors()
        {

            foreach (var door in _doors)
            {
                if (door is IMyAirtightHangarDoor || SkipBlock(door)) continue;
                if (_collection.TryGetValue(door, out var time))
                {
                    if (!((DateTime.Now - time).TotalSeconds > _doorDelay)) continue;
                    door.CloseDoor();
                    _collection.Remove(door);
                    continue;
                }
                if (door.Status != DoorStatus.Opening) continue;
                _collection.Add(door,DateTime.Now);
            }

        }

        //---------------navigation----------------------

        //set autopilot lookat
        private void LookAt(MyDetectedEntityInfo entity)
        {
            if (_remotes.Count < 1 || _isStatic) return;

            foreach (var remote in _remotes)
                if (!Closed(remote) && remote.CustomData == "Aim" && !CheckPlayer())
                {
                    remote.ClearWaypoints();
                    remote.AddWaypoint(entity.Position, entity.Name);
                    remote.ControlThrusters = false;
                    remote.FlightMode = FlightMode.OneWay;
                    if (!CheckToleranceAngle(remote, entity.Position - remote.GetPosition()))
                    {
                        remote.SetAutoPilotEnabled(true);
                    }
                    else
                    {
                        remote.SetAutoPilotEnabled(false);
                        Echo("aiming at target");
                    }
                }
                else
                {
                    Echo("No remotes found with Aim");
                }
        }

        //check remote aim
        private static bool CheckToleranceAngle(IMyTerminalBlock referenceBlock, Vector3D toTarget)
        {
            var toleranceAngle = MathHelper.ToRadians(5.0);
            var toleranceCosine = Math.Cos(toleranceAngle) * Math.Cos(toleranceAngle);
            var forwardVector = referenceBlock.WorldMatrix.Forward; //this is normalized wich is good for us
            var checkCosine = Vector3D.Dot(forwardVector, toTarget) * Vector3D.Dot(forwardVector, toTarget) /
                              toTarget.LengthSquared();

            return checkCosine > toleranceCosine; //lol fixed
        }


        //check for player operating ship
        private bool CheckPlayer()
        {
            foreach (var cockpit in _cockpits)
            {
                if (SkipBlock(cockpit) || !cockpit.IsUnderControl)
                    continue;
                return cockpit.IsUnderControl;
            }

            return false;
        }

        //---------------combat--------------------------

        //manage turrets
        private void ManageTurrets()
        {
            if (_turrets.Count < 1)
                return;
            var priority = new Random().Next(_aggression + 1, _aggression * 5);
            foreach (var turret in _turrets)
            {
                if (SkipBlock(turret)) continue;

                if (turret.GetInventory().ItemCount < 1)
                {
                    turret.ShowOnHUD = true;
                    continue;
                }

                //check if Turrets custom data already has a number and assign one if it doesnt
                int tryNumber;
                if (string.IsNullOrEmpty(turret.CustomData) || !int.TryParse(turret.CustomData, out tryNumber) || int.Parse(turret.CustomData) > _defaultAggression * 5)
                    turret.CustomData = turret is IMyLargeInteriorTurret
                        ? (_aggression - 1).ToString()
                        : priority.ToString();

                //compare number in custom data to aggression and turn off if higher
                turret.Enabled = int.Parse(turret.CustomData) < _aggression;
                if (StringContains(turret.CustomName,"designator"))
                {
                    if (int.Parse(turret.CustomData) > _aggression - 1) turret.CustomData = (_aggression - 1).ToString();
                    continue;
                }

                if (!turret.Enabled) continue;
                SetTurret(turret);
            }
        }

        //handles Cerebro's aggression level
        private void AggroBuilder()
        {
            _aggression = HasTarget(out _myTarget)
                ? Math.Min(_aggression += 2, _defaultAggression * 5)
                : Math.Max(_aggression -= 1, _defaultAggression);
        }

        //refocus turrets on target in combat
        private void Refocus()
        {
            ResetTurrets();
            if (_myTarget.IsEmpty() || _myTarget.Position == new Vector3D(0, 0, 0)) return;
            foreach (var turret in _turrets)
            {
                if (SkipBlock(turret) || !turret.Enabled) continue;
                turret.SetTarget(_myTarget.Position);
            }
            if (_isStatic || _remotes.Count == 0)return;
            //aim ship at target
            LookAt(_myTarget);
        }

        private void CheckConnectors()
        {
            foreach (var connector in _connectors)
            {
                if (Closed(connector) || connector.Status != MyShipConnectorStatus.Connectable)
                {
                    _collection.Remove(connector);
                    continue;
                }
                if (_collection.TryGetValue(connector, out var time))
                {
                    if (time.Second < _connectDelay) continue;
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
            foreach (var turret in _turrets) turret.ResetTargetingToDefault();
        }

        //returns if any of the turrets are actively targeting something
        private bool HasTarget(out MyDetectedEntityInfo target)
        {
            foreach (var turret in _turrets)
            {
                if (!turret.Enabled || !turret.HasTarget || SkipBlock(turret))
                    continue;
                target = turret.GetTargetedEntity();
                return turret.HasTarget;
            }

            target = new MyDetectedEntityInfo();
            return false;
        }

        //---------------Misc----------------------------

        //assign blocks to lists
        private void GetBlocks()
        {
            //clear lists first
            _productionBlocks.Clear();
            _reactorFuel.Clear();
            _refineries.Clear();
            _assemblers.Clear();
            _gravGens.Clear();
            _batteries.Clear();
            _reactors.Clear();
            _gasTanks.Clear();
            _oxygenTanks.Clear();
            _hydrogenTanks.Clear();
            _airVents.Clear();
            _textPanels.Clear();
            _connectors.Clear();
            _turrets.Clear();
            _lights.Clear();
            _doors.Clear();
            _sensors.Clear();
            _solars.Clear();
            _timers.Clear();
            _cockpits.Clear();
            _remotes.Clear();
            _gasGens.Clear();


            GridTerminalSystem.GetBlocksOfType(_gridBlocks, (x) => x.IsSameConstructAs(Me) 
            && !StringContains(x.CustomData, "ignore") && !StringContains(x.CustomName, "ignore"));
            _myProjector = (IMyProjector) GridTerminalSystem.GetBlockWithName(_reProj);

            if (_lowBatteries.Count > 0)
            {
                foreach (var battery in _lowBatteries)
                {
                    if (Closed(battery)) _lowBatteries.Remove(battery);
                }
            }

            foreach (var block in _gridBlocks)
            {
                var door = block as IMyDoor;
                var gasGen = block as IMyGasGenerator;
                var refinery = block as IMyRefinery;
                var assembler = block as IMyAssembler;
                var battery = block as IMyBatteryBlock;
                var reactor = block as IMyReactor;
                var gravGen = block as IMyGravityGenerator;
                var gasTank = block as IMyGasTank;
                var textPanel = block as IMyTextPanel;
                var connector = block as IMyShipConnector;
                var turret = block as IMyLargeTurretBase;
                var vent = block as IMyAirVent;
                var light = block as IMyLightingBlock;
                var sensor = block as IMySensorBlock;
                var solarPanel = block as IMySolarPanel;
                var timer = block as IMyTimerBlock;
                var cockpit = block as IMyShipController;
                var remote = block as IMyRemoteControl;
                var productionBlock = block as IMyProductionBlock;


                //Assign Blocks
                if (gasGen != null) _gasGens.Add(gasGen);
                if (vent != null) _airVents.Add(vent);
                if (door != null) _doors.Add(door);
                if (refinery != null) _refineries.Add(refinery);
                if (assembler != null) _assemblers.Add(assembler);
                if (battery != null) _batteries.Add(battery);
                if (reactor != null)
                {
                    _reactors.Add(reactor); 
                    if (reactor.GetInventory().ItemCount > 0) _reactorFuel.Add(reactor,reactor.GetInventory(0).GetItemAt(0).Value.Type);
                }

                if (gravGen != null) _gravGens.Add(gravGen);
                if (gasTank != null)
                {
                    _gasTanks.Add(gasTank);
                    if (StringContains(gasTank.BlockDefinition.SubtypeId,"hydrogen"))_hydrogenTanks.Add(gasTank);
                    if (StringContains(gasTank.BlockDefinition.SubtypeId,"oxygen"))_oxygenTanks.Add(gasTank);
                }
                if (textPanel != null) _textPanels.Add(textPanel);
                if (connector != null) _connectors.Add(connector);
                if (turret != null)
                {
                    _turrets.Add(turret);
                    if (StringContains(turret.CustomName,"designator")) _designators.Add(turret);
                }

                if (light != null) _lights.Add(light);
                if (sensor != null) _sensors.Add(sensor);
                if (solarPanel != null) _solars.Add(solarPanel);
                if (timer != null) _timers.Add(timer);
                if (cockpit != null) _cockpits.Add(cockpit);
                if (remote != null) _remotes.Add(remote);
                if (productionBlock != null) _productionBlocks.Add(productionBlock);
            }
        }

        private static bool StringContains(string source, string toCheck, StringComparison comp = StringComparison.OrdinalIgnoreCase)
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
    }
}