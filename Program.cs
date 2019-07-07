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
using VRageMath;

namespace IngameScript
{
    internal sealed class Program : MyGridProgram
    {
        public enum Pilot
        {
            Disabled,
            Cruise,
            Land,
            Takeoff
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            ScriptInitiate();
        }


        private void Main(string argument)
        {
            if (!_scriptInitialize) ScriptInitiate();
            if (!_hasAntenna && _hive) GrabNewAntenna();
            if (_remoteControl == null && !_isStatic && _autoNavigate) GrabNewRemote();
            CurrentState(argument, out _currentMode);
            if (_currentMode != Switch.Start)
            {
                Echo("Script Paused!");
                status.Clear();
                status.Append("AI Paused ");
                return;
            }

            CountTicks();
            if (GridFlags() || _combatFlag)
                _alert = _combatFlag ? AlertState.Combat : AlertState.Error;
            else
                _alert = AlertState.Clear;
            status.Clear();
            status.Append("AI Running ");
            //debug running
            WriteToScreens(_sb);
            WriteToScreens(_sbPower, "power");
            DisplayData(_menuPointer);
            _script = (float) Runtime.LastRunTimeMs;
            Echo($"Script RunTime: {_script:F2} ms");
            SystemsCheck(_counter);
            //Runtime.UpdateFrequency = _master ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
        }

        private void SystemsCheck(int i)
        {
            var opt2 = _isStatic ? UpdateFrequency.Update10 : UpdateFrequency.Update1;
            Runtime.UpdateFrequency = _alert == AlertState.Clear && _autoPilot == Pilot.Disabled
                ? UpdateFrequency.Update10
                : opt2;
            _turretControl = _turrets.Count > 0;
            _isStatic = Me.CubeGrid.IsStatic;
            _hive = _isStatic && _hasAntenna;
            _hasProjector = _myProjector != null && !SkipBlock(_myProjector);
            _hasAntenna = _myAntenna != null && !SkipBlock(_myAntenna);
            AutoDoors();
            if (_autoNavigate && _remoteControl != null)
            {
                _inGravity = _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);
                _currentSpeed = _remoteControl.GetShipSpeed();
                CheckNavigation();
            }

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
                            if (time.Second < ProjectorShutoffDelay || _damageDetected) break;
                            _myProjector.Enabled = false;
                            _collection.Remove(_myProjector);
                        }

