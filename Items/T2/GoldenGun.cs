﻿using RoR2;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System;
using System.Collections.ObjectModel;
using TILER2;
using static TILER2.MiscUtil;

namespace ThinkInvisible.ClassicItems {
    public class GoldenGun : Item_V2<GoldenGun> {
        public override string displayName => "Golden Gun";
		public override ItemTier itemTier => ItemTier.Tier2;
		public override ReadOnlyCollection<ItemTag> itemTags => new ReadOnlyCollection<ItemTag>(new[]{ItemTag.Damage});

        [AutoConfigUpdateActions(AutoConfigUpdateActionTypes.InvalidateLanguage)]
        [AutoConfig("Maximum multiplier to add to player damage.", AutoConfigFlags.PreventNetMismatch, 0f, float.MaxValue)]
        public float damageBoost {get;private set;} = 0.4f;

        [AutoConfigUpdateActions(AutoConfigUpdateActionTypes.InvalidateLanguage)]
        [AutoConfig("Gold required for maximum damage. Scales with difficulty level.", AutoConfigFlags.PreventNetMismatch, 0, int.MaxValue)]
        public int goldAmt {get;private set;} = 700;
        
        [AutoConfigUpdateActions(AutoConfigUpdateActionTypes.InvalidateLanguage)]
        [AutoConfig("Inverse-exponential multiplier for reduced GoldAmt per stack (higher = more powerful).", AutoConfigFlags.PreventNetMismatch, 0f, 0.999f)]
        public float goldReduc {get;private set;} = 0.5f;

        [AutoConfig("If true, deployables (e.g. Engineer turrets) with Golden Gun will benefit from their master's money.",
            AutoConfigFlags.PreventNetMismatch)]
        public bool inclDeploys {get;private set;} = true;

        private bool ilFailed = false;
        
        public BuffIndex goldenGunBuff {get;private set;}
        protected override string GetNameString(string langid = null) => displayName;
        protected override string GetPickupString(string langid = null) => "More gold, more damage.";
        protected override string GetDescString(string langid = null) => "Deal <style=cIsDamage>bonus damage</style> based on your <style=cIsUtility>money</style>, up to <style=cIsDamage>" + Pct(damageBoost) + "</style> at <style=cIsUtility>$" + goldAmt.ToString("N0") + "</style> <style=cStack>(cost increases with difficulty, -" + Pct(goldReduc) + " per stack)</style>.";
        protected override string GetLoreString(string langid = null) => "A relic of times long past (ClassicItems mod)";

        public override void SetupAttributes() {
            base.SetupAttributes();

            var goldenGunBuffDef = new R2API.CustomBuff(new BuffDef {
                buffColor = new Color(0.85f, 0.8f, 0.3f),
                canStack = true,
                isDebuff = false,
                name = $"{modInfo.shortIdentifier}GoldenGun",
                iconPath = "@ClassicItems:Assets/ClassicItems/icons/GoldenGun_icon.png"
            });
            goldenGunBuff = R2API.BuffAPI.Add(goldenGunBuffDef);
        }

        public override void SetupBehavior() {
            base.SetupBehavior();
            if(Compat_ItemStats.enabled) {
                Compat_ItemStats.CreateItemStatDef(itemDef,
                    ((count, inv, master) => {
                        return Run.instance.GetDifficultyScaledCost(goldAmt) * Mathf.Pow(goldReduc, count - 1);
                    },
                    (value, inv, master) => { return $"Full Damage Cost: ${value.ToString("N0")}"; }
                ));
            }
        }

        public override void Install() {
            base.Install();
            IL.RoR2.HealthComponent.TakeDamage += IL_CBTakeDamage;
            if(ilFailed) IL.RoR2.HealthComponent.TakeDamage -= IL_CBTakeDamage;
            else {
                On.RoR2.CharacterBody.FixedUpdate += On_CBFixedUpdate;
                On.RoR2.CharacterBody.OnInventoryChanged += On_CBInventoryChanged;
            }
        }

        public override void Uninstall() {
            base.Uninstall();
            IL.RoR2.HealthComponent.TakeDamage -= IL_CBTakeDamage;
            On.RoR2.CharacterBody.FixedUpdate -= On_CBFixedUpdate;
            On.RoR2.CharacterBody.OnInventoryChanged -= On_CBInventoryChanged;
        }

