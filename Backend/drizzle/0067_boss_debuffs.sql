-- The effects on the boss during the fight (debuffs the players put on it): {o: origin of the upload, list: [{skillId, skill, casts, uptimePermille, givers, spans}]}.
ALTER TABLE `encounters` ADD `boss_debuffs_json` text;
