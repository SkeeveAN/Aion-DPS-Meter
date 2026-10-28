-- Patch 2026-10-28: boss HP of Transcendence stages 1 and 2 (Deus Research Base 2320/2340, Shattered Arkanis 2330/2350) reduced by 6.5 %. The reference only learns upward, so it is set here; rows already at the new value are left alone.
UPDATE `boss_hp_reference` SET `base_hp` = 10347645, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2320601 AND `base_hp` > 10554598;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 5478165, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2320615 AND `base_hp` > 5587728;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 5478165, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2320616 AND `base_hp` > 5587728;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 5478165, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2330707 AND `base_hp` > 5587728;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 5478165, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2330708 AND `base_hp` > 5587728;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 10347645, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2330709 AND `base_hp` > 10554598;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 12795475, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2340601 AND `base_hp` > 13051384;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 6774075, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2340615 AND `base_hp` > 6909556;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 6773589, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2340616 AND `base_hp` > 6909061;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 6774075, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2350707 AND `base_hp` > 6909556;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 6774075, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2350708 AND `base_hp` > 6909556;
--> statement-breakpoint
UPDATE `boss_hp_reference` SET `base_hp` = 12795475, `source` = 'patch', `samples` = 0 WHERE `npc_id` = 2350709 AND `base_hp` > 13051384;
