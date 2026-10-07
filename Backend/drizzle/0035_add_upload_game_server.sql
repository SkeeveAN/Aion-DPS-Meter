-- What the client says about the server it plays on: the own character's numeric server id (reused by every region) and the game server's IP:port (tells the regions apart).
ALTER TABLE `uploads` ADD `client_server_id` integer;
--> statement-breakpoint
ALTER TABLE `uploads` ADD `game_server` text;
