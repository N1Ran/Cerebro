Cerebro by N1Ran

Introduction:
	This script is a grid management script.  It is currently in development but it is design to manage most blocks on the grid.
	Power management allows the script to turn off blocks not in use to conserve power.  Turret management keeps all turrets except
	designators offline till enemy is detected.  Oxygen and hydrogen management is also included in the script.  Script is optimize for 
	server and multiplayer use.

Commands
->Note:  while some of the commands are not case sensitive, it is advice to use lower case when entering commands
*Main

*Power
	->Power On: "power on"
	->Power off: "power off"
	->Recharge: "power recharge"

*Docking
	->Dock: "dock" -- This will switch to dock mode and recharge batteries if "RechargeWhenConnected" is set to true;

*Navigation
	->TakeOff: "takeoff (boolAllowPlayerControl) (angle) (speed)"  ie  takeoff false 100 45
	->Cruise: "cruise (boolAllowPlayerControl) (speed) (height) (direction)"

*Weapon System
	Turrets can indivitually be set to trigger base on Aggression number.  This number is randomly set an script's first run.  You can make changes to individual turret settings via its
	customData.  Available turret duty is as follow: Designator, AntiMissile, and Antipersonnel.  