        private void On_CBInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self) {
            orig(self);
            var cpt = self.GetComponent<GoldenGunComponent>();
            if(!cpt) cpt = self.gameObject.AddComponent<GoldenGunComponent>();
            var newIcnt = GetCount(self);
            if(cpt.cachedIcnt != newIcnt) {
                cpt.cachedIcnt = newIcnt;
                UpdateGGBuff(self);
            }
        }

        private void On_CBFixedUpdate(On.RoR2.CharacterBody.orig_FixedUpdate orig, CharacterBody self) {
            orig(self);
            if(!self) return;
            var cpt = self.GetComponent<GoldenGunComponent>();
            if(!cpt) return;
            var newMoney = self.master?.money ?? 0;
            if(inclDeploys) {
                var dplc = self.GetComponent<Deployable>();
                if(dplc) newMoney += dplc.ownerMaster?.money ?? 0;
            }
            if(cpt.cachedMoney != newMoney || cpt.cachedDiff != Run.instance.difficultyCoefficient) {
                cpt.cachedMoney = newMoney;
                cpt.cachedDiff = Run.instance.difficultyCoefficient;
                UpdateGGBuff(self);
            }
        }

        void UpdateGGBuff(CharacterBody cb) {
            var cpt = cb.GetComponent<GoldenGunComponent>();
            int tgtBuffStacks = (cpt.cachedIcnt<1) ? 0 : Mathf.Clamp(Mathf.FloorToInt(cpt.cachedMoney / (Run.instance.GetDifficultyScaledCost(goldAmt) * Mathf.Pow(goldReduc, cpt.cachedIcnt - 1)) * 100f), 0, 100);
                
            int currBuffStacks = cb.GetBuffCount(goldenGunBuff);
            if(tgtBuffStacks != currBuffStacks)
                cb.SetBuffCount(goldenGunBuff, tgtBuffStacks);
        }

        private void IL_CBTakeDamage(ILContext il) {
            var c = new ILCursor(il);

            bool ILFound;

            int locDmg = -1;
            ILFound = c.TryGotoNext(
                x=>x.MatchLdarg(1),
                x=>x.MatchLdfld<DamageInfo>("damage"),
                x=>x.MatchStloc(out locDmg));
            
            if(!ILFound) {
                ilFailed = true;
                ClassicItemsPlugin._logger.LogError("Failed to apply Golden Gun IL patch (damage var read), item will not work; target instructions not found");
                return;
            }

            int locChrm = -1;
            ILFound = c.TryGotoNext(
                x=>x.MatchLdloc(out locChrm),
                x=>x.MatchCallOrCallvirt<CharacterMaster>("get_inventory"),
                x=>x.MatchLdcI4((int)ItemIndex.Crowbar))
            && c.TryGotoPrev(MoveType.After,
                x=>x.OpCode == OpCodes.Brfalse);

            if(ILFound) {
                c.Emit(OpCodes.Ldloc, locChrm);
                c.Emit(OpCodes.Ldloc, locDmg);
                c.EmitDelegate<Func<CharacterMaster,float,float>>((chrm, origdmg) => {
                    var icnt = GetCount(chrm.inventory);
                    if(icnt == 0) return origdmg;
                    var moneyFac = chrm.money;
                    if(inclDeploys) {
                        var dplc = chrm.GetComponent<Deployable>();
                        if(dplc) moneyFac += dplc.ownerMaster.money;
                    }
                    var moneyCoef = moneyFac / (Run.instance.GetDifficultyScaledCost(goldAmt) * Mathf.Pow(goldReduc, icnt - 1));
                    return origdmg * (1 + Mathf.Lerp(0,damageBoost,moneyCoef));
                });
                c.Emit(OpCodes.Stloc, locDmg);
            } else {
                ilFailed = true;
                ClassicItemsPlugin._logger.LogError("Failed to apply Golden Gun IL patch (damage var write), item will not work; target instructions not found");
                return;
            }
        }
    }

    public class GoldenGunComponent : MonoBehaviour {
        public uint cachedMoney = 0u;
        public int cachedIcnt = 0;
        public float cachedDiff = 0f;
    }
}
