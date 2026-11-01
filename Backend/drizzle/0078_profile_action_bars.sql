-- The own action bars as the login sent them ({pages: [{page: 1..3, slots: [{slot, skill, fallbacks: [skill]}]}]}); page n = skill preset n; own character only.
ALTER TABLE `player_profiles` ADD `action_bars_json` text DEFAULT '{}' NOT NULL;
