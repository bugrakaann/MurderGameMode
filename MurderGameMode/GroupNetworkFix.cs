// Reference: 0Harmony
// Requires: EventManager
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using UnityEngine;
using Oxide.Core;
using Network;
using HarmonyLib;
using Network.Visibility;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("GroupNetworkFix","Apwned","1.0.0")]
    [Description("Helps to isolate network groups")]
    internal class GroupNetworkFix : RustPlugin
    {
        Harmony harmony = new Harmony("com.apwned.murdergamemode");
        void Init() => harmony.PatchAll();

        [HarmonyPatch(typeof(Networkable),nameof(Networkable.UpdateGroups))]
        class Patch
        {
            static bool Prefix(Networkable __instance,ref bool __result ,Vector3 position)
            {
                object groupID = Interface.CallHook("OnGroupUpdate",__instance.ID);
                uint ID = Convert.ToUInt32(groupID);
                if(groupID == null) ID = 35716;
                Group group = Net.sv.visibility.Get(ID);
                __result = __instance.SwitchGroup(group);
                return false;
            }
        }
        
        [HarmonyPatch(typeof(NetworkVisibilityGrid))]
        [HarmonyPatch("GetVisibleFrom")]
        class Patch02
        {
            static bool Prefix (NetworkVisibilityGrid __instance, Group group, List<Group> groups,int radius)
            {
                groups.Add(Net.sv.visibility.Get(0U));
                groups.Add(Net.sv.visibility.Get(group.ID));
                return false;
            }
        }
        //Dropped Items will inherit their parent network
        [HarmonyPatch(typeof(DroppedItem), nameof(DroppedItem.ShouldInheritNetworkGroup))]
        class Patch03
        {
            static bool Prefix(ref bool __result)
            {
                __result = true;
                return false;
            }
        }
        //BaseCorpses will inherit their parent network
        [HarmonyPatch(typeof(BaseCorpse), nameof(BaseCorpse.ShouldInheritNetworkGroup))]
        class Patch04
        {
            static bool Prefix(ref bool __result)
            {
                __result = true;
                return false;
            }
        }
        //RelationshipManager UpdateAcquaintancesFor GetCloseConnections Fix (Related to getting group by group location)
        [HarmonyPatch(typeof(RelationshipManager),nameof(RelationshipManager.UpdateAcquaintancesFor))]
        class Patch05
        {
            static bool Prefix(RelationshipManager __instance,BasePlayer player,float deltaSeconds)
            {
                RelationshipManager.PlayerRelationships playerRelationships = __instance.GetRelationships(player.userID);
                List<BasePlayer> list = Facepunch.Pool.GetList<BasePlayer>();
                //Relationship of people are not going to be brought by group positions
                //Instead we will only update relationships for current players group
                //BaseNetworkable.GetCloseConnections(player.transform.position, __instance.GetAcquaintanceMaxDist(), list);
                #region Modified Part
                Group group = Net.sv.visibility.TryGet(player.net.group.ID);
                group.subscribers.ForEach(connection => 
                {
                    if (connection.active)
                    {
                        BasePlayer basePlayer = connection.player as BasePlayer;
                        if(basePlayer != null)
                            list.Add(basePlayer);
                    }
                });
                #endregion
                foreach (BasePlayer basePlayer in list)
                {
                    if (!(basePlayer == player) && !basePlayer.isClient && basePlayer.IsAlive() && !basePlayer.IsSleeping() && !basePlayer.limitNetworking)
                    {
                        global::RelationshipManager.PlayerRelationshipInfo relations = playerRelationships.GetRelations(basePlayer.userID);
                        if (Vector3.Distance(player.transform.position, basePlayer.transform.position) <= __instance.GetAcquaintanceMaxDist())
                        {
                            relations.lastSeenTime = UnityEngine.Time.realtimeSinceStartup;
                            if ((relations.type == RelationshipManager.RelationshipType.NONE || relations.type == RelationshipManager.RelationshipType.Acquaintance) && player.IsPlayerVisibleToUs(basePlayer, 1218519041))
                            {
                                int num = Mathf.CeilToInt(deltaSeconds);
                                if (player.InSafeZone() || basePlayer.InSafeZone())
                                {
                                    num = 0;
                                }
                                if (relations.type != RelationshipManager.RelationshipType.Acquaintance || (relations.weight < 60 && num > 0))
                                {
                                    __instance.SetRelationship(player, basePlayer, RelationshipManager.RelationshipType.Acquaintance, num, false);
                                }
                            }
                        }
                    }
                }
                Facepunch.Pool.FreeList(ref list);
                return false;
            }
        }

        //Isolating ingame voice chats per network group
        [HarmonyPatch(typeof(BasePlayer),nameof(BasePlayer.OnReceivedVoice))]
        class Patch06
        {
            static bool Prefix(BasePlayer __instance,byte[] data)
            {
                if (Interface.CallHook("OnPlayerVoice", __instance, data) != null)
                {
                    return false;
                }
                var write = Net.sv.StartWrite();
                write.PacketID(Message.Type.VoiceData);
                write.UInt32(__instance.net.ID);
                write.BytesWithSize(data);
                write.Send(new SendInfo(__instance.GetSubscribers()) //Only modified this line to send voice chats only to current network group
                {
                    priority = Network.Priority.Immediate
                });
                
                if (__instance.activeTelephone != null)
                {
                    __instance.activeTelephone.OnReceivedVoiceFromUser(data);
                }
                return false;
            }
        }
        
        [HarmonyPatch(typeof(AntiHack), nameof(AntiHack.IsNoClipping))]
        class Patch07
        {
            static bool Prefix(ref bool __result,BasePlayer ply, TickInterpolator ticks, float deltaTime)
            {
                if (ply.HasComponent<EventManager.NetworkGroupData>())
                {
                    __result = false;
                    return false;
                }
                if (EventManager.AdminEditStaticsRoom.IsAdminAlreadyEditingRoom(ply))
                {
                    __result = false;
                    return false; 
                }
                return true;
            }
        }
        //Removes effects from global network and makes it isolated
        [HarmonyPatch(typeof(EffectNetwork),nameof(EffectNetwork.Send),new Type[] {typeof(Effect)})]
        class Patch08
        {
            static bool Prefix(Effect effect)
            {
                if (Net.sv == null)
                {
                    return false;
                }
                if (!Net.sv.IsConnected())
                {
                    return false;
                }
                using (TimeWarning.New("EffectNetwork.Send", 0))
                {
                    if (!string.IsNullOrEmpty(effect.pooledString))
                    {
                        effect.pooledstringid = global::StringPool.Get(effect.pooledString);
                    }
                    if (effect.pooledstringid == 0U)
                    {
                        Debug.Log("String ID is 0 - unknown effect " + effect.pooledString);
                    }
                    
                    Group group;
                    
                    BaseEntity baseEntity = BaseNetworkable.serverEntities.Find(effect.entity) as BaseEntity;
                    if (!baseEntity.IsValid())
                    {
                        return false;
                    }
                    group = baseEntity.net.group;
                    if (group != null)
                    {
                        var write = Net.sv.StartWrite();
                        write.PacketID(Message.Type.Effect);
                        effect.WriteToStream(write);
                        write.Send(new SendInfo(group.subscribers));
                    }
                    
                }
                return false;
            }
        }

        //These fixes are needed to send effects like fall damage effect,c4 effects that are sent globally
        //Main Problem:		Effect.server.Run(string, Vector3, Vector3, Connection, bool) : void @06002661
        #region Global Effect Fixes
        [HarmonyPatch(typeof(BaseMelee),nameof(BaseMelee.ServerUse))]
        class Patch09
        {
            static bool Prefix(BaseMelee __instance)
            {
                if (__instance.isClient)
                {
                    return false;
                }
                if (__instance.HasAttackCooldown())
                {
                    return false;
                }
                global::BasePlayer ownerPlayer = __instance.GetOwnerPlayer();
                if (ownerPlayer == null)
                {
                    return false;
                }
                __instance.StartAttackCooldown(__instance.repeatDelay * 2f);
                ownerPlayer.SignalBroadcast(global::BaseEntity.Signal.Attack, string.Empty, null);
                if (__instance.swingEffect.isValid)
                {
                    var effect = new Effect();
                    effect.Init(Effect.Type.Generic, __instance.transform.position, Vector3.forward);
                    effect.pooledString = __instance.swingEffect.resourcePath;

                    foreach (Connection connection in __instance.net.group.subscribers)
                        EffectNetwork.Send(effect, connection);
                }
                if (__instance.IsInvoking(new Action(__instance.ServerUse_Strike)))
                {
                    __instance.CancelInvoke(new Action(__instance.ServerUse_Strike));
                }
                __instance.Invoke(new Action(__instance.ServerUse_Strike), __instance.aiStrikeDelay);
                return false;
            }
        }

        [HarmonyPatch(typeof(BasePlayer),nameof(BasePlayer.ApplyFallDamageFromVelocity))]
        class Patch10
        {
            static bool Prefix(BasePlayer __instance,float velocity)
            {
                float num = Mathf.InverseLerp(-15f, -100f, velocity);
                if (num == 0f)
                {
                    return false;
                }
                if (Interface.CallHook("OnPlayerLand", __instance, num) != null)
                {
                    return false;
                }
                __instance.metabolism.bleeding.Add(num * 0.5f);
                float num2 = num * 500f;
                __instance.Hurt(num2, Rust.DamageType.Fall, null, true);
                if (num2 > 20f && __instance.fallDamageEffect.isValid)
                {
                    global::Effect.server.Run(__instance.fallDamageEffect.resourcePath, __instance.transform.position, Vector3.zero, null, false);
                    var effect = new Effect();
                    effect.Init(Effect.Type.Generic, __instance.transform.position, Vector3.zero);
                    effect.pooledString = __instance.fallDamageEffect.resourcePath;

                    foreach (Connection connection in __instance.net.group.subscribers)
                        EffectNetwork.Send(effect, connection);
                }
                Interface.CallHook("OnPlayerLanded", __instance, num);
                return false;
            }
        }

        [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.WoundingTick))]
        class Patch11
        {
            static bool Prefix(BasePlayer __instance)
            {
                if (Interface.Call("OnWoundCheck", __instance) != null)
                    return false;
                return true;
            }
        }
        
        #endregion

        #region Isolate Building Ability
        //If you wanna be able to build in rooms have a look at Construction.UpdatePlacement method
        /*
        //Being able to build isolated wall,prefab etc in network group
        [HarmonyPatch(typeof(Construction))]
        [HarmonyPatch("TestPlacingThroughWall")]
        class Patch07
        {
            static bool Prefix(ref bool __result,ref Construction.Placement placement, Transform transform, Construction common, Construction.Target target)
            {
                Vector3 vector = placement.position - target.ray.origin;
                RaycastHit hit;
                bool isHitEntity = Physics.Raycast(target.ray.origin, vector.normalized, out hit, vector.magnitude, 2097152);
                BaseEntity hitEntity = hit.GetEntity();
                if (!isHitEntity)
                {
                    __result = true;
                    return false;
                }
                StabilityEntity stabilityEntity = hit.GetEntity() as StabilityEntity;
                if (stabilityEntity != null && target.entity == stabilityEntity)
                {
                    __result = true;
                    return false;
                }
                if (vector.magnitude - hit.distance < 0.2f)
                {
                    __result = true;
                    return false;
                }
                if (hitEntity != null)
                {
                    if (!hitEntity.net.subscriber.IsSubscribed(target.player.net.group))
                    {
                        __result = true;
                        return false;
                    }
                }
                Construction.lastPlacementError = "object in placement path";
                transform.SetPositionAndRotation(hit.point, placement.rotation);
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Construction),nameof(Construction.UpdatePlacement))]
        class Patch08
        {
            static bool Prefix(Construction __instance,ref bool __result, Transform transform, global::Construction common, ref global::Construction.Target target)
            {
                if (!target.valid)
                {
                    return false;
                }
                if (!common.canBypassBuildingPermission && !target.player.CanBuild())
                {
                    Construction.lastPlacementError = "You don't have permission to build here";
                    return false;
                }
                List<Socket_Base> list = Pool.GetList<global::Socket_Base>();
                common.FindMaleSockets(target, list);
                foreach (Socket_Base socket_Base in list)
                {
                    Construction.Placement placement = null;
                    if (!(target.entity != null) || !(target.socket != null) || !target.entity.IsOccupied(target.socket))
                    {
                        if (placement == null)
                        {
                            placement = socket_Base.DoPlacement(target);
                        }
                        if (placement != null)
                        {
                            if (!socket_Base.CheckSocketMods(placement))
                            {
                                transform.position = placement.position;
                                transform.rotation = placement.rotation;
                            }
                            AccessTools.Method(typeof(Construction), "TestPlacingThroughRock").Invoke(__instance, );
                            else if (!__instance.TestPlacingThroughRock(ref placement, target))
                            {
                                transform.position = placement.position;
                                transform.rotation = placement.rotation;
                                Construction.lastPlacementError = "Placing through rock";
                            }
                            else if (!Construction.TestPlacingThroughWall(ref placement, transform, common, target))
                            {
                                transform.position = placement.position;
                                transform.rotation = placement.rotation;
                                Construction.lastPlacementError = "Placing through wall";
                            }
                            else if (!__instance.TestPlacingCloseToRoad(ref placement, target))
                            {
                                transform.position = placement.position;
                                transform.rotation = placement.rotation;
                                Construction.lastPlacementError = "Placing too close to road";
                            }
                            else if (Vector3.Distance(placement.position, target.player.eyes.position) > common.maxplaceDistance + 1f)
                            {
                                transform.position = placement.position;
                                transform.rotation = placement.rotation;
                                Construction.lastPlacementError = "Too far away";
                            }
                            else
                            {
                                DeployVolume[] volumes = PrefabAttribute.server.FindAll<DeployVolume>(__instance.prefabID);
                                if (DeployVolume.Check(placement.position, placement.rotation, volumes, -1))
                                {
                                    transform.position = placement.position;
                                    transform.rotation = placement.rotation;
                                    Construction.lastPlacementError = "Not enough space";
                                }
                                else if (BuildingProximity.Check(target.player, __instance, placement.position, placement.rotation))
                                {
                                    transform.position = placement.position;
                                    transform.rotation = placement.rotation;
                                    Construction.lastPlacementError = "Too close to another building";
                                }
                                else if (common.isBuildingPrivilege && !target.player.CanPlaceBuildingPrivilege(placement.position, placement.rotation, common.bounds))
                                {
                                    transform.position = placement.position;
                                    transform.rotation = placement.rotation;
                                    Construction.lastPlacementError = "Cannot stack building privileges";
                                }
                                else
                                {
                                    bool flag = target.player.IsBuildingBlocked(placement.position, placement.rotation, common.bounds);
                                    if (common.canBypassBuildingPermission || !flag)
                                    {
                                        target.inBuildingPrivilege = flag;
                                        transform.SetPositionAndRotation(placement.position, placement.rotation);
                                        Pool.FreeList<Socket_Base>(ref list);
                                        return true;
                                    }
                                    transform.position = placement.position;
                                    transform.rotation = placement.rotation;
                                    Construction.lastPlacementError = "You don't have permission to build here";
                                }
                            }
                        }
                    }
                }
                Pool.FreeList<Socket_Base>(ref list);
                return false;
            }
        }
        */
        #endregion
    }
}
