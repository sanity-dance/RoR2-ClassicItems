﻿using RoR2;
using UnityEngine;
using TILER2;
using static TILER2.MiscUtil;


namespace ThinkInvisible.ClassicItems {
    public class SkeletonKey : Equipment_V2<SkeletonKey> {
        public override string displayName => "Skeleton Key";

        [AutoConfigUpdateActions(AutoConfigUpdateActionTypes.InvalidateLanguage)]
        [AutoConfig("Radius around the user to search for chests to open when using Skeleton Key.", AutoConfigFlags.None, 0f, float.MaxValue)]
        public float radius {get;private set;} = 50f;

		public override float cooldown {get;protected set;} = 90f;        
        protected override string GetNameString(string langid = null) => displayName;        
        protected override string GetPickupString(string langid = null) => "Open all nearby chests.";
        protected override string GetDescString(string langid = null) => "Opens all <style=cIsUtility>chests</style> within <style=cIsUtility>" + radius.ToString("N0") + " m</style> for <style=cIsUtility>no cost</style>.";        
        protected override string GetLoreString(string langid = null) => "A relic of times long past (ClassicItems mod)";
        
        public SkeletonKey() { }

        protected override bool PerformEquipmentAction(EquipmentSlot slot) {
            if(!slot.characterBody) return false;
            if(SceneCatalog.mostRecentSceneDef.baseSceneName == "bazaar") return false;
            var sphpos = slot.characterBody.transform.position;
            var sphrad = radius;
                
            if(instance.CheckEmbryoProc(slot.characterBody)) sphrad *= 2;
			Collider[] sphits = Physics.OverlapSphere(sphpos, sphrad, LayerIndex.defaultLayer.mask, QueryTriggerInteraction.Collide);
            bool foundAny = false;
            foreach(Collider c in sphits) {
                var ent = EntityLocator.GetEntity(c.gameObject);
                if(!ent) continue;
				var cptChest = ent.GetComponent<ChestBehavior>();
                if(!cptChest) continue;
                var cptPurch = ent.GetComponent<PurchaseInteraction>();
                if(cptPurch && cptPurch.available && cptPurch.costType == CostTypeIndex.Money) {
                    cptPurch.SetAvailable(false);
                    cptChest.Open();
                    foundAny = true;
                }
            }
            return foundAny;
        }
    }
}