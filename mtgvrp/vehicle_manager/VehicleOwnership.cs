﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using GrandTheftMultiplayer.Server.API;
using GrandTheftMultiplayer.Server.Elements;
using GrandTheftMultiplayer.Server.Managers;
using GrandTheftMultiplayer.Shared;
using GrandTheftMultiplayer.Shared.Math;
using mtgvrp.core;
using mtgvrp.inventory;
using mtgvrp.player_manager;
using mtgvrp.core.Help;

namespace mtgvrp.vehicle_manager
{
    class VehicleOwnership : Script
    {
        public VehicleOwnership()
        {
            API.onClientEventTrigger += API_onClientEventTrigger;
        }

        private void API_onClientEventTrigger(Client sender, string eventName, params object[] arguments)
        {
            Character character = sender.GetCharacter();
            switch (eventName)
            {
                case "myvehicles_locatecar":
                    vehicle_manager.Vehicle lcVeh =
                        VehicleManager.Vehicles.Single(
                            x => x.NetHandle.Value == Convert.ToInt32(arguments[0]) && x.OwnerId == character.Id);
                    Vector3 loc = API.getEntityPosition(lcVeh.NetHandle);
                    API.triggerClientEvent(sender, "myvehicles_setCheckpointToCar", loc.X, loc.Y, loc.Z);
                    API.sendChatMessageToPlayer(sender, "A checkpoint has been set to the vehicle.");
                    break;

                case "myvehicles_abandoncar":
                    vehicle_manager.Vehicle acVeh =
                        VehicleManager.Vehicles.Single(
                            x => x.Id == Convert.ToInt32(arguments[0]) && x.OwnerId == character.Id);
                    VehicleManager.despawn_vehicle(acVeh);
                    VehicleManager.delete_vehicle(acVeh);
                    acVeh.Delete();
                    API.sendChatMessageToPlayer(sender,
                        $"You have sucessfully abandoned your ~r~{VehicleOwnership.returnCorrDisplayName(acVeh.VehModel)}~w~");
                    break;

                case "myvehicles_sellcar":
                    vehicle_manager.Vehicle scVeh =
                        VehicleManager.Vehicles.Single(
                            x => x.Id == Convert.ToInt32(arguments[0]) && x.OwnerId == character.Id);
                    var tid = (string) arguments[1];
                    var target = PlayerManager.ParseClient(tid);
                    if (target == null)
                    {
                        API.sendChatMessageToPlayer(sender, "That player isn't online or doesn't exist.");
                        return;
                    }
                    var targetChar = target.GetCharacter();
                    var targetAccount = target.GetAccount();

                    var price = 0;
                    if (!int.TryParse((string) arguments[2], out price))
                    {
                        API.sendChatMessageToPlayer(sender, "Invalid price entered.");
                        return;
                    }
                    if (price < 0)
                    {
                        API.sendChatMessageToPlayer(sender, "Price cannot be negative.");
                        return;
                    }

                    if (targetChar.OwnedVehicles.Count >= VehicleManager.GetMaxOwnedVehicles(targetChar.Client))
                    {
                        API.sendChatMessageToPlayer(sender, "This player cannot own any more vehicles.");
                        return;
                    }

                    API.sendChatMessageToPlayer(sender,
                        $"Are you sure you would like to sell the ~r~{VehicleOwnership.returnCorrDisplayName(scVeh.VehModel)}~w~ for ~r~${price}~w~ to the player ~r~{targetChar.rp_name()}~w~?");
                    API.sendChatMessageToPlayer(sender, "Use /confirmsellvehicle to sell.");
                    API.setEntityData(sender, "sellcar_selling", new dynamic[] {scVeh, targetChar, price});
                    break;
                case "groupvehicles_locatecar":
                    vehicle_manager.Vehicle gVeh =
                        VehicleManager.Vehicles.Single(
                            x => x.NetHandle.Value == Convert.ToInt32(arguments[0]) && x.GroupId == character.GroupId);
                    Vector3 location = API.getEntityPosition(gVeh.NetHandle);
                    API.triggerClientEvent(sender, "myvehicles_setCheckpointToCar", location.X, location.Y, location.Z);
                    API.sendChatMessageToPlayer(sender, "A checkpoint has been set to the vehicle.");
                    break;
            }
        }

        [Command("myvehicles"), Help(HelpManager.CommandGroups.Vehicles, "Lists the vehicles you own.", null)]
        public void myvehicles_cmd(Client player)
        {
            //Get all owned vehicles and send them.
            Character character = player.GetCharacter();
            if (!character.OwnedVehicles.Any())
            {
                API.sendChatMessageToPlayer(player,"You don't have any vehicles to manage!");
                return;
            }
            string[][] cars = character.OwnedVehicles
                .Select(x => new[]
                    {VehicleOwnership.returnCorrDisplayName(x.VehModel), x.Id.ToString(), x.NetHandle.Value.ToString()})
                .ToArray();

            API.triggerClientEvent(player, "myvehicles_showmenu", API.toJson(cars));
        }

