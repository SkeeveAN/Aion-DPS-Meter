-- Marks a player whose character is deleted in the game (the row stays: fights refer to it). Null = alive.
ALTER TABLE `players` ADD `deleted_at` text;
