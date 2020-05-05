﻿using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API.Utils;
using RoR2;
using System;
using UnityEngine;
using UnityEngine.Networking;

namespace ThinkInvisible.ClassicItems {
    public class Prescriptions : ItemBoilerplate<Prescriptions> {
        public override string itemCodeName {get;} = "Prescriptions";

        private ConfigEntry<float> cfgDuration;
        private ConfigEntry<float> cfgASpdBoost;
        private ConfigEntry<float> cfgDmgBoost;
        private ConfigEntry<bool> cfgUseIL;
        public float duration {get;private set;}
        public float aSpdBoost {get;private set;}
        public float dmgBoost {get;private set;}
        public bool useIL {get; private set;}

        private bool ilFailed = false;
        public BuffIndex prescriptionsBuff {get;private set;}

        protected override void SetupConfigInner(ConfigFile cfl) {
            cfgDuration = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "Duration"), 11f, new ConfigDescription(
                "Duration of the buff applied by Prescriptions.",
                new AcceptableValueRange<float>(0f,float.MaxValue)));
            cfgASpdBoost = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "ASpdBoost"), 0.4f, new ConfigDescription(
                "Attack speed added while Prescriptions is active.",
                new AcceptableValueRange<float>(0f,float.MaxValue)));
            cfgDmgBoost = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "DmgBoost"), 10f, new ConfigDescription(
                "Base damage added while Prescriptions is active.",
                new AcceptableValueRange<float>(0f,float.MaxValue)));
            cfgUseIL = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "UseIL"), true, new ConfigDescription(
                "Set to false to change Prescriptions' effect from an IL patch to an event hook, which may help if experiencing compatibility issues with another mod. This will change how Prescriptions interacts with other effects."));

            duration = cfgDuration.Value;
            aSpdBoost = cfgASpdBoost.Value;
            dmgBoost = cfgDmgBoost.Value;
            useIL = cfgUseIL.Value;
        }
        
        protected override void SetupAttributesInner() {
            itemIsEquipment = true;

            modelPathName = "prescriptions_model.prefab";
            iconPathName = "prescriptions_icon.png";
            eqpEnigmable = true;
            eqpCooldown = 45;

            RegLang("Pillaged Gold",
                "Increase damage and attack speed for 8 seconds.",
                ".",
                "A relic of times long past (ClassicItems mod)");
        }

        protected override void SetupBehaviorInner() {
            var prescriptionsBuffDef = new R2API.CustomBuff(new BuffDef {
                buffColor = Color.red,
                canStack = true,
                isDebuff = false,
                name = "Prescriptions",
                iconPath = "@ClassicItems:Assets/ClassicItems/icons/" + iconPathName
            });
            prescriptionsBuff = R2API.BuffAPI.Add(prescriptionsBuffDef);

            On.RoR2.EquipmentSlot.PerformEquipmentAction += On_ESPerformEquipmentAction;
            
            if(useIL) {
                IL.RoR2.CharacterBody.RecalculateStats += IL_CBRecalcStats;
                if(ilFailed) {
                    IL.RoR2.CharacterBody.RecalculateStats -= IL_CBRecalcStats;
                    On.RoR2.CharacterBody.RecalculateStats += On_CBRecalcStats;
                }
            } else
                On.RoR2.CharacterBody.RecalculateStats += On_CBRecalcStats;
        }
        
        private bool On_ESPerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot slot, EquipmentIndex eqpid) {
            if(eqpid == regIndexEqp) {
                var sbdy = slot.characterBody;
                if(!sbdy) return false;
                sbdy.ClearTimedBuffs(prescriptionsBuff);
                sbdy.AddTimedBuff(prescriptionsBuff, duration);
                if(Embryo.instance.CheckProc<Prescriptions>(sbdy)) sbdy.AddTimedBuff(prescriptionsBuff, duration);
                return true;
            } else return orig(slot, eqpid);
        }
        private void IL_CBRecalcStats(ILContext il) {
            var c = new ILCursor(il);

            bool ILFound;

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("baseDamage"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelDamage"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd());
            if(ILFound) {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float,CharacterBody,float>>((origDmg, cb) => {
                    return origDmg + (cb.HasBuff(prescriptionsBuff) ? dmgBoost : 0f);
                });
            } else {
                ilFailed = true;
                Debug.LogError("ClassicItems: failed to apply Prescriptions IL patch (damage modifier), falling back to event hook");
                return;
            }

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("baseAttackSpeed"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<CharacterBody>("levelAttackSpeed"),
                x=>x.MatchLdloc(out _),
                x=>x.MatchMul(),
                x=>x.MatchAdd());
            if(ILFound) {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float,CharacterBody,float>>((origASpd, cb) => {
                    return origASpd * (1f + cb.GetBuffCount(prescriptionsBuff) * aSpdBoost);
                });
            } else {
                ilFailed = true;
                Debug.LogError("ClassicItems: failed to apply Prescriptions IL patch (attack speed modifier), falling back to event hook");
                return;
            }
        }
        private void On_CBRecalcStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self) {
            orig(self);
            
            if(self.GetBuffCount(prescriptionsBuff) == 0) return;
            Reflection.SetPropertyValue(self, "damage", self.damage + dmgBoost);
            Reflection.SetPropertyValue(self, "attackSpeed", self.attackSpeed + aSpdBoost * self.GetBuffCount(prescriptionsBuff));
        }
    }
}