        [Command("confirmsellvehicle"),
         Help(HelpManager.CommandGroups.Vehicles, "To confirm that you want to sell your vehicle.", null)]
        public void confirmsellvehicle_cmd(Client player)
        {
            Character character = player.GetCharacter();
            var data = API.getEntityData(player, "sellcar_selling");
            if (data != null)
            {
                Vehicle veh = data[0];
                Character target = data[1];
                int price = data[2];
                API.setEntityData(target.Client, "sellcar_buying", new dynamic[] {character, veh, price});
                API.setEntityData(player, "sellcar_selling", null);
                API.sendChatMessageToPlayer(target.Client,
                    $"~r~{character.rp_name()}~w~ has offered to sell you a ~r~{VehicleOwnership.returnCorrDisplayName(veh.VehModel)}~w~ for ~r~${price}~w~.");
                API.sendChatMessageToPlayer(target.Client, "Use /confirmbuyvehicle to buy it.");
                API.sendChatMessageToPlayer(player, "Request sent.");
            }
            else
                API.sendChatMessageToPlayer(player, "You aren't selling any car.");
        }

        [Command("confirmbuyvehicle"),
         Help(HelpManager.CommandGroups.Vehicles, "To confirm that you want to buy a vehicle.", null)]
        public void confirmbuyvehicle_cmd(Client player)
        {
            Character character = player.GetCharacter();
            Account account = player.GetAccount();
            var data = API.getEntityData(player, "sellcar_buying");
            if (data != null)
            {
                Character buyingFrom = data[0];
                Vehicle veh = data[1];
                int price = data[2];
                //Make sure near him.
                var buyingPos = buyingFrom.Client.position;
                if (player.position.DistanceTo(buyingPos) <= 5f)
                {
                    //make sure still have slots.
                    if (character.OwnedVehicles.Count < VehicleManager.GetMaxOwnedVehicles(character.Client))
                    {
                        //make sure have money.
                        if (Money.GetCharacterMoney(character) >= price)
                        {
                            //Do actual process.
                            InventoryManager.GiveInventoryItem(buyingFrom, new Money(), price);
                            InventoryManager.DeleteInventoryItem(character, typeof(Money), price);
                            veh.OwnerId = character.Id;
                            veh.OwnerName = character.CharacterName;
                            veh.Save();

                            //DONE, now spawn if hes vip.
                            if (!veh.IsSpawned)
                            {
                                //He won't be able to buy it anyways if he wasn't VIP... so I THINK he can now have it spawned, right ? :/
                                if (VehicleManager.spawn_vehicle(veh) != 1)
                                    API.consoleOutput(
                                        $"There was an error spawning vehicle #{veh.Id} of {character.CharacterName}.");
                            }

                            //Tell.
                            API.sendChatMessageToPlayer(player, "You have sucessfully bought the car.");
                            API.sendChatMessageToPlayer(buyingFrom.Client, "You have successfully sold the car.");
                            API.setEntityData(player, "sellcar_buying", null);
                        }
                        else
                        {
                            API.sendChatMessageToPlayer(player, "You don't have enough money.");
                            API.sendChatMessageToPlayer(buyingFrom.Client, "The buyer doesn't have enough money.");
                            API.setEntityData(player, "sellcar_buying", null);
                        }
                    }
                    else
                    {
                        API.sendChatMessageToPlayer(player, "You can't own anymore vehicles.");
                        API.sendChatMessageToPlayer(buyingFrom.Client, "The buyer can't own anymore vehicles.");
                        API.setEntityData(player, "sellcar_buying", null);
                    }
                }
                else
                {
                    API.sendChatMessageToPlayer(player, "You must be near the buyer.");
                    API.sendChatMessageToPlayer(buyingFrom.Client, "The buyer must be near you.");
                    API.setEntityData(player, "sellcar_buying", null);
                }
            }
            else
                API.sendChatMessageToPlayer(player, "You aren't buying any car.");
        }

        public static string returnCorrDisplayName(VehicleHash hash)
        {
            string disName = API.shared.getVehicleDisplayName(hash);
            if (disName != null)
            {
                return disName;
            }
            switch(hash)
            {
                case VehicleHash.Ratbike:
                    return "Ratbike";
                case VehicleHash.Chimera:
                    return "Chimera";
                case VehicleHash.Zombiea:
                    return "Zombie";
                case VehicleHash.Faggio:
                    return "Faggio Sport";
                case VehicleHash.Avarus:
                    return "Avarus";
                case VehicleHash.Sanctus:
                    return "Santus";
                case VehicleHash.Elegy:
                    return "Elegy";
                case VehicleHash.SultanRS:
                    return "Sultan RS";
                case VehicleHash.Zentorno:
                    return "Zentorno";
                case VehicleHash.Turismo2:
                    return "Turismo";
                case VehicleHash.Italigtb:
                    return "Itali GTB";
                case VehicleHash.Nero:
                    return "Nero";
                default:
                    return "Vehicle";
            }
        }



    }

}
