-- The game's permanent character id of a player (it stays when he is renamed), sent by clients from 1.2.1 on; a hint for recognising a renamed character.
ALTER TABLE `players` ADD `game_character_id` integer;
