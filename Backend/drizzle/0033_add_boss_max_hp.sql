-- Highest hit-point reading of the boss as the client saw it (null = not reported). Tells explore from conquest by HP.
ALTER TABLE `encounters` ADD `boss_max_hp` integer;
