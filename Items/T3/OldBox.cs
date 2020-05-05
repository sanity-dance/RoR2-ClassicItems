﻿using RoR2;
using UnityEngine;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine.Networking;
using R2API;
using static ThinkInvisible.ClassicItems.MiscUtil;
using System.Collections.ObjectModel;

namespace ThinkInvisible.ClassicItems {
    public class OldBox : ItemBoilerplate<OldBox> {
        public override string itemCodeName {get;} = "OldBox";

        private ConfigEntry<float> cfgHealthThreshold;
        private ConfigEntry<float> cfgRadius;
        private ConfigEntry<float> cfgDuration;
        private ConfigEntry<bool> cfgRequireHealth;
        
        public float healthThreshold {get; private set;}
        public float radius {get; private set;}
        public float duration {get; private set;}
        public bool requireHealth {get; private set;}

        protected override void SetupConfigInner(ConfigFile cfl) {
            cfgHealthThreshold = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "HealthThreshold"), 0.5f, new ConfigDescription(
                "Fraction of max health required as damage taken to trigger Old Box (halved per additional stack).",
                new AcceptableValueRange<float>(0f, 1f)));
            cfgRadius = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "Radius"), 25f, new ConfigDescription(
                "AoE radius for Old Box.",
                new AcceptableValueRange<float>(0f, float.MaxValue)));
            cfgDuration = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "Duration"), 2f, new ConfigDescription(
                "Duration of fear debuff applied by Old Box.",
                new AcceptableValueRange<float>(0f, float.MaxValue)));
            cfgRequireHealth = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "RequireHealth"), true, new ConfigDescription(
                "If true, damage to shield and barrier (from e.g. Personal Shield Generator, Topaz Brooch) will not count towards triggering Old Box."));

            healthThreshold = cfgHealthThreshold.Value;
            radius = cfgRadius.Value;
            duration = cfgDuration.Value;
            requireHealth = cfgRequireHealth.Value;
        }
        
        protected override void SetupAttributesInner() {
            modelPathName = "oldbox_model.prefab";
            iconPathName = "oldbox_icon.png";
            RegLang("Old Box",
            	"Chance to fear enemies when attacked.",
            	"<style=cDeath>When hit for more than " + pct(healthThreshold,1,1f) + " max health</style> <style=cStack>(/2 per stack)</style>, <style=cIsUtility>fear enemies</style> within <style=cIsUtility>" + radius.ToString("N0") + " m</style> for <style=cIsUtility>" + duration.ToString("N1") + " seconds</style>. <style=cIsUtility>Feared enemies will run out of melee</style>, <style=cDeath>but that won't stop them from shooting you.</style>",
            	"A relic of times long past (ClassicItems mod)");
            _itemTags = new List<ItemTag>{ItemTag.Utility};
            itemTier = ItemTier.Lunar;
        }

        protected override void SetupBehaviorInner() {
			On.RoR2.HealthComponent.TakeDamage += On_HCTakeDamage;
        }

        private void On_HCTakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo di) {
            var oldHealth = self.health;
            var oldCH = self.combinedHealth;

			orig(self, di);

			int icnt = GetCount(self.body);
            float adjThreshold = healthThreshold * Mathf.Pow(2, 1-icnt);
			if(icnt < 1
                || (requireHealth && (oldHealth - self.health)/self.fullHealth < adjThreshold)
                || (!requireHealth && (oldCH - self.combinedHealth)/self.fullCombinedHealth < adjThreshold))
                return;

            /*Vector3 corePos = Util.GetCorePosition(self.body);
			var thisThingsGonnaX = GlobalEventManager.instance.explodeOnDeathPrefab;
			var x = thisThingsGonnaX.GetComponent<DelayBlast>();
			EffectManager.SpawnEffect(x.explosionEffect, new EffectData {
				origin = corePos,
				rotation = Quaternion.identity,
                color = Color.blue,
				scale = radius
			}, true);*/

            var tind = TeamIndex.Monster | TeamIndex.Neutral | TeamIndex.Player;
			tind &= ~self.body.teamComponent.teamIndex;
			ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(tind);
			float sqrad = radius * radius;
			foreach(TeamComponent tcpt in teamMembers) {
				if ((tcpt.transform.position - self.body.corePosition).sqrMagnitude <= sqrad) {
					if (tcpt.body && tcpt.body.mainHurtBox && tcpt.body.isActiveAndEnabled) {
                        tcpt.body.AddTimedBuff(ClassicItemsPlugin.fearBuff, duration);
					}
				}
			}
        }
	}
}