                        if (!_damageDetected && !_myProjector.Enabled) break;
                        _myProjector.Enabled = true;
                        _collection.Add(_myProjector, DateTime.Now);
                    }

                    break;
                case 2:
                    if (_powerManagement) BatteryCheck();
                    break;
                case 3:
                    if (!_turretControl) break;
                    _combatFlag = _aggression > DefaultAggression;
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
                        if (_combatFlag) AttackTarget();
                        else
                            FollowMe();
                    }

                    break;
                case 6:
                    if (!_controlProduction) break;
                    CheckProduction();
                    break;
                case 7:
                    ParseIni();
                    break;
                case 8:
                    CheckConnectors();
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

        private void GrabNewRemote()
        {
            if (_remoteControl != null || _remotes.Count == 0) return;
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
            if (antennas.Count == 0)
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
            _aggression = DefaultAggression;
            ParseIni();
            GetBlocks();
        }

        private void CurrentState(string st, out Switch result)
        {
            var t = st.Split(' ');
            while (true)
            {
                if (StringContains(t[0], "pause"))
                {
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    result = Switch.Pause;
                    return;
                }

                if (StringContains(t[0], "start")) Runtime.UpdateFrequency = UpdateFrequency.Update10;

                if (StringContains(t[0], "takeoff"))
                {
                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);

                    if (_autoNavigate && _remoteControl != null) _autoPilot = Pilot.Takeoff;
                }

                if (StringContains(t[0], "cancel"))
                    switch (t[1].ToLower())
                    {
                        case "land":
                            OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                            _autoPilot = Pilot.Disabled;
                            break;
                        case "takeoff":
                            OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                            _autoPilot = Pilot.Disabled;
                            break;
                        case "cruise":
                            OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                            _cruiseHeight = 0;
                            _cruiseSpeed = 0;
                            _autoPilot = Pilot.Disabled;
                            break;
                        case "trip":
                            _autoPilot = Pilot.Disabled;
                            foreach (var remote in _remotes)
                            {
                                if (Closed(remote)) continue;
                                remote.SetAutoPilotEnabled(false);
                            }

                            foreach (var controller in _cockpits)
                            {
                                if (Closed(controller)) continue;
                                controller.DampenersOverride = true;
                            }

                            OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                            break;
                        default:
                            OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                            break;
                    }

                if (StringContains(t[0], "land"))
                {
                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    _autoPilot = _inGravity ? Pilot.Land : Pilot.Disabled;
                }

                if (StringContains(t[0], "cruise"))
                {
                    OverrideThrust(false, Vector3D.Zero, 0, 0, 0);
                    Vector3D x;
                    switch (t[2].ToLower())
                    {
                        case "up":
                            x = _remoteControl.WorldMatrix.Up;
                            break;
                        case "down":
                            x = _remoteControl.WorldMatrix.Down;
                            break;
                        case "forward":
                            x = _remoteControl.WorldMatrix.Forward;
                            break;
                        case "backward":
                            x = _remoteControl.WorldMatrix.Backward;
                            break;
                        default:
                            x = _remoteControl.WorldMatrix.Forward;
                            break;
                    }

                    _autoPilot = Pilot.Cruise;
                    _cruiseDirection = x;
                    _cruiseHeight = _inGravity ? double.Parse(t[2]) : 0;
                    _cruiseSpeed = double.Parse(t[3]);
                }

                if (StringContains(t[0], "cyclemenu+"))
                    if (_menuPointer < 3)
                        _menuPointer += 1;
                if (StringContains(t[0], "cyclemenu-"))
                    if (_menuPointer > 0)
                        _menuPointer -= 1;
                break;
            }

            result = Switch.Start;
        }


        private static void SetTurret(IMyTerminalBlock turret)
        {
            if (!(turret is IMyLargeTurretBase)) return;
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

            if (turret is IMyLargeInteriorTurret && !StringContains(turret.CustomName, "designator"))
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
            return _productionFlag || _powerFlag || _navFlag || _damageDetected;
        }

        //count ticks
        private void CountTicks()
        {
            _counter = _counter < 12 ? _counter += 1 : 0;
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
        private void WriteToScreens(StringBuilder sb, string lcd = null)
        {
            foreach (var textPanel in _textPanels)
            {
                if (SkipBlock(textPanel) || !StringContains(textPanel.CustomName, "cerebro")) continue;
                if (string.IsNullOrEmpty(lcd))
                {
                    textPanel.WriteText(sb);
                    continue;
                }

                if (!StringContains(textPanel.CustomName, lcd)) continue;
                textPanel.WriteText(sb);
            }

            //Me.GetSurface(0).WriteText(sb);
        }

        //Alerts the player should see
        private void Alerts()
        {
            _sb.Clear();
            _sbPower.Clear();
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

            _sbPower.Append($"PowerFlag = {_powerFlag}");
            _sbPower.AppendLine();


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
                        _sbPower.Append(
                            $"  {battery.CustomName} Recharging - {Math.Round(battery.CurrentStoredPower / battery.MaxStoredPower * 100)}%");
                        _sbPower.AppendLine();
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

            _alertCounter = _alertCounter < 10 ? _alertCounter += 1 : 0;


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
                if (Closed(light) || !StringContains(light.CustomName, "alert"))
                {
                    light.Enabled = !_combatFlag;
                    continue;
                }

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
                if (Closed(reactor)) continue;
                if (!_reactorFuel.TryGetValue(reactor.BlockDefinition.SubtypeId, out var fuel))
                {
                    reactor.UseConveyorSystem = true;
                    continue;
                }

                reactor.UseConveyorSystem = false;
                foreach (var block in _gridBlocks)
                {
                    if (Closed(block) || !block.HasInventory || block is IMyReactor) continue;

                    var y = reactor.GetInventory(0).GetItemAmount(fuel) < lowCap
                        ? lowCap - reactor.GetInventory(0).GetItemAmount(fuel)
                        : reactor.GetInventory(0).GetItemAmount(fuel) - lowCap;
                    if (reactor.GetInventory(0).GetItemAmount(fuel) < lowCap)
                    {
                        var z = block.GetInventory().FindItem(fuel);
                        if (z == null || !block.GetInventory().CanTransferItemTo(reactor.GetInventory(0), fuel))
                            continue;
                        block.GetInventory().TransferItemTo(reactor.GetInventory(0), z.Value, y);
                    }
                    else if (reactor.GetInventory(0).GetItemAmount(fuel) > lowCap)
                    {
                        var z = reactor.GetInventory().GetItemAt(0);
                        if (z != null && !block.GetInventory().IsFull &&
                            reactor.GetInventory().CanTransferItemTo(block.GetInventory(), fuel))
                            reactor.GetInventory(0).TransferItemTo(block.GetInventory(), z.Value, y);
                    }
                }
            }
        }


        //handle batteries
        private void BatteryCheck()
        {
            double batteryPower = 0;
            double batteryMax = 0;
            foreach (var panel in _solars) panel.Enabled = true;
            foreach (var battery in _batteries)
            {
                if (SkipBlock(battery)) continue;
                battery.Enabled = true;
                if (_alert == AlertState.Combat)
                {
                    battery.ChargeMode = ChargeMode.Auto;
                    continue;
                }

                if (StringContains(battery.CustomName, "backup") && !_lowBatteries.Contains(battery))
                {
                    if (battery.CurrentStoredPower / battery.MaxStoredPower < 0.5f)
                        _lowBatteries.Add(battery);
                    else
                        battery.ChargeMode = ChargeMode.Auto;
                    continue;
                }

                batteryMax += battery.MaxOutput;
                batteryPower += battery.CurrentOutput;
                if (!_lowBatteries.Contains(battery) &&
                    (battery.CurrentStoredPower / battery.MaxStoredPower < _rechargePoint ||
                     ShipConnected() && _rechargeWhenConnected &&
                     battery.CurrentStoredPower / battery.MaxStoredPower <= 0.99))
                {
                    _lowBatteries.Add(battery);
                    battery.ChargeMode = ChargeMode.Recharge;
                    continue;
                }

                if (_lowBatteries.Contains(battery))
                {
                    if (battery.CurrentStoredPower / battery.MaxStoredPower < 1f)
                        battery.ChargeMode = ChargeMode.Recharge;

                    else
                        _lowBatteries.Remove(battery);
                    continue;
                }

                battery.ChargeMode = _isStatic ? ChargeMode.Discharge : ChargeMode.Auto;
            }

            var batteriesInRecharge = _lowBatteries.Count;
            var totalBatteries = _batteries.Count;
            var batteryUse = batteryPower / batteryMax;
            _powerFlag = (float) batteriesInRecharge / totalBatteries >= 0.50f || batteryUse >= _overload;
            foreach (var reactor in _reactors)
            {
                if (SkipBlock(reactor)) continue;
                reactor.Enabled = _powerFlag;
            }
        }

        private bool ShipConnected()
        {
            foreach (var connector in _connectors)
                if (connector.Status == MyShipConnectorStatus.Connected)
                    return connector.Status == MyShipConnectorStatus.Connected;

            return false;
        }


        //---------------industrial----------------------

        //check airvents
        private void CheckVents()
        {
            if (_airVents.Count == 0) return;
            var ventNeedAir = 0;
            foreach (var vent in _airVents)
            {
                if (SkipBlock(vent) || StringContains(vent.CustomData, "outside")) continue;
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
                    ventNeedAir += 1;
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
            return oxygenState < 0.50f;
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
                damageCount += 1;
            }

            result = damageCount > 0;
        }

        private void SetWeldersOnline(bool b)
        {
            if (_shipWelders.Count == 0 || !_handleRepair) return;
            foreach (var welder in _shipWelders)
            {
                if (StringContains(welder.CustomData, "ignore")) continue;
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
            if (_gasTanks.Count == 0) return;
            var lowTanks = 0;
            foreach (var tank in _gasTanks)
            {
                if (!tank.Enabled || SkipBlock(tank))
                    continue;
                if (_rechargeWhenConnected && tank.FilledRatio < 1f && ShipConnected())
                {
                    tank.Stockpile = true;
                    lowTanks += 1;
                    continue;
                }

                tank.Stockpile = false;
                if (tank.FilledRatio >= _tankFillLevel) continue;

                lowTanks += 1;
                tank.Enabled = true;
            }

            _tankRefill = lowTanks > 0;
        }


        private void GasGenSwitch(bool b)
        {
            if (_gasGens.Count == 0) return;
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

                if (door.Status != DoorStatus.Opening && door.Status != DoorStatus.Open) continue;
                _collection.Add(door, DateTime.Now);
            }
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
                if (string.IsNullOrEmpty(turret.CustomData) || !int.TryParse(turret.CustomData, out _) ||
                    int.Parse(turret.CustomData) > DefaultAggression * 5)
                    turret.CustomData = turret is IMyLargeInteriorTurret
                        ? (_aggression - 1).ToString()
                        : priority.ToString();

                //compare number in custom data to aggression and turn off if higher
                turret.Enabled = int.Parse(turret.CustomData) < _aggression;
                if (StringContains(turret.CustomName, "designator"))
                {
                    if (int.Parse(turret.CustomData) > DefaultAggression - 1)
                        turret.CustomData = (DefaultAggression - 1).ToString();
                    continue;
                }

                if (StringContains(turret.CustomName, "antimissile"))
                {
                    if (int.Parse(turret.CustomData) > DefaultAggression + 1)
                        turret.CustomData = (DefaultAggression + 1).ToString();
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
                ? Math.Min(_aggression += 2, DefaultAggression * 5)
                : Math.Max(_aggression -= 1, DefaultAggression);
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
                    if (time.Second < ConnectDelay) continue;
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
            _gyros.Clear();
            _productionBlocks.Clear();
            if (_reactorFuel.Keys.Count > 10) _reactorFuel.Clear();
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
            _shipWelders.Clear();

            GridTerminalSystem.GetBlockGroupWithName(_welderGroup)?.GetBlocksOfType(_shipWelders);
            GridTerminalSystem.GetBlocksOfType(_gridBlocks, x => x.IsSameConstructAs(Me)
                                                                 && !StringContains(x.CustomData, "ignore") &&
                                                                 !StringContains(x.CustomName, "ignore"));
            _myProjector = (IMyProjector) GridTerminalSystem.GetBlockWithName(_reProj);

            if (_lowBatteries.Count > 0)
                foreach (var battery in _lowBatteries)
                    if (Closed(battery))
                        _lowBatteries.Remove(battery);

            foreach (var block in _gridBlocks)
                switch (block)
                {
                    //Assign Blocks
                    case IMyGyro gyro:
                        _gyros.Add(gyro);
                        break;
                    case IMyGasGenerator gasGen:
                        _gasGens.Add(gasGen);
                        break;
                    case IMyAirVent vent:
                        _airVents.Add(vent);
                        break;
                    case IMyDoor door:
                        _doors.Add(door);
                        break;
                    case IMyRefinery refinery:
                        _refineries.Add(refinery);
                        break;
                    case IMyAssembler assembler:
                        _assemblers.Add(assembler);
                        break;
                    case IMyBatteryBlock battery:
                        _batteries.Add(battery);
                        break;
                    case IMyReactor reactor:
                    {
                        _reactors.Add(reactor);
                        if (reactor.GetInventory().ItemCount > 0 &&
                            !_reactorFuel.ContainsKey(reactor.BlockDefinition.SubtypeId))
                            _reactorFuel.Add(reactor.BlockDefinition.SubtypeId,
                                reactor.GetInventory(0).GetItemAt(0).Value.Type);
                        break;
                    }

                    case IMyGravityGenerator gravGen:
                        _gravGens.Add(gravGen);
                        break;
                    case IMyGasTank gasTank:
                    {
                        _gasTanks.Add(gasTank);
                        if (StringContains(gasTank.BlockDefinition.SubtypeId, "hydrogen")) _hydrogenTanks.Add(gasTank);
                        if (StringContains(gasTank.BlockDefinition.SubtypeId, "oxygen")) _oxygenTanks.Add(gasTank);
                        break;
                    }

                    case IMyTextPanel textPanel:
                        _textPanels.Add(textPanel);
                        break;
                    case IMyShipConnector connector:
                        _connectors.Add(connector);
                        break;
                    case IMyLargeTurretBase turret:
                    {
                        _turrets.Add(turret);
                        if (StringContains(turret.CustomName, "designator")) _designators.Add(turret);
                        break;
                    }

                    case IMyLightingBlock light:
                        _lights.Add(light);
                        break;
                    case IMySensorBlock sensor:
                        _sensors.Add(sensor);
                        break;
                    case IMySolarPanel solarPanel:
                        _solars.Add(solarPanel);
                        break;
                    case IMyTimerBlock timer:
                        _timers.Add(timer);
                        break;
                    case IMyShipController cockpit:
                    {
                        _cockpits.Add(cockpit);
                        if (cockpit is IMyRemoteControl remote) _remotes.Add(remote);
                        break;
                    }

                    case IMyProductionBlock productionBlock:
                        _productionBlocks.Add(productionBlock);
                        break;
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

        private void SetRotationTowardsCoordinates(bool enableRotation, IMyTerminalBlock referenceBlock,
            Vector3D targetCoords, List<IMyGyro> gyroRotateList, double minRotation = 0.1, double rotationAccuracy = 1,
            double rotationStrength = 1)
        {
            var gyroRotation = new Vector3D(0, 0, 0);
            double totalAxisDifference = 0;
            var refMatrix = referenceBlock.WorldMatrix;
            var pitchDirections = new Dictionary<Vector3D, Vector3D>();
            var yawDirections = new Dictionary<Vector3D, Vector3D>();

            if (enableRotation)
            {
                var maxRotation = 3.14;

                if (referenceBlock.CubeGrid.GridSizeEnum == MyCubeSize.Small) maxRotation *= 2;

                var forwardDir =
                    Vector3D.Normalize(targetCoords - referenceBlock.GetPosition()); //Direction To The Target
                var targetCheck = forwardDir * 100 + Vector3D.Zero;
                var upDistCheck = Vector3D.Distance(targetCheck, referenceBlock.WorldMatrix.Up * 100 + Vector3D.Zero);
                var downDistCheck =
                    Vector3D.Distance(targetCheck, referenceBlock.WorldMatrix.Down * 100 + Vector3D.Zero);
                var leftDistCheck =
                    Vector3D.Distance(targetCheck, referenceBlock.WorldMatrix.Left * 100 + Vector3D.Zero);
                var rightDistCheck =
                    Vector3D.Distance(targetCheck, referenceBlock.WorldMatrix.Right * 100 + Vector3D.Zero);

                double pitchPowerModifier = 1;
                double pitchAxisDifference = 0;
                double yawPowerModifier = 1;
                double yawAxisDifference = 0;

                //Pitch
                if (upDistCheck < downDistCheck)
                {
                    gyroRotation.X = -1 * maxRotation;
                    pitchAxisDifference = downDistCheck - upDistCheck;
                }
                else
                {
                    gyroRotation.X = maxRotation;
                    pitchAxisDifference = upDistCheck - downDistCheck;
                }

                pitchPowerModifier = pitchAxisDifference / 200;

                if (pitchPowerModifier < minRotation) pitchPowerModifier = minRotation;

                //Yaw
                if (leftDistCheck < rightDistCheck)
                {
                    gyroRotation.Y = -1 * maxRotation;
                    yawAxisDifference = rightDistCheck - leftDistCheck;
                }
                else
                {
                    gyroRotation.Y = maxRotation;
                    yawAxisDifference = leftDistCheck - rightDistCheck;
                }

                yawPowerModifier = yawAxisDifference / 200;

                if (yawPowerModifier < minRotation) yawPowerModifier = minRotation;

                //Apply Rotation To Gyros

                if (pitchAxisDifference > rotationAccuracy)
                {
                    gyroRotation.X *= pitchPowerModifier;
                    gyroRotation.X *= rotationStrength;
                }
                else
                {
                    gyroRotation.X = 0;
                }

                if (yawAxisDifference > rotationAccuracy)
                {
                    gyroRotation.Y *= yawPowerModifier;
                    gyroRotation.Y *= rotationStrength;
                }
                else
                {
                    gyroRotation.Y = 0;
                }

                totalAxisDifference = yawAxisDifference + pitchAxisDifference;
                pitchDirections.Add(refMatrix.Forward, refMatrix.Up);
                pitchDirections.Add(refMatrix.Up, refMatrix.Backward);
                pitchDirections.Add(refMatrix.Backward, refMatrix.Down);
                pitchDirections.Add(refMatrix.Down, refMatrix.Forward);

                yawDirections.Add(refMatrix.Forward, refMatrix.Right);
                yawDirections.Add(refMatrix.Right, refMatrix.Backward);
                yawDirections.Add(refMatrix.Backward, refMatrix.Left);
                yawDirections.Add(refMatrix.Left, refMatrix.Forward);
            }

            var pitchDirectionsList = pitchDirections.Keys.ToList();
            var yawDirectionsList = yawDirections.Keys.ToList();

            foreach (var gyro in gyroRotateList)
            {
                if (gyro == null) continue;

                if (gyro.IsWorking == false || gyro.IsFunctional == false ||
                    gyro.CubeGrid != referenceBlock.CubeGrid) continue;

                if (enableRotation == false)
                {
                    gyro.GyroOverride = false;
                    continue;
                }

                if (totalAxisDifference < rotationAccuracy)
                {
                    gyro.Yaw = 0;
                    gyro.Pitch = 0;
                    gyro.Roll = 0;
                    continue;
                }

                var gyroMatrix = gyro.WorldMatrix;
                double[] localRotation = {0, 0, 0};
                var pitchIndex = 0;
                var yawIndex = 0;

                var localPitchDirections = new Dictionary<Vector3D, Vector3D>();
                var localYawDirections = new Dictionary<Vector3D, Vector3D>();
                var localRollDirections = new Dictionary<Vector3D, Vector3D>();

                var gyroPitchDirections = new Dictionary<Vector3D, Vector3D>();
                var gyroYawDirections = new Dictionary<Vector3D, Vector3D>();

                localPitchDirections.Add(gyroMatrix.Forward, gyroMatrix.Up);
                localPitchDirections.Add(gyroMatrix.Up, gyroMatrix.Backward);
                localPitchDirections.Add(gyroMatrix.Backward, gyroMatrix.Down);
                localPitchDirections.Add(gyroMatrix.Down, gyroMatrix.Forward);

                localYawDirections.Add(gyroMatrix.Forward, gyroMatrix.Right);
                localYawDirections.Add(gyroMatrix.Right, gyroMatrix.Backward);
                localYawDirections.Add(gyroMatrix.Backward, gyroMatrix.Left);
                localYawDirections.Add(gyroMatrix.Left, gyroMatrix.Forward);

                localRollDirections.Add(gyroMatrix.Up, gyroMatrix.Right);
                localRollDirections.Add(gyroMatrix.Right, gyroMatrix.Down);
                localRollDirections.Add(gyroMatrix.Down, gyroMatrix.Left);
                localRollDirections.Add(gyroMatrix.Left, gyroMatrix.Up);

                //Get Pitch Axis
                var checkPitchPitch = pitchDirectionsList.Except(localPitchDirections.Keys.ToList()).ToList();
                if (checkPitchPitch.Count == 0)
                {
                    pitchIndex = 0;
                    gyroPitchDirections = localPitchDirections;
                }

                var checkPitchYaw = pitchDirectionsList.Except(localYawDirections.Keys.ToList()).ToList();
                if (checkPitchYaw.Count == 0)
                {
                    pitchIndex = 1;
                    gyroPitchDirections = localYawDirections;
                }

                var checkPitchRoll = pitchDirectionsList.Except(localRollDirections.Keys.ToList()).ToList();
                if (checkPitchRoll.Count == 0)
                {
                    pitchIndex = 2;
                    gyroPitchDirections = localRollDirections;
                }

                //Get Yaw Axis
                var checkYawPitch = yawDirectionsList.Except(localPitchDirections.Keys.ToList()).ToList();
                if (checkYawPitch.Count == 0)
                {
                    yawIndex = 0;
                    gyroYawDirections = localPitchDirections;
                }

                var checkYawYaw = yawDirectionsList.Except(localYawDirections.Keys.ToList()).ToList();
                if (checkYawYaw.Count == 0)
                {
                    yawIndex = 1;
                    gyroYawDirections = localYawDirections;
                }

                var checkYawRoll = yawDirectionsList.Except(localRollDirections.Keys.ToList()).ToList();
                if (checkYawRoll.Count == 0)
                {
                    yawIndex = 2;
                    gyroYawDirections = localRollDirections;
                }

                //Assign Pitch
                if (pitchDirections[refMatrix.Forward] == gyroPitchDirections[refMatrix.Forward])
                    localRotation[pitchIndex] = gyroRotation.X;
                else
                    localRotation[pitchIndex] = gyroRotation.X * -1;

                if (pitchIndex == 1 || pitchIndex == 2) localRotation[pitchIndex] *= -1;

                //Assign Yaw
                if (yawDirections[refMatrix.Forward] == gyroYawDirections[refMatrix.Forward])
                    localRotation[yawIndex] = gyroRotation.Y;
                else
                    localRotation[yawIndex] = gyroRotation.Y * -1;

                if (yawIndex == 0) localRotation[yawIndex] *= -1;

                //Apply To Gyros
                gyro.Pitch = (float) localRotation[0];
                gyro.Yaw = (float) localRotation[1];
                gyro.Roll = (float) localRotation[2];
                gyro.GyroOverride = true;
                break;
            }
        }


        //check block for ownership rites
        private bool IsOwned(IMyCubeBlock block)
        {
            return block.GetUserRelationToOwner(Me.OwnerId) == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                   block.OwnerId == Me.OwnerId;
        }

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
            switch (_autoPilot)
            {
                case Pilot.Disabled:
                    break;
                case Pilot.Cruise:
                    LevelShip(false);
                    Cruise(_cruiseDirection, _cruiseHeight, _cruiseSpeed);
                    if (_counter == 10) OverrideThrust(false, _remoteControl.WorldMatrix.Down, 1, _currentSpeed);

                    return;
                case Pilot.Land:
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    LevelShip(false);
                    Land();
                    if (!(_currentHeight < 20) || !(_currentSpeed < 1)) return;
                    OverrideThrust(false, Vector3D.Zero, 0, 0);
                    _autoPilot = Pilot.Disabled;
                    return;
                case Pilot.Takeoff:
                    LevelShip(false);
                    TakeOff();
                    return;
                default:
                    return;
            }

            if (!_combatFlag)
                LevelShip();
            if (_combatFlag && !CheckPlayer() && !_myTarget.IsEmpty() && !ShipConnected() && !_inGravity)
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

        private void Cruise(Vector3D dir, double height, double speed = 100)
        {
            _remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out _currentHeight);
            _thrust = _remoteControl.GetShipVelocities().LinearVelocity.Y < speed
                ? Math.Min(_thrust += 0.1f, 1f)
                : Math.Max(_thrust -= 0.1f, 0);
            var x = Math.Max(height - 500, 2500);
            if (_inGravity && _currentHeight > height + 1000)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.01f, _currentSpeed, 50);
                return;
            }

            if (_inGravity && _currentHeight < x)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Up, 1, _currentSpeed, 25);
                return;
            }

            OverrideThrust(true, dir, 1, _currentSpeed, speed + 10);
        }

        private void TakeOff()
        {
            var x = _remoteControl.GetShipVelocities().LinearVelocity.Y +
                    _remoteControl.GetShipVelocities().LinearVelocity.Z;
            if (_currentHeight <= _landingBrakeHeight) _thrust = 1f;
            else
                _thrust = x < 80 && _currentSpeed < 90
                    ? Math.Min(_thrust += 0.15f, 1)
                    : Math.Max(_thrust -= 0.05f, 0);
            OverrideThrust(_inGravity && x < 100, _remoteControl.WorldMatrix.Up, _thrust, _currentSpeed);
            _autoPilot = _inGravity ? Pilot.Takeoff : Pilot.Disabled;
        }

        private void Land()
        {
            foreach (var remote in _remotes)
            {
                if (Closed(remote)) continue;
                remote.SetAutoPilotEnabled(false);
            }

            if (_currentHeight > _landingBrakeHeight)
            {
                OverrideThrust(true, _remoteControl.WorldMatrix.Down, 0.001f, _currentSpeed);
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
            var thrustList = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrustList);

            foreach (var thruster in thrustList)
            {
                if (thruster == null || !thruster.IsFunctional) continue;

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
        }

        private void LevelShip(bool checkPlayer = true)
        {
            if (checkPlayer && CheckPlayer())
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
            foreach (var gyro in _gyros)
            {
                if (gyro.WorldMatrix.Forward != _remoteControl.WorldMatrix.Forward &&
                    gyro.WorldMatrix.Right != _remoteControl.WorldMatrix.Right)
                {
                    _autoNavigate = false;
                    continue;
                }

                var roll = (float) VectorMath.AngleBetween(flattenedUpVector, Vector3D.Up) *
                           Math.Sign(Vector3D.Dot(Vector3D.Right, flattenedUpVector));
                var pitch = (float) VectorMath.AngleBetween(forward, _remoteControl.WorldMatrix.Forward) *
                            Math.Sign(Vector3D.Dot(up, _remoteControl.WorldMatrix.Forward));
                _pitchDelay = _inGravity && (Math.Abs(pitch) >= 0.1f || Math.Abs(roll) >= 0.1f)
                    ? Math.Min(_pitchDelay += 5, 100)
                    : Math.Max(_pitchDelay -= 1, 0);
                if (_pitchDelay >= 1)
                {
                    gyro.Enabled = true;
                    gyro.GyroOverride = true;
                    gyro.Yaw = 0f;
                    gyro.Pitch = 0f;
                    gyro.Roll = 0f;

                    if (pitch > 0.05f) gyro.Pitch = 0.1f;

                    if (pitch < -0.05f) gyro.Pitch = -0.1f;
                    if (Math.Abs(roll) > 1.8f || roll > 0.1f) gyro.Roll = 0.05f;

                    if (roll < 0.05f) gyro.Roll = -0.05f;
                }
                else
                {
                    gyro.GyroOverride = false;
                    gyro.Pitch = 0f;
                    gyro.Roll = 0f;
                    gyro.Yaw = 0f;
                }
            }
        }

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

        #endregion

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
        private readonly List<IMyTextPanel> _textPanels = new List<IMyTextPanel>();
        private readonly List<IMyTimerBlock> _timers = new List<IMyTimerBlock>();
        private readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        private readonly List<IMyRemoteControl> _remotes = new List<IMyRemoteControl>();
        private readonly List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        private readonly List<IMyShipController> _cockpits = new List<IMyShipController>();
        private readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private readonly List<IMyBatteryBlock> _lowBatteries = new List<IMyBatteryBlock>();
        private readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();
        private readonly List<IMyGyro> _gyros = new List<IMyGyro>();

        //dictionary
        private readonly Dictionary<IMyCubeBlock, DateTime> _collection = new Dictionary<IMyCubeBlock, DateTime>();
        private readonly Dictionary<string, MyItemType> _reactorFuel = new Dictionary<string, MyItemType>();

        #endregion

        #region Fields

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly StringBuilder _sbPower = new StringBuilder();
        private readonly StringBuilder _info = new StringBuilder();
        private readonly StringBuilder status = new StringBuilder();


        //enum
        private Switch _currentMode = Switch.Start;

        private AlertState _alert = AlertState.Clear;

        //Boolean
        private bool _combatFlag;
        private bool _hasAntenna;
        private bool _hasProjector;
        private bool _hive = true;
        private readonly bool _productionFlag = false;
        private bool _isStatic;
        private bool _needAir;
        private bool _powerFlag;
        private bool _scriptInitialize;
        private bool _tankRefill;
        private bool _turretControl = true;
        private bool _controlVents = true;
        private bool _controlGasSystem = true;
        private bool _controlProduction = true;
        private readonly bool _navFlag = false;
        private bool _capReactors = true;
        private bool _powerManagement;
        private bool _master = true;
        private bool _handleRepair = true;
        private bool _damageDetected;
        private bool _autoNavigate;
        private bool _rechargeWhenConnected;
        private bool _inGravity;


        private Vector3D _cruiseDirection;
        private double _cruiseSpeed;
        private double _cruiseHeight;
        private Pilot _autoPilot = Pilot.Disabled;


        //Floats, Int, double
        private const int ConnectDelay = 5;
        private const int DefaultAggression = 10;
        private int _doorDelay = 5;
        private readonly int _productionDelay = 30;
        private int _aggression;
        private int _alertCounter;
        private int _counter;
        private int _menuPointer;
        private const int ProjectorShutoffDelay = 30;
        private int _pitchDelay;
        private float _thrust;
        private double _currentHeight;
        private double _currentSpeed;

        private static int _lowFuel = 50;
        private float _script;

        private static double _tankFillLevel = 0.75;
        private static double _landingBrakeHeight = 2500;
        private static float _rechargePoint = .15f;
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
        private const string INI_CONTROLS_NAVIGATE = "Enable Navigation";

        //Delays
        private const string INI_SECTION_DELAYS = "Cerebro Settings - Delays and Floats";
        private const string INI_DELAYS_DOOR = "Door Delay";
        private const string INI_DELAYS_TANK = "Tank Refill Level";
        private const string INI_DELAYS_LANDINGHEIGHT = "Landing Braking Height";


        //Reactor
        private const string INI_SECTION_POWER = "Cerebro Settings - Power";
        private const string INI_POWER_ENABLECAP = "Enable Reactor Fuel Cap";
        private const string INI_POWER_CAPLEVEL = "Reactor Fuel Fill Level";

        //Battery
        private const string INI_POWER_RECHARGEPOINT = "Battery Recharge Point";
        private const string INI_POWER_OVERLOAD = "Power Overload";
        private const string INI_POWER_RECHARGEONCONNECT = "Recharge When Connected";


        private void ParseIni()
        {
            _ini.Clear();
            _ini.TryParse(Me.CustomData);

            _iniSections.Clear();
            _ini.GetSections(_iniSections);

            if (_iniSections.Count == 0)
            {
                _customDataSb.Clear();
                _customDataSb.Append(Me.CustomData);
                _customDataSb.Replace("---\n", "");

                _ini.EndContent = _customDataSb.ToString();
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
            _autoNavigate = _ini.Get(INI_SECTION_CONTROLS, INI_CONTROLS_NAVIGATE).ToBoolean(_autoNavigate);

            _doorDelay = _ini.Get(INI_SECTION_DELAYS, INI_DELAYS_DOOR).ToInt32(_doorDelay);
            _tankFillLevel = _ini.Get(INI_SECTION_DELAYS, INI_DELAYS_TANK).ToDouble(_tankFillLevel);
            _landingBrakeHeight = _ini.Get(INI_SECTION_DELAYS, INI_DELAYS_LANDINGHEIGHT).ToDouble(_landingBrakeHeight);

            _lowFuel = _ini.Get(INI_SECTION_POWER, INI_POWER_CAPLEVEL).ToInt32(_lowFuel);
            _rechargePoint = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT).ToSingle(_rechargePoint);
            _rechargeWhenConnected = _ini.Get(INI_SECTION_POWER, INI_POWER_RECHARGEONCONNECT)
                .ToBoolean(_rechargeWhenConnected);
            _overload = _ini.Get(INI_SECTION_POWER, INI_POWER_OVERLOAD).ToSingle(_overload);
            _capReactors = _ini.Get(INI_SECTION_POWER, INI_POWER_ENABLECAP).ToBoolean(_capReactors);


            WriteIni();
        }

        private void WriteIni()
        {
            //General Settings
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_MASTER, _master);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_HIVE, _hive);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_PROJECTOR, _reProj);
            _ini.Set(INI_SECTION_GENERAL, INI_GENERAL_WELDERS, _welderGroup);

            //Control Settings
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_REPAIR, _handleRepair);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_GAS, _controlGasSystem);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_POWER, _powerManagement);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_PRODUCTION, _controlProduction);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_TURRETS, _turretControl);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_VENTS, _controlVents);
            _ini.Set(INI_SECTION_CONTROLS, INI_CONTROLS_NAVIGATE, _autoNavigate);

            //Delays
            _ini.Set(INI_SECTION_DELAYS, INI_DELAYS_DOOR, _doorDelay);
            _ini.Set(INI_SECTION_DELAYS, INI_DELAYS_TANK, _tankFillLevel);
            _ini.Set(INI_SECTION_DELAYS, INI_DELAYS_LANDINGHEIGHT, _landingBrakeHeight);

            //Power
            _ini.Set(INI_SECTION_POWER, INI_POWER_ENABLECAP, _capReactors);
            _ini.Set(INI_SECTION_POWER, INI_POWER_CAPLEVEL, _lowFuel);
            _ini.Set(INI_SECTION_POWER, INI_POWER_RECHARGEONCONNECT, _rechargeWhenConnected);
            _ini.Set(INI_SECTION_POWER, INI_POWER_RECHARGEPOINT, _rechargePoint);
            _ini.Set(INI_SECTION_POWER, INI_POWER_OVERLOAD, _overload);

            var output = _ini.ToString();
            if (!string.Equals(output, Me.CustomData))
                Me.CustomData = output;
        }

        #endregion
    }
}