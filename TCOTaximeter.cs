using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Oxide.Core.Plugins;
using System.Threading.Tasks;
using Random = System.Random;
using Oxide.Game.Rust.Cui;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("TCOTaximeter", "TCO", "1.0.0")]
    [Description("Plugin um Taxifahrten abzurechnen")]
    public class TCOTaximeter : RustPlugin
    {
        private Random random = new Random();
        private static TCOTaximeter PLUGIN;

        private Timer refreshTimer;
        private readonly Dictionary<ModularCar, Taximeter> ModularCarTaximeter = new Dictionary<ModularCar, Taximeter>();
        private readonly Dictionary<BasePlayer, TaximeterPlayer> TaximeterPlayerUIs = new Dictionary<BasePlayer, TaximeterPlayer>();

        public class Taximeter
        {
            public int pricePer;
            public int currentPrice;
            public float distance;
            public Vector3 lastPosition;
            public bool occupied;
            public bool enabled;
        }

        public class TaximeterPlayer
        {
            public ModularCar modularCar;
        }
        /* Global Data */

        void Init()
        {
            PLUGIN = this;
        }

        void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                DestroyPlayerUI(player);
            }
        }

        private void DestroyPlayerUI(BasePlayer player){
            CuiHelper.DestroyUi(player, "TaximeterUI");
        }

        void OnServerInitialized(bool initial)
        {
            refreshTimer = timer.Repeat(2, 0, () => RefreshUI());
        }

        private void RefreshUI(){
            foreach (var entry in ModularCarTaximeter)
            {
                ModularCar modularCar = entry.Key;
                Taximeter taximeter = entry.Value;
                if(!modularCar) continue;

                if(!ModularCarTaximeter[modularCar].occupied) continue;

                Vector3 currentPosition = modularCar.ServerPosition;
                ModularCarTaximeter[modularCar].distance += modularCar.Distance2D(ModularCarTaximeter[modularCar].lastPosition);
                ModularCarTaximeter[modularCar].lastPosition = currentPosition;

                if(ModularCarTaximeter[modularCar].distance > 0){
                    ModularCarTaximeter[modularCar].distance -= 100;
                    ModularCarTaximeter[modularCar].currentPrice += ModularCarTaximeter[modularCar].pricePer;
                } 
            }

            foreach (var entry in TaximeterPlayerUIs)
            {
                BasePlayer player = entry.Key;
                TaximeterPlayer taximeterPlayer = entry.Value;
                showTaximeterUI(player, taximeterPlayer.modularCar, taximeterPlayer.modularCar.IsDriver(player));
            }
        }

        [ChatCommand("taximeter")]
        private void addtaximeter(BasePlayer player)
        {
            if (player == null) return;
            if (!player.IsAdmin) return;

            RaycastHit hit;
            if (!UnityEngine.Physics.Raycast(player.eyes.HeadRay(), out hit, 5f))
                return;

            if (!(hit.GetEntity() is ModularCar))
            {
                player.ChatMessage("You're not looking at a vehicle!");
                return;
            }

            ModularCar modularCar = hit.GetEntity() as ModularCar;
            if (modularCar == null) return;

            VehicleModuleTaxi vehicleModuleTaxi = GetFirstTaxiModule(modularCar);
            if(!vehicleModuleTaxi){
                player.ChatMessage("There is no taxi module!");
                return;
            }

            RemoveAllTaxilight(modularCar);
            AttachTaxilight(modularCar);
        }

        private void OnLootEntityEnd(BasePlayer player, ModularCarGarage carLift)
        {
            if (carLift == null) return;

            if (carLift.carOccupant != null)
            {
                ModularCar modularCar = carLift.carOccupant;
                if (modularCar == null) return;

                VehicleModuleTaxi vehicleModuleTaxi = GetFirstTaxiModule(modularCar);
                if(!vehicleModuleTaxi) return;

                RemoveAllTaxilight(modularCar);
                AttachTaxilight(modularCar);
            }
        }

        private void AttachTaxilight(ModularCar modularCar){
            
            VehicleModuleSeating taxiModule = GetFirstCockpitModule(modularCar);
            if(taxiModule != null){
                IOEntity taxiLight = taxiModule.GetComponentInChildren<IOEntity>();
                if (taxiLight == null){
                    BaseEntity theNewEntity = GameManager.server.CreateEntity("assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.deployed.prefab", taxiModule.transform.position);
                    if (!theNewEntity)
                    {
                        return;
                    }

                    theNewEntity.Spawn();
                    theNewEntity.transform.localPosition = new Vector3(0.00f, 1.395f, -0.645f); //0.05f, 1.42f, 0.675f
                    theNewEntity.transform.localEulerAngles = new Vector3(-90f,0,0);
                    theNewEntity.SetParent(taxiModule);
                    //player.ChatMessage(theNewEntity.ShortPrefabName + ": (" + theNewEntity.GetComponents<Component>().Length + ") " + string.Join(", ", theNewEntity.GetComponents<Component>().Select(eachComp => eachComp.GetType().Name)));
                    UnityEngine.Object.DestroyImmediate(theNewEntity.GetComponent<DestroyOnGroundMissing>());
                    UnityEngine.Object.DestroyImmediate(theNewEntity.GetComponent<GroundWatch>());
                    UnityEngine.Object.DestroyImmediate(theNewEntity.GetComponent<BoxCollider>());
                    UnityEngine.Object.DestroyImmediate(theNewEntity.GetComponent<MeshCollider>());
                    theNewEntity.OwnerID = 0;
                    BaseCombatEntity theCombatEntity = theNewEntity as BaseCombatEntity;
                    if (theCombatEntity)
                    {
                        theCombatEntity.pickup.enabled = false;
                    }
                    theNewEntity.EnableSaving(true);
                    theNewEntity.SendNetworkUpdateImmediate();
                    ToogleTaxilight(modularCar, false);
                }
            }  
            return;
        }

        private void RemoveAllTaxilight(ModularCar modularCar){
            var non_engine_parts = modularCar.AttachedModuleEntities.Where(x => x.ShortPrefabName.Contains("cockpit"));
            foreach (var nep in non_engine_parts)
            {
                VehicleModuleSeating taxiModule = nep as VehicleModuleSeating;
                if(taxiModule != null){
                    IOEntity taxiLight = taxiModule.GetComponentInChildren<IOEntity>();
                    if (taxiLight != null){
                        taxiLight.Kill();
                    }
                }
            }
        }

        private VehicleModuleTaxi GetFirstTaxiModule(ModularCar modularCar){
            var non_engine_parts = modularCar.AttachedModuleEntities.Where(x => !x.HasAnEngine);
            foreach (var nep in non_engine_parts)
            {
                VehicleModuleTaxi vehicleModule = nep as VehicleModuleTaxi;
                if(vehicleModule != null){
                    return vehicleModule;
                }
            }
            return null;
        }

        private VehicleModuleSeating GetFirstCockpitModule(ModularCar modularCar){
            var v_m_parts = modularCar.AttachedModuleEntities.Where(x => x.ShortPrefabName.Contains("cockpit"));
            foreach (var nep in v_m_parts)
            {
                VehicleModuleSeating vehicleModule = nep as VehicleModuleSeating;
                if(vehicleModule != null){
                    return vehicleModule;
                }
            }
            return null;
        }

        private void ToogleTaxilight(ModularCar modularCar, bool setOn)
        {
            VehicleModuleSeating taxiModule = GetFirstCockpitModule(modularCar);
            if(!taxiModule) return;
            IOEntity taxiLight = taxiModule.GetComponentInChildren<IOEntity>();
            if(!taxiLight) return;
            taxiLight.UpdateHasPower(setOn ? taxiLight.ConsumptionAmount() : 0, 0);
            taxiLight.SetFlag(BaseEntity.Flags.On, setOn);
        }

        void OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            BaseVehicle vehicle = entity.VehicleParent();
            ModularCar modularCar = vehicle as ModularCar;
            if(modularCar == null) return;

            VehicleModuleTaxi vehicleModule = GetFirstTaxiModule(modularCar);
            if(!vehicleModule) return;

            if (!ModularCarTaximeter.ContainsKey(modularCar))
            {
                Taximeter taximeter = new Taximeter
                { 
                    pricePer = 5, 
                    currentPrice = 0,
                    distance = 0,
                    lastPosition = new Vector3(),
                    occupied = false,
                    enabled = false
                };
                ModularCarTaximeter.Add(modularCar, taximeter);
            }

            if (!TaximeterPlayerUIs.ContainsKey(player))
            {
                TaximeterPlayer taximeterPlayer = new TaximeterPlayer
                { 
                    modularCar = modularCar,
                };
                TaximeterPlayerUIs.Add(player, taximeterPlayer);
            }
            
            showTaximeterUI(player, modularCar, modularCar.IsDriver(player));
        }

        void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            TaximeterPlayerUIs.Remove(player);
            NextTick(() => { DestroyPlayerUI(player); });
        }

        [ConsoleCommand("taximeterCMDUI")]
        private void taximeterCMDUI(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !arg.HasArgs()) return;

            switch (arg.Args[0])
            {
                case "priceMinus":
                {
                    if(ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].pricePer < 1) break;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].pricePer -= 1;
                    break;
                }

                case "pricePlus":
                {
                    if(ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].pricePer > 20) break;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].pricePer += 1;
                    break;
                }

                case "toggle":
                {
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].currentPrice = 0;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].lastPosition = TaximeterPlayerUIs[player].modularCar.ServerPosition;
                    ToogleTaxilight(TaximeterPlayerUIs[player].modularCar, ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied);
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied = !ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied;
                    break;
                }

                case "enable":
                {
                    ToogleTaxilight(TaximeterPlayerUIs[player].modularCar, true);
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].currentPrice = 0;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].enabled = !ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].enabled;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied = false;
                    break;
                }

                case "disable":
                {
                    ToogleTaxilight(TaximeterPlayerUIs[player].modularCar, false);
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].enabled = !ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].enabled;
                    ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied = false;
                    break;
                }
            }

            showTaximeterUI(player, TaximeterPlayerUIs[player].modularCar, true);
        }

        private void showTaximeterUI(BasePlayer player, ModularCar modularCar, bool isDriver)
        {
            var container = new CuiElementContainer();
            string taximeterGuestUI = "TaximeterUI";

            DestroyPlayerUI(player);

            if(ModularCarTaximeter[modularCar].enabled){
                container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.4 0.11", AnchorMax = "0.6 0.19",
                    },
                    Image =
                    {
                        Color = "0.14 0.14 0.16 0.8"
                    }
                }, "Hud", taximeterGuestUI);

                container.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0 0.5", AnchorMax = "1 1",
                    },
                    Text =
                    {
                        Text = "Taximeter",
                        Align = TextAnchor.MiddleCenter,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 14,
                        Color = "1 1 1 1"
                    }
                }, taximeterGuestUI);

                container.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.45",
                    },
                    Text =
                    {
                        Text = $"{ModularCarTaximeter[modularCar].currentPrice} Scrap",
                        Align = TextAnchor.MiddleRight,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 18,
                        Color = "1 0.95 0 1"
                    }
                }, taximeterGuestUI);

                container.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.45",
                    },
                    Text =
                    {
                        Text = $"{ModularCarTaximeter[modularCar].pricePer} Sc/100m",
                        Align = TextAnchor.MiddleLeft,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 18,
                        Color = "1 0 0 1"
                    }
                }, taximeterGuestUI);

                //Price Buttons
                if(isDriver){
                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.02 0.7", AnchorMax = "0.15 0.90"
                        },
                        Text =
                        {
                            Text = "-",
                            Align = TextAnchor.MiddleCenter,
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 10,
                            Color = "1 1 1 1"
                        },
                        Button =
                        {
                            Color = "0.7 0 0 0.7",
                            Command = "taximeterCMDUI priceMinus"
                        }
                    }, taximeterGuestUI);

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.17 0.7", AnchorMax = "0.30 0.90"
                        },
                        Text =
                        {
                            Text = "+",
                            Align = TextAnchor.MiddleCenter,
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 10,
                            Color = "1 1 1 1"
                        },
                        Button =
                        {
                            Color = "0.7 0 0 0.7",
                            Command = "taximeterCMDUI pricePlus"
                        }
                    }, taximeterGuestUI);

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.4 0.4", AnchorMax = "0.6 0.6"
                        },
                        Text =
                        {
                            Text = "AUS",
                            Align = TextAnchor.MiddleCenter,
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 10,
                            Color = "1 1 1 1"
                        },
                        Button =
                        {
                            Color = "0.7 0 0 0.7",
                            Command = "taximeterCMDUI disable"
                        }
                    }, taximeterGuestUI);

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.7 0.7", AnchorMax = "0.98 0.90"
                        },
                        Text =
                        {
                            Text = (ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied ? "BESETZT" : "FREI"),
                            Align = TextAnchor.MiddleCenter,
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 10,
                            Color = "1 1 1 1"
                        },
                        Button =
                        {
                            Color = (ModularCarTaximeter[TaximeterPlayerUIs[player].modularCar].occupied ? "0.7 0 0 0.7" : "0 0.7 0 0.7"),
                            Command = "taximeterCMDUI toggle"
                        }
                    }, taximeterGuestUI);
                }
            } else if(isDriver){
                container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.45 0.11", AnchorMax = "0.55 0.13",
                    },
                    Image =
                    {
                        Color = "0.14 0.14 0.16 0.8"
                    }
                }, "Hud", taximeterGuestUI);

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1"
                    },
                    Text =
                    {
                        Text = "Taximeter einschalten",
                        Align = TextAnchor.MiddleCenter,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 10,
                        Color = "1 1 1 1"
                    },
                    Button =
                    {
                        Color = "0.7 0 0 0.7",
                        Command = "taximeterCMDUI enable"
                    }
                }, taximeterGuestUI);
            }

            CuiHelper.AddUi(player, container);
        }

    }
